import AgentSpeedCore
import AppKit

/// Drawing pieces the three styles share (y-down contexts of `BoardRaster`).
@MainActor
enum BoardDrawing {
    static func fill(_ rect: CGRect, radius: CGFloat = 0, color: RGBColor, in context: CGContext) {
        context.setFillColor(color.cgColor)
        if radius > 0 {
            context.addPath(CGPath(roundedRect: rect, cornerWidth: min(radius, rect.width / 2), cornerHeight: min(radius, rect.height / 2),
                                   transform: nil))
            context.fillPath()
        } else {
            context.fill(rect)
        }
    }

    static func stroke(_ rect: CGRect, radius: CGFloat, width: CGFloat, color: RGBColor, in context: CGContext) {
        let inset = rect.insetBy(dx: width / 2, dy: width / 2)
        context.setStrokeColor(color.cgColor)
        context.setLineWidth(width)
        context.addPath(CGPath(roundedRect: inset, cornerWidth: max(0, radius - width / 2), cornerHeight: max(0, radius - width / 2),
                               transform: nil))
        context.strokePath()
    }

    /// A linear gradient across `rect` (clipped to its rounded shape) through `stops` (color, location).
    static func gradient(_ rect: CGRect, radius: CGFloat = 0, stops: [(RGBColor, CGFloat)], from start: CGPoint, to end: CGPoint,
                         in context: CGContext) {
        guard let gradient = CGGradient(colorsSpace: CGColorSpace(name: CGColorSpace.sRGB),
                                        colors: stops.map(\.0.cgColor) as CFArray, locations: stops.map(\.1)) else { return }
        context.saveGState()
        context.addPath(CGPath(roundedRect: rect, cornerWidth: min(radius, rect.width / 2), cornerHeight: min(radius, rect.height / 2),
                               transform: nil))
        context.clip()
        context.drawLinearGradient(gradient, start: start, end: end, options: [.drawsBeforeStartLocation, .drawsAfterEndLocation])
        context.restoreGState()
    }

    /// A soft radial light (CSS `radial-gradient(closest-side, color, transparent)` under a blur): its alpha falls
    /// linearly to the edge.
    static func radial(center: CGPoint, radius: CGSize, color: RGBColor, in context: CGContext) {
        guard radius.width > 0, radius.height > 0,
              let gradient = CGGradient(colorsSpace: CGColorSpace(name: CGColorSpace.sRGB),
                                        colors: [color.cgColor, color.opacity(0).cgColor] as CFArray,
                                        locations: [0, 1]) else { return }
        context.saveGState()
        context.translateBy(x: center.x, y: center.y)
        context.scaleBy(x: 1, y: radius.height / radius.width)
        context.drawRadialGradient(gradient, startCenter: .zero, startRadius: 0, endCenter: .zero, endRadius: radius.width, options: [])
        context.restoreGState()
    }

    /// Draws `body` (in the context it is given) with a glow (CSS `box-shadow` / `drop-shadow` of `blur`) under it, or
    /// plainly when `color` is nil.
    static func glowing(_ color: RGBColor?, blur: CGFloat, in context: CGContext, _ body: (CGContext) -> Void) {
        if let color, blur > 0 { BoardGlow.fill(color, blur: blur, in: context, shapes: body) }
        body(context)
    }

    // MARK: Phase badge (DESIGN 4: three dots while generating, a wrench in tools, a grey dot when idle)

    enum BadgeLook {
        /// Glass: three source-colored dots.
        case dots
        /// Neon: one glowing dot.
        case glowDot
    }

    /// The badge in a 10 × 10 box at `origin`.
    static func badge(_ phase: AgentPhase, color: RGBColor, idle: RGBColor, look: BadgeLook, glows: Bool, at origin: CGPoint,
                      in context: CGContext) {
        switch phase {
        case .generating where look == .dots:
            for (index, level) in [1.0, 0.72, 0.48].enumerated() {
                let x = origin.x - 1 + CGFloat(index) * 4.5
                fillCircle(CGRect(x: x, y: origin.y + 3.5, width: 3, height: 3), color: color.opacity(level), in: context)
            }
        case .generating:
            glowing(glows ? color : nil, blur: 7, in: context) {
                fillCircle(CGRect(x: origin.x + 2, y: origin.y + 2, width: 6, height: 6), color: color, in: $0)
            }
        case .tools:
            if let wrench = symbol("wrench.adjustable.fill", size: 8.5, color: color) {
                let size = wrench.size
                wrench.draw(in: CGRect(x: origin.x + 5 - size.width / 2, y: origin.y + 5 - size.height / 2, width: size.width,
                                       height: size.height), from: .zero, operation: .sourceOver, fraction: 1,
                            respectFlipped: true, hints: nil)
            }
        case .idle:
            fillCircle(CGRect(x: origin.x + 3, y: origin.y + 3, width: 4, height: 4), color: idle, in: context)
        }
    }

