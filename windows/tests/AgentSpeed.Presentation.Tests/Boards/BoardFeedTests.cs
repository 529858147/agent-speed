using AgentSpeed.Core;
using AgentSpeed.Presentation.Boards;
using static AgentSpeed.Presentation.Tests.Boards.BoardGroupingTests;

namespace AgentSpeed.Presentation.Tests.Boards;

public class BoardFeedTests
{
    private static SpeedSnapshot Snapshot(double seconds, params AgentStatus[] agents)
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
                new SourceStatus(SourceKind.Codex, true, "c", true, 1, new Rate(codex, codex * 4), new TokenTotals(Output: 500), 1),
                new SourceStatus(SourceKind.ClaudeCode, true, "a", true, 1, new Rate(claude, claude * 4), new TokenTotals(Output: 700), 1),
            ],
            Agents = agents,
            GeneratingCount = agents.Count(a => a.Phase == AgentPhase.Generating),
            DayStart = Epoch.AddHours(-9),
        };
    }

    [Fact]
    public void RanksGroupsAndReportsPassesOncePerEngineTick()
    {
        var feed = new BoardFeed("b");
        feed.Configure(null, SpeedMetric.Output, 2000, 0);

        var first = feed.Update(Snapshot(0, Agent("a", 300), Agent("b", 100)), 0);
        Assert.Equal(["a", "b"], first.Groups.Select(g => g.Id));
        Assert.Equal(400, first.Value);

        // A value-only update between engine ticks never moves a row.
        var between = feed.Update(Snapshot(0.1, Agent("a", 100), Agent("b", 700)), 0.1);
        Assert.Equal(["a", "b"], between.Groups.Select(g => g.Id));
        Assert.Empty(between.Events);

        var passed = feed.Update(Snapshot(0.5, Agent("a", 100), Agent("b", 700)), 0.5);
        Assert.Equal(["b", "a"], passed.Groups.Select(g => g.Id));
        Assert.Equal(new BoardRankEvent(BoardRankEventKind.NewLeader, "b", 2, 1), Assert.Single(passed.Events));

        var after = feed.Update(Snapshot(1.0, Agent("a", 100), Agent("b", 700)), 1.0);
        Assert.Empty(after.Events);
    }

    [Fact]
    public void ABoardCountsOnlyItsSources()
    {
        var feed = new BoardFeed("b");
        feed.Configure(SourceSet.Of(SourceKind.Codex), SpeedMetric.Output, 2000, 0);
        var data = feed.Update(Snapshot(0, Agent("a", 300), Agent("c", 50, source: SourceKind.Codex), Agent("c1", 20, "c", source: SourceKind.Codex)), 0);

        Assert.Equal(["c"], data.Groups.Select(g => g.Id));
        Assert.Equal(70, data.Value);
        Assert.Equal(500, data.TodayOutput);
        Assert.Equal(2, data.Generating);
    }

    [Fact]
    public void ChangingTheSourcesStartsTheRankingOver()
    {
        var feed = new BoardFeed("b");
        feed.Configure(null, SpeedMetric.Output, 2000, 0);
        feed.Update(Snapshot(0, Agent("a", 300), Agent("b", 280)), 0);
        feed.Configure(null, SpeedMetric.Total, 2_000_000, 0);
        var data = feed.Update(Snapshot(0.5, Agent("a", 300), Agent("b", 280)), 0.5);
        Assert.Empty(data.Events);
    }

    [Fact]
    public void KeepsTheLiveValuePerSecond()
    {
        var feed = new BoardFeed("b");
        feed.Configure(null, SpeedMetric.Output, 2000, 0);
        feed.Update(Snapshot(0, Agent("a", 10)), 0);
        feed.Update(Snapshot(1, Agent("a", 20)), 1);
        feed.Update(Snapshot(3, Agent("a", 40)), 3);

        Span<double> history = stackalloc double[5];
        feed.CopyHistory(history);
        Assert.Equal([0, 10, 20, 0, 40], history.ToArray());
    }

    [Fact]
    public void GroupsTheRankingHasNotSeenFollowByValue()
    {
        var groups = BoardGrouping.Group([Agent("x", 5), Agent("y", 50), Agent("z", 1)], SpeedMetric.Output);
        Assert.Equal(["z", "y", "x"], BoardFeed.Arrange(groups, ["z"]).Select(g => g.Id));
    }
}
