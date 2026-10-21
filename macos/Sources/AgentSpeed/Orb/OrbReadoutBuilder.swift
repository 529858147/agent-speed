import SwiftUI

/// The centered readout drawn on the GPU (DESIGN section 2, layers 6 and 8): rolling value with heat glow, forward
/// lean and redline jitter, the unit, the speed title (or the hover info line) and the new-peak pill.
@MainActor
enum OrbReadoutBuilder {
    /// Duration of the title slide (DESIGN section 2, layer 6).
    static let titleSlide: TimeInterval = 0.35
    /// Widest the title may be, in diameters: the chord inside the gauge ring at the label's height, with margin.
    static let titleWidth: CGFloat = 0.58

    private struct Layout {
        let valueBaseline: CGFloat
        let unitBaseline: CGFloat
        let labelBaseline: CGFloat
        let pillCenter: CGFloat

        init(_ frame: OrbFrame) {
            let s = frame.s
            let y = frame.geometry.center.y
            valueBaseline = y + 0.012 * s
            unitBaseline = valueBaseline + 0.104 * s
            labelBaseline = unitBaseline + 0.09 * s
            pillCenter = y - 0.232 * s
        }
    }

    static func add(_ scene: inout OrbScene, _ frame: OrbFrame, animator: OrbAnimator, scale: CGFloat) {
        let layout = Layout(frame)
        addValue(&scene, frame, layout: layout, animator: animator, scale: scale)
        addUnit(&scene, frame, layout: layout, animator: animator, scale: scale)
        addLabels(&scene, frame, layout: layout, animator: animator, scale: scale)
        addOverflow(&scene, frame, animator: animator, scale: scale)
        addPeakPill(&scene, frame, layout: layout, animator: animator, scale: scale)
    }

    private static func secondary(_ frame: OrbFrame) -> RGBColor {
        frame.content.increaseContrast ? frame.tokens.secondaryText.opacity(1.35) : frame.tokens.secondaryText
    }

    // MARK: Value

    private static func addValue(_ scene: inout OrbScene, _ frame: OrbFrame, layout: Layout, animator: OrbAnimator, scale: CGFloat) {
        let readout = animator.readout
        let style = OrbTextStyle(size: 0.24 * frame.s, weight: .bold, monospacedDigits: true, padding: 0.07 * frame.s)
        func glyphs(_ string: String) -> [OrbTextTexture] {
            string.compactMap { animator.textCache.texture(String($0), style: style, language: frame.content.language, scale: scale) }
        }
        let progress = readout.progress(at: frame.time)
        let current = glyphs(readout.current)
        let previous = progress < 1 ? readout.previous.map(glyphs) : nil
        let currentChars = Array(readout.current)
        let previousChars = readout.previous.map(Array.init) ?? []
        guard !current.isEmpty else { return }

        let centerX = frame.geometry.center.x
        let currentX = positions(current, centerX: centerX)
        let previousX = previous.map { positions($0, centerX: centerX) } ?? []

        let tokens = frame.tokens
        let color = tokens.idleValueText.mix(tokens.valueText, animator.activity.value)
        let glowStrength = animator.valueGlow.value * (frame.dark ? 0.85 : 0.45)
        let glow = glowStrength > 0.01 ? tokens.heat(max(frame.heat, 0.2)).opacity(glowStrength) : nil
        let lean = frame.effects.skew ? tan(6 * frame.heat * animator.skew.value * .pi / 180) : 0
        var jitter = CGSize.zero
        if frame.effects.jitter, frame.tier == .redline, frame.redlineAmount > 0.01 {
            let tick = Int((frame.time * 30).rounded(.down))
            let amplitude = 0.6 * frame.redlineAmount
            jitter = CGSize(width: (Noise.hash(tick, 3) - 0.5) * 2 * amplitude, height: (Noise.hash(tick, 7) - 0.5) * 2 * amplitude)
        }
        let baseline = layout.valueBaseline + jitter.height
        let direction: CGFloat = readout.rising ? 1 : -1
        let slide = frame.effects.reduceMotion ? 0 : 0.07 * frame.s

        func place(_ texture: OrbTextTexture, x: CGFloat, offset: CGFloat, alpha: Double) {
            guard alpha > 0.01 else { return }
            scene.texts.append(OrbTextDraw(
                texture, x: x + jitter.width, baseline: baseline + offset, color: color.opacity(alpha),
                glow: glow?.opacity(alpha), glowRadius: 0.035 * frame.s, shear: lean, shearPivot: baseline
            ))
        }

        let previousCount = previous?.count ?? 0
        for k in 0..<max(current.count, previousCount) {
            let newIndex = current.count - 1 - k
            let oldIndex = previousCount - 1 - k
            let newX = newIndex >= 0 ? currentX[newIndex] : nil
            let oldX = oldIndex >= 0 ? previousX[oldIndex] : nil
            let changed = previous != nil && (newIndex < 0 || oldIndex < 0 || currentChars[newIndex] != previousChars[oldIndex])
            let p = staggered(progress, position: k)
            if newIndex >= 0, let newX {
                let x = oldX.map { $0 + (newX - $0) * p } ?? newX
                place(current[newIndex], x: x, offset: changed ? direction * slide * (1 - p) : 0, alpha: changed ? p : 1)
            }
            if changed, oldIndex >= 0, let previous, let oldX {
                let x = newX.map { oldX + ($0 - oldX) * p } ?? oldX
                place(previous[oldIndex], x: x, offset: -direction * slide * p, alpha: (1 - p) * (1 - p))
            }
        }
    }