    static func fillCircle(_ rect: CGRect, color: RGBColor, in context: CGContext) {
        context.setFillColor(color.cgColor)
        context.fillEllipse(in: rect)
    }

    private struct SymbolKey: Hashable {
        var name: String
        var size: CGFloat
        var color: [Double]
    }

    private static var symbols: [SymbolKey: NSImage] = [:]

    static func symbol(_ name: String, size: CGFloat, color: RGBColor) -> NSImage? {
        let key = SymbolKey(name: name, size: size, color: [color.red, color.green, color.blue, color.alpha])
        if let image = symbols[key] { return image }
        let configuration = NSImage.SymbolConfiguration(pointSize: size, weight: .semibold)
            .applying(NSImage.SymbolConfiguration(paletteColors: [color.nsColor]))
        guard let image = NSImage(systemSymbolName: name, accessibilityDescription: nil)?.withSymbolConfiguration(configuration)
        else { return nil }
        if symbols.count > 64 { symbols.removeAll() }
        symbols[key] = image
        return image
    }

    // MARK: Rows

    /// Glass and neon sub rows' elbow from the parent: down from above the row, then right to the name.
    static func connector(x: CGFloat, midY: CGFloat, color: RGBColor, in context: CGContext) {
        let path = CGMutablePath()
        path.move(to: CGPoint(x: x + 0.5, y: midY - 23))
        path.addLine(to: CGPoint(x: x + 0.5, y: midY - 5))
        path.addQuadCurve(to: CGPoint(x: x + 5.5, y: midY - 0.5), control: CGPoint(x: x + 0.5, y: midY - 0.5))
        path.addLine(to: CGPoint(x: x + 7.5, y: midY - 0.5))
        context.setStrokeColor(color.cgColor)
        context.setLineWidth(1)
        context.addPath(path)
        context.strokePath()
    }

    /// The dots before a more row: one per source (three at most), overlapping.
    static func stack(_ colors: [RGBColor], diameter: CGFloat, overlap: CGFloat, ring: RGBColor?, glows: Bool, at origin: CGPoint,
                      in context: CGContext) -> CGFloat {
        let dots = colors.isEmpty ? [] : (0..<3).map { colors[min($0, colors.count - 1)] }
        var x = origin.x
        for color in dots {
            let rect = CGRect(x: x, y: origin.y, width: diameter, height: diameter)
            if let ring { fillCircle(rect.insetBy(dx: -1.5, dy: -1.5), color: ring, in: context) }
            glowing(glows && ring == nil ? color : nil, blur: 4, in: context) {
                fillCircle(rect, color: color.opacity(ring == nil ? 1 : 0.85), in: $0)
            }
            x += diameter - overlap
        }
        return dots.isEmpty ? 0 : x - origin.x + overlap
    }
}

/// One part of a board's footer: a label around a stronger value (`boards.today`, the cost, `boards.peak`).
struct BoardFooterPart {
    var prefix: String
    var value: String
    var suffix: String
    /// The value is a word in the label's ink (today's cost as `cost.subscription.tag`).
    var quiet = false

    /// Splits the localized template of `key` around its `%@`.
    @MainActor
    init(_ key: String, value: String, l10n: Localizer, uppercase: Bool = false) {
        let template = l10n(key)
        let parts = template.components(separatedBy: "%@")
        let locale = l10n.locale
        func cased(_ text: String) -> String { uppercase ? text.uppercased(with: locale) : text }
        prefix = cased(parts.first ?? "")
        self.value = value
        suffix = cased(parts.count > 1 ? parts[1] : "")
    }

    init(value: String, quiet: Bool = false) {
        prefix = ""
        self.value = value
        suffix = ""
        self.quiet = quiet
    }

    /// Draws `parts` spread across `width` (CSS `justify-content: space-between`) on one baseline.
    @MainActor
    static func draw(_ parts: [BoardFooterPart], label: BoardFont, strong: BoardFont, labelColor: RGBColor, strongColor: RGBColor,
                     width: CGFloat, midY: CGFloat, in context: CGContext) {
        let lines = parts.map { part in
            [BoardLabel(part.prefix, label, color: labelColor), BoardLabel(part.value, strong, color: part.quiet ? labelColor : strongColor),
             BoardLabel(part.suffix, label, color: labelColor)]
        }
        let widths = lines.map { $0.reduce(0) { $0 + $1.width } }
        let free = max(0, width - widths.reduce(0, +))
        let gap = lines.count > 1 ? free / CGFloat(lines.count - 1) : 0
        var x: CGFloat = 0
        for (index, line) in lines.enumerated() {
            let baseline = line[1].baseline(centeredOn: midY)
            var cursor = x
            for label in line {
                label.draw(in: context, x: cursor, baseline: baseline)
                cursor += label.width
            }
            x += widths[index] + gap
        }
    }
}
