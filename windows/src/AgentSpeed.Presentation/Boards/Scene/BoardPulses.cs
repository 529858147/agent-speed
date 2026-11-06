using AgentSpeed.Core;
using AgentSpeed.Presentation.Boards.Gpu;
using AgentSpeed.Presentation.Motion;

namespace AgentSpeed.Presentation.Boards.Scene;

/// <summary>
/// Row badges and the prototypes' endless row animations, composed on the GPU from cached layers so no row is rasterized again
/// when an agent changes phase or a pulse moves (DESIGN 2c Two layers): every phase badge is a shared layer; a generating one
/// pulses (glass's three bouncing dots <c>.badge.gen i</c>, neon's breathing dot <c>.badge.gen::before</c>, timing's fading
/// stripe <c>.gen .stripe</c>), and so does the top-rung meter's shimmer
/// (<c>.cap.top .meter::after</c>, glass and neon). They move only while the board animates anyway; otherwise, and under
/// Reduce Motion, they rest like the prototypes' <c>.rm</c> rules.
/// </summary>
public static class BoardPulses
{
    private static readonly string[] Keys = BuildKeys(string.Empty);

    // Two-line group rows (DESIGN 2c, Live metrics): timing's stripe spans both lines (24 pt).
    private static readonly string[] TallKeys = BuildKeys("2");

    /// <summary>
    /// The cached layer of a row's phase badge (timing: its team stripe), shared by rows of one source, kind and phase, so a row
    /// body never depends on its phase; null for the more lines.
    /// </summary>
    public static string? BadgeKey(BoardEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (Badge(entry) is not { } badge)
        {
            return null;
        }

        var index = (((int)badge.Source * 3) + (int)badge.Phase) * 2 + (entry.Kind == BoardEntryKind.Sub ? 1 : 0);
        var keys = entry.TwoLine ? TallKeys : Keys;
        return (uint)index < (uint)keys.Length ? keys[index] : null;
    }

    /// <summary>The phase and source a row's badge shows (a group shows generating while any member generates).</summary>
    public static (AgentPhase Phase, SourceKind Source)? Badge(BoardEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return entry.Kind switch
        {
            BoardEntryKind.Group => (entry.Group.BadgePhase, entry.Group.Root.Source),
            BoardEntryKind.Sub when entry.Agent is { } agent => (agent.Phase, agent.Source),
            _ => null,
        };
    }

    /// <summary>The badge layer's size in points: what it draws, centered, with room for its glow.</summary>
    public static (double Width, double Height) BadgeSize(BoardStyle style, bool sub, AgentPhase phase, bool tall = false) => style switch
    {
        BoardStyle.Neon => (44, 44),
        BoardStyle.Timing => sub ? (20, 28) : tall ? (22, 42) : (22, 36),
        _ => phase == AgentPhase.Generating ? (5, 5) : (12, 12),
    };

    /// <summary>Where the badge's center sits in its row (points from the row's top-left); glass's first of three dots.</summary>
    public static (double X, double Y) BadgeCenter(BoardStyle style, bool sub, AgentPhase phase, double rowHeight) => style switch
    {
        BoardStyle.Timing => sub ? (37, rowHeight / 2) : (36.5, (rowHeight - 1) / 2),
        BoardStyle.Glass when phase == AgentPhase.Generating => (30.5, rowHeight / 2 + 0.5),
        _ => (35, rowHeight / 2),
    };

    /// <summary>Glass's dots stand this far apart.</summary>
    public const double DotSpacing = 4.5;

