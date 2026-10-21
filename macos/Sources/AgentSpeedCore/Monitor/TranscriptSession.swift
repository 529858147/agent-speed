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

/// Every transcript file of the enabled roots, their parsers, the shared dedupe set and the engine.
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

    let enabledSources: Set<SourceKind>
    let limits: ReadingLimits
    private(set) var roots: [Root]
    private var files: [String: TrackedFile] = [:]
    private var indexes: [TranscriptFamily: DirectoryIndex] = [:]
    private var deduper = Deduper()
    private var engine = SpeedEngine()
    private var nextFileID: FileID = 0
    private var pendingEvents: [SpeedEvent] = []
    private var unreadablePaths: Set<String> = []
    private var removedMalformedLines = 0
    private(set) var partialHistory = false

    init(configuration: MonitorConfiguration, limits: ReadingLimits = .standard) {
        enabledSources = configuration.enabledSources
        self.limits = limits
        roots = [
            Root(family: .codex, path: configuration.resolvedCodexRoot, canonicalPath: configuration.resolvedCodexRoot,
                 enabled: configuration.readsCodex, available: false),
            Root(family: .claude, path: configuration.resolvedClaudeRoot, canonicalPath: configuration.resolvedClaudeRoot,
                 enabled: configuration.readsClaude, available: false),
        ]
        refreshAvailability()
    }

    // MARK: - Roots

    var hasAvailableRoot: Bool { roots.contains { $0.enabled && $0.available } }

    var watchedPaths: [String] { roots.filter { $0.enabled && $0.available }.map(\.canonicalPath) }

    /// Re-checks which roots exist. Returns true when availability changed.
    @discardableResult
    func refreshAvailability() -> Bool {
        var changed = false
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

    func root(for family: TranscriptFamily) -> Root {
        roots.first { $0.family == family }!
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
                let readsBacklog = file.stat.modifiedMs >= threshold
                let start: FileTailer.Start
                if !readsBacklog {
                    start = .end(file.stat.size)
                } else if file.stat.size > limits.largeFileBytes {
                    start = .tail(limits.tailWindowBytes)
                    partialHistory = true
                } else {
                    start = .beginning
                }
                let tracked = track(file, family: root.family, start: start)
                if readsBacklog { backlog.append((file, tracked)) }
            }
        }
        backlog.sort { TranscriptDiscovery.startupOrder($0.file, $1.file) }
        let ordered = backlog.map(\.tracked)

        let jobs = ordered.map { DecodeJob(family: $0.family, tailer: $0.tailer, size: $0.lastStat.size) }
        let decoded = Self.decodeInParallel(jobs, progress: progress, isCancelled: isCancelled)
        for (file, result) in zip(ordered, decoded) {
            guard let result else { continue }
            file.tailer = result.tailer
            summary.filesRead += 1
            summary.bytesRead += Int64(result.read.bytesRead)
            var upserts: [ResponseUpsert] = []
            file.parser.consume(result.lines, deduper: &deduper) { upserts.append($0) }
            let project = projectName(of: file)
            for upsert in upserts { deliver(upsert, project: project, live: false) }
            finishRead(of: file, result: result.read)
        }
        summary.partialHistory = partialHistory
        summary.durationMs = Double(DispatchTime.now().uptimeNanoseconds - started) / 1_000_000
        return summary
    }

    private struct DecodeJob: Sendable {
        var family: TranscriptFamily
        var tailer: FileTailer
        var size: Int64
    }

    private struct DecodedFile: Sendable {
        var tailer: FileTailer
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
        switch job.family {
        case .codex:
            var lines: [LineExtraction<CodexLine>] = []
            let read = job.tailer.read { bytes in
                let extraction = CodexLineExtractor.extract(bytes)
                if case .blank = extraction { return }
                lines.append(extraction)
            }
            return DecodedFile(tailer: job.tailer, lines: .codex(lines), read: read)
        case .claude:
            var lines: [LineExtraction<ClaudeLine>] = []
            let read = job.tailer.read { bytes in
                let extraction = ClaudeLineExtractor.extract(bytes)
                if case .blank = extraction { return }
                lines.append(extraction)
            }
            return DecodedFile(tailer: job.tailer, lines: .claude(lines), read: read)
        }
    }

    // MARK: - Live reading

    /// Reads files reported by the watcher. Unknown transcript paths under a root become new agents.
    @discardableResult
    func handleChanges(atCanonicalPaths paths: Set<String>) -> Bool {
        var changed = false
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
        var index = indexes[root.family] ?? DirectoryIndex(root: root.path)
        let paths = index.scan()
        indexes[root.family] = index
        return paths
    }

    @discardableResult
    private func read(_ file: TrackedFile, live: Bool) -> Bool {
        var upserts: [ResponseUpsert] = []
        let result = file.tailer.read(onReset: { file.resetParser() }) { bytes in
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
        let output = engine.advance(nowMs: nowMs, todayStartMs: todayStartMs, emitsEvents: emitsEvents)
        let events = pendingEvents + output.events
        pendingEvents.removeAll()
        return (output, events)
    }

    /// Responses currently held by the engine.
    var responses: [ResponseRecord] { engine.storedResponses }

    var malformedLines: Int {
        files.values.reduce(removedMalformedLines) { $0 + $1.parser.state.malformedLines }
    }

    var unreadableFileCount: Int { unreadablePaths.count }

    /// Agents of each source with at least one line today.
    func sessionCounts(todayStartMs: Int64) -> [SourceKind: Int] {
        var counts: [SourceKind: Int] = [:]
        for file in files.values {
            let state = file.parser.state
            guard let last = state.lastLineMs, last >= todayStartMs, enabledSources.contains(state.source) else { continue }
            counts[state.source, default: 0] += 1
        }
        return counts
    }

    private func deliver(_ upsert: ResponseUpsert, project: String, live: Bool) {
        guard enabledSources.contains(upsert.record.source) else { return }
        if let event = engine.upsert(upsert.record, project: project, isCompletion: upsert.isCompletion, emitsEvents: live) {
            pendingEvents.append(event)
        }
    }

    private func syncAgent(_ file: TrackedFile) {
        let state = file.parser.state
        if state.lastLineMs != nil, enabledSources.contains(state.source) {
            engine.setAgent(EngineAgent(id: file.path, state: state))
        } else {
            engine.removeAgent(id: file.path)
        }
    }

    private func projectName(of file: TrackedFile) -> String {
        file.parser.state.project ?? EngineAgent.fallbackProject(for: file.path)
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
        engine.removeAgent(id: path)
    }

    /// Maps an FSEvents (canonical) path to the configured-root path, if it names a candidate transcript.
    private func localPath(forCanonical path: String) -> (String, TranscriptFamily)? {
        guard path.hasSuffix(".jsonl") else { return nil }
        for root in roots where root.enabled && root.available {
            let prefix = root.canonicalPath == "/" ? "/" : root.canonicalPath + "/"
            guard path.hasPrefix(prefix) else { continue }
            let relative = path.dropFirst(prefix.count)
            let depth = relative.split(separator: "/", omittingEmptySubsequences: true).count
            guard depth >= 1, depth <= TranscriptDiscovery.maxDepth + 1, !relative.contains("/../") else { return nil }
            let localPrefix = root.path == "/" ? "/" : root.path + "/"
            return (localPrefix + relative, root.family)
        }
        return nil
    }
}
