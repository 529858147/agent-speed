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

    static var defaultDshRoot: URL {
        if let home = ProcessInfo.processInfo.environment["DSH_HOME"], !home.isEmpty {
            return URL(fileURLWithPath: home, isDirectory: true).appendingPathComponent("sessions", isDirectory: true)
        }
        return FileManager.default.homeDirectoryForCurrentUser
            .appendingPathComponent(".dsh/sessions", isDirectory: true)
    }
}

extension MonitorConfiguration {
    var resolvedCodexRoot: String {
        TranscriptDiscovery.normalizedRoot((codexRoot ?? TranscriptLocations.defaultCodexRoot).path)
    }

    var resolvedClaudeRoot: String {
        TranscriptDiscovery.normalizedRoot((claudeRoot ?? TranscriptLocations.defaultClaudeRoot).path)
    }

    var resolvedDshRoot: String {
        TranscriptDiscovery.normalizedRoot((dshRoot ?? TranscriptLocations.defaultDshRoot).path)
    }

    var readsCodex: Bool { enabledSources.contains(.codex) }
    var readsClaude: Bool { enabledSources.contains(.claudeCode) || enabledSources.contains(.claudeDesktop) }
    var readsDsh: Bool { enabledSources.contains(.deepseek) }

    /// True when switching between the two configurations requires reading the transcripts again.
    func requiresRescan(comparedTo other: MonitorConfiguration) -> Bool {
        enabledSources != other.enabledSources
            || resolvedCodexRoot != other.resolvedCodexRoot
            || resolvedClaudeRoot != other.resolvedClaudeRoot
            || resolvedDshRoot != other.resolvedDshRoot
    }
}
