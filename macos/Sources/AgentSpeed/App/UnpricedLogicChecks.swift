import AgentSpeedCore
import Foundation

/// Checks of the unknown-prices popover and Settings → Cost line (DESIGN 9, METRICS 9.10), run with the history
/// checks by `--render-previews`.
@MainActor
enum UnpricedLogicChecks {
    static func run(l10n: Localizer) -> [(String, Bool)] {
        var results: [(String, Bool)] = []
        func check(_ name: String, _ condition: Bool) { results.append(("unpriced: " + name, condition)) }
        func fact(_ model: String, _ provider: String?, fast: Bool = false, _ requests: Int) -> UnpricedFact {
            UnpricedFact(key: UnpricedFactKey(model: model, provider: provider, fast: fast), requests: requests)
        }
        func day(_ day: String, _ source: SourceKind, requests: Int, facts: [UnpricedFact], cost: Bool = true) -> DailyUsage {
            let unpriced = facts.reduce(0) { $0 + $1.requests }
            return DailyUsage(
                day: day, source: source, totals: TokenTotals(requests: requests, input: 10, output: 10), measuredOutput: 0,
                measuredMs: 0, unfinalized: 0, agents: 1, peak: nil, models: [],
                cost: cost ? CostTally(costMicroUsd: 100, unpricedRequests: unpriced) : nil, priceVersion: cost ? 3 : nil,
                unpriced: UnpricedReading(requests: requests, noCostData: cost ? 0 : requests, facts: cost ? facts : []))
        }
        let table = PriceBook.bundled
        let today = DayKey(year: 2026, month: 10, day: 26)!
        let usage = [
            day("2026-10-20", .codex, requests: 40, facts: [fact("neko-model-ab", "neko-route", 25), fact("/Users/dev/q.gguf", "custom", 2)]),
            day("2026-10-22", .codex, requests: 12, facts: [fact("neko-model-ab", "neko-route", 4), fact("gpt-5.5", nil, 3)]),
            day("2026-10-21", .claudeCode, requests: 9, facts: [fact("claude-sonnet-5-5", nil, fast: true, 2), fact("", nil, 1)]),
            day("2026-10-23", .workbuddyAi, requests: 5, facts: [fact("Kimi-K3-Turbo", "custom", 2)]),
            day("2026-10-21", .opencode, requests: 8, facts: [], cost: false),
        ]
        func content(source: SourceKind? = nil, marked: Set<SourceKind> = [], range: HistoryRangeKind = .days7) -> HistoryContent {
            HistoryContent.build(usage: usage, source: source, rangeKind: range, customRange: nil, sort: HistorySort(), today: today,
                                 l10n: l10n, costOptions: HistoryCostOptions(marked: marked), prices: table)
        }
        let all = content().unpriced
        check("the notice counts every unpriced request of the view", all.unpriced == 39 && content().unpricedRequests == 39)
        check("sources by unpriced requests, rows by requests", all.groups.map(\.source) == [.codex, .claudeCode, .workbuddyAi, .opencode]
              && all.groups.first?.rows.first?.key.model == "neko-model-ab" && all.groups.first?.rows.first?.requests == 29)
        check("the current table moves a priced fact to pricedNow", all.pricedNow == 3 && all.groups[0].pricedNow == 3
              && all.groups.allSatisfy { $0.rowRequests + $0.pricedNow == $0.unpriced })
        check("a row spans the days of its facts", all.groups[0].rows.first.map { $0.first == "2026-10-20" && $0.last == "2026-10-22" } == true)
        let subscription = content(marked: [.codex]).unpriced
        check("subscription sources are left out", subscription.unpriced == 5 && subscription.range.excluded == [.codex])
        let single = content(source: .codex, marked: [.codex]).unpriced
        check("a source selected directly shows its own", single.unpriced == 34 && single.range.excluded.isEmpty
              && single.range.source == .codex)
        check("range names of the copied text", content().unpriced.range.view == "7d" && content(range: .all).unpriced.range.view == "all"
              && content(range: .yesterday).unpriced.range.view == "yesterday")

        let text = UnpricedText.render(all, app: UnpricedText.App(version: "1.4.2", platform: "macos", os: "macos-26"))
        let lines = text.split(separator: "\n", omittingEmptySubsequences: false)
        check("copied text: header, LF endings, no path", lines.first == "Agent Speed unpriced diagnostics 1" && text.hasSuffix("\n")
              && !text.contains("/Users/") && text.contains("model=\"<path>\"")
              && text.contains("range view=7d first=2026-10-20 last=2026-10-26 days=7 source=all excluded=none")
              && text.contains("source id=opencode requests=8 unpriced=0 pricedNow=0 noCostData=8 derived=0 rows=0"))

        let rows = all.groups.flatMap(\.rows)
        let sonnet = rows.first { $0.key.model == "claude-sonnet-5-5" }!
        let kimi = rows.first { $0.key.model == "Kimi-K3-Turbo" }!
        let neko = rows.first { $0.key.model == "neko-model-ab" }!
        let empty = rows.first { $0.key.model.isEmpty }!
        check("details: Fast without a lookup equal to the model", UnpricedDisplay.details(sonnet, l10n)
              == UnpricedDisplay.Details(provider: nil, fast: true, lookup: nil) && sonnet.diagnosis.reason == .noFastPrice)
        check("details: provider and a lookup key that differs", UnpricedDisplay.details(kimi, l10n).provider == l10n("history.unpricedModels.via", "custom")
              && UnpricedDisplay.details(kimi, l10n).lookup == l10n("history.unpricedModels.lookup", "kimi-k3-turbo"))
        check("a fact without a model", UnpricedDisplay.model(empty, l10n) == l10n("cost.noModelName") && empty.diagnosis.reason == .noModel)
        check("an unpriceable chip adds the table's rule", UnpricedDisplay.reasonHelp(neko.diagnosis, l10n).hasPrefix(l10n("cost.reason.unpriceable.hint"))
              && UnpricedDisplay.reasonHelp(neko.diagnosis, l10n).contains("Neko"))
        check("seen: one date when equal, a range otherwise",
              UnpricedDisplay.seen(sonnet, l10n) == l10n("history.unpricedModels.seen", HistoryText.date(DayKey("2026-10-21")!, l10n))
              && UnpricedDisplay.seen(neko, l10n).contains(" – "))
        check("the row reads its reason, provider, requests and days", UnpricedDisplay.accessibilityValue(neko, l10n).contains(l10n("cost.reason.unpriceable"))
              && UnpricedDisplay.accessibilityValue(neko, l10n).contains("neko-route"))

        var big = UnpricedGroup(source: .codex)
        big.rows = (0..<23).map { index in
            UnpricedRow(source: .codex, key: UnpricedFactKey(model: "m\(index)", provider: nil, fast: false),
                        diagnosis: UnpricedDiagnosis(reason: .notInTable, canonical: "m\(index)"), requests: 100 - index,
                        first: "2026-10-20", last: "2026-10-20")
        }
        check("more than 20 rows: the rest is counted", UnpricedDisplay.more(big, l10n) == l10n("history.unpricedModels.more", 3, 78 + 79 + 80)
              && UnpricedDisplay.more(all.groups[0], l10n) == nil)

        let live = [
            SourceStatus(kind: .codex, enabled: true, rootPath: "", available: true, sessionCount: 1, live: .zero,
                         today: TokenTotals(requests: 9), activeAgents: 0,
                         unpricedFacts: [fact("neko-model-7", "custom", 3), fact("codex-auto-review", "openai", 1)]),
            SourceStatus(kind: .claudeCode, enabled: true, rootPath: "", available: true, sessionCount: 1, live: .zero,
                         today: TokenTotals(requests: 4), activeAgents: 0, unpricedFacts: [fact("neko-model-7", nil, 3), fact("", nil, 1)]),
            SourceStatus(kind: .opencode, enabled: false, rootPath: "", available: true, sessionCount: 1, live: .zero,
                         today: TokenTotals(requests: 4), activeAgents: 0, unpricedFacts: [fact("zzz", nil, 4)]),
        ]
        let items = UnpricedDisplay.todayItems(UnpricedDisplay.today(live, day: "2026-10-26", table: table))
        check("today: one item per model and reason over enabled sources, most requests first", items.map(\.model) == ["neko-model-7", "", "codex-auto-review"]
              && items.first?.requests == 6)
        let line = UnpricedDisplay.todayLine(items, l10n) ?? ""
        check("today: items read model and reason", line.contains(l10n("prices.unknownToday.item", "neko-model-7", l10n("cost.reason.unpriceable")))
              && line.contains(l10n("cost.noModelName")) && UnpricedDisplay.todayLine([], l10n) == nil)
        check("today: each reason's hint once", UnpricedDisplay.todayHelp(items, l10n).components(separatedBy: l10n("cost.reason.unpriceable.hint")).count == 2)
        return results
    }
}
