import Foundation

/// Default transcript roots (METRICS 2). Safe to call from any thread.
enum TranscriptLocations {
    static var defaultCodexRoot: URL {
        if let home = ProcessInfo.processInfo.environment["CODEX_HOME"], !home.isEmpty {
            return URL(fileURLWithPath: home, isDirectory: true).appendingPathComponent("sessions", isDirectory: true)
        }
        return FileManager.default.homeDirectoryForCurrentUser
            .appendingPathComponent(".codex/sessions", isDirectory: true)
    }

    static var defaultClaudeRoot: URL {
        if let directory = ProcessInfo.processInfo.environment["CLAUDE_CONFIG_DIR"], !directory.isEmpty {
            return URL(fileURLWithPath: directory, isDirectory: true).appendingPathComponent("projects", isDirectory: true)
        }
        return FileManager.default.homeDirectoryForCurrentUser
            .appendingPathComponent(".claude/projects", isDirectory: true)
    }
}

extension MonitorConfiguration {
    var resolvedCodexRoot: String {
        TranscriptDiscovery.normalizedRoot((codexRoot ?? TranscriptLocations.defaultCodexRoot).path)
    }

    var resolvedClaudeRoot: String {
        TranscriptDiscovery.normalizedRoot((claudeRoot ?? TranscriptLocations.defaultClaudeRoot).path)
    }

    var readsCodex: Bool { enabledSources.contains(.codex) }
    var readsClaude: Bool { enabledSources.contains(.claudeCode) || enabledSources.contains(.claudeDesktop) }

    /// True when switching between the two configurations requires reading the transcripts again.
    func requiresRescan(comparedTo other: MonitorConfiguration) -> Bool {
        enabledSources != other.enabledSources
            || resolvedCodexRoot != other.resolvedCodexRoot
            || resolvedClaudeRoot != other.resolvedClaudeRoot
    }
}
