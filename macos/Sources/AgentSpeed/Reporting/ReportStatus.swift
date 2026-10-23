import AgentSpeedCore
import SwiftUI

/// The pane's status line (DESIGN 11, Status): always a localized string of the table, with a dot whose color never
/// carries meaning alone.
enum ReportStatus: Equatable {
    case off
    case networkOff
    case never
    case reporting(last: Date)
    case idle
    case quiet
    case paused(until: Date)
    case pausedIndefinitely
    case connecting
    case registering
    case retrying(reason: String)
    case rateLimited(until: Date)
    case changed
    case problem(ReportProblem)

    enum Tone: Equatable {
        case green, gray, amber, red
    }

    var tone: Tone {
        switch self {
        case .reporting: .green
        case .off, .networkOff, .never, .idle, .quiet, .paused, .pausedIndefinitely: .gray
        case .connecting, .registering, .retrying, .rateLimited: .amber
        case .changed, .problem: .red
        }
    }

    /// Red: the user has to act (the dashboard indicator's attention dot).
    var needsUser: Bool { tone == .red }

    @MainActor
    func text(_ l10n: Localizer, now: Date) -> String {
        switch self {
        case .off: return l10n("report.status.off")
        case .networkOff: return l10n("settings.network.offHint")
        case .never: return l10n("report.status.never")
        case .reporting(let last):
            let seconds = max(0, Int(now.timeIntervalSince(last).rounded(.down)))
            let when = seconds < 60
                ? l10n("time.secondsAgo", seconds)
                : last.formatted(Date.RelativeFormatStyle(presentation: .named, unitsStyle: .wide).locale(l10n.locale))
            return l10n("report.status.reporting", when)
        case .idle: return l10n("report.status.idle")
        case .quiet: return l10n("report.status.quiet")
        case .paused(let until): return l10n("report.status.paused", Self.time(until, l10n))
        case .pausedIndefinitely: return l10n("report.status.pausedIndefinitely")
        case .connecting: return l10n("report.status.connecting")
        case .registering: return l10n("report.status.registering")
        case .retrying(let reason): return l10n("report.status.retrying", reason)
        case .rateLimited(let until): return l10n("report.status.rateLimited", Self.time(until, l10n))
        case .changed: return l10n("report.service.changed")
        case .problem(let problem): return l10n(problem.statusKey)
        }
    }

    var color: Color {
        switch tone {
        case .green: .green
        case .gray: Color(nsColor: .tertiaryLabelColor)
        case .amber: .orange
        case .red: .red
        }
    }

    @MainActor
    static func time(_ date: Date, _ l10n: Localizer) -> String {
        date.formatted(Date.FormatStyle(date: .omitted, time: .shortened).locale(l10n.locale))
    }
}

extension ReportProblem {
    var statusKey: String {
        switch self {
        case .apiKey: "report.status.apiKey"
        case .usernameTaken: "report.status.usernameTaken"
        case .keyMismatch: "report.status.keyMismatch"
        case .rejected: "report.status.rejected"
        case .forbidden: "report.status.forbidden"
        case .upgrade: "report.status.upgrade"
        case .gone: "report.status.gone"
        }
    }
}
