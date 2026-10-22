import AgentSpeedCore
import SwiftUI

/// Colors of the readout; styles that put text on their own lit surfaces override them (DESIGN 2a.3, readout split).
struct OrbReadoutPalette {
    var value: RGBColor?
    var unit: RGBColor?
    var title: RGBColor?
    /// Replaces the heat glow of the value (a dark shadow under the liquid surface); its alpha is the strength.
    var shadow: RGBColor?
    var shadowRadius: CGFloat = 0
    var valueGlow = true

    static let standard = OrbReadoutPalette()
}

/// The readout drawn on the GPU (DESIGN 2 layers 6 and 8, 2a Shared foundation): the value with the odometer, heat
/// glow, forward lean and redline jitter, the unit, the speed title (or the hover info line), the agents overflow
/// and the new-peak pill, placed by the style's frame table.
///
/// One builder per orb: it owns the odometer state and reuses its glyph storage, so drawing never allocates.
@MainActor
final class OrbReadoutBuilder {
    /// Duration of the title slide (DESIGN section 2, layer 6).
    static let titleSlide: TimeInterval = 0.35
    static let titleFade: TimeInterval = 0.25
    static let peakDuration: TimeInterval = 1.8
    /// Unit and title never go below this size.
    static let textFloor: CGFloat = 7

    let textCache: OrbTextCache
    private(set) var odometer = OrbOdometer()
    private var current: ContiguousArray<OrbTextTexture> = []
    private var previous: ContiguousArray<OrbTextTexture> = []
    private var currentX: ContiguousArray<CGFloat> = []

    init(textCache: OrbTextCache = .shared) {
        self.textCache = textCache
        current.reserveCapacity(16)
        previous.reserveCapacity(16)
        currentX.reserveCapacity(16)
    }

    /// Feeds the shown value (called every frame; only changes start a roll).
    func update(text: String, value: Double, at time: TimeInterval) {
        odometer.update(text, value: value, at: time)
    }

    /// Feeds the shown value every frame; it is formatted only when it changes (formatting allocates).
    func update(value: Double, at time: TimeInterval) {
        guard value != formattedValue else { return }
        formattedValue = value
        odometer.update(SpeedFormat.speed(value), value: value, at: time)
    }

    private var formattedValue = -1.0

    func reset(text: String, value: Double) {
        odometer.reset(text, value: value)
        formattedValue = value
    }

    var isRolling: Bool { odometer.changedAt.isFinite }

    func isSettled(at time: TimeInterval) -> Bool { time - odometer.changedAt >= OrbOdometer.duration + 0.05 }

    // MARK: Layout

    private struct Lines {
        var value: CGFloat
        var unit: CGFloat
        var title: CGFloat
        var pill: CGFloat
    }

    private func lines(_ frame: OrbFrame, _ layout: OrbReadoutLayout) -> Lines {
        let s = frame.s
        let y = frame.center.y
        let value = y + layout.valueBaseline * s
        let unit = layout.unitSize == nil ? value : value + layout.unitGap * s
        let title: CGFloat = switch layout.title {
        case .belowUnit(let gap): unit + gap * s
        case .belowValue(let gap): value + gap * s
        case .fixed(let offset): y + offset * s
        }
        return Lines(value: value, unit: unit, title: title, pill: y + layout.pillCenter * s)
    }

    func add(into scene: inout OrbScene, _ frame: OrbFrame, layout: OrbReadoutLayout, palette: OrbReadoutPalette = .standard,
             clip: OrbTextClip = .none, pill: Bool = true) {
        let lines = lines(frame, layout)
        addValue(&scene, frame, layout: layout, line: lines.value, palette: palette, clip: clip)
        guard !frame.content.valueOnly else { return }
        if let unitSize = layout.unitSize {
            addUnit(&scene, frame, size: unitSize, line: lines.unit, palette: palette, clip: clip)
        }
        addLabels(&scene, frame, layout: layout, line: lines.title, palette: palette, clip: clip)
        if layout.dots != nil { addOverflow(&scene, frame, layout: layout, clip: clip) }
        if pill { addPeakPill(&scene, frame, line: lines.pill) }
    }

