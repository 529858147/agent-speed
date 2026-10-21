import AgentSpeedCore
import Observation
import SwiftUI

/// Per-orb animation state: frame scheduling, live events and the particle and pulse simulation.
///
/// Frames run at 60 fps while anything moves and not at all otherwise (or while the orb is hidden), so a quiet orb
/// costs no CPU and holds no GPU memory. The idle sweep runs in Core Animation (`OrbIdleSweep`).
@MainActor
@Observable
final class OrbAnimator {
    enum FrameMode: Equatable {
        case paused
        case active
    }

    struct Pulse {
        var start: TimeInterval
        var strength: Double
        var heat: Double
    }

    static let pulseDuration: TimeInterval = 0.7
    static let peakDuration: TimeInterval = 1.8
    static let sweepPeriod: TimeInterval = 6
    static let sweepDuration: TimeInterval = 2.2

    private(set) var frameMode: FrameMode = .active
    /// Bumps on every celebrated new peak.
    private(set) var peakCount = 0

    @ObservationIgnored let style: OrbStyle
    @ObservationIgnored private(set) var particles = OrbParticleSystem()
    @ObservationIgnored private(set) var pulses: [Pulse] = []
    @ObservationIgnored private(set) var peakStart: TimeInterval = -.infinity
    @ObservationIgnored private(set) var cometPhase = 0.0
    @ObservationIgnored private(set) var breathPhase = 0.0
    @ObservationIgnored private(set) var flare = OrbEnvelope()
    @ObservationIgnored private(set) var cometVisibility = OrbEnvelope()
    @ObservationIgnored private(set) var redline = OrbEnvelope()
    @ObservationIgnored private(set) var hover = OrbEnvelope()
    @ObservationIgnored private(set) var activity = OrbEnvelope()
    @ObservationIgnored private(set) var valueGlow = OrbEnvelope()
    @ObservationIgnored private(set) var skew = OrbEnvelope()
    @ObservationIgnored var readout = OrbRollingReadout()
    @ObservationIgnored let textCache = OrbTextCache()
    @ObservationIgnored private var cachedGeometry: OrbGeometry?

    @ObservationIgnored private weak var model: AppModel?
    @ObservationIgnored private weak var interaction: OrbInteraction?
    @ObservationIgnored private var listener: UUID?
    @ObservationIgnored private var loop: ObservationLoop?
    @ObservationIgnored private var housekeeping: Timer?
    @ObservationIgnored private var lastStep: TimeInterval?
    @ObservationIgnored private var hoverChangedAt: TimeInterval = -.infinity
    @ObservationIgnored private var readoutChangedAt: TimeInterval = -.infinity
    @ObservationIgnored private var createdAt = Clock.now
    @ObservationIgnored private var envelopeTargets = EnvelopeTargets()

    private struct EnvelopeTargets: Equatable {
        var comet = 0.0
        var redline = 0.0
        var hover = 0.0
        var activity = 0.0
        var glow = 0.0
        var skew = 0.0
    }

    init(style: OrbStyle) {
        self.style = style
    }

    var isAttached: Bool { model != nil }

    /// Whether the pointer is over the floating orb's disc.
    var hoverTarget: Bool { style == .floating && (interaction?.isHovered ?? false) }

    // MARK: Lifecycle

    func attach(model: AppModel, interaction: OrbInteraction?) {
        guard self.model == nil else { return }
        self.model = model
        self.interaction = interaction
        let value = model.gauge.readoutValue
        readout.reset(SpeedFormat.speed(value), value: value)
        prime(tier: model.gauge.tier, effects: effects(for: model))
        listener = model.store.addEventListener { [weak self] event in
            self?.handle(event)
        }
        var lastReadout = value
        var lastHover = interaction?.isHovered ?? false
        loop = ObservationLoop { [weak self] in
            guard let self, let model = self.model else { return }
            let gauge = model.gauge
            _ = (gauge.revision, gauge.tier, gauge.title, model.settings.effects, model.accessibility.reduceMotion)
            if gauge.readoutValue != lastReadout {
                lastReadout = gauge.readoutValue
                self.readoutChangedAt = Clock.now
            }
            let hovered = self.interaction?.isHovered ?? false
            if hovered != lastHover {
                lastHover = hovered
                self.hoverChangedAt = Clock.now
            }
            _ = self.interaction?.isVisibleOnScreen
            self.reevaluate()
        }
    }