    /// <summary>
    /// Draws a row's badge from its layer at the row's (<paramref name="left"/>, <paramref name="top"/>): a generating badge
    /// pulses (<paramref name="phase"/>, seconds, desynchronizes rows; <paramref name="moving"/> false holds the resting frame),
    /// the others stand still.
    /// </summary>
    public static void EmitBadge(BoardDrawList list, in BoardLayerSlot slot, BoardStyle style, bool sub, AgentPhase badge, double left, double top,
        double height, double alpha, double now, double phase, bool moving, RectBounds clip)
    {
        ArgumentNullException.ThrowIfNull(list);
        var (cx, cy) = BadgeCenter(style, sub, badge, height);
        cx += left;
        cy += top;
        if (badge != AgentPhase.Generating)
        {
            Place(list, slot, cx, cy, 1, alpha, clip);
            return;
        }

        switch (style)
        {
            case BoardStyle.Neon:
            {
                // pulse 1 s: scale .65 and opacity .7 at 50 %.
                var u = moving ? Swell(now + phase, 1) : 0;
                Place(list, slot, cx, cy, 1 - 0.35 * u, alpha * (1 - 0.3 * u), clip);
                break;
            }

            case BoardStyle.Timing:
            {
                // stripe 1.1 s: opacity .45 at 50 %.
                var u = moving ? Swell(now + phase, 1.1) : 0;
                Place(list, slot, cx, cy, 1, alpha * (1 - 0.55 * u), clip);
                break;
            }

            default:
            {
                // dot 1.1 s, each 0.15 s after the one before: opacity .35 → 1 and up 1.5 pt at 40 %, back by 100 %; at rest .9.
                for (var i = 0; i < 3; i++)
                {
                    var lift = 0.0;
                    var opacity = 0.9;
                    if (moving)
                    {
                        var u = Bounce(now + phase - 0.15 * i, 1.1);
                        lift = 1.5 * u;
                        opacity = 0.35 + 0.65 * u;
                    }

                    Place(list, slot, cx + i * DotSpacing, cy - lift, 1, alpha * opacity, clip);
                }

                break;
            }
        }
    }

    /// <summary>The shimmer's cycle in seconds and its band's peak (glass .75 over 2.6 s, neon .85 over 2.2 s; timing has none).</summary>
    public static (double Seconds, double Peak) Shimmer(BoardStyle style) => style switch
    {
        BoardStyle.Neon => (2.2, 0.85),
        BoardStyle.Glass => (2.6, 0.75),
        _ => (0, 0),
    };

    /// <summary>
    /// The top-rung meter's shimmer over the row body <paramref name="body"/> drawn at (<paramref name="left"/>, <paramref name="top"/>):
    /// a 105° white band in the meter's box (inset −2 pt −3 pt, 300 % wide) crossing it in the first 35 % of the cycle.
    /// </summary>
    public static void EmitShimmer(BoardDrawList list, in BoardLayerSlot body, BoardStyle style, double left, double top, double height, double alpha,
        double now, double phase, RectBounds clip)
    {
        ArgumentNullException.ThrowIfNull(list);
        var (seconds, peak) = Shimmer(style);
        if (seconds <= 0 || !double.IsFinite(body.MeterX))
        {
            return;
        }

        var s = Fraction((now + phase) / seconds);
        if (s >= 0.35)
        {
            return;
        }

        var width = EffortMeterWidth + 6;
        var boxLeft = left + body.MeterX - 3;
        var mid = top + height / 2;

        // Only the meter's box of the body is sampled, cut on whole device pixels of the body so its texels stay registered.
        var scale = list.Scale > 0 ? list.Scale : 1;
        double Snap(double offset, double max) => Math.Clamp(Math.Round(offset * scale) / scale, 0, max);
        var x0 = Snap(boxLeft - left, body.Width);
        var x1 = Snap(boxLeft + width - left, body.Width);
        var y0 = Snap(mid - 7 - top, body.Height);
        var y1 = Snap(mid + 7 - top, body.Height);
        if (x1 - x0 < 1 || y1 - y0 < 1)
        {
            return;
        }

        var u = (body.U1 - body.U0) / body.Width;
        var v = (body.V1 - body.V0) / body.Height;
        var part = new BoardLayerSlot(body.U0 + (float)(x0 * u), body.V0 + (float)(y0 * v), body.U0 + (float)(x1 * u), body.V0 + (float)(y1 * v),
            x1 - x0, y1 - y0);

        // background-position 140 % → −60 %: the image's left edge at −2w × p; its band at the middle of a 3w image.
        var p = 1.4 - 2.0 * Ease(s / 0.35);
        var center = boxLeft - 2 * width * p + 1.5 * width;
        var line = 3 * width * Math.Sin(105 * Math.PI / 180) + 14 * Math.Abs(Math.Cos(105 * Math.PI / 180));
        var meter = new RectBounds((float)(left + x0), (float)(top + y0), (float)(left + x1), (float)(top + y1));
        var box = clip.Right > clip.Left
            ? new RectBounds(Math.Max(meter.Left, clip.Left), Math.Max(meter.Top, clip.Top), Math.Min(meter.Right, clip.Right), Math.Min(meter.Bottom, clip.Bottom))
            : meter;
        if (box.Right <= box.Left || box.Bottom <= box.Top)
        {
            return;
        }

        var instance = BoardLayerInstance.Create(part, left + x0, top + y0, alpha, clip: box);
        instance.Shimmer(center, 0.15 * line, peak);
        list.AddLayer(instance);
    }

