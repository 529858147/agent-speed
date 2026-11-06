import AgentSpeedOrbModel
import AppKit

/// The hero number as one item per character, so changed digits roll in their slots and the whole number punches
/// (DESIGN 2c Hero number), and an empty item over it carrying the new-peak sheen and `boards.newPeak` chip. The
/// number's glow stays in the hero item underneath (`drawGlow`).
@MainActor
struct BoardHeroNumber {
    struct Look {
        var font: BoardFont
        var color: RGBColor
        /// The white-hot (timing: green) a rising digit rolls in with.
        var hot: RGBColor
        var hotGlow: RGBColor?
        var hotBlur: CGFloat = 0
        /// Neon's chromatic fringes (h ≥ 0.85).
        var fringes = false
    }

    let text: String
    /// `live_A` behind `text`, for the rising and punch tests.
    let value: Double
    let look: Look
    /// Top-left of the number's line box (board points).
    let origin: CGPoint
    let label: BoardLabel

    init(_ text: String, value: Double, look: Look, origin: CGPoint) {
        self.text = text
        self.value = value
        self.look = look
        self.origin = origin
        label = BoardLabel(text, look.font, color: look.color)
    }

    var height: CGFloat { look.font.size * 1.08 }
    var rect: CGRect { CGRect(x: origin.x, y: origin.y, width: label.width, height: height) }
    /// The baseline in the line box.
    var baseline: CGFloat { label.baseline(lineBoxTop: 0) }

    /// The digits' items with this tick's rolls and punch, then the item for the number's decals.
    func items(memory: BoardTickMemory, decals: [BoardDecal]) -> [BoardContentItem] {
        let characters = Array(text)
        let previous = memory.number.map(Array.init)
        let rolling = memory.fresh && previous?.count == characters.count
        let rising = memory.value.map { value >= $0 } ?? true
        let punch = memory.fresh && BoardContentRules.punches(from: memory.value, to: value)
        let anchor = CGPoint(x: origin.x, y: origin.y + height * 0.8)
        let look = self.look, pad: CGFloat = 3
        // Slots sit on whole points with the glyph at its exact place inside, so the digits land on the glow's cut-outs.
        let top = origin.y.rounded(.down)
        let baseline = self.baseline + origin.y - top
        let offsets = label.glyphOffsets
        var items: [BoardContentItem] = []
        for (index, character) in characters.enumerated() {
            let start = index < offsets.count ? offsets[index] : label.width
            let end = index + 1 < offsets.count ? offsets[index + 1] : label.width
            let left = (origin.x + start - pad).rounded(.down)
            let x = origin.x + start - left
            let frame = CGRect(x: left, y: top, width: (x + end - start + pad).rounded(.up), height: (height + origin.y - top).rounded(.up))
            let glyph = String(character)
            let revision = AnyHashable([AnyHashable(glyph), AnyHashable(look.font), AnyHashable(look.fringes), AnyHashable(x),
                                        AnyHashable(baseline), AnyHashable([look.color.red, look.color.green, look.color.blue, look.color.alpha])])
            var item = BoardContentItem(key: "num\(characters.count).\(index)", frame: frame, motion: .none, revision: revision) { context, _ in
                Self.draw(glyph, look: look, color: look.color, glow: nil, blur: 0, x: x, baseline: baseline, in: context)
            }
            item.clips = true
            item.hot = BoardContentDrawing { context, _ in
                Self.draw(glyph, look: look, color: look.hot, glow: look.hotGlow, blur: look.hotBlur, x: x, baseline: baseline, in: context)
            }
            if rolling, let previous, previous[index] != character { item.accents.append(.roll(rising: rising)) }
            if punch { item.accents.append(.punch(anchor: anchor)) }
            items.append(item)
        }
        var fx = BoardContentItem(key: "num.fx", frame: rect, motion: .none, revision: AnyHashable("fx")) { _, _ in }
        if memory.fresh {
            fx.accents = decals.map { .decal($0) }
            if punch { fx.accents.append(.punch(anchor: anchor)) }
        }
        items.append(fx)
        return items
    }

