import AgentSpeedOrbModel
import AppKit

/// The keyboard focus ring of a board row (DESIGN 12 *Keyboard*): the system's focus color
/// (`keyboardFocusIndicatorColor`, the accent at half strength; full strength and a little wider with Increase
/// Contrast) as a ring hugging the outside of the row's rounded shape, as AppKit draws it around a control. It is a
/// cached piece of the row (it springs with it when the ranking moves) drawn over the row's other pieces; a focus move
/// replaces it at once, so it costs one small raster and no frames of its own.
@MainActor
enum BoardFocusRing {
    static let width: CGFloat = 3
    static let contrastWidth: CGFloat = 3.5

    /// The row's corner radius: the rows' plates (glass 10, neon 9), timing's square-cut data box 5.
    static func radius(_ style: BoardStyle) -> CGFloat {
        switch style {
        case .glass: 10
        case .neon: 9
        case .timing: 5
        }
    }

    /// The system focus color in the board's appearance.
    static func color(dark: Bool, highContrast: Bool) -> RGBColor {
        var color = RGBColor(red: 0, green: 0.4, blue: 0.96, alpha: 0.5)
        NSAppearance(named: dark ? .darkAqua : .aqua)?.performAsCurrentDrawingAppearance {
            if let system = NSColor.keyboardFocusIndicatorColor.usingColorSpace(.sRGB) {
                color = RGBColor(red: Double(system.redComponent), green: Double(system.greenComponent),
                                 blue: Double(system.blueComponent), alpha: Double(system.alphaComponent))
            }
        }
        if highContrast { color.alpha = 1 }
        return color
    }

    /// The ring of the row whose body is `row` (its key is the row's).
    static func item(around row: BoardContentItem, style: BoardStyle, dark: Bool, highContrast: Bool) -> BoardContentItem {
        let color = self.color(dark: dark, highContrast: highContrast)
        let width = highContrast ? contrastWidth : self.width
        let radius = self.radius(style)
        let reach = width + 1
        let frame = row.frame.insetBy(dx: -reach, dy: -reach)
        return BoardContentItem(key: row.key + "#focus", frame: frame, motion: .row,
                                revision: AnyHashable([AnyHashable([color.red, color.green, color.blue, color.alpha]),
                                                       AnyHashable(width), AnyHashable(radius), AnyHashable(frame.size.width),
                                                       AnyHashable(frame.size.height)])) { context, size in
            let shape = CGRect(origin: .zero, size: size).insetBy(dx: reach, dy: reach)
            // Centred half a width outside the row's outline: the ring's inner edge meets the row's edge.
            let ring = shape.insetBy(dx: -width / 2, dy: -width / 2)
            let corner = min(radius + width / 2, ring.height / 2)
            context.setStrokeColor(color.cgColor)
            context.setLineWidth(width)
            context.addPath(CGPath(roundedRect: ring, cornerWidth: corner, cornerHeight: corner, transform: nil))
            context.strokePath()
        }
    }
}
