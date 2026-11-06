using AgentSpeed.Presentation.Boards;
using AgentSpeed.Presentation.Boards.Demo;
using AgentSpeed.Presentation.Boards.Effects;
using AgentSpeed.Presentation.Boards.Gpu;
using AgentSpeed.Presentation.Boards.Scene;
using AgentSpeed.Presentation.Colors;
using AgentSpeed.Presentation.Localization;
using AgentSpeed.Presentation.Orb.Gpu;

namespace AgentSpeed.Presentation.Tests.Boards;

public class BoardContentTests
{
    private static readonly Localizer Zh = new(Localizer.LoadEmbeddedTable(), LanguageCodes.ZhHant);

    // The custom properties of spec/prototypes/board/board-*.html.
    [Theory]
    [InlineData(BoardStyle.Timing, true, "#F5F7FB", "#0C0E14", "#1B1F2A")]
    [InlineData(BoardStyle.Timing, false, "#0E1118", "#FBFBFD", "#E6E9F0")]
    [InlineData(BoardStyle.Neon, true, "#F3F6FF", "#05060C", null)]
    [InlineData(BoardStyle.Neon, false, "#0A0D18", "#F7F8FC", null)]
    public void PalettesCarryThePrototypesColors(BoardStyle style, bool dark, string ink, string panel, string? block)
    {
        var palette = BoardPalette.For(style, dark);
        Assert.Equal(Rgba.FromHex(ink), palette.Ink);
        Assert.Equal(Rgba.FromHex(panel), palette.Panel);
        if (block is not null)
        {
            Assert.Equal(Rgba.FromHex(block), palette.Block);
        }

        Assert.Equal(dark, palette.Dark);
        Assert.False(palette.HighContrast);
    }

    [Theory]
    [InlineData(BoardStyle.Glass, true)]
    [InlineData(BoardStyle.Timing, false)]
    [InlineData(BoardStyle.Neon, true)]
    public void HighContrastMakesThePanelSolidAndTheInkFull(BoardStyle style, bool dark)
    {
        var palette = BoardPalette.For(style, dark, highContrast: true);
        Assert.True(palette.HighContrast);
        Assert.Equal(1, palette.Panel.A, 6);
        Assert.Equal(dark ? Rgba.White : Rgba.Black, palette.Ink);
        Assert.Equal(palette.Ink, palette.Ink2);
        Assert.Equal(palette.Ink, palette.Edge);
    }

    [Theory]
    [InlineData(BoardSize.S, 20)]
    [InlineData(BoardSize.M, 30)]
    [InlineData(BoardSize.L, 30)]
    public void TimingsBarsSitAtTheHerosRightAndItsMetaBelowThem(BoardSize size, int bars)
    {
        var hero = BoardHeroLayout.For(BoardStyle.Timing, size);
        var metrics = BoardMetrics.For(BoardStyle.Timing, size);
        Assert.Equal(metrics.Side - 2 * metrics.Pad, hero.Width, 6);
        Assert.Equal(bars * BoardHeroLayout.BarWidth + (bars - 1) * BoardHeroLayout.BarGap, hero.History.Width, 6);
        Assert.Equal(hero.Width, hero.History.Right, 6);
        Assert.Equal(38, hero.History.Height, 6);

        // The counts may run under the bars: the meta line starts below them.
        Assert.True(hero.MetaTop >= hero.History.Bottom, $"meta at {hero.MetaTop}, bars end at {hero.History.Bottom}");
        Assert.True(hero.NumberTop > hero.LiveDot.Bottom);
        Assert.Equal(7, hero.LiveDot.Width, 6);
        Assert.Equal(-0.04 * metrics.ValueSize, hero.Tracking, 6);
    }

    [Theory]
    [InlineData(BoardSize.S)]
    [InlineData(BoardSize.M)]
    [InlineData(BoardSize.L)]
    public void NeonsLedBarRunsAlongTheHerosFoot(BoardSize size)
    {
        var hero = BoardHeroLayout.For(BoardStyle.Neon, size);
        Assert.Equal(0, hero.History.Left, 6);
        Assert.Equal(hero.Width, hero.History.Width, 6);
        Assert.Equal(7, hero.History.Height, 6);
        Assert.Equal(hero.Height - 6, hero.History.Bottom, 6);
        Assert.True(hero.MetaTop + 12 <= hero.History.Top, "the meta line clears the LED bar");
        Assert.Equal(0, hero.LiveDot.Width, 6);
    }

