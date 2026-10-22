import AgentSpeedCore
import AgentSpeedOrbModel
import SwiftUI

/// `led` — the LED gauge (DESIGN 2 layers 3–5, 2a.1): 36 segments lit along a 270° arc in the heat color of their own
/// position, tier tick dots, the comet, sparks and embers. Also the dashboard hero's gauge.
@MainActor
final class LedStyle: OrbStyleRenderer {
    let kind = OrbStyleKind.led
    let readoutLayout = OrbReadoutLayout.led

    private(set) var particles = OrbParticleSystem()
    private(set) var cometPhase = 0.0
    private var cometVisibility = OrbEnvelope()
    private var cometTarget = 0.0

    init() {}

    // MARK: Simulation

    func step(_ frame: OrbFrame) {
        let dt = frame.dt
        guard dt > 0 else { return }
        if frame.resumed {
            particles.removeAll()
            cometTarget = !frame.isIdle && frame.effects.comet && !frame.unavailable ? 1 : 0
            cometVisibility = OrbEnvelope(cometTarget)
        }
        let h = frame.instrumentHeat
        let effects = frame.effects
        if effects.comet { cometPhase = (cometPhase + Self.lerp(1 / 6, 1 / 0.7, h) * dt).truncatingRemainder(dividingBy: 1) }
        cometTarget = !frame.isIdle && effects.comet && !frame.unavailable ? 1 : 0
        cometVisibility.step(toward: cometTarget, dt: dt, timeConstant: 0.28)

        let lod = frame.isSmall ? 0.5 : 1
        let rate = effects.particleRate * lod
        let sparkRate = effects.particles && h >= 0.45 ? 120 * ((h - 0.45) / 0.55) * rate : 0
        let emberRate = effects.particles && frame.tier == .redline ? 90 * max(frame.overdrive, 0.3) * rate : 0
        if sparkRate > 0 || emberRate > 0 || !particles.isEmpty {
            particles.step(dt: dt, time: frame.time, emission: .init(
                sparkRate: sparkRate,
                emberRate: emberRate,
                lead: h,
                heat: h,
                overdrive: frame.overdrive,
                dark: frame.dark,
                sizeScale: frame.dark ? 1 : 1.2,
                spin: 1
            ))
        }
    }

    func isBusy(_ frame: OrbFrame) -> Bool {
        !particles.isEmpty || !cometVisibility.isSettled(at: cometTarget)
    }

    func ambientFrameClass(_ frame: OrbFrame) -> OrbFrameClass {
        if frame.isIdle || frame.unavailable { return particles.isEmpty ? .none : .fps30 }
        return frame.instrumentHeat >= 0.45 || frame.tier >= .overdrive ? .fps60 : .fps30
    }

    // MARK: Layers

    func build(into scene: inout OrbScene, _ frame: OrbFrame) {
        if !frame.content.isThumbnail { addHeatHaze(&scene, frame) }
        addSegments(&scene, frame)
        guard !frame.content.valueOnly else { return }
        addTicks(&scene, frame)
        addComet(&scene, frame)
        addParticles(&scene, frame)
    }

    /// Max effects at redline: faint rising heat waves above the ring.
    private func addHeatHaze(_ scene: inout OrbScene, _ frame: OrbFrame) {
        guard frame.effects.heatHaze, frame.redlineAmount > 0.01 else { return }
        let g = frame.geometry
        let color = frame.dark ? HeatPalette.whiteHot : HeatPalette.amberHot
        let strands = 9
        for index in 0..<strands {
            let seed = Noise.hash(index, 91)
            let offset = (Double(index) / Double(strands - 1) - 0.5) * 0.62
            let x0 = g.center.x + offset * frame.s
            let top = g.center.y - 0.74 * frame.s
            let base = g.center.y - sqrt(max(0, 0.25 - offset * offset)) * frame.s - 2
            guard base > top + 4 else { continue }
            let flicker = 0.5 + 0.5 * sin(frame.time * (5 + seed * 3) + seed * 20)
            let alpha = (frame.dark ? 0.08 : 0.1) * frame.redlineAmount * (0.4 + 0.6 * flicker)
            let steps = 6
            var previous = CGPoint(x: x0, y: base)
            for step in 1...steps {
                let v = Double(step) / Double(steps)
                let y = base - (base - top) * v
                let wave = sin(frame.time * (3.1 + seed * 2) + v * 9 + seed * 12) * 0.018 * frame.s * v
                let point = CGPoint(x: x0 + wave, y: y)
                let fade = 1 - (v - 1 / Double(steps))
                scene.add(.capsule(from: previous, to: point, width: 1.4 * frame.unit, color: color.opacity(alpha * fade),
                                   tailColor: color.opacity(alpha * (fade + 1 / Double(steps)))), blend: frame.glowBlend)
                previous = point
            }
        }
    }

    private struct SegmentLight {
        var brightness: Double
        var whiteness: Double
    }

