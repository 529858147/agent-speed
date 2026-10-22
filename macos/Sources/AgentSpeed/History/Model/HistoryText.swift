import AgentSpeedCore
import Foundation

/// User-visible text of the Usage History window, in the selected language.
@MainActor
enum HistoryText {
    private static let minus = "\u{2212}"
    private static let rangeDash = " – "

    /// `30 天` / `1 day`
    static func days(_ count: Int, _ l10n: Localizer) -> String {
        l10n(count == 1 ? "mac.history.days.one" : "mac.history.days", count)
    }

    /// `2026/9/22 – 10/21 · 30 天`; the year is written once when both ends share it. A single day reads
    /// `2026/10/21（週三）`.
    static func rangeSubtitle(_ range: DayRange, _ l10n: Localizer) -> String {
        if range.isSingleDay { return longDate(range.start, l10n) }
        let language = l10n.language
        let dates: String
        if range.start.year == range.end.year {
            // Spelled-out English puts the year last ("Sep 22 – Oct 21, 2026"); numeric forms put it first.
            dates = language == .en
                ? HistoryDates.string(range.start, .monthDay, language: language) + rangeDash + date(range.end, l10n)
                : date(range.start, l10n) + rangeDash + HistoryDates.string(range.end, .monthDay, language: language)
        } else {
            dates = date(range.start, l10n) + rangeDash + date(range.end, l10n)
        }
        return "\(dates) · \(days(range.count, l10n))"
    }

    /// `2026/10/21`, `Oct 21, 2026`
    static func date(_ day: DayKey, _ l10n: Localizer) -> String {
        HistoryDates.string(day, .date, language: l10n.language)
    }

    /// The month-day form inside the current year, the full date otherwise.
    static func shortDate(_ day: DayKey, today: DayKey, _ l10n: Localizer) -> String {
        day.year == today.year ? HistoryDates.string(day, .monthDay, language: l10n.language) : date(day, l10n)
    }

    static func weekday(_ day: DayKey, _ l10n: Localizer) -> String {
        HistoryDates.string(day, .weekday, language: l10n.language)
    }

    /// The table's date cell: `今天`, `昨天`, else `10/19（週日）`.
    static func tableDate(_ day: DayKey, today: DayKey, _ l10n: Localizer) -> String {
        if day == today { return l10n("history.today") }
        if day == today.adding(days: -1) { return l10n("history.yesterday") }
        return l10n("history.weekdayDate", shortDate(day, today: today, l10n), weekday(day, l10n))
    }

    /// The tooltip title: `2026/10/21（週三）`.
    static func longDate(_ day: DayKey, _ l10n: Localizer) -> String {
        l10n("history.weekdayDate", date(day, l10n), weekday(day, l10n))
    }

    /// A month rule's label: `10月` / `Oct`, with the year at year boundaries.
    static func month(_ day: DayKey, withYear: Bool, _ l10n: Localizer) -> String {
        HistoryDates.string(day, withYear ? .yearMonth : .month, language: l10n.language)
    }

    /// An axis label under a bar: `10/21` / `Oct 21`, or just the day of the month inside a labelled month.
    static func axisDay(_ day: DayKey, _ l10n: Localizer) -> String {
        HistoryDates.string(day, .monthDay, language: l10n.language)
    }

    /// An axis value in the compact form without trailing zeros: `1.5M`, `500k`, `2.5`.
    static func axisNumber(_ value: Double) -> String {
        guard value.isFinite, value > 0 else { return "0" }
        let compact = value >= 1000 ? SpeedFormat.tokens(Int(value.rounded())) : SpeedFormat.speed(value)
        guard let dot = compact.firstIndex(of: ".") else { return compact }
        let suffixStart = compact[dot...].firstIndex { !$0.isNumber && $0 != "." } ?? compact.endIndex
        var digits = String(compact[..<suffixStart])
        while digits.hasSuffix("0") { digits.removeLast() }
        if digits.hasSuffix(".") { digits.removeLast() }
        return digits + compact[suffixStart...]
    }

