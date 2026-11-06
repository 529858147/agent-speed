using AgentSpeed.Presentation.Boards;
using AgentSpeed.Presentation.Boards.Demo;

namespace AgentSpeed.Presentation.Tests.Boards;

public class BoardSimTests
{
    // Totals of ticks 10–12 of spec/prototypes/board/sim.js (seed 7), printed by node from the prototype itself.
    [Theory]
    [InlineData(BoardSimScenario.Swarm, 11542.479511, 11377.576570, 11231.787845, 4)]
    [InlineData(BoardSimScenario.Team, 1031.083479, 1143.855715, 1229.148434, 5)]
    [InlineData(BoardSimScenario.Duel, 605.684166, 585.789461, 535.976633, 2)]
    [InlineData(BoardSimScenario.Calm, 0, 17.024605, 42.506230, 2)]
    public void ThePortTellsThePrototypesStory(BoardSimScenario scenario, double t10, double t11, double t12, int roots)
    {
        var sim = new BoardSim(scenario);
        var totals = new List<double>();
        for (var i = 0; i < 12; i++)
        {
            sim.Step();
            totals.Add(sim.Total);
        }

        Assert.Equal(t10, totals[9], 1e-5);
        Assert.Equal(t11, totals[10], 1e-5);
        Assert.Equal(t12, totals[11], 1e-5);
        Assert.Equal(roots, sim.Agents.Count(a => a.ParentId is null));
    }

    [Fact]
    public void ATargetScalesTheTotalAndQuietStopsEveryone()
    {
        var sim = new BoardSim(BoardSimScenario.Team) { TargetTotal = 3000 };
        sim.Step();
        Assert.Equal(3000, sim.Total, 6);

        sim.TargetTotal = null;
        sim.Quiet = true;
        for (var i = 0; i < 12; i++)
        {
            sim.Step();
        }

        Assert.Equal(0, sim.Total);
    }

    [Fact]
    public void TheDemoFeedRanksAndLaysOutTheStory()
    {
        var feed = new BoardDemoFeed(BoardSimScenario.Duel);
        feed.Sim.TargetTotal = 2400;
        var leaders = new HashSet<string>();
        for (var i = 0; i < 80; i++)
        {
            var tick = feed.Step(BoardStyle.Neon, BoardSize.M);
            Assert.NotEmpty(tick.Entries);
            leaders.Add(tick.Entries[0].Key);
        }

        Assert.Equal(2, leaders.Count);
        Assert.True(feed.Events >= 1);
    }
}
