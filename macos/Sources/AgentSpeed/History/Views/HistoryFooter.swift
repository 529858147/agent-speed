import AgentSpeedCore
import SwiftUI

/// Analysis state on the left (progress while the background analysis runs, else when it last finished), the privacy
/// note on the right (DESIGN 4b, item 5).
struct HistoryFooter: View {
    let store: HistoryStore
    let l10n: Localizer
    let onRefresh: () -> Void

    var body: some View {
        HStack(spacing: Spacing.xs) {
            state
            Spacer(minLength: Spacing.m)
            HStack(spacing: 5) {
                Image(systemName: "lock.shield")
                    .imageScale(.small)
                    .accessibilityHidden(true)
                Text(l10n("history.privacy"))
                    .lineLimit(1)
                    .truncationMode(.tail)
            }
            .help(l10n("history.privacy"))
            .layoutPriority(-1)
        }
        .font(HistoryFont.footer)
        .foregroundStyle(.historySecondary)
        .padding(.horizontal, HistoryLayout.outerPadding)
        .frame(height: HistoryLayout.footerHeight)
        .frame(maxWidth: .infinity)
        .background(.bar)
        .overlay(alignment: .top) { Hairline() }
        .accessibilityElement(children: .contain)
    }

    @ViewBuilder
    private var state: some View {
        switch store.state {
        case .analyzing(let progress, let done, let total):
            HStack(spacing: Spacing.xs) {
                ProgressView(value: progress.clamped(to: 0...1))
                    .progressViewStyle(.linear)
                    .controlSize(.small)
                    .frame(width: 88)
                    .accessibilityHidden(true)
                Text(HistoryText.analyzing(progress, l10n))
                    .lineLimit(1)
                if total > 0 {
                    Text(HistoryText.analyzingFiles(done: done, total: total, l10n))
                        .lineLimit(1)
                }
            }
            .fixedSize()
            .accessibilityElement(children: .combine)
            .transition(.opacity)
        case .ready(let updatedAt):
            HStack(spacing: 6) {
                TimelineView(.periodic(from: .now, by: 30)) { context in
                    Text(HistoryText.updated(updatedAt, now: context.date, l10n))
                        .lineLimit(1)
                }
                Button(action: onRefresh) {
                    Image(systemName: "arrow.clockwise")
                        .font(.system(size: 10.5, weight: .semibold))
                        .frame(width: 18, height: 18)
                        .contentShape(Rectangle())
                }
                .buttonStyle(.plain)
                .help(l10n("mac.history.refresh"))
                .accessibilityLabel(l10n("mac.history.refresh"))
            }
            .fixedSize()
            .transition(.opacity)
        case .failed:
            Label(l10n("mac.history.failed"), systemImage: "exclamationmark.triangle")
                .lineLimit(1)
                .fixedSize()
        case .notLoaded:
            EmptyView()
        }
    }
}

/// Centered muted gauge with `history.empty`; while the first analysis runs it fills with the progress instead.
struct HistoryEmptyState: View {
    let state: HistoryState
    let l10n: Localizer

    @Environment(\.colorScheme) private var colorScheme
    @Environment(\.colorSchemeContrast) private var contrast

    var body: some View {
        VStack(spacing: Spacing.s) {
            switch state {
            case .analyzing(let progress, let done, let total):
                ZStack {
                    RestingGauge(lineWidth: 3.2)
                        .opacity(0.5)
                    GaugeGlyph(
                        fraction: max(0.02, progress),
                        fill: Color.accentColor,
                        track: .clear,
                        lineWidth: 3.2
                    )
                    .animation(.easeOut(duration: 0.4), value: progress)
                }
                .frame(width: 52, height: 52)
                .padding(.bottom, Spacing.xxs)
                Text(HistoryText.analyzing(progress, l10n))
                    .font(.system(size: 15, weight: .semibold))
                    .monospacedDigit()
                if total > 0 {
                    Text(HistoryText.analyzingFiles(done: done, total: total, l10n))
                        .font(.system(size: 13))
                        .monospacedDigit()
                        .foregroundStyle(.historySecondary)
                }
            default:
                RestingGauge(lineWidth: 3.2)
                    .frame(width: 52, height: 52)
                    .padding(.bottom, Spacing.xxs)
                Text(l10n("history.empty"))
                    .font(.system(size: 15, weight: .semibold))
                Text(l10n("history.empty.hint"))
                    .font(.system(size: 13))
                    .foregroundStyle(.historySecondary)
                    .multilineTextAlignment(.center)
            }
        }
        .padding(.bottom, Spacing.xxl)
        .accessibilityElement(children: .combine)
    }
}
