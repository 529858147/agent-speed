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
    /// Σ measured output ÷ Σ measured time. Nil when nothing was measured, or for a model whose records also hold other
    /// models: the store keeps measured time per source and day, not per model (METRICS 8).
    let averageSpeed: Double?
    /// The speed is unknown only because time is not stored per model.
    let speedNotAttributable: Bool
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
        /// Some of the subject's output shares a record's measured time with another subject.
        var shared = false
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

    /// A record's measured time belongs to a model only when that model is the record's single contributor;
    /// output no named model accounts for is gathered under `otherModels`.
    private static func byModel(_ records: [DailyUsage]) -> [BreakdownItem] {
        var tallies: [BreakdownItem.Subject: Tally] = [:]
        for record in records {
            var parts: [(BreakdownItem.Subject, output: Int, requests: Int)] = []
            for model in record.models {
                let name = model.model.trimmingCharacters(in: .whitespaces)
                guard !name.isEmpty, model.output > 0 || model.requests > 0 else { continue }
                parts.append((.model(name), max(0, model.output), max(0, model.requests)))
            }
            let named = parts.reduce(0) { $0 + $1.output }
            let rest = max(0, record.totals.output) - named
            if rest > 0 {
                let namedRequests = parts.reduce(0) { $0 + $1.requests }
                parts.append((.otherModels, rest, max(0, record.totals.requests - namedRequests)))
            }
            let sole = parts.count == 1
            for part in parts {
                var tally = tallies[part.0] ?? Tally()
                tally.output += part.output
                tally.requests += part.requests
                if sole {
                    tally.measuredOutput += max(0, record.measuredOutput)
                    tally.measuredMs += max(0, record.measuredMs)
                } else {
                    tally.shared = true
                }
                tallies[part.0] = tally
            }
        }
        return items(tallies)
    }

    private static func items(_ tallies: [BreakdownItem.Subject: Tally]) -> [BreakdownItem] {
        let total = tallies.values.reduce(0) { $0 + $1.output }
        let largest = tallies.values.map(\.output).max() ?? 0
        return tallies
            .map { subject, tally in
                let speed = tally.shared ? nil : HistoryAggregator.speed(measuredOutput: tally.measuredOutput, measuredMs: tally.measuredMs)
                return BreakdownItem(
                    subject: subject,
                    output: tally.output,
                    requests: tally.requests,
                    share: total > 0 ? Double(tally.output) / Double(total) : 0,
                    scale: largest > 0 ? Double(tally.output) / Double(largest) : 0,
                    averageSpeed: speed,
                    speedNotAttributable: tally.shared
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