    /// The glow under the digits, without the glyphs (drawn by the hero item at `x`, `top` in its coordinates).
    func drawGlow(in context: CGContext, x: CGFloat, top: CGFloat, color: RGBColor, blurs: [CGFloat]) {
        let blurs = blurs.filter { $0 > 0 }
        guard !blurs.isEmpty else { return }
        label.drawGlowOnly(in: context, x: x, baseline: top + baseline, glow: color, blurs: blurs)
    }

    /// The gold sheen across the digits of a new peak (the prototypes' `.num.gild`, 1.3 s).
    func sheen(highlight: RGBColor = RGBColor(hex: 0xFFE58A)) -> BoardDecal {
        let width = rect.width
        return BoardDecal(frame: CGRect(origin: .zero, size: rect.size), motion: .sweep(seconds: 1.3), celebration: true,
                          mask: label.outline(x: 0, baseline: baseline), bandWidth: max(24, width * 0.5)) { context, size in
            BoardDrawing.gradient(CGRect(origin: .zero, size: size),
                                  stops: [(highlight.opacity(0), 0), (highlight, 0.32), (.white, 0.5), (highlight, 0.68), (highlight.opacity(0), 1)],
                                  from: .zero, to: CGPoint(x: size.width, y: 0), in: context)
        }
    }

    /// `boards.newPeak` popping above the number for 2.2 s (`.peakchip`); in the number's points, its top-left at `origin`
    /// when given.
    static func peakChip(style: BoardStyle, l10n: Localizer, glows: Bool, origin: CGPoint? = nil) -> BoardDecal {
        let glass = style == .glass
        let font = glass ? BoardFont(size: 10, weight: .bold, tracking: 0.08) : BoardFont(family: .mono, size: 9.5, weight: .bold, tracking: 0.16)
        let label = BoardLabel(l10n("boards.newPeak"), font, color: RGBColor(hex: 0x241A00))
        let chip = CGSize(width: label.width + 12, height: font.size + 6)
        let margin: CGFloat = glows ? 14 : 0
        let corner = origin ?? CGPoint(x: 0, y: glass ? -14 : -13)
        let frame = CGRect(x: corner.x - margin, y: corner.y - margin, width: chip.width + 2 * margin, height: chip.height + 2 * margin)
        return BoardDecal(frame: frame, motion: .pop(seconds: 2.2), celebration: true) { context, _ in
            let rect = CGRect(x: margin, y: margin, width: chip.width, height: chip.height)
            BoardDrawing.glowing(glows ? BoardPalette.gold.opacity(glass ? 0.45 : 0.7) : nil, blur: glass ? 14 : 16, in: context) {
                BoardDrawing.fill(rect, radius: glass ? 6 : 4, color: BoardPalette.gold, in: $0)
            }
            if glass {
                BoardDrawing.gradient(rect, radius: 6, stops: [(RGBColor(hex: 0xFFE58A), 0), (BoardPalette.gold, 1)],
                                      from: CGPoint(x: rect.minX, y: 0), to: CGPoint(x: rect.maxX, y: 0), in: context)
            }
            label.draw(in: context, x: rect.minX + 6, baseline: label.baseline(centeredOn: rect.midY))
        }
    }

    private static func draw(_ glyph: String, look: Look, color: RGBColor, glow: RGBColor?, blur: CGFloat, x: CGFloat,
                             baseline: CGFloat, in context: CGContext) {
        if look.fringes {
            BoardLabel(glyph, look.font, color: RGBColor(red: 1, green: 40 / 255, blue: 110 / 255, alpha: 0.75))
                .draw(in: context, x: x - 1.5, baseline: baseline)
            BoardLabel(glyph, look.font, color: RGBColor(red: 0, green: 225 / 255, blue: 1, alpha: 0.75))
                .draw(in: context, x: x + 1.5, baseline: baseline)
        }
        BoardLabel(glyph, look.font, color: color).draw(in: context, x: x, baseline: baseline, glow: glow, blur: blur)
    }
}