    private func secondary(_ frame: OrbFrame) -> RGBColor {
        frame.content.increaseContrast ? frame.tokens.secondaryText.opacity(1.35) : frame.tokens.secondaryText
    }

    // MARK: Value

    private func addValue(_ scene: inout OrbScene, _ frame: OrbFrame, layout: OrbReadoutLayout, line: CGFloat,
                          palette: OrbReadoutPalette, clip: OrbTextClip) {
        let s = frame.s
        let unavailable = frame.unavailable
        let text = unavailable ? "–" : odometer.current
        let language = frame.content.language
        let fullSize = layout.valueSize * s
        var style = OrbTextStyle(size: fullSize, weight: .bold, monospacedDigits: true, padding: 0.07 * s)
        func advance(_ string: String, _ style: OrbTextStyle) -> CGFloat {
            var total: CGFloat = 0
            for character in string {
                total += textCache.texture(character, style: style, language: language, scale: scene.backingScale)?.advance ?? 0
            }
            return total
        }
        let natural = advance(text, style)
        let limit = layout.valueMaxWidth * s
        if natural > limit, natural > 0 {
            let fit = max(0.4, (limit / natural * 50).rounded(.down) / 50)
            style.size = fullSize * fit
            style.padding = 0.07 * s * fit
        }
        current.removeAll(keepingCapacity: true)
        currentX.removeAll(keepingCapacity: true)
        for character in text {
            guard let texture = textCache.texture(character, style: style, language: language, scale: scene.backingScale) else { continue }
            current.append(texture)
        }
        guard !current.isEmpty else { return }
        var total: CGFloat = 0
        for texture in current { total += texture.advance }
        var x = frame.center.x - total / 2
        for texture in current {
            currentX.append(x)
            x += texture.advance
        }

        let tokens = frame.tokens
        let idleColor = tokens.idleValueText
        let color = palette.value ?? (unavailable ? idleColor : idleColor.mix(tokens.valueText, frame.activity))
        var glow: RGBColor?
        var glowRadius: CGFloat = 0
        if let shadow = palette.shadow {
            glow = shadow
            glowRadius = palette.shadowRadius
        } else if layout.valueGlow, palette.valueGlow, !unavailable {
            let strength = valueGlowStrength * (frame.dark ? 0.85 : 0.45)
            if strength > 0.01 {
                glow = tokens.heat(max(frame.heat, 0.2)).opacity(strength)
                glowRadius = 0.035 * s
            }
        }
        let lean = frame.effects.skew && layout.valueMotion && !unavailable ? tan(6 * frame.heat * leanAmount * .pi / 180) : 0
        var jitter = CGSize.zero
        if frame.effects.jitter, layout.valueMotion, frame.tier == .redline, frame.redlineAmount > 0.01, !unavailable {
            let tick = Int((frame.time * 30).rounded(.down))
            let amplitude = 0.6 * frame.redlineAmount * frame.unit
            jitter = CGSize(width: (Noise.hash(tick, 3) - 0.5) * 2 * amplitude, height: (Noise.hash(tick, 7) - 0.5) * 2 * amplitude)
        }
        let baseline = line + jitter.height
        let rolls = layout.rolls && frame.effects.level > .off && !frame.effects.reduceMotion && s >= 80 && !unavailable
        let progress = rolls ? odometer.progress(at: frame.time) : 1

        func place(_ texture: OrbTextTexture, x: CGFloat, offset: CGFloat, alpha: Double, cell: CGRect?) {
            guard alpha > 0.01 else { return }
            var textClip = clip
            if let cell {
                switch clip {
                case .none: textClip = .rect(cell)
                case .rect(let rect): textClip = .rect(rect.intersection(cell))
                case .liquid(let below, let rect): textClip = .liquid(below: below, rect: rect.intersection(cell))
                }
            }
            scene.add(OrbTextDraw(texture, x: x + jitter.width, baseline: baseline + offset, color: color.opacity(alpha),
                                  glow: glow?.opacity(alpha), glowRadius: glowRadius, shear: lean, shearPivot: baseline,
                                  clip: textClip))
        }

        guard progress < 1, let old = odometer.previous, !unavailable else {
            for index in current.indices { place(current[index], x: currentX[index], offset: 0, alpha: 1, cell: nil) }
            return
        }
        previous.removeAll(keepingCapacity: true)
        for character in old {
            guard let texture = textCache.texture(character, style: style, language: language, scale: scene.backingScale) else { continue }
            previous.append(texture)
        }
        if previous.count == current.count {
            // Odometer: only the characters that changed roll, each clipped to its own cell (DESIGN 2a).
            let eased = 1 - pow(1 - progress, 3)
            let direction: CGFloat = odometer.rising ? -1 : 1
            let distance = style.size * 0.92
            var oldIterator = old.makeIterator()
            var newIterator = text.makeIterator()
            for index in current.indices {
                let oldCharacter = oldIterator.next()
                let newCharacter = newIterator.next()
                let x = currentX[index]
                if oldCharacter == newCharacter {
                    place(current[index], x: x, offset: 0, alpha: 1, cell: nil)
                    continue
                }
                let width = current[index].advance
                let cell = CGRect(x: x - 2 + jitter.width, y: baseline - style.size * 0.92, width: width + 4, height: style.size * 1.12)
                place(previous[index], x: x, offset: direction * distance * eased, alpha: 1 - eased, cell: cell)
                place(current[index], x: x, offset: -direction * distance * (1 - eased), alpha: eased, cell: cell)
            }
        } else {
            // Different lengths crossfade in the first half of the roll (0.12 s).
            let mix = min(1, progress / 0.5)
            var oldTotal: CGFloat = 0
            for texture in previous { oldTotal += texture.advance }
            var oldX = frame.center.x - oldTotal / 2
            for texture in previous {
                place(texture, x: oldX, offset: 0, alpha: 1 - mix, cell: nil)
                oldX += texture.advance
            }
            for index in current.indices { place(current[index], x: currentX[index], offset: 0, alpha: mix, cell: nil) }
        }
    }

