namespace AgentSpeed.Presentation.Boards.Scene;

/// <summary>A box in points.</summary>
public readonly record struct BoardBox(double Left, double Top, double Width, double Height)
{
    public double Right => Left + Width;

    public double Bottom => Top + Height;

    public double CenterX => Left + Width / 2;

    public double CenterY => Top + Height / 2;
}

/// <summary>
/// Where a style's hero puts its parts (the prototypes' <c>.hero</c>, <c>.value</c>, <c>.meta</c>, <c>.spark</c>, <c>.bars</c>
/// and <c>.led</c>), in points from the hero's top-left corner (<see cref="BoardMetrics.Pad"/>, <see cref="BoardMetrics.HeroTop"/>).
/// The cached hero layer and the scene, which composes the number from glyph layers and animates the timing live dot, both
/// place things by it.
/// </summary>
/// <param name="Width">The hero's width (the square less both pads).</param>
/// <param name="Height">The hero's height.</param>
/// <param name="NumberTop">Top of the number's box, which is 1.08 em tall (the roll window).</param>
/// <param name="NumberSize">The number's font size.</param>
/// <param name="Tracking">Space added between the number's glyphs (the CSS letter spacing, negative).</param>
/// <param name="UnitGap">Space between the number and its unit.</param>
/// <param name="MetaTop">Top of the title and counts line.</param>
/// <param name="History">The sparkline, the bars or the LED bar.</param>
/// <param name="LiveDot">Timing's <c>boards.live</c> dot (7 pt), or an empty box.</param>
public sealed record BoardHeroLayout(
    double Width,
    double Height,
    double NumberTop,
    double NumberSize,
    double Tracking,
    double UnitGap,
    double MetaTop,
    BoardBox History,
    BoardBox LiveDot)
{
    /// <summary>The number's box height: the CSS <c>height: 1.08em</c> of <c>.num</c>.</summary>
    public double NumberHeight => NumberSize * 1.08;

    /// <summary>Timing's history bars (S shows the last 20).</summary>
    public const int Bars = 30;

    public const double BarWidth = 3;

    public const double BarGap = 2;

    /// <summary>Neon's LED segments.</summary>
    public const int Segments = 24;

    /// <summary>Room around the unit's layer for its glow (the layer is the hero's height, the unit at this x).</summary>
    public const double UnitPad = 16;

    /// <summary>Room around the history's layer for the sparkline's end dot, the bars' rounding and the LED bloom.</summary>
    public const double HistoryPad = 12;

    /// <summary>The hero without the live metrics.</summary>
    public static BoardHeroLayout For(BoardStyle style, BoardSize size) => For(style, size, BoardReadouts.None);

    /// <summary>
    /// The hero with the live metrics switched as <paramref name="readouts"/>: with a sub-gauge (layout B, DESIGN 2c Live metrics)
    /// the left column keeps the number, the unit, the title and the counts; glass's and timing's history becomes the full-width
    /// strip under the band (its box is then below the hero), neon keeps its LED bar and centres the left column in the band
    /// less the bar's 17 pt.
    /// </summary>
    public static BoardHeroLayout For(BoardStyle style, BoardSize size, BoardReadouts readouts)
    {
        var metrics = BoardMetrics.For(style, size, readouts);
        if (readouts.Hero)
        {
            return Gauge(style, size, metrics);
        }

        var width = metrics.Side - 2 * metrics.Pad;
        var hero = metrics.HeroHeight;
        var big = metrics.ValueSize;
        switch (style)
        {
            case BoardStyle.Timing:
            {
                // LIVE (10) + 6 + number (1.08 em) + 7 + title pill (17), centered in the hero.
                var top = (hero - (10 + 6 + 1.08 * big + 7 + 17)) / 2;
                var bars = size == BoardSize.S ? 20 : Bars;
                var barsWidth = bars * BarWidth + (bars - 1) * BarGap;
                return new BoardHeroLayout(width, hero, top + 16, big, -0.04 * big, 6, top + 16 + 1.08 * big + 7,
                    new BoardBox(width - barsWidth, (hero - 38) / 2, barsWidth, 38), new BoardBox(0, top + 1.5, 7, 7));
            }

            case BoardStyle.Neon:
                return new BoardHeroLayout(width, hero, 0, big, -0.03 * big, 7, 1.08 * big + 8, new BoardBox(0, hero - 13, width, 7), default);

            default:
            {
                // Number (1.08 em) + 7 + meta line (12 pt text, 16 pt line), centered in the hero.
                var top = (hero - (1.08 * big + 7 + 16)) / 2;
                var spark = size == BoardSize.S ? 76 : 104;
                return new BoardHeroLayout(width, hero, top, big, -0.02 * big, 6, top + 1.08 * big + 7,
                    new BoardBox(width - spark, (hero - 40) / 2, spark, 40), default);
            }
        }
    }

    private static BoardHeroLayout Gauge(BoardStyle style, BoardSize size, BoardMetrics metrics)
    {
        var width = metrics.Side - 2 * metrics.Pad;
        var hero = metrics.HeroHeight;
        var big = metrics.ValueSize;
        switch (style)
        {
            case BoardStyle.Timing:
            {
                var top = (hero - (10 + 6 + 1.08 * big + 7 + 17)) / 2;
                return new BoardHeroLayout(width, hero, top + 16, big, -0.04 * big, 6, top + 16 + 1.08 * big + 7,
                    new BoardBox(0, metrics.StripTop - metrics.HeroTop, width, metrics.StripHeight), new BoardBox(0, top + 1.5, 7, 7));
            }

            case BoardStyle.Neon:
            {
                // .hero { padding-bottom: 17px } with .hl centred: the number (1.08 em), 8, the meta line (12).
                var top = (hero - 17 - (1.08 * big + 8 + 12)) / 2;
                return new BoardHeroLayout(width, hero, top, big, -0.03 * big, 7, top + 1.08 * big + 8, new BoardBox(0, hero - 13, width, 7), default);
            }

            default:
            {
                var top = (hero - (1.08 * big + 7 + 16)) / 2;
                return new BoardHeroLayout(width, hero, top, big, -0.02 * big, 6, top + 1.08 * big + 7,
                    new BoardBox(0, metrics.StripTop - metrics.HeroTop, width, metrics.StripHeight), default);
            }
        }
    }
}