    /// <summary>A row's own phase in seconds, so rows do not pulse in step.</summary>
    public static double PhaseOf(string key) => Seed(key) * 2.6;

    /// <summary>A stable fraction 0–1 of a row's key (FNV-1a; <c>string.GetHashCode</c> changes from run to run).</summary>
    public static double Seed(string? key)
    {
        var hash = 2166136261u;
        foreach (var c in key ?? string.Empty)
        {
            hash = (hash ^ c) * 16777619u;
        }

        return (hash & 0xFFFF) / 65536.0;
    }

    private const double EffortMeterWidth = 2.5 * 5 + 1.5 * 4;

    // A layer centered on (cx, cy), scaled about its center.
    private static void Place(BoardDrawList list, in BoardLayerSlot slot, double cx, double cy, double scale, double alpha, RectBounds clip)
    {
        if (alpha <= 0.002)
        {
            return;
        }

        var instance = BoardLayerInstance.Create(slot, cx - slot.Width * scale / 2, cy - slot.Height * scale / 2, alpha, clip: clip);
        instance.Rect.Z *= (float)scale;
        instance.Rect.W *= (float)scale;
        list.AddLayer(instance);
    }

    // CSS keyframes {50% {…}} with ease-in-out: 0 → 1 at half the cycle → 0.
    private static double Swell(double t, double seconds)
    {
        var s = Fraction(t / seconds);
        return s < 0.5 ? Ease(s / 0.5) : 1 - Ease((s - 0.5) / 0.5);
    }

    // CSS keyframes {0%, 100% {…} 40% {…}} with ease-in-out: 0 → 1 at 40 % → 0.
    private static double Bounce(double t, double seconds)
    {
        var s = Fraction(t / seconds);
        return s < 0.4 ? Ease(s / 0.4) : 1 - Ease((s - 0.4) / 0.6);
    }

    private static double Ease(double t) => Easing.CubicBezier(0.42, 0, 0.58, 1, Math.Clamp(t, 0, 1));

    private static double Fraction(double x) => x - Math.Floor(x);

    private static string[] BuildKeys(string variant)
    {
        var sources = Enum.GetValues<SourceKind>();
        var count = 0;
        foreach (var source in sources)
        {
            count = Math.Max(count, (int)source + 1);
        }

        var keys = new string[count * 3 * 2];
        for (var i = 0; i < keys.Length; i++)
        {
            var phase = (AgentPhase)(i / 2 % 3);
            keys[i] = "#badge" + variant + (i % 2 == 1 ? "s" : "g") + "gti"[(int)phase] + (i / 6).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return keys;
    }
}
