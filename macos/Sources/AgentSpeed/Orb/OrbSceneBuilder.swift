import AgentSpeedCore
import SwiftUI

/// Values the renderer needs that come from observable state (resolved in the view body so changes redraw).
struct OrbContent {
    var diameter: CGFloat
    var tokens: OrbTokens
    var increaseContrast: Bool
    var unit: String
    /// The speed title (DESIGN section 1) and the one it replaces while the label slides.
    var titleLabel: String
    var previousTitleLabel: String
    var infoLine: String
    var peakLabel: String
    var latinTracking: Bool
    var language: AppLanguage
    var generating: [SourceKind]
    /// The readout is drawn only on the floating orb; the dashboard shows its own numbers.
    var showsReadout: Bool
}

/// One frame of the orb, derived from the gauge, the animator and the content at a time.
struct OrbFrame {
    let time: TimeInterval
    let geometry: OrbGeometry
    let content: OrbContent
    let effects: OrbEffects
    let heat: Double
    let displayHeat: Double
    let overdrive: Double
    let tier: SpeedTier
    let titleChangedAt: TimeInterval
    let flash: Double
    let breath: Double
    let breathPhase: Double
    let redlineAmount: Double

    var tokens: OrbTokens { content.tokens }
    var dark: Bool { content.tokens.isDark }
    var s: CGFloat { geometry.diameter }
    /// Scale relative to the default 148 pt orb, for point-sized details.
    var unit: CGFloat { geometry.diameter / 148 }
    /// How far (in diameters from the center) the aura and pulses reach; the embedded orb stays inside its margins.
    var reach: CGFloat { content.showsReadout ? 0.75 : 0.66 }
    /// Light elements add up on dark glass and blend normally on porcelain.
    var glowBlend: OrbBatch.Blend { dark ? .additive : .over }
}

/// Builds the dynamic layers of the orb (DESIGN section 2) as GPU instances, back to front.
@MainActor
enum OrbSceneBuilder {
    static func scene(content: OrbContent, animator: OrbAnimator, model: AppModel, canvasSide: CGFloat, scale: CGFloat, time: TimeInterval) -> OrbScene {
        let gauge = model.gauge
        let effects = animator.effects(for: model)
        let heat = gauge.heat(at: time)
        let overdrive = gauge.overdrive(at: time)
        let tier = gauge.tier
        animator.step(to: time, input: .init(
            heat: heat,
            overdrive: overdrive,
            tier: tier,
            effects: effects,
            dark: content.tokens.isDark,
            hovered: animator.hoverTarget
        ))

        let sinceChange = time - gauge.tierChangedAt
        let flash = effects.flash && gauge.tierChangedAt.isFinite && sinceChange < 0.4 ? 1 - Easing.easeOut(sinceChange / 0.4) : 0
        let frame = OrbFrame(
            time: time,
            geometry: animator.geometry(diameter: content.diameter, side: canvasSide),
            content: content,
            effects: effects,
            heat: heat,
            displayHeat: heat * (effects.celebrate ? animator.peakSweep(at: time) : 1),
            overdrive: overdrive,
            tier: tier,
            titleChangedAt: gauge.titleChangedAt,
            flash: flash,
            breath: effects.breathing ? 1 + 0.15 * sin(2 * .pi * animator.breathPhase) : 1,
            breathPhase: animator.breathPhase,
            redlineAmount: animator.redline.value
        )

        var scene = OrbScene()
        addAura(&scene, frame, animator: animator)
        addPulses(&scene, frame, animator: animator)
        addPeakRays(&scene, frame, animator: animator)
        addHeatHaze(&scene, frame)
        addSegments(&scene, frame)
        addTicks(&scene, frame, redline: model.settings.redline)
        addComet(&scene, frame, animator: animator)
        addParticles(&scene, frame, animator: animator)
        addAgentDots(&scene, frame)
        if content.showsReadout {
            let value = gauge.readoutValue
            animator.readout.update(SpeedFormat.speed(value), value: value, at: time)
            OrbReadoutBuilder.add(&scene, frame, animator: animator, scale: scale)
        }
        return scene
    }

    // MARK: Aura

