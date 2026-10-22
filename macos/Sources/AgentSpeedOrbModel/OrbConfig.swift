import Foundation

/// One floating orb (DESIGN 2b, Data model). Decoding is tolerant: missing fields take their defaults and
/// out-of-range values are clamped, so a hand-edited or newer document never fails to load.
public struct OrbConfig: Equatable, Hashable, Identifiable, Sendable {
    public static let nameLimit = 32

    /// UUID string, never reused.
    public var id: String
    /// Custom name (1–32 characters, trimmed); nil = automatic.
    public var name: String?
    public var sources: OrbSourceSelection
    public var style: OrbStyleKind
    /// Diameter in points, within the style's range.
    public var size: Double
    public var opacity: Double
    public var alwaysOnTop: Bool
    public var clickThrough: Bool
    public var fadeWhenIdle: Bool
    /// macOS only.
    public var allSpaces: Bool
    public var visible: Bool
    /// nil = the General redline of that metric. Stored as given; the app snaps it to its slider steps.
    public var redlineOutput: Double?
    public var redlineTotal: Double?

    public init(
        id: String,
        name: String? = nil,
        sources: OrbSourceSelection = .all,
        style: OrbStyleKind = .led,
        size: Double = OrbSizes.default,
        opacity: Double = 1,
        alwaysOnTop: Bool = true,
        clickThrough: Bool = false,
        fadeWhenIdle: Bool = false,
        allSpaces: Bool = true,
        visible: Bool = true,
        redlineOutput: Double? = nil,
        redlineTotal: Double? = nil
    ) {
        self.id = id
        self.name = Self.cleanName(name)
        self.sources = sources
        self.style = style
        self.size = style.clampSize(size)
        self.opacity = Self.cleanOpacity(opacity)
        self.alwaysOnTop = alwaysOnTop
        self.clickThrough = clickThrough
        self.fadeWhenIdle = fadeWhenIdle
        self.allSpaces = allSpaces
        self.visible = visible
        self.redlineOutput = Self.cleanRedline(redlineOutput)
        self.redlineTotal = Self.cleanRedline(redlineTotal)
    }

    /// The orb of a fresh install: all sources, `led`, 148 pt, at the default position.
    public static func makeDefault(id: String) -> OrbConfig {
        OrbConfig(id: id)
    }

    /// The same orb in another style; the size is clamped to the new style's range.
    public func withStyle(_ style: OrbStyleKind) -> OrbConfig {
        var copy = self
        copy.style = style
        copy.size = style.clampSize(size)
        return copy
    }

    /// Trimmed and limited to 32 characters; empty means automatic (nil).
    public static func cleanName(_ name: String?) -> String? {
        guard let trimmed = name?.trimmingCharacters(in: .whitespacesAndNewlines), !trimmed.isEmpty else { return nil }
        guard trimmed.count > nameLimit else { return trimmed }
        return String(trimmed.prefix(nameLimit)).trimmingCharacters(in: .whitespacesAndNewlines)
    }

    public static func cleanOpacity(_ opacity: Double) -> Double {
        opacity.isFinite ? opacity.clampedToRange(OrbSizes.opacityRange) : 1
    }

    static func cleanRedline(_ value: Double?) -> Double? {
        guard let value, value.isFinite, value > 0 else { return nil }
        return value
    }
}

extension OrbConfig: Codable {
    enum CodingKeys: String, CodingKey {
        case id, name, sources, style, size, opacity, alwaysOnTop, clickThrough, fadeWhenIdle, allSpaces, visible
        case redlineOutput, redlineTotal
    }

    public init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        let id = try container.decode(String.self, forKey: .id).trimmingCharacters(in: .whitespacesAndNewlines)
        guard !id.isEmpty else {
            throw DecodingError.dataCorruptedError(forKey: .id, in: container, debugDescription: "empty id")
        }
        func value<T: Decodable>(_ type: T.Type, _ key: CodingKeys) -> T? {
            (try? container.decodeIfPresent(type, forKey: key)) ?? nil
        }
        self.init(
            id: id,
            name: value(String.self, .name),
            sources: value(OrbSourceSelection.self, .sources) ?? .all,
            style: value(OrbStyleKind.self, .style) ?? .led,
            size: value(Double.self, .size) ?? OrbSizes.default,
            opacity: value(Double.self, .opacity) ?? 1,
            alwaysOnTop: value(Bool.self, .alwaysOnTop) ?? true,
            clickThrough: value(Bool.self, .clickThrough) ?? false,
            fadeWhenIdle: value(Bool.self, .fadeWhenIdle) ?? false,
            allSpaces: value(Bool.self, .allSpaces) ?? true,
            visible: value(Bool.self, .visible) ?? true,
            redlineOutput: value(Double.self, .redlineOutput),
            redlineTotal: value(Double.self, .redlineTotal)
        )
    }

    public func encode(to encoder: Encoder) throws {
        var container = encoder.container(keyedBy: CodingKeys.self)
        try container.encode(id, forKey: .id)
        try container.encode(name, forKey: .name)
        try container.encode(sources, forKey: .sources)
        try container.encode(style, forKey: .style)
        try container.encode(size, forKey: .size)
        try container.encode(opacity, forKey: .opacity)
        try container.encode(alwaysOnTop, forKey: .alwaysOnTop)
        try container.encode(clickThrough, forKey: .clickThrough)
        try container.encode(fadeWhenIdle, forKey: .fadeWhenIdle)
        try container.encode(allSpaces, forKey: .allSpaces)
        try container.encode(visible, forKey: .visible)
        try container.encode(redlineOutput, forKey: .redlineOutput)
        try container.encode(redlineTotal, forKey: .redlineTotal)
    }
}