    /// Glow and lean envelopes, set by the animator each frame.
    var valueGlowStrength = 0.0
    var leanAmount = 0.0

    // MARK: Unit and labels

    private func addUnit(_ scene: inout OrbScene, _ frame: OrbFrame, size: CGFloat, line: CGFloat, palette: OrbReadoutPalette,
                         clip: OrbTextClip) {
        let tracking = 0.5 * frame.unit
        let style = OrbTextStyle(size: max(Self.textFloor, size * frame.s), weight: .medium, tracking: tracking)
        guard let texture = textCache.texture(frame.content.unit, style: style, language: frame.content.language,
                                              scale: scene.backingScale) else { return }
        scene.add(OrbTextDraw(texture, centerX: frame.center.x + tracking / 2, baseline: line,
                              color: palette.unit ?? secondary(frame), glow: palette.shadow, glowRadius: palette.shadowRadius,
                              clip: clip))
    }

    private func addLabels(_ scene: inout OrbScene, _ frame: OrbFrame, layout: OrbReadoutLayout, line: CGFloat,
                           palette: OrbReadoutPalette, clip: OrbTextClip) {
        let hover = frame.unavailable ? 0 : hoverAmount
        let labelAlpha = 1 - hover
        let x = frame.center.x
        let size = max(Self.textFloor, layout.titleSize * frame.s)
        let tracking = (frame.content.latinTracking ? 1.6 : 1.0) * frame.unit * size / (0.062 * frame.s)
        let style = OrbTextStyle(size: size, weight: .semibold, tracking: tracking)
        var color = palette.title ?? frame.tokens.tierColor(frame.heat)
        if palette.title == nil {
            if frame.isIdle || frame.unavailable { color = secondary(frame) }
            if frame.content.increaseContrast && !frame.dark { color = color.darkened(0.12) }
        }
        let limit = layout.titleMaxWidth * frame.s

        if labelAlpha > 0.01 {
            let title = frame.content.unavailableTitle ?? frame.content.titleLabel
            let elapsed = frame.time - frame.titleChangedAt
            let duration = layout.titleCrossfades ? Self.titleFade : Self.titleSlide
            let transitioning = frame.content.unavailableTitle == nil && elapsed >= 0 && elapsed < duration
                && frame.content.previousTitleLabel != frame.content.titleLabel
            let q = transitioning ? (layout.titleCrossfades ? elapsed / duration : Easing.easeInOut(elapsed / duration)) : 1
            let travel = frame.effects.reduceMotion || layout.titleCrossfades || frame.effects.level == .off ? 0 : size * 1.1
            // The sliding labels stay inside their own line, so the old one leaves and the new one arrives.
            let lineBox = CGRect(x: x - frame.s / 2, y: line - size * 1.25, width: frame.s, height: size * 1.6)
            let titleClip: OrbTextClip = switch clip {
            case .none: transitioning ? .rect(lineBox) : .none
            case .rect(let rect): .rect(rect.intersection(lineBox))
            case .liquid(let below, let rect): .liquid(below: below, rect: rect.intersection(lineBox))
            }
            func draw(_ label: String, offset: CGFloat, alpha: Double) {
                guard alpha > 0.01, let (texture, labelTracking) = fittedTitle(label, style: style, limit: limit, frame: frame,
                                                                                  scale: scene.backingScale) else { return }
                scene.add(OrbTextDraw(texture, centerX: x + labelTracking / 2, baseline: line + offset, color: color.opacity(alpha),
                                      glow: palette.shadow, glowRadius: palette.shadowRadius, clip: titleClip))
            }
            draw(title, offset: travel * (1 - q), alpha: labelAlpha * q)
            if transitioning {
                draw(frame.content.previousTitleLabel, offset: -travel * q, alpha: labelAlpha * (1 - q))
            }
        }
        if hover > 0.01 {
            let infoStyle = OrbTextStyle(size: max(Self.textFloor, size * 0.935 * layout.infoScale), weight: .medium, monospacedDigits: true)
            guard let texture = textCache.texture(frame.content.infoLine, style: infoStyle, language: frame.content.language,
                                                  scale: scene.backingScale) else { return }
            let infoLimit = (layout.infoMaxWidth ?? layout.titleMaxWidth) * frame.s
            let fit = max(0.7, min(1, infoLimit / max(texture.advance, 1)))
            scene.add(OrbTextDraw(texture, centerX: x, baseline: line, color: (palette.unit ?? secondary(frame)).opacity(1.25 * hover),
                                  glow: palette.shadow, glowRadius: palette.shadowRadius, scale: fit,
                                  pivot: CGPoint(x: x, y: line), clip: clip))
        }
    }

