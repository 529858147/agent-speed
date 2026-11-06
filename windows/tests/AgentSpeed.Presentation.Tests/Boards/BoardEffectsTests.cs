using AgentSpeed.Presentation.Boards;
using AgentSpeed.Presentation.Boards.Effects;
using AgentSpeed.Presentation.Boards.Gpu;
using AgentSpeed.Presentation.Colors;
using AgentSpeed.Presentation.Heat;
using AgentSpeed.Presentation.Motion;
using AgentSpeed.Presentation.Orb;
using AgentSpeed.Presentation.Orb.Gpu;
using AgentSpeed.Presentation.Settings;

namespace AgentSpeed.Presentation.Tests.Boards;

public class BoardEffectsTests
{
    private static readonly BoardRowFx Row = new("g1", 10, 330, 140, 32, 24, 140, 106, Rgba.FromHex("#3B82F6"));

    private static BoardEffects Effects(BoardStyle style = BoardStyle.Glass, BoardEffectsPolicy? policy = null)
    {
        var effects = new BoardEffects(style, BoardSize.M, 7);
        if (policy is { } p)
        {
            effects.Policy = p;
        }

        return effects;
    }

    private static void Run(BoardEffects effects, double seconds, double start = 0)
    {
        for (var t = start; t <= start + seconds; t += 1 / 60.0)
        {
            effects.Advance(t);
        }
    }

    [Theory]
    [InlineData(BoardStyle.Glass, new[] { 1, 1, 2, 3, 4 })]
    [InlineData(BoardStyle.Timing, new[] { 1, 1, 2, 2, 3 })]
    [InlineData(BoardStyle.Neon, new[] { 1, 2, 3, 4, 4 })]
    public void CometsFollowTheTierPerStyle(BoardStyle style, int[] counts)
    {
        var effects = Effects(style);
        foreach (var tier in Enum.GetValues<SpeedTier>())
        {
            effects.SetHeat(0.5, 0, tier, true);
            Assert.Equal(counts[(int)tier], effects.Comets.Count);
        }
    }

    [Fact]
    public void LapRunsFromSevenSecondsAtIdleToOnePointSixAtTheRedline()
    {
        var effects = Effects();
        effects.SetHeat(0, 0, SpeedTier.Idle, true);
        Assert.Equal(7, effects.Comets.LapSeconds, 6);
        effects.SetHeat(1, 0, SpeedTier.Redline, true);
        Assert.Equal(1.6, effects.Comets.LapSeconds, 6);
    }

    [Fact]
    public void AnIdleBoardNeedsNoFrames()
    {
        var effects = Effects();
        effects.SetHeat(0, 0, SpeedTier.Idle, false);
        Run(effects, 1);
        Assert.Equal(FrameClass.None, effects.Demand);

        effects.SetHeat(0.3, 0, SpeedTier.Cruise, true);
        Run(effects, 1.5, 1.1);
        Assert.Equal(FrameClass.Fps30, effects.Demand);

        effects.SetHeat(0.7, 0, SpeedTier.Overdrive, true);
        Run(effects, 1.5, 2.7);
        Assert.Equal(FrameClass.Fps60, effects.Demand);
    }

    [Fact]
    public void ShortLivedEffectsRunAtSixtyUntilTheyEnd()
    {
        var effects = Effects(BoardStyle.Neon);
        effects.SetHeat(0.3, 0, SpeedTier.Cruise, true);
        Run(effects, 1.2);
        effects.Overtake(Row);
        Assert.True(effects.Alive);
        Assert.Equal(FrameClass.Fps60, effects.Demand);
        Run(effects, 2, 1.3);
        Assert.False(effects.Alive);
        Assert.Equal(FrameClass.Fps30, effects.Demand);
    }

    [Fact]
    public void ANewLeaderTurnsTheCometsGoldAndFaster()
    {
        var effects = Effects();
        effects.SetHeat(0.4, 0, SpeedTier.Boost, true);
        var lap = effects.Comets.LapSeconds;
        effects.NewLeader(Row);
        Assert.Equal(BoardComets.GoldSeconds, effects.Comets.Gold, 6);
        Assert.Equal(lap / BoardComets.GoldSpeedup, effects.Comets.LapSeconds, 6);
        Assert.True(effects.Particles.Count >= 70);
        Run(effects, 2.5);
        Assert.Equal(0, effects.Comets.Gold);
    }

