import AppKit

/// Owns the panel's alpha: fade in/out for the visibility setting and the dimmed state of "fade when idle".
@MainActor
final class OrbWindowFader {
    static let dimmedAlpha: CGFloat = 0.45

    private weak var panel: NSWindow?
    private(set) var isShown = false
    private(set) var isDimmed = false
    private var generation = 0

    init(panel: NSWindow) {
        self.panel = panel
    }

    func setShown(_ shown: Bool) {
        guard shown != isShown, let panel else { return }
        isShown = shown
        if shown {
            if !panel.isVisible {
                panel.alphaValue = 0
                panel.orderFrontRegardless()
            }
            fade(to: targetAlpha, duration: 0.2, timing: .easeOut)
        } else {
            fade(to: 0, duration: 0.2, timing: .easeOut) { [weak self] in
                guard let self, !self.isShown else { return }
                self.panel?.orderOut(nil)
            }
        }
    }

    func setDimmed(_ dimmed: Bool) {
        guard dimmed != isDimmed else { return }
        isDimmed = dimmed
        guard isShown else { return }
        if dimmed {
            fade(to: targetAlpha, duration: 0.6, timing: .easeInEaseOut)
        } else {
            fade(to: targetAlpha, duration: 0.12, timing: .easeOut)
        }
    }

    /// Re-applies front ordering after a level or Spaces change.
    func reorderIfShown() {
        guard isShown, let panel else { return }
        panel.orderFrontRegardless()
    }

    private var targetAlpha: CGFloat { isShown ? (isDimmed ? Self.dimmedAlpha : 1) : 0 }

    private func fade(
        to alpha: CGFloat,
        duration: TimeInterval,
        timing: CAMediaTimingFunctionName,
        completion: (@MainActor () -> Void)? = nil
    ) {
        guard let panel else { return }
        generation &+= 1
        let current = generation
        NSAnimationContext.runAnimationGroup { context in
            context.duration = duration
            context.timingFunction = CAMediaTimingFunction(name: timing)
            panel.animator().alphaValue = alpha
        } completionHandler: { [weak self] in
            MainActor.assumeIsolated {
                guard let self, self.generation == current else { return }
                completion?()
            }
        }
    }
}

/// Dims the orb after 60 s of idle speed (setting "fade when idle"). Event driven: one one-shot timer for the exact
/// moment the dim is due, recomputed whenever the tier, the setting or the pointer state changes.
@MainActor
final class OrbWindowIdleDimmer {
    static let idleDelay: TimeInterval = 60
    /// Dims again this long after the pointer leaves an orb that has been idle for a while.
    static let leaveDelay: TimeInterval = 1.5

    private let model: AppModel
    private let interaction: OrbInteraction
    private let apply: (Bool) -> Void
    private let armedAt = Clock.now
    private var pointerLeftAt: TimeInterval = -.infinity
    private var wasEngaged = false
    private var timer: Timer?
    private var loop: ObservationLoop?

    init(model: AppModel, interaction: OrbInteraction, apply: @escaping (Bool) -> Void) {
        self.model = model
        self.interaction = interaction
        self.apply = apply
        loop = ObservationLoop { [weak self] in self?.evaluate() }
    }

    func cancel() {
        loop?.cancel()
        timer?.invalidate()
        timer = nil
    }

    private func evaluate() {
        let enabled = model.settings.orbFadeWhenIdle
        let idle = model.gauge.tier == .idle
        let engaged = interaction.isHovered || interaction.isDragging
        let now = Clock.now
        if engaged != wasEngaged {
            wasEngaged = engaged
            if !engaged { pointerLeftAt = now }
        }
        timer?.invalidate()
        timer = nil
        guard enabled, idle, !engaged else {
            apply(false)
            return
        }
        let lastActive = now - model.gauge.idleDuration(at: now)
        let due = max(lastActive + Self.idleDelay, armedAt + Self.idleDelay, pointerLeftAt + Self.leaveDelay)
        if now >= due {
            apply(true)
            return
        }
        apply(false)
        let timer = Timer(timeInterval: due - now, repeats: false) { [weak self] _ in
            MainActor.assumeIsolated { self?.evaluate() }
        }
        timer.tolerance = 0.1
        RunLoop.main.add(timer, forMode: .common)
        self.timer = timer
    }
}
