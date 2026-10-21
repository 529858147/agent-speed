import AgentSpeedCore
import SwiftUI

/// Redline of the selected metric: title and live value, a full-width slider, and the gauge scale it calibrates.
/// The slider moves along a 0…1 position: linear for output speed, logarithmic for total throughput.
struct RedlineControl: View {
    let settings: AppSettings
    let l10n: Localizer
    var reduceMotion = false

    var body: some View {
        let title = l10n("settings.redline")
        let valueText = l10n("mac.settings.speedValue", SpeedFormat.speed(settings.redline))
        VStack(alignment: .leading, spacing: Spacing.xs) {
            HStack(alignment: .firstTextBaseline) {
                RowTitle(title)
                Spacer(minLength: Spacing.s)
                ValueText(valueText)
                    .contentTransition(.numericText())
                    .accessibilityHidden(true)
            }
            Slider(value: position, in: 0...1) {
                Text(title)
            }
            .labelsHidden()
            .accessibilityValue(Text(valueText))
            .accessibilityAdjustableAction { direction in
                adjust(direction)
            }
            RedlineScale()
                .padding(.top, Spacing.xxs)
        }
        .padding(.vertical, Spacing.xxs)
        .animation(reduceMotion ? nil : Motion.layout, value: settings.metric)
    }

    private var position: Binding<Double> {
        let settings = settings
        return Binding(
            get: { SettingsLimits.position(ofRedline: settings.redline, metric: settings.metric) },
            set: { settings.redline = SettingsLimits.redline(atPosition: $0, metric: settings.metric) }
        )
    }

    /// One VoiceOver adjustment moves 1/40 of the slider (about 50 tok/s for output, about 12 % for total).
    private func adjust(_ direction: AccessibilityAdjustmentDirection) {
        let sign: Double
        switch direction {
        case .increment: sign = 1
        case .decrement: sign = -1
        @unknown default: return
        }
        let metric = settings.metric
        let current = settings.redline
        let position = SettingsLimits.position(ofRedline: current, metric: metric)
        var next = SettingsLimits.redline(atPosition: position + sign / 40, metric: metric)
        if next == current {
            let step = SettingsLimits.redlineStep(at: current, metric: metric)
            next = SettingsLimits.snapRedline(current + sign * step, metric: metric)
        }
        settings.redline = next
    }
}

/// The orb's 36-segment gauge unrolled: the heat spectrum from idle to redline, with dots where boost,
/// overdrive and redline begin (the same positions as the orb's tick dots).
struct RedlineScale: View {
    @Environment(\.colorScheme) private var colorScheme

    static let tiers: [SpeedTier] = [.boost, .overdrive, .redline]
    private static let segments = 36
    private static let gap: CGFloat = 2
    private static let barHeight: CGFloat = 5
    private static let dotSize: CGFloat = 4
    private static let dotSpacing: CGFloat = 3

    var body: some View {
        let dark = colorScheme == .dark
        Canvas { context, size in
            let count = Self.segments
            let width = (size.width - Self.gap * CGFloat(count - 1)) / CGFloat(count)
            let top = size.height - Self.barHeight
            for index in 0..<count {
                let heat = Double(index) / Double(count - 1)
                let rect = CGRect(x: CGFloat(index) * (width + Self.gap), y: top, width: width, height: Self.barHeight)
                context.fill(
                    Path(roundedRect: rect, cornerRadius: 1.5, style: .continuous),
                    with: .color(HeatPalette.color(heat, dark: dark).color)
                )
            }
            for tier in Self.tiers {
                let heat = HeatPalette.heat(forFraction: tier.threshold)
                let x = (heat * size.width).clamped(to: Self.dotSize...(size.width - Self.dotSize))
                let dot = CGRect(x: x - Self.dotSize / 2, y: 0, width: Self.dotSize, height: Self.dotSize)
                context.fill(Path(ellipseIn: dot), with: .color(HeatPalette.color(heat, dark: dark).color))
            }
        }
        .frame(height: Self.dotSize + Self.dotSpacing + Self.barHeight)
        .accessibilityHidden(true)
    }
}

/// The redline hint with each threshold speed set in the heat color of its tick.
struct RedlineHint: View {
    let settings: AppSettings
    let l10n: Localizer
    @Environment(\.colorScheme) private var colorScheme
    @Environment(\.colorSchemeContrast) private var contrast

    var body: some View {
        Text(attributed)
            .font(.subheadline)
            .foregroundStyle(.secondary)
            .fixedSize(horizontal: false, vertical: true)
            .frame(maxWidth: .infinity, alignment: .leading)
    }

    private var attributed: AttributedString {
        let thresholds = settings.tierThresholds
        let values = [thresholds.boost, thresholds.overdrive, thresholds.redline]
        let markers = ["\u{E000}", "\u{E001}", "\u{E002}"]
        var text = AttributedString(l10n("settings.redline.hint", markers[0], markers[1], markers[2]))
        for (index, marker) in markers.enumerated() {
            guard let range = text.range(of: marker) else { continue }
            let heat = HeatPalette.heat(forFraction: RedlineScale.tiers[index].threshold)
            var run = AttributedString(SpeedFormat.speed(values[index]))
            run.foregroundColor = HeatText.color(heat, dark: colorScheme == .dark, increasedContrast: contrast == .increased)
            run.font = .subheadline.monospacedDigit().weight(.semibold)
            text.replaceSubrange(range, with: run)
        }
        return text
    }
}
