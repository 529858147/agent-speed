import AgentSpeedCore
import Foundation

/// Checks of the history window's pure logic, run by `--render-previews` (the app has no test target). The results
/// land in `history-checks.txt` next to the PNGs; failures are also printed to standard error.
@MainActor
enum HistoryLogicChecks {
    static func run(l10n: Localizer, into directory: URL) {
        var results: [(String, Bool)] = []
        func check(_ name: String, _ condition: Bool) { results.append((name, condition)) }

        // Day arithmetic.
        let today = DayKey(year: 2026, month: 10, day: 21)!
        check("parse round trip", DayKey("2026-10-21") == today && today.description == "2026-10-21")
        check("reject malformed day", DayKey("2026-13-01") == nil && DayKey("2026-2-30") == nil && DayKey("20261021") == nil)
        check("ordinal round trip", DayKey(ordinal: today.ordinal) == today && DayKey(ordinal: 0).description == "1970-01-01")
        check("leap day", DayKey("2028-02-29") != nil && DayKey("2026-02-29") == nil)
        check("weekday", today.weekday == 4 && DayKey("1970-01-01")!.weekday == 5)
        check("month boundary", DayKey("2026-02-28")!.adding(days: 1) == DayKey("2026-03-01"))

        // Ranges.
        let first = DayKey("2026-03-14")!
        check("7 days", HistoryRanges.resolve(.days7, today: today, firstDataDay: first) == DayRange(DayKey("2026-10-15")!, today))
        check("30 days count", HistoryRanges.resolve(.days30, today: today, firstDataDay: first).count == 30)
        check("this year", HistoryRanges.resolve(.year, today: today, firstDataDay: first).start == DayKey("2026-01-01"))
        check("all starts at first data", HistoryRanges.resolve(.all, today: today, firstDataDay: first).start == first)
        check("all without data", HistoryRanges.resolve(.all, today: today, firstDataDay: nil) == .single(today))
        let custom = DayRange(DayKey("2026-09-01")!, DayKey("2026-09-10")!)
        check("custom", HistoryRanges.resolve(.custom, today: today, firstDataDay: first, custom: custom) == custom)
        check("previous period", HistoryRanges.previous(custom) == DayRange(DayKey("2026-08-22")!, DayKey("2026-08-31")!))
        let bounds = DayRange(first, today)
        let clamped = HistoryRanges.clamp(from: DayKey("2026-01-01")!, to: DayKey("2026-02-01")!, within: bounds)
        check("custom clamps to data, end never before start", clamped == .single(first))

        // Aggregation: speed is Σ measured output ÷ Σ measured time; peaks combine with max, all sources only.
        func usage(_ day: String, _ source: SourceKind, output: Int, ms: Int64, peak: Double?) -> DailyUsage {
            DailyUsage(
                day: day, source: source,
                totals: TokenTotals(requests: 1, input: output * 10, cachedInput: output * 9, cacheWriteInput: 0, output: output, reasoning: 0),
                measuredOutput: ms > 0 ? output : 0, measuredMs: ms, unfinalized: 0, agents: 1, peak: peak,
                models: [ModelUsage(model: "m-\(source.rawValue)", requests: 1, output: output)]
            )
        }
        let sample = [
            usage("2026-10-20", .codex, output: 1000, ms: 10_000, peak: 300),
            usage("2026-10-20", .claudeCode, output: 3000, ms: 10_000, peak: 300),
            usage("2026-10-21", .codex, output: 500, ms: 0, peak: nil),
            usage("bad-day", .codex, output: 1, ms: 1, peak: nil),
        ]
        let all = HistoryAggregator.daily(sample, source: nil)
        check("days grouped and bad days dropped", all.count == 2)
        check("speed of sums", all.first?.averageSpeed == 200)
        check("peak is max, not sum", all.first?.peak == 300)
        check("peak hidden for one source", HistoryAggregator.daily(sample, source: .codex).first?.peak == nil)
        check("no speed without measurement", all.last?.averageSpeed == nil)
        check("range totals speed", HistoryAggregator.totals(all).averageSpeed == 200)
        check("cache hit", all.first?.cacheHitRatio == 0.9)
        check("stack order largest first", HistoryChartMath.stackOrder(all) == [.claudeCode, .codex])
        check("change", HistoryAggregator.change(120, from: 100).map { abs($0 - 0.2) < 1e-9 } == true)
        check("no change without a previous period", HistoryAggregator.change(120, from: 0) == nil)

        // Chart math.
        check("nice ticks", HistoryChartMath.ticks(1_380_000) == AxisTicks(step: 500_000, intervals: 3))
        check("aligned ticks", HistoryChartMath.alignedTicks(130, intervals: 3) == AxisTicks(step: 50, intervals: 3))
        check("runs break on gaps", HistoryChartMath.runs([1, 2, nil, 4, nil, nil, 7]) == [0..<2, 3..<4, 6..<7])
        let marks = HistoryChartMath.marks(DayRange(DayKey("2026-09-22")!, today), slot: 6, weekStart: 1)
        check("month rule on the 1st", marks.rules == [9])
        check("weekly labels on week starts", marks.days.allSatisfy { DayRange(DayKey("2026-09-22")!, today).day(at: $0).weekday == 1 })

        // Text.
        check("axis numbers", HistoryText.axisNumber(1_500_000) == "1.5M" && HistoryText.axisNumber(500_000) == "500k"
            && HistoryText.axisNumber(1_000_000) == "1M" && HistoryText.axisNumber(150) == "150")
        check("signed change", HistoryText.change(0.123) == "+12%" && HistoryText.change(-0.004) == "\u{2212}0.4%"
            && HistoryText.change(0.0001) == "0%")
        check("percent", HistoryText.percent(0.004) == "<1%" && HistoryText.percent(0.904) == "90%")

        // Table.
        let sorted = HistoryTable.sort(all, by: HistorySort(column: .speed, descending: true))
        check("rows without a value sort last", sorted.last?.averageSpeed == nil)
        check("default order is newest first", HistoryTable.sort(all, by: HistorySort()).first?.day == today)
        check("sort toggles", HistorySort().toggled(.date) == HistorySort(column: .date, descending: false)
            && HistorySort().toggled(.models) == HistorySort(column: .models, descending: false))
        let copied = HistoryTable.copyText(all, columns: HistoryColumn.visible(allSources: true), today: today, l10n)
        check("copy has a header and one line per row", copied.split(separator: "\n").count == 3)

        // CSV.
        let csv = HistoryCSV.build(all, source: nil)
        let lines = csv.components(separatedBy: "\r\n")
        check("csv header", lines.first == HistoryCSV.columns.joined(separator: ","))
        check("csv row", lines[1].hasPrefix("2026-10-20,all,2,40000,36000,0,4000,0,90.0,4000,20000,200.0,300.0,2,0,"))
        check("csv bom", HistoryCSV.encode(csv).prefix(3) == Data([0xEF, 0xBB, 0xBF]))
        check("csv file name", HistoryCSV.fileName(custom, source: .claudeCode) == "agent-speed-history-claude-code-2026-09-01-2026-09-10.csv")
        let risky = DaySummary(
            day: today, requests: 0, input: 0, cachedInput: 0, cacheWriteInput: 0, output: 0, reasoning: 0,
            measuredOutput: 0, measuredMs: 0, unfinalized: 0, agents: 0, peak: nil,
            models: [ModelUsage(model: "=cmd,\"x\"", requests: 1, output: 1)], sources: []
        )
        check("csv neutralizes formulas and quotes", HistoryCSV.build([risky], source: nil).contains("\"'=cmd,\"\"x\"\"\""))

        // Column layout never goes below the minimums and fills the width.
        let specs = HistoryColumn.allCases.map(HistoryColumnLayout.spec)
        let wide = HistoryColumnLayout.widths(specs, width: 1200)
        check("columns fill wide tables", abs(wide.reduce(0, +) + HistoryColumnLayout.gap * Double(specs.count - 1) - 1200) < 0.5)
        let narrow = HistoryColumnLayout.widths(specs, width: 600)
        check("columns keep their minimums", zip(narrow, specs).allSatisfy { $0 >= $1.minimum - 0.001 })

        let report = results.map { "\($0.1 ? "PASS" : "FAIL")  \($0.0)" }.joined(separator: "\n")
        try? (report + "\n").write(to: directory.appendingPathComponent("history-checks.txt"), atomically: true, encoding: .utf8)
        for (name, passed) in results where !passed {
            FileHandle.standardError.write(Data("history check failed: \(name)\n".utf8))
        }
    }
}
