import AgentSpeedCore
import AppKit

/// A data folder the user can relocate. Claude Code and Claude Desktop share the Claude root; OpenCode's folder is the
/// one that contains `opencode.db`; Gemini CLI reads one `tmp` folder, or by default two (an override replaces both).
enum TranscriptFolder: CaseIterable {
    case codex
    case claude
    case dsh
    case opencode
    case gemini

    /// Every default location, in reading order.
    var defaultURLs: [URL] {
        switch self {
        case .codex: [LiveMonitor.defaultCodexRoot]
        case .claude: [LiveMonitor.defaultClaudeRoot]
        case .dsh: [LiveMonitor.defaultDshRoot]
        case .opencode: [LiveMonitor.defaultOpencodeRoot]
        case .gemini: LiveMonitor.defaultGeminiRoots
        }
    }

    var defaultURL: URL { defaultURLs[0] }

    var chooseMessageKey: String {
        switch self {
        case .codex: "mac.settings.sources.chooseCodex"
        case .claude: "mac.settings.sources.chooseClaude"
        case .dsh: "mac.settings.sources.chooseDsh"
        case .opencode: "mac.settings.sources.chooseOpencode"
        case .gemini: "mac.settings.sources.chooseGemini"
        }
    }

    /// What the section footer explains about the folder, if anything.
    var footnoteKey: String? {
        switch self {
        case .codex: nil
        case .claude: "settings.sources.sharedRoot"
        case .dsh: "settings.sources.dshHint"
        case .opencode: "settings.sources.opencodeHint"
        case .gemini: "settings.sources.geminiHint"
        }
    }

    @MainActor
    func customURL(in settings: AppSettings) -> URL? {
        switch self {
        case .codex: settings.codexRoot
        case .claude: settings.claudeRoot
        case .dsh: settings.dshRoot
        case .opencode: settings.opencodeRoot
        case .gemini: settings.geminiRoot
        }
    }

    @MainActor
    func url(in settings: AppSettings) -> URL {
        customURL(in: settings) ?? defaultURL
    }

    /// The folders read with the current settings: the custom one, else every default.
    @MainActor
    func urls(in settings: AppSettings) -> [URL] {
        customURL(in: settings).map { [$0] } ?? defaultURLs
    }

    /// Stores a custom folder; choosing the (first) default location, or nil, clears the override.
    @MainActor
    func setURL(_ url: URL?, in settings: AppSettings) {
        let value = url.flatMap { Self.isSameFolder($0, defaultURL) ? nil : $0 }
        switch self {
        case .codex: settings.codexRoot = value
        case .claude: settings.claudeRoot = value
        case .dsh: settings.dshRoot = value
        case .opencode: settings.opencodeRoot = value
        case .gemini: settings.geminiRoot = value
        }
    }

    static func isSameFolder(_ lhs: URL, _ rhs: URL) -> Bool {
        lhs.standardizedFileURL.path == rhs.standardizedFileURL.path
    }

    static func exists(_ url: URL) -> Bool {
        var isDirectory: ObjCBool = false
        return FileManager.default.fileExists(atPath: url.path, isDirectory: &isDirectory) && isDirectory.boolValue
    }

    /// "~/.codex/sessions" for display; the full path stays available as help text.
    static func displayPath(_ url: URL) -> String {
        (url.standardizedFileURL.path as NSString).abbreviatingWithTildeInPath
    }
}

/// Lets the user pick a transcript folder in a sheet and validates the choice. Never writes anything.
@MainActor
enum FolderChooser {
    static func choose(
        message: String,
        prompt: String,
        startingAt current: URL,
        window: NSWindow?,
        completion: @escaping @MainActor (URL) -> Void
    ) {
        let panel = NSOpenPanel()
        panel.canChooseDirectories = true
        panel.canChooseFiles = false
        panel.allowsMultipleSelection = false
        panel.canCreateDirectories = false
        panel.treatsFilePackagesAsDirectories = false
        panel.resolvesAliases = true
        panel.showsHiddenFiles = true
        panel.message = message
        panel.prompt = prompt
        panel.directoryURL = nearestExistingFolder(current)

        if let window {
            panel.beginSheetModal(for: window) { response in
                MainActor.assumeIsolated { finish(panel, response, completion) }
            }
        } else {
            panel.begin { response in
                MainActor.assumeIsolated { finish(panel, response, completion) }
            }
        }
    }

    private static func finish(
        _ panel: NSOpenPanel,
        _ response: NSApplication.ModalResponse,
        _ completion: @MainActor (URL) -> Void
    ) {
        guard response == .OK, let url = panel.url else { return }
        guard let folder = validatedFolder(url) else {
            NSSound.beep()
            return
        }
        completion(folder)
    }

    /// A local, existing directory (not a package), as a standardized file URL.
    static func validatedFolder(_ url: URL) -> URL? {
        guard url.isFileURL else { return nil }
        let standardized = url.standardizedFileURL
        let values = try? standardized.resourceValues(forKeys: [.isDirectoryKey, .isPackageKey])
        guard values?.isDirectory == true, values?.isPackage != true else { return nil }
        return URL(fileURLWithPath: standardized.path, isDirectory: true)
    }

    private static func nearestExistingFolder(_ url: URL) -> URL {
        var candidate = url.standardizedFileURL
        while !TranscriptFolder.exists(candidate), candidate.pathComponents.count > 1 {
            candidate.deleteLastPathComponent()
        }
        return candidate
    }
}
