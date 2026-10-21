import AgentSpeedCore
import SwiftUI

/// One row per detected data source with its live speed and today's output (DESIGN section 4.3). Sources that are not
/// on this Mac are left to Settings → Data Sources.
struct SourcesSection: View {
    let model: AppModel

    var body: some View {
        let l10n = model.l10n
        let sources = SourceKind.allCases.compactMap { kind in
            model.snapshot.sources.first { $0.kind == kind && $0.isDetected }
        }

        VStack(alignment: .leading, spacing: DashboardLayout.rowSpacing) {
            HStack(spacing: Spacing.s) {
                SectionTitle(text: l10n("dashboard.sources"), latin: model.dashboardLatinCaps)
                Spacer(minLength: 0)
                ColumnCaption(text: l10n("dashboard.today"), width: DashboardLayout.tokensColumn)
                ColumnCaption(text: l10n("unit.tokS"), width: DashboardLayout.speedColumn)
            }
            .padding(.bottom, 2)
            ForEach(sources) { source in
                SourceRow(model: model, source: source)
                    .transition(.opacity)
            }
        }
        .animation(model.dashboardMotion, value: sources.map(\.kind))
    }
}

extension SourceStatus {
    /// The source's folder exists or it has data today.
    var isDetected: Bool { available || today.requests > 0 }
}

private struct SourceRow: View {
    let model: AppModel
    let source: SourceStatus

    @Environment(\.colorScheme) private var colorScheme

    private enum Status { case live, disabled }

    private var status: Status { source.enabled ? .live : .disabled }

    var body: some View {
        let l10n = model.l10n
        let status = status
        let speed = model.value(of: source.live)

        HStack(spacing: Spacing.s) {
            Circle()
                .fill(source.kind.tint(dark: colorScheme == .dark))
                .frame(width: 8, height: 8)
                .opacity(status == .live ? 1 : 0.35)
                .accessibilityHidden(true)
            Text(l10n(source.kind.titleKey))
                .font(DashboardFont.row)
                .foregroundStyle(status == .live ? AnyShapeStyle(.primary) : AnyShapeStyle(.dashboardSecondary))
                .lineLimit(1)
                .layoutPriority(1)
            Spacer(minLength: Spacing.xs)
            switch status {
            case .live:
                Text(SpeedFormat.tokens(source.today.output))
                    .font(DashboardFont.detail)
                    .foregroundStyle(.dashboardSecondary)
                    .frame(width: DashboardLayout.tokensColumn, alignment: .trailing)
                    .accessibilityLabel("\(l10n("dashboard.today")) \(l10n("today.output")) \(SpeedFormat.tokens(source.today.output))")
                Text(SpeedFormat.speed(speed))
                    .font(DashboardFont.rowNumber)
                    .foregroundStyle(speed > 0 ? AnyShapeStyle(.primary) : AnyShapeStyle(.dashboardSecondary))
                    .frame(width: DashboardLayout.speedColumn, alignment: .trailing)
                    .accessibilityLabel(l10n("mac.settings.speedValue", SpeedFormat.speed(speed)))
            case .disabled:
                Text(l10n("source.disabled"))
                    .font(DashboardFont.detail)
                    .foregroundStyle(.dashboardSecondary)
                    .lineLimit(1)
            }
        }
        .frame(minHeight: DashboardLayout.rowHeight)
        .accessibilityElement(children: .combine)
    }
}
