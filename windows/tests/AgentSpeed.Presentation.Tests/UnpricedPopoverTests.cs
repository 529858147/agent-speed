using AgentSpeed.Core;
using AgentSpeed.Core.Pricing;
using AgentSpeed.Core.Unpriced;
using AgentSpeed.Presentation.Cost;
using AgentSpeed.Presentation.History;
using AgentSpeed.Presentation.Localization;
using static AgentSpeed.Presentation.Tests.TestSupport.HistoryData;

namespace AgentSpeed.Presentation.Tests;

/// <summary>The unknown-prices popover of the History window and Settings → Cost's list (DESIGN 9, METRICS 9.10).</summary>
public class UnpricedPopoverTests
{
    private static readonly DayRange Week = new(D(2026, 10, 15), Today);

    private static readonly PriceTable Table = PriceBook.Bundled;

    private static DailyUsage Day(string day, SourceKind source, long requests, params (string Model, string? Provider, bool Fast, long Requests)[] facts) =>
        Usage(day, source, requests: requests) with
        {
            Cost = new CostTally(1000, facts.Sum(f => f.Requests)),
            Unpriced = new UnpricedReading(0, 0, UnpricedFacts.Sorted(facts.Select(f =>
                KeyValuePair.Create(UnpricedFact.Of(f.Model, f.Provider, f.Fast), f.Requests)))),
        };

    private static readonly DailyUsage[] Days =
    [
        Day("2026-10-19", SourceKind.Codex, 40, ("neko-model-7", "neko-route", false, 25), ("", "openai", false, 2), ("mimo-v2.6-pro", "custom", false, 5)),
        Day("2026-10-20", SourceKind.Codex, 12, ("neko-model-7", "neko-route", false, 4)),
        Day("2026-10-20", SourceKind.ClaudeCode, 3, ("claude-sonnet-5-5", null, true, 1)),
        Day("2026-10-21", SourceKind.WorkBuddyAi, 5, ("Mystery-K9", "custom", false, 1)),
        Day("2026-10-21", SourceKind.Gemini, 9),
        Day("2026-10-01", SourceKind.Codex, 7, ("old-model", null, false, 7)),
    ];

    [Fact]
    public void ThePopoverGroupsRowsBySourceWithReasonsAndDetails()
    {
        var loc = For(LanguageCodes.En);
        var view = UnpricedPopover.ViewOf(Days, Week, null, default, Table);
        var popover = UnpricedPopover.Build(view, loc);

        Assert.Equal("38", popover.Count);
        Assert.Equal([SourceKind.Codex, SourceKind.ClaudeCode, SourceKind.WorkBuddyAi], popover.Groups.Select(g => g.Source));
        var codex = popover.Groups[0];
        Assert.Equal(("Codex", "31"), (codex.Name, codex.Requests));
        Assert.Equal(string.Empty, codex.More);

        var neko = codex.Rows[0];
        Assert.Equal(("neko-model-7", false, "29", "No public price"), (neko.Model, neko.IsUnnamed, neko.Requests, neko.Reason));
        Assert.Equal(["via neko-route"], neko.Details);
        Assert.StartsWith(loc["cost.reason.unpriceable.hint"], neko.ReasonHint, StringComparison.Ordinal);
        Assert.EndsWith("Random aliases of a local router (Neko Route); no model or price can be derived.", neko.ReasonHint, StringComparison.Ordinal);
        Assert.EndsWith("Seen: Oct 19 – Oct 20, 2026", neko.Tooltip, StringComparison.Ordinal);

        var unnamed = codex.Rows[1];
        Assert.Equal(("No model metadata", true, "Missing model"), (unnamed.Model, unnamed.IsUnnamed, unnamed.Reason));
        Assert.Equal(["via openai"], unnamed.Details);

        var fast = popover.Groups[1].Rows[0];
        Assert.Equal(("No Fast price", true), (fast.Reason, fast.Fast));
        Assert.Equal(["Fast"], fast.Details);
        Assert.Equal(loc["cost.reason.noFastPrice.hint"], fast.ReasonHint);

        var mystery = popover.Groups[2].Rows[0];
        Assert.Equal(["via custom", "looked up as mystery-k9"], mystery.Details);
        Assert.Equal("Not in price table · via custom · looked up as mystery-k9" + Environment.NewLine + "Seen: Oct 21, 2026", mystery.Tooltip);
        Assert.Contains("Requests", mystery.AccessibleValue, StringComparison.Ordinal);

        Assert.Equal(loc.Format("history.unpricedModels.pricedNow", 5), popover.PricedNow);
        Assert.Equal(view.Unpriced, view.Groups.Sum(g => g.RowRequests) + view.PricedNow);
    }