    /// Brightness of each segment (the last lit one partially) and its share of white-hot near the leading edge.
    private static func light(_ index: Int, lit: Double) -> SegmentLight {
        let brightness = (lit - Double(index)).clamped(to: 0...1)
        guard brightness > 0 else { return SegmentLight(brightness: 0, whiteness: 0) }
        let distance = lit - Double(index + 1)
        let whiteness = distance < 0 ? 1 : (1 - distance).clamped(to: 0...1)
        return SegmentLight(brightness: brightness, whiteness: whiteness)
    }

    /// Segment `i` takes the heat color of its own position (`i / 35`); the first few are lifted off the idle slate
    /// so a lit segment never reads as unlit.
    private static func segmentColor(_ index: Int, _ frame: OrbFrame) -> RGBColor {
        let h = max(0.07, Double(index) / Double(OrbGeometry.segmentCount - 1))
        return frame.dark ? HeatPalette.color(h) : HeatPalette.color(h).darkened(0.06)
    }

    private static func shimmer(_ index: Int, _ frame: OrbFrame) -> Double {
        guard frame.effects.shimmer, frame.redlineAmount > 0.001 else { return 1 }
        let tick = Int((frame.time * 12).rounded(.down))
        let random = 0.75 + 0.25 * Noise.hash(index, tick)
        return 1 - (1 - random) * frame.redlineAmount
    }

    private static func segment(_ index: Int, _ frame: OrbFrame, color: RGBColor, core: Double = 1, glow: CGFloat = 0,
                                glowAlpha: Double = 0) -> OrbInstance {
        let g = frame.geometry
        let angle = OrbGeometry.segmentAngle(index)
        return .roundedRect(
            center: g.point(angle: angle, radius: g.gaugeRadius),
            halfSize: CGSize(width: g.segmentLength / 2, height: g.segmentWidth / 2),
            rotation: angle * .pi / 180,
            corner: g.segmentCorner,
            color: color,
            core: core,
            glow: glow,
            glowAlpha: glowAlpha
        )
    }

    /// The idle sign of life: a band of light travelling along the unlit segments (in step with `needle`).
    private static func sweepBoost(_ index: Int, _ frame: OrbFrame) -> Double {
        guard let u = frame.signOfLife else { return 0 }
        let position = Double(index) / Double(OrbGeometry.segmentCount - 1)
        let d = position - (u * 1.3 - 0.15)
        if frame.effects.reduceMotion { return (frame.dark ? 0.1 : 0.06) * sin(.pi * u) }
        return (frame.dark ? 0.16 : 0.1) * exp(-d * d * 90)
    }

    private func addSegments(_ scene: inout OrbScene, _ frame: OrbFrame) {
        let lit = (frame.unavailable ? 0 : frame.displayHeat) * Double(OrbGeometry.segmentCount)
        let bloom = frame.effects.bloom
        let glowRadius = 0.02 * frame.s * bloom
        let thumbnail = frame.content.isThumbnail

        if !thumbnail {
            // Bloom under the lit segments, white-hot at the leading edge.
            for index in 0..<OrbGeometry.segmentCount {
                let light = Self.light(index, lit: lit)
                guard light.brightness > 0.01 else { continue }
                var color = Self.segmentColor(index, frame)
                if light.whiteness > 0 {
                    color = color.mixOKLab(frame.tokens.leadingSegment(Double(index) / 35, overdrive: frame.overdrive), light.whiteness * 0.8)
                }
                let base = (frame.dark ? 0.3 : 0.2) + 0.45 * light.whiteness
                let alpha = light.brightness * base * Self.shimmer(index, frame) * (1 + 0.5 * frame.flash)
                scene.add(Self.segment(index, frame, color: color.opacity(min(1, alpha * bloom)), core: 0,
                                       glow: glowRadius * (1 + 0.7 * light.whiteness), glowAlpha: 0.6), blend: frame.glowBlend)
            }
            if frame.redlineAmount > 0.01 {
                let pulse = 0.55 + 0.45 * sin(2 * .pi * frame.breathPhase)
                let color = HeatPalette.highlight(frame.heat, overdrive: frame.overdrive, dark: frame.dark)
                let end = OrbGeometry.angle(forFraction: frame.displayHeat)
                scene.add(.arc(center: frame.center, radius: 0.447 * frame.s, width: 0.012 * frame.s,
                               start: OrbGeometry.startAngle, sweep: end - OrbGeometry.startAngle,
                               color: color.opacity(0.5 * frame.redlineAmount * pulse * bloom),
                               tailColor: color.opacity(0.5 * frame.redlineAmount * pulse * bloom),
                               glow: 0.02 * frame.s, glowAlpha: 0.8), blend: frame.glowBlend)
            }
        }

        // Crisp segments.
        let unlit = frame.content.increaseContrast ? frame.tokens.unlitSegment.opacity(1.8) : frame.tokens.unlitSegment
        for index in 0..<OrbGeometry.segmentCount {
            let light = Self.light(index, lit: lit)
            if light.brightness < 1 {
                let boost = Self.sweepBoost(index, frame)
                let color = boost > 0 ? unlit.opacity(1 + boost / max(unlit.alpha, 0.01)) : unlit
                scene.add(Self.segment(index, frame, color: color), blend: .over)
            }
            guard light.brightness > 0 else { continue }
            var color = Self.segmentColor(index, frame).scaled(Self.shimmer(index, frame))
            if light.whiteness > 0 {
                color = color.mixOKLab(frame.tokens.leadingSegment(Double(index) / 35, overdrive: frame.overdrive), light.whiteness)
            }
            if frame.flash > 0 { color = color.mix(.white, 0.25 * frame.flash) }
            scene.add(Self.segment(index, frame, color: color.opacity(light.brightness)), blend: .over)
        }
    }

