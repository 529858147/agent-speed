import AgentSpeedCore
import Foundation

/// One segment of a day's bar: a source's output (or cost, in display units) stacked on the ones below it.
struct ChartSegment: Identifiable, Equatable, Sendable {
    var id: String { "\(day)/\(source.rawValue)" }
    let day: DayKey
    let source: SourceKind
    let value: Double
    /// Cumulative value below and at the top of the segment.
    let lower: Double
    let upper: Double
    /// Value of the segment stacked on this one (0 for the top).
    let above: Double

    var isTop: Bool { above == 0 }
}

/// Everything the chart draws for one filter.
struct HistoryChartModel: Equatable, Sendable {
    let range: DayRange
    let today: DayKey
    /// Daily output with the speed line, or daily cost without it (DESIGN 9).
    var metric: HistoryChartMetric = .output
    /// Bottom first.
    let stack: [SourceKind]
    let isSingleSource: Bool
    let segments: [ChartSegment]
    /// One value per day of the range (nil where nothing was measured); all nil for the cost metric.
    let speeds: [Double?]
    /// The left axis: output tokens, or cost in the display currency.
    let outputTicks: AxisTicks
    let speedTicks: AxisTicks
    let days: [DayKey: DaySummary]
    /// Formats the cost metric's axis and tooltips.
    var money: HistoryMoney?

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
    /// The single day split by source or model; nil for ranges of more than one day, which are charted instead.
    let breakdown: DayBreakdown?
    let columns: [HistoryColumn]
    let sort: HistorySort
    /// The table rows in the chosen order.
    let rows: [DaySummary]
    /// Money in the display currency and how costs count (DESIGN 9).
    var money: HistoryMoney
    var costOptions: HistoryCostOptions
    /// Models with unknown prices in the range (`history.unpricedNotice`).
    var unpricedModels: [UnpricedModel] = []

    var isEmpty: Bool { days.isEmpty }

    /// Requests of the range whose model has no known price.
    var unpricedRequests: Int { unpricedModels.reduce(0) { $0 + $1.requests } }

    /// At least one source is marked as a subscription: the toolbar offers `history.includeSubscriptions`.
    var offersSubscriptionToggle: Bool { !costOptions.marked.isEmpty }

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
        l10n: Localizer,
        money: HistoryMoney = .usd,
        costOptions: HistoryCostOptions = HistoryCostOptions()
    ) -> HistoryContent {
        let bounds = HistoryAggregator.bounds(usage, source: source)
        let range = HistoryRanges.resolve(rangeKind, today: today, firstDataDay: bounds?.start, custom: customRange)
        let previousRange = HistoryRanges.previous(range)
        let sparkRange = HistoryRanges.sparkRange(range)
        let window = DayRange(min(previousRange.start, sparkRange.start), range.end)
        let excluded = costOptions.excluded(for: source)
        let loaded = HistoryAggregator.daily(usage, source: source, range: window, excluded: excluded)
        let days = loaded.filter { range.contains($0.day) }
        let previous = loaded.filter { previousRange.contains($0.day) }
        let sparkDays = loaded.filter { sparkRange.contains($0.day) }

        var pickerSources = HistoryAggregator.sourcesWithHistory(usage)
        if let source, !pickerSources.contains(source) {
            pickerSources = SourceKind.allCases.filter { pickerSources.contains($0) || $0 == source }
        }
        let columns = HistoryColumn.visible(allSources: source == nil)
        let effectiveSort = columns.contains(sort.column) ? sort : HistorySort()
        let costTitleKey = source.map { costOptions.marked.contains($0) } == true ? "history.summary.costApi" : "history.summary.cost"
        return HistoryContent(
            today: today,
            source: source,
            rangeKind: rangeKind,
            range: range,
            dataBounds: bounds,
            pickerSources: pickerSources,
            days: days,
            cards: HistorySummary.build(days: days, previous: previous, sparkDays: sparkDays, range: range, today: today,
                                        money: money, costTitleKey: costTitleKey, l10n),
            chart: chartModel(days: days, range: range, today: today, source: source, metric: costOptions.metric, money: money),
            breakdown: range.isSingleDay
                ? HistoryBreakdown.build(usage, day: range.start, source: source, metric: costOptions.metric, excluded: excluded)
                : nil,
            columns: columns,
            sort: effectiveSort,
            rows: HistoryTable.sort(days, by: effectiveSort),
            money: money,
            costOptions: costOptions,
            unpricedModels: HistoryAggregator.unpricedModels(usage, source: source, range: range, excluded: excluded)
        )
    }

    /// The same content in another row order.
    @MainActor
    func sorted(by sort: HistorySort) -> HistoryContent {
        let sort = columns.contains(sort.column) ? sort : HistorySort()
        return HistoryContent(
            today: today, source: source, rangeKind: rangeKind, range: range, dataBounds: dataBounds,
            pickerSources: pickerSources, days: days, cards: cards, chart: chart, breakdown: breakdown, columns: columns, sort: sort,
            rows: HistoryTable.sort(days, by: sort), money: money, costOptions: costOptions, unpricedModels: unpricedModels
        )
    }

    /// Bars of output (with the speed line) or of cost in the display currency (without it), stacked by source.
    private static func chartModel(
        days: [DaySummary], range: DayRange, today: DayKey, source: SourceKind?, metric: HistoryChartMetric, money: HistoryMoney
    ) -> HistoryChartModel {
        let stack = source.map { [$0] } ?? HistoryChartMath.stackOrder(days, metric: metric)
        var segments: [ChartSegment] = []
        var maxValue = 0.0
        func value(_ day: DaySummary, _ kind: SourceKind) -> Double {
            switch metric {
            case .output: Double(source == nil ? day.output(of: kind) : day.output)
            case .cost: money.value(source == nil ? day.costMicroUsd(of: kind) : day.cost.microUsd)
            }
        }
        for day in days {
            var lower = 0.0
            let parts = stack.compactMap { kind -> (SourceKind, Double)? in
                let amount = value(day, kind)
                return amount > 0 ? (kind, amount) : nil
            }
            for (index, part) in parts.enumerated() {
                segments.append(ChartSegment(
                    day: day.day, source: part.0, value: part.1,
                    lower: lower, upper: lower + part.1, above: index + 1 < parts.count ? parts[index + 1].1 : 0
                ))
                lower += part.1
            }
            maxValue = max(maxValue, lower)
        }
        let speeds = metric == .output
            ? HistoryAggregator.series(days, range: range, missing: nil) { $0.averageSpeed }
            : Array(repeating: nil, count: max(range.count, 0))
        let outputTicks = HistoryChartMath.ticks(maxValue * 1.04, maxIntervals: 4)
        let maxSpeed = speeds.compactMap { $0 }.max() ?? 0
        return HistoryChartModel(
            range: range,
            today: today,
            metric: metric,
            stack: stack,
            isSingleSource: source != nil,
            segments: segments,
            speeds: speeds,
            outputTicks: outputTicks,
            speedTicks: HistoryChartMath.alignedTicks(maxSpeed * 1.04, intervals: outputTicks.intervals),
            days: Dictionary(uniqueKeysWithValues: days.map { ($0.day, $0) }),
            money: money
        )
    }
}

extension HistoryMoney {
    /// US dollars in English, for checks and callers without a display context.
    static let usd: HistoryMoney = {
        let usd = CurrencyTable.bundled.currencyOrUSD(Currency.usdCode)
        return HistoryMoney(currency: usd, usd: usd, language: "en", rate: 1, currencyName: "US Dollar")
    }()
}
