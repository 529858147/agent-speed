import AgentSpeedCore
import AppKit

/// Keeps the status item's accessibility value and tooltip current, at most once per second (DESIGN section 7).
@MainActor
final class StatusItemAccessibility {
    static let interval: TimeInterval = 1

    private let model: AppModel
    private weak var button: NSStatusBarButton?
    private var loop: ObservationLoop?
    private var pending: Timer?
    private var updatedAt: TimeInterval = -.infinity

    init(model: AppModel, button: NSStatusBarButton) {
        self.model = model
        self.button = button
        loop = ObservationLoop { [weak self] in
            guard let self else { return }
            _ = (self.model.gauge.readoutValue, self.model.gauge.title, self.model.l10n.language)
            _ = (self.model.settings.liveUnit, self.model.snapshot.liveCost, self.model.cost.currency)
            self.schedule()
        }
    }

    func cancel() {
        loop?.cancel()
        pending?.invalidate()
        pending = nil
    }

    private func schedule() {
        let elapsed = Clock.now - updatedAt
        if elapsed >= Self.interval {
            pending?.invalidate()
            pending = nil
            update()
            return
        }
        guard pending == nil else { return }
        let timer = Timer(timeInterval: Self.interval - elapsed, repeats: false) { [weak self] _ in
            MainActor.assumeIsolated {
                guard let self else { return }
                self.pending = nil
                self.update()
            }
        }
        RunLoop.main.add(timer, forMode: .common)
        pending = timer
    }

    private func update() {
        guard let button else { return }
        updatedAt = Clock.now
        let l10n = model.l10n
        let speed = Self.spokenSpeed(model.gauge.readoutValue, locale: l10n.locale)
        let title = model.titles.text(model.gauge.title, in: model.titles.general, l10n: l10n).capitalized(with: l10n.locale)
        let name = l10n("app.name")
        if button.accessibilityLabel() != name { button.setAccessibilityLabel(name) }
        if let money = StatusItemValue.money(model) {
            // The money unit reads the amount with the currency's name (DESIGN 9, Accessibility).
            let amount = money.atLeast ? l10n("a11y.orb.costAtLeast", money.number) : money.number
            let text = money.unknown ? l10n("a11y.orb.costUnknown", title) : l10n("a11y.orb.cost", amount, model.cost.currencyName, title)
            button.setAccessibilityValue(text)
            button.toolTip = text
            return
        }
        button.setAccessibilityValue(l10n("a11y.orb", speed, title))
        button.toolTip = l10n("mac.a11y.statusItem", speed, title)
    }

    /// Whole numbers with locale grouping read better aloud than the compact "1.28k" of the visual readout.
    static func spokenSpeed(_ value: Double, locale: Locale) -> String {
        let value = value.isFinite ? max(0, value) : 0
        let digits = value < 10 ? 0...1 : 0...0
        return value.formatted(.number.precision(.fractionLength(digits)).locale(locale))
    }
}
