import AgentSpeedCore
import SwiftUI

/// `report.status` (DESIGN 11, Status): one line with a dot that always has text, the rank when the service gives one,
/// the WebSocket stats line, and Pause or Resume (or Try Again for a WebSocket stop that offers it). Changes are
/// announced politely, at most once a minute, except when the user must act.
struct ReportStatusSection: View {
    let model: AppModel

    @State private var lastAnnouncement = Date.distantPast
    @State private var announced: ReportStatus?

    var body: some View {
        let l10n = model.l10n
        let report = model.report
        Section {
            TimelineView(.periodic(from: .now, by: 1)) { context in
                let status = report.status
                HStack(alignment: .firstTextBaseline, spacing: Spacing.s) {
                    Circle()
                        .fill(status.color)
                        .frame(width: 8, height: 8)
                        .alignmentGuide(.firstTextBaseline) { $0[VerticalAlignment.center] + 4 }
                        .accessibilityHidden(true)
                    VStack(alignment: .leading, spacing: 2) {
                        Text(status.text(l10n, now: context.date))
                            .monospacedDigit()
                            .fixedSize(horizontal: false, vertical: true)
                            .contentTransition(.opacity)
                        if let detail = status.detail {
                            Footnote(l10n(detail.key))
                        }
                        if let rank = report.rank, report.isOn {
                            Footnote(l10n("report.status.rank", rank))
                        }
                        if report.effectiveTransport == .websocket, report.isOn, let stats = report.streamStats,
                           stats.isShown {
                            ReportStreamStatsLine(model: model, stats: stats)
                                .padding(.top, 1)
                                .transition(model.accessibility.reduceMotion ? .identity : .opacity)
                        }
                    }
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .accessibilityElement(children: .combine)
                    .accessibilityAddTraits(.updatesFrequently)
                    .accessibilityHint(streamCounters ?? "")
                    if case .stream(let issue) = status, issue.offersRetry {
                        Button(l10n("report.service.retry")) { report.retryStream() }
                            .controlSize(.small)
                            .fixedSize()
                            .disabled(!model.settings.networkEnabled)
                    } else if report.isOn {
                        ReportPauseControl(model: model)
                    } else if status == .review {
                        Button(l10n("report.service.review")) { report.reviewInvitation() }
                            .controlSize(.small)
                            .fixedSize()
                    } else if Self.offersRetry(status) {
                        Button(l10n("report.service.retry")) { report.retry() }
                            .controlSize(.small)
                            .fixedSize()
                    }
                }
                .onChange(of: Self.kind(status)) { _, _ in announce(status, now: context.date) }
            }
        } header: {
            Text(l10n("report.status"))
        }
    }

    /// Every WebSocket counter, for VoiceOver (sighted users get it as the stats line's tooltip).
    private var streamCounters: String? {
        let report = model.report
        guard report.effectiveTransport == .websocket, report.isOn, let stats = report.streamStats, stats.isShown else {
            return nil
        }
        return stats.tooltip(model.l10n)
    }

    /// Setting up after the invitation: a failed fetch or registration can be tried again at once.
    private static func offersRetry(_ status: ReportStatus) -> Bool {
        switch status {
        case .service, .registerFailed: true
        default: false
        }
    }

    /// The status without its changing times, so the announcement fires on real changes only.
    private static func kind(_ status: ReportStatus) -> String {
        switch status {
        case .reporting: "reporting"
        case .paused: "paused"
        case .rateLimited: "rateLimited"
        case .retrying: "retrying"
        case .streaming: "streaming"
        case .stream(.reconnecting): "reconnecting"
        default: "\(status)"
        }
    }

    private func announce(_ status: ReportStatus, now: Date) {
        guard status != announced else { return }
        guard status.needsUser || now.timeIntervalSince(lastAnnouncement) >= 60 else { return }
        announced = status
        lastAnnouncement = now
        AccessibilityNotification.Announcement(status.text(model.l10n, now: now)).post()
    }
}

/// `report.pause` as a pull-down (1 hour, until resumed), or `report.resume` while paused.
struct ReportPauseControl: View {
    let model: AppModel

    var body: some View {
        let l10n = model.l10n
        let report = model.report
        if report.isPaused {
            Button(l10n("report.resume")) { report.resume() }
                .buttonStyle(.borderless)
                .font(.system(size: 12))
        } else {
            Menu(l10n("report.pause")) {
                Button(l10n("report.pause.hour")) { report.pause(for: 3600) }
                Button(l10n("report.pause.indefinite")) { report.pause(for: nil) }
            }
            .menuStyle(.borderlessButton)
            .font(.system(size: 12))
            .fixedSize()
        }
    }
}

/// `report.ws.stats` under the status line (DESIGN 11, Status): latency p50 and p95, delivered and not delivered, with
/// every counter in the tooltip and in VoiceOver's help.
struct ReportStreamStatsLine: View {
    let model: AppModel
    let stats: ReportStreamStats

    var body: some View {
        let l10n = model.l10n
        Text(stats.line(l10n))
            .font(.system(size: 11))
            .monospacedDigit()
            .foregroundStyle(.secondary)
            .fixedSize(horizontal: false, vertical: true)
            .contentTransition(.numericText())
            .help(stats.tooltip(l10n))
            .animation(model.accessibility.reduceMotion ? nil : Motion.digits, value: stats)
    }
}