    private static let tickTiers: [SpeedTier] = [.cruise, .boost, .overdrive, .redline]

    private func addTicks(_ scene: inout OrbScene, _ frame: OrbFrame) {
        let g = frame.geometry
        let idle = frame.dark
            ? RGBColor.white.opacity(frame.content.increaseContrast ? 0.5 : 0.26)
            : RGBColor.black.opacity(frame.content.increaseContrast ? 0.45 : 0.22)
        let shown = frame.unavailable ? 0 : frame.displayHeat
        let redline = max(frame.redline, 1)
        for tier in Self.tickTiers {
            let fraction = HeatPalette.heat(forFraction: tier.enterSpeed(redline: redline) / redline)
            let passed = shown >= fraction
            let color = passed ? frame.tokens.heat(max(fraction, 0.1)) : idle
            scene.add(.circle(center: g.point(angle: OrbGeometry.angle(forFraction: fraction), radius: g.tickRadius),
                              radius: 0.0085 * frame.s, color: color, glow: passed && frame.dark ? 1.5 * frame.unit : 0,
                              glowAlpha: 0.5), blend: .over)
        }
    }

    /// Dims the comet while it passes the gap at the bottom of the gauge.
    private static func cometGapFade(_ angle: Double) -> Double {
        var delta = (angle - 90).truncatingRemainder(dividingBy: 360)
        if delta > 180 { delta -= 360 }
        if delta < -180 { delta += 360 }
        return 0.3 + 0.7 * Easing.smoothstep(18, 52, abs(delta))
    }

    private func addComet(_ scene: inout OrbScene, _ frame: OrbFrame) {
        let visibility = cometVisibility.value
        guard visibility > 0.01 else { return }
        let g = frame.geometry
        let headAngle = OrbGeometry.startAngle + 360 * cometPhase
        let alpha = visibility * (0.25 + 0.6 * frame.heat) * Self.cometGapFade(headAngle)
        let heat = frame.tokens.heat(max(frame.heat, 0.1))
        let hot = frame.dark ? HeatPalette.whiteHot : heat
        scene.add(.arc(center: g.center, radius: g.cometRadius, width: 0.0125 * frame.s, start: headAngle - 40, sweep: 40,
                       color: hot.opacity(alpha), tailColor: heat.opacity(alpha * 0.6),
                       glow: 0.012 * frame.s * frame.effects.bloom, glowAlpha: 0.55), blend: frame.glowBlend)
        scene.add(.circle(center: g.point(angle: headAngle, radius: g.cometRadius), radius: 0.0085 * frame.s,
                          color: hot.opacity(alpha), glow: 0.022 * frame.s * frame.effects.bloom, glowAlpha: 0.8),
                  blend: frame.glowBlend)
    }

    private func addParticles(_ scene: inout OrbScene, _ frame: OrbFrame) {
        guard !particles.isEmpty else { return }
        let g = frame.geometry
        for particle in particles.particles {
            let life = particle.age / particle.life
            let color = particle.hot.mix(particle.cool, min(1, life * 1.3))
            let center = CGPoint(x: g.center.x + particle.x * frame.s, y: g.center.y + particle.y * frame.s)
            let zone = frame.readoutZoneFade(at: center)
            switch particle.kind {
            case .spark:
                let alpha = pow(1 - life, 1.25) * zone
                let radius = particle.size * frame.unit * (1 - 0.45 * life)
                let streak = 0.045
                let tail = CGPoint(x: center.x - particle.vx * streak * frame.s, y: center.y - particle.vy * streak * frame.s)
                scene.add(.capsule(from: tail, to: center, width: radius * 1.3, color: color.opacity(alpha),
                                   tailColor: color.opacity(alpha * 0.35), glow: frame.dark ? radius * 1.4 : 0,
                                   glowAlpha: 0.45), blend: frame.glowBlend)
            case .ember:
                let flicker = 0.7 + 0.3 * sin(frame.time * 23 + particle.seed * 50)
                let alpha = pow(sin(.pi * min(1, life)), 0.7) * flicker * zone
                let radius = particle.size * frame.unit * (1 - 0.35 * life)
                scene.add(.circle(center: center, radius: radius, color: color.opacity(alpha),
                                  glow: radius * (frame.dark ? 1.9 : 1.2), glowAlpha: frame.dark ? 0.5 : 0.3),
                          blend: frame.glowBlend)
            }
        }
    }

    static func lerp(_ a: Double, _ b: Double, _ t: Double) -> Double {
        a + (b - a) * t.clamped(to: 0...1)
    }
}
