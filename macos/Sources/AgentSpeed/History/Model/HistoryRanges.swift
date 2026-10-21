import Foundation

/// The range presets of the Usage History toolbar (DESIGN 4b, item 1).
enum HistoryRangeKind: String, CaseIterable, Identifiable, Sendable {
    case days7, days30, days90, year, all, custom

    var id: String { rawValue }

    var titleKey: String {
        switch self {
        case .days7: "history.range.7d"
        case .days30: "history.range.30d"
        case .days90: "history.range.90d"
        case .year: "history.range.year"
        case .all: "history.range.all"
        case .custom: "history.range.custom"
        }
    }
}

enum HistoryRanges {
    /// The days a preset covers, always ending today. `.all` starts at the first day with data (today when there is
    /// none); `.custom` uses `custom`, or the last 30 days when none was chosen.
    static func resolve(_ kind: HistoryRangeKind, today: DayKey, firstDataDay: DayKey?, custom: DayRange? = nil) -> DayRange {
        switch kind {
        case .days7: last(7, endingAt: today)
        case .days30: last(30, endingAt: today)
        case .days90: last(90, endingAt: today)
        case .year: DayRange(DayKey(year: today.year, month: 1, day: 1) ?? today, today)
        case .all: DayRange(firstDataDay.map { min($0, today) } ?? today, today)
        case .custom: custom ?? last(30, endingAt: today)
        }
    }

    /// The equally long period that ends the day before `range` starts.
    static func previous(_ range: DayRange) -> DayRange {
        let end = range.start.adding(days: -1)
        return DayRange(end.adding(days: -(range.count - 1)), end)
    }

    /// A custom range clamped to `bounds`, with the end never before the start.
    static func clamp(from: DayKey, to: DayKey, within bounds: DayRange) -> DayRange {
        let start = from.clamped(to: bounds.start...bounds.end)
        let end = to.clamped(to: bounds.start...bounds.end)
        return DayRange(start, max(start, end))
    }

    private static func last(_ days: Int, endingAt today: DayKey) -> DayRange {
        DayRange(today.adding(days: -(days - 1)), today)
    }
}
