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
    private var historyWindow: HistoryWindowController?
    private var updateWindow: UpdateWindowController?
    private var updateScheduler: UpdateScheduler?
    private let updateNotifier = UpdateNotifier()

    init(options: LaunchOptions) {
        self.options = options
    }

    func applicationDidFinishLaunching(_ notification: Notification) {
        if let directory = options.previewDirectory {
            PreviewRenderer.run(into: directory)
            NSApp.terminate(nil)
            return
        }
        #if DEBUG
        if let directory = options.heroCPUDirectory {
            HeroLiveHarness.start(into: directory)
            return
        }
        if let directory = options.heroFramesDirectory {
            HeroFrameHarness.run(into: directory)
            NSApp.terminate(nil)
            return
        }
        if let directory = options.motionHarnessDirectory {
            MotionHarness.start(into: directory)
            return
        }
        if let directory = options.screenshotsDirectory {
            ScreenshotHarness.run(into: directory, onlyHistory: options.screenshotsOnlyHistory)
            return
        }
        #endif
        let model = AppModel()
        model.commands = self
        self.model = model
        model.activate(live: true)

        let dashboard = DashboardController(model: model)
        self.dashboard = dashboard
        settingsWindow = SettingsWindowController(model: model)
        historyWindow = HistoryWindowController(model: model)
        let statusItem = StatusItemController(model: model)
        self.statusItem = statusItem
        dashboard.onVisibilityChange = { [weak statusItem, weak model] shown in
            statusItem?.setHighlighted(shown)
            if !shown { model?.updates.clearUpdatedNotice() }
        }
        orb = OrbWindowController(model: model)

        MainMenu.install(model: model)
        if options.demo { model.startDemoLoop() }
        startUpdates(model: model)
    }

    /// Notifications, the schedule, the relaunch hand-off, the developer flags and the one-time `update.done`.
    private func startUpdates(model: AppModel) {
        let updates = model.updates
        updateWindow = UpdateWindowController(model: model)
        updateNotifier.install()
        updateNotifier.onOpen = { [weak self] in self?.showUpdate() }
        updates.onNewRelease = { [weak self, weak model] release in
            guard let self, let model else { return }
            let version = release.version.description
            self.updateNotifier.announce(
                release,
                title: model.l10n("update.available.title", version),
                body: model.l10n("update.notification.body")
            )
        }
        updates.onReadyToRelaunch = {
            NSApp.terminate(nil)
        }
        UpdateWorkspace.removeStale()

        let scheduler = UpdateScheduler(controller: updates, settings: model.settings)
        updateScheduler = scheduler
        scheduler.start()

        if options.installUpdateNow {
            Task { [weak self] in
                await updates.check(manual: true)
                guard updates.availableRelease != nil else { return }
                self?.showUpdate()
                updates.install()
            }
        } else if options.checkUpdatesNow {
            checkForUpdates()
        }
        if updates.justUpdatedTo != nil {
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.8) { [weak self] in self?.showDashboard() }
        }
    }

    @objc func openSettingsFromMenu(_ sender: Any?) {
        showSettings(nil)
    }

    @objc func openHistoryFromMenu(_ sender: Any?) {
        showHistory(source: nil)
    }

    func applicationWillTerminate(_ notification: Notification) {
        updateScheduler?.stop()
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

    func showHistory(source: SourceKind?) {
        dashboard?.close()
        model?.startHistory()
        historyWindow?.show(source: source)
    }

    func setOrbVisible(_ visible: Bool) {
        model?.settings.orbVisible = visible
    }

    func resetOrbPosition() {
        orb?.resetPosition()
    }

    func showUpdate() {
        dashboard?.close()
        updateWindow?.show()
    }

    func checkForUpdates() {
        model?.updates.startCheck(manual: true)
        showUpdate()
    }

    func quit() {
        NSApp.terminate(nil)
    }
}
