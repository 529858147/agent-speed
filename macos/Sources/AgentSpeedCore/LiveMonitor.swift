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
}
