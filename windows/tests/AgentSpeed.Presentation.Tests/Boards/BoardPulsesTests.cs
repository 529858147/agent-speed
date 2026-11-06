using AgentSpeed.Core;
using AgentSpeed.Presentation.Boards;
using AgentSpeed.Presentation.Boards.Gpu;
using AgentSpeed.Presentation.Boards.Scene;
using static AgentSpeed.Presentation.Tests.Boards.BoardGroupingTests;

namespace AgentSpeed.Presentation.Tests.Boards;

public class BoardPulsesTests
{
    private static readonly BoardLayerSlot Slot = new(0, 0, 0.1f, 0.1f, 20, 20);

    private static BoardEntry Group(AgentPhase phase, SourceKind source = SourceKind.ClaudeCode)
    {
        var groups = BoardGrouping.Group([Agent("a", phase == AgentPhase.Generating ? 50 : 0, phase: phase, source: source)], SpeedMetric.Output);
        return BoardLayoutPlan.Arrange(groups, BoardSizes.Limits(BoardSize.M), BoardMetrics.For(BoardStyle.Glass, BoardSize.M), SpeedMetric.Output)[0];
    }

    [Fact]
    public void BadgeLayersAreSharedPerSourceAndPhase()
    {
        var claude = BoardPulses.BadgeKey(Group(AgentPhase.Generating));
        Assert.NotNull(claude);
        Assert.Equal(claude, BoardPulses.BadgeKey(Group(AgentPhase.Generating)));
        Assert.NotEqual(claude, BoardPulses.BadgeKey(Group(AgentPhase.Generating, SourceKind.Codex)));
        Assert.NotEqual(claude, BoardPulses.BadgeKey(Group(AgentPhase.Tools)));
        Assert.NotEqual(BoardPulses.BadgeKey(Group(AgentPhase.Tools)), BoardPulses.BadgeKey(Group(AgentPhase.Idle)));
        var keys = Enum.GetValues<SourceKind>().SelectMany(s => Enum.GetValues<AgentPhase>().Select(p => BoardPulses.BadgeKey(Group(p, s))));
        Assert.Equal(Enum.GetValues<SourceKind>().Length * 3, keys.Distinct().Count());
    }

    [Fact]
    public void AToolsBadgeStandsStill()
    {
        var list = new BoardDrawList();
        var seen = new HashSet<(float, float)>();
        for (var t = 0.0; t < 1.2; t += 0.1)
        {
            list.Reset();
            BoardPulses.EmitBadge(list, Slot, BoardStyle.Glass, false, AgentPhase.Tools, 10, 100, 32, 1, t, 0, true, default);
            Assert.Equal(1, list.LayerCount);
            seen.Add((list.Layers[0].Rect.Y, list.Layers[0].Params.X));
        }

        Assert.Single(seen);
    }

    [Theory]
    [InlineData(BoardStyle.Glass, 3)]
    [InlineData(BoardStyle.Timing, 1)]
    [InlineData(BoardStyle.Neon, 1)]
    public void ABadgePulsesWhileTheBoardAnimatesAndRestsOtherwise(BoardStyle style, int parts)
    {
        var list = new BoardDrawList();
        var seen = new HashSet<(float, float, float)>();
        for (var t = 0.0; t < 1.2; t += 0.1)
        {
            list.Reset();
            BoardPulses.EmitBadge(list, Slot, style, false, AgentPhase.Generating, 10, 100, 32, 1, t, 0, true, default);
            Assert.Equal(parts, list.LayerCount);
            seen.Add((list.Layers[0].Rect.Y, list.Layers[0].Rect.Z, list.Layers[0].Params.X));
        }

        Assert.True(seen.Count > 4, $"{style} badge varies over {seen.Count} frames");

        var rest = new HashSet<(float, float, float)>();
        for (var t = 0.0; t < 1.2; t += 0.1)
        {
            list.Reset();
            BoardPulses.EmitBadge(list, Slot, style, false, AgentPhase.Generating, 10, 100, 32, 1, t, 0, false, default);
            rest.Add((list.Layers[0].Rect.Y, list.Layers[0].Rect.Z, list.Layers[0].Params.X));
        }

        Assert.Single(rest);
    }

    [Fact]
    public void GlassDotsBounceOneAfterAnother()
    {
        var list = new BoardDrawList();
        BoardPulses.EmitBadge(list, Slot, BoardStyle.Glass, false, AgentPhase.Generating, 0, 0, 32, 1, 0.44, 0, true, default);
        var tops = list.Layers.ToArray().Select(l => l.Rect.Y).ToArray();
        Assert.True(tops[0] < tops[1] && tops[1] < tops[2], string.Join(", ", tops));
        Assert.Equal(BoardPulses.DotSpacing, list.Layers[1].Rect.X - list.Layers[0].Rect.X, 4);
    }

    [Theory]
    [InlineData(BoardStyle.Glass)]
    [InlineData(BoardStyle.Neon)]
    public void ATopRungMeterShimmersEarlyInItsCycleInsideTheMeter(BoardStyle style)
    {
        var body = new BoardLayerSlot(0.1f, 0.2f, 0.6f, 0.3f, 250, 32, MeterX: 160);
        var (seconds, peak) = BoardPulses.Shimmer(style);
        var list = new BoardDrawList { Scale = 2 };
        var shown = 0;
        for (var t = 0.0; t < seconds; t += seconds / 40)
        {
            list.Reset();
            list.Scale = 2;
            BoardPulses.EmitShimmer(list, body, style, 20, 100, 32, 1, t, 0, default);
            if (list.LayerCount == 0)
            {
                continue;
            }

            shown++;
            var layer = list.Layers[0];
            Assert.Equal(-peak, layer.Params.W, 4);
            Assert.True(layer.Clip.X >= 20 + 160 - 3 - 0.5 && layer.Clip.Z <= 20 + 160 + 18.5 + 3 + 0.5, $"{layer.Clip}");
            Assert.True(layer.Uv.X >= body.U0 && layer.Uv.Z <= body.U1);
            Assert.True(t / seconds < 0.36);
        }

        Assert.InRange(shown, 10, 16);
    }

    [Fact]
    public void TimingAndBodiesWithoutAMeterDoNotShimmer()
    {
        var list = new BoardDrawList();
        BoardPulses.EmitShimmer(list, new BoardLayerSlot(0, 0, 1, 1, 250, 32, MeterX: 160), BoardStyle.Timing, 0, 0, 32, 1, 0.1, 0, default);
        BoardPulses.EmitShimmer(list, new BoardLayerSlot(0, 0, 1, 1, 250, 32), BoardStyle.Glass, 0, 0, 32, 1, 0.1, 0, default);
        Assert.Equal(0, list.LayerCount);
    }
}
