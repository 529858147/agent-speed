import Foundation

/// Reads local agent transcripts on a background queue and publishes snapshots to a `SpeedStore`.
@MainActor
public final class LiveMonitor {
    public private(set) var configuration: MonitorConfiguration
    private let store: SpeedStore

    public init(store: SpeedStore, configuration: MonitorConfiguration = MonitorConfiguration()) {
        self.store = store
        self.configuration = configuration
    }

    public func start() {}

    public func stop() {}

    public func update(configuration: MonitorConfiguration) {
        self.configuration = configuration
    }

    public static var defaultCodexRoot: URL {
        if let home = ProcessInfo.processInfo.environment["CODEX_HOME"], !home.isEmpty {
            return URL(fileURLWithPath: home, isDirectory: true).appendingPathComponent("sessions", isDirectory: true)
        }
        return FileManager.default.homeDirectoryForCurrentUser
            .appendingPathComponent(".codex/sessions", isDirectory: true)
    }

    public static var defaultClaudeRoot: URL {
        if let dir = ProcessInfo.processInfo.environment["CLAUDE_CONFIG_DIR"], !dir.isEmpty {
            return URL(fileURLWithPath: dir, isDirectory: true).appendingPathComponent("projects", isDirectory: true)
        }
        return FileManager.default.homeDirectoryForCurrentUser
            .appendingPathComponent(".claude/projects", isDirectory: true)
    }
}