    private static func addAura(_ scene: inout OrbScene, _ frame: OrbFrame, animator: OrbAnimator) {
        let flare = animator.flare.value
        let alpha = frame.tokens.auraOpacity * frame.heat * frame.breath * (1 + 0.7 * flare)
            + (frame.dark ? 0.05 : 0.03) * flare
        guard alpha > 0.004 else { return }
        let color = frame.tokens.heat(max(frame.heat, 0.12))
        scene.add(.radial(center: frame.geometry.center, inner: 0.5 * frame.s, outer: frame.reach * frame.s, color: color, alpha: min(1, alpha)),
                  blend: frame.glowBlend)
    }

    // MARK: Pulses and peak

    private static func addPulses(_ scene: inout OrbScene, _ frame: OrbFrame, animator: OrbAnimator) {
        for pulse in animator.pulses {
            let p = ((frame.time - pulse.start) / OrbAnimator.pulseDuration).clamped(to: 0...1)
            let eased = Easing.easeOut(p)
            let color = frame.dark
                ? HeatPalette.color(pulse.heat).mixOKLab(HeatPalette.whiteHot, 0.18)
                : HeatPalette.color(pulse.heat, dark: false)
            let radius: CGFloat
            let alpha: Double
            if frame.effects.reduceMotion {
                radius = 0.53 * frame.s
                alpha = 0.5 * sin(.pi * p) * pulse.strength
            } else {
                radius = (0.5 + (frame.reach - 0.51) * eased) * frame.s
                alpha = 0.6 * (1 - eased) * (0.45 + 0.55 * pulse.strength)
            }
            let width = max(0.3, 2 * frame.unit * (1 - eased) * (0.55 + 0.45 * pulse.strength))
            scene.add(.ring(center: frame.geometry.center, radius: radius, width: width, color: color.opacity(alpha),
                            glow: 3 * frame.unit, glowAlpha: 0.35), blend: frame.glowBlend)
        }
    }

    private static func addPeakRays(_ scene: inout OrbScene, _ frame: OrbFrame, animator: OrbAnimator) {
        let elapsed = frame.time - animator.peakStart
        guard frame.effects.celebrate, elapsed >= 0, elapsed < 0.95 else { return }
        let g = frame.geometry
        let p = elapsed / 0.95
        let gold = frame.dark ? HeatPalette.gold.mixOKLab(HeatPalette.whiteHot, 0.25) : HeatPalette.gold.darkened(0.1)
        if frame.effects.reduceMotion {
            scene.add(.ring(center: g.center, radius: 0.54 * frame.s, width: 3 * frame.unit, color: gold.opacity(0.45 * sin(.pi * p)),
                            glow: 4 * frame.unit, glowAlpha: 0.4), blend: frame.glowBlend)
            return
        }
        let travel = Easing.easeOut(p)
        let alpha = pow(1 - p, 1.3)
        for index in 0..<24 {
            let long = index.isMultiple(of: 2)
            let angle = Double(index) * 15 + 3.75
            let reach = long ? 0.23 : 0.16
            let head = 0.515 + reach * travel
            let tail = 0.505 + reach * max(0, travel - 0.42) * 1.25
            scene.add(.capsule(
                from: g.point(angle: angle, radius: tail * frame.s),
                to: g.point(angle: angle, radius: head * frame.s),
                width: (long ? 1.7 : 1.2) * frame.unit,
                color: gold.opacity(alpha),
                tailColor: gold.opacity(alpha * 0.25),
                glow: 2.2 * frame.unit,
                glowAlpha: 0.45
            ), blend: frame.glowBlend)
        }
    }

