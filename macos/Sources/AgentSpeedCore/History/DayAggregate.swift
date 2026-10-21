import Foundation

/// One source on one calendar day.
struct UsageKey: Hashable, Sendable {
    var day: String
    var source: SourceKind
}

struct ModelTally: Equatable, Sendable {
    var requests = 0
    var output = 0
}

/// Aggregates of METRICS 8 for one (day, source), from one file or summed over files.
struct DayAggregate: Equatable, Sendable {
    var totals = TokenTotals()
    var measuredOutput = 0
    var measuredMs: Int64 = 0
    var unfinalized = 0
    var agents = 0
    var models: [String: ModelTally] = [:]

    mutating func add(_ record: ResponseRecord) {
        totals.add(record.usage, sign: 1)
        if record.isFinal, let timed = TimedResponse(record) {
            measuredOutput = measuredOutput.saturatingAdd(record.usage.output)
            measuredMs += timed.durationMs
        }
        if !record.isFinal { unfinalized += 1 }
        models[record.model ?? "", default: ModelTally()].requests += 1
        models[record.model ?? "", default: ModelTally()].output += record.usage.output
    }

    mutating func merge(_ other: DayAggregate) {
        totals.requests = totals.requests.saturatingAdd(other.totals.requests)
        totals.input = totals.input.saturatingAdd(other.totals.input)
        totals.cachedInput = totals.cachedInput.saturatingAdd(other.totals.cachedInput)
        totals.cacheWriteInput = totals.cacheWriteInput.saturatingAdd(other.totals.cacheWriteInput)
        totals.output = totals.output.saturatingAdd(other.totals.output)
        totals.reasoning = totals.reasoning.saturatingAdd(other.totals.reasoning)
        measuredOutput = measuredOutput.saturatingAdd(other.measuredOutput)
        measuredMs += other.measuredMs
        unfinalized += other.unfinalized
        agents += other.agents
        for (model, tally) in other.models {
            models[model, default: ModelTally()].requests += tally.requests
            models[model, default: ModelTally()].output += tally.output
        }
    }

    /// Per-day contributions of one agent's responses, all attributed to `source` (the file's final source).
    static func contributions<Records: Sequence>(
        of records: Records,
        source: SourceKind,
        labeler: inout DayLabeler
    ) -> [UsageKey: DayAggregate] where Records.Element == ResponseRecord {
        var days: [UsageKey: DayAggregate] = [:]
        for record in records {
            days[UsageKey(day: labeler.label(forMs: record.endMs), source: source), default: DayAggregate()].add(record)
        }
        for key in days.keys { days[key]?.agents = 1 }
        return days
    }

    func usage(day: String, source: SourceKind, peak: Double?) -> DailyUsage {
        DailyUsage(
            day: day,
            source: source,
            totals: totals,
            measuredOutput: measuredOutput,
            measuredMs: measuredMs,
            unfinalized: unfinalized,
            agents: agents,
            peak: peak,
            models: models
                .map { ModelUsage(model: $0.key, requests: $0.value.requests, output: $0.value.output) }
                .sorted { $0.model.unicodeScalars.lexicographicallyPrecedes($1.model.unicodeScalars) }
        )
    }
}

/// `YYYY-MM-DD` of Unix-millisecond instants in one time zone, cached per day (responses of a file are mostly
/// close together in time).
struct DayLabeler: Sendable {
    let calendar: Calendar
    private var startMs: Int64 = 1
    private var endMs: Int64 = 0
    private var label = ""

    init(timeZone: TimeZone) {
        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = timeZone
        self.calendar = calendar
    }

    mutating func label(forMs ms: Int64) -> String {
        if ms >= startMs, ms < endMs { return label }
        let date = Date(timeIntervalSince1970: TimeInterval(ms) / 1000)
        label = Self.format(calendar.dateComponents([.year, .month, .day], from: date))
        if let interval = calendar.dateInterval(of: .day, for: date) {
            startMs = Clock.milliseconds(interval.start)
            endMs = Clock.milliseconds(interval.end)
        } else {
            startMs = 1
            endMs = 0
        }
        return label
    }

    func label(for date: Date) -> String {
        Self.format(calendar.dateComponents([.year, .month, .day], from: date))
    }

    private static func format(_ components: DateComponents) -> String {
        let year = components.year ?? 1970, month = components.month ?? 1, day = components.day ?? 1
        func pad(_ value: Int, _ width: Int) -> String {
            let text = String(value)
            return text.count >= width ? text : String(repeating: "0", count: width - text.count) + text
        }
        return "\(pad(year, 4))-\(pad(month, 2))-\(pad(day, 2))"
    }
}
