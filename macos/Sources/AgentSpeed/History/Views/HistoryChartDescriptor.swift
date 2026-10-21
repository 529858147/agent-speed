import Accessibility
import AgentSpeedCore
import SwiftUI

/// Audio graph of the daily chart: output per day, with the average speed as a second series.
struct HistoryChartDescriptor: AXChartDescriptorRepresentable {
    let title: String
    let summary: String
    let dayTitle: String
    let outputTitle: String
    let speedTitle: String
    let labels: [String]
    let outputs: [Double]
    let speeds: [Double?]
    let outputMax: Double
    let language: AppLanguage

    @MainActor
    init(chart: HistoryChartModel, l10n: Localizer) {
        title = l10n("history.chart.output")
        summary = HistoryText.rangeSubtitle(chart.range, l10n)
        dayTitle = l10n("history.table.date")
        outputTitle = l10n("history.table.output")
        speedTitle = "\(l10n("history.chart.speed")) (\(l10n("unit.tokS")))"
        labels = chart.range.days.map { HistoryText.longDate($0, l10n) }
        outputs = chart.range.days.map { Double(chart.days[$0]?.output ?? 0) }
        speeds = chart.speeds
        outputMax = max(chart.outputTicks.max, 1)
        language = l10n.language
    }

    func makeChartDescriptor() -> AXChartDescriptor {
        let xAxis = AXCategoricalDataAxisDescriptor(title: dayTitle, categoryOrder: labels)
        let yAxis = AXNumericDataAxisDescriptor(title: outputTitle, range: 0...outputMax, gridlinePositions: []) { value in
            SpeedFormat.tokens(Int(value.rounded()))
        }
        let output = AXDataSeriesDescriptor(
            name: outputTitle,
            isContinuous: false,
            dataPoints: zip(labels, outputs).map { AXDataPoint(x: $0, y: $1) }
        )
        let speed = AXDataSeriesDescriptor(
            name: speedTitle,
            isContinuous: true,
            dataPoints: zip(labels, speeds).compactMap { label, value in value.map { AXDataPoint(x: label, y: $0) } }
        )
        return AXChartDescriptor(
            title: title,
            summary: summary,
            xAxis: xAxis,
            yAxis: yAxis,
            additionalAxes: [],
            series: [output, speed]
        )
    }
}
