import AgentSpeedCore
import Foundation

guard let arguments = ProbeArguments.parse(Array(CommandLine.arguments.dropFirst())) else {
    FileHandle.standardError.write(Data((ProbeArguments.usage + "\n").utf8))
    exit(2)
}

switch arguments.mode {
case .dumpToday:
    DumpToday.run(configuration: arguments.configuration)
case .dumpHistory:
    DumpHistory.run(configuration: arguments.configuration, utc: arguments.utc, storePath: arguments.historyStore)
case .watch(let seconds):
    WatchMode.run(configuration: arguments.configuration, seconds: seconds)
}
