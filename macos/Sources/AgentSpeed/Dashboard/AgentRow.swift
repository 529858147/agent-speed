import AgentSpeedCore
import SwiftUI

/// One agent: phase badge, project, source and model, live speed. The phase in words is in the tooltip and for VoiceOver.
struct AgentRow: View {
    let model: AppModel
    let agent: AgentStatus
    /// Visual nesting level in the parent tree; roots are 0.
    let depth: Int
    /// Snapshot time, the reference for "x minutes ago".
    let now: Date

    @Environment(\.colorScheme) private var colorScheme

    var body: some View {
        let l10n = model.l10n
        let sourceName = l10n(agent.source.titleKey)
        let project = agent.project.isEmpty ? sourceName : agent.project
        // Model first: when space runs out the source name truncates, and the badge color still carries it.
        let detail = [agent.model, sourceName].compactMap { $0?.isEmpty == false ? $0 : nil }.joined(separator: " · ")
        let speed = model.value(of: agent.live)
        let status = status(l10n)

        HStack(spacing: Spacing.s) {
            PhaseBadge(
                phase: agent.phase,
                tint: agent.source.tint(dark: colorScheme == .dark),
                animated: !model.accessibility.reduceMotion
            )
            .anchorPreference(key: BadgeAnchors.self, value: .bounds) { [agent.id: $0] }

            HStack(alignment: .firstTextBaseline, spacing: 6) {
                Text(project)
                    .font(DashboardFont.rowTitle)
                    .lineLimit(1)
                    .layoutPriority(1)
                    .accessibilityLabel(agent.isSubagent ? "\(project), \(l10n("agents.subagent"))" : project)
                Text(detail)
                    .font(DashboardFont.detail)
                    .foregroundStyle(.dashboardSecondary)
                    .lineLimit(1)
                    .truncationMode(.tail)
            }

            Spacer(minLength: Spacing.xs)

            LiveSpeedValue(
                speed: speed,
                phase: agent.phase,
                l10n: l10n,
                reduceMotion: model.accessibility.reduceMotion
            )
        }
        .frame(height: DashboardLayout.rowHeight)
        .padding(.leading, CGFloat(depth) * DashboardLayout.subagentIndent)
        .contentShape(Rectangle())
        .help("\(project) · \(status)")
        .accessibilityElement(children: .combine)
        .accessibilityValue(status)
    }

    private func status(_ l10n: Localizer) -> String {
        switch agent.phase {
        case .generating: return l10n("phase.generating")
        case .tools: return l10n("phase.tools")
        case .idle:
            let seconds = Int(now.timeIntervalSince(agent.lastActivity).rounded(.down))
            let ago: String
            if seconds < 10 {
                ago = l10n("time.justNow")
            } else if seconds < 60 {
                ago = l10n("time.secondsAgo", seconds)
            } else {
                ago = l10n("time.minutesAgo", seconds / 60)
            }
            return "\(l10n("phase.idle")) · \(ago)"
        }
    }
}
