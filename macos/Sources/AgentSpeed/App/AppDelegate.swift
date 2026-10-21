import AgentSpeedCore
import AppKit

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate, AppCommands {
    private let options: LaunchOptions
    private var model: AppModel?
    private var statusItem: StatusItemController?
    private var orb: OrbWindowController?
    private var dashboard: DashboardController?
    private var settingsWindow: SettingsWindowController?

    init(options: LaunchOptions) {
        self.options = options
    }

    func applicationDidFinishLaunching(_ notification: Notification) {
        if let directory = options.previewDirectory {
            PreviewRenderer.run(into: directory)
            NSApp.terminate(nil)
            return
        }
        let model = AppModel()
        model.commands = self
        self.model = model
        model.activate(live: true)

        let dashboard = DashboardController(model: model)
        self.dashboard = dashboard
        settingsWindow = SettingsWindowController(model: model)
        let statusItem = StatusItemController(model: model)
        self.statusItem = statusItem
        dashboard.onVisibilityChange = { [weak statusItem] shown in statusItem?.setHighlighted(shown) }
        orb = OrbWindowController(model: model)

        MainMenu.install(model: model)
        if options.demo { model.startDemoLoop() }
    }

    @objc func openSettingsFromMenu(_ sender: Any?) {
        showSettings(nil)
    }

    func applicationWillTerminate(_ notification: Notification) {
        orb?.savePosition()
        model?.shutdown()
    }

    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool {
        showSettings(nil)
        return false
    }

    // MARK: AppCommands

    func toggleDashboard() {
        guard let anchor = statusItem?.anchorView else { return }
        dashboard?.toggle(relativeTo: anchor)
    }

    func showDashboard() {
        guard let anchor = statusItem?.anchorView else { return }
        dashboard?.show(relativeTo: anchor)
    }

    func closeDashboard() {
        dashboard?.close()
    }

    func showSettings(_ pane: SettingsPane?) {
        dashboard?.close()
        settingsWindow?.show(pane: pane)
    }

    func setOrbVisible(_ visible: Bool) {
        model?.settings.orbVisible = visible
    }

    func resetOrbPosition() {
        orb?.resetPosition()
    }

    func quit() {
        NSApp.terminate(nil)
    }
}
