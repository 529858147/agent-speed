using AgentSpeed.Core;
using AgentSpeed.Presentation.Boards;
using AgentSpeed.Presentation.Boards.Live;
using AgentSpeed.Presentation.Localization;
using AgentSpeed.Presentation.Settings.Boards;
using AgentSpeed.Presentation.Settings.Orbs;
using AgentSpeed.Presentation.Titles;
using static AgentSpeed.Presentation.Tests.Boards.BoardGroupingTests;
using static AgentSpeed.Presentation.Tests.Boards.BoardLiveFeedTests;

namespace AgentSpeed.Presentation.Tests.Boards;

public class BoardInteractionTests
{
    private static readonly Localizer En = new(Localizer.LoadEmbeddedTable(), LanguageCodes.En);
    private static readonly Localizer Zh = new(Localizer.LoadEmbeddedTable(), LanguageCodes.ZhHant);

    // ---- Expansion ----

    [Fact]
    public void AClickTogglesOneGroupAndTheOthersCollapse()
    {
        var expansion = new BoardExpansion();
        Assert.Equal("a", expansion.Toggle("a", 0));
        Assert.Equal("b", expansion.Toggle("b", 1));
        Assert.Null(expansion.Toggle("b", 2));
        Assert.Null(expansion.Current(2));
        Assert.Null(expansion.Deadline);
    }

    [Fact]
    public void TheExpansionEndsAfterSixtySecondsWithoutInteraction()
    {
        var expansion = new BoardExpansion();
        expansion.Toggle("a", 0);
        expansion.Touch(30);
        Assert.Equal(90, expansion.Deadline);
        Assert.Equal("a", expansion.Current(89.9));
        Assert.False(expansion.Expire(89.9));
        Assert.True(expansion.Expire(90));
        Assert.Null(expansion.Current(90));
        Assert.False(expansion.Expire(91));
    }

    [Fact]
    public void TouchingWithoutAnExpansionStartsNothing()
    {
        var expansion = new BoardExpansion();
        expansion.Touch(5);
        Assert.Null(expansion.Deadline);
    }

    // ---- Announcements ----

    [Fact]
    public void ANewLeaderIsAnnouncedAtMostOncePerTenSeconds()
    {
        var feed = new BoardLiveFeed(new BoardDefinition("b"));
        feed.Configure(feed.Board, SpeedMetric.Output, 2000, 0);
        var texts = new BoardTexts(En, TitleTheme.Vehicles);
        var announcer = new BoardAnnouncer();
        feed.Update(Snapshot(0, Agent("a", 300), Agent("b", 100)), 0, texts);

        var first = feed.Update(Snapshot(0.5, Agent("a", 100), Agent("b", 700)), 0.5, texts);
        Assert.Equal(En.Format("a11y.board.newLeader", "p-b"), announcer.NewLeader(first, 0.5, En));

        var back = feed.Update(Snapshot(1.0, Agent("a", 900), Agent("b", 100)), 1.0, texts);
        Assert.Contains(back.Events, e => e.Kind == BoardRankEventKind.NewLeader);
        Assert.Null(announcer.NewLeader(back, 1.0, En));

        var later = feed.Update(Snapshot(11, Agent("a", 100), Agent("b", 900)), 11, texts);
        Assert.NotNull(announcer.NewLeader(later, 11, En));
    }

    [Fact]
    public void OvertakesBelowTheLeadAreNotAnnounced()
    {
        var feed = new BoardLiveFeed(new BoardDefinition("b"));
        feed.Configure(feed.Board, SpeedMetric.Output, 2000, 0);
        var texts = new BoardTexts(En, TitleTheme.Vehicles);
        feed.Update(Snapshot(0, Agent("a", 900), Agent("b", 300), Agent("c", 100)), 0, texts);
        var overtake = feed.Update(Snapshot(0.5, Agent("a", 900), Agent("b", 100), Agent("c", 500)), 0.5, texts);

        Assert.Contains(overtake.Events, e => e.Kind == BoardRankEventKind.Overtake);
        Assert.Null(new BoardAnnouncer().NewLeader(overtake, 0.5, En));
    }