    [Theory]
    [InlineData(BoardSize.S, 76)]
    [InlineData(BoardSize.M, 104)]
    [InlineData(BoardSize.L, 104)]
    public void GlasssSparklineKeepsItsWidth(BoardSize size, double spark)
    {
        var hero = BoardHeroLayout.For(BoardStyle.Glass, size);
        Assert.Equal(spark, hero.History.Width, 6);
        Assert.Equal(hero.Width, hero.History.Right, 6);
        Assert.Equal(1.08 * hero.NumberSize, hero.NumberHeight, 6);
    }

    [Theory]
    [InlineData(BoardStyle.Glass, BoardCalloutKind.Climb)]
    [InlineData(BoardStyle.Timing, BoardCalloutKind.Climb)]
    [InlineData(BoardStyle.Neon, BoardCalloutKind.Climb)]
    [InlineData(BoardStyle.Neon, BoardCalloutKind.NewPeak)]
    public void ChipsPopInHoldAndFade(BoardStyle style, BoardCalloutKind kind)
    {
        var start = BoardCallouts.Chip(style, kind, 0);
        var hold = BoardCallouts.Chip(style, kind, 0.4);
        var end = BoardCallouts.Chip(style, kind, 1);
        Assert.Equal(0, start.Alpha, 6);
        Assert.Equal((1.0, 0.0, 0.0, 1.0), hold);
        Assert.Equal(0, end.Alpha, 6);
    }

    [Fact]
    public void TheBannerWipesInHoldsAndWipesOutToTheRight()
    {
        Assert.Equal((0.0, 0.0), BoardCallouts.Wipe(0));
        Assert.Equal((0.0, 1.0), BoardCallouts.Wipe(0.5));
        Assert.Equal((1.0, 1.0), BoardCallouts.Wipe(1));
        Assert.True(BoardCallouts.Wipe(0.09).To is > 0 and < 1);
        Assert.True(BoardCallouts.Wipe(0.9).From is > 0 and < 1);
    }

    [Fact]
    public void TheDemoFillsEveryHeroAndFooterField()
    {
        var feed = new BoardDemoFeed(BoardSimScenario.Team) { Localizer = Zh };
        BoardTick? tick = null;
        for (var i = 0; i < 8; i++)
        {
            tick = feed.Step(BoardStyle.Timing, BoardSize.M);
        }

        Assert.NotNull(tick);
        Assert.False(string.IsNullOrEmpty(tick.Title));
        Assert.True(tick.GeneratingCount > 0);
        Assert.NotEmpty(tick.History);
        Assert.Equal(tick.Value, tick.History[^1], 6);
        Assert.True(tick.PeakToday >= tick.Value);
        Assert.False(string.IsNullOrEmpty(tick.TodayText));
        Assert.StartsWith("$", tick.CostText, StringComparison.Ordinal);
        Assert.False(string.IsNullOrEmpty(tick.PeakText));
    }

    [Fact]
    public void WithoutALocalizerTheDemoHasNoTitle()
    {
        var tick = new BoardDemoFeed(BoardSimScenario.Duel).Step(BoardStyle.Glass, BoardSize.S);
        Assert.Equal(string.Empty, tick.Title);
    }

    [Theory]
    [InlineData(BoardStyle.Glass)]
    [InlineData(BoardStyle.Timing)]
    [InlineData(BoardStyle.Neon)]
    public void ASweepStaysOnItsRow(BoardStyle style)
    {
        var sweeps = new BoardSweeps();
        sweeps.Add("row", 20, 300, 100, 30, Rgba.FromHex("#34D399"), BoardEffectsPresets.For(style).Sweep);
        var list = new BoardDrawList();
        var crossed = 0;
        for (var t = 0.0; t < 1; t += 0.05)
        {
            list.Reset();
            sweeps.Emit(list);
            foreach (var instance in list.Instances)
            {
                Assert.True(instance.A.X - instance.B.X >= 20 - 1e-3 && instance.A.X + instance.B.X <= 300 + 1e-3,
                    $"{style} band at {instance.A.X} ± {instance.B.X}");
                Assert.Equal(100, instance.A.Y, 3);
            }

            crossed += list.Count > 0 ? 1 : 0;
            sweeps.Step(0.05 * BoardEffectsPresets.For(style).Sweep.Seconds);
        }

        Assert.True(crossed > 5, $"{style} sweep shows on {crossed} frames");
        sweeps.Step(0.1);
        Assert.Equal(0, sweeps.Count);
    }

