using AgentSpeed.Presentation.Colors;
using AgentSpeed.Presentation.Gauge;
using AgentSpeed.Presentation.Orb.Styles;
using AgentSpeed.Presentation.Particles;

namespace AgentSpeed.Presentation.Orb.Gpu.Styles;

/// <summary>The moving parts of the <c>led</c> instrument: redline edge glow and heat haze, the comet, sparks and embers.</summary>
public sealed partial class LedDraw
{
    private const int HazeStrands = 9;
    private const int HazeSteps = 6;

    /// <summary>Particle colors per heat bucket, so a frame never mixes in OKLab: hot (low and high mix) and cool ends.</summary>
    private const int ParticleBuckets = 32;

    private static readonly double[] HazeSeeds = BuildHazeSeeds();

    private readonly Rgba[] _sparkHotLow = new Rgba[ParticleBuckets + 1];
    private readonly Rgba[] _sparkHotHigh = new Rgba[ParticleBuckets + 1];
    private readonly Rgba[] _sparkCool = new Rgba[ParticleBuckets + 1];
    private readonly Rgba[] _emberHotLow = new Rgba[ParticleBuckets + 1];
    private readonly Rgba[] _emberHotHigh = new Rgba[ParticleBuckets + 1];
    private readonly Rgba[] _emberCool = new Rgba[ParticleBuckets + 1];

    private void ConfigureEffects(OrbGpuContext context)
    {
        var hot = context.Dark ? HeatPalette.WhiteHot : HeatPalette.LightSparkHot;
        for (var i = 0; i <= ParticleBuckets; i++)
        {
            var h = i / (double)ParticleBuckets;
            var spark = HeatPalette.Sample(Math.Max(0.05, h));
            _sparkHotLow[i] = OkLab.Mix(spark, hot, context.Dark ? 0.35 : 0);
            _sparkHotHigh[i] = OkLab.Mix(spark, hot, context.Dark ? 0.9 : 0.35);
            _sparkCool[i] = context.Dark ? spark : OkLab.Darken(spark, 0.08);
            var ember = HeatPalette.Sample(h);
            _emberHotLow[i] = OkLab.Mix(ember, hot, 0.2);
            _emberHotHigh[i] = OkLab.Mix(ember, hot, 0.6);
            _emberCool[i] = HeatPalette.Sample(h - (context.Dark ? 0.08 : 0.04));
        }
    }

    /// <summary>
    /// The scene's eased redline amount (0..1), recovered from <see cref="OrbScene.EdgeGlow"/>, which is that amount times
    /// <c>0.3 + 0.2 sin(2π 2.2 t)</c> while the aura breathes and times 0.25 otherwise.
    /// </summary>
    internal static double RedlineAmount(OrbScene scene)
    {
        var glow = scene.EdgeGlow;
        if (glow <= 0)
        {
            return 0;
        }

        var wave = scene.Effects.AuraBreathing ? 0.3 + 0.2 * Math.Sin(2 * Math.PI * 2.2 * scene.Time) : 0.25;
        return StyleMath.Clamp01(glow / wave);
    }

    /// <summary>Max effects at redline: faint rising heat waves above the ring.</summary>
    private void AddHeatHaze(OrbDrawList list, OrbGpuContext ctx, in OrbFrame frame, double redline)
    {
        if (!frame.Effects.HeatDistortion || redline <= 0.01)
        {
            return;
        }

        var s = ctx.Size;
        var t = frame.Time;
        var color = _dark ? HeatPalette.WhiteHot : HeatPalette.LightSparkHot;
        var width = 1.4 * ctx.Unit;
        var top = ctx.CenterY - 0.74 * s;
        const double step = 1.0 / HazeSteps;
        for (var index = 0; index < HazeStrands; index++)
        {
            var seed = HazeSeeds[index];
            var offset = (index / (double)(HazeStrands - 1) - 0.5) * 0.62;
            var x0 = ctx.CenterX + offset * s;
            var bottom = ctx.CenterY - Math.Sqrt(Math.Max(0, 0.25 - offset * offset)) * s - 2;
            if (bottom <= top + 4)
            {
                continue;
            }

            var flicker = 0.5 + 0.5 * Math.Sin(t * (5 + seed * 3) + seed * 20);
            var alpha = (_dark ? 0.08 : 0.1) * redline * (0.4 + 0.6 * flicker);
            var previousX = x0;
            var previousY = bottom;
            for (var k = 1; k <= HazeSteps; k++)
            {
                var v = k * step;
                var y = bottom - (bottom - top) * v;
                var x = x0 + Math.Sin(t * (3.1 + seed * 2) + v * 9 + seed * 12) * 0.018 * s * v;
                var fade = 1 - (v - step);
                list.Add(OrbGpuInstance.Capsule(previousX, previousY, x, y, width, color.WithAlpha(alpha * fade),
                    color.WithAlpha(alpha * (fade + step))), ctx.GlowBlend);
                previousX = x;
                previousY = y;
            }
        }
    }

    /// <summary>Redline: a pulsing glow along the outer edge of the lit arc, brightest toward the leading edge.</summary>
    private void AddEdgeGlow(OrbDrawList list, OrbGpuContext ctx, OrbScene scene, in OrbFrame frame, double shown, double overdrive,
        double redline)
    {
        var alpha = scene.EdgeGlow * frame.Effects.Bloom;
        if (redline <= 0.01 || alpha <= 0.004 || shown <= 0.002)
        {
            return;
        }

        var heat = HeatPalette.Sample(frame.Heat);
        var color = _dark
            ? OkLab.Mix(heat, HeatPalette.WhiteHot, 0.7 + 0.3 * overdrive)
            : OkLab.Mix(heat, HeatPalette.LightSparkHot, 0.25 * overdrive);
        color = color.WithAlpha(alpha);
        var s = ctx.Size;
        var sweep = GaugeLayout.AngleForFraction(shown) - GaugeLayout.StartDegrees;
        list.Add(OrbGpuInstance.Arc(ctx.CenterX, ctx.CenterY, 0.447 * s, 0.012 * s, GaugeLayout.StartDegrees, sweep, color, color,
            glow: 0.02 * s, glowAlpha: 0.8), ctx.GlowBlend);
    }