    /// Exact count with grouping separators, for tooltips: `1,234,567`.
    static func fullNumber(_ value: Int, _ l10n: Localizer) -> String {
        value.formatted(.number.locale(l10n.locale))
    }

    /// Compact speed with its unit, or `history.noSpeed`.
    static func speed(_ speed: Double?, _ l10n: Localizer) -> String {
        guard let speed, speed.isFinite, speed > 0 else { return l10n("history.noSpeed") }
        return "\(SpeedFormat.speed(speed)) \(l10n("unit.tokS"))"
    }

    /// Speed with one decimal and the unit, for tooltips on compact values.
    static func fullSpeed(_ speed: Double?, _ l10n: Localizer) -> String {
        guard let speed, speed.isFinite, speed > 0 else { return l10n("history.noSpeed") }
        let number = speed.formatted(.number.precision(.fractionLength(1)).locale(l10n.locale))
        return "\(number) \(l10n("unit.tokS"))"
    }

    /// A share as a whole percentage (`92%`); below 1 % but above 0 it reads `<1%`.
    static func percent(_ ratio: Double?) -> String {
        guard let ratio, ratio.isFinite else { return "–" }
        let clamped = ratio.clamped(to: 0...1)
        if clamped > 0, clamped < 0.005 { return "<1%" }
        return "\(Int((clamped * 100).rounded()))%"
    }

    /// A signed change: `+12%`, `−8%`, `+0.4%`, `0%`.
    static func change(_ ratio: Double) -> String {
        guard ratio.isFinite else { return "–" }
        let percent = ratio * 100
        let magnitude = abs(percent)
        let text: String
        if magnitude < 0.05 {
            return "0%"
        } else if magnitude < 1 {
            text = String(format: "%.1f", locale: Locale(identifier: "en_US_POSIX"), (magnitude * 10).rounded() / 10)
        } else {
            text = Int(magnitude.rounded()).formatted(.number.locale(Locale(identifier: "en_US_POSIX")))
        }
        return (percent > 0 ? "+" : minus) + text + "%"
    }

    /// `比前一段期間 +12%`
    static func versusPrevious(_ ratio: Double, _ l10n: Localizer) -> String {
        l10n("history.summary.vsPrevious", change(ratio))
    }

    /// `比前一天 +12%`
    static func versusPreviousDay(_ ratio: Double, _ l10n: Localizer) -> String {
        l10n("history.summary.vsPreviousDay", change(ratio))
    }

    /// `活躍 18 天`
    static func activeDays(_ days: Int, _ l10n: Localizer) -> String {
        l10n("history.summary.activeDays", days)
    }

    /// `單日最高 10/3`
    static func busiestDay(_ day: DayKey, today: DayKey, _ l10n: Localizer) -> String {
        l10n("history.summary.bestDay", shortDate(day, today: today, l10n))
    }

    /// Up to `max` model names, the rest as `+n`: `gpt-6, claude-opus +2`.
    static func topModels(_ models: [ModelUsage], _ l10n: Localizer, max: Int = 2) -> String {
        guard !models.isEmpty else { return "–" }
        let limit = Swift.max(1, max)
        let shown = models.prefix(limit).map(\.model).joined(separator: ", ")
        let rest = models.count - limit
        return rest > 0 ? "\(shown) \(l10n("mac.history.models.more", rest))" : shown
    }

    /// `更新於 剛剛`, `更新於 5 分鐘前`, then the time of day, then the date and time.
    static func updated(_ at: Date, now: Date, _ l10n: Localizer) -> String {
        let elapsed = max(0, now.timeIntervalSince(at))
        let when: String
        if elapsed < 60 {
            when = l10n("time.justNow")
        } else if elapsed < 3600 {
            when = l10n("time.minutesAgo", Int(elapsed / 60))
        } else {
            let calendar = HistoryDates.calendar
            let time = HistoryDates.string(at, .time, language: l10n.language)
            when = calendar.isDate(at, inSameDayAs: now)
                ? time
                : "\(date(DayKey(at, calendar: calendar), l10n)) \(time)"
        }
        return l10n("history.updated", when)
    }

