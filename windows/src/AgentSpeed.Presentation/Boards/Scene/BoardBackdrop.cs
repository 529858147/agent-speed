using AgentSpeed.Presentation.Boards.Gpu;
using AgentSpeed.Presentation.Colors;
using AgentSpeed.Presentation.Orb.Gpu;

namespace AgentSpeed.Presentation.Boards.Scene;

/// <summary>
/// The rounded square's fill and edge per style and appearance (the prototypes' <c>.panel</c>). Windows draws glass as a tinted
/// translucent fill (DESIGN 2c: an acrylic blur only if it costs nothing per frame), so its fill is denser than the
/// prototype's 0.58 over a 30 px backdrop blur. The static patterns (timing's hatch and footer slab, neon's grid) are a cached
/// layer; the moving ambience is <see cref="BoardAmbience"/>.
/// </summary>
public static class BoardBackdrop
{
    public static Rgba Fill(BoardStyle style, bool dark = true) => BoardPalette.For(style, dark).Panel;

    public static Rgba Edge(BoardStyle style, bool dark = true) => BoardPalette.For(style, dark).Edge;

    public static void Emit(BoardDrawList list, in BoardCanvas canvas, BoardStyle style, BoardPalette palette, BoardMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(palette);
        ArgumentNullException.ThrowIfNull(metrics);
        var center = canvas.Center;
        list.Clip = BoardClip.None;
        list.Add(OrbGpuInstance.RoundedRect(center, center, canvas.Half, canvas.Half, 0, canvas.Radius, palette.Panel));
        if (palette.HighContrast)
        {
            list.Add(OrbGpuInstance.RoundedRing(center, center, canvas.Half - 0.5, canvas.Half - 0.5, canvas.Radius - 0.5, 1, palette.Edge));
            return;
        }

        switch (style)
        {
            case BoardStyle.Timing:
                // box-shadow: 0 0 0 1px var(--edge), just outside the slab.
                list.Add(OrbGpuInstance.RoundedRing(center, center, canvas.Half + 0.5, canvas.Half + 0.5, canvas.Radius + 0.5, 1, palette.Edge));
                break;
            case BoardStyle.Neon:
                break;
            default:
            {
                // inset 0 0 0 .5px var(--glass-edge), inset 0 1px 0 0 var(--glass-top).
                list.Add(OrbGpuInstance.RoundedRing(center, center, canvas.Half - 0.25, canvas.Half - 0.25, canvas.Radius - 0.25, 0.5, palette.Edge));
                var m = BoardCanvas.Margin;
                list.Clip = BoardClip.Inside;
                list.Add(OrbGpuInstance.Capsule(m + canvas.Radius * 0.55, m + 0.5, m + canvas.Side - canvas.Radius * 0.55, m + 0.5, 1, palette.Pattern));
                list.Clip = BoardClip.None;

                // .board::before: the list's top rule, 6 pt in from its sides; the captions line's hairline replaces it.
                if (metrics.IntelligenceColumn)
                {
                    break;
                }

                list.Add(OrbGpuInstance.RoundedRect(m + metrics.ListLeft + metrics.ListWidth / 2, m + metrics.ListTop + 0.5, metrics.ListWidth / 2 - 6,
                    0.5, 0, 0, palette.Line));
                break;
            }
        }
    }
}
