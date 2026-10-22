import AgentSpeedCore
import Foundation

/// One bar of a single day's breakdown: a source with all sources, else one model of the selected source.
struct BreakdownItem: Identifiable, Equatable, Sendable {
    enum Subject: Hashable, Sendable {
        case source(SourceKind)
        case model(String)
        /// Output the day's records do not attribute to a named model.
        case otherModels
    }

    var id: Subject { subject }
    let subject: Subject
    let output: Int
    let requests: Int
    /// Share of the day's output; the items' shares add up to 1 when the day has output.
    let share: Double
    /// Output relative to the largest item (the bar length, 0…1).
    let scale: Double
    /// Σ measured output ÷ Σ measured time (per source, or per model, METRICS 8); nil when nothing was measured.
    let averageSpeed: Double?
}

/// A single day split by source (all sources) or by model (one source), most output first (DESIGN 4b, item 1).
struct DayBreakdown: Equatable, Sendable {
    enum Kind: Sendable { case bySource, byModel }

    let day: DayKey
    let kind: Kind
    let items: [BreakdownItem]

    var output: Int { items.reduce(0) { $0 + $1.output } }
    var isEmpty: Bool { items.isEmpty }

    var titleKey: String {
        switch kind {
        case .bySource: "history.chart.bySource"
        case .byModel: "history.chart.byModel"
        }
    }
}

enum HistoryBreakdown {
    /// `day` split by source when `source` is nil, else by the models of `source`.
    static func build(_ usage: [DailyUsage], day: DayKey, source: SourceKind?) -> DayBreakdown {
        let records = usage.filter { record in
            (source == nil || record.source == source) && HistoryAggregator.hasUsage(record) && DayKey(record.day) == day
        }
        if source == nil {
            return DayBreakdown(day: day, kind: .bySource, items: bySource(records))
        }
        return DayBreakdown(day: day, kind: .byModel, items: byModel(records))
    }

    private struct Tally {
        var output = 0
        var requests = 0
        var measuredOutput = 0
        var measuredMs: Int64 = 0

        mutating func add(_ other: Tally) {
            output += other.output
            requests += other.requests
            measuredOutput += other.measuredOutput
            measuredMs += other.measuredMs
        }
    }

    private static func bySource(_ records: [DailyUsage]) -> [BreakdownItem] {
        var tallies: [BreakdownItem.Subject: Tally] = [:]
        for record in records {
            var tally = tallies[.source(record.source)] ?? Tally()
            tally.output += max(0, record.totals.output)
            tally.requests += max(0, record.totals.requests)
            tally.measuredOutput += max(0, record.measuredOutput)
            tally.measuredMs += max(0, record.measuredMs)
            tallies[.source(record.source)] = tally
        }
        return items(tallies)
    }

    /// Each model with its own measured output and time; what no named model accounts for (responses without a
    /// model, rows stored before per-model time existed) is gathered under `otherModels`.
    private static func byModel(_ records: [DailyUsage]) -> [BreakdownItem] {
        var tallies: [BreakdownItem.Subject: Tally] = [:]
        for record in records {
            var named = Tally()
            for model in record.models {
                let name = model.model.trimmingCharacters(in: .whitespaces)
                guard !name.isEmpty, model.output > 0 || model.requests > 0 else { continue }
                let part = Tally(output: max(0, model.output), requests: max(0, model.requests),
                                 measuredOutput: max(0, model.measuredOutput), measuredMs: max(0, model.measuredMs))
                tallies[.model(name), default: Tally()].add(part)
                named.add(part)
            }
            let rest = Tally(
                output: max(0, record.totals.output - named.output),
                requests: max(0, record.totals.requests - named.requests),
                measuredOutput: max(0, record.measuredOutput - named.measuredOutput),
                measuredMs: max(0, record.measuredMs - named.measuredMs)
            )
            if rest.output > 0 { tallies[.otherModels, default: Tally()].add(rest) }
        }
        return items(tallies)
    }

    private static func items(_ tallies: [BreakdownItem.Subject: Tally]) -> [BreakdownItem] {
        let total = tallies.values.reduce(0) { $0 + $1.output }
        let largest = tallies.values.map(\.output).max() ?? 0
        return tallies
            .map { subject, tally in
                let speed = HistoryAggregator.speed(measuredOutput: tally.measuredOutput, measuredMs: tally.measuredMs)
                return BreakdownItem(
                    subject: subject,
                    output: tally.output,
                    requests: tally.requests,
                    share: total > 0 ? Double(tally.output) / Double(total) : 0,
                    scale: largest > 0 ? Double(tally.output) / Double(largest) : 0,
                    averageSpeed: speed
                )
            }
            .sorted(by: precedes)
    }

    /// Most output first, then most requests, then source order or model name; other models always last.
    private static func precedes(_ lhs: BreakdownItem, _ rhs: BreakdownItem) -> Bool {
        if (lhs.subject == .otherModels) != (rhs.subject == .otherModels) { return rhs.subject == .otherModels }
        if lhs.output != rhs.output { return lhs.output > rhs.output }
        if lhs.requests != rhs.requests { return lhs.requests > rhs.requests }
        switch (lhs.subject, rhs.subject) {
        case let (.source(a), .source(b)):
            return (SourceKind.allCases.firstIndex(of: a) ?? 0) < (SourceKind.allCases.firstIndex(of: b) ?? 0)
        case let (.model(a), .model(b)):
            return a.localizedStandardCompare(b) == .orderedAscending
        default:
            return false
        }
    }
}
