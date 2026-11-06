import AgentSpeedOrbModel
import AppKit

/// One readout of the sub-gauge as text (DESIGN 2c Live metrics, *Values*): the label, the number or `–` or the tag,
/// the unit, `≥`, and whether it is drawn quietly (`–`, a real `0`, the tag: `ink-3`, no glow).
struct BoardGaugeReading: Equatable {
    var label: String
    var number: String
    var unit: String
    var atLeast = false
    var quiet = false
    /// The subscription tag (UI font, 0.8 × the number's size).
    var tag = false
}

/// The glass and neon sub-gauge's table (DESIGN 2c Live metrics, *Table cells*): one block of fixed cells centred on
/// the sub-gauge's centre line — label, `≥` (always reserved), number (as wide as `0.00088`; values right-aligned),
/// unit — so values never move it. The cells are measured for the switched-on rows when the language, currency, font
/// or switches change; a number wider than its cell widens it at once and it narrows 6 s later.
@MainActor
struct BoardGaugeTable {
    static let numberSample = "0.00088"

    let style: BoardStyle
    let metrics: BoardGaugeMetrics
    let palette: BoardPalette
    let labelFont: BoardFont
    let numberFont: BoardFont
    let unitFont: BoardFont
    let atLeastFont: BoardFont
    let tagFont: BoardFont
    let labelCell: CGFloat
    let atLeastCell: CGFloat
    let unitCell: CGFloat
    /// The number cell's width before values widen it.
    let numberBase: CGFloat

    /// `labels` and `units`: those of every switched-on table row (思考 hiding at zero keeps its cells).
    init(style: BoardStyle, size: BoardSize, palette: BoardPalette, labels: [String], units: [String]) {
        self.style = style
        metrics = BoardGaugeMetrics(style, size)
        self.palette = palette
        let neon = style == .neon
        let number = CGFloat(metrics.tableNumberSize)
        labelFont = BoardFont(size: CGFloat(metrics.tableLabelSize), weight: .semibold, tracking: CGFloat(metrics.tableLabelTracking))
        numberFont = neon ? BoardFont(family: .mono, size: number, weight: .bold, tracking: -0.02)
            : BoardFont(size: number, weight: .semibold, tabular: true)
        unitFont = neon ? BoardFont(family: .mono, size: CGFloat(metrics.tableUnitSize), weight: .semibold)
            : BoardFont(size: CGFloat(metrics.tableUnitSize), weight: .medium)
        atLeastFont = BoardFont(size: number * CGFloat(BoardGaugeMetrics.atLeastScale))
        tagFont = BoardFont(size: number * 0.8, weight: .semibold)
        let ink = palette.ink3
        let labelStyle = labelFont, unitStyle = unitFont
        labelCell = (labels.map { BoardLabel($0, labelStyle, color: ink).width }.max() ?? 0) + CGFloat(metrics.labelGap)
        atLeastCell = BoardLabel("≥", atLeastFont, color: ink).width + 1
        unitCell = CGFloat(metrics.unitGap) + (units.map { BoardLabel($0, unitStyle, color: ink).width }.max() ?? 0)
        numberBase = BoardLabel(Self.numberSample, numberFont, color: ink).width.rounded(.up)
    }

    /// The width a reading's number needs.
    func need(_ reading: BoardGaugeReading) -> CGFloat {
        BoardLabel(reading.number, reading.tag ? tagFont : numberFont, color: palette.ink).width.rounded(.up)
    }

    func width(numberCell: CGFloat) -> CGFloat { labelCell + atLeastCell + numberCell + unitCell }

    /// The number's color: the readout's ink, quiet values in the quiet ink.
    private func numberColor(_ reading: BoardGaugeReading) -> RGBColor {
        reading.quiet ? palette.quiet : style == .neon ? palette.led : palette.ink
    }