    /// `正在分析歷史紀錄… 42%`
    static func analyzing(_ progress: Double, _ l10n: Localizer) -> String {
        let percent = progress.isFinite ? Int(floor(progress.clamped(to: 0...1) * 100)) : 0
        return l10n("history.analyzing", percent)
    }

    /// `12 / 340 個檔案`
    static func analyzingFiles(done: Int, total: Int, _ l10n: Localizer) -> String {
        l10n("history.analyzingFiles", max(0, done), max(0, total))
    }

    /// One labelled value for screen readers: `輸出：1.2M`.
    static func labelled(_ label: String, _ value: String, _ l10n: Localizer) -> String {
        l10n("mac.history.a11y.item", label, value)
    }

    static func join(_ parts: [String], _ l10n: Localizer) -> String {
        parts.filter { !$0.isEmpty }.joined(separator: l10n("mac.history.a11y.join"))
    }

    /// Everything a day holds, as one sentence for screen readers.
    static func accessible(_ day: DaySummary, today: DayKey, _ l10n: Localizer) -> String {
        var parts = [
            labelled(l10n("history.table.output"), fullNumber(day.output, l10n), l10n),
            labelled(l10n("history.table.input"), fullNumber(day.input, l10n), l10n),
            labelled(l10n("history.table.requests"), fullNumber(day.requests, l10n), l10n),
            labelled(l10n("history.table.cacheHit"), percent(day.cacheHitRatio), l10n),
            labelled(l10n("history.table.speed"), speed(day.averageSpeed, l10n), l10n),
        ]
        if let peak = day.peak {
            parts.append(labelled(l10n("history.table.peak"), speed(peak, l10n), l10n))
        }
        parts.append(labelled(l10n("history.table.agents"), fullNumber(day.agents, l10n), l10n))
        if !day.models.isEmpty {
            let models = day.models.prefix(3).map { modelWithSpeed($0, l10n) } + (day.models.count > 3 ? [l10n("mac.history.models.more", day.models.count - 3)] : [])
            parts.append(labelled(l10n("history.table.models"), models.joined(separator: ", "), l10n))
        }
        return labelled(dayTitle(day.day, today: today, l10n), join(parts, l10n), l10n)
    }

    /// A breakdown bar's name: the source, the model, or `其他模型`.
    static func breakdownName(_ subject: BreakdownItem.Subject, _ l10n: Localizer) -> String {
        switch subject {
        case .source(let source): l10n(source.titleKey)
        case .model(let model): model
        case .otherModels: l10n("mac.history.breakdown.otherModels")
        }
    }

    /// A model with its average speed when it was measured: `claude-opus-5-5 · 83 tok/s`.
    static func modelWithSpeed(_ model: ModelUsage, _ l10n: Localizer) -> String {
        guard let speed = HistoryAggregator.speed(measuredOutput: model.measuredOutput, measuredMs: model.measuredMs) else {
            return model.model
        }
        return "\(model.model) · \(self.speed(speed, l10n))"
    }

    /// One breakdown bar for screen readers: `輸出：312,345，佔比：38%，平均速度：88.2 tok/s，請求：151`.
    static func accessible(_ item: BreakdownItem, _ l10n: Localizer) -> String {
        join([
            labelled(l10n("history.table.output"), fullNumber(item.output, l10n), l10n),
            labelled(l10n("mac.history.breakdown.share"), percent(item.share), l10n),
            labelled(l10n("history.table.speed"), fullSpeed(item.averageSpeed, l10n), l10n),
            labelled(l10n("history.table.requests"), fullNumber(item.requests, l10n), l10n),
        ], l10n)
    }

    /// The day's long date, with `進行中` for today.
    static func dayTitle(_ day: DayKey, today: DayKey, _ l10n: Localizer) -> String {
        day == today ? "\(longDate(day, l10n)) \(l10n("history.inProgress"))" : longDate(day, l10n)
    }
}
