import Foundation

/// Default data locations (METRICS 2). Safe to call from any thread.
enum TranscriptLocations {
    static var defaultCodexRoot: URL {
        if let home = environment("CODEX_HOME") {
            return URL(fileURLWithPath: home, isDirectory: true).appendingPathComponent("sessions", isDirectory: true)
        }
        return home.appendingPathComponent(".codex/sessions", isDirectory: true)
    }

    static var defaultClaudeRoot: URL {
        if let directory = environment("CLAUDE_CONFIG_DIR") {
            return URL(fileURLWithPath: directory, isDirectory: true).appendingPathComponent("projects", isDirectory: true)
        }
        return home.appendingPathComponent(".claude/projects", isDirectory: true)
    }

    static var defaultDshRoot: URL {
        if let home = environment("DSH_HOME") {
            return URL(fileURLWithPath: home, isDirectory: true).appendingPathComponent("sessions", isDirectory: true)
        }
        return home.appendingPathComponent(".dsh/sessions", isDirectory: true)
    }

    /// The folder that contains `opencode.db`.
    static var defaultOpencodeRoot: URL {
        if let data = environment("XDG_DATA_HOME") {
            return URL(fileURLWithPath: data, isDirectory: true).appendingPathComponent("opencode", isDirectory: true)
        }
        return home.appendingPathComponent(".local/share/opencode", isDirectory: true)
    }

    /// Gemini CLI's `tmp` folders: `<home>/.gemini/tmp` and, for its macOS seatbelt sandbox (v0.63+),
    /// `<home>/.cache/.gemini/tmp`, where `<home>` is `$GEMINI_CLI_HOME`, else the user's home (METRICS 2).
    static var defaultGeminiRoots: [URL] {
        geminiRoots(cliHome: environment("GEMINI_CLI_HOME"))
    }

    static func geminiRoots(cliHome: String?) -> [URL] {
        let base = cliHome.map { URL(fileURLWithPath: $0, isDirectory: true) } ?? home
        return [
            base.appendingPathComponent(".gemini/tmp", isDirectory: true),
            base.appendingPathComponent(".cache/.gemini/tmp", isDirectory: true),
        ]
    }

    /// Default roots of a family; Gemini CLI has two.
    static func defaultRoots(for family: TranscriptFamily) -> [URL] {
        switch family {
        case .codex: [defaultCodexRoot]
        case .claude: [defaultClaudeRoot]
        case .dsh: [defaultDshRoot]
        case .gemini: defaultGeminiRoots
        }
    }

    private static var home: URL { FileManager.default.homeDirectoryForCurrentUser }

    private static func environment(_ name: String) -> String? {
        guard let value = ProcessInfo.processInfo.environment[name], !value.isEmpty else { return nil }
        return value
    }
}

extension MonitorConfiguration {
    /// The configured root of a transcript family, nil for the default.
    func configuredRoot(for family: TranscriptFamily) -> URL? {
        switch family {
        case .codex: codexRoot
        case .claude: claudeRoot
        case .dsh: dshRoot
        case .gemini: geminiRoot
        }
    }

    /// Every root of a family: the configured one, else the defaults.
    func resolvedRoots(for family: TranscriptFamily) -> [String] {
        let urls = configuredRoot(for: family).map { [$0] } ?? TranscriptLocations.defaultRoots(for: family)
        var roots: [String] = []
        for url in urls {
            let root = TranscriptDiscovery.normalizedRoot(url.path)
            if !roots.contains(root) { roots.append(root) }
        }
        return roots
    }

    /// The first root of a family.
    func resolvedRoot(for family: TranscriptFamily) -> String {
        resolvedRoots(for: family)[0]
    }

    func reads(_ family: TranscriptFamily) -> Bool {
        family.sources.contains { enabledSources.contains($0) }
    }

    var resolvedCodexRoot: String { resolvedRoot(for: .codex) }
    var resolvedClaudeRoot: String { resolvedRoot(for: .claude) }
    var resolvedDshRoot: String { resolvedRoot(for: .dsh) }

    var resolvedOpencodeRoot: String {
        TranscriptDiscovery.normalizedRoot((opencodeRoot ?? TranscriptLocations.defaultOpencodeRoot).path)
    }

    /// Codex archives finished sessions next to the sessions folder (METRICS 8).
    var resolvedCodexArchiveRoot: String {
        ((resolvedCodexRoot as NSString).deletingLastPathComponent as NSString).appendingPathComponent("archived_sessions")
    }

    var readsCodex: Bool { reads(.codex) }
    var readsClaude: Bool { reads(.claude) }
    var readsDsh: Bool { reads(.dsh) }
    var readsOpencode: Bool { enabledSources.contains(.opencode) }

    /// True when switching between the two configurations requires reading the transcripts again.
    func requiresRescan(comparedTo other: MonitorConfiguration) -> Bool {
        enabledSources != other.enabledSources
            || TranscriptFamily.allCases.contains { resolvedRoots(for: $0) != other.resolvedRoots(for: $0) }
            || resolvedOpencodeRoot != other.resolvedOpencodeRoot
    }
}
