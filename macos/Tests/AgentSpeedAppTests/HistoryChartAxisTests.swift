import AgentSpeedCore
import Foundation
import Testing
@testable import AgentSpeed

/// The plot and the copy its width is measured on (macOS issue #28) draw the same axis labels, read from the model.
@Suite("History chart axes")
@MainActor
struct HistoryChartAxisTests {
    let today = DayKey("2026-10-28")!

    private func chart(_ metric: HistoryChartMetric, traffic: HistoryTraffic = .none) -> HistoryChartModel {
        HistoryContent.build(
            usage: PreviewHistory.days(endingAt: today.date(in: HistoryDates.calendar), count: 40), source: nil, rangeKind: .days30,
            customRange: nil, sort: HistorySort(), today: today, l10n: Localizer(preference: .en),
            costOptions: HistoryCostOptions(metric: metric), traffic: traffic
        ).chart
    }

    @Test func outputLabelsOutputOnTheLeftAndSpeedOnTheRight() {
        let chart = chart(.output)
        let left = chart.axisValues.map(chart.leadingAxisLabel)
        let right = chart.outputTicks.values.map(chart.trailingAxisLabel)
        #expect(left.count == right.count)
        #expect(left.first == "0" && right.first == "0")
        #expect(left.last == HistoryText.axisNumber(chart.outputTicks.max))
        #expect(right.last == HistoryText.axisNumber(chart.speedTicks.max))
    }

    @Test func costLabelsAreMoney() {
        let chart = chart(.cost)
        #expect(chart.leadingAxisLabel(chart.outputTicks.max) == HistoryMoney.usd.axisLabel(chart.outputTicks.max, step: chart.outputTicks.step))
        #expect(chart.leadingAxisLabel(chart.outputTicks.max).hasPrefix("$"))
    }

    @Test func trafficNamesItsSides() {
        let store = TrafficStore()
        TrafficPreviewData.publish(into: store, now: today.date(in: HistoryDates.calendar).addingTimeInterval(12 * 3600))
        let traffic = HistoryTraffic(days: store.days, since: store.since, includeTools: true, bits: false)
        let chart = chart(.traffic, traffic: traffic)
        #expect(chart.leadingAxisLabel(chart.outputTicks.max).hasPrefix("↓ "))
        if let lower = chart.lowerTicks, lower.max > 0 {
            #expect(chart.leadingAxisLabel(-lower.max).hasPrefix("↑ "))
        }
        #expect(chart.leadingAxisLabel(0) == "0")
    }
}
