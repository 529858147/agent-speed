import Foundation
import os

/// Statistics of the startup backlog read.
public struct SeedSummary: Equatable, Sendable {
    public var filesDiscovered = 0
    public var filesRead = 0
    public var bytesRead: Int64 = 0
    public var durationMs: Double = 0
    public var partialHistory = false

    public init() {}
}

/// Every transcript file of the enabled roots, the OpenCode database, their parsers, the shared dedupe set and the
/// engine.
///
/// Not thread-safe: one owner (a serial queue) drives it. Only seeding fans out, and it joins before returning.
final class TranscriptSession {
    /// Size rules of METRICS 4 and 5; tests shrink them.
    struct ReadingLimits: Sendable {
        var largeFileBytes = FileTailer.largeFileBytes
        var tailWindowBytes = FileTailer.tailWindowBytes
        var maxLineBytes = FileTailer.defaultMaxLineBytes

        static let standard = ReadingLimits()
    }

    struct Root: Sendable {
        var family: TranscriptFamily
        var path: String
        var canonicalPath: String
        var enabled: Bool
        var available: Bool
    }

    static let startupLookbackMs: Int64 = 15 * 60_000
    static let hotWindowMs: Int64 = 120_000
    static let maxDecodeWorkers = 8
    /// Deduplication owner of every OpenCode row; transcript files count up from 0.
    static let opencodeFileID: FileID = -1

    let enabledSources: Set<SourceKind>
    let limits: ReadingLimits
    private(set) var roots: [Root]
    private var files: [String: TrackedFile] = [:]
    /// Incremental directory walks, by root path.
    private var indexes: [String: DirectoryIndex] = [:]
    private var deduper = Deduper()
    private var engine = SpeedEngine()
    private var parentMembers: [String: AgentParents.Member] = [:]
    private var parentLinks: [String: String] = [:]
    private var parentLinksAreStale = false
    private var nextFileID: FileID = 0
    private var pendingEvents: [SpeedEvent] = []
    private var unreadablePaths: Set<String> = []
    private var removedMalformedLines = 0
    private(set) var partialHistory = false
    /// The OpenCode database, when that source is enabled (METRICS 4.5, 5.2).
    let opencode: OpencodeSource?
    private let opencodeRoot: String
    private var opencodeNeedsSeed = false
    private var opencodeLinks: [String: String] = [:]

    /// Wall-clock milliseconds, for the age rule of completion events (METRICS 6.4); tests pin it.
    private let clock: @Sendable () -> Int64

    init(
        configuration: MonitorConfiguration,
        limits: ReadingLimits = .standard,
        clock: @escaping @Sendable () -> Int64 = { Clock.milliseconds(Date()) }
    ) {
        enabledSources = configuration.enabledSources
        self.limits = limits
        self.clock = clock
        roots = TranscriptFamily.allCases.flatMap { family in
            configuration.resolvedRoots(for: family).map { path in
                Root(family: family, path: path, canonicalPath: path, enabled: configuration.reads(family), available: false)
            }
        }
        opencodeRoot = configuration.resolvedOpencodeRoot
        opencode = configuration.readsOpencode ? OpencodeSource(root: opencodeRoot, fileID: Self.opencodeFileID) : nil
        refreshAvailability()
    }

    // MARK: - Roots

    var hasAvailableRoot: Bool { roots.contains { $0.enabled && $0.available } || opencode?.isReady == true }

    var watchedPaths: [String] {
        var paths = roots.filter { $0.enabled && $0.available }.map(\.canonicalPath)
        if let directory = opencode?.watchedDirectory, opencode?.isReady == true, !paths.contains(directory) {
            paths.append(directory)
        }
        return paths
    }

    /// Root path and availability of the OpenCode source (the folder that contains `opencode.db`).
    var opencodeLocation: (path: String, available: Bool, status: OpencodeSource.Status?) {
        (opencodeRoot, opencode?.exists ?? false, opencode?.status)
    }

