using AgentSpeed.Core;
using AgentSpeed.Presentation.Boards;
using static AgentSpeed.Presentation.Tests.Boards.BoardGroupingTests;

namespace AgentSpeed.Presentation.Tests.Boards;

public class BoardLayoutPlanTests
{
    private static IReadOnlyList<BoardGroup> Groups(int count, int subs = 0, double subLive = 5, AgentPhase subPhase = AgentPhase.Generating)
    {
        var agents = new List<AgentStatus>();
        for (var g = 0; g < count; g++)
        {
            agents.Add(Agent("g" + g, 1000 - g * 50));
            for (var s = 0; s < subs; s++)
            {
                agents.Add(Agent($"g{g}s{s}", subLive - s * 0.1, "g" + g, subPhase));
            }
        }

        return BoardGrouping.Group(agents, SpeedMetric.Output);
    }

    // The planner's fixture: the board without metrics and with one-line group rows at every size (the S height, 2 more at L), so
    // the cases exercise the limits of DESIGN 2c Layout per size; the app's M and L boards add the second line.
    private static BoardMetrics OneLine(BoardStyle style, BoardSize size) =>
        BoardMetrics.For(style, size) with { GroupHeight = BoardMetrics.For(style, BoardSize.S).GroupHeight + (size == BoardSize.L ? 2 : 0) };

    private static IReadOnlyList<BoardEntry> Plan(BoardStyle style, BoardSize size, IReadOnlyList<BoardGroup> groups, string? expanded = null) =>
        BoardLayoutPlan.Arrange(groups, BoardSizes.Limits(size), OneLine(style, size), SpeedMetric.Output, expanded);

    [Theory]
    [InlineData(BoardStyle.Glass, BoardSize.S, 9, 3)]
    [InlineData(BoardStyle.Glass, BoardSize.S, 4, 4)]
    [InlineData(BoardStyle.Glass, BoardSize.M, 9, 5)]
    [InlineData(BoardStyle.Glass, BoardSize.M, 6, 5)]
    [InlineData(BoardStyle.Glass, BoardSize.M, 5, 5)]
    [InlineData(BoardStyle.Glass, BoardSize.L, 9, 7)]
    [InlineData(BoardStyle.Neon, BoardSize.M, 9, 5)]
    [InlineData(BoardStyle.Timing, BoardSize.S, 9, 4)]
    [InlineData(BoardStyle.Timing, BoardSize.M, 9, 6)]
    [InlineData(BoardStyle.Timing, BoardSize.M, 6, 6)]
    [InlineData(BoardStyle.Timing, BoardSize.L, 8, 8)]
    [InlineData(BoardStyle.Timing, BoardSize.L, 9, 7)]
    public void GroupsFitAsWholeRowsWithTheMoreLine(BoardStyle style, BoardSize size, int groups, int shown)
    {
        var plan = Plan(style, size, Groups(groups));

        Assert.Equal(shown, plan.Count(e => e.Kind == BoardEntryKind.Group));
        var rest = plan.SingleOrDefault(e => e.Kind == BoardEntryKind.MoreGroups);
        Assert.Equal(groups > shown, rest is not null);
        if (rest is not null)
        {
            Assert.Equal(groups - shown, rest.Rest);
        }
    }

    [Fact]
    public void SubRowsFillTheRoomThatRemainsInRankOrder()
    {
        var plan = Plan(BoardStyle.Glass, BoardSize.M, Groups(3, subs: 3));

        Assert.Equal(
            [
                (BoardEntryKind.Group, "g0", 6.0), (BoardEntryKind.Sub, "g0s0", 38.0), (BoardEntryKind.Sub, "g0s1", 60.0),
                (BoardEntryKind.MoreSubs, "g0#more", 82.0), (BoardEntryKind.Group, "g1", 102.0), (BoardEntryKind.Group, "g2", 134.0),
            ],
            plan.Select(e => (e.Kind, e.Key, e.Y)));
        var more = plan[3];
        Assert.Equal(1, more.Rest);
        Assert.Equal(4.8, more.RestLive, 6);
    }

