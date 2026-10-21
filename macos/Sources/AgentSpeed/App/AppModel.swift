import AgentSpeedCore
import AppKit
import Observation

/// Commands the UI can trigger; implemented by `AppDelegate`.
@MainActor
protocol AppCommands: AnyObject {
    func toggleDashboard()
    func showDashboard()
    func closeDashboard()
    func showSettings(_ pane: SettingsPane?)
    /// Opens the Usage History window filtered to `source` (all sources when nil).
    func showHistory(source: SourceKind?)
    func setOrbVisible(_ visible: Bool)
    func resetOrbPosition()
    /// Opens the update window on the current update state.
    func showUpdate()
    /// Manual check from a menu: opens the update window, which reports the result.
    func checkForUpdates()
    func quit()
}

/// The app's shared state: one store, the settings, the live gauge, the data monitor and the daily history.
@MainActor
@Observable
final class AppModel {
    let store: SpeedStore
    /// Daily usage (METRICS 8) for the history window, filled by `HistoryService` (or previews).
    let history: HistoryStore
    let settings: AppSettings
    let l10n: Localizer
    let gauge: LiveGauge
    let accessibility: AccessibilityState
    let launchAtLogin: LaunchAtLogin
    let updates: UpdateController

    /// True while the 20 s effects preview (or the `--demo` loop) drives the store.
    private(set) var isPreviewing = false

    @ObservationIgnored weak var commands: AppCommands?
    @ObservationIgnored private(set) var monitor: LiveMonitor?
    @ObservationIgnored private(set) var historyService: HistoryService?
    @ObservationIgnored private var historyStartTimer: Timer?
    @ObservationIgnored private var demo: DemoDriver?
    @ObservationIgnored private var demoLoops = false
    @ObservationIgnored private var loops: [ObservationLoop] = []
    @ObservationIgnored private var appliedConfiguration: MonitorConfiguration?

    init(
        store: SpeedStore = SpeedStore(),
        settings: AppSettings = AppSettings(),
        updates: UpdateController? = nil,
        history: HistoryStore = HistoryStore()
    ) {
        self.store = store
        self.history = history
        self.settings = settings
        self.updates = updates ?? UpdateController()
        l10n = Localizer(preference: settings.language)
        gauge = LiveGauge()
        accessibility = AccessibilityState()
        launchAtLogin = LaunchAtLogin()
        gauge.redline = settings.redline
    }

    var snapshot: SpeedSnapshot { store.snapshot }

    /// Effects level after Reduce Motion.
    var effectsLevel: EffectsLevel { accessibility.effectiveLevel(settings.effects) }

    func value(of rate: Rate) -> Double { rate.value(for: settings.metric) }

    /// Starts reacting to settings and data. `live` starts reading transcripts.
    func activate(live: Bool) {
        loops.append(ObservationLoop { [weak self] in
            guard let self else { return }
            self.l10n.apply(self.settings.language)
        })
        loops.append(ObservationLoop { [weak self] in
            guard let self else { return }
            Self.applyAppearance(self.settings.appearance)
        })
        loops.append(ObservationLoop { [weak self] in
            guard let self else { return }
            self.gauge.redline = self.settings.redline
            let snapshot = self.store.snapshot
            self.gauge.ingest(self.value(of: snapshot.live), now: Clock.now)
        })
        loops.append(ObservationLoop { [weak self] in
            // The effects preview sweeps every title; it must not replace the vehicle the agents last reached.
            guard let self, let key = self.gauge.title.vehicleKey, !self.store.isPreviewing else { return }
            if self.settings.heroLastTitle != key { self.settings.heroLastTitle = key }
        })
        loops.append(ObservationLoop { [weak self] in
            guard let self else { return }
            let previewing = self.store.isPreviewing
            if self.isPreviewing != previewing { self.isPreviewing = previewing }
        })
        guard live else { return }
        let monitor = LiveMonitor(store: store, configuration: settings.monitorConfiguration)
        self.monitor = monitor
        appliedConfiguration = settings.monitorConfiguration
        monitor.start()
        loops.append(ObservationLoop { [weak self] in
            guard let self else { return }
            let configuration = self.settings.monitorConfiguration
            guard configuration != self.appliedConfiguration else { return }
            self.appliedConfiguration = configuration
            self.monitor?.update(configuration: configuration)
            self.historyService?.update(configuration: configuration)
        })
        scheduleHistory()
    }

