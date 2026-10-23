import AgentSpeedCore
import AppKit

/// The menu bar's second value (DESIGN 3): the live money rate after a tok/s value, as an 11 pt tabular number and a
/// 9 pt unit. The number sits right-aligned in a cell the renderer's width governor
/// sizes (grows at once, shrinks after a delay), so the item never jitters while the numbers change.
struct StatusItemSecondary: Equatable {
    static let size: CGFloat = 11
    static let unitSize: CGFloat = 9
    static let gap: CGFloat = 8
    static let unitGap: CGFloat = 2

    var number: String
    var unit: String
    /// Idle, nothing priced or zero: the number takes the secondary color.
    var dim: Bool
    /// Room for the number; at least its drawn width.
    var cell: CGFloat = 0

    static var font: NSFont { .monospacedDigitSystemFont(ofSize: size, weight: .regular) }
    static var unitFont: NSFont { .systemFont(ofSize: unitSize, weight: .medium) }

    @MainActor
    init?(model: AppModel) {
        let settings = model.settings
        guard settings.barSecondary, settings.liveUnit == .tokens, settings.barMode.showsValue else { return nil }
        let live = StatusItemValue.liveMoney(model)
        number = live.unknown ? "–" : (live.atLeast ? "≥" : "") + live.number
        unit = live.unit
        dim = live.unknown || live.value == 0
        cell = numberWidth
    }

    private static func width(_ text: String, font: NSFont) -> CGFloat {
        ceil((text as NSString).size(withAttributes: [.font: font]).width)
    }

    /// Drawn width of the number. Digits are tabular, so it only changes with the digit count or format.
    var numberWidth: CGFloat { Self.width(number, font: Self.font) }

    /// The block's width, gap included.
    var width: CGFloat {
        Self.gap + cell + Self.unitGap + Self.width(unit, font: Self.unitFont)
    }

    /// Text spoken after the main value.
    var spoken: String { number + " " + unit }

    func draw(at x: CGFloat, baseline: CGFloat, palette: StatusItemPalette) {
        let font = Self.font
        let numberWidth = (number as NSString).size(withAttributes: [.font: font]).width
        let numberRight = x + Self.gap + cell
        NSAttributedString(string: number, attributes: [
            .font: font,
            .foregroundColor: (dim ? palette.secondary : palette.primary.opacity(0.85)).nsColor,
        ]).draw(at: CGPoint(x: numberRight - numberWidth, y: baseline - font.ascender))
        let unitFont = Self.unitFont
        NSAttributedString(string: unit, attributes: [
            .font: unitFont,
            .foregroundColor: palette.secondary.nsColor,
        ]).draw(at: CGPoint(x: numberRight + Self.unitGap, y: baseline - unitFont.ascender))
    }
}