    /// Re-checks which roots exist. Returns true when availability changed.
    @discardableResult
    func refreshAvailability() -> Bool {
        var changed = false
        if let opencode, opencode.refreshAvailability() {
            changed = true
            opencodeNeedsSeed = true
        }
        for index in roots.indices {
            let available = TranscriptDiscovery.isAvailable(root: roots[index].path)
            if available != roots[index].available { changed = true }
            roots[index].available = available
            if available, let resolved = realpath(roots[index].path, nil) {
                roots[index].canonicalPath = String(cString: resolved)
                free(resolved)
            }
        }
        return changed
    }

    /// The family's first available root, else its first root (Gemini CLI has two).
    func root(for family: TranscriptFamily) -> Root {
        roots.first { $0.family == family && $0.available } ?? roots.first { $0.family == family }!
    }

    // MARK: - Seeding

    /// Reads the startup backlog (METRICS 5). Responses are seeded: they never produce live events.
    func seed(
        todayStartMs: Int64,
        progress: @Sendable (Double) -> Void = { _ in },
        isCancelled: @Sendable () -> Bool = { false }
    ) -> SeedSummary {
        let started = DispatchTime.now().uptimeNanoseconds
        var summary = SeedSummary()
        refreshAvailability()

        var backlog: [(file: DiscoveredFile, tracked: TrackedFile)] = []
        let threshold = todayStartMs - Self.startupLookbackMs
        for root in roots where root.enabled && root.available {
            for file in TranscriptDiscovery.mostRecent(TranscriptDiscovery.files(at: scan(root))) where files[file.path] == nil {
                summary.filesDiscovered += 1
                var readsBacklog = file.stat.modifiedMs >= threshold
                let start: FileTailer.Start
                if !readsBacklog {
                    start = .end(file.stat.size)
                } else if TranscriptReader.isDocument(file.path), file.stat.size > DocumentReader.defaultMaxBytes {
                    // Whole-document sessions this large are not read at all (METRICS 4.6).
                    partialHistory = true
                    start = .end(file.stat.size)
                    readsBacklog = false
                } else if file.stat.size > limits.largeFileBytes, !TranscriptReader.isCompressedStream(file.path) {
                    // Compressed sessions are always read whole (METRICS 5.3).
                    partialHistory = true
                    if TranscriptReader.isFrameStream(file.path) {
                        // A frame stream cannot be entered in the middle, so it is not seeded at all (METRICS 5.1).
                        start = .end(file.stat.size)
                        readsBacklog = false
                    } else {
                        start = .tail(limits.tailWindowBytes)
                    }
                } else {
                    start = .beginning
                }
                let tracked = track(file, family: root.family, start: start)
                if readsBacklog { backlog.append((file, tracked)) }
            }
        }
        backlog.sort { TranscriptDiscovery.startupOrder($0.file, $1.file) }
        let ordered = backlog.map(\.tracked)

        let jobs = ordered.map { DecodeJob(family: $0.family, reader: $0.reader, size: $0.lastStat.size) }
        let decoded = Self.decodeInParallel(jobs, progress: progress, isCancelled: isCancelled)
        for (file, result) in zip(ordered, decoded) {
            guard let result else { continue }
            file.reader = result.reader
            summary.filesRead += 1
            summary.bytesRead += Int64(result.read.bytesRead)
            var upserts: [ResponseUpsert] = []
            file.parser.consume(result.lines, deduper: &deduper) { upserts.append($0) }
            let project = projectName(of: file)
            for upsert in upserts { deliver(upsert, project: project, live: false) }
            finishRead(of: file, result: result.read)
        }
        if let opencode, opencode.exists {
            summary.filesDiscovered += 1
            if opencode.isReady {
                summary.filesRead += 1
                summary.bytesRead += opencode.databaseSize
            }
            seedOpencode(todayStartMs: todayStartMs)
        }
        summary.partialHistory = partialHistory
        summary.durationMs = Double(DispatchTime.now().uptimeNanoseconds - started) / 1_000_000
        return summary
    }

    private struct DecodeJob: Sendable {
        var family: TranscriptFamily
        var reader: TranscriptReader
        var size: Int64
    }

    private struct DecodedFile: Sendable {
        var reader: TranscriptReader
        var lines: DecodedLines
        var read: FileTailer.ReadResult
    }

