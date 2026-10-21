import AgentSpeedCore
import Foundation
import Observation

/// Every user setting (DESIGN section 5). Persisted to UserDefaults on change and observable for live updates.
@MainActor
@Observable
final class AppSettings {
    var appearance: AppearanceMode { didSet { store(appearance.rawValue, .appearance) } }
    var language: LanguagePreference { didSet { store(language.rawValue, .language) } }
    var metric: SpeedMetric { didSet { store(metric.rawValue, .metric) } }
    /// Redline of the output-speed metric.
    var redlineOutput: Double {
        didSet {
            let snapped = SettingsLimits.snapRedline(redlineOutput, metric: .output)
            if snapped != redlineOutput { redlineOutput = snapped; return }
            store(redlineOutput, .redline)
        }
    }
    /// Redline of the total-throughput metric (includes cached input, typically ~1000× larger).
    var redlineTotal: Double {
        didSet {
            let snapped = SettingsLimits.snapRedline(redlineTotal, metric: .total)
            if snapped != redlineTotal { redlineTotal = snapped; return }
            store(redlineTotal, .redlineTotal)
        }
    }

    var barMode: BarDisplayMode { didSet { store(barMode.rawValue, .barMode) } }
    var barColoredGlyph: Bool { didSet { store(barColoredGlyph, .barColoredGlyph) } }
    var barShowsUnit: Bool { didSet { store(barShowsUnit, .barShowsUnit) } }

    var orbVisible: Bool { didSet { store(orbVisible, .orbVisible) } }
    var orbSize: Double {
        didSet {
            let clamped = orbSize.clamped(to: SettingsLimits.orbSize).rounded()
            if clamped != orbSize { orbSize = clamped; return }
            store(orbSize, .orbSize)
        }
    }
    var orbOpacity: Double {
        didSet {
            let clamped = orbOpacity.clamped(to: SettingsLimits.orbOpacity)
            if clamped != orbOpacity { orbOpacity = clamped; return }
            store(orbOpacity, .orbOpacity)
        }
    }
    var orbAlwaysOnTop: Bool { didSet { store(orbAlwaysOnTop, .orbAlwaysOnTop) } }
    var orbClickThrough: Bool { didSet { store(orbClickThrough, .orbClickThrough) } }
    var orbFadeWhenIdle: Bool { didSet { store(orbFadeWhenIdle, .orbFadeWhenIdle) } }
    var orbAllSpaces: Bool { didSet { store(orbAllSpaces, .orbAllSpaces) } }

    /// Dashboard agents list: every agent instead of the first rows (DESIGN section 4.4).
    var agentsExpanded: Bool { didSet { store(agentsExpanded, .agentsExpanded) } }

    var effects: EffectsLevel { didSet { store(effects.rawValue, .effects) } }
    var celebratePeaks: Bool { didSet { store(celebratePeaks, .celebratePeaks) } }
    var responsePulses: Bool { didSet { store(responsePulses, .responsePulses) } }

    var enabledSources: Set<SourceKind> {
        didSet {
            store(enabledSources.map(\.rawValue).sorted(), .enabledSources)
            store(SourceKind.allCases.map(\.rawValue), .knownSources)
        }
    }
    /// Nil means the default location from `LiveMonitor`.
    var codexRoot: URL? { didSet { store(codexRoot?.path, .codexRoot) } }
    var claudeRoot: URL? { didSet { store(claudeRoot?.path, .claudeRoot) } }
    var dshRoot: URL? { didSet { store(dshRoot?.path, .dshRoot) } }

    /// UPDATES section 3: check GitHub at launch and every 6 h.
    var autoCheckUpdates: Bool { didSet { store(autoCheckUpdates, .autoCheckUpdates) } }

    @ObservationIgnored private let defaults: UserDefaults

