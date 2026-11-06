import AgentSpeedCore
import SwiftUI

/// What the status line needs of the WebSocket session (DESIGN 11, Status), published only when it changes.
struct ReportStreamState: Equatable {
    var open: Bool
    var stop: ReportStreamScheduler.Stop?
    /// A connection failed since the last stable one.
    var failing: Bool

    init(_ scheduler: ReportStreamScheduler) {
        open = scheduler.connection == .open
        stop = scheduler.stop
        failing = scheduler.failures > 0
    }

    init(open: Bool, stop: ReportStreamScheduler.Stop? = nil, failing: Bool = false) {
        self.open = open
        self.stop = stop
        self.failing = failing
    }
}

/// The status lines only the WebSocket transport has (DESIGN 11, Status). A connected socket alone is never one of
/// them: only an acknowledged report counts as reporting. A service without the capability has none: reports go over
/// HTTP then and the line is HTTP's.
enum ReportStreamIssue: Equatable {
    /// Amber, with the last acknowledged report's time.
    case reconnecting(last: Date?)
    /// Red, with *Try Again*.
    case mismatch, replaced
    case incompatible, nonce, clientState

    /// The stream's own issue for `state`; nil when a shared status applies (a stop with a shared problem included).
    init?(_ state: ReportStreamState, lastDelivery: Date?) {
        switch state.stop {
        case .mismatch: self = .mismatch
        case .replaced: self = .replaced
        case .incompatible: self = .incompatible
        case .nonce: self = .nonce
        case .clientState: self = .clientState
        case .some: return nil
        case nil:
            guard state.failing, !state.open else { return nil }
            self = .reconnecting(last: lastDelivery)
        }
    }

    /// The shared problem a stop stands for (the user's fix clears it); nil for the stream's own stops.
    static func problem(for stop: ReportStreamScheduler.Stop) -> ReportProblem? {
        switch stop {
        case .apiKey: .apiKey
        case .forbidden: .forbidden
        case .keyMismatch: .keyMismatch
        case .upgrade: .upgrade
        case .rejected: .rejected
        case .gone: .gone
        case .nonce, .clientState, .incompatible, .mismatch, .replaced: nil
        }
    }

    /// A stop: the user has to act. Shown ahead of waits and the activity.
    var isStop: Bool {
        switch self {
        case .reconnecting: false
        case .mismatch, .replaced, .incompatible, .nonce, .clientState: true
        }
    }

    var offersRetry: Bool { self == .mismatch || self == .replaced }

    var key: String {
        switch self {
        case .reconnecting(let last): last == nil ? "report.status.wsReconnectingNever" : "report.status.wsReconnecting"
        case .mismatch: "report.status.wsMismatch"
        case .replaced: "report.status.wsReplaced"
        case .incompatible: "report.status.wsIncompatible"
        case .nonce: "report.status.wsNonce"
        case .clientState: "report.status.wsClientState"
        }
    }
}

/// The line under the status while WebSocket is effective (DESIGN 11, Status): latency p50 and p95 (METRICS 11.9,
/// Latency), delivered and not delivered counts, and every counter for its tooltip.
struct ReportStreamStats: Equatable {
    var counters: ReportStreamScheduler.Counters
    var p50: Int64?
    var p95: Int64?

    init(_ scheduler: ReportStreamScheduler) {
        counters = scheduler.counters
        p50 = scheduler.latency.p50
        p95 = scheduler.latency.p95
    }

    init(counters: ReportStreamScheduler.Counters, p50: Int64?, p95: Int64?) {
        self.counters = counters
        self.p50 = p50
        self.p95 = p95
    }

    /// The session delivered or lost something: the line is shown.
    var isShown: Bool { counters.delivered > 0 || counters.notDelivered > 0 }

    @MainActor
    func line(_ l10n: Localizer) -> String {
        l10n("report.ws.stats", latency(p50, l10n), latency(p95, l10n), count(counters.delivered, l10n),
             count(counters.notDelivered, l10n))
    }

    @MainActor
    func tooltip(_ l10n: Localizer) -> String {
        let c = counters
        return l10n("report.ws.counters", count(c.built, l10n), count(c.accepted, l10n), count(c.duplicate, l10n),
                    count(c.rejected, l10n), count(c.stale, l10n), count(c.saturated, l10n), count(c.unknown, l10n))
    }

    @MainActor
    private func latency(_ value: Int64?, _ l10n: Localizer) -> String {
        guard let value else { return l10n("report.ws.noLatency") }
        return l10n("mac.report.ws.milliseconds", count(Int(max(0, value)), l10n))
    }

    @MainActor
    private func count(_ value: Int, _ l10n: Localizer) -> String {
        value.formatted(.number.locale(l10n.locale))
    }
}