    [Fact]
    public void ASmallBoardHasNoSubRows()
    {
        Assert.DoesNotContain(Plan(BoardStyle.Timing, BoardSize.S, Groups(2, subs: 4)), e => e.Kind is BoardEntryKind.Sub or BoardEntryKind.MoreSubs);
    }

    [Fact]
    public void OnlyGeneratingSubAgentsGetRows()
    {
        var plan = Plan(BoardStyle.Timing, BoardSize.M, Groups(1, subs: 3, subLive: 0, subPhase: AgentPhase.Idle));
        Assert.Equal([BoardEntryKind.Group], plan.Select(e => e.Kind));
    }

    [Fact]
    public void AnExpandedGroupShowsUpToEightSubAgentsAndTheOthersCollapse()
    {
        var groups = Groups(2, subs: 12, subPhase: AgentPhase.Idle, subLive: 0);
        var plan = Plan(BoardStyle.Timing, BoardSize.L, groups, expanded: "g1");

        Assert.Equal(8, plan.Count(e => e.Kind == BoardEntryKind.Sub && e.Group.Id == "g1"));
        Assert.DoesNotContain(plan, e => e.Kind == BoardEntryKind.Sub && e.Group.Id != "g1");
        Assert.Equal(4, plan.Single(e => e.Kind == BoardEntryKind.MoreSubs).Rest);
    }

    [Fact]
    public void AnExpandedGroupThatIsNotShownChangesNothing()
    {
        var groups = Groups(3, subs: 3);
        Assert.Equal(Plan(BoardStyle.Glass, BoardSize.M, groups).Select(e => e.Key), Plan(BoardStyle.Glass, BoardSize.M, groups, "nope").Select(e => e.Key));
    }

    [Fact]
    public void NoMoreLineWhenEverySubAgentIsShown()
    {
        var plan = Plan(BoardStyle.Glass, BoardSize.L, Groups(1, subs: 2));
        Assert.Equal([BoardEntryKind.Group, BoardEntryKind.Sub, BoardEntryKind.Sub], plan.Select(e => e.Kind));
    }

    [Fact]
    public void TheMoreGroupsLineSumsTheHiddenGroups()
    {
        var groups = Groups(7);
        var plan = Plan(BoardStyle.Glass, BoardSize.M, groups);
        var rest = plan.Single(e => e.Kind == BoardEntryKind.MoreGroups);
        Assert.Equal(groups.Skip(5).Sum(g => g.Live), rest.RestLive, 6);
    }

    [Fact]
    public void NoRowIsEverCutInHalf()
    {
        var random = new Random(7);
        foreach (var style in BoardStyles.All)
        {
            foreach (var size in BoardSizes.All)
            {
                var metrics = BoardMetrics.For(style, size);
                for (var trial = 0; trial < 200; trial++)
                {
                    var agents = new List<AgentStatus>();
                    var count = random.Next(0, 12);
                    for (var g = 0; g < count; g++)
                    {
                        agents.Add(Agent("g" + g, random.NextDouble() * 900));
                        var subs = random.Next(0, 15);
                        for (var s = 0; s < subs; s++)
                        {
                            agents.Add(Agent($"g{g}s{s}", random.NextDouble() < 0.3 ? 0 : random.NextDouble() * 200, "g" + g,
                                random.NextDouble() < 0.5 ? AgentPhase.Generating : AgentPhase.Idle));
                        }
                    }

                    var groups = BoardGrouping.Group(agents, SpeedMetric.Output);
                    var expanded = count > 0 && random.NextDouble() < 0.3 ? "g" + random.Next(count) : null;
                    var plan = BoardLayoutPlan.Arrange(groups, BoardSizes.Limits(size), metrics, SpeedMetric.Output, expanded);
                    var bottom = plan.Count == 0 ? metrics.StartY : plan[^1].Y + plan[^1].Height;
                    Assert.True(bottom <= metrics.StartY + metrics.Room + 1e-9, $"{style} {size} trial {trial}: {bottom}");
                    Assert.True(bottom <= metrics.ListHeight + 1e-9, $"{style} {size} trial {trial}: list {metrics.ListHeight}");
                    Assert.Equal(Math.Min(count, BoardSizes.Limits(size).Groups) > 0, plan.Any(e => e.Kind == BoardEntryKind.Group));
                }
            }
        }
    }
}
