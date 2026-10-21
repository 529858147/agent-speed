import Foundation

/// Developer command-line flags.
struct LaunchOptions: Sendable {
    var demo = false
    var previewDirectory: URL?

    static func parse(_ arguments: [String] = CommandLine.arguments) -> LaunchOptions {
        var options = LaunchOptions()
        var iterator = arguments.dropFirst().makeIterator()
        while let argument = iterator.next() {
            switch argument {
            case "--demo":
                options.demo = true
            case "--render-previews":
                if let path = iterator.next() {
                    options.previewDirectory = URL(fileURLWithPath: (path as NSString).expandingTildeInPath, isDirectory: true)
                }
            default:
                continue
            }
        }
        return options
    }
}
