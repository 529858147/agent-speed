import AgentSpeedCore
import AppKit
import SwiftUI

/// `--render-previews DIR`: renders every surface to PNG for visual review, then exits.
@MainActor
enum PreviewRenderer {
    static func run(into directory: URL) {
        try? FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        let only = ProcessInfo.processInfo.environment["AGENT_SPEED_PREVIEWS"]?.split(separator: ",").map(String.init)
        func wants(_ group: String) -> Bool { only?.contains(group) ?? true }

        let suite = "AgentSpeedPreview"
        UserDefaults.standard.removePersistentDomain(forName: suite)
        let defaults = UserDefaults(suiteName: suite) ?? .standard
        let model = AppModel(
            settings: AppSettings(defaults: defaults),
            updates: UpdateController(configuration: .current(environment: [:]), store: UpdateStore(defaults: defaults))
        )
        model.activate(live: false)
        let redline = model.settings.redline
        GaugeChecks.run(into: directory)

        if wants("orb") {
            SurfacePreviews.renderOrbs(model: model, into: directory)
        }
        if wants("menubar") {
            SurfacePreviews.renderMenuBar(model: model, into: directory)
        }
        if wants("titles") {
            TitlePreviews.render(model: model, into: directory)
        }
        if wants("hero") {
            HeroPreviews.render(model: model, into: directory)
        }
        if only?.contains("herobench") == true {
            HeroBenchmark.run(model: model, into: directory)
        }
        if wants("dashboard") {
            let cases: [(String, Double, PreviewData.AgentSet, Bool)] = [
                ("cruise", redline * 0.08, .standard, false),
                ("redline", redline * 0.92, .standard, false),
                ("groups", redline * 0.6, .grouped, false),
                ("agents-collapsed", redline * 0.7, .crowd(16), false),
                ("agents-expanded", redline * 0.7, .crowd(10), true),
                ("agents-scrolling", redline * 0.7, .crowd(16), true),
            ]
            for (name, speed, agents, expanded) in cases {
                model.settings.agentsExpanded = expanded
                publish(model, speed: speed, agents: agents)
                for dark in [false, true] {
                    PreviewSnapshot.writeHosted(
                        DashboardView(model: model).appEnvironment(model).environment(\.orbStillTime, Clock.now),
                        dark: dark,
                        to: directory.appendingPathComponent("dashboard-\(name)-\(dark ? "dark" : "light").png")
                    )
                }
            }
            model.settings.agentsExpanded = false
            publish(model, speed: redline * 0.3)
            for dark in [false, true] {
                PreviewSnapshot.writeHosted(
                    DashboardView(model: model).appEnvironment(model)
                        .environment(\.orbStillTime, Clock.now)
                        .environment(\.previewHoveredSource, .claudeCode),
                    dark: dark,
                    to: directory.appendingPathComponent("dashboard-source-hover-\(dark ? "dark" : "light").png")
                )
            }
            publish(model, snapshot: PreviewData.withPartialSources(
                PreviewData.snapshot(speed: redline * 0.3, redline: model.settings.redline)
            ))
            for dark in [false, true] {
                PreviewSnapshot.writeHosted(
                    DashboardView(model: model).appEnvironment(model).environment(\.orbStillTime, Clock.now),
                    dark: dark,
                    to: directory.appendingPathComponent("dashboard-sources-\(dark ? "dark" : "light").png")
                )
            }
        }
        if wants("history") {
            HistoryLogicChecks.run(l10n: model.l10n, into: directory)
            HistoryPreviews.render(model: model, into: directory)
        }
        if wants("update") {
            UpdatePreviews.render(model: model, into: directory)
        }
        if wants("settings") {
            publish(model, speed: redline * 0.4)
            for pane in SettingsPane.allCases {
                for dark in [false, true] {
                    PreviewSnapshot.writeHosted(
                        SettingsPaneView(pane: pane, model: model).appEnvironment(model),
                        dark: dark,
                        to: directory.appendingPathComponent("settings-\(pane.rawValue)-\(dark ? "dark" : "light").png")
                    )
                }
            }
        }
    }

    static func publish(_ model: AppModel, speed: Double, agents: PreviewData.AgentSet = .standard) {
        publish(model, snapshot: PreviewData.snapshot(speed: speed, redline: model.settings.redline, agents: agents))
    }

    static func publish(_ model: AppModel, snapshot: SpeedSnapshot) {
        model.store.publishLive(snapshot)
        snapGauge(model, to: model.value(of: snapshot.live), now: Clock.now)
    }

    /// Snaps the gauge, title and tier to `value` at the current settings. The app syncs the gauge's redline from the
    /// settings asynchronously, so a preview that just changed the redline (the redline ladder renders) would
    /// otherwise snap against a stale one and show the wrong title and heat.
    static func snapGauge(_ model: AppModel, to value: Double, tier: SpeedTier? = nil, now: TimeInterval) {
        model.gauge.redline = model.settings.redline
        model.gauge.snap(to: value, tier: tier, now: now)
    }
}

@MainActor
enum PreviewSnapshot {
    static func writeImageRenderer<V: View>(_ view: V, dark: Bool, scale: CGFloat = 2, to url: URL) {
        let renderer = ImageRenderer(content: view.environment(\.colorScheme, dark ? .dark : .light))
        renderer.scale = scale
        renderer.isOpaque = false
        guard let image = renderer.cgImage else { return }
        write(image, to: url)
    }

    /// Renders through a real offscreen window so AppKit-backed controls draw too.
    static func writeHosted<V: View>(_ view: V, dark: Bool, background: NSColor = .windowBackgroundColor, to url: URL) {
        let appearance = NSAppearance(named: dark ? .darkAqua : .aqua)
        let host = NSHostingView(rootView: view
            .environment(\.colorScheme, dark ? .dark : .light)
            .background(Color(nsColor: background)))
        host.appearance = appearance
        let size = host.fittingSize
        host.frame = CGRect(origin: .zero, size: size)
        let window = NSWindow(contentRect: host.frame, styleMask: [.borderless], backing: .buffered, defer: false)
        window.appearance = appearance
        window.backgroundColor = background
        window.contentView = host
        host.layoutSubtreeIfNeeded()
        host.displayIfNeeded()
        RunLoop.main.run(until: Date().addingTimeInterval(0.15))
        guard let rep = host.bitmapImageRepForCachingDisplay(in: host.bounds) else { return }
        host.cacheDisplay(in: host.bounds, to: rep)
        guard let image = rep.cgImage else { return }
        write(image, to: url)
    }

    static func write(_ image: CGImage, to url: URL) {
        let rep = NSBitmapImageRep(cgImage: image)
        guard let data = rep.representation(using: .png, properties: [:]) else { return }
        try? data.write(to: url)
    }
}
