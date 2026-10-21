import AppKit

/// Where the disc sits on one display, independent of that display's resolution: each axis is either docked to an
/// edge at a fixed distance or a fraction of the usable area.
struct OrbPositionRecord: Equatable, Sendable {
    enum Axis: Equatable, Sendable {
        /// Distance from the disc edge to the low (left/bottom) usable edge.
        case low(CGFloat)
        /// Distance from the disc edge to the high (right/top) usable edge.
        case high(CGFloat)
        /// Disc center as a fraction of the usable span.
        case fraction(CGFloat)
    }

    var x: Axis
    var y: Axis

    init(x: Axis, y: Axis) {
        self.x = x
        self.y = y
    }

    init(center: CGPoint, radius: CGFloat, visible: CGRect) {
        x = Self.axis(center.x, radius: radius, low: visible.minX, high: visible.maxX)
        y = Self.axis(center.y, radius: radius, low: visible.minY, high: visible.maxY)
    }

    func center(radius: CGFloat, visible: CGRect) -> CGPoint {
        CGPoint(
            x: Self.resolve(x, radius: radius, low: visible.minX, high: visible.maxX),
            y: Self.resolve(y, radius: radius, low: visible.minY, high: visible.maxY)
        )
    }

    private static func axis(_ value: CGFloat, radius: CGFloat, low: CGFloat, high: CGFloat) -> Axis {
        let toLow = value - radius - low
        let toHigh = high - (value + radius)
        if toLow <= OrbSnap.threshold, toLow <= toHigh { return .low(max(0, toLow)) }
        if toHigh <= OrbSnap.threshold { return .high(max(0, toHigh)) }
        let span = high - low
        return .fraction(span > 0 ? ((value - low) / span).clamped(to: 0...1) : 0.5)
    }

    private static func resolve(_ axis: Axis, radius: CGFloat, low: CGFloat, high: CGFloat) -> CGFloat {
        let value: CGFloat = switch axis {
        case let .low(distance): low + distance + radius
        case let .high(distance): high - distance - radius
        case let .fraction(fraction): low + fraction * (high - low)
        }
        return OrbSnap.clampAxis(value, radius: radius, low: low, high: high)
    }
}

/// Persists the orb position per display (keyed by display UUID) in UserDefaults.
@MainActor
final class OrbPositionStore {
    private let defaults: UserDefaults

    init(defaults: UserDefaults = .standard) {
        self.defaults = defaults
    }

    /// The display the user last placed the orb on.
    var lastDisplay: String? {
        defaults.string(forKey: Key.lastDisplay)
    }

    func record(for display: String) -> OrbPositionRecord? {
        guard let entry = positions[display] as? [String: Any],
              let x = Self.decode(entry, prefix: "x"),
              let y = Self.decode(entry, prefix: "y")
        else { return nil }
        return OrbPositionRecord(x: x, y: y)
    }

    func save(_ record: OrbPositionRecord, for display: String) {
        var all = positions
        var entry: [String: Any] = [:]
        Self.encode(record.x, prefix: "x", into: &entry)
        Self.encode(record.y, prefix: "y", into: &entry)
        all[display] = entry
        defaults.set(all, forKey: Key.positions)
        defaults.set(display, forKey: Key.lastDisplay)
    }

    func clear() {
        defaults.removeObject(forKey: Key.positions)
        defaults.removeObject(forKey: Key.lastDisplay)
    }

    private var positions: [String: Any] {
        defaults.dictionary(forKey: Key.positions) ?? [:]
    }

    private static func encode(_ axis: OrbPositionRecord.Axis, prefix: String, into entry: inout [String: Any]) {
        let (anchor, value): (String, CGFloat) = switch axis {
        case let .low(distance): ("low", distance)
        case let .high(distance): ("high", distance)
        case let .fraction(fraction): ("fraction", fraction)
        }
        entry[prefix + "Anchor"] = anchor
        entry[prefix] = Double(value)
    }

    private static func decode(_ entry: [String: Any], prefix: String) -> OrbPositionRecord.Axis? {
        guard let anchor = entry[prefix + "Anchor"] as? String,
              let raw = (entry[prefix] as? NSNumber)?.doubleValue, raw.isFinite
        else { return nil }
        let value = CGFloat(raw)
        switch anchor {
        case "low": return .low(value.clamped(to: 0...OrbSnap.threshold))
        case "high": return .high(value.clamped(to: 0...OrbSnap.threshold))
        case "fraction": return .fraction(value.clamped(to: 0...1))
        default: return nil
        }
    }

    private enum Key {
        static let positions = "orb.positions"
        static let lastDisplay = "orb.lastDisplay"
    }
}

extension NSScreen {
    /// Stable identifier of the physical display, surviving reconnects and resolution changes.
    var orbDisplayUUID: String? {
        guard let number = deviceDescription[NSDeviceDescriptionKey("NSScreenNumber")] as? NSNumber else { return nil }
        let displayID = number.uint32Value
        guard let uuid = CGDisplayCreateUUIDFromDisplayID(displayID)?.takeRetainedValue(),
              let string = CFUUIDCreateString(nil, uuid)
        else { return "display-\(displayID)" }
        return string as String
    }

    var orbSnapScreen: OrbSnapScreen {
        OrbSnapScreen(frame: frame, visibleFrame: visibleFrame)
    }
}
