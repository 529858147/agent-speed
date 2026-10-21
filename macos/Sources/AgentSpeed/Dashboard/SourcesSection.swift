import AgentSpeedCore
import SwiftUI

/// One row per data source with its live speed and today's output (DESIGN section 4.3).
struct SourcesSection: View {
    let model: AppModel

    var body: some View {
        let l10n = model.l10n
        let sources = SourceKind.allCases.compactMap { kind in
            model.snapshot.sources.first { $0.kind == kind }
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
            }
        }
    }
}

private struct SourceRow: View {
    let model: AppModel
    let source: SourceStatus

    @Environment(\.colorScheme) private var colorScheme

    private enum Status { case live, notFound, disabled }

    private var status: Status {
        if !source.enabled { return .disabled }
        return source.available ? .live : .notFound
    }

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
            case .notFound:
                Text(l10n("source.notFound"))
                    .font(DashboardFont.detail)
                    .foregroundStyle(.dashboardSecondary)
                    .lineLimit(1)
                Button(l10n("mac.dashboard.openSettings")) {
                    model.commands?.showSettings(.sources)
                }
                .buttonStyle(.link)
                .font(DashboardFont.detail)
                .fixedSize()
                .help(l10n("mac.dashboard.openSettings"))
            case .disabled:
                Text(l10n("source.disabled"))
                    .font(DashboardFont.detail)
                    .foregroundStyle(.dashboardSecondary)
                    .lineLimit(1)
            }
        }
        .frame(minHeight: DashboardLayout.rowHeight)
        .accessibilityElement(children: status == .notFound ? .contain : .combine)
    }
}
