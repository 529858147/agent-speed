using AgentSpeed.Core;
using AgentSpeed.Presentation.Boards;

namespace AgentSpeed.Presentation.Tests.Boards;

public class BoardGroupingTests
{
    internal static readonly DateTimeOffset Epoch = new(2026, 10, 24, 9, 0, 0, TimeSpan.Zero);

    internal static AgentStatus Agent(string id, double live, string? parent = null, AgentPhase phase = AgentPhase.Generating, int activeSeconds = 0,
        SourceKind source = SourceKind.ClaudeCode) =>
        new(id, source, "p-" + id, "model", phase, parent is not null, new Rate(live, live * 4), null, Epoch.AddSeconds(activeSeconds), parent);

    [Fact]
    public void GroupsARootWithAllItsDescendants()
    {
        var groups = BoardGrouping.Group([Agent("r", 10), Agent("c", 5, "r"), Agent("g", 3, "c"), Agent("x", 7)], SpeedMetric.Output);

        Assert.Equal(["r", "x"], groups.Select(g => g.Id));
        var r = groups[0];
        Assert.Equal(["c", "g"], r.Subs.Select(s => s.Id));
        Assert.Equal(18, r.Live);
        Assert.Equal(8, r.SubLive);
        Assert.Empty(groups[1].Subs);
    }

    [Fact]
    public void ASubAgentWhoseParentIsNotListedIsItsOwnRoot()
    {
        var groups = BoardGrouping.Group([Agent("orphan", 4, "gone"), Agent("child", 2, "orphan")], SpeedMetric.Output);

        var group = Assert.Single(groups);
        Assert.Equal("orphan", group.Id);
        Assert.Equal(6, group.Live);
    }

    [Fact]
    public void GroupSpeedUsesTheSelectedMetric()
    {
        var groups = BoardGrouping.Group([Agent("r", 10), Agent("c", 5, "r")], SpeedMetric.Total);
        Assert.Equal(60, groups[0].Live);
    }

    [Fact]
    public void SubsSortByLiveThenLatestActivityThenId()
    {
        var groups = BoardGrouping.Group(
            [Agent("r", 1), Agent("b", 5, "r", activeSeconds: 1), Agent("a", 5, "r", activeSeconds: 1), Agent("z", 5, "r", activeSeconds: 9), Agent("q", 9, "r")],
            SpeedMetric.Output);

        Assert.Equal(["q", "z", "a", "b"], groups[0].Subs.Select(s => s.Id));
    }

    [Fact]
    public void CountsPhasesAndLatestActivityOfEveryMember()
    {
        var groups = BoardGrouping.Group(
            [Agent("r", 0, phase: AgentPhase.Idle, activeSeconds: 2), Agent("c", 3, "r", AgentPhase.Generating, 30), Agent("t", 0, "r", AgentPhase.Tools, 5)],
            SpeedMetric.Output);

        var group = groups[0];
        Assert.Equal(1, group.Generating);
        Assert.Equal(1, group.Tools);
        Assert.Equal(Epoch.AddSeconds(30).ToUnixTimeMilliseconds(), group.LastActiveMs);
        Assert.Equal(AgentPhase.Generating, group.BadgePhase);
    }

    [Fact]
    public void NegativeOrNonFiniteRatesCountAsZero()
    {
        var groups = BoardGrouping.Group([Agent("r", double.NaN), Agent("c", -4, "r")], SpeedMetric.Output);
        Assert.Equal(0, groups[0].Live);
    }

    [Fact]
    public void DuplicateAgentsCountOnce()
    {
        var groups = BoardGrouping.Group([Agent("r", 2), Agent("r", 2)], SpeedMetric.Output);
        Assert.Equal(2, Assert.Single(groups).Live);
    }
}
