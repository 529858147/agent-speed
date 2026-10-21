import Foundation

enum SpeedTier: Int, CaseIterable, Comparable, Sendable {
    case idle, cruise, boost, overdrive, redline

    /// Entry threshold as a fraction of the redline speed (cruise uses an absolute 0.5 tok/s).
    var threshold: Double {
        switch self {
        case .idle: 0
        case .cruise: 0
        case .boost: 0.15
        case .overdrive: 0.375
        case .redline: 0.75
        }
    }

    func enterSpeed(redline: Double) -> Double {
        self == .cruise ? SpeedTier.cruiseSpeed : threshold * redline
    }

    var titleKey: String {
        switch self {
        case .idle: "tier.idle"
        case .cruise: "tier.cruise"
        case .boost: "tier.boost"
        case .overdrive: "tier.overdrive"
        case .redline: "tier.redline"
        }
    }

    var next: SpeedTier? { SpeedTier(rawValue: rawValue + 1) }
    var previous: SpeedTier? { SpeedTier(rawValue: rawValue - 1) }

    static let cruiseSpeed = 0.5

    static func < (lhs: SpeedTier, rhs: SpeedTier) -> Bool { lhs.rawValue < rhs.rawValue }
}

/// Tier selection with hysteresis (enter at the threshold, leave at 90 %), a 3 s idle delay and a 0.8 s hold.
struct TierTracker {
    static let holdDuration: TimeInterval = 0.8
    static let idleDelay: TimeInterval = 3
    static let exitRatio = 0.9

    private(set) var shown: SpeedTier = .idle
    private var candidate: SpeedTier = .idle
    private var candidateSince: TimeInterval = 0
    private var belowCruiseSince: TimeInterval?

    /// Feeds a raw value; returns true when the shown tier changed.
    mutating func update(value: Double, redline: Double, now: TimeInterval) -> Bool {
        let next = rawTier(value: value, redline: redline, now: now)
        if next != candidate {
            candidate = next
            candidateSince = now
        }
        guard candidate != shown, now - candidateSince >= Self.holdDuration else { return false }
        shown = candidate
        return true
    }

    /// Next moment a re-evaluation could change the shown tier without new data.
    func nextDecisionTime(after now: TimeInterval) -> TimeInterval? {
        var times: [TimeInterval] = []
        if candidate != shown { times.append(candidateSince + Self.holdDuration) }
        if let since = belowCruiseSince, candidate != .idle { times.append(since + Self.idleDelay) }
        return times.filter { $0 > now }.min()
    }

    mutating func reset(to tier: SpeedTier = .idle) {
        shown = tier
        candidate = tier
        belowCruiseSince = nil
    }

    private mutating func rawTier(value: Double, redline: Double, now: TimeInterval) -> SpeedTier {
        if value < SpeedTier.cruiseSpeed {
            if belowCruiseSince == nil { belowCruiseSince = now }
        } else {
            belowCruiseSince = nil
        }
        if let since = belowCruiseSince, now - since >= Self.idleDelay { return .idle }

        var tier = candidate == .idle ? (value >= SpeedTier.cruiseSpeed ? .cruise : .idle) : candidate
        guard tier != .idle else { return .idle }
        while let up = tier.next, value >= up.enterSpeed(redline: redline) { tier = up }
        while tier > .cruise, value < tier.enterSpeed(redline: redline) * Self.exitRatio, let down = tier.previous {
            tier = down
        }
        return tier
    }
}