    [Theory]
    [InlineData(BoardStyle.Glass, 10, 10)]
    [InlineData(BoardStyle.Timing, 0, 0)]
    [InlineData(BoardStyle.Neon, 10, 9)]
    public void ASweepLeansAtItsPrototypesAngleInsideTheRowsCorners(BoardStyle style, double slant, double corner)
    {
        var look = BoardEffectsPresets.For(style).Sweep;
        var sweeps = new BoardSweeps();
        sweeps.Add("row", 20, 300, 100, 30, Rgba.FromHex("#34D399"), look);
        var list = new BoardDrawList();
        var shown = 0;
        for (var step = 0; step < 40; step++)
        {
            list.Reset();
            sweeps.Emit(list);
            shown += list.Count > 0 ? 1 : 0;
            for (var i = 0; i < list.Count; i++)
            {
                var instance = list.Instances[i];
                Assert.Equal((float)OrbFill.Band, instance.Extra2.X);
                Assert.Equal(slant * Math.PI / 180, instance.Extra2.Y, 4);
                Assert.Equal(corner, instance.B.Z, 4);
                Assert.Equal(15 - look.Inset, instance.B.Y, 4);
                Assert.True(instance.Extra2.Z < instance.Extra2.W);

                // The stretches tile: each starts where the one before it ends.
                if (i > 0)
                {
                    Assert.Equal(list.Instances[i - 1].Extra2.W, instance.Extra2.Z, 3);
                }
            }

            sweeps.Step(look.Seconds / 40);
        }

        Assert.True(shown > 3, $"{style} sweep shows on {shown} frames");
    }

    [Fact]
    public void TimingDarkensALightRowInsteadOfLightingIt()
    {
        var timing = BoardEffectsPresets.Timing;
        Assert.True(timing.Sweep.Additive);
        Assert.NotNull(timing.SweepLight);
        Assert.False(timing.SweepLight.Additive);
        Assert.NotNull(BoardEffectsPresets.Glass.LeadSweep);
    }

    private sealed class GlyphLayers : IBoardLayers
    {
        public bool TryGet(string key, out BoardLayerSlot slot)
        {
            // Every value glyph is 8 pt wide plus its pads, 20 pt tall; nothing else exists.
            slot = new BoardLayerSlot(0, 0, 1, 1, 8 + 2 * BoardValueGlyphs.Pad, 20);
            return key.StartsWith("#v", StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AValueEndsAtItsRightEdgeInItsColor()
    {
        var list = new BoardDrawList();
        var color = Rgba.FromHex("#FB7185").WithAlpha(0.5);
        BoardValueGlyphs.Emit(list, new GlyphLayers(), "1.2k", BoardValueSet.Group, right: 300, centerY: 50, tracking: -0.26, color, alpha: 0.8,
            default);
        Assert.Equal(4, list.LayerCount);
        var layers = list.Layers;
        var advance = 8 - 0.26;
        Assert.Equal(300 - 4 * 8 - 3 * -0.26 - BoardValueGlyphs.Pad, layers[0].Rect.X, 3);
        Assert.Equal(layers[0].Rect.X + advance, layers[1].Rect.X, 3);
        Assert.Equal(40, layers[0].Rect.Y, 3);
        Assert.Equal(0.4, layers[3].Params.X, 3);
        Assert.Equal(1, layers[3].Tint.W, 6);
        Assert.Equal((float)color.R, layers[3].Tint.X, 5);
    }

    [Theory]
    [InlineData(BoardStyle.Glass, false, 3000, "hot")]
    [InlineData(BoardStyle.Glass, true, 3000, "ink2")]
    [InlineData(BoardStyle.Neon, true, 3000, "hot")]
    [InlineData(BoardStyle.Timing, false, 3000, "ink")]
    [InlineData(BoardStyle.Neon, false, 0, "ink3")]
    [InlineData(BoardStyle.Glass, false, 50, "ink")]
    public void ValuesTakeThePrototypesColors(BoardStyle style, bool sub, double live, string expected)
    {
        var palette = BoardPalette.For(style, dark: true);
        var color = BoardValueGlyphs.Color(style, palette, sub, live, 3200);
        var want = expected switch
        {
            "ink" => palette.Ink,
            "ink2" => palette.Ink2,
            "ink3" => palette.Ink3,
            _ => Modes.EffortCapsulePalette.Heat(Heat.HeatMath.Heat(live, 3200), true),
        };
        Assert.Equal(want, color);
        Assert.Equal("–", BoardValueGlyphs.Text(0.04));
        Assert.All(BoardValueGlyphs.Text(1234.5), c => Assert.Contains(c, BoardValueGlyphs.Glyphs));
    }

    [Theory]
    [InlineData(300, 271, "−29")]
    [InlineData(250, 270, "+20")]
    public void IntervalsAreTheGapToTheGroupAbove(double above, double live, string expected)
    {
        Assert.Equal(expected, BoardValueGlyphs.Interval(above, live));
        Assert.All(expected, c => Assert.Contains(c, BoardValueGlyphs.Glyphs));
        Assert.Equal(3, BoardValueGlyphs.Sets(BoardStyle.Timing));
        Assert.Equal(2, BoardValueGlyphs.Sets(BoardStyle.Neon));
    }
}
