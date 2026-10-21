import AgentSpeedCore
import Foundation

struct ProbeArguments {
    enum Mode {
        case dumpToday
        case watch(seconds: Int)
    }

    var mode: Mode
    var configuration: MonitorConfiguration

    static let usage = """
        usage: AgentSpeedProbe (--dump-today | --watch SECONDS) [--codex PATH] [--claude PATH]
        """

    static func parse(_ arguments: [String]) -> ProbeArguments? {
        var mode: Mode?
        var configuration = MonitorConfiguration()
        var iterator = arguments.makeIterator()
        while let argument = iterator.next() {
            switch argument {
            case "--dump-today":
                mode = .dumpToday
            case "--watch":
                guard let value = iterator.next(), let seconds = Int(value), seconds > 0, seconds <= 86_400 else { return nil }
                mode = .watch(seconds: seconds)
            case "--codex":
                guard let path = iterator.next() else { return nil }
                configuration.codexRoot = URL(fileURLWithPath: path, isDirectory: true)
            case "--claude":
                guard let path = iterator.next() else { return nil }
                configuration.claudeRoot = URL(fileURLWithPath: path, isDirectory: true)
            default:
                return nil
            }
        }
        guard let mode else { return nil }
        return ProbeArguments(mode: mode, configuration: configuration)
    }
}
