import AgentSpeedOrbModel
import AppKit

/// The hero's history drawings (DESIGN 2c Content 1), with the geometry of `BoardHistoryShape`.
@MainActor
enum BoardHeroHistory {
    /// Glass: the sparkline with its fading area and the latest point (the prototype's `drawSpark`).
    static func sparkline(_ values: [Double], in rect: CGRect, color: RGBColor, context: CGContext) {
        let points = BoardHistoryShape.sparkline(values, width: rect.width, height: rect.height)
            .map { CGPoint(x: rect.minX + $0.x, y: rect.minY + $0.y) }
        guard let first = points.first, let last = points.last else { return }
        let line = CGMutablePath()
        line.move(to: first)
        for index in 1..<points.count {
            let a = points[index - 1], b = points[index]
            line.addQuadCurve(to: CGPoint(x: (a.x + b.x) / 2, y: (a.y + b.y) / 2), control: a)
        }
        line.addLine(to: last)

        let area = line.mutableCopy() ?? CGMutablePath()
        area.addLine(to: CGPoint(x: rect.maxX, y: rect.maxY))
        area.addLine(to: CGPoint(x: rect.minX, y: rect.maxY))
        area.closeSubpath()
        context.saveGState()
        context.addPath(area)
        context.clip()
        if let gradient = CGGradient(colorsSpace: CGColorSpace(name: CGColorSpace.sRGB),
                                     colors: [color.opacity(0.35).cgColor, color.opacity(0).cgColor] as CFArray, locations: [0, 1]) {
            context.drawLinearGradient(gradient, start: CGPoint(x: 0, y: rect.minY), end: CGPoint(x: 0, y: rect.maxY), options: [])
        }
        context.restoreGState()

        context.setStrokeColor(color.cgColor)
        context.setLineWidth(1.5)
        context.setLineJoin(.round)
        context.addPath(line)
        context.strokePath()
        BoardDrawing.fillCircle(CGRect(x: last.x - 2.4, y: last.y - 2.4, width: 4.8, height: 4.8), color: color, in: context)
    }

    /// Glass's strip under the hero (DESIGN 2c Live metrics, *Hero*): the 60 s sparkline stretched over `rect`, a
    /// 1.25 pt line in the heat color over its area fading from 28 % at the top to 0, no end dot.
    static func strip(_ values: [Double], in rect: CGRect, color: RGBColor, context: CGContext) {
        let points = BoardHistoryShape.strip(values, width: rect.width, height: rect.height)
            .map { CGPoint(x: rect.minX + $0.x, y: rect.minY + $0.y) }
        guard let first = points.first, let last = points.last else { return }
        let line = CGMutablePath()
        line.move(to: first)
        for index in 1..<points.count {
            let a = points[index - 1], b = points[index]
            line.addQuadCurve(to: CGPoint(x: (a.x + b.x) / 2, y: (a.y + b.y) / 2), control: a)
        }
        line.addLine(to: last)
        let area = line.mutableCopy() ?? CGMutablePath()
        area.addLine(to: CGPoint(x: rect.maxX, y: rect.maxY))
        area.addLine(to: CGPoint(x: rect.minX, y: rect.maxY))
        area.closeSubpath()
        context.saveGState()
        context.addPath(area)
        context.clip()
        if let gradient = CGGradient(colorsSpace: CGColorSpace(name: CGColorSpace.sRGB),
                                     colors: [color.opacity(0.28).cgColor, color.opacity(0).cgColor] as CFArray, locations: [0, 1]) {
            context.drawLinearGradient(gradient, start: CGPoint(x: 0, y: rect.minY), end: CGPoint(x: 0, y: rect.maxY), options: [])
        }
        context.restoreGState()
        context.setStrokeColor(color.cgColor)
        context.setLineWidth(1.25)
        context.setLineJoin(.round)
        context.setLineCap(.round)
        context.addPath(line)
        context.strokePath()
    }

    /// Timing's strip: the 30 one-second bars, 3 wide, the first at the left inset and the last ending at the right
    /// one, bottoms on the strip's bottom, in the heat color at 85 %.
    static func stripBars(_ values: [Double], in rect: CGRect, color: RGBColor, context: CGContext) {
        let count = 30
        let heights = BoardHistoryShape.stripBars(values, count: count, height: rect.height)
        let width: CGFloat = 3
        let step = (rect.width - width) / CGFloat(count - 1)
        context.setFillColor(color.opacity(0.85).cgColor)
        for (index, height) in heights.enumerated() {
            let bar = CGRect(x: rect.minX + CGFloat(index) * step, y: rect.maxY - height, width: width, height: height)
            context.addPath(CGPath(roundedRect: bar, cornerWidth: min(1, height / 2), cornerHeight: min(1, height / 2), transform: nil))
        }
        context.fillPath()
    }

    /// Timing: one-second bars, right-aligned in `rect`, bottoms on its bottom edge.
    static func bars(_ values: [Double], count: Int, in rect: CGRect, color: RGBColor, context: CGContext) {
        let heights = BoardHistoryShape.bars(values, count: count, height: rect.height)
        let width: CGFloat = 3, gap: CGFloat = 2
        var x = rect.maxX - CGFloat(count) * width - CGFloat(count - 1) * gap
        context.setFillColor(color.opacity(0.85).cgColor)
        for height in heights {
            let bar = CGRect(x: x, y: rect.maxY - height, width: width, height: height)
            context.addPath(CGPath(roundedRect: bar, cornerWidth: 1, cornerHeight: 1, transform: nil))
            x += width + gap
        }
        context.fillPath()
    }

    /// Neon: 24 segments across `rect`, lit to the heat in the heat ramp, today's peak marked white-hot. `glows` draws
    /// only the glows of the lit segments and the peak (a soft layer under the crisp segments), else the segments.
    static func led(lit: Int, peak: Int?, glows: Bool, in rect: CGRect, palette: BoardPalette, context: CGContext) {
        let segments = 24
        let gap: CGFloat = 2
        let width = (rect.width - gap * CGFloat(segments - 1)) / CGFloat(segments)
        func segment(_ index: Int) -> CGRect {
            CGRect(x: rect.minX + CGFloat(index) * (width + gap), y: rect.minY, width: width, height: rect.height)
        }
        func color(_ index: Int) -> RGBColor { palette.heat(Double(index + 1) / Double(segments)) }
        let peakColor = palette.dark ? HeatPalette.whiteHot : PaletteInk.peakLight
        guard !glows else {
            if lit > 0 {
                BoardGlow.fillColored(blur: 6, in: context) { context in
                    for index in 0..<lit { BoardDrawing.fill(segment(index), radius: 1.5, color: color(index), in: context) }
                }
            }
            if let peak {
                BoardGlow.fill(peakColor, blur: 8, in: context) { BoardDrawing.fill(segment(peak), radius: 1.5, color: peakColor, in: $0) }
            }
            return
        }
        for index in 0..<segments {
            let fill = index < lit ? color(index) : index == peak ? peakColor : palette.unlit
            BoardDrawing.fill(segment(index), radius: 1.5, color: fill, in: context)
        }
    }

    private enum PaletteInk {
        /// The peak segment on light boards: white-hot would vanish, so a deep gold.
        static let peakLight = RGBColor(hex: 0xC99A06)
    }
}