    /// Max effects at redline: faint rising heat waves above the ring.
    private static func addHeatHaze(_ scene: inout OrbScene, _ frame: OrbFrame) {
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

    // MARK: Gauge

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

    private static func segment(_ index: Int, _ frame: OrbFrame, color: RGBColor, core: Double = 1, glow: CGFloat = 0, glowAlpha: Double = 0) -> OrbInstance {
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

    private static func addSegments(_ scene: inout OrbScene, _ frame: OrbFrame) {
        let lit = frame.displayHeat * Double(OrbGeometry.segmentCount)
        let bloom = frame.effects.bloom
        let glowRadius = 0.02 * frame.s * bloom

        // Bloom under the lit segments, white-hot at the leading edge.
        for index in 0..<OrbGeometry.segmentCount {
            let light = light(index, lit: lit)
            guard light.brightness > 0.01 else { continue }
            var color = segmentColor(index, frame)
            if light.whiteness > 0 {
                color = color.mixOKLab(frame.tokens.leadingSegment(Double(index) / 35, overdrive: frame.overdrive), light.whiteness * 0.8)
            }
            let base = (frame.dark ? 0.3 : 0.2) + 0.45 * light.whiteness
            let alpha = light.brightness * base * shimmer(index, frame) * (1 + 0.5 * frame.flash)
            scene.add(segment(index, frame, color: color.opacity(min(1, alpha * bloom)), core: 0,
                              glow: glowRadius * (1 + 0.7 * light.whiteness), glowAlpha: 0.6), blend: frame.glowBlend)
        }
        if frame.redlineAmount > 0.01 {
            let pulse = 0.55 + 0.45 * sin(2 * .pi * frame.breathPhase)
            let color = HeatPalette.highlight(frame.heat, overdrive: frame.overdrive, dark: frame.dark)
            let end = OrbGeometry.angle(forFraction: frame.displayHeat)
            scene.add(.arc(center: frame.geometry.center, radius: 0.447 * frame.s, width: 0.012 * frame.s,
                           start: OrbGeometry.startAngle, sweep: end - OrbGeometry.startAngle,
                           color: color.opacity(0.5 * frame.redlineAmount * pulse * bloom),
                           tailColor: color.opacity(0.5 * frame.redlineAmount * pulse * bloom),
                           glow: 0.02 * frame.s, glowAlpha: 0.8), blend: frame.glowBlend)
        }

        // Crisp segments.
        let unlit = frame.content.increaseContrast ? frame.tokens.unlitSegment.opacity(1.8) : frame.tokens.unlitSegment
        for index in 0..<OrbGeometry.segmentCount {
            let light = light(index, lit: lit)
            if light.brightness < 1 {
                scene.add(segment(index, frame, color: unlit), blend: .over)
            }
            guard light.brightness > 0 else { continue }
            var color = segmentColor(index, frame).scaled(shimmer(index, frame))
            if light.whiteness > 0 {
                color = color.mixOKLab(frame.tokens.leadingSegment(Double(index) / 35, overdrive: frame.overdrive), light.whiteness)
            }
            if frame.flash > 0 { color = color.mix(.white, 0.25 * frame.flash) }
            scene.add(segment(index, frame, color: color.opacity(light.brightness)), blend: .over)
        }
    }

    private static func addTicks(_ scene: inout OrbScene, _ frame: OrbFrame, redline: Double) {
        let g = frame.geometry
        let idle = frame.dark
            ? RGBColor.white.opacity(frame.content.increaseContrast ? 0.5 : 0.26)
            : RGBColor.black.opacity(frame.content.increaseContrast ? 0.45 : 0.22)
        for tier in [SpeedTier.cruise, .boost, .overdrive, .redline] {
            let fraction = HeatPalette.heat(forFraction: tier.enterSpeed(redline: redline) / max(redline, 1))
            let passed = frame.displayHeat >= fraction
            let color = passed ? frame.tokens.heat(max(fraction, 0.1)) : idle
            scene.add(.circle(center: g.point(angle: OrbGeometry.angle(forFraction: fraction), radius: g.tickRadius),
                              radius: 0.0085 * frame.s, color: color, glow: passed && frame.dark ? 1.5 * frame.unit : 0,
                              glowAlpha: 0.5), blend: .over)
        }
    }

    // MARK: Comet

    static func cometHeadAngle(_ animator: OrbAnimator) -> Double {
        OrbGeometry.startAngle + 360 * animator.cometPhase
    }

    /// Dims the comet while it passes the gap at the bottom of the gauge.
    private static func cometGapFade(_ angle: Double) -> Double {
        var delta = (angle - 90).truncatingRemainder(dividingBy: 360)
        if delta > 180 { delta -= 360 }
        if delta < -180 { delta += 360 }
        return 0.3 + 0.7 * Easing.smoothstep(18, 52, abs(delta))
    }

    private static func addComet(_ scene: inout OrbScene, _ frame: OrbFrame, animator: OrbAnimator) {
        let visibility = animator.cometVisibility.value
        guard visibility > 0.01 else { return }
        let g = frame.geometry
        let headAngle = cometHeadAngle(animator)
        let alpha = visibility * (0.25 + 0.6 * frame.heat) * cometGapFade(headAngle)
        let heat = frame.tokens.heat(max(frame.heat, 0.1))
        let hot = frame.dark ? HeatPalette.whiteHot : heat
        scene.add(.arc(center: g.center, radius: g.cometRadius, width: 0.0125 * frame.s, start: headAngle - 40, sweep: 40,
                       color: hot.opacity(alpha), tailColor: heat.opacity(alpha * 0.6),
                       glow: 0.012 * frame.s * frame.effects.bloom, glowAlpha: 0.55), blend: frame.glowBlend)
        scene.add(.circle(center: g.point(angle: headAngle, radius: g.cometRadius), radius: 0.0085 * frame.s,
                          color: hot.opacity(alpha), glow: 0.022 * frame.s * frame.effects.bloom, glowAlpha: 0.8),
                  blend: frame.glowBlend)
    }

    // MARK: Particles

    private static func addParticles(_ scene: inout OrbScene, _ frame: OrbFrame, animator: OrbAnimator) {
        let particles = animator.particles.particles
        guard !particles.isEmpty else { return }
        let g = frame.geometry
        for particle in particles {
            let life = particle.age / particle.life
            let color = particle.hot.mixOKLab(particle.cool, min(1, life * 1.3))
            let center = CGPoint(x: g.center.x + particle.x * frame.s, y: g.center.y + particle.y * frame.s)
            switch particle.kind {
            case .spark:
                let alpha = pow(1 - life, 1.25)
                let radius = particle.size * frame.unit * (1 - 0.45 * life)
                let streak = 0.045
                let tail = CGPoint(x: center.x - particle.vx * streak * frame.s, y: center.y - particle.vy * streak * frame.s)
                scene.add(.capsule(from: tail, to: center, width: radius * 1.3, color: color.opacity(alpha),
                                   tailColor: color.opacity(alpha * 0.35), glow: frame.dark ? radius * 1.4 : 0,
                                   glowAlpha: 0.45), blend: frame.glowBlend)
            case .ember:
                let flicker = 0.7 + 0.3 * sin(frame.time * 23 + particle.seed * 50)
                let alpha = pow(sin(.pi * min(1, life)), 0.7) * flicker
                let radius = particle.size * frame.unit * (1 - 0.35 * life)
                scene.add(.circle(center: center, radius: radius, color: color.opacity(alpha),
                                  glow: radius * (frame.dark ? 1.9 : 1.2), glowAlpha: frame.dark ? 0.5 : 0.3),
                          blend: frame.glowBlend)
            }
        }
    }

    // MARK: Agent dots

    private static func addAgentDots(_ scene: inout OrbScene, _ frame: OrbFrame) {
        let sources = frame.content.generating
        guard !sources.isEmpty else { return }
        let layout = OrbAgentDotsLayout(count: sources.count, diameter: frame.s, center: frame.geometry.center)
        for (index, source) in sources.prefix(layout.dotCount).enumerated() {
            let color = frame.dark ? source.tint : source.tint.darkened(0.12)
            let pulse = frame.effects.ambient && !frame.effects.reduceMotion
                ? 0.62 + 0.38 * (0.5 + 0.5 * sin(2 * .pi * (frame.time * 0.9 + Double(index) * 0.19)))
                : 1
            scene.add(.circle(center: layout.center(of: index), radius: layout.dotDiameter / 2, color: color.opacity(pulse),
                              glow: frame.dark ? layout.dotDiameter * 0.7 : 0, glowAlpha: 0.45), blend: .over)
        }
    }
}

/// Positions of the agent dots in the gauge gap (max 6, then 5 dots and `+n`).
struct OrbAgentDotsLayout {
    let dotCount: Int
    let overflow: Int
    let dotDiameter: CGFloat
    let spacing: CGFloat
    let y: CGFloat
    let startX: CGFloat
    /// Room reserved for the `+n` label.
    static func overflowWidth(_ diameter: CGFloat) -> CGFloat { 0.075 * diameter }

    init(count: Int, diameter: CGFloat, center: CGPoint) {
        overflow = count > 6 ? count - 5 : 0
        dotCount = overflow > 0 ? 5 : count
        let unit = diameter / 148
        dotDiameter = 4 * unit
        spacing = 3.2 * unit
        y = center.y + 0.392 * diameter
        let total = CGFloat(dotCount) * dotDiameter + CGFloat(max(0, dotCount - 1)) * spacing
            + (overflow > 0 ? spacing + Self.overflowWidth(diameter) : 0)
        startX = center.x - total / 2
    }

    func center(of index: Int) -> CGPoint {
        CGPoint(x: startX + CGFloat(index) * (dotDiameter + spacing) + dotDiameter / 2, y: y)
    }

    /// Leading edge of the `+n` label.
    var overflowX: CGFloat { startX + CGFloat(dotCount) * (dotDiameter + spacing) }
}
