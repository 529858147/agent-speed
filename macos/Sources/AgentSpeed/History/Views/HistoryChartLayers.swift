import AgentSpeedCore
import Charts
import SwiftUI

/// Behind the marks: month rules and labels, and the band of the hovered day.
struct ChartBackdrop: View {
    let chart: HistoryChartModel
    let interaction: HistoryInteraction
    let proxy: ChartProxy
    let frame: CGRect
    let geometry: ChartDayGeometry
    let marks: ChartAxisMarks
    let l10n: Localizer
    let dark: Bool
    let increasedContrast: Bool

    var body: some View {
        let calendar = HistoryDates.calendar
        let rule = HistoryPalette.monthRule(dark: dark, increasedContrast: increasedContrast)
        let top = frame.minY - HistoryChartView.monthLabelSpace
        ZStack(alignment: .topLeading) {
            Canvas { context, _ in
                for index in marks.rules {
                    guard let x = proxy.position(forX: chart.range.day(at: index).date(in: calendar)) else { continue }
                    let px = (frame.minX + x).rounded() + 0.5
                    var path = Path()
                    path.move(to: CGPoint(x: px, y: top + 2))
                    path.addLine(to: CGPoint(x: px, y: frame.maxY))
                    context.stroke(path, with: .color(rule), style: StrokeStyle(lineWidth: 1, dash: [2, 3]))
                }
            }
            ForEach(marks.months, id: \.index) { label in
                let x = label.index == 0 ? 0 : (proxy.position(forX: label.day.date(in: calendar)) ?? 0)
                Text(HistoryText.month(label.day, withYear: label.kind == .year, l10n))
                    .font(HistoryFont.axis.weight(.medium))
                    .foregroundStyle(.historySecondary)
                    .fixedSize()
                    .offset(x: frame.minX + x + (label.index == 0 ? 0 : 5), y: top)
            }
            HoverBand(chart: chart, interaction: interaction, proxy: proxy, frame: frame, geometry: geometry, dark: dark)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
        .allowsHitTesting(false)
        .accessibilityHidden(true)
    }
}

private struct HoverBand: View {
    let chart: HistoryChartModel
    let interaction: HistoryInteraction
    let proxy: ChartProxy
    let frame: CGRect
    let geometry: ChartDayGeometry
    let dark: Bool

    var body: some View {
        if let day = interaction.hoveredDay ?? interaction.keyboardDay, chart.range.contains(day),
           let center = ChartPointerLayer.centerX(day, proxy: proxy) {
            let width = max(geometry.slot, geometry.barWidth + 6)
            RoundedRectangle(cornerRadius: min(6, width / 3), style: .continuous)
                .fill(HistoryPalette.hover(dark: dark))
                .frame(width: width, height: frame.height + 6)
                .offset(x: frame.minX + center - width / 2, y: frame.minY - 3)
        }
    }
}

/// Above the marks: pointer tracking, today's hatching, the crosshair and the tooltip.
struct ChartPointerLayer: View {
    let chart: HistoryChartModel
    let interaction: HistoryInteraction
    let proxy: ChartProxy
    let frame: CGRect
    let geometry: ChartDayGeometry
    let l10n: Localizer
    let dark: Bool
    let increasedContrast: Bool
    let reduceMotion: Bool

    @State private var tooltipHeight: CGFloat = 180

