import AgentSpeedCore
import Foundation

enum AppearanceMode: String, CaseIterable, Identifiable, Sendable {
    case auto, light, dark

    var id: String { rawValue }
    var titleKey: String { "appearance.\(rawValue)" }
}

enum LanguagePreference: String, CaseIterable, Identifiable, Sendable {
    case system
    case zhHant = "zh-Hant"
    case zhHans = "zh-Hans"
    case en
    case ja

    var id: String { rawValue }

    var resolved: AppLanguage {
        switch self {
        case .system: AppLanguage.system
        case .zhHant: .zhHant
        case .zhHans: .zhHans
        case .en: .en
        case .ja: .ja
        }
    }

    var titleKey: String {
        switch self {
        case .system: "settings.language.system"
        case .zhHant: "language.zhHant"
        case .zhHans: "language.zhHans"
        case .en: "language.en"
        case .ja: "language.ja"
        }
    }
}

enum BarDisplayMode: String, CaseIterable, Identifiable, Sendable {
    case glyphValue, value, glyph

    var id: String { rawValue }
    var titleKey: String { "settings.bar.mode.\(rawValue)" }
    var showsGlyph: Bool { self != .value }
    var showsValue: Bool { self != .glyph }
}

enum EffectsLevel: String, CaseIterable, Identifiable, Comparable, Sendable {
    case off, soft, standard, max

    var id: String { rawValue }
    var titleKey: String { "effects.\(rawValue)" }

    private var rank: Int {
        switch self {
        case .off: 0
        case .soft: 1
        case .standard: 2
        case .max: 3
        }
    }

    static func < (lhs: EffectsLevel, rhs: EffectsLevel) -> Bool { lhs.rank < rhs.rank }
}

enum OrbSizePreset: CaseIterable, Identifiable, Sendable {
    case small, medium, large

    var id: Self { self }

    var diameter: Double {
        switch self {
        case .small: 112
        case .medium: 148
        case .large: 196
        }
    }

    var titleKey: String {
        switch self {
        case .small: "menu.size.small"
        case .medium: "menu.size.medium"
        case .large: "menu.size.large"
        }
    }
}

extension SpeedMetric: Identifiable {
    public var id: String { rawValue }

    var titleKey: String { self == .output ? "metric.output" : "metric.total" }
}

extension SourceKind {
    var titleKey: String { "source.\(rawValue)" }
}

enum SettingsLimits {
    /// Output-speed redline range; kept for callers that predate per-metric redlines.
    static let redline: ClosedRange<Double> = 50...2000
    static let redlineStep: Double = 50
    static let redlineTotal: ClosedRange<Double> = 20_000...2_000_000

    static func redlineRange(for metric: SpeedMetric) -> ClosedRange<Double> {
        metric == .output ? redline : redlineTotal
    }

    static func defaultRedline(for metric: SpeedMetric) -> Double {
        metric == .output ? 400 : 200_000
    }

    /// Slider increment at a value: 50 tok/s for output; two significant digits for total throughput
    /// (1k steps below 100k, 10k below 1M, 100k above), so the slider feels logarithmic.
    static func redlineStep(at value: Double, metric: SpeedMetric) -> Double {
        guard metric == .total else { return redlineStep }
        let magnitude = floor(log10(max(value, 1)))
        return pow(10, max(3, magnitude - 1))
    }

    static func snapRedline(_ value: Double, metric: SpeedMetric) -> Double {
        let range = redlineRange(for: metric)
        let value = value.isFinite ? value.clamped(to: range) : defaultRedline(for: metric)
        let step = redlineStep(at: value, metric: metric)
        return ((value / step).rounded() * step).clamped(to: range)
    }

    /// Maps a slider position (0...1) to a redline: linear for output, logarithmic for total throughput.
    static func redline(atPosition position: Double, metric: SpeedMetric) -> Double {
        let range = redlineRange(for: metric)
        let p = position.clamped(to: 0...1)
        let raw = metric == .output
            ? range.lowerBound + (range.upperBound - range.lowerBound) * p
            : range.lowerBound * pow(range.upperBound / range.lowerBound, p)
        return snapRedline(raw, metric: metric)
    }

    static func position(ofRedline value: Double, metric: SpeedMetric) -> Double {
        let range = redlineRange(for: metric)
        let value = value.clamped(to: range)
        if metric == .output {
            return (value - range.lowerBound) / (range.upperBound - range.lowerBound)
        }
        return log(value / range.lowerBound) / log(range.upperBound / range.lowerBound)
    }
    static let orbSize: ClosedRange<Double> = 96...240
    static let orbOpacity: ClosedRange<Double> = 0.4...1
}
