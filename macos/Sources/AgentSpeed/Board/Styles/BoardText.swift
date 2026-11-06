import AppKit
import CoreText

/// A type style of the prototypes' CSS: family, size, weight, letter spacing (em) and tabular digits.
struct BoardFont: Hashable {
    enum Family: Hashable {
        /// SF Pro (`--ui`, `--num`, `--disp`).
        case system
        /// SF Mono (`--mono`, neon's `--num`).
        case mono
    }

    var family = Family.system
    var size: CGFloat
    var weight: NSFont.Weight = .regular
    /// CSS `letter-spacing` in em.
    var tracking: CGFloat = 0
    var tabular = false

    @MainActor private static var cache: [BoardFont: NSFont] = [:]

    @MainActor
    var font: NSFont {
        if let font = Self.cache[self] { return font }
        let font = switch family {
        case .system: tabular ? NSFont.monospacedDigitSystemFont(ofSize: size, weight: weight)
            : NSFont.systemFont(ofSize: size, weight: weight)
        case .mono: NSFont.monospacedSystemFont(ofSize: size, weight: weight)
        }
        Self.cache[self] = font
        return font
    }
}

/// One line of text laid out with Core Text, drawn into the board's y-down contexts.
@MainActor
struct BoardLabel {
    let line: CTLine
    let width: CGFloat
    let font: NSFont
    let text: String
    let style: BoardFont
    let color: RGBColor
    private let attributes: [NSAttributedString.Key: Any]

    init(_ text: String, _ style: BoardFont, color: RGBColor) {
        let key = Key(text: text, font: style, color: [color.red, color.green, color.blue, color.alpha])
        if let cached = Self.cache[key] {
            self = cached
            return
        }
        var attributes: [NSAttributedString.Key: Any] = [
            .font: style.font,
            NSAttributedString.Key(kCTForegroundColorAttributeName as String): color.cgColor,
        ]
        if style.tracking != 0 { attributes[.kern] = style.tracking * style.size }
        self.init(line: CTLineCreateWithAttributedString(NSAttributedString(string: text, attributes: attributes)),
                  font: style.font, attributes: attributes, text: text, style: style, color: color)
        // Boards lay out the same names, values and labels on every tick: a line laid out once is reused.
        if Self.cache.count >= Self.cacheLimit { Self.cache.removeAll(keepingCapacity: true) }
        Self.cache[key] = self
    }

    private struct Key: Hashable {
        var text: String
        var font: BoardFont
        var color: [Double]
    }

    private static var cache: [Key: BoardLabel] = [:]
    private static let cacheLimit = 1024

    private init(line: CTLine, font: NSFont, attributes: [NSAttributedString.Key: Any], text: String, style: BoardFont,
                 color: RGBColor) {
        self.line = line
        self.font = font
        self.attributes = attributes
        self.text = text
        self.style = style
        self.color = color
        width = CGFloat(CTLineGetTypographicBounds(line, nil, nil, nil))
    }

    var isEmpty: Bool { width <= 0 }

    /// The line cut to `limit` with an ellipsis (CSS `text-overflow: ellipsis`); empty when not even that fits.
    func truncated(to limit: CGFloat) -> BoardLabel {
        guard width > limit + 0.5 else { return self }
        let token = CTLineCreateWithAttributedString(NSAttributedString(string: "…", attributes: attributes))
        let cut = limit > 0 ? CTLineCreateTruncatedLine(line, Double(limit), .end, token) : nil
        return BoardLabel(line: cut ?? CTLineCreateWithAttributedString(NSAttributedString(string: "")), font: font,
                          attributes: attributes, text: text, style: style, color: color)
    }

    /// The baseline that centers the line box on `midY` (CSS `align-items: center`).
    func baseline(centeredOn midY: CGFloat) -> CGFloat {
        midY + (font.ascender + font.descender) / 2
    }

    /// The baseline of a CSS `line-height: 1` box whose top is at `top`.
    func baseline(lineBoxTop top: CGFloat) -> CGFloat {
        top + (font.pointSize - (font.ascender - font.descender)) / 2 + font.ascender
    }

