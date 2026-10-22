import AgentSpeedCore
import AgentSpeedOrbModel
import AppKit

/// Keeps one orb's accessibility label, value and hint current, publishing at most once per second (DESIGN 2b,
/// Accessibility and keyboard). A single orb tracking all sources reads exactly as before: the app name, today's
/// `a11y.orb` value and the platform hint. With several orbs (or a subset) the label is `a11y.orb.named` (or
/// `a11y.orb.unavailable`) and the hint names the sources.
@MainActor
final class OrbAccessibilityReporter {
    static let interval: TimeInterval = 1

    private let model: AppModel
    private let runtime: OrbRuntime
    private weak var host: OrbHostView?
    private var loop: ObservationLoop?
    private var timer: Timer?
    private var publishedAt: TimeInterval = -.infinity
    /// The unit last published; a change is announced once (DESIGN 9, Accessibility).
    private var publishedUnit: OrbUnit?

    init(model: AppModel, runtime: OrbRuntime, host: OrbHostView) {
        self.model = model
        self.runtime = runtime
        self.host = host
        loop = ObservationLoop { [weak self] in self?.observe() }
    }

    func cancel() {
        loop?.cancel()
        timer?.invalidate()
        timer = nil
    }

    private func observe() {
        let gauge = runtime.feed.gauge
        _ = (gauge.readoutValue, gauge.title, runtime.feed.unavailableTitleKey, model.l10n.language)
        _ = (runtime.feed.money, runtime.feed.unit)
        _ = model.orbs.document
        _ = model.titles.document
        let elapsed = Clock.now - publishedAt
        if elapsed >= Self.interval {
            publish()
            return
        }
        guard timer == nil else { return }
        let timer = Timer(timeInterval: Self.interval - elapsed, repeats: false) { [weak self] _ in
            MainActor.assumeIsolated {
                guard let self else { return }
                self.timer = nil
                self.publish()
            }
        }
        RunLoop.main.add(timer, forMode: .common)
        self.timer = timer
    }

    private func publish() {
        guard let host else { return }
        publishedAt = Clock.now
        let l10n = model.l10n
        let gauge = runtime.feed.gauge
        let orbs = model.orbs
        let speed = SpeedFormat.speed(gauge.readoutValue)
        let title = model.titles.text(gauge.title, in: model.titles.theme(forOrb: runtime.config), l10n: l10n)
        let single = orbs.orbs.count == 1 && runtime.config.sources.isAll
        let label: String
        let value: String
        let help: String
        // The money unit reads the amount with the currency's name (DESIGN 9, Accessibility).
        let money = runtime.feed.money
        let amount = money.map { $0.atLeast ? l10n("a11y.orb.costAtLeast", $0.number) : $0.number }
        let currency = model.cost.currencyName
        if single {
            label = l10n("app.name")
            if let money, money.unknown {
                value = l10n("a11y.orb.costUnknown", title)
            } else if let amount {
                value = l10n("a11y.orb.cost", amount, currency, title)
            } else {
                value = l10n("a11y.orb", speed, title)
            }
            help = l10n("mac.a11y.orbHint")
        } else {
            let name = orbs.displayName(of: runtime.id)
            if runtime.isUnavailable {
                label = l10n("a11y.orb.unavailable", name)
                value = ""
            } else if let money, money.unknown {
                label = name
                value = l10n("a11y.orb.costUnknown", title)
            } else if let amount {
                label = l10n("a11y.orb.costNamed", name, amount, currency, title)
                value = ""
            } else {
                label = l10n("a11y.orb.named", name, speed, title)
                value = ""
            }
            let names = runtime.config.sources.isAll
                ? l10n("orbs.sources.all")
                : runtime.config.sources.knownSources.map { l10n($0.titleKey) }.joined(separator: l10n("list.separator"))
            help = l10n("a11y.orb.sources", names)
        }
        host.accessibilityHelpText = help
        let labelChanged = label != host.accessibilityLabelText
        let valueChanged = value != host.accessibilityValueText
        host.accessibilityLabelText = label
        host.accessibilityValueText = value
        if valueChanged { NSAccessibility.post(element: host, notification: .valueChanged) }
        if labelChanged, !single { NSAccessibility.post(element: host, notification: .titleChanged) }
        let unit = runtime.feed.unit
        if let publishedUnit, publishedUnit != unit {
            let name = unit == .cost ? model.cost.unitPerSecond : l10n("unit.tokS")
            NSAccessibility.post(element: host, notification: .announcementRequested, userInfo: [
                .announcement: l10n("a11y.unitChanged", name),
                .priority: NSAccessibilityPriorityLevel.high.rawValue,
            ])
        }
        publishedUnit = unit
    }
}
