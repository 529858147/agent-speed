using AgentSpeed.Core;
using AgentSpeed.Presentation.Boards;
using AgentSpeed.Presentation.Boards.Gpu;
using AgentSpeed.Presentation.Boards.Scene;
using AgentSpeed.Presentation.Heat;
using AgentSpeed.Presentation.Orb;
using static AgentSpeed.Presentation.Tests.Boards.BoardGroupingTests;

namespace AgentSpeed.Presentation.Tests.Boards;

public class BoardSceneTests
{
    private sealed class AllLayers : IBoardLayers
    {
        public bool TryGet(string key, out BoardLayerSlot slot)
        {
            slot = new BoardLayerSlot(0, 0, 0.5f, 0.1f, key.EndsWith("#v", StringComparison.Ordinal) ? 60 : 200, 30);
            return true;
        }
    }

    private static BoardTick Tick(IReadOnlyList<(string Id, double Live)> groups, IReadOnlyList<BoardRankEvent>? events = null,
        bool generating = true, bool newPeak = false)
    {
        var agents = groups.Select(g => Agent(g.Id, g.Live, phase: g.Live > 0 ? AgentPhase.Generating : AgentPhase.Idle)).ToArray();
        var ranked = BoardGrouping.Group(agents, SpeedMetric.Output);
        var order = groups.Select(g => ranked.First(r => r.Id == g.Id)).ToArray();
        var entries = BoardLayoutPlan.Arrange(order, BoardSizes.Limits(BoardSize.M), BoardMetrics.For(BoardStyle.Glass, BoardSize.M),
            SpeedMetric.Output);
        var total = groups.Sum(g => g.Live);
        return new BoardTick(total, SpeedFormat.Speed(total), HeatMath.RawTier(total, 2000), 2000, SpeedMetric.Output, generating, entries,
            events ?? [], newPeak);
    }

    private static BoardScene Scene() => new(BoardStyle.Glass, BoardSize.M, 3);

    private static void Frames(BoardScene scene, BoardDrawList list, double from, double seconds)
    {
        for (var t = from; t <= from + seconds; t += 1 / 60.0)
        {
            list.Reset();
            scene.Render(t, new AllLayers(), list);
        }
    }

    [Fact]
    public void TheFirstTickIsSettledAndAQuietBoardIsStatic()
    {
        var scene = Scene();
        scene.Apply(Tick([("a", 0), ("b", 0)], generating: false), 0);
        scene.Settle();
        var list = new BoardDrawList();
        Frames(scene, list, 0, 0.1);
        Assert.Equal(FrameClass.None, scene.Demand(0.1));
        // Two rows (body, phase badge, value layer and their value's dash glyph), the backdrop, the hero with its history and unit,
        // the number's one glyph and the footer.
        Assert.Equal(2 * 4 + 1 + 3 + 1 + 1, list.LayerCount);
    }

    [Fact]
    public void TheBloomFadesOutBeforeTheWindowsEdge()
    {
        var scene = Scene();
        scene.Apply(Tick([("a", 1900)]), 0);
        scene.Settle();
        var list = new BoardDrawList();
        Frames(scene, list, 0, 0.1);
        Assert.Contains(list.Batches.ToArray(), b => b.Clip == BoardClip.Outside);
        Assert.Equal(BoardCanvas.Margin - 2, list.OutsideReach);
    }

    [Fact]
    public void APassMovesRowsAtSixtyUntilTheySettle()
    {
        var scene = Scene();
        var list = new BoardDrawList();
        scene.Apply(Tick([("a", 100), ("b", 50)]), 0);
        scene.Settle();
        Frames(scene, list, 0, 0.5);
        scene.Apply(Tick([("b", 300), ("a", 100)], [new BoardRankEvent(BoardRankEventKind.NewLeader, "b", 2, 1)]), 0.5);
        Assert.Equal(FrameClass.Fps60, scene.Demand(0.5));
        Assert.True(scene.Effects.Particles.Count > 0);
        Frames(scene, list, 0.5, 3);
        var b = scene.Rows.First(r => r.Key == "b");
        Assert.Equal(b.Target, b.Y, 1);
        Assert.Equal(0, b.Target - b.Entry.Y, 6);
        Assert.Equal(FrameClass.Fps30, scene.Demand(3.6));
    }

    [Fact]
    public void AFlashingValueAsksForThirtyFramesUntilItEnds()
    {
        var scene = Scene();
        var list = new BoardDrawList();
        scene.Apply(Tick([("a", 0.2), ("b", 0.1)], generating: false), 0);
        scene.Settle();
        Frames(scene, list, 0, 0.5);
        Assert.Equal(FrameClass.None, scene.Demand(0.5));
        scene.Apply(Tick([("a", 0.2), ("b", 0.15)], generating: false), 1);
        Frames(scene, list, 1, 0.4);
        Assert.Equal(FrameClass.None, scene.Effects.Demand);
        Assert.Equal(FrameClass.Fps30, scene.Demand(1.4));
        Frames(scene, list, 1.4, 0.6);
        Assert.Equal(FrameClass.None, scene.Demand(2.0));
    }

