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

        if wants("orb") {
            SurfacePreviews.renderOrbs(model: model, into: directory)
        }
        if wants("menubar") {
            SurfacePreviews.renderMenuBar(model: model, into: directory)
        }
        if wants("dashboard") {
            for (name, speed, grouped) in [("cruise", redline * 0.08, false), ("redline", redline * 0.92, false), ("groups", redline * 0.6, true)] {
                publish(model, speed: speed, grouped: grouped)
                for dark in [false, true] {
                    PreviewSnapshot.writeHosted(
                        DashboardView(model: model).appEnvironment(model).environment(\.orbStillTime, Clock.now),
                        dark: dark,
                        to: directory.appendingPathComponent("dashboard-\(name)-\(dark ? "dark" : "light").png")
                    )
                }
            }
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

    static func publish(_ model: AppModel, speed: Double, grouped: Bool = false) {
        let snapshot = PreviewData.snapshot(speed: speed, redline: model.settings.redline, grouped: grouped)
        model.store.publishLive(snapshot)
        model.gauge.snap(to: model.value(of: snapshot.live), now: Clock.now)
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
