import AgentSpeedOrbModel
import AppKit

/// Where a style puts the traffic readout (DESIGN 10, Floating orb readout), in diameters from the center.
struct OrbTrafficPlacement: Equatable {
    /// Baseline of the line nearest the value.
    var nearestBaseline: CGFloat
    /// The lines sit below the value (`needle`, ↓ line nearest); otherwise above it (↑ line nearest).
    var below: Bool
    /// Text size `t` = max(7 pt, size × S).
    var size: CGFloat
    /// A line must fit in this × S, else the readout is not drawn.
    var fit: CGFloat

    static let led = OrbTrafficPlacement(nearestBaseline: -0.190, below: false, size: 0.050, fit: 0.44)
    static let liquid = led
    static let reactor = OrbTrafficPlacement(nearestBaseline: -0.148, below: false, size: 0.046, fit: 0.32)
    static let needle = OrbTrafficPlacement(nearestBaseline: 0.108, below: true, size: 0.046, fit: 0.285)
    static let minimal = OrbTrafficPlacement(nearestBaseline: -0.185, below: false, size: 0.054, fit: 0.50)

    static func of(_ style: OrbStyleKind) -> OrbTrafficPlacement {
        switch style {
        case .led: .led
        case .needle: .needle
        case .liquid: .liquid
        case .reactor: .reactor
        case .minimal: .minimal
        }
    }

    /// Line pitch, × t.
    static let pitch: CGFloat = 1.32
    /// Gaps between arrow, number and unit, × t.
    static let gap: CGFloat = 0.16
    /// Arrow box, × t.
    static let arrowWidth: CGFloat = 0.50
    static let arrowHeight: CGFloat = 0.66
    /// Unit size, × t.
    static let unitScale: CGFloat = 0.82

    /// Never below the 7 pt floor of every orb label.
    func textSize(diameter s: CGFloat) -> CGFloat { max(7, size * s) }

    /// Baselines of the download (first) and upload lines.
    func baselines(center y: CGFloat, diameter s: CGFloat) -> (down: CGFloat, up: CGFloat) {
        let nearest = y + nearestBaseline * s
        let pitch = Self.pitch * textSize(diameter: s)
        return below ? (nearest, nearest + pitch) : (nearest - pitch, nearest)
    }
}

/// Widths of the readout's fixed cells, measured with the platform font once per size (DESIGN 10: the readout shows only
/// where a line fits, computed per size, never truncated or shrunk below 7 pt).
@MainActor
enum OrbTrafficMetrics {
    struct Cells: Equatable {
        var number: CGFloat
        var unit: CGFloat
    }

    private struct Key: Hashable {
        var size: CGFloat
        var bits: Bool
    }

    private static var cache: [Key: Cells] = [:]

    static func numberFont(_ t: CGFloat) -> NSFont { .monospacedDigitSystemFont(ofSize: t, weight: .semibold) }
    static func unitFont(_ t: CGFloat) -> NSFont { .systemFont(ofSize: OrbTrafficPlacement.unitScale * t, weight: .medium) }

    /// The `8.88` number cell and the `MB/s` (bits: `Mb/s`) unit cell at text size `t`.
    static func cells(size t: CGFloat, bits: Bool) -> Cells {
        let key = Key(size: t, bits: bits)
        if let cached = cache[key] { return cached }
        func width(_ text: String, _ font: NSFont) -> CGFloat {
            let line = CTLineCreateWithAttributedString(NSAttributedString(string: text, attributes: [.font: font]))
            return CGFloat(CTLineGetTypographicBounds(line, nil, nil, nil))
        }
        let cells = Cells(number: width("8.88", numberFont(t)), unit: width(bits ? "Mb/s" : "MB/s", unitFont(t)))
        if cache.count > 128 { cache.removeAll(keepingCapacity: true) }
        cache[key] = cells
        return cells
    }

    /// One line: arrow, gap, number cell, gap, unit cell (≈ 5.6 t).
    static func lineWidth(size t: CGFloat, bits: Bool) -> CGFloat {
        let cells = cells(size: t, bits: bits)
        return (OrbTrafficPlacement.arrowWidth + 2 * OrbTrafficPlacement.gap) * t + cells.number + cells.unit
    }

    static func fits(_ placement: OrbTrafficPlacement, diameter s: CGFloat, bits: Bool) -> Bool {
        lineWidth(size: placement.textSize(diameter: s), bits: bits) <= placement.fit * s
    }
}