    /// Hover envelope, set by the animator each frame.
    var hoverAmount = 0.0

    /// The title rasterized at its own size when it fits, else at a smaller size (down to 80 %) so it stays crisp;
    /// it is never cut.
    private func fittedTitle(_ label: String, style: OrbTextStyle, limit: CGFloat, frame: OrbFrame, scale: CGFloat)
        -> (OrbTextTexture, CGFloat)? {
        let language = frame.content.language
        guard let natural = textCache.texture(label, style: style, language: language, scale: scale) else { return nil }
        let fit = TitleFit.scale(width: natural.advance - style.tracking, limit: limit)
        guard fit < 1 else { return (natural, style.tracking) }
        var smaller = style
        smaller.size = max(Self.textFloor, style.size * fit)
        smaller.tracking = style.tracking * fit
        guard let texture = textCache.texture(label, style: smaller, language: language, scale: scale) else {
            return (natural, style.tracking)
        }
        return (texture, smaller.tracking)
    }

    private func addOverflow(_ scene: inout OrbScene, _ frame: OrbFrame, layout: OrbReadoutLayout, clip: OrbTextClip) {
        let count = frame.content.generating.count
        guard count > 6, let offset = layout.dots, !frame.isIdle, !frame.unavailable else { return }
        let dots = OrbAgentDotsLayout(count: count, diameter: frame.s, center: frame.center, offset: offset)
        let style = OrbTextStyle(size: max(Self.textFloor, 0.055 * frame.s), weight: .semibold, monospacedDigits: true)
        guard let texture = textCache.overflowTexture(dots.overflow, style: style, language: frame.content.language,
                                                      scale: scene.backingScale) else { return }
        let capHeight = textCache.capHeight(style)
        scene.add(OrbTextDraw(texture, x: dots.overflowX, baseline: dots.y + capHeight / 2, color: secondary(frame), clip: clip))
    }

