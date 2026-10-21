import Foundation

/// Pure, deterministic speed computation (METRICS 6). Every time-dependent call takes an explicit `nowMs`.
struct SpeedEngine: Sendable {
    static let liveWindowMs: Int64 = 3000
    static let recentWindowMs: Int64 = 10 * 60_000
    static let recentCount = 3
    static let fallbackWindowMs: Int64 = 30 * 60_000
    static let fallbackCount = 10
    /// Long enough for the 30-minute fallback windows; history and live only need 15 minutes.
    static let retentionMs: Int64 = fallbackWindowMs
    static let staleMs: Int64 = 300_000
    static let agentListWindowMs: Int64 = 600_000
    static let historySeconds = 300
    static let newPeakMinimum = 20.0
    static let newPeakMargin = 1.0
    static let newPeakCooldownMs: Int64 = 60_000

    private(set) var agents: [String: EngineAgent] = [:]
    private var responses: [String: ResponseRecord] = [:]
    private var projects: [String: String] = [:]
    private var ledger: DayLedger?
    private var lastResponse: ResponseSummary?
    private var observedPeak = Rate.zero
    private var observedPeakAtMs: Int64?
    private var previousPeakOutput = 0.0
    private var lastNewPeakMs: Int64?

    init() {}

    // MARK: - Inputs

    mutating func setAgent(_ agent: EngineAgent) {
        agents[agent.id] = agent
    }

    mutating func removeAgent(id: String) {
        agents[id] = nil
    }

    /// Stores (or replaces) a response. Returns a `responseCompleted` event for live completions.
    @discardableResult
    mutating func upsert(
        _ record: ResponseRecord,
        project: String,
        isCompletion: Bool,
        emitsEvents: Bool
    ) -> SpeedEvent? {
        let old = responses.updateValue(record, forKey: record.key)
        projects[record.key] = project
        ledger?.replace(old, with: record)
        let summary = Self.summary(of: record, project: project)
        if lastResponse.map({ summary.endedAt >= $0.endedAt }) ?? true {
            lastResponse = summary
        }
        guard isCompletion, emitsEvents else { return nil }
        return .responseCompleted(summary)
    }

    /// Responses currently held (today's and the retention window), unordered.
    var storedResponses: [ResponseRecord] { Array(responses.values) }

    // MARK: - Computation

    mutating func advance(nowMs: Int64, todayStartMs: Int64, emitsEvents: Bool) -> EngineOutput {
        rollOverIfNeeded(todayStartMs: todayStartMs)
        prune(nowMs: nowMs, todayStartMs: todayStartMs)

        let timed = recentTimedResponses(nowMs: nowMs)
        let measured = timed.filter(\.record.isFinal)
        let unmeasured = timed.filter { !$0.record.isFinal }
        let references = SpeedReferences(timed: measured, nowMs: nowMs)
        let window = LiveWindow(
            nowMs: nowMs, measured: measured, unmeasured: unmeasured, agents: agents,
            latestEnds: latestEndsByAgent(), references: references
        )
        let history = Self.history(nowMs: nowMs, measured: measured, estimates: window.estimates)

        let live = window.total
        if live.output > observedPeak.output {
            observedPeak.output = live.output
            observedPeakAtMs = nowMs
        }
        observedPeak.total = max(observedPeak.total, live.total)

        let measuredPeak = ledger?.measuredPeak() ?? DayLedger.MeasuredPeak()
        let peak = Rate(
            output: max(measuredPeak.output, observedPeak.output),
            total: max(measuredPeak.total, observedPeak.total)
        )
        let peakAt = measuredPeak.output >= observedPeak.output ? measuredPeak.outputAtMs : observedPeakAtMs

        var events: [SpeedEvent] = []
        if emitsEvents,
           live.output >= Self.newPeakMinimum,
           live.output >= previousPeakOutput + Self.newPeakMargin,
           lastNewPeakMs.map({ nowMs - $0 >= Self.newPeakCooldownMs }) ?? true {
            events.append(.newPeak(live.output))
            lastNewPeakMs = nowMs
        }
        previousPeakOutput = peak.output

        return EngineOutput(
            nowMs: nowMs,
            live: live,
            liveEstimatedShare: window.estimatedShare,
            sourceLive: window.bySource,
            agents: agentList(nowMs: nowMs, window: window, references: references),
            history: history,
            today: ledger?.totals ?? TokenTotals(),
            todayBySource: ledger?.bySource ?? [:],
            averageToday: ledger?.average,
            unfinalizedToday: ledger?.unfinalized ?? 0,
            peakToday: peak,
            peakTodayAtMs: peak.output > 0 ? peakAt : nil,
            lastResponse: lastResponse,
            events: events
        )
    }

    // MARK: - Day and retention

    private mutating func rollOverIfNeeded(todayStartMs: Int64) {
        guard ledger?.dayStartMs != todayStartMs else { return }
        let isNewDay = ledger != nil
        var fresh = DayLedger(dayStartMs: todayStartMs)
        for record in responses.values where fresh.contains(record) {
            fresh.replace(nil, with: record)
        }
        ledger = fresh
        if isNewDay {
            observedPeak = .zero
            observedPeakAtMs = nil
            previousPeakOutput = 0
        }
    }

