import SwiftUI

struct OrbParticle {
    enum Kind { case spark, ember }

    var kind: Kind
    /// Position and velocity in units of the disc diameter, relative to the disc center.
    var x: Double
    var y: Double
    var vx: Double
    var vy: Double
    var age: Double
    var life: Double
    /// Radius in points at the reference diameter of 148.
    var size: Double
    var hot: RGBColor
    var cool: RGBColor
    var seed: Double
}

/// Fixed-capacity particle pool (DESIGN section 2, layer 5). Dead particles are swap-removed, so stepping never
/// allocates.
struct OrbParticleSystem {
    static let capacity = 400

    private(set) var particles: [OrbParticle] = []
    private var sparkCarry = 0.0
    private var emberCarry = 0.0
    private var random = OrbRandom(seed: 0xA5A5_1234_9876_0F0F)

    init() {
        particles.reserveCapacity(Self.capacity)
    }

    var isEmpty: Bool { particles.isEmpty }

    mutating func removeAll() {
        particles.removeAll(keepingCapacity: true)
        sparkCarry = 0
        emberCarry = 0
    }

    struct Emission {
        var sparkRate: Double
        var emberRate: Double
        /// Gauge fraction of the leading edge (0...1).
        var lead: Double
        var heat: Double
        var overdrive: Double
        var dark: Bool
        var sizeScale: Double
        /// Angular direction of travel of the comet (radians per second, positive = clockwise).
        var spin: Double
    }

    mutating func step(dt: Double, time: Double, emission: Emission) {
        guard dt > 0 else { return }
        var index = 0
        while index < particles.count {
            particles[index].age += dt
            if particles[index].age >= particles[index].life {
                particles.swapAt(index, particles.count - 1)
                particles.removeLast()
                continue
            }
            advance(&particles[index], dt: dt, time: time)
            index += 1
        }
        sparkCarry += emission.sparkRate * dt
        while sparkCarry >= 1 {
            sparkCarry -= 1
            emitSpark(emission)
        }
        emberCarry += emission.emberRate * dt
        while emberCarry >= 1 {
            emberCarry -= 1
            emitEmber(emission)
        }
    }

    private func advance(_ particle: inout OrbParticle, dt: Double, time: Double) {
        switch particle.kind {
        case .spark:
            let drag = exp(-2.2 * dt)
            particle.vx *= drag
            particle.vy *= drag
            particle.vy += 0.05 * dt
        case .ember:
            let drag = exp(-0.9 * dt)
            let swirl = sin(time * 5.3 + particle.seed * 40) * 0.55 + sin(time * 11.7 + particle.seed * 17) * 0.25
            particle.vx = particle.vx * drag + swirl * 0.42 * dt
            particle.vy = particle.vy * drag - 0.30 * dt
        }
        particle.x += particle.vx * dt
        particle.y += particle.vy * dt
    }

    private mutating func emitSpark(_ emission: Emission) {
        guard particles.count < Self.capacity, emission.lead > 0.01 else { return }
        let fromLead = random.next() < 0.62
        let fraction = fromLead ? emission.lead - random.range(0...0.012) : random.range(0...1) * emission.lead
        let angle = OrbGeometry.angle(forFraction: max(0, fraction)) * .pi / 180
        let radius = 0.432 + random.range(-0.01...0.012)
        let outward = random.range(0.24...0.62) * (0.7 + 0.5 * emission.heat)
        let tangential = random.range(0.12...0.38) * (0.6 + 0.6 * emission.heat) * (emission.spin >= 0 ? 1 : -1)
        let segmentHeat = max(0.05, fraction)
        let heat = HeatPalette.color(segmentHeat)
        let hot = emission.dark ? HeatPalette.whiteHot : HeatPalette.amberHot
        let start = emission.dark
            ? heat.mixOKLab(hot, random.range(0.35...0.9) + 0.1 * emission.overdrive)
            : heat.mixOKLab(hot, random.range(0...0.35))
        particles.append(OrbParticle(
            kind: .spark,
            x: radius * cos(angle),
            y: radius * sin(angle),
            vx: outward * cos(angle) - tangential * sin(angle),
            vy: outward * sin(angle) + tangential * cos(angle),
            age: 0,
            life: random.range(0.5...1.2),
            size: random.range(0.6...1.4) * emission.sizeScale,
            hot: start,
            cool: emission.dark ? heat : heat.darkened(0.08),
            seed: random.next()
        ))
    }

    private mutating func emitEmber(_ emission: Emission) {
        guard particles.count < Self.capacity else { return }
        let angle = random.range(200...340) * .pi / 180
        let radius = random.range(0.40...0.49)
        let heat = HeatPalette.color(random.range(0.78...1))
        let hot = emission.dark ? HeatPalette.whiteHot : HeatPalette.amberHot
        particles.append(OrbParticle(
            kind: .ember,
            x: radius * cos(angle),
            y: radius * sin(angle),
            vx: random.range(-0.05...0.05),
            vy: -random.range(0.1...0.26),
            age: 0,
            life: random.range(0.8...1.6),
            size: random.range(0.75...1.6) * emission.sizeScale,
            hot: heat.mixOKLab(hot, random.range(0.2...0.6)),
            cool: emission.dark ? HeatPalette.color(random.range(0.76...0.86)) : HeatPalette.color(random.range(0.8...0.9)),
            seed: random.next()
        ))
    }
}