    private struct DecodeProgress: Sendable {
        var next = 0
        var bytes: Int64 = 0
        var results: [DecodedFile?]
    }

    /// Reads and decodes files concurrently; each job is handled by exactly one worker.
    private static func decodeInParallel(
        _ jobs: [DecodeJob],
        progress: @Sendable (Double) -> Void,
        isCancelled: @Sendable () -> Bool
    ) -> [DecodedFile?] {
        guard !jobs.isEmpty else { return [] }
        let schedule = jobs.indices.sorted { jobs[$0].size > jobs[$1].size }
        let totalBytes = max(1, jobs.reduce(Int64(0)) { $0 + $1.size })
        let state = OSAllocatedUnfairLock(initialState: DecodeProgress(results: Array(repeating: nil, count: jobs.count)))
        let workers = min(jobs.count, max(1, min(ProcessInfo.processInfo.activeProcessorCount, maxDecodeWorkers)))

        DispatchQueue.concurrentPerform(iterations: workers) { _ in
            while !isCancelled() {
                let slot = state.withLock { progress -> Int in
                    defer { progress.next += 1 }
                    return progress.next
                }
                guard slot < schedule.count else { return }
                let index = schedule[slot]
                var job = jobs[index]
                let decoded = decode(&job)
                let size = job.size
                let done = state.withLock { progress -> Int64 in
                    progress.results[index] = decoded
                    progress.bytes += size
                    return progress.bytes
                }
                progress(min(1, Double(done) / Double(totalBytes)))
            }
        }
        return state.withLock { $0.results }
    }

    private static func decode(_ job: inout DecodeJob) -> DecodedFile {
        let (lines, read) = TranscriptDecoder.decode(job.family, reader: &job.reader)
        return DecodedFile(reader: job.reader, lines: lines, read: read)
    }

    // MARK: - Live reading

    /// Reads files reported by the watcher. Unknown transcript paths under a root become new agents.
    @discardableResult
    func handleChanges(atCanonicalPaths paths: Set<String>) -> Bool {
        var changed = false
        if let opencode, paths.contains(where: opencode.matches(canonicalPath:)) {
            changed = apply(opencode.refresh(deduper: &deduper), live: true)
        }
        for path in paths.sorted() {
            guard let (localPath, family) = localPath(forCanonical: path) else { continue }
            if let file = files[localPath] {
                changed = read(file, live: true) || changed
            } else if let stat = FileStat.lstat(localPath), stat.isRegularFile {
                let file = track(DiscoveredFile(path: localPath, stat: stat), family: family, start: .beginning)
                changed = read(file, live: true) || changed
            }
        }
        return changed
    }

    /// The 1-second fallback: stat files modified in the last two minutes.
    @discardableResult
    func pollRecentFiles(nowMs: Int64) -> Bool {
        var changed = false
        if let update = opencode?.refreshIfChanged(deduper: &deduper) {
            changed = apply(update, live: true)
        }
        let hot = files.values.filter { nowMs - $0.lastStat.modifiedMs <= Self.hotWindowMs }
        for file in hot.sorted(by: { $0.path < $1.path }) {
            guard let stat = FileStat.lstat(file.path), stat.isRegularFile else {
                untrack(file.path)
                changed = true
                continue
            }
            if file.hasChanged(comparedTo: stat) { changed = read(file, live: true) || changed }
        }
        return changed
    }