    private static func positions(_ glyphs: [OrbTextTexture], centerX: CGFloat) -> [CGFloat] {
        let total = glyphs.reduce(0) { $0 + $1.advance }
        var x = centerX - total / 2
        return glyphs.map { glyph in
            defer { x += glyph.advance }
            return x
        }
    }

    /// Rightmost digits move first, like an odometer.
    private static func staggered(_ progress: Double, position: Int) -> Double {
        let delay = min(0.3, Double(position) * 0.07)
        return ((progress - delay) / (1 - delay)).clamped(to: 0...1)
    }

    // MARK: Unit and labels

    private static func addUnit(_ scene: inout OrbScene, _ frame: OrbFrame, layout: Layout, animator: OrbAnimator, scale: CGFloat) {
        let tracking = 0.5 * frame.unit
        let style = OrbTextStyle(size: 0.072 * frame.s, weight: .medium, tracking: tracking)
        guard let texture = animator.textCache.texture(frame.content.unit, style: style, language: frame.content.language, scale: scale) else { return }
        scene.texts.append(OrbTextDraw(texture, centerX: frame.geometry.center.x + tracking / 2, baseline: layout.unitBaseline, color: secondary(frame)))
    }

    private static func addLabels(_ scene: inout OrbScene, _ frame: OrbFrame, layout: Layout, animator: OrbAnimator, scale: CGFloat) {
        let hover = animator.hover.value
        let labelAlpha = 1 - hover
        let x = frame.geometry.center.x
        let tracking = (frame.content.latinTracking ? 1.6 : 1.0) * frame.unit
        let style = OrbTextStyle(size: 0.062 * frame.s, weight: .semibold, tracking: tracking)
        var color = frame.tokens.tierColor(frame.heat)
        if frame.content.increaseContrast && !frame.dark { color = color.darkened(0.12) }

        if labelAlpha > 0.01 {
            let elapsed = frame.time - frame.titleChangedAt
            let transitioning = elapsed >= 0 && elapsed < titleSlide && frame.content.previousTitleLabel != frame.content.titleLabel
            let q = transitioning ? Easing.easeInOut(elapsed / titleSlide) : 1
            let travel = frame.effects.reduceMotion ? 0 : 0.045 * frame.s
            let limit = titleWidth * frame.s
            func draw(_ label: String, offset: CGFloat, alpha: Double) {
                guard alpha > 0.01, let (texture, labelTracking) = fittedTitle(label, style: style, limit: limit, frame: frame,
                                                                                  animator: animator, scale: scale) else { return }
                scene.texts.append(OrbTextDraw(texture, centerX: x + labelTracking / 2, baseline: layout.labelBaseline + offset,
                                               color: color.opacity(alpha)))
            }
            draw(frame.content.titleLabel, offset: travel * (1 - q), alpha: labelAlpha * q)
            if transitioning {
                draw(frame.content.previousTitleLabel, offset: -travel * q, alpha: labelAlpha * (1 - q))
            }
        }
        if hover > 0.01 {
            let infoStyle = OrbTextStyle(size: 0.058 * frame.s, weight: .medium, monospacedDigits: true)
            guard let texture = animator.textCache.texture(frame.content.infoLine, style: infoStyle, language: frame.content.language, scale: scale) else { return }
            let limit = 0.62 * frame.s
            let fit = min(1, limit / max(texture.advance, 1))
            scene.texts.append(OrbTextDraw(texture, centerX: x, baseline: layout.labelBaseline, color: secondary(frame).opacity(1.25 * hover),
                                           scale: fit, pivot: CGPoint(x: x, y: layout.labelBaseline)))
        }
    }

