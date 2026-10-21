import Foundation

/// What a given effects level allows (DESIGN section 2, Effects intensity), after Reduce Motion and the
/// embedded cap.
struct OrbEffects: Equatable {
    let level: EffectsLevel
    let reduceMotion: Bool

    init(level: EffectsLevel, reduceMotion: Bool, style: OrbStyle) {
        var level = reduceMotion ? min(level, .soft) : level
        if style == .embedded { level = min(level, .soft) }
        self.level = level
        self.reduceMotion = reduceMotion
    }

    var ambient: Bool { level >= .soft }
    var breathing: Bool { level >= .soft }
    var comet: Bool { level >= .soft && !reduceMotion }
    var pulses: Bool { level >= .soft }
    var particles: Bool { level >= .standard }
    var skew: Bool { level >= .standard }
    var jitter: Bool { level >= .standard }
    var shimmer: Bool { level >= .soft && !reduceMotion }
    var flash: Bool { level >= .soft }
    var idleSweep: Bool { level >= .soft }
    var heatHaze: Bool { level == .max }
    var celebrate: Bool { level >= .soft }
    var particleRate: Double { level == .max ? 1.6 : 1 }
    var bloom: Double { level == .max ? 1.35 : (level == .off ? 0.7 : 1) }
}

/// Exponential approach toward a target, stepped per frame.
struct OrbEnvelope {
    private(set) var value: Double

    init(_ value: Double = 0) {
        self.value = value
    }

    mutating func step(toward target: Double, dt: Double, timeConstant: Double) {
        guard timeConstant > 0 else {
            value = target
            return
        }
        value += (target - value) * (1 - exp(-dt / timeConstant))
        if abs(target - value) < 0.0005 { value = target }
    }

    func isSettled(at target: Double) -> Bool { abs(value - target) < 0.001 }
}