    /// Full rediscovery: new files are read from offset 0, vanished files are dropped, and files active today are
    /// re-checked in case the watcher missed a change.
    @discardableResult
    func rediscover(todayStartMs: Int64) -> Bool {
        var changed = refreshAvailability()
        var seen: Set<String> = []
        var fresh: [(DiscoveredFile, TranscriptFamily)] = []
        let activeSince = todayStartMs - Self.startupLookbackMs
        for root in roots where root.enabled && root.available {
            for path in boundedCandidates(root) {
                seen.insert(path)
                if let file = files[path] {
                    guard file.lastStat.modifiedMs >= activeSince, let stat = FileStat.lstat(path) else { continue }
                    if file.hasChanged(comparedTo: stat) { changed = read(file, live: true) || changed }
                } else if let stat = FileStat.lstat(path), stat.isRegularFile {
                    fresh.append((DiscoveredFile(path: path, stat: stat), root.family))
                }
            }
        }
        for path in files.keys where !seen.contains(path) {
            untrack(path)
            changed = true
        }
        fresh.sort { TranscriptDiscovery.startupOrder($0.0, $1.0) }
        for (discovered, family) in fresh {
            let file = track(discovered, family: family, start: .beginning)
            changed = read(file, live: true) || changed
        }
        if let opencode {
            if opencodeNeedsSeed {
                seedOpencode(todayStartMs: todayStartMs)
                changed = true
            } else {
                changed = apply(opencode.rediscover(nowMs: clock(), deduper: &deduper), live: true) || changed
            }
        }
        return changed
    }

    /// Candidate paths of a root, limited to the most recently modified ones when there are too many.
    private func boundedCandidates(_ root: Root) -> [String] {
        let candidates = scan(root)
        guard candidates.count > TranscriptDiscovery.maxFiles else { return candidates }
        let ranked = candidates.compactMap { path -> DiscoveredFile? in
            if let file = files[path] { return DiscoveredFile(path: path, stat: file.lastStat) }
            guard let stat = FileStat.lstat(path), stat.isRegularFile else { return nil }
            return DiscoveredFile(path: path, stat: stat)
        }
        return TranscriptDiscovery.mostRecent(ranked).map(\.path)
    }

    private func scan(_ root: Root) -> [String] {
        var index = indexes[root.path] ?? DirectoryIndex(root: root.path, names: root.family.names)
        let paths = index.scan()
        indexes[root.path] = index
        return paths
    }

    @discardableResult
    private func read(_ file: TrackedFile, live: Bool) -> Bool {
        var upserts: [ResponseUpsert] = []
        let result = file.reader.read(onReset: { file.resetParser() }, onMalformed: { file.parser.noteMalformedLine() }) { bytes in
            file.parser.consume(rawLine: bytes, deduper: &deduper) { upserts.append($0) }
            guard !upserts.isEmpty else { return }
            let project = projectName(of: file)
            for upsert in upserts { deliver(upsert, project: project, live: live) }
            upserts.removeAll(keepingCapacity: true)
        }
        if result.failed, FileStat.lstat(file.path) == nil {
            untrack(file.path)
            return true
        }
        finishRead(of: file, result: result)
        return result.bytesRead > 0 || result.didReset
    }

    private func finishRead(of file: TrackedFile, result: FileTailer.ReadResult) {
        if result.oversized { partialHistory = true }
        if result.failed {
            unreadablePaths.insert(file.path)
        } else {
            unreadablePaths.remove(file.path)
        }
        if let stat = result.stat { file.lastStat = stat }
        syncAgent(file)
    }

    // MARK: - Engine

    func advance(nowMs: Int64, todayStartMs: Int64, emitsEvents: Bool) -> (output: EngineOutput, events: [SpeedEvent]) {
        if parentLinksAreStale {
            parentLinks = AgentParents.resolve(Array(parentMembers.values), direct: opencodeLinks)
            engine.setParents(parentLinks)
            parentLinksAreStale = false
        }
        let output = engine.advance(nowMs: nowMs, todayStartMs: todayStartMs, emitsEvents: emitsEvents)
        let events = pendingEvents + output.events
        pendingEvents.removeAll()
        return (output, events)
    }

    /// Responses currently held by the engine.
    var responses: [ResponseRecord] { engine.storedResponses }

    var malformedLines: Int {
        files.values.reduce(removedMalformedLines) { $0 + $1.parser.state.malformedLines } + (opencode?.malformedRows ?? 0)
    }

    var unreadableFileCount: Int { unreadablePaths.count + (opencode?.status == .unreadable ? 1 : 0) }