    /// The title rasterized at its own size when it fits, else at a smaller size (down to 80 %) so it stays crisp;
    /// it is never cut.
    private static func fittedTitle(
        _ label: String, style: OrbTextStyle, limit: CGFloat, frame: OrbFrame, animator: OrbAnimator, scale: CGFloat
    ) -> (OrbTextTexture, CGFloat)? {
        let language = frame.content.language
        guard let natural = animator.textCache.texture(label, style: style, language: language, scale: scale) else { return nil }
        let fit = TitleFit.scale(width: natural.advance - style.tracking, limit: limit)
        guard fit < 1 else { return (natural, style.tracking) }
        var smaller = style
        smaller.size = style.size * fit
        smaller.tracking = style.tracking * fit
        guard let texture = animator.textCache.texture(label, style: smaller, language: language, scale: scale) else {
            return (natural, style.tracking)
        }
        return (texture, smaller.tracking)
    }

    private static func addOverflow(_ scene: inout OrbScene, _ frame: OrbFrame, animator: OrbAnimator, scale: CGFloat) {
        let count = frame.content.generating.count
        guard count > 6 else { return }
        let dots = OrbAgentDotsLayout(count: count, diameter: frame.s, center: frame.geometry.center)
        let style = OrbTextStyle(size: 0.055 * frame.s, weight: .semibold, monospacedDigits: true)
        guard let texture = animator.textCache.texture("+\(dots.overflow)", style: style, language: frame.content.language, scale: scale) else { return }
        let capHeight = style.font().capHeight
        scene.texts.append(OrbTextDraw(texture, x: dots.overflowX, baseline: dots.y + capHeight / 2, color: secondary(frame)))
    }

    // MARK: New peak

    private static func addPeakPill(_ scene: inout OrbScene, _ frame: OrbFrame, layout: Layout, animator: OrbAnimator, scale: CGFloat) {
        let elapsed = frame.time - animator.peakStart
        let total = OrbAnimator.peakDuration
        guard frame.effects.celebrate, elapsed >= 0, elapsed < total else { return }
        let fadeOut = Easing.smoothstep(total - 0.32, total, elapsed)
        let alpha = min(1, elapsed / 0.12) * (1 - fadeOut)
        let pop = frame.effects.reduceMotion ? 1 : (0.55 + 0.45 * Easing.springPop(elapsed)) * (1 - 0.04 * fadeOut)
        guard alpha > 0.01 else { return }

        let tracking = (frame.content.latinTracking ? 0.9 : 0.4) * frame.unit
        let style = OrbTextStyle(size: 0.056 * frame.s, weight: .heavy, tracking: tracking)
        guard let texture = animator.textCache.texture(frame.content.peakLabel, style: style, language: frame.content.language, scale: scale) else { return }
        let center = CGPoint(x: frame.geometry.center.x, y: layout.pillCenter)
        let height = 0.082 * frame.s * pop
        let width = (texture.advance + 0.085 * frame.s) * pop
        let gold = HeatPalette.gold.mixOKLab(HeatPalette.whiteHot, 0.2).mixOKLab(HeatPalette.color(0.92), 0.25)
        scene.add(.roundedRect(center: center, halfSize: CGSize(width: width / 2, height: height / 2), rotation: 0,
                               corner: height / 2, color: gold.opacity(alpha), glow: frame.dark ? 0.025 * frame.s : 0.012 * frame.s,
                               glowAlpha: frame.dark ? 0.55 : 0.3), blend: .over)
        let capHeight = style.font().capHeight * pop
        scene.texts.append(OrbTextDraw(
            texture, centerX: center.x + tracking / 2 * pop, baseline: center.y + capHeight / 2,
            color: RGBColor(red: 0.10, green: 0.07, blue: 0.02, alpha: alpha),
            scale: pop, pivot: CGPoint(x: center.x, y: center.y + capHeight / 2)
        ))
    }
}
