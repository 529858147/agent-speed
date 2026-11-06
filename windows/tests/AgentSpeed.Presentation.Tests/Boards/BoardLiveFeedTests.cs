using AgentSpeed.Core;
using AgentSpeed.Presentation.Boards;
using AgentSpeed.Presentation.Boards.Live;
using AgentSpeed.Presentation.Boards.Scene;
using AgentSpeed.Presentation.Cost;
using AgentSpeed.Presentation.Heat;
using AgentSpeed.Presentation.Localization;
using AgentSpeed.Presentation.Settings.Boards;
using AgentSpeed.Presentation.Settings.Orbs;
using AgentSpeed.Presentation.Titles;
using static AgentSpeed.Presentation.Tests.Boards.BoardGroupingTests;

namespace AgentSpeed.Presentation.Tests.Boards;

public class BoardLiveFeedTests
{
    private static readonly Localizer En = new(Localizer.LoadEmbeddedTable(), LanguageCodes.En);

    private static BoardTexts Texts(CostDisplay? cost = null) => new(En, TitleTheme.Vehicles, cost);

    internal static SpeedSnapshot Snapshot(double seconds, params AgentStatus[] agents)
    {
        var codex = agents.Where(a => a.Source == SourceKind.Codex).Sum(a => a.Live.Output);
        var claude = agents.Where(a => a.Source == SourceKind.ClaudeCode).Sum(a => a.Live.Output);
        return SpeedSnapshot.Empty with
        {
            Timestamp = Epoch.AddSeconds(seconds),
            State = MonitorState.Running,
            Live = new Rate(codex + claude, (codex + claude) * 4),
            Sources =
            [
                new SourceStatus(SourceKind.Codex, true, "c", true, 1, new Rate(codex, codex * 4), new TokenTotals(Requests: 4, Output: 1_500), 1)
                {
                    TodayCost = new CostTally(2_000_000),
                },
                new SourceStatus(SourceKind.ClaudeCode, true, "a", true, 1, new Rate(claude, claude * 4), new TokenTotals(Requests: 2, Output: 2_700_000), 1)
                {
                    TodayCost = new CostTally(10_340_000),
                },
            ],
            Today = new TokenTotals(Requests: 6, Output: 2_701_500),
            SubsetPeaks = new Dictionary<SourceSet, double> { [SourceSet.Of(SourceKind.Codex)] = 10 },
            Agents = agents,
            GeneratingCount = agents.Count(a => a.Phase == AgentPhase.Generating),
            DayStart = Epoch.AddHours(-9),
        };
    }

    private static BoardLiveFeed Feed(BoardDefinition? board = null)
    {
        var feed = new BoardLiveFeed(board ?? new BoardDefinition("b"));
        feed.Configure(feed.Board, SpeedMetric.Output, 2000, 0);
        return feed;
    }

    [Fact]
    public void TheTickCarriesTheBoardsValueTitleCountsAndFooter()
    {
        var feed = Feed();
        var tick = feed.Update(Snapshot(0, Agent("a", 300), Agent("b", 100, phase: AgentPhase.Tools), Agent("a1", 20, "a")), 0, Texts(new CostDisplay(En)));

        Assert.Equal(420, tick.Value);
        Assert.Equal("420", tick.ValueText);
        Assert.Equal(TitleTheme.Vehicles.Label(feed.Data.Title, En), tick.Title);
        Assert.Equal(2, tick.GeneratingCount);
        Assert.Equal(1, tick.ToolsCount);
        Assert.True(tick.Generating);
        Assert.Equal(2_701_500, tick.TodayOutput);
        Assert.Equal(En.Format("boards.today", "2.70M"), tick.TodayText);
        Assert.Equal("$12.34", tick.CostText);
        Assert.Equal(En.Format("boards.peak", SpeedFormat.Speed(tick.PeakToday)), tick.PeakText);
        Assert.Equal(BoardTick.HistorySeconds, tick.History.Count);
        Assert.Equal(420, tick.History[^1]);
        Assert.Equal(["a", "a1", "b"], tick.Entries.Select(e => e.Key));
    }

    [Fact]
    public void ABoardCountsOnlyItsSourcesIncludingItsCost()
    {
        var board = new BoardDefinition("b") { Sources = OrbSources.Of(SourceKind.Codex) };
        var feed = Feed(board);
        var tick = feed.Update(Snapshot(0, Agent("a", 300), Agent("c", 50, source: SourceKind.Codex), Agent("c1", 20, "c", source: SourceKind.Codex)), 0,
            Texts(new CostDisplay(En)));

        Assert.Equal(70, tick.Value);
        Assert.Equal(["c", "c1"], tick.Entries.Select(e => e.Key));
        Assert.Equal(1_500, tick.TodayOutput);
        Assert.Equal("$2.00", tick.CostText);
    }

    [Fact]
    public void WithoutACostDisplayTheFooterHasNoCost()
    {
        var tick = Feed().Update(Snapshot(0, Agent("a", 30)), 0, Texts());
        Assert.Null(tick.CostText);
    }

