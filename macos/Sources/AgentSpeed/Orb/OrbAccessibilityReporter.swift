import AgentSpeedCore
import AppKit

/// Keeps the orb's accessibility label, value and hint current, publishing the spoken value at most once per second.
@MainActor
final class OrbAccessibilityReporter {
    static let interval: TimeInterval = 1

    private let model: AppModel
    private weak var host: OrbHostView?
    private var loop: ObservationLoop?
    private var timer: Timer?
    private var publishedAt: TimeInterval = -.infinity

    init(model: AppModel, host: OrbHostView) {
        self.model = model
        self.host = host
        loop = ObservationLoop { [weak self] in self?.observe() }
    }

    func cancel() {
        loop?.cancel()
        timer?.invalidate()
        timer = nil
    }

    private func observe() {
        _ = model.gauge.readoutValue
        _ = model.gauge.tier
        _ = model.l10n.language
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
        let gauge = model.gauge
        host.accessibilityLabelText = l10n("app.name")
        host.accessibilityHelpText = l10n("mac.a11y.orbHint")
        let value = l10n("a11y.orb", SpeedFormat.speed(gauge.readoutValue), l10n(gauge.tier.titleKey))
        guard value != host.accessibilityValueText else { return }
        host.accessibilityValueText = value
        NSAccessibility.post(element: host, notification: .valueChanged)
    }
}