    [Fact]
    public void WithoutCelebrationsANewLeaderIsAPass()
    {
        var effects = Effects(policy: new BoardEffectsPolicy(Celebrate: false));
        effects.SetHeat(0.4, 0, SpeedTier.Boost, true);
        effects.NewLeader(Row);
        Assert.Equal(0, effects.Comets.Gold);
        Assert.InRange(effects.Particles.Count, 1, BoardParticles.StreakCount + 14);
    }

    [Theory]
    [InlineData(EffectsIntensity.Off, false)]
    [InlineData(EffectsIntensity.Standard, true)]
    public void ReduceMotionAndOffDrawNothingThatTravels(EffectsIntensity intensity, bool reduceMotion)
    {
        var effects = Effects(BoardStyle.Neon, new BoardEffectsPolicy(intensity, reduceMotion));
        effects.SetHeat(1, 0.5, SpeedTier.Redline, true);
        effects.Overtake(Row);
        effects.NewLeader(Row);
        effects.Punch(20, 20, 120, 40, 0.5);
        Run(effects, 1);
        Assert.Equal(0, effects.Comets.Count);
        Assert.False(effects.Alive);
        Assert.Equal(FrameClass.None, effects.Demand);
    }

    [Fact]
    public void SoftKeepsOneCometAndNoParticles()
    {
        var effects = Effects(BoardStyle.Glass, new BoardEffectsPolicy(EffectsIntensity.Soft));
        effects.SetHeat(1, 0, SpeedTier.Redline, true);
        effects.Overtake(Row);
        Run(effects, 0.5);
        Assert.Equal(1, effects.Comets.Count);
        Assert.Equal(0, effects.Particles.Count);
    }

    [Fact]
    public void MaxReleasesHalfAgainAsManyParticles()
    {
        var standard = Effects();
        var max = Effects(policy: new BoardEffectsPolicy(EffectsIntensity.Max));
        var burst = new BoardBurst(20, 100, 1, new BoardColors(Rgba.White));
        standard.Burst(0, 0, burst);
        max.Burst(0, 0, burst);
        Assert.Equal(20, standard.Particles.Count);
        Assert.Equal(30, max.Particles.Count);
    }

    [Fact]
    public void ThePoolsAreBounded()
    {
        var effects = Effects();
        for (var i = 0; i < 100; i++)
        {
            effects.Celebrate(100, 100);
            effects.Lightning(0, 0, 100, 100, Rgba.White, 4);
        }

        Assert.Equal(BoardParticles.Capacity, effects.Particles.Count);
        var list = new BoardDrawList();
        effects.EmitFront(list);
        Assert.True(list.Count > BoardParticles.Capacity);
    }

    [Fact]
    public void HighContrastDrawsNoGlows()
    {
        var effects = Effects(BoardStyle.Glass, new BoardEffectsPolicy(HighContrast: true));
        effects.SetHeat(1, 1, SpeedTier.Redline, true);
        Run(effects, 1);
        var list = new BoardDrawList();
        effects.EmitBack(list);
        effects.EmitFront(list);
        Assert.True(list.Count > 0);
        foreach (var instance in list.Instances)
        {
            Assert.Equal(0, instance.B.W);
        }
    }

    [Fact]
    public void TheBloomStaysOutsideAndTheWarpInside()
    {
        var effects = Effects(BoardStyle.Neon);
        effects.SetHeat(0.8, 0.2, SpeedTier.Redline, true);
        Run(effects, 0.5);
        var list = new BoardDrawList();
        effects.EmitBack(list);
        Assert.Equal(BoardClip.Outside, list.Batches[0].Clip);
        Assert.Contains(list.Batches.ToArray(), b => b.Clip == BoardClip.Inside && b.Blend == OrbBlend.Additive && b.Count >= 20);
    }