    [Fact]
    public void ThePopoverFollowsTheSourceAndTheExcludedSubscriptions()
    {
        var claudeOnly = UnpricedPopover.ViewOf(Days, Week, SourceKind.ClaudeCode, default, Table);
        Assert.Equal([SourceKind.ClaudeCode], claudeOnly.Groups.Select(g => g.Source));

        var withoutCodex = UnpricedPopover.ViewOf(Days, Week, null, SourceSet.Of(SourceKind.Codex), Table);
        Assert.DoesNotContain(withoutCodex.Groups, g => g.Source == SourceKind.Codex);
        Assert.Equal(2, withoutCodex.Unpriced);
        Assert.Equal(string.Empty, UnpricedPopover.Build(withoutCodex, For(LanguageCodes.En)).PricedNow);
    }

    [Fact]
    public void LongGroupsShowTwentyRowsAndCountTheRest()
    {
        var facts = Enumerable.Range(0, 23).Select(i => ($"mystery-{i:00}", (string?)null, false, 30L - i)).ToArray();
        var view = UnpricedPopover.ViewOf([Day("2026-10-20", SourceKind.Codex, 1000, facts)], Week, null, default, Table);
        var group = Assert.Single(UnpricedPopover.Build(view, For(LanguageCodes.En)).Groups);
        Assert.Equal(UnpricedPopover.MaxRows, group.Rows.Count);
        Assert.Equal("3 more · 27 requests", group.More);
    }

    [Fact]
    public void TheCopiedTextNamesTheWindowsRange()
    {
        var view = UnpricedPopover.ViewOf(Days, Week, null, SourceSet.Of(SourceKind.WorkBuddyAi), Table);
        var text = UnpricedPopover.CopyText(view, new UnpricedApp("1.4.1", "windows", "windows-11"), Table, HistoryRangeKind.Days7, Week, null,
            SourceSet.Of(SourceKind.WorkBuddyAi));
        var lines = text.Split('\n');
        Assert.Equal("app version=1.4.1 platform=windows os=windows-11", lines[1]);
        Assert.Equal("range view=7d first=2026-10-15 last=2026-10-21 days=7 source=all excluded=workbuddyAi", lines[3]);
        Assert.DoesNotContain('\r', text);
        Assert.EndsWith("\n", text, StringComparison.Ordinal);
        Assert.Equal(
            ["today", "yesterday", "7d", "30d", "90d", "year", "all", "custom"],
            Enum.GetValues<HistoryRangeKind>().Select(UnpricedPopover.ViewName));
    }

    [Fact]
    public void TodaysUnknownModelsAreListedOncePerModelAndReason()
    {
        SourceStatus Source(SourceKind kind, long requests, params (string Model, string? Provider, bool Fast, bool NoDate, long Requests)[] facts) =>
            new(kind, true, string.Empty, true, 0, default, new TokenTotals(requests), 0)
            {
                UnpricedFacts = UnpricedFacts.Sorted(facts.Select(f => KeyValuePair.Create(UnpricedFact.Of(f.Model, f.Provider, f.Fast, f.NoDate), f.Requests))),
            };

        SourceStatus[] sources =
        [
            Source(SourceKind.Codex, 20, ("neko-model-7", "a", false, false, 3), ("neko-model-7", "b", false, false, 2), ("", null, false, false, 1),
                ("mimo-v2.6-pro", "custom", false, false, 4)),
            Source(SourceKind.OpenCode, 5, ("neko-model-7", null, false, false, 1), ("gpt-5.5", null, false, true, 2)),
            Source(SourceKind.Gemini, 0),
        ];
        var live = UnpricedView.Build(UnpricedPart.Live(sources, "2026-10-21"), Table, "2026-10-21", "2026-10-21");

        var (text, tooltip) = CostPaneText.UnknownToday(live, For(LanguageCodes.En));
        Assert.Equal("Models without a price today: neko-model-7 (No public price), gpt-5.5 (Invalid time), No model metadata (Missing model)", text);
        Assert.StartsWith(text, tooltip, StringComparison.Ordinal);
        Assert.Contains(For(LanguageCodes.En)["cost.reason.noDate.hint"], tooltip, StringComparison.Ordinal);
        Assert.Single(tooltip.Split(For(LanguageCodes.En)["cost.reason.unpriceable.hint"]).Skip(1));

        var (zh, _) = CostPaneText.UnknownToday(live, For(LanguageCodes.ZhHant));
        Assert.Equal("今天價格未知的模型：neko-model-7（無公開價格）、gpt-5.5（時間無效）、無模型中繼資料（缺少模型）", zh);
        Assert.Equal((string.Empty, string.Empty), CostPaneText.UnknownToday(UnpricedView.Empty, For(LanguageCodes.En)));
    }
}