    [Fact]
    public void RowsThatLeaveFadeOutAndAreDropped()
    {
        var scene = Scene();
        var list = new BoardDrawList();
        scene.Apply(Tick([("a", 100), ("b", 50)]), 0);
        scene.Settle();
        scene.Apply(Tick([("a", 100)]), 1);
        Assert.Equal(2, scene.Rows.Count);
        Frames(scene, list, 1, 0.5);
        Assert.Single(scene.Rows);
    }

    [Fact]
    public void RowsThatLeftAreDroppedWithoutFrames()
    {
        var scene = Scene();
        for (var i = 0; i < 40; i++)
        {
            scene.Apply(Tick([("a" + i, 100), ("b", 50)]), i * 0.5);
        }

        Assert.Equal(3, scene.Rows.Count);
    }

    [Fact]
    public void ARiseOfMoreThanEightPercentPunchesTheNumber()
    {
        var scene = Scene();
        var list = new BoardDrawList();
        scene.Apply(Tick([("a", 300)]), 0);
        scene.Settle();
        Frames(scene, list, 0, 0.2);
        Assert.Equal(0, scene.Effects.Particles.Count);
        scene.Apply(Tick([("a", 400)]), 0.5);
        Assert.InRange(scene.Effects.Particles.Count, 6, 22);
        Assert.Equal(FrameClass.Fps60, scene.Demand(0.6));
    }

    [Fact]
    public void HitRowFindsTheGroupUnderThePoint()
    {
        var scene = Scene();
        scene.Apply(Tick([("a", 100), ("b", 50)]), 0);
        var metrics = scene.Metrics;
        var b = scene.Rows.First(r => r.Key == "b");
        var y = BoardCanvas.Margin + metrics.ListTop + b.Y + b.Entry.Height / 2;
        Assert.Equal("b", scene.HitRow(BoardCanvas.Margin + metrics.ListLeft + 40, y)?.Key);
        Assert.Null(scene.HitRow(5, y));
    }

    [Fact]
    public void AWarmFrameDoesNotAllocate()
    {
        var scene = new BoardScene(BoardStyle.Neon, BoardSize.L, 5);
        var list = new BoardDrawList();
        var layers = new AllLayers();
        scene.Apply(Tick([("a", 2000), ("b", 900), ("c", 400)]), 0);
        scene.Apply(Tick([("b", 2600), ("a", 2000), ("c", 400)], [new BoardRankEvent(BoardRankEventKind.NewLeader, "b", 2, 1)]), 0.5);
        var t = 0.5;
        for (var i = 0; i < 60; i++)
        {
            t += 1 / 60.0;
            list.Reset();
            scene.Render(t, layers, list);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 30; i++)
        {
            t += 1 / 60.0;
            list.Reset();
            scene.Render(t, layers, list);
            _ = scene.Demand(t);
        }

        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
    }

    [Fact]
    public void RowsDrawTheirShapesThenTheirLayersInFewBatches()
    {
        var scene = Scene();
        var list = new BoardDrawList();
        scene.Apply(Tick([("a", 900), ("b", 600), ("c", 400), ("d", 200), ("e", 100)]), 0);
        scene.Settle();
        Frames(scene, list, 0, 0.2);

        // Five generating rows with underlines and comets: shapes and layers do not alternate row by row, nor blends comet by comet.
        var listBatches = 0;
        foreach (var batch in list.Batches)
        {
            listBatches += batch.Clip == BoardClip.Inside ? 1 : 0;
        }

        Assert.True(list.BatchCount <= 14, string.Join("; ", list.Batches.ToArray().Select(b => $"{b.Pass} {b.Blend} {b.Clip} {b.Count}")));
        Assert.True(listBatches <= 8, $"{listBatches} batches inside the square");
    }

    [Fact]
    public void TimingRowsCarryTheirIntervalAndOthersNone()
    {
        var timing = new BoardScene(BoardStyle.Timing, BoardSize.M, 3);
        timing.Apply(Tick([("a", 300), ("b", 271)]), 0);
        Assert.Equal(string.Empty, timing.Rows.First(r => r.Key == "a").IntervalText);
        Assert.Equal("−29", timing.Rows.First(r => r.Key == "b").IntervalText);
        Assert.Equal("271", timing.Rows.First(r => r.Key == "b").ValueText);

        var glass = Scene();
        glass.Apply(Tick([("a", 300), ("b", 271)]), 0);
        Assert.All(glass.Rows, r => Assert.Equal(string.Empty, r.IntervalText));
    }
}
