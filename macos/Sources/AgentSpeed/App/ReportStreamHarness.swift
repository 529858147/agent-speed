#if DEBUG
import AgentSpeedCore
import Foundation

/// The WebSocket part of `--report-e2e` (`AGENT_SPEED_REPORT_E2E_WEBSOCKET=1`, server with `--websocket`): the real
/// controller streaming by default, HTTP chosen and WebSocket again, streaming while busy, the stats, pause, the
/// network switch, back to HTTP and again, turning off with `leaving`, and the quit rule.
@MainActor
enum ReportStreamHarness {
    typealias Check = @MainActor (String, Bool) -> Void
    typealias Wait = @MainActor (String, Double, @escaping @MainActor () -> Bool) async -> Void

    static func run(_ model: AppModel, busy: (Bool) -> Void, check: Check, wait: Wait) async {
        let report = model.report
        check("WebSocket is the default transport", report.transport == .websocket
              && report.effectiveTransport == .websocket)
        busy(true)
        await wait("streaming by default", 10) {
            if case .streaming = report.status { true } else { false }
        }

        report.setTransport(.http)
        check("HTTP chosen: the session ends at once", report.transport == .http && report.stream == nil
              && report.lastDeliveryAt == nil)
        await wait("HTTP reports", 20) {
            if case .reporting = report.status { true } else { false }
        }

        report.setTransport(.websocket)
        check("switching applies at once", report.transport == .websocket && report.run == nil
              && report.lastDeliveryAt == nil)
        await wait("streaming while busy", 10) {
            if case .streaming = report.status { true } else { false }
        }
        await wait("reports acknowledged one by one", 10) { (report.stream?.scheduler.counters.accepted ?? 0) >= 4 }
        await wait("stats line with latency", 5) {
            report.streamStats?.isShown == true && report.streamStats?.p50 != nil
        }
        check("nothing unmatched or unknown", report.stream?.scheduler.counters.unmatched == 0
              && report.stream?.scheduler.counters.unknown == 0)
        check("the preview is the upgrade and the message body", {
            guard let preview = report.preview() else { return false }
            return preview.requestLine.hasPrefix("GET ws://") && preview.onChange && preview.nextIn == nil
                && String(decoding: preview.body.compactData, as: UTF8.self).contains("\"realtime\":")
        }())

        report.pause(for: 3600)
        await wait("paused: the connection closes", 3) { report.streamState?.open == false }
        report.resume()
        await wait("resumed: open again", 5) { report.streamState?.open == true }

        model.settings.networkEnabled = false
        await wait("network off: closed", 3) { report.streamState?.open == false && report.status == .networkOff }
        let offAccepted = report.stream?.scheduler.counters.accepted ?? 0
        try? await Task.sleep(for: .seconds(2))
        check("network off: nothing sent", report.stream?.scheduler.counters.accepted == offAccepted)
        model.settings.networkEnabled = true
        await wait("network back on: streaming resumes", 10) {
            (report.stream?.scheduler.counters.accepted ?? 0) > offAccepted
        }

        report.setTransport(.http)
        check("back to HTTP: the session ended", report.stream == nil && report.streamState == nil)
        await wait("HTTP reports again", 20) {
            if case .reporting = report.status { true } else { false }
        }

        report.setTransport(.websocket)
        await wait("WebSocket again", 10) { (report.stream?.scheduler.counters.accepted ?? 0) >= 1 }
        report.setEnabled(false)
        await wait("turned off: leaving answered, the session ends", 3) { report.stream == nil }
        check("turned off: the choice is kept", report.transport == .websocket)

        report.setEnabled(true)
        await wait("on again: streaming", 10) { (report.stream?.scheduler.counters.accepted ?? 0) >= 1 }
        let asked = Date()
        var replied: Date?
        report.prepareToQuit { replied = Date() }
        await wait("quit: replied within a second", 2) { replied != nil }
        check("quit: leaving waited for", (replied ?? .distantFuture).timeIntervalSince(asked) <= 1.2)
        report.shutdown()
        busy(false)
    }
}
#endif
