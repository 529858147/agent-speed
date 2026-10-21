import AgentSpeedCore
import SwiftUI

/// The embedded orb with the big live readout, speed title and agent context (DESIGN section 4.1).
struct HeroSection: View {
    let model: AppModel

    /// The orb's aura reaches 0.25 × diameter past its frame; this keeps it inside the popover.
    private static let auraRoom: CGFloat = 8

    var body: some View {
        let l10n = model.l10n
        let gauge = model.gauge
        let value = gauge.readoutValue
        let tier = gauge.tier
        let title = gauge.title
        let snapshot = model.snapshot
        let speed = SpeedFormat.speed(value)
        let titleName = l10n(title.titleKey)
        let context = l10n("agents.context", snapshot.generatingCount, snapshot.toolsCount)

        HStack(spacing: Spacing.l) {
            OrbView(model: model, diameter: DashboardLayout.orbDiameter, style: .embedded)
                .accessibilityHidden(true)
            VStack(alignment: .leading, spacing: Spacing.xxs) {
                HeroReadout(
                    value: value,
                    text: speed,
                    unit: l10n("unit.tokS"),
                    isIdle: tier == .idle,
                    reduceMotion: model.accessibility.reduceMotion
                )
                SpeedTitleLabel(
                    title: title,
                    text: titleName,
                    heat: HeatPalette.heat(value: value, redline: model.settings.redline),
                    latin: model.dashboardLatinCaps,
                    reduceMotion: model.accessibility.reduceMotion
                )
                Text(context)
                    .font(DashboardFont.context)
                    .foregroundStyle(.dashboardSecondary)
                    .lineLimit(1)
                    .minimumScaleFactor(0.85)
                    .padding(.top, 2)
            }
            Spacer(minLength: 0)
        }
        .padding(.leading, Self.auraRoom)
        .padding(.top, Self.auraRoom)
        .accessibilityElement(children: .ignore)
        .accessibilityLabel(l10n("a11y.orb", speed, titleName))
        .accessibilityValue(context)
    }
}

private struct HeroReadout: View {
    let value: Double
    let text: String
    let unit: String
    let isIdle: Bool
    let reduceMotion: Bool

    var body: some View {
        HStack(alignment: .firstTextBaseline, spacing: 5) {
            Text(text)
                .font(DashboardFont.heroValue)
                .foregroundStyle(isIdle ? AnyShapeStyle(.dashboardSecondary) : AnyShapeStyle(.primary))
                .contentTransition(reduceMotion ? .opacity : .numericText(value: value))
                .animation(reduceMotion ? Motion.fade : Motion.digits, value: value)
                .lineLimit(1)
                .minimumScaleFactor(0.6)
            Text(unit)
                .font(DashboardFont.heroUnit)
                .foregroundStyle(.dashboardSecondary)
                .fixedSize()
        }
        .animation(Motion.fade, value: isIdle)
    }
}

/// The speed title in the heat color; a new title slides up into place (0.35 s). It shrinks to fit, never truncating.
private struct SpeedTitleLabel: View {
    let title: SpeedTitle
    let text: String
    let heat: Double
    let latin: Bool
    let reduceMotion: Bool

    @Environment(\.colorScheme) private var colorScheme
    @Environment(\.colorSchemeContrast) private var contrast

    var body: some View {
        let color = DashboardPalette.heatText(heat, dark: colorScheme == .dark, increasedContrast: contrast == .increased)
        ZStack(alignment: .leading) {
            Text(text)
                .font(DashboardFont.tier)
                .textCase(latin ? .uppercase : nil)
                .tracking(latin ? 1.4 : 0.8)
                .foregroundStyle(color)
                .lineLimit(1)
                .minimumScaleFactor(TitleFit.minimumScale)
                .allowsTightening(true)
                .id(title)
                .transition(transition)
        }
        .clipped()
        .animation(reduceMotion ? Motion.fade : .spring(response: 0.35, dampingFraction: 0.86), value: title)
    }

    private var transition: AnyTransition {
        guard !reduceMotion else { return .opacity }
        return .asymmetric(
            insertion: .move(edge: .bottom).combined(with: .opacity),
            removal: .move(edge: .top).combined(with: .opacity)
        )
    }
}
