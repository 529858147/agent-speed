using AgentSpeed.Presentation.Boards.Effects;
using AgentSpeed.Presentation.Boards.Gpu;
using AgentSpeed.Presentation.Colors;
using AgentSpeed.Presentation.Motion;
using AgentSpeed.Presentation.Orb.Gpu;

namespace AgentSpeed.Presentation.Boards.Scene;

/// <summary>
/// Each style's ambience (DESIGN 2c Style ambience): glass's heat-tinted aurora drifting behind the hero (19 s and 23 s cycles),
/// timing's heat rail along the top edge and pulsing <c>boards.live</c> dot, neon's heat glow from the top-right corner and
/// breathing neon tube (3.2 s → 1 s with heat). Effects off and Reduce Motion keep the static parts and stop the motion;
/// high contrast drops the glows.
/// </summary>
public static class BoardAmbience
{
    private static readonly Rgba Live = Rgba.FromHex("#EF4444");

    private static readonly (double At, Rgba Color)[] Rail =
    [
        (0, Rgba.FromHex("#2DD4BF")), (0.4, Rgba.FromHex("#818CF8")), (0.75, Rgba.FromHex("#FB7185")), (1, Rgba.FromHex("#FACC15")),
    ];

    /// <summary>What lies behind the content.</summary>
    public static void EmitBack(BoardDrawList list, in BoardCanvas canvas, BoardStyle style, BoardPalette palette, double heat, double now,
        in BoardEffectsPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(palette);
        var moving = policy.Intensity > Settings.EffectsIntensity.Off && !policy.ReduceMotion;
        var m = BoardCanvas.Margin;
        var w = canvas.Side;
        list.Clip = BoardClip.Inside;
        switch (style)
        {
            case BoardStyle.Glass when policy.Intensity > Settings.EffectsIntensity.Off && !palette.HighContrast:
            {
                var alpha = 0.18 + heat * 0.62;
                var a = Drift(now, 19, moving);
                var b = Drift(now, 23, moving);
                var near = HeatPalette.Sample(Math.Max(0.06, heat));
                var far = HeatPalette.Sample(Math.Min(1, heat + 0.18));
                var r = 0.49 * w;
                list.Add(OrbGpuInstance.Radial(m + w * (0.29 + 0.18 * 0.98 * a), m + w * (0.205 + 0.08 * 0.99 * a), 0, (r + 34) * (1 + 0.15 * a),
                    near, alpha));
                list.Add(OrbGpuInstance.Radial(m + w * (0.78 - 0.14 * 0.98 * b), m + w * (0.095 + 0.12 * 0.99 * b), 0, (r + 34) * (1 - 0.08 * b),
                    far, alpha * 0.7));
                break;
            }

            case BoardStyle.Neon when !palette.HighContrast:
                // radial-gradient(120% 80% at 85% 0%, heat × 22 %, transparent 60%).
                list.Add(OrbGpuInstance.Radial(m + 0.85 * w, m, 0, 0.72 * w, HeatPalette.Sample(Math.Max(0.06, heat)), heat * 0.22));
                break;
        }

        list.Clip = BoardClip.None;
    }

    /// <summary>What lies above the content: the rail, the live dot's pulse and the tube.</summary>
    public static void EmitFront(BoardDrawList list, in BoardCanvas canvas, BoardStyle style, BoardPalette palette, BoardMetrics metrics,
        BoardHeroLayout hero, double heat, bool generating, double now, in BoardEffectsPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(palette);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(hero);
        var moving = policy.Intensity > Settings.EffectsIntensity.Off && !policy.ReduceMotion;
        var glows = !palette.HighContrast;
        var m = BoardCanvas.Margin;
        var w = canvas.Side;
        var color = HeatPalette.Sample(Math.Max(0.06, heat));
        list.Clip = BoardClip.Inside;
        switch (style)
        {
            case BoardStyle.Timing:
            {
                list.Add(OrbGpuInstance.RoundedRect(m + w / 2, m + 1.5, w / 2, 1.5, 0, 0, palette.Line));
                var lit = w * Math.Clamp(heat, 0, 1);
                if (lit > 0.5)
                {
                    if (glows)
                    {
                        list.Add(OrbGpuInstance.Capsule(m, m + 1.5, m + lit, m + 1.5, 3, color, null, 0, OrbGpuInstance.Glow(5), 0.7));
                    }

                    for (var i = 1; i < Rail.Length; i++)
                    {
                        var x0 = m + lit * Rail[i - 1].At;
                        var x1 = m + lit * Rail[i].At;
                        list.Add(OrbGpuInstance.Capsule(x0, m + 1.5, x1, m + 1.5, 3, Rail[i].Color, Rail[i - 1].Color));
                    }
                }

                if (generating && moving && glows)
                {
                    // box-shadow 0 → 7 px of rgba(239,68,68,.55) → 0, every 1.6 s.
                    var t = (now % 1.6) / 1.6;
                    var spread = 7 * Easing.OutCubic(t);
                    var cx = m + metrics.Pad + hero.LiveDot.CenterX;
                    var cy = m + metrics.HeroTop + hero.LiveDot.CenterY;
                    list.Clip = BoardClip.None;
                    list.Add(OrbGpuInstance.Ring(cx, cy, 3.5 + spread / 2, spread, Live.WithAlpha(0.55 * (1 - t))));
                }

                break;
            }

            case BoardStyle.Neon:
            {
                var breath = 3.2 - 2.2 * heat;
                var phase = moving ? Easing.InOutSine(Triangle(now / (breath / 2))) : 0;
                var opacity = 0.45 + 0.55 * heat + ((0.3 + 0.5 * heat) - (0.45 + 0.55 * heat)) * phase;
                var tube = OkLab.Mix(color, Rgba.White, 0.12).WithAlpha(0.85 * opacity);
                var c = canvas.Center;
                if (!glows)
                {
                    list.Add(OrbGpuInstance.RoundedRing(c, c, canvas.Half - 0.5, canvas.Half - 0.5, canvas.Radius - 0.5, 1, color));
                    break;
                }

                list.Add(OrbGpuInstance.RoundedRing(c, c, canvas.Half - 0.6, canvas.Half - 0.6, canvas.Radius - 0.6, 1.2, tube, 1,
                    OrbGpuInstance.Glow((6 + heat * 18) / 2), 0.55 * opacity));
                break;
            }
        }

        list.Clip = BoardClip.None;
    }

    /// <summary>Glass's lobes and neon's tube move while effects run: CSS <c>alternate</c> cycles of <paramref name="seconds"/>.</summary>
    private static double Drift(double now, double seconds, bool moving) => moving ? Easing.InOutSine(Triangle(now / seconds)) : 0;

    // 0 → 1 → 0 over two units (one unit each way).
    private static double Triangle(double x)
    {
        var f = x % 2;
        if (f < 0)
        {
            f += 2;
        }

        return f < 1 ? f : 2 - f;
    }
}
