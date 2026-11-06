using AgentSpeed.Presentation.Colors;

namespace AgentSpeed.Presentation.Boards.Scene;

/// <summary>
/// One style's colors in one appearance (the custom properties of <c>spec/prototypes/board/board-*.html</c>, dark and light),
/// shared by the cached content and the GPU backdrop. High contrast (DESIGN 2c Accessibility) keeps the style's hues but makes
/// the panel solid and every text tone full strength.
/// </summary>
/// <param name="Ink">Primary text.</param>
/// <param name="Ink2">Secondary text.</param>
/// <param name="Ink3">Tertiary text (ranks, details, footer labels).</param>
/// <param name="Line">Hairlines: the list's top rule, timing's row rules, sub elbows.</param>
/// <param name="Fill">Hover and capsule fill.</param>
/// <param name="Unlit">Unlit meter bars and LED segments.</param>
/// <param name="AmberInk">The speed bolts.</param>
/// <param name="Panel">The rounded square's fill (glass: denser than the prototype, which blurs the desktop behind it).</param>
/// <param name="Panel2">Timing's footer slab, neon's deck tint.</param>
/// <param name="Edge">The square's hairline edge.</param>
/// <param name="Pattern">Timing's diagonal hatch, neon's 22 pt grid, glass's top highlight.</param>
/// <param name="Block">Timing's position blocks.</param>
/// <param name="BlockInk">Timing's position numbers.</param>
public sealed record BoardPalette(
    Rgba Ink,
    Rgba Ink2,
    Rgba Ink3,
    Rgba Line,
    Rgba Fill,
    Rgba Unlit,
    Rgba AmberInk,
    Rgba Panel,
    Rgba Panel2,
    Rgba Edge,
    Rgba Pattern,
    Rgba Block,
    Rgba BlockInk)
{
    public bool Dark { get; init; } = true;

    public bool HighContrast { get; init; }

    public static BoardPalette For(BoardStyle style, bool dark, bool highContrast = false)
    {
        var palette = (style, dark) switch
        {
            (BoardStyle.Timing, true) => new BoardPalette(Hex("#F5F7FB"), Rgb(232, 236, 245, 0.66), Rgb(232, 236, 245, 0.36), White(0.06), White(0.05),
                White(0.16), Hex("#F5A524"), Hex("#0C0E14"), Hex("#12151D"), White(0.09), White(0.025), Hex("#1B1F2A"), Hex("#E8ECF5")),
            (BoardStyle.Timing, false) => new BoardPalette(Hex("#0E1118"), Rgb(14, 17, 24, 0.62), Rgb(14, 17, 24, 0.38), Black(0.06), Black(0.045),
                Black(0.14), Hex("#B86E00"), Hex("#FBFBFD"), Hex("#F1F3F7"), Black(0.08), Black(0.025), Hex("#E6E9F0"), Hex("#1A1E28")),
            (BoardStyle.Neon, true) => new BoardPalette(Hex("#F3F6FF"), Rgb(214, 224, 255, 0.66), Rgb(214, 224, 255, 0.36), Rgb(140, 170, 255, 0.10),
                Rgb(140, 170, 255, 0.07), Rgb(140, 170, 255, 0.16), Hex("#FFC24B"), Hex("#05060C"), Hex("#0A0D18"), Rgb(140, 170, 255, 0.10),
                Rgb(120, 150, 255, 0.05), Rgb(140, 170, 255, 0.13), Hex("#F3F6FF")),
            (BoardStyle.Neon, false) => new BoardPalette(Hex("#0A0D18"), Rgb(10, 13, 24, 0.62), Rgb(10, 13, 24, 0.38), Rgb(20, 30, 80, 0.08),
                Rgb(20, 30, 80, 0.05), Rgb(20, 30, 80, 0.14), Hex("#B86E00"), Hex("#F7F8FC"), Hex("#ECEFF7"), Rgb(20, 30, 80, 0.10),
                Rgb(20, 30, 80, 0.04), Rgb(20, 30, 80, 0.09), Hex("#0A0D18")),
            (_, true) => new BoardPalette(Rgb(244, 246, 252, 0.96), Rgb(226, 230, 242, 0.62), Rgb(226, 230, 242, 0.38), White(0.08), White(0.055),
                White(0.18), Hex("#F5A524"), Rgb(20, 22, 33, 0.86), Rgb(20, 22, 33, 0.86), White(0.13), White(0.22), White(0.09), Hex("#F4F6FC")),
            _ => new BoardPalette(Rgb(17, 20, 30, 0.94), Rgb(30, 36, 52, 0.6), Rgb(30, 36, 52, 0.38), Black(0.07), Black(0.045), Black(0.14),
                Hex("#B86E00"), Rgb(250, 250, 253, 0.9), Rgb(250, 250, 253, 0.9), Black(0.08), White(0.9), Black(0.07), Hex("#11141E")),
        };

        palette = palette with { Dark = dark };
        if (!highContrast)
        {
            return palette;
        }

        var ink = dark ? Rgba.White : Rgba.Black;
        return palette with
        {
            HighContrast = true,
            Ink = ink,
            Ink2 = ink,
            Ink3 = ink.WithAlpha(0.8),
            Line = ink.WithAlpha(0.5),
            Panel = palette.Panel.WithAlpha(1),
            Panel2 = palette.Panel2.WithAlpha(1),
            Edge = ink,
            Unlit = ink.WithAlpha(0.35),
        };
    }

    private static Rgba Hex(string hex) => Rgba.FromHex(hex);

    private static Rgba Rgb(int r, int g, int b, double a) => new(r / 255.0, g / 255.0, b / 255.0, a);

    private static Rgba White(double a) => Rgba.White.WithAlpha(a);

    private static Rgba Black(double a) => Rgba.Black.WithAlpha(a);
}