    func draw(in context: CGContext, x: CGFloat, baseline: CGFloat) {
        context.saveGState()
        context.textMatrix = CGAffineTransform(scaleX: 1, y: -1)
        context.textPosition = CGPoint(x: x, y: baseline)
        CTLineDraw(line, context)
        context.restoreGState()
    }

    /// Draws the line with a glow (CSS `text-shadow: 0 0 blur color`).
    func draw(in context: CGContext, x: CGFloat, baseline: CGFloat, glow: RGBColor?, blur: CGFloat) {
        if let glow, blur > 0 { BoardGlow.fill(glow, blur: blur, in: context) { draw(in: $0, x: x, baseline: baseline) } }
        draw(in: context, x: x, baseline: baseline)
    }

    /// Draws only the glows of the glyphs (several `blurs` in one color, back to front), with the glyphs cut out of
    /// them: another item draws the glyphs.
    func drawGlowOnly(in context: CGContext, x: CGFloat, baseline: CGFloat, glow: RGBColor, blurs: [CGFloat]) {
        context.saveGState()
        context.beginTransparencyLayer(auxiliaryInfo: nil)
        BoardGlow.fill(blurs.map { BoardGlow.Layer(color: glow, blur: $0) }, in: context) { draw(in: $0, x: x, baseline: baseline) }
        context.setBlendMode(.destinationOut)
        draw(in: context, x: x, baseline: baseline)
        context.endTransparencyLayer()
        context.restoreGState()
    }

    func drawGlowOnly(in context: CGContext, x: CGFloat, baseline: CGFloat, glow: RGBColor, blur: CGFloat) {
        drawGlowOnly(in: context, x: x, baseline: baseline, glow: glow, blurs: [blur])
    }

    /// Where each character's glyph starts (caret offsets sit mid-way in the tracking, glyphs do not).
    var glyphOffsets: [CGFloat] {
        var offsets = [CGFloat](repeating: 0, count: CTLineGetStringRange(line).length)
        for run in CTLineGetGlyphRuns(line) as? [CTRun] ?? [] {
            let count = CTRunGetGlyphCount(run)
            var positions = [CGPoint](repeating: .zero, count: count)
            var indices = [CFIndex](repeating: 0, count: count)
            CTRunGetPositions(run, CFRange(location: 0, length: 0), &positions)
            CTRunGetStringIndices(run, CFRange(location: 0, length: 0), &indices)
            for (index, position) in zip(indices, positions) where index < offsets.count { offsets[index] = position.x }
        }
        return offsets
    }

    /// Fills the glyphs with a linear gradient from `start` to `end` (CSS `background-clip: text`).
    func drawGradient(in context: CGContext, x: CGFloat, baseline: CGFloat, colors: [RGBColor], start: CGPoint, end: CGPoint) {
        guard let gradient = CGGradient(colorsSpace: CGColorSpace(name: CGColorSpace.sRGB),
                                        colors: colors.map(\.cgColor) as CFArray, locations: nil) else { return }
        context.saveGState()
        context.addPath(outline(x: x, baseline: baseline))
        context.clip()
        context.drawLinearGradient(gradient, start: start, end: end, options: [.drawsBeforeStartLocation, .drawsAfterEndLocation])
        context.restoreGState()
    }

    /// The glyphs' outlines at `x` and `baseline` (y down).
    func outline(x: CGFloat, baseline: CGFloat) -> CGPath {
        let path = CGMutablePath()
        for run in CTLineGetGlyphRuns(line) as? [CTRun] ?? [] {
            let count = CTRunGetGlyphCount(run)
            guard count > 0,
                  let font = (CTRunGetAttributes(run) as NSDictionary)[kCTFontAttributeName as String] else { continue }
            var glyphs = [CGGlyph](repeating: 0, count: count)
            var positions = [CGPoint](repeating: .zero, count: count)
            CTRunGetGlyphs(run, CFRange(location: 0, length: count), &glyphs)
            CTRunGetPositions(run, CFRange(location: 0, length: count), &positions)
            for index in 0..<count {
                guard let glyph = CTFontCreatePathForGlyph(font as! CTFont, glyphs[index], nil) else { continue }
                let transform = CGAffineTransform(translationX: x + positions[index].x, y: baseline - positions[index].y)
                    .scaledBy(x: 1, y: -1)
                path.addPath(glyph, transform: transform)
            }
        }
        return path
    }
}