    var body: some View {
        ZStack(alignment: .topLeading) {
            TodayMarker(chart: chart, proxy: proxy, frame: frame, geometry: geometry, l10n: l10n, dark: dark)
            crosshair
            Rectangle()
                .fill(.clear)
                .contentShape(Rectangle())
                .frame(width: max(frame.width, 0), height: max(frame.height, 0))
                .offset(x: frame.minX, y: frame.minY)
                .onContinuousHover { phase in
                    switch phase {
                    case .active(let location): interaction.hover(day(atX: location.x), from: .chart)
                    case .ended: interaction.hover(nil, from: .chart)
                    }
                }
                .onTapGesture { location in
                    guard let day = day(atX: location.x), chart.days[day] != nil else { return }
                    interaction.revealInTable(day)
                }
            tooltip
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
    }

    static func centerX(_ day: DayKey, proxy: ChartProxy) -> CGFloat? {
        proxy.position(forX: day.date(in: HistoryDates.calendar).addingTimeInterval(12 * 3600))
    }

    private func day(atX x: CGFloat) -> DayKey? {
        guard let date: Date = proxy.value(atX: x) else { return nil }
        let day = DayKey(date, calendar: HistoryDates.calendar)
        return day.clamped(to: chart.range.start...chart.range.end)
    }

    private var activeDay: DayKey? {
        guard let day = interaction.hoveredDay ?? interaction.keyboardDay, chart.range.contains(day) else { return nil }
        return day
    }

    @ViewBuilder
    private var crosshair: some View {
        if let day = activeDay, let center = Self.centerX(day, proxy: proxy) {
            let x = (frame.minX + center).rounded() + 0.5
            Rectangle()
                .fill(HistoryPalette.secondary(dark: dark, increasedContrast: increasedContrast).opacity(0.6))
                .frame(width: 1, height: frame.height)
                .offset(x: x - 0.5, y: frame.minY)
                .allowsHitTesting(false)
            if let speed = chart.speeds[safe: chart.range.index(of: day)] ?? nil,
               let y = proxy.position(forY: chart.scaledSpeed(speed)) {
                Circle()
                    .fill(HistoryPalette.ink(dark: dark, increasedContrast: increasedContrast))
                    .frame(width: 8, height: 8)
                    .background(Circle().fill(Color(nsColor: .windowBackgroundColor)).padding(-2))
                    .offset(x: x - 4, y: frame.minY + y - 4)
                    .allowsHitTesting(false)
            }
        }
    }

    /// The card for the hovered day (chart pointer or keyboard; table hovers only show the crosshair).
    @ViewBuilder
    private var tooltip: some View {
        let fromChart = interaction.hovered.map { $0.origin == .chart } ?? (interaction.keyboardDay != nil)
        if fromChart, let day = activeDay, let center = Self.centerX(day, proxy: proxy) {
            let width = HistoryLayout.tooltipWidth
            let gap: CGFloat = 14
            let x = frame.minX + center
            let right = x + gap + width <= frame.maxX + 8
            let left = right ? x + gap : max(frame.minX - 8, x - gap - width)
            HistoryTooltip(day: day, summary: chart.days[day], chart: chart, l10n: l10n, dark: dark)
                .frame(width: width)
                .fixedSize(horizontal: false, vertical: true)
                .onGeometryChange(for: CGFloat.self) { $0.size.height } action: { tooltipHeight = $0 }
                .offset(x: left, y: max(frame.minY - 4, min(frame.minY + 6, frame.maxY - tooltipHeight)))
                .animation(reduceMotion ? nil : .spring(response: 0.26, dampingFraction: 0.9), value: day)
                .transition(.opacity.animation(HistoryMotion.fade))
                .allowsHitTesting(false)
                .accessibilityHidden(true)
        }
    }
}

/// Today's bar is drawn lighter with diagonal hatching in its source colors, labelled 進行中.
private struct TodayMarker: View {
    let chart: HistoryChartModel
    let proxy: ChartProxy
    let frame: CGRect
    let geometry: ChartDayGeometry
    let l10n: Localizer
    let dark: Bool

    var body: some View {
        let today = chart.today
        let segments = chart.segments.filter { $0.day == today }
        if chart.range.contains(today), !segments.isEmpty, let center = ChartPointerLayer.centerX(today, proxy: proxy),
           let total = segments.last?.upper, let topY = proxy.position(forY: total), let baseY = proxy.position(forY: 0) {
            let width = geometry.barWidth
            let left = frame.minX + center - width / 2
            Canvas { context, _ in
                for segment in segments {
                    guard let upper = proxy.position(forY: segment.upper), let lower = proxy.position(forY: segment.lower) else { continue }
                    let rect = CGRect(x: left, y: frame.minY + upper, width: width, height: max(0, lower - upper))
                    let radius = geometry.cornerRadius
                    let clip = Path(roundedRect: rect, cornerRadius: segment.isTop ? radius : 0, style: .continuous)
                    context.drawLayer { layer in
                        layer.clip(to: clip)
                        var stripes = Path()
                        let spacing: CGFloat = 4.5
                        var offset = -rect.height
                        while offset < rect.width + rect.height {
                            stripes.move(to: CGPoint(x: rect.minX + offset, y: rect.maxY))
                            stripes.addLine(to: CGPoint(x: rect.minX + offset + rect.height, y: rect.minY))
                            offset += spacing
                        }
                        layer.stroke(stripes, with: .color(segment.source.tint(dark: dark).opacity(0.9)), lineWidth: 1.2)
                    }
                }
            }
            .allowsHitTesting(false)
            // Centred over the bar, but never past the plot's edges.
            let labelWidth: CGFloat = 96
            let labelX = (frame.minX + center - labelWidth / 2).clamped(to: frame.minX...max(frame.minX, frame.maxX - labelWidth))
            let alignment: Alignment = labelX > frame.minX + center - labelWidth / 2 + 0.5
                ? .leading
                : (labelX < frame.minX + center - labelWidth / 2 - 0.5 ? .trailing : .center)
            Text(l10n("history.inProgress"))
                .font(.system(size: 9.5, weight: .semibold))
                .foregroundStyle(.historySecondary)
                .fixedSize()
                .padding(.horizontal, 4)
                .padding(.vertical, 1)
                .background(Capsule().fill(.background.opacity(0.88)))
                .frame(width: labelWidth, alignment: alignment)
                .offset(x: labelX, y: frame.minY + topY - 17)
                .opacity(baseY - topY > 0 ? 1 : 0)
                .allowsHitTesting(false)
        }
    }
}

extension Array {
    subscript(safe index: Int) -> Element? {
        indices.contains(index) ? self[index] : nil
    }
}