    private mutating func prune(nowMs: Int64, todayStartMs: Int64) {
        let cutoff = min(todayStartMs, nowMs - Self.retentionMs)
        guard responses.values.contains(where: { $0.endMs < cutoff }) else { return }
        responses = responses.filter { $0.value.endMs >= cutoff }
        projects = projects.filter { responses[$0.key] != nil }
    }

    /// Latest end of any stored response (valid or not) per agent.
    private func latestEndsByAgent() -> [String: Int64] {
        var ends: [String: Int64] = [:]
        for record in responses.values {
            ends[record.agentId] = max(ends[record.agentId] ?? record.endMs, record.endMs)
        }
        return ends
    }

    /// Valid responses ending within the retention window, most recent first.
    private func recentTimedResponses(nowMs: Int64) -> [TimedResponse] {
        let cutoff = nowMs - Self.retentionMs
        var timed: [TimedResponse] = []
        for record in responses.values where record.endMs >= cutoff {
            if let response = TimedResponse(record) { timed.append(response) }
        }
        timed.sort { lhs, rhs in
            lhs.endMs != rhs.endMs ? lhs.endMs > rhs.endMs : lhs.record.key < rhs.record.key
        }
        return timed
    }

    // MARK: - History

    private static func history(nowMs: Int64, measured timed: [TimedResponse], estimates: [InFlightEstimate]) -> [HistoryPoint] {
        let lastSecond = ResponseTiming.second(of: nowMs)
        let firstSecond = lastSecond - Int64(historySeconds) + 1
        var measured = Array(repeating: Rate.zero, count: historySeconds)
        var estimated = Array(repeating: Rate.zero, count: historySeconds)
        for response in timed where response.endMs > firstSecond * 1000 {
            response.distribute(firstSecond: firstSecond, lastSecond: lastSecond) { second, output, total in
                let index = Int(second - firstSecond)
                measured[index].output += output
                measured[index].total += total
            }
        }
        for estimate in estimates {
            let end = min(estimate.endMs ?? nowMs, nowMs)
            let from = max(firstSecond, ResponseTiming.second(of: estimate.startMs))
            let to = min(lastSecond, ResponseTiming.second(of: end))
            guard from <= to else { continue }
            for second in from...to {
                let overlap = ResponseTiming.overlap(second * 1000, (second + 1) * 1000, estimate.startMs, end)
                guard overlap > 0 else { continue }
                let index = Int(second - firstSecond)
                estimated[index].output += estimate.speed.output * Double(overlap) / 1000
                estimated[index].total += estimate.speed.total * Double(overlap) / 1000
            }
        }
        return (0..<historySeconds).map { index in
            HistoryPoint(
                time: Date(timeIntervalSince1970: TimeInterval(firstSecond + Int64(index))),
                measured: measured[index],
                estimated: estimated[index]
            )
        }
    }

    // MARK: - Agents list

    private func agentList(nowMs: Int64, window: LiveWindow, references: SpeedReferences) -> [EngineAgentStatus] {
        var rows: [EngineAgentStatus] = []
        for agent in agents.values {
            let isFresh = agent.lastLineMs.map { nowMs - $0 <= Self.staleMs } ?? false
            let phase = isFresh ? agent.phase : .idle
            let isRecent = agent.lastLineMs.map { $0 >= nowMs - Self.agentListWindowMs } ?? false
            guard isRecent || phase == .generating else { continue }
            rows.append(EngineAgentStatus(
                agent: agent,
                phase: phase,
                live: window.byAgent[agent.id] ?? .zero,
                recentSpeed: references.recent(for: agent.id)?.output
            ))
        }
        rows.sort { lhs, rhs in
            let lhsGenerating = lhs.phase == .generating
            let rhsGenerating = rhs.phase == .generating
            if lhsGenerating != rhsGenerating { return lhsGenerating }
            if lhs.live.output != rhs.live.output { return lhs.live.output > rhs.live.output }
            let lhsActivity = lhs.agent.lastLineMs ?? .min
            let rhsActivity = rhs.agent.lastLineMs ?? .min
            if lhsActivity != rhsActivity { return lhsActivity > rhsActivity }
            return lhs.agent.id < rhs.agent.id
        }
        return rows
    }

    // MARK: - Summaries

    static func summary(of record: ResponseRecord, project: String) -> ResponseSummary {
        let timed = TimedResponse(record)
        let duration = timed.map { Double($0.durationMs) / 1000 }
        return ResponseSummary(
            source: record.source,
            project: project,
            model: record.model,
            outputTokens: record.usage.output,
            totalTokens: record.usage.total,
            duration: duration,
            speed: duration.map { Double(record.usage.output) / $0 },
            endedAt: Date(timeIntervalSince1970: TimeInterval(record.endMs) / 1000)
        )
    }
}