    [Theory]
    [InlineData(BoardStyle.Glass)]
    [InlineData(BoardStyle.Timing)]
    [InlineData(BoardStyle.Neon)]
    public void TheBloomKeepsItsWholeBlurAtTheTopOfTheHeat(BoardStyle style)
    {
        // DESIGN 2c Perimeter: σ = bloom × blur / 2 at every heat; the Outside clip's fade, not a smaller blur, keeps it off the
        // window's edge.
        var effects = Effects(style);
        effects.SetHeat(1, 1, SpeedTier.Redline, true);
        Run(effects, 0.5);
        var list = new BoardDrawList();
        effects.EmitBack(list);
        Assert.Equal(BoardClip.Outside, list.Batches[0].Clip);
        var bloom = list.Instances[list.Batches[0].Start];
        Assert.Equal(OrbGpuInstance.Glow(BoardEffectsPresets.For(style).BloomBlur * 1.15 / 2), bloom.B.W, 2);
    }

    [Fact]
    public void ShockwavesStayInsideTheSquare()
    {
        var effects = Effects();
        effects.Celebrate(24, 140);
        Run(effects, 0.4);
        var list = new BoardDrawList();
        effects.EmitFront(list);
        var instances = list.Instances.ToArray();
        var waves = 0;
        foreach (var batch in list.Batches)
        {
            for (var i = batch.Start; i < batch.Start + batch.Count; i++)
            {
                if (batch.Pass == BoardPass.Shapes && instances[i].Shape == OrbShape.Ring)
                {
                    Assert.Equal(BoardClip.Inside, batch.Clip);
                    waves++;
                }
            }
        }

        Assert.Equal(2, waves);
    }

    [Fact]
    public void RedlineSparksLeaveTheCometHeads()
    {
        var effects = Effects();
        effects.SetHeat(1, 0, SpeedTier.Redline, true);
        effects.Comets.Settle();
        Run(effects, 0.2);
        Assert.InRange(effects.Particles.Count, 4 * 3, 4 * 3 * 2);
    }

    [Fact]
    public void AWarmRedlineFrameDoesNotAllocate()
    {
        var effects = Effects(BoardStyle.Neon);
        var list = new BoardDrawList();
        effects.SetHeat(1, 0.6, SpeedTier.Redline, true);
        var t = 0.0;
        void Frame()
        {
            t += 1 / 60.0;
            effects.Advance(t);
            list.Reset();
            effects.EmitBack(list);
            effects.EmitFront(list);
            _ = list.Fingerprint();
        }

        effects.NewLeader(Row);
        for (var i = 0; i < 120; i++)
        {
            Frame();
        }

        effects.Overtake(Row);
        effects.Punch(20, 20, 120, 40, 0.4);
        Frame();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 30; i++)
        {
            Frame();
        }

        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
    }

    [Fact]
    public void TheCanvasHitsOnlyTheRoundedSquare()
    {
        var canvas = BoardCanvas.For(BoardStyle.Glass, BoardSize.M);
        Assert.Equal(420, canvas.Extent);
        Assert.True(canvas.Contains(210, 210));
        Assert.True(canvas.Contains(BoardCanvas.Margin + 1, 210));
        Assert.False(canvas.Contains(BoardCanvas.Margin + 2, BoardCanvas.Margin + 2));
        Assert.False(canvas.Contains(20, 210));
    }

    [Theory]
    [InlineData(0.3, 0.7, 0.2, 1.0)]
    [InlineData(0.2, 0.8, 0.2, 1.0)]
    public void CubicBezierRunsFromZeroToOne(double x1, double y1, double x2, double y2)
    {
        Assert.Equal(0, Easing.CubicBezier(x1, y1, x2, y2, 0), 6);
        Assert.Equal(1, Easing.CubicBezier(x1, y1, x2, y2, 1), 6);
        Assert.True(Easing.CubicBezier(x1, y1, x2, y2, 0.5) > 0.8);
        Assert.Equal(0.37, Easing.CubicBezier(0, 0, 1, 1, 0.37), 5);
    }
}
