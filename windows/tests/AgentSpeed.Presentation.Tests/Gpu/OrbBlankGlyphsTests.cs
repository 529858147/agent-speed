using AgentSpeed.Presentation.Orb.Gpu;

namespace AgentSpeed.Presentation.Tests.Gpu;

/// <summary>
/// Issue #31: a string the rasterizer returned blank (WPF's RenderTargetBitmap while displays are reconfigured) is never
/// kept in the glyph atlas; it is retried with backoff, per display scale, and the orb that missed it wakes for the retry.
/// </summary>
public class OrbBlankGlyphsTests
{
    private double _now = 100;

    private OrbBlankGlyphs Make() => new(() => _now);

    private static OrbTextKey Key(string text) => OrbTextKey.Create(text, OrbFont.TrafficNumber, 11.9);

    [Theory]
    [InlineData("0", true)]
    [InlineData(".", true)]
    [InlineData("KB/s", true)]
    [InlineData("待機", true)]
    [InlineData("🚀", true)]
    [InlineData(" ", false)]
    [InlineData(" ​", false)]
    [InlineData("\t\n", false)]
    public void ExpectsInkForAnyVisibleCharacter(string text, bool expected) => Assert.Equal(expected, OrbBlankGlyphs.ExpectsInk(text));

    [Fact]
    public void HasInkReadsOnlyTheAlphaOfPremultipliedPixels()
    {
        var pixels = new byte[4 * 6];
        Assert.False(OrbBlankGlyphs.HasInk(pixels));
        pixels[0] = pixels[1] = pixels[2] = 255;
        Assert.False(OrbBlankGlyphs.HasInk(pixels));
        pixels[4 * 5 + 3] = 1;
        Assert.True(OrbBlankGlyphs.HasInk(pixels));
        Assert.False(OrbBlankGlyphs.HasInk([]));
    }

    [Fact]
    public void ABlankStringWaitsItsDelayThenIsTriedAgain()
    {
        var blank = Make();
        var dot = Key(".");
        Assert.False(blank.IsWaiting(dot, 100));
        Assert.Null(blank.SecondsToRetry);

        blank.Failed(dot, 100);
        Assert.True(blank.IsWaiting(dot, 100));
        Assert.Equal(OrbBlankGlyphs.FirstDelay, blank.SecondsToRetry!.Value, 9);

        _now += OrbBlankGlyphs.FirstDelay;
        Assert.False(blank.IsWaiting(dot, 100));
        Assert.Null(blank.SecondsToRetry);

        blank.Succeeded(dot, 100);
        Assert.Equal(0, blank.Count);
    }

    [Fact]
    public void TheDelayDoublesWhileTheStringStaysBlankUpToTheCap()
    {
        var blank = Make();
        var arrow = OrbTextKey.Create("↓", OrbFont.Arrow, 11.9);
        var delays = new List<double>();
        for (var i = 0; i < 9; i++)
        {
            blank.Failed(arrow, 100);
            delays.Add(blank.SecondsToRetry!.Value);
            _now += delays[^1];
        }

        Assert.Equal(new[] { 0.25, 0.5, 1, 2, 4, 8, 8, 8, 8 }, delays.Select(d => Math.Round(d, 6)));
        blank.Succeeded(arrow, 100);
        blank.Failed(arrow, 100);
        Assert.Equal(OrbBlankGlyphs.FirstDelay, blank.SecondsToRetry!.Value, 9);
    }

    [Fact]
    public void EachDisplayScaleWaitsOnItsOwn()
    {
        var blank = Make();
        var digit = Key("2");
        blank.Failed(digit, 100);
        Assert.True(blank.IsWaiting(digit, 100));
        Assert.False(blank.IsWaiting(digit, 150));
        Assert.False(blank.IsWaiting(Key("5"), 100));
    }

    [Fact]
    public void ARetryLeftUnrequestedNeverKeepsAnOrbAwake()
    {
        var blank = Make();
        blank.Failed(Key("2"), 100);
        _now += 1;
        // Due but nobody asks for it any more (the reading changed): no wake-up is scheduled for it.
        Assert.Null(blank.SecondsToRetry);
        blank.Failed(Key("."), 150);
        Assert.Equal(OrbBlankGlyphs.FirstDelay, blank.SecondsToRetry!.Value, 9);
        blank.Clear();
        Assert.Equal(0, blank.Count);
        Assert.Null(blank.SecondsToRetry);
    }
}
