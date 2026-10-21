import AgentSpeedCore
import Foundation

/// One segment of a day's bar: a source's output stacked on the ones below it.
struct ChartSegment: Identifiable, Equatable, Sendable {
    var id: String { "\(day)/\(source.rawValue)" }
    let day: DayKey
    let source: SourceKind
    let output: Int
    /// Cumulative output below and at the top of the segment.
    let lower: Int
    let upper: Int
    /// Output of the segment stacked on this one (0 for the top).
    let above: Int

    var isTop: Bool { above == 0 }
}

/// Everything the chart draws for one filter.
struct HistoryChartModel: Equatable, Sendable {
    let range: DayRange
    let today: DayKey
    /// Bottom first.
    let stack: [SourceKind]
    let isSingleSource: Bool
    let segments: [ChartSegment]
    /// One value per day of the range (nil where nothing was measured).
    let speeds: [Double?]
    let outputTicks: AxisTicks
    let speedTicks: AxisTicks
    let days: [DayKey: DaySummary]

    var speedRuns: [Range<Int>] { HistoryChartMath.runs(speeds) }

    func summary(at index: Int) -> DaySummary? {
        guard index >= 0, index < range.count else { return nil }
        return days[range.day(at: index)]
    }

    /// Speed mapped onto the output axis, so both share one plot.
    func scaledSpeed(_ speed: Double) -> Double {
        speedTicks.max > 0 ? speed / speedTicks.max * outputTicks.max : 0
    }
}

/// Everything the window shows for one filter, computed once per change of the data or the filter.
struct HistoryContent: Equatable, Sendable {
    let today: DayKey
    let source: SourceKind?
    let rangeKind: HistoryRangeKind
    let range: DayRange
    /// First and last day with data for the selected source.
    let dataBounds: DayRange?
    /// Sources the picker offers: those with history, plus the selected one.
    let pickerSources: [SourceKind]
    /// Days with data in the range, oldest first.
    let days: [DaySummary]
    let cards: [SummaryCard]
    let chart: HistoryChartModel
    let columns: [HistoryColumn]
    let sort: HistorySort
    /// The table rows in the chosen order.
    let rows: [DaySummary]

    var isEmpty: Bool { days.isEmpty }

    /// The range of days the custom range pickers allow.
    var selectableBounds: DayRange {
        DayRange(dataBounds.map { min($0.start, today) } ?? today.adding(days: -29), today)
    }

    @MainActor
    static func build(
        usage: [DailyUsage],
        source: SourceKind?,
        rangeKind: HistoryRangeKind,
        customRange: DayRange?,
        sort: HistorySort,
        today: DayKey,
        l10n: Localizer
    ) -> HistoryContent {
        let bounds = HistoryAggregator.bounds(usage, source: source)
        let range = HistoryRanges.resolve(rangeKind, today: today, firstDataDay: bounds?.start, custom: customRange)
        let previousRange = HistoryRanges.previous(range)
        let window = DayRange(previousRange.start, range.end)
        let both = HistoryAggregator.daily(usage, source: source, range: window)
        let days = both.filter { range.contains($0.day) }
        let previous = both.filter { previousRange.contains($0.day) }

        var pickerSources = HistoryAggregator.sourcesWithHistory(usage)
        if let source, !pickerSources.contains(source) {
            pickerSources = SourceKind.allCases.filter { pickerSources.contains($0) || $0 == source }
        }
        let columns = HistoryColumn.visible(allSources: source == nil)
        let effectiveSort = columns.contains(sort.column) ? sort : HistorySort()
        return HistoryContent(
            today: today,
            source: source,
            rangeKind: rangeKind,
            range: range,
            dataBounds: bounds,
            pickerSources: pickerSources,
            days: days,
            cards: HistorySummary.build(days: days, previous: previous, range: range, today: today, l10n),
            chart: chartModel(days: days, range: range, today: today, source: source),
            columns: columns,
            sort: effectiveSort,
            rows: HistoryTable.sort(days, by: effectiveSort)
        )
    }

    /// The same content in another row order.
    @MainActor
    func sorted(by sort: HistorySort) -> HistoryContent {
        let sort = columns.contains(sort.column) ? sort : HistorySort()
        return HistoryContent(
            today: today, source: source, rangeKind: rangeKind, range: range, dataBounds: dataBounds,
            pickerSources: pickerSources, days: days, cards: cards, chart: chart, columns: columns, sort: sort,
            rows: HistoryTable.sort(days, by: sort)
        )
    }

    private static func chartModel(days: [DaySummary], range: DayRange, today: DayKey, source: SourceKind?) -> HistoryChartModel {
        let stack = source.map { [$0] } ?? HistoryChartMath.stackOrder(days)
        var segments: [ChartSegment] = []
        var maxOutput = 0
        for day in days {
            var lower = 0
            let parts = stack.compactMap { kind -> (SourceKind, Int)? in
                let output = source == nil ? day.output(of: kind) : day.output
                return output > 0 ? (kind, output) : nil
            }
            for (index, part) in parts.enumerated() {
                segments.append(ChartSegment(
                    day: day.day, source: part.0, output: part.1,
                    lower: lower, upper: lower + part.1, above: index + 1 < parts.count ? parts[index + 1].1 : 0
                ))
                lower += part.1
            }
            maxOutput = max(maxOutput, lower)
        }
        let speeds = HistoryAggregator.series(days, range: range, missing: nil) { $0.averageSpeed }
        let outputTicks = HistoryChartMath.ticks(Double(maxOutput) * 1.04, maxIntervals: 4)
        let maxSpeed = speeds.compactMap { $0 }.max() ?? 0
        return HistoryChartModel(
            range: range,
            today: today,
            stack: stack,
            isSingleSource: source != nil,
            segments: segments,
            speeds: speeds,
            outputTicks: outputTicks,
            speedTicks: HistoryChartMath.alignedTicks(maxSpeed * 1.04, intervals: outputTicks.intervals),
            days: Dictionary(uniqueKeysWithValues: days.map { ($0.day, $0) })
        )
    }
}