    func detach() {
        if let listener { model?.store.removeEventListener(listener) }
        listener = nil
        loop?.cancel()
        loop = nil
        housekeeping?.invalidate()
        housekeeping = nil
        model = nil
        interaction = nil
    }

    // MARK: Events

    func effects(for model: AppModel) -> OrbEffects {
        OrbEffects(level: model.settings.effects, reduceMotion: model.accessibility.reduceMotion, style: style)
    }

    private func handle(_ event: SpeedEvent) {
        guard let model else { return }
        let now = Clock.now
        let effects = effects(for: model)
        switch event {
        case .responseCompleted(let response):
            guard model.settings.responsePulses, effects.pulses else { return }
            let output = max(1, Double(response.outputTokens))
            let strength = (log10(output) / 3.4).clamped(to: 0.3...1)
            let speed = response.speed ?? model.gauge.smoothedValue(at: now)
            let heat = HeatPalette.heat(value: speed, redline: model.settings.redline(for: .output))
            addPulse(at: now, strength: strength, heat: max(heat, 0.08))
        case .newPeak:
            guard model.settings.celebratePeaks, effects.celebrate else { return }
            celebrate(at: now)
        }
        reevaluate()
    }

    /// Coalesces pulses to at most one every 150 ms.
    func addPulse(at time: TimeInterval, strength: Double, heat: Double) {
        if let last = pulses.indices.last, time - pulses[last].start < 0.15 {
            pulses[last].strength = max(pulses[last].strength, strength)
            pulses[last].heat = max(pulses[last].heat, heat)
            return
        }
        pulses.append(Pulse(start: time, strength: strength, heat: heat))
        if pulses.count > 6 { pulses.removeFirst(pulses.count - 6) }
        flare.step(toward: max(flare.value, strength), dt: 1, timeConstant: 0)
    }

    func celebrate(at time: TimeInterval) {
        peakStart = time
        peakCount &+= 1
    }

    // MARK: Simulation

    struct StepInput {
        var heat: Double
        var overdrive: Double
        var tier: SpeedTier
        var effects: OrbEffects
        var dark: Bool
        var hovered: Bool
    }

    /// Advances the simulation to `time`. Called once per rendered frame.
    func step(to time: TimeInterval, input: StepInput) {
        let dt = (time - (lastStep ?? time)).clamped(to: 0...(1.0 / 15))
        lastStep = time
        guard dt > 0 else { return }
        let h = input.heat
        let effects = input.effects

        if effects.comet { cometPhase = (cometPhase + Self.lerp(1 / 6, 1 / 0.7, h) * dt).truncatingRemainder(dividingBy: 1) }
        if effects.breathing { breathPhase = (breathPhase + Self.lerp(0.25, 1 / 0.7, h) * dt).truncatingRemainder(dividingBy: 1) }

        let targets = EnvelopeTargets(
            comet: input.tier != .idle && effects.comet ? 1 : 0,
            redline: input.tier == .redline ? 1 : 0,
            hover: input.hovered ? 1 : 0,
            activity: input.tier == .idle ? 0 : 1,
            glow: input.tier >= .boost ? 1 : 0,
            skew: input.tier >= .overdrive && effects.skew ? 1 : 0
        )
        envelopeTargets = targets
        cometVisibility.step(toward: targets.comet, dt: dt, timeConstant: 0.28)
        redline.step(toward: targets.redline, dt: dt, timeConstant: 0.2)
        hover.step(toward: targets.hover, dt: dt, timeConstant: effects.reduceMotion ? 0.06 : 0.09)
        flare.step(toward: 0, dt: dt, timeConstant: 0.28)
        activity.step(toward: targets.activity, dt: dt, timeConstant: 0.18)
        valueGlow.step(toward: targets.glow, dt: dt, timeConstant: 0.22)
        skew.step(toward: targets.skew, dt: dt, timeConstant: 0.3)

        pulses.removeAll { time - $0.start > Self.pulseDuration }

        let rate = effects.particleRate
        let sparkRate = effects.particles && h >= 0.45 ? 120 * ((h - 0.45) / 0.55) * rate : 0
        let emberRate = effects.particles && input.tier == .redline ? 90 * max(input.overdrive, 0.3) * rate : 0
        if sparkRate > 0 || emberRate > 0 || !particles.isEmpty {
            particles.step(dt: dt, time: time, emission: .init(
                sparkRate: sparkRate,
                emberRate: emberRate,
                lead: h,
                heat: h,
                overdrive: input.overdrive,
                dark: input.dark,
                sizeScale: input.dark ? 1 : 1.2,
                spin: 1
            ))
        }
    }

