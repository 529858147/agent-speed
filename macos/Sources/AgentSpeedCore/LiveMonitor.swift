import Foundation

/// Reads local agent transcripts on a background queue and publishes snapshots to a `SpeedStore`.
@MainActor
public final class LiveMonitor {
    public private(set) var configuration: MonitorConfiguration
    private let store: SpeedStore
    private var core: MonitorCore?
    private var mailbox: SnapshotMailbox?

    public init(store: SpeedStore, configuration: MonitorConfiguration = MonitorConfiguration()) {
        self.store = store
        self.configuration = configuration
    }

    /// True between `start()` and `stop()`.
    public var isRunning: Bool { core != nil }

    public func start() {
        guard core == nil else { return }
        let mailbox = SnapshotMailbox { [weak store] snapshot, events in
            store?.publishLive(snapshot, events: events)
        }
        let core = MonitorCore(configuration: configuration, mailbox: mailbox)
        self.mailbox = mailbox
        self.core = core
        core.start()
    }

    public func stop() {
        mailbox?.close()
        core?.stop()
        mailbox = nil
        core = nil
    }

    /// Applies new settings; transcripts are read again when the roots or the enabled sources change.
    public func update(configuration: MonitorConfiguration) {
        let needsRescan = configuration.requiresRescan(comparedTo: self.configuration)
        self.configuration = configuration
        guard needsRescan, isRunning else { return }
        stop()
        start()
    }

    public nonisolated static var defaultCodexRoot: URL {
        TranscriptLocations.defaultCodexRoot
    }

    public nonisolated static var defaultClaudeRoot: URL {
        TranscriptLocations.defaultClaudeRoot
    }

    public nonisolated static var defaultDshRoot: URL {
        TranscriptLocations.defaultDshRoot
    }

    /// Gemini CLI's default `tmp` folders (a configured root replaces both).
    public nonisolated static var defaultGeminiRoots: [URL] {
        TranscriptLocations.defaultGeminiRoots
    }

    /// The folder that contains OpenCode's `opencode.db`.
    public nonisolated static var defaultOpencodeRoot: URL {
        TranscriptLocations.defaultOpencodeRoot
    }

    /// omp's automatic `sessions` roots: the default root first, then one per named profile (a configured root
    /// replaces all of them).
    public nonisolated static var defaultOmpRoots: [URL] {
        TranscriptLocations.defaultOmpRoots
    }

    /// omp's default `sessions` root (the first of `defaultOmpRoots`).
    public nonisolated static var defaultOmpRoot: URL {
        TranscriptLocations.defaultOmpRoots[0]
    }

    /// Antigravity's store folders, each containing `conversations` (a configured store replaces all three).
    public nonisolated static var defaultAntigravityStores: [URL] {
        TranscriptLocations.defaultAntigravityStores
    }

    /// False for a `*-backup` store (or its `conversations` folder), which is never read.
    public nonisolated static func isReadableAntigravityStore(_ folder: URL) -> Bool {
        TranscriptLocations.isReadableAntigravityRoot(TranscriptLocations.antigravityConversations(folder).path)
    }
}
