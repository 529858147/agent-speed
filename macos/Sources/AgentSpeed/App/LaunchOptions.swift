import Foundation

/// Developer command-line flags.
struct LaunchOptions: Sendable {
    var demo = false
    var previewDirectory: URL?
    /// `--check-updates-now`: run a manual update check right after launch.
    var checkUpdatesNow = false
    /// `--install-update-now`: check, then install an available update without waiting for the button.
    var installUpdateNow = false
    #if DEBUG
    /// `--motion-harness DIR`: records the agents list expanding and collapsing, see `MotionHarness`.
    var motionHarnessDirectory: URL?
    #endif

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
            #if DEBUG
            case "--motion-harness":
                if let path = iterator.next() {
                    options.motionHarnessDirectory = URL(fileURLWithPath: (path as NSString).expandingTildeInPath, isDirectory: true)
                }
            #endif
            default:
                continue
            }
        }
        return options
    }
}
