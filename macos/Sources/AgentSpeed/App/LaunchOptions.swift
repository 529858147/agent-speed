import Foundation

/// Developer command-line flags.
struct LaunchOptions: Sendable {
    var demo = false
    var previewDirectory: URL?
    /// `--check-updates-now`: run a manual update check right after launch.
    var checkUpdatesNow = false
    /// `--install-update-now`: check, then install an available update without waiting for the button.
    var installUpdateNow = false

    static func parse(_ arguments: [String] = CommandLine.arguments) -> LaunchOptions {
        var options = LaunchOptions()
        var iterator = arguments.dropFirst().makeIterator()
        while let argument = iterator.next() {
            switch argument {
            case "--demo":
                options.demo = true
            case "--check-updates-now":
                options.checkUpdatesNow = true
            case "--install-update-now":
                options.installUpdateNow = true
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
