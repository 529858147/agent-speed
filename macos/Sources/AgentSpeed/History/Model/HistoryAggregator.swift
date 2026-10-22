import AgentSpeedCore
import Foundation

/// Turns the stored per-source days (`DailyUsage`) into what the Usage History window shows.
enum HistoryAggregator {
    /// Days with data inside `range` for one source (every source when nil), oldest first. Records with an unreadable
    /// day or without any usage are ignored. Peaks are kept only for the all-sources view (METRICS 8).
    static func daily(_ usage: [DailyUsage], source: SourceKind?, range: DayRange? = nil) -> [DaySummary] {
        var groups: [DayKey: [DailyUsage]] = [:]
        for record in usage {
            if let source, record.source != source { continue }
            guard hasUsage(record), let day = DayKey(record.day) else { continue }
            if let range, !range.contains(day) { continue }
            groups[day, default: []].append(record)
        }
        return groups.keys.sorted().map { summarize($0, groups[$0] ?? [], includePeak: source == nil) }
    }

    static func totals(_ days: [DaySummary]) -> HistoryTotals {
        var totals = HistoryTotals()
        for day in days {
            totals.requests += day.requests
            totals.input += day.input
            totals.cachedInput += day.cachedInput
            totals.output += day.output
            totals.measuredOutput += day.measuredOutput
            totals.measuredMs += day.measuredMs
            totals.activeDays += 1
            if let busiest = totals.busiestDay {
                if day.output > busiest.output || (day.output == busiest.output && day.day > busiest.day) {
                    totals.busiestDay = day
                }
            } else {
                totals.busiestDay = day
            }
        }
        return totals
    }

    /// Sources with at least one day of usage, in `SourceKind` order.
    static func sourcesWithHistory(_ usage: [DailyUsage]) -> [SourceKind] {
        let found = Set(usage.lazy.filter(hasUsage).map(\.source))
        return SourceKind.allCases.filter(found.contains)
    }

    /// First and last day with usage, or nil when there is none.
    static func bounds(_ usage: [DailyUsage], source: SourceKind?) -> DayRange? {
        var first: DayKey?
        var last: DayKey?
        for record in usage {
            if let source, record.source != source { continue }
            guard hasUsage(record), let day = DayKey(record.day) else { continue }
            if first.map({ day < $0 }) ?? true { first = day }
            if last.map({ day > $0 }) ?? true { last = day }
        }
        guard let first, let last else { return nil }
        return DayRange(first, last)
    }

    /// One value per day of `range`: `value` for days with data, `missing` for the others.
    static func series(_ days: [DaySummary], range: DayRange, missing: Double? = 0, value: (DaySummary) -> Double?) -> [Double?] {
        var series = [Double?](repeating: missing, count: max(range.count, 0))
        for day in days where range.contains(day.day) {
            series[range.index(of: day.day)] = value(day)
        }
        return series
    }

    /// Relative change from `previous`; nil when there is nothing to compare with.
    static func change(_ current: Double?, from previous: Double?) -> Double? {
        guard let current, let previous, current.isFinite, previous.isFinite, previous > 0 else { return nil }
        return (current - previous) / previous
    }

    /// Output tok/s over measured responses (METRICS 8); nil when nothing was measured.
    static func speed(measuredOutput: Int, measuredMs: Int64) -> Double? {
        measuredMs > 0 && measuredOutput > 0 ? Double(measuredOutput) / (Double(measuredMs) / 1000) : nil
    }

    static func hasUsage(_ record: DailyUsage) -> Bool {
        record.totals.requests > 0 || record.totals.output > 0 || record.totals.input > 0
    }

    private static func summarize(_ day: DayKey, _ records: [DailyUsage], includePeak: Bool) -> DaySummary {
        var requests = 0, input = 0, cached = 0, cacheWrite = 0, output = 0, reasoning = 0
        var measuredOutput = 0, unfinalized = 0, agents = 0
        var measuredMs: Int64 = 0
        var peak: Double?
        var models: [String: ModelUsage] = [:]
        var sources: [SourceKind: Int] = [:]
        for record in records {
            let totals = record.totals
            requests += max(0, totals.requests)
            input += max(0, totals.input)
            cached += max(0, totals.cachedInput)
            cacheWrite += max(0, totals.cacheWriteInput)
            output += max(0, totals.output)
            reasoning += max(0, totals.reasoning)
            measuredOutput += max(0, record.measuredOutput)
            measuredMs += max(0, record.measuredMs)
            unfinalized += max(0, record.unfinalized)
            agents += max(0, record.agents)
            // The peak is the day's overall peak repeated on every source row: combine with max, never sum.
            if includePeak, let value = record.peak, value.isFinite, value > 0 {
                peak = max(peak ?? 0, value)
            }
            sources[record.source, default: 0] += max(0, totals.output)
            for model in record.models where !model.model.trimmingCharacters(in: .whitespaces).isEmpty {
                var current = models[model.model] ?? ModelUsage(model: model.model, requests: 0, output: 0)
                current.requests += max(0, model.requests)
                current.output += max(0, model.output)
                current.measuredOutput += max(0, model.measuredOutput)
                current.measuredMs += max(0, model.measuredMs)
                models[model.model] = current
            }
        }
        let modelList = models.values
            .sorted { lhs, rhs in
                if lhs.output != rhs.output { return lhs.output > rhs.output }
                if lhs.requests != rhs.requests { return lhs.requests > rhs.requests }
                return lhs.model < rhs.model
            }
        let sourceList = SourceKind.allCases.compactMap { kind in
            sources[kind].flatMap { $0 > 0 ? SourceOutput(source: kind, output: $0) : nil }
        }
        return DaySummary(
            day: day, requests: requests, input: input, cachedInput: cached, cacheWriteInput: cacheWrite,
            output: output, reasoning: reasoning, measuredOutput: measuredOutput, measuredMs: measuredMs,
            unfinalized: unfinalized, agents: agents, peak: peak, models: modelList, sources: sourceList
        )
    }
}
