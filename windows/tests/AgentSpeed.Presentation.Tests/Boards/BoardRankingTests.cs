using System.Text.Json;
using AgentSpeed.Presentation.Boards;
using AgentSpeed.Presentation.Tests.TestSupport;

namespace AgentSpeed.Presentation.Tests.Boards;

public class BoardRankingTests
{
    public static TheoryData<string> CaseNames()
    {
        var data = new TheoryData<string>();
        foreach (var element in Cases().EnumerateArray())
        {
            data.Add(element.GetProperty("name").GetString()!);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void MatchesTheSharedCase(string name)
    {
        var item = Cases().EnumerateArray().Single(c => c.GetProperty("name").GetString() == name);
        var ranking = new BoardRanking();
        var tick = 0;
        foreach (var expected in item.GetProperty("ticks").EnumerateArray())
        {
            tick++;
            var groups = expected.GetProperty("groups").EnumerateArray()
                .Select(g => new BoardRankInput(g.GetProperty("id").GetString()!, g.GetProperty("live").GetDouble(), g.GetProperty("lastActiveMs").GetInt64()))
                .ToArray();
            var result = ranking.Tick(groups);
            var order = expected.GetProperty("order").EnumerateArray().Select(e => e.GetString()!).ToArray();
            Assert.True(order.SequenceEqual(result.Order), $"{name}, tick {tick}: order {string.Join(",", result.Order)}");
            var events = expected.GetProperty("events").EnumerateArray()
                .Select(e => new BoardRankEvent(e.GetProperty("type").GetString() == "newLeader" ? BoardRankEventKind.NewLeader : BoardRankEventKind.Overtake,
                    e.GetProperty("id").GetString()!, e.GetProperty("from").GetInt32(), e.GetProperty("to").GetInt32()))
                .ToArray();
            Assert.Equal(events, result.Events);
        }
    }

    [Fact]
    public void TheFixtureHasEveryRuleOfTheSpec()
    {
        Assert.True(Cases().GetArrayLength() >= 10);
    }

    [Fact]
    public void DuplicateIdsCountOnce()
    {
        var ranking = new BoardRanking();
        var result = ranking.Tick([new BoardRankInput("a", 10, 0), new BoardRankInput("a", 99, 0), new BoardRankInput("b", 5, 0)]);
        Assert.Equal(["a", "b"], result.Order);
    }

    [Fact]
    public void ResetForgetsTheOrderAndThePressure()
    {
        var ranking = new BoardRanking();
        ranking.Tick([new BoardRankInput("a", 100, 0), new BoardRankInput("b", 90, 0)]);
        ranking.Tick([new BoardRankInput("a", 100, 0), new BoardRankInput("b", 130, 0)]);
        ranking.Reset();
        var result = ranking.Tick([new BoardRankInput("a", 100, 0), new BoardRankInput("b", 130, 0)]);
        Assert.Equal(["b", "a"], result.Order);
        Assert.Empty(result.Events);
    }

    [Fact]
    public void GainedCountsThePlaces()
    {
        Assert.Equal(2, new BoardRankEvent(BoardRankEventKind.Overtake, "x", 4, 2).Gained);
    }

    private static JsonElement Cases()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(RepositoryPaths.FixtureFile("board-ranking-cases.json")));
        return document.RootElement.GetProperty("cases").Clone();
    }
}