    // MARK: New peak

    private func addPeakPill(_ scene: inout OrbScene, _ frame: OrbFrame, line: CGFloat) {
        let elapsed = frame.peakAge
        let total = Self.peakDuration
        guard frame.effects.celebrate, elapsed >= 0, elapsed < total else { return }
        let fadeOut = Easing.smoothstep(total - 0.32, total, elapsed)
        let alpha = min(1, elapsed / (frame.effects.reduceMotion ? 0.2 : 0.12)) * (1 - fadeOut)
        let pop = frame.effects.reduceMotion ? 1 : (0.55 + 0.45 * Easing.springPop(elapsed)) * (1 - 0.04 * fadeOut)
        guard alpha > 0.01 else { return }

        let tracking = (frame.content.latinTracking ? 0.9 : 0.4) * frame.unit
        let style = OrbTextStyle(size: max(Self.textFloor, 0.056 * frame.s), weight: .heavy, tracking: tracking)
        guard let texture = textCache.texture(frame.content.peakLabel, style: style, language: frame.content.language,
                                              scale: scene.backingScale) else { return }
        let center = CGPoint(x: frame.center.x, y: line)
        let height = 0.082 * frame.s * pop
        let width = (texture.advance + 0.085 * frame.s) * pop
        let gold = HeatPalette.gold.mixOKLab(HeatPalette.whiteHot, 0.2).mixOKLab(HeatPalette.color(0.92), 0.25)
        scene.add(.roundedRect(center: center, halfSize: CGSize(width: width / 2, height: height / 2), rotation: 0,
                               corner: height / 2, color: gold.opacity(alpha), glow: frame.dark ? 0.025 * frame.s : 0.012 * frame.s,
                               glowAlpha: frame.dark ? 0.55 : 0.3), blend: .over)
        let capHeight = textCache.capHeight(style) * pop
        scene.add(OrbTextDraw(
            texture, centerX: center.x + tracking / 2 * pop, baseline: center.y + capHeight / 2,
            color: RGBColor(red: 0.10, green: 0.07, blue: 0.02, alpha: alpha),
            scale: pop, pivot: CGPoint(x: center.x, y: center.y + capHeight / 2)
        ))
    }
}

/// Odometer state of the value readout (DESIGN 2a): the shown string and the roll from the previous one.
struct OrbOdometer {
    static let duration: TimeInterval = 0.24

    private(set) var current = "0"
    private(set) var previous: String?
    private(set) var changedAt: TimeInterval = -.infinity
    private(set) var rising = true
    private var value = 0.0

    mutating func update(_ text: String, value: Double, at time: TimeInterval) {
        guard text != current else {
            self.value = value
            return
        }
        previous = current
        rising = value >= self.value
        current = text
        changedAt = time
        self.value = value
    }

    mutating func reset(_ text: String, value: Double) {
        current = text
        previous = nil
        changedAt = -.infinity
        self.value = value
    }

    /// 0 at the change, 1 when settled.
    func progress(at time: TimeInterval) -> Double {
        let t = (time - changedAt) / Self.duration
        guard t < 1 else { return 1 }
        return max(0, t)
    }
}
