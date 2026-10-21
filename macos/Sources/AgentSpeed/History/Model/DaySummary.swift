import AgentSpeedCore
import Foundation

/// One source's output on one day, for stacking the chart.
struct SourceOutput: Hashable, Sendable {
    let source: SourceKind
    let output: Int
}

/// One calendar day for the selected source, or summed over every source (METRICS 8). Speeds are always
/// Σ measured output ÷ Σ measured time, never an average of averages.
struct DaySummary: Identifiable, Equatable, Sendable {
    var id: DayKey { day }
    let day: DayKey
    let requests: Int
    let input: Int
    let cachedInput: Int
    let cacheWriteInput: Int
    let output: Int
    let reasoning: Int
    let measuredOutput: Int
    let measuredMs: Int64
    let unfinalized: Int
    let agents: Int
    /// The day's overall live peak; only days observed live have one, and only the all-sources view shows it.
    let peak: Double?
    /// Every model of the day, most output first.
    let models: [ModelUsage]
    /// Output per source (non-zero only), in `SourceKind` order.
    let sources: [SourceOutput]

    /// Output tok/s over the day's measured responses.
    var averageSpeed: Double? {
        measuredMs > 0 && measuredOutput > 0 ? Double(measuredOutput) / (Double(measuredMs) / 1000) : nil
    }

    /// Share of input served from the cache (cachedInput ÷ input).
    var cacheHitRatio: Double? {
        input > 0 ? (Double(cachedInput) / Double(input)).clamped(to: 0...1) : nil
    }

    func output(of source: SourceKind) -> Int {
        sources.first { $0.source == source }?.output ?? 0
    }
}

/// Totals over a run of days.
struct HistoryTotals: Equatable, Sendable {
    var requests = 0
    var input = 0
    var cachedInput = 0
    var output = 0
    var measuredOutput = 0
    var measuredMs: Int64 = 0
    var activeDays = 0
    /// The day with the most output (the newest of equals).
    var busiestDay: DaySummary?

    static let empty = HistoryTotals()

    /// Σ measured output ÷ Σ measured time over the whole range.
    var averageSpeed: Double? {
        measuredMs > 0 && measuredOutput > 0 ? Double(measuredOutput) / (Double(measuredMs) / 1000) : nil
    }

    var hasData: Bool { activeDays > 0 }
}