    /// The history analysis reads every log, so it starts only once the live monitor has read today's transcripts
    /// (or after a grace period, or as soon as the history window asks for it), never slowing the live start.
    private func scheduleHistory() {
        let timer = Timer(timeInterval: Self.historyGracePeriod, repeats: false) { [weak self] _ in
            MainActor.assumeIsolated { self?.startHistory() }
        }
        timer.tolerance = 2
        RunLoop.main.add(timer, forMode: .common)
        historyStartTimer = timer
        loops.append(ObservationLoop { [weak self] in
            guard let self, self.historyService == nil else { return }
            if case .starting = self.store.snapshot.state { return }
            DispatchQueue.main.async { [weak self] in self?.startHistory() }
        })
    }

    private static let historyGracePeriod: TimeInterval = 20

    /// Starts the daily history analysis if it is not running yet.
    func startHistory() {
        guard historyService == nil, monitor != nil else { return }
        historyStartTimer?.invalidate()
        historyStartTimer = nil
        let service = HistoryService(store: history, configuration: settings.monitorConfiguration, liveStore: store)
        historyService = service
        service.start()
    }

    /// Deletes the stored daily statistics and analyses the current logs again (Settings → Data Sources).
    func clearHistory() {
        startHistory()
        history.clear()
    }

    func shutdown() {
        demo?.stop()
        historyStartTimer?.invalidate()
        historyService?.stop()
        monitor?.stop()
        loops.forEach { $0.cancel() }
        loops.removeAll()
    }

    /// Runs the scripted sweep through every tier (DESIGN section 5, Preview effects).
    func previewEffects(duration: TimeInterval = 20) {
        let driver = demo ?? DemoDriver(store: store)
        demo = driver
        driver.onFinish = { [weak self] in
            guard let self, self.demoLoops else { return }
            DispatchQueue.main.async { [weak self] in
                guard let self, self.demoLoops else { return }
                self.demo?.start(redline: self.demoRedline, duration: duration)
            }
        }
        driver.start(redline: demoRedline, duration: duration)
    }

    /// `DemoDriver` scales output speed to the redline it gets and reports total throughput as 24× output, so the
    /// total metric needs a proportionally smaller input to sweep through the same tiers.
    private var demoRedline: Double {
        settings.metric == .output ? settings.redline : settings.redline / DemoDriverRatio.totalPerOutput
    }

    func stopPreview() {
        demoLoops = false
        demo?.stop()
    }

    /// `--demo`: loop the preview forever.
    func startDemoLoop() {
        demoLoops = true
        previewEffects()
    }

    private static func applyAppearance(_ mode: AppearanceMode) {
        let appearance: NSAppearance? = switch mode {
        case .auto: nil
        case .light: NSAppearance(named: .aqua)
        case .dark: NSAppearance(named: .darkAqua)
        }
        if NSApp.appearance?.name != appearance?.name { NSApp.appearance = appearance }
    }
}

enum DemoDriverRatio {
    static let totalPerOutput = 24.0
}

/// Settings window panes (DESIGN section 5).
enum SettingsPane: String, CaseIterable, Identifiable, Sendable {
    case general, menuBar, orb, effects, sources, about

    var id: String { rawValue }

    var titleKey: String {
        switch self {
        case .general: "settings.general"
        case .menuBar: "settings.bar"
        case .orb: "settings.orb"
        case .effects: "settings.effects"
        case .sources: "settings.sources"
        case .about: "settings.about"
        }
    }

    var symbol: String {
        switch self {
        case .general: "gearshape"
        case .menuBar: "menubar.rectangle"
        case .orb: "circle.circle"
        case .effects: "sparkles"
        case .sources: "folder"
        case .about: "info.circle"
        }
    }
}