    func geometry(diameter: CGFloat, side: CGFloat) -> OrbGeometry {
        if let cachedGeometry, cachedGeometry.diameter == diameter, cachedGeometry.center.x == side / 2 {
            return cachedGeometry
        }
        let geometry = OrbGeometry(diameter: diameter, canvasSide: side)
        cachedGeometry = geometry
        return geometry
    }

    /// Prepares an unattached animator to draw a still frame of the current state.
    func primeForStill(model: AppModel) {
        let value = model.gauge.readoutValue
        readout.reset(SpeedFormat.speed(value), value: value)
        prime(tier: model.gauge.tier, effects: effects(for: model))
    }

    /// Starts the envelopes at their resting values so a freshly shown orb doesn't fade in its glow.
    private func prime(tier: SpeedTier, effects: OrbEffects) {
        activity = OrbEnvelope(tier == .idle ? 0 : 1)
        valueGlow = OrbEnvelope(tier >= .boost ? 1 : 0)
        skew = OrbEnvelope(tier >= .overdrive && effects.skew ? 1 : 0)
        redline = OrbEnvelope(tier == .redline ? 1 : 0)
        cometVisibility = OrbEnvelope(tier != .idle && effects.comet ? 1 : 0)
    }

    /// Runs the simulation forward without rendering (previews).
    func prewarm(from start: TimeInterval, to end: TimeInterval, input: StepInput) {
        lastStep = start
        var time = start
        while time < end {
            time = min(end, time + 1.0 / 60)
            step(to: time, input: input)
        }
    }

    /// Gauge sweep progress after a new peak (0 → 1 over 0.5 s), or 1 when none.
    func peakSweep(at time: TimeInterval) -> Double {
        let t = time - peakStart
        guard t >= 0, t < 0.5 else { return 1 }
        return Easing.easeOut(t / 0.5)
    }

    /// When the idle sweep cycle starts: a few seconds after the orb settled into idle.
    func idleEpoch(for gauge: LiveGauge) -> TimeInterval {
        let since = gauge.tierChangedAt.isFinite ? gauge.tierChangedAt : createdAt
        return since + 2.5
    }

    // MARK: Scheduling

    private func reevaluate() {
        guard let model else { return }
        let now = Clock.now
        let mode = desiredMode(model: model, at: now)
        if frameMode != mode { frameMode = mode }
        if mode == .paused {
            housekeeping?.invalidate()
            housekeeping = nil
        } else {
            guard housekeeping == nil else { return }
            let timer = Timer(timeInterval: 0.25, repeats: true) { [weak self] _ in
                MainActor.assumeIsolated { self?.reevaluate() }
            }
            timer.tolerance = 0.05
            RunLoop.main.add(timer, forMode: .common)
            housekeeping = timer
        }
    }

    private func desiredMode(model: AppModel, at now: TimeInterval) -> FrameMode {
        if let interaction, !interaction.isVisibleOnScreen { return .paused }
        let gauge = model.gauge
        let effects = effects(for: model)
        let busy = !gauge.isSettled(at: now)
            || (gauge.tier != .idle && (effects.ambient || effects.shimmer))
            || !particles.isEmpty
            || !pulses.isEmpty
            || now - peakStart < Self.peakDuration + 0.4
            || now - gauge.tierChangedAt < 0.9
            || now - gauge.titleChangedAt < OrbReadoutBuilder.titleSlide + 0.1
            || now - hoverChangedAt < 0.5
            || flare.value > 0.002
            || !cometVisibility.isSettled(at: envelopeTargets.comet)
            || !redline.isSettled(at: envelopeTargets.redline)
            || !hover.isSettled(at: envelopeTargets.hover)
            || !activity.isSettled(at: envelopeTargets.activity)
            || !valueGlow.isSettled(at: envelopeTargets.glow)
            || !skew.isSettled(at: envelopeTargets.skew)
            || now - readoutChangedAt < OrbRollingReadout.duration + 0.05
        return busy ? .active : .paused
    }

    static func lerp(_ a: Double, _ b: Double, _ t: Double) -> Double {
        a + (b - a) * t.clamped(to: 0...1)
    }
}