    init(defaults: UserDefaults = .standard) {
        self.defaults = defaults
        appearance = AppearanceMode(rawValue: defaults.string(forKey: Key.appearance.rawValue) ?? "") ?? .auto
        language = LanguagePreference(rawValue: defaults.string(forKey: Key.language.rawValue) ?? "") ?? .system
        metric = SpeedMetric(rawValue: defaults.string(forKey: Key.metric.rawValue) ?? "") ?? .output
        redlineOutput = SettingsLimits.snapRedline(
            defaults.object(forKey: Key.redline.rawValue) as? Double ?? SettingsLimits.defaultRedline(for: .output),
            metric: .output
        )
        redlineTotal = SettingsLimits.snapRedline(
            defaults.object(forKey: Key.redlineTotal.rawValue) as? Double ?? SettingsLimits.defaultRedline(for: .total),
            metric: .total
        )
        barMode = BarDisplayMode(rawValue: defaults.string(forKey: Key.barMode.rawValue) ?? "") ?? .glyphValue
        barColoredGlyph = defaults.object(forKey: Key.barColoredGlyph.rawValue) as? Bool ?? true
        barShowsUnit = defaults.object(forKey: Key.barShowsUnit.rawValue) as? Bool ?? true
        orbVisible = defaults.object(forKey: Key.orbVisible.rawValue) as? Bool ?? true
        orbSize = (defaults.object(forKey: Key.orbSize.rawValue) as? Double ?? 148)
            .clamped(to: SettingsLimits.orbSize)
        orbOpacity = (defaults.object(forKey: Key.orbOpacity.rawValue) as? Double ?? 1)
            .clamped(to: SettingsLimits.orbOpacity)
        orbAlwaysOnTop = defaults.object(forKey: Key.orbAlwaysOnTop.rawValue) as? Bool ?? true
        orbClickThrough = defaults.object(forKey: Key.orbClickThrough.rawValue) as? Bool ?? false
        orbFadeWhenIdle = defaults.object(forKey: Key.orbFadeWhenIdle.rawValue) as? Bool ?? false
        orbAllSpaces = defaults.object(forKey: Key.orbAllSpaces.rawValue) as? Bool ?? true
        agentsExpanded = defaults.object(forKey: Key.agentsExpanded.rawValue) as? Bool ?? false
        effects = EffectsLevel(rawValue: defaults.string(forKey: Key.effects.rawValue) ?? "") ?? .standard
        celebratePeaks = defaults.object(forKey: Key.celebratePeaks.rawValue) as? Bool ?? true
        responsePulses = defaults.object(forKey: Key.responsePulses.rawValue) as? Bool ?? true
        if let raw = defaults.array(forKey: Key.enabledSources.rawValue) as? [String] {
            // A source added after the choice was saved starts enabled, like it would for a new user.
            let known = (defaults.array(forKey: Key.knownSources.rawValue) as? [String]).map(Set.init) ?? Self.legacySources
            enabledSources = Set(raw.compactMap(SourceKind.init(rawValue:)))
                .union(SourceKind.allCases.filter { !known.contains($0.rawValue) })
        } else {
            enabledSources = Set(SourceKind.allCases)
        }
        codexRoot = Self.url(defaults.string(forKey: Key.codexRoot.rawValue))
        claudeRoot = Self.url(defaults.string(forKey: Key.claudeRoot.rawValue))
        dshRoot = Self.url(defaults.string(forKey: Key.dshRoot.rawValue))
        autoCheckUpdates = defaults.object(forKey: Key.autoCheckUpdates.rawValue) as? Bool ?? true
    }

    var monitorConfiguration: MonitorConfiguration {
        MonitorConfiguration(enabledSources: enabledSources, codexRoot: codexRoot, claudeRoot: claudeRoot, dshRoot: dshRoot)
    }

    func isEnabled(_ source: SourceKind) -> Bool { enabledSources.contains(source) }

    func setEnabled(_ source: SourceKind, _ enabled: Bool) {
        if enabled { enabledSources.insert(source) } else { enabledSources.remove(source) }
    }

    /// Redline of the selected metric; setting it edits that metric's value (DESIGN section 1).
    var redline: Double {
        get { redline(for: metric) }
        set {
            switch metric {
            case .output: redlineOutput = newValue
            case .total: redlineTotal = newValue
            }
        }
    }

    func redline(for metric: SpeedMetric) -> Double {
        metric == .output ? redlineOutput : redlineTotal
    }

    /// Slider range of the selected metric's redline.
    var redlineRange: ClosedRange<Double> { SettingsLimits.redlineRange(for: metric) }

    /// Thresholds (tok/s) where boost, overdrive and redline begin for the current redline.
    var tierThresholds: (boost: Double, overdrive: Double, redline: Double) {
        (redline * SpeedTier.boost.threshold, redline * SpeedTier.overdrive.threshold, redline * SpeedTier.redline.threshold)
    }

    /// The sources that existed before `knownSources` was saved alongside `enabledSources`.
    private static let legacySources: Set<String> = ["codex", "claudeCode", "claudeDesktop"]

    private static func url(_ path: String?) -> URL? {
        guard let path, !path.isEmpty else { return nil }
        return URL(fileURLWithPath: path, isDirectory: true)
    }

    private func store(_ value: Any?, _ key: Key) {
        if let value { defaults.set(value, forKey: key.rawValue) } else { defaults.removeObject(forKey: key.rawValue) }
    }

    private enum Key: String {
        case appearance, language, metric, redline, redlineTotal
        case barMode, barColoredGlyph, barShowsUnit
        case orbVisible, orbSize, orbOpacity, orbAlwaysOnTop, orbClickThrough, orbFadeWhenIdle, orbAllSpaces
        case agentsExpanded
        case effects, celebratePeaks, responsePulses
        case enabledSources, knownSources, codexRoot, claudeRoot, dshRoot
        case autoCheckUpdates
    }
}

extension Comparable {
    func clamped(to range: ClosedRange<Self>) -> Self {
        min(max(self, range.lowerBound), range.upperBound)
    }
}
