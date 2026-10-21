import AgentSpeedCore
import Foundation
import Observation

/// The live speed reduced to what every surface draws: smoothed heat, overdrive and value, and the shown tier.
@MainActor
@Observable
final class LiveGauge {
    private(set) var tier: SpeedTier = .idle
    private(set) var previousTier: SpeedTier = .idle
    /// Value shown in text readouts, refreshed at most 4 times per second.
    private(set) var readoutValue: Double = 0
    /// Bumps on every ingest so paused renderers redraw.
    private(set) var revision = 0

    @ObservationIgnored private(set) var tierChangedAt: TimeInterval = -.infinity
    @ObservationIgnored private(set) var rawValue: Double = 0
    @ObservationIgnored private(set) var lastActiveAt: TimeInterval = -.infinity
    @ObservationIgnored private var heatSpring = SpringValue()
    @ObservationIgnored private var overdriveSpring = SpringValue()
    @ObservationIgnored private var valueSpring = SpringValue(omega: 9)
    @ObservationIgnored private var tracker = TierTracker()
    @ObservationIgnored private var readoutUpdatedAt: TimeInterval = -.infinity
    @ObservationIgnored private var pendingReadout: Timer?
    @ObservationIgnored private var decisionTimer: Timer?

    @ObservationIgnored var redline: Double = 400 {
        didSet { if redline != oldValue { ingest(rawValue, now: Clock.now) } }
    }

    static let readoutInterval: TimeInterval = 0.25

    init() {}

    func ingest(_ value: Double, now: TimeInterval) {
        let value = value.isFinite ? max(0, value) : 0
        rawValue = value
        if value >= SpeedTier.cruiseSpeed { lastActiveAt = now }
        heatSpring.retarget(HeatPalette.heat(value: value, redline: redline), at: now)
        overdriveSpring.retarget(HeatPalette.overdrive(value: value, redline: redline), at: now)
        valueSpring.retarget(value, at: now)
        evaluateTier(now: now)
        scheduleReadout(now: now)
        revision &+= 1
    }

    /// Jumps straight to a value without animation (previews, first snapshot).
    func snap(to value: Double, tier: SpeedTier? = nil, now: TimeInterval) {
        rawValue = value
        heatSpring.snap(to: HeatPalette.heat(value: value, redline: redline), at: now)
        overdriveSpring.snap(to: HeatPalette.overdrive(value: value, redline: redline), at: now)
        valueSpring.snap(to: value, at: now)
        let resolved = tier ?? Self.tier(for: value, redline: redline)
        tracker.reset(to: resolved)
        previousTier = resolved
        self.tier = resolved
        tierChangedAt = -.infinity
        readoutValue = value
        if value >= SpeedTier.cruiseSpeed { lastActiveAt = now }
        revision &+= 1
    }

    func heat(at time: TimeInterval) -> Double { heatSpring.value(at: time).clamped(to: 0...1) }
    func overdrive(at time: TimeInterval) -> Double { overdriveSpring.value(at: time).clamped(to: 0...1) }
    func smoothedValue(at time: TimeInterval) -> Double { max(0, valueSpring.value(at: time)) }
    var targetHeat: Double { heatSpring.target }

    func isSettled(at time: TimeInterval) -> Bool {
        heatSpring.isSettled(at: time) && overdriveSpring.isSettled(at: time)
    }

    /// Seconds since the speed last reached the cruise threshold.
    func idleDuration(at time: TimeInterval) -> TimeInterval { time - lastActiveAt }

    static func tier(for value: Double, redline: Double) -> SpeedTier {
        guard value >= SpeedTier.cruiseSpeed else { return .idle }
        return SpeedTier.allCases.last { $0 != .idle && value >= $0.enterSpeed(redline: redline) } ?? .cruise
    }

    private func evaluateTier(now: TimeInterval) {
        if tracker.update(value: rawValue, redline: redline, now: now) {
            previousTier = tier
            tier = tracker.shown
            tierChangedAt = now
        }
        decisionTimer?.invalidate()
        decisionTimer = nil
        guard let next = tracker.nextDecisionTime(after: now) else { return }
        let timer = Timer(timeInterval: max(0.02, next - now + 0.01), repeats: false) { [weak self] _ in
            MainActor.assumeIsolated {
                guard let self else { return }
                self.evaluateTier(now: Clock.now)
            }
        }
        RunLoop.main.add(timer, forMode: .common)
        decisionTimer = timer
    }

    private func scheduleReadout(now: TimeInterval) {
        let elapsed = now - readoutUpdatedAt
        if elapsed >= Self.readoutInterval {
            pendingReadout?.invalidate()
            pendingReadout = nil
            publishReadout(now: now)
            return
        }
        guard pendingReadout == nil else { return }
        let timer = Timer(timeInterval: Self.readoutInterval - elapsed, repeats: false) { [weak self] _ in
            MainActor.assumeIsolated {
                guard let self else { return }
                self.pendingReadout = nil
                self.publishReadout(now: Clock.now)
            }
        }
        RunLoop.main.add(timer, forMode: .common)
        pendingReadout = timer
    }

    private func publishReadout(now: TimeInterval) {
        readoutUpdatedAt = now
        if readoutValue != rawValue { readoutValue = rawValue }
    }
}