    // ---- Accessibility ----

    [Fact]
    public void TheBoardIsALabelledListOfItsGroups()
    {
        var board = new BoardDefinition("b") { Sources = OrbSources.Of(SourceKind.Codex) };
        Assert.Equal(En.Format("a11y.board", OrbNaming.SourceName(SourceKind.Codex, En), "7.88k", "Cruising"),
            BoardAccessibility.Board(board, "7.88k", "Cruising", En));

        var feed = new BoardLiveFeed(new BoardDefinition("b"));
        feed.Configure(feed.Board, SpeedMetric.Output, 2000, 0);
        var tick = feed.Update(Snapshot(0, Agent("a", 300), Agent("a1", 20, "a"), Agent("b", 100)), 0, new BoardTexts(En, TitleTheme.Vehicles));
        Assert.Equal(["a", "b"], BoardAccessibility.Items(tick).Select(e => e.Key));
        Assert.Empty(BoardAccessibility.Items(null));
    }

    [Fact]
    public void ARowReadsRankProjectSpeedEffortAndSubAgents()
    {
        var root = Agent("r", 7000) with
        {
            Modes = ExecutionModes.Of(ExecutionModeNative.Effort("xhigh", ExecutionModeEvidence.Recorded, ExecutionModeScope.Turn)),
        };
        var group = BoardGrouping.Group([root, .. Enumerable.Range(0, 100).Select(i => Agent("s" + i, 8.8, "r"))], SpeedMetric.Output)[0];
        var entry = new BoardEntry(BoardEntryKind.Group, "r", group, root, 1, 0, 40);

        var label = BoardAccessibility.Row(entry, Zh);
        Assert.StartsWith(Zh.Format("a11y.board.row", 1, "p-r", "7.88k"), label);
        Assert.Contains("xhigh", label);
        Assert.EndsWith(Zh.Format("boards.subCount", 100), label);
        Assert.Contains("，", label);

        var plain = BoardGrouping.Group([Agent("x", 50)], SpeedMetric.Output)[0];
        Assert.Equal(En.Format("a11y.board.row", 2, "p-x", "50"), BoardAccessibility.Row(new BoardEntry(BoardEntryKind.Group, "x", plain, plain.Root, 2, 0, 40), En));
    }

    // ---- Tooltip ----

    [Fact]
    public void TheTooltipNamesTheGroupAndItsSpeed()
    {
        var group = BoardGrouping.Group([Agent("r", 300), Agent("c", 40, "r")], SpeedMetric.Output)[0];
        var lines = BoardTooltip.Lines(group, En);

        Assert.Equal("p-r · " + OrbNaming.SourceName(SourceKind.ClaudeCode, En), lines[0]);
        Assert.Equal("model", lines[1]);
        Assert.Contains(En.Format("effort.tooltip", En["modes.unrecorded"]), lines);
        Assert.Equal(En.Format("win.boards.tip.speedSubs", "340", "40"), lines[^1]);

        var solo = BoardTooltip.Lines(BoardGrouping.Group([Agent("x", 12)], SpeedMetric.Output)[0], En);
        Assert.Equal(En.Format("win.boards.tip.speed", "12"), solo[^1]);
    }

    [Fact]
    public void TheTooltipStringsExistInEveryLanguage()
    {
        foreach (var language in LanguageCodes.All)
        {
            var loc = new Localizer(Localizer.LoadEmbeddedTable(), language);
            Assert.NotEqual("win.boards.tip.speed", loc["win.boards.tip.speed"]);
            Assert.NotEqual("win.boards.tip.speedSubs", loc["win.boards.tip.speedSubs"]);
        }
    }
}
