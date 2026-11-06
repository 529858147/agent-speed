import AgentSpeedOrbModel
import AppKit

/// A live metric's number as one clipped item per character (DESIGN 2c Live metrics, *Updates, motion and quiet*):
/// on a fresh tick the changed characters roll in their slots like the hero's digits (0.24 s, up when the value rises),
/// without the hero's white-hot flash; a change in the number of characters, or between `–` or the tag and a number,
/// crossfades in 0.15 s instead.
@MainActor
struct BoardRollingText {
    static let crossfade = 0.15

    let key: String
    let label: BoardLabel
    /// The text's left edge and baseline, board points.
    let x: CGFloat
    let baseline: CGFloat

    init(key: String, text: String, font: BoardFont, color: RGBColor, x: CGFloat, baseline: CGFloat) {
        self.key = key
        label = BoardLabel(text, font, color: color)
        self.x = x
        self.baseline = baseline
    }

    /// Right-aligned at `right`.
    init(key: String, text: String, font: BoardFont, color: RGBColor, right: CGFloat, baseline: CGFloat) {
        let label = BoardLabel(text, font, color: color)
        self.init(key: key, label: label, x: right - label.width, baseline: baseline)
    }

    /// Centred on `center`.
    init(key: String, text: String, font: BoardFont, color: RGBColor, center: CGFloat, baseline: CGFloat) {
        let label = BoardLabel(text, font, color: color)
        self.init(key: key, label: label, x: center - label.width / 2, baseline: baseline)
    }

    private init(key: String, label: BoardLabel, x: CGFloat, baseline: CGFloat) {
        self.key = key
        self.label = label
        self.x = x
        self.baseline = baseline
    }

    var text: String { label.text }
    var width: CGFloat { label.width }

    /// Characters that roll: digits, separators and the METRICS 7 suffixes.
    static func rolls(_ text: String) -> Bool {
        !text.isEmpty && text.allSatisfy { $0.isASCII && ($0.isNumber || ".,kMB".contains($0)) }
    }

    /// The value behind a shown number (`1.2k` → 1200), for the roll's direction.
    static func value(_ text: String) -> Double? {
        var digits = text.replacingOccurrences(of: ",", with: "")
        var scale = 1.0
        if let last = digits.last, let multiplier = ["k": 1e3, "M": 1e6, "B": 1e9][String(last)] {
            scale = multiplier
            digits.removeLast()
        }
        return Double(digits).map { $0 * scale }
    }

    /// The slots' items. `previous`: the text the last fresh tick showed here (nil: nothing to roll from).
    func items(previous: String?, fresh: Bool, motion: BoardContentItem.Motion, group: BoardContentItem.Group = .hero)
        -> [BoardContentItem] {
        let characters = Array(text)
        let old = previous.map(Array.init)
        let animate = fresh && previous != nil && previous != text
        let rolling = animate && old?.count == characters.count && Self.rolls(text) && Self.rolls(previous ?? "")
        let rising = (Self.value(text) ?? 0) >= (previous.flatMap(Self.value) ?? 0)
        let font = label.font
        let size = font.pointSize
        // A slot is the number's 1.1 em box (the prototypes' `.mn`), on whole points with the glyph at its place.
        let boxTop = baseline - font.ascender - (1.1 * size - (font.ascender - font.descender)) / 2
        let top = boxTop.rounded(.down)
        let height = (boxTop + 1.1 * size - top).rounded(.up)
        let inSlot = baseline - top
        let offsets = label.glyphOffsets
        let color = label.color
        let style = label.style
        var items: [BoardContentItem] = []
        for (index, character) in characters.enumerated() {
            let start = index < offsets.count ? offsets[index] : label.width
            let end = index + 1 < offsets.count ? offsets[index + 1] : label.width
            let left = (x + start - 1).rounded(.down)
            let glyphX = x + start - left
            let frame = CGRect(x: left, y: top, width: (glyphX + end - start + 1).rounded(.up), height: height)
            let glyph = String(character)
            let revision = AnyHashable([AnyHashable(glyph), AnyHashable(style), AnyHashable(glyphX), AnyHashable(inSlot),
                                        AnyHashable([color.red, color.green, color.blue, color.alpha])])
            var item = BoardContentItem(key: "\(key)\(characters.count).\(index)", frame: frame, motion: motion,
                                        revision: revision) { context, _ in
                BoardLabel(glyph, style, color: color).draw(in: context, x: glyphX, baseline: inSlot)
            }
            item.group = group
            item.clips = true
            if rolling, let old, old[index] != character {
                item.accents.append(.roll(rising: rising))
            } else if animate, !rolling, old?.count == characters.count {
                item.accents.append(.crossfade(Self.crossfade))
            }
            items.append(item)
        }
        return items
    }

    /// The box the glyphs paint, grown by `margin` (glows), board points.
    func box(margin: CGFloat) -> CGRect {
        label.box(x: x, baseline: baseline, margin: margin)
    }
}