    /// Agents of each source with at least one line today.
    func sessionCounts(todayStartMs: Int64) -> [SourceKind: Int] {
        var counts: [SourceKind: Int] = [:]
        for file in files.values {
            let state = file.parser.state
            guard let last = state.lastLineMs, last >= todayStartMs, enabledSources.contains(state.source) else { continue }
            counts[state.source, default: 0] += 1
        }
        if let opencode, enabledSources.contains(.opencode) {
            counts[.opencode] = opencode.activeSessionCount(todayStartMs: todayStartMs)
        }
        return counts
    }

    private func deliver(_ upsert: ResponseUpsert, project: String, live: Bool) {
        guard enabledSources.contains(upsert.record.source) else { return }
        let nowMs = live ? clock() : nil
        if let event = engine.upsert(upsert.record, project: project, isCompletion: upsert.isCompletion, emitsEvents: live, nowMs: nowMs) {
            pendingEvents.append(event)
        }
    }

    private func syncAgent(_ file: TrackedFile) {
        let state = file.parser.state
        if state.lastLineMs != nil, enabledSources.contains(state.source) {
            let member = AgentParents.Member(id: file.path, family: file.family, thread: state.thread)
            if parentMembers.updateValue(member, forKey: file.path) != member { parentLinksAreStale = true }
            engine.setAgent(EngineAgent(id: file.path, state: state, parentId: parentLinks[file.path]))
        } else {
            removeAgent(file.path)
        }
    }

    private func removeAgent(_ path: String) {
        if parentMembers.removeValue(forKey: path) != nil { parentLinksAreStale = true }
        engine.removeAgent(id: path)
    }

    private func projectName(of file: TrackedFile) -> String {
        file.parser.state.project ?? EngineAgent.fallbackProject(for: file.path)
    }

    // MARK: - OpenCode

    private func seedOpencode(todayStartMs: Int64) {
        guard let opencode else { return }
        opencodeNeedsSeed = false
        apply(opencode.seed(todayStartMs: todayStartMs, deduper: &deduper), live: false)
    }

    @discardableResult
    private func apply(_ update: OpencodeSource.Update, live: Bool) -> Bool {
        guard !update.isEmpty else { return false }
        for (upsert, project) in update.upserts { deliver(upsert, project: project, live: live) }
        for id in update.removedAgents { engine.removeAgent(id: id) }
        for (id, state) in update.agents {
            if state.lastLineMs != nil {
                engine.setAgent(EngineAgent(id: id, state: state, parentId: parentLinks[id]))
            } else {
                engine.removeAgent(id: id)
            }
        }
        let links = opencode?.parentLinks ?? [:]
        if links != opencodeLinks {
            opencodeLinks = links
            parentLinksAreStale = true
        }
        return true
    }

    // MARK: - Tracking

    private func track(_ file: DiscoveredFile, family: TranscriptFamily, start: FileTailer.Start) -> TrackedFile {
        let tracked = TrackedFile(
            path: file.path, family: family, fileID: nextFileID, stat: file.stat, start: start,
            maxLineBytes: limits.maxLineBytes
        )
        nextFileID &+= 1
        files[file.path] = tracked
        return tracked
    }

    private func untrack(_ path: String) {
        guard let file = files.removeValue(forKey: path) else { return }
        removedMalformedLines += file.parser.state.malformedLines
        unreadablePaths.remove(path)
        removeAgent(path)
    }

    /// Maps an FSEvents (canonical) path to the configured-root path, if it names a candidate transcript.
    private func localPath(forCanonical path: String) -> (String, TranscriptFamily)? {
        let name = (path as NSString).lastPathComponent
        for root in roots where root.enabled && root.available && root.family.names.matches(name) {
            let prefix = root.canonicalPath == "/" ? "/" : root.canonicalPath + "/"
            guard path.hasPrefix(prefix) else { continue }
            let relative = path.dropFirst(prefix.count)
            let components = relative.split(separator: "/", omittingEmptySubsequences: true)
            guard !components.isEmpty, root.family.names.accepts(relativeComponents: components),
                  !relative.contains("/../") else { return nil }
            let localPrefix = root.path == "/" ? "/" : root.path + "/"
            return (localPrefix + relative, root.family)
        }
        return nil
    }
}
