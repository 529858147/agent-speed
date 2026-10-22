import AgentSpeedCore
import Foundation

struct ProbeArguments {
    enum Mode {
        case dumpToday
        case dumpHistory
        case watch(seconds: Int)
    }

    var mode: Mode
    var configuration: MonitorConfiguration
    /// `--utc`: group history days in UTC instead of the local time zone.
    var utc = false
    /// `--history-store PATH`: reuse (and update) a persistent history store instead of analysing in memory.
    var historyStore: String?

    static let usage = """
        usage: AgentSpeedProbe (--dump-today | --dump-history [--utc] [--history-store PATH] | --watch SECONDS)
                               [--codex PATH] [--claude PATH] [--dsh PATH] [--opencode PATH] [--gemini PATH]
                               [--omp PATH] [--antigravity PATH]
        """

    static func parse(_ arguments: [String]) -> ProbeArguments? {
        var mode: Mode?
        var configuration = MonitorConfiguration()
        var utc = false
        var historyStore: String?
        var iterator = arguments.makeIterator()
        while let argument = iterator.next() {
            switch argument {
            case "--dump-today":
                mode = .dumpToday
            case "--dump-history":
                mode = .dumpHistory
            case "--utc":
                utc = true
            case "--history-store":
                guard let path = iterator.next() else { return nil }
                historyStore = path
            case "--watch":
                guard let value = iterator.next(), let seconds = Int(value), seconds > 0, seconds <= 86_400 else { return nil }
                mode = .watch(seconds: seconds)
            case "--codex":
                guard let path = iterator.next() else { return nil }
                configuration.codexRoot = URL(fileURLWithPath: path, isDirectory: true)
            case "--claude":
                guard let path = iterator.next() else { return nil }
                configuration.claudeRoot = URL(fileURLWithPath: path, isDirectory: true)
            case "--dsh":
                guard let path = iterator.next() else { return nil }
                configuration.dshRoot = URL(fileURLWithPath: path, isDirectory: true)
            case "--gemini":
                // One Gemini CLI `tmp` folder; it replaces both defaults.
                guard let path = iterator.next() else { return nil }
                configuration.geminiRoot = URL(fileURLWithPath: path, isDirectory: true)
            case "--omp":
                // One omp `sessions` folder; it replaces the default root and every profile.
                guard let path = iterator.next() else { return nil }
                configuration.ompRoot = URL(fileURLWithPath: path, isDirectory: true)
            case "--antigravity":
                // One Antigravity store folder or its `conversations` folder; it replaces the three default stores.
                guard let path = iterator.next() else { return nil }
                configuration.antigravityRoot = URL(fileURLWithPath: path, isDirectory: true)
            case "--opencode":
                // The database file (as in reference.py) or the folder that contains it.
                guard let path = iterator.next() else { return nil }
                configuration.opencodeRoot = URL(fileURLWithPath: path)
            default:
                return nil
            }
        }
        guard let mode else { return nil }
        return ProbeArguments(mode: mode, configuration: configuration, utc: utc, historyStore: historyStore)
    }
}