    /// The items of one row: its label, `≥` and unit (one cached piece), its number's rolling characters and, on dark
    /// neon, the digits' glow. `left` is the block's left edge, `baseline` the row's (board points).
    func items(_ reading: BoardGaugeReading, key: String, left: CGFloat, numberCell: CGFloat, baseline: CGFloat,
               previous: String?, fresh: Bool) -> [BoardContentItem] {
        let palette = self.palette
        let labelFont = self.labelFont, unitFont = self.unitFont, atLeastFont = self.atLeastFont
        let label = BoardLabel(reading.label, labelFont, color: palette.ink3)
        let atLeastX = left + labelCell
        let numberRight = atLeastX + atLeastCell + numberCell
        let unitX = numberRight + CGFloat(metrics.unitGap)
        let unit = BoardLabel(reading.unit, unitFont, color: palette.ink3)
        let atLeast = BoardLabel("≥", atLeastFont, color: palette.ink3)
        let width = labelCell + atLeastCell + numberCell + unitCell
        let rect = CGRect(x: left - 2, y: baseline - 12, width: width + 4, height: 16)
        var frame = BoardContentItem(key: key + ".frame", frame: rect.integral, motion: .glide(fade: 0.25),
                                     revision: AnyHashable([AnyHashable(reading.label), AnyHashable(reading.unit),
                                                            AnyHashable(reading.atLeast), AnyHashable(labelCell),
                                                            AnyHashable(numberCell), AnyHashable(palette.dark),
                                                            AnyHashable(palette.highContrast)])) { context, _ in
            let origin = rect.integral.origin
            context.translateBy(x: -origin.x, y: -origin.y)
            label.draw(in: context, x: left, baseline: baseline)
            if reading.atLeast { atLeast.draw(in: context, x: atLeastX, baseline: baseline) }
            if !reading.unit.isEmpty { unit.draw(in: context, x: unitX, baseline: baseline) }
        }
        frame.group = .hero
        let number = BoardRollingText(key: key + ".n", text: reading.number, font: reading.tag ? tagFont : numberFont,
                                      color: numberColor(reading), right: numberRight, baseline: baseline)
        var items = [frame]
        if style == .neon, palette.dark, palette.glows, !reading.quiet {
            var glow = BoardContentItem.glow(key + ".glow", label: number.label, x: number.x, baseline: baseline,
                                             layers: [BoardGlow.Layer(color: BoardPalette.ledGlow, blur: 6)])
            glow.motion = .glide(fade: 0.25)
            items.append(glow)
        }
        items += number.items(previous: previous, fresh: fresh, motion: .glide(fade: BoardRollingText.crossfade))
        return items
    }
}

extension BoardContentItem {
    /// The glow of a line of text without its glyphs (soft, at half resolution), for digits drawn by other items.
    @MainActor
    static func glow(_ key: String, label: BoardLabel, x: CGFloat, baseline: CGFloat, layers: [BoardGlow.Layer]) -> BoardContentItem {
        let reach = (layers.map(\.blur).max() ?? 0) * 1.5
        let rect = label.box(x: x, baseline: baseline, margin: reach).integral
        let colors = layers.map { [$0.color.red, $0.color.green, $0.color.blue, $0.color.alpha, Double($0.blur)] }
        var item = BoardContentItem(key: key, frame: rect, motion: .none,
                                    revision: AnyHashable([AnyHashable(label.text), AnyHashable(label.style), AnyHashable(x - rect.minX),
                                                           AnyHashable(baseline - rect.minY), AnyHashable(colors)])) { context, _ in
            context.saveGState()
            context.beginTransparencyLayer(auxiliaryInfo: nil)
            BoardGlow.fill(layers, in: context) { label.draw(in: $0, x: x - rect.minX, baseline: baseline - rect.minY) }
            context.setBlendMode(.destinationOut)
            label.draw(in: context, x: x - rect.minX, baseline: baseline - rect.minY)
            context.endTransparencyLayer()
            context.restoreGState()
        }
        item.group = .hero
        item.rasterScale = BoardContentItem.softScale
        return item
    }
}
