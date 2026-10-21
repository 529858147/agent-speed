import AgentSpeedCore
import Foundation

enum SummaryMetric: String, CaseIterable, Identifiable, Sendable {
    case output, input, requests, speed

    var id: String { rawValue }

    var titleKey: String {
        switch self {
        case .output: "history.summary.output"
        case .input: "history.summary.input"
        case .requests: "history.summary.requests"
        case .speed: "history.summary.speed"
        }
    }
}

struct SummaryCard: Identifiable, Equatable, Sendable {
    var id: SummaryMetric { metric }
    let metric: SummaryMetric
    /// Compact value (METRICS 7).
    let value: String
    /// Unit drawn after the value (speed only).
    let unit: String?
    /// Exact value for the tooltip.
    let fullValue: String
    /// Active days, busiest day or the change against the previous period.
    let secondary: String
    /// The change shown in `secondary`, when it is one (colored green or red).
    let change: Double?
    /// One value per day of the range; nil where the line breaks.
    let spark: [Double?]
}

/// The four summary cards (DESIGN 4b, item 2). Same choice of secondary lines as on Windows.
@MainActor
enum HistorySummary {
    /// - Parameters:
    ///   - days: days with data in `range`.
    ///   - previous: days with data in the equally long period before it.
    static func build(days: [DaySummary], previous: [DaySummary], range: DayRange, today: DayKey, _ l10n: Localizer) -> [SummaryCard] {
        let totals = HistoryAggregator.totals(days)
        let before = HistoryAggregator.totals(previous)

        let outputChange = HistoryAggregator.change(Double(totals.output), from: Double(before.output))
        let outputSecondary = outputChange.map { HistoryText.versusPrevious($0, l10n) }
            ?? totals.busiestDay.map { HistoryText.busiestDay($0.day, today: today, l10n) }
            ?? ""

        let busiestInput = days.max { lhs, rhs in
            lhs.input != rhs.input ? lhs.input < rhs.input : lhs.day < rhs.day
        }
        let inputSecondary = busiestInput.flatMap { $0.input > 0 ? HistoryText.busiestDay($0.day, today: today, l10n) : nil } ?? ""

        let speedChange = HistoryAggregator.change(totals.averageSpeed, from: before.averageSpeed)
        let speedValue = totals.averageSpeed

        return [
            SummaryCard(
                metric: .output, value: SpeedFormat.tokens(totals.output), unit: nil,
                fullValue: HistoryText.fullNumber(totals.output, l10n), secondary: outputSecondary, change: outputChange,
                spark: HistoryAggregator.series(days, range: range) { Double($0.output) }
            ),
            SummaryCard(
                metric: .input, value: SpeedFormat.tokens(totals.input), unit: nil,
                fullValue: HistoryText.fullNumber(totals.input, l10n), secondary: inputSecondary, change: nil,
                spark: HistoryAggregator.series(days, range: range) { Double($0.input) }
            ),
            SummaryCard(
                metric: .requests, value: SpeedFormat.tokens(totals.requests), unit: nil,
                fullValue: HistoryText.fullNumber(totals.requests, l10n),
                secondary: totals.activeDays > 0 ? HistoryText.activeDays(totals.activeDays, l10n) : "",
                change: nil,
                spark: HistoryAggregator.series(days, range: range) { Double($0.requests) }
            ),
            SummaryCard(
                metric: .speed, value: speedValue.map(SpeedFormat.speed) ?? "–", unit: speedValue == nil ? nil : l10n("unit.tokS"),
                fullValue: HistoryText.fullSpeed(speedValue, l10n),
                secondary: speedChange.map { HistoryText.versusPrevious($0, l10n) } ?? "",
                change: speedChange,
                spark: HistoryAggregator.series(days, range: range, missing: nil) { $0.averageSpeed }
            ),
        ]
    }
}