    /// <summary>Dims the comet while it passes the gap at the bottom of the gauge.</summary>
    private static double CometGapFade(double degrees)
    {
        var delta = (degrees - 90) % 360;
        if (delta > 180)
        {
            delta -= 360;
        }
        else if (delta < -180)
        {
            delta += 360;
        }

        return 0.3 + 0.7 * StyleMath.SmoothStep(18, 52, Math.Abs(delta));
    }

    /// <summary>A 40° tapered arc (heat → white-hot head) travelling just outside the gauge, and its glowing head.</summary>
    private void AddComet(OrbDrawList list, OrbGpuContext ctx, OrbScene scene, in OrbFrame frame)
    {
        var opacity = scene.CometOpacity;
        if (opacity <= 0.003)
        {
            return;
        }

        var head = scene.CometDegrees;
        var alpha = opacity * CometGapFade(head);
        var s = ctx.Size;
        var bloom = frame.Effects.Bloom;
        var heat = ctx.Label(Math.Max(frame.Heat, 0.1));
        var hot = _dark ? HeatPalette.WhiteHot : heat;
        var radius = GaugeLayout.CometRadius * s;
        list.Add(OrbGpuInstance.Arc(ctx.CenterX, ctx.CenterY, radius, 0.0125 * s, head - 40, 40, hot.WithAlpha(alpha),
            heat.WithAlpha(alpha * 0.6), glow: 0.012 * s * bloom, glowAlpha: 0.55), ctx.GlowBlend);
        var (x, y) = ctx.Polar(head, GaugeLayout.CometRadius);
        list.Add(OrbGpuInstance.Circle(x, y, 0.0085 * s, hot.WithAlpha(alpha), glow: 0.022 * s * bloom, glowAlpha: 0.8), ctx.GlowBlend);
    }

    /// <summary>Sparks streak outward from the lit arc; embers rise and flicker; both fade behind the readout.</summary>
    private void AddParticles(OrbDrawList list, OrbGpuContext ctx, OrbScene scene, in OrbFrame frame)
    {
        var particles = scene.Particles.Particles;
        if (particles.IsEmpty)
        {
            return;
        }

        var s = ctx.Size;
        var size = ctx.Tokens.ParticleSizeScale * ctx.Unit * 0.5;
        var blend = ctx.GlowBlend;
        var t = frame.Time;
        for (var i = 0; i < particles.Length; i++)
        {
            ref readonly var p = ref particles[i];
            var life = (double)p.Progress;
            var bucket = (int)Math.Round(StyleMath.Clamp01(p.Heat) * ParticleBuckets);
            var x = ctx.CenterX + p.X * s;
            var y = ctx.CenterY + p.Y * s;
            var zone = ReadoutZone.Fade(p.X, p.Y);
            if (p.Kind == ParticleKind.Spark)
            {
                var alpha = Math.Pow(1 - life, 1.25) * zone;
                if (alpha <= 0.01)
                {
                    continue;
                }

                var hot = Rgba.Lerp(_sparkHotLow[bucket], _sparkHotHigh[bucket], p.Seed);
                var color = Rgba.Lerp(hot, _sparkCool[bucket], Math.Min(1, life * 1.3));
                var radius = p.Size * size * (1 - 0.45 * life);
                list.Add(OrbGpuInstance.Capsule(x - p.VelocityX * 0.045 * s, y - p.VelocityY * 0.045 * s, x, y, radius * 1.3,
                    color.WithAlpha(alpha), color.WithAlpha(alpha * 0.35), glow: _dark ? radius * 1.4 : 0, glowAlpha: 0.45), blend);
            }
            else
            {
                var flicker = 0.7 + 0.3 * Math.Sin(t * 23 + p.Seed * 50);
                var alpha = Math.Pow(Math.Sin(Math.PI * Math.Min(1, life)), 0.7) * flicker * zone;
                if (alpha <= 0.01)
                {
                    continue;
                }

                var hot = Rgba.Lerp(_emberHotLow[bucket], _emberHotHigh[bucket], p.Seed);
                var color = Rgba.Lerp(hot, _emberCool[bucket], Math.Min(1, life * 1.3));
                var radius = p.Size * size * (1 - 0.35 * life);
                list.Add(OrbGpuInstance.Circle(x, y, radius, color.WithAlpha(alpha), glow: radius * (_dark ? 1.9 : 1.2),
                    glowAlpha: _dark ? 0.5 : 0.3), blend);
            }
        }
    }

    private static double[] BuildHazeSeeds()
    {
        var seeds = new double[HazeStrands];
        for (var i = 0; i < seeds.Length; i++)
        {
            seeds[i] = Hash(i, 91);
        }

        return seeds;
    }

    /// <summary>Deterministic noise in 0..1 for a pair of keys (the macOS <c>Noise.hash</c>).</summary>
    private static double Hash(int a, int b)
    {
        var x = unchecked((ulong)((long)a * 73_856_093 ^ (long)b * 19_349_663));
        x ^= x >> 33;
        x = unchecked(x * 0xff51afd7ed558ccdUL);
        x ^= x >> 33;
        x = unchecked(x * 0xc4ceb9fe1a85ec53UL);
        x ^= x >> 33;
        return x % 10_000 / 10_000.0;
    }
}