    [Fact]
    public void PassesAreReportedOnceAndNotRepeatedByARelayout()
    {
        var feed = Feed();
        feed.Update(Snapshot(0, Agent("a", 300), Agent("b", 100)), 0, Texts());
        var passed = feed.Update(Snapshot(0.5, Agent("a", 100), Agent("b", 700)), 0.5, Texts());

        Assert.Equal(new BoardRankEvent(BoardRankEventKind.NewLeader, "b", 2, 1), Assert.Single(passed.Events));
        Assert.Empty(feed.Last!.Events);
        var again = feed.Relayout(Texts());
        Assert.NotNull(again);
        Assert.Empty(again.Events);
        Assert.False(again.NewPeak);
        Assert.Equal(["b", "a"], again.Entries.Where(e => e.Kind == BoardEntryKind.Group).Select(e => e.Key));
    }

    [Fact]
    public void ARelayoutBeforeAnyDataIsNothing() => Assert.Null(Feed().Relayout(Texts()));

    [Fact]
    public void TheExpandedGroupShowsItsSubAgentsAndUnknownGroupsAreIgnored()
    {
        // The boards without metrics: S has room for both sub rows.
        var board = new BoardDefinition("b") { Size = BoardSize.S, Readouts = BoardReadouts.None };
        var feed = Feed(board);
        var snapshot = Snapshot(0, Agent("a", 300), Agent("a1", 20, "a"), Agent("a2", 10, "a"), Agent("b", 100));

        var collapsed = feed.Update(snapshot, 0, Texts());
        Assert.DoesNotContain(collapsed.Entries, e => e.Kind == BoardEntryKind.Sub);
        Assert.Null(collapsed.Expanded);

        var open = feed.Relayout(Texts(), "a")!;
        Assert.Equal("a", open.Expanded);
        Assert.Equal(["a1", "a2"], open.Entries.Where(e => e.Kind == BoardEntryKind.Sub).Select(e => e.Key));

        var gone = feed.Relayout(Texts(), "zzz")!;
        Assert.Null(gone.Expanded);
    }

    [Fact]
    public void TheLayoutFollowsTheBoardsSize()
    {
        var agents = Enumerable.Range(0, 10).Select(i => Agent("g" + i, 100 - i)).ToArray();
        var small = Feed(new BoardDefinition("s") { Size = BoardSize.S }).Update(Snapshot(0, agents), 0, Texts());
        var large = Feed(new BoardDefinition("l") { Size = BoardSize.L }).Update(Snapshot(0, agents), 0, Texts());

        Assert.True(small.Entries.Count(e => e.Kind == BoardEntryKind.Group) <= 4);
        Assert.True(large.Entries.Count(e => e.Kind == BoardEntryKind.Group) > 4);
        Assert.Contains(small.Entries, e => e.Kind == BoardEntryKind.MoreGroups);
    }

    [Fact]
    public void TheEnginesNewPeakCelebratesOnlyAnAllSourcesBoard()
    {
        var all = Feed();
        all.Update(Snapshot(0, Agent("a", 300)), 0, Texts());
        var peak = new SpeedEvent.NewPeak(300);
        var celebrated = all.EnginePeak(peak);
        Assert.NotNull(celebrated);
        Assert.True(celebrated.NewPeak);
        Assert.Empty(celebrated.Events);
        Assert.False(all.Last!.NewPeak);

        var codex = Feed(new BoardDefinition("c") { Sources = OrbSources.Of(SourceKind.Codex) });
        codex.Update(Snapshot(0, Agent("c", 30, source: SourceKind.Codex)), 0, Texts());
        Assert.Null(codex.EnginePeak(peak));
        Assert.Null(Feed().EnginePeak(peak));
    }

    [Fact]
    public void ASubsetBoardCelebratesItsOwnNewPeakOnce()
    {
        var board = new BoardDefinition("c") { Sources = OrbSources.Of(SourceKind.Codex) };
        var feed = Feed(board);
        Assert.False(feed.Update(Snapshot(1, Agent("c", 10, source: SourceKind.Codex)), 1, Texts()).NewPeak);

        var peak = feed.Update(Snapshot(2, Agent("c", 80, source: SourceKind.Codex)), 2, Texts());
        Assert.True(peak.NewPeak);
        Assert.False(feed.Last!.NewPeak);
        Assert.Equal(En.Format("boards.peak", "80"), peak.PeakText);
    }

    [Fact]
    public void TheTitleUsesTheGivenThemeAndLanguage()
    {
        var zh = new Localizer(Localizer.LoadEmbeddedTable(), LanguageCodes.ZhHant);
        var feed = Feed();
        var tick = feed.Update(Snapshot(0, Agent("a", 1900)), 0, new BoardTexts(zh, TitleTheme.Vehicles));
        Assert.Equal(TitleTheme.Vehicles.Label(feed.Data.Title, zh), tick.Title);
        Assert.Equal(zh.Format("boards.today", "2.70M"), tick.TodayText);
    }
}
