using AgentSpeed.Presentation.Colors;
using AgentSpeed.Presentation.Gauge;
using AgentSpeed.Presentation.Heat;
using AgentSpeed.Presentation.Orb.Styles;
using AgentSpeed.Presentation.Particles;

namespace AgentSpeed.Presentation.Orb.Gpu.Styles;

/// <summary>
/// The <c>led</c> instrument on the GPU (DESIGN 2 layers 3–5, 2a.1), the macOS look on the Windows simulation of
/// <see cref="OrbScene"/>: 36 rounded segments lit in the heat color of their own position with a bloom and a white-hot
/// leading edge, the tier tick dots, the redline edge glow and heat haze, the comet, sparks and embers.
/// </summary>
public sealed partial class LedDraw : IOrbStyleDraw
{
    /// <summary>
    /// Most instances one frame can add: the fixed layers (≤ 140 with the Max heat haze) plus the whole particle pool. A Max
    /// redline frame adds about 470 in practice.
    /// </summary>
    internal const int Budget = 140 + ParticleSystem.DefaultCapacity;

    private const int Segments = GaugeLayout.SegmentCount;
    private const double LastSegment = Segments - 1;

    private static readonly SpeedTier[] TickTiers = [SpeedTier.Cruise, SpeedTier.Boost, SpeedTier.Overdrive, SpeedTier.Redline];

    private readonly double[] _segmentX = new double[Segments];
    private readonly double[] _segmentY = new double[Segments];
    private readonly double[] _segmentAngle = new double[Segments];
    private readonly Rgba[] _segmentColor = new Rgba[Segments];
    private readonly double[] _tickX = new double[TickTiers.Length];
    private readonly double[] _tickY = new double[TickTiers.Length];
    private readonly double[] _tickFraction = new double[TickTiers.Length];

    private OrbGpuContext? _ctx;
    private double _tickRedline = double.NaN;
    private double _halfLength;
    private double _halfWidth;
    private double _corner;
    private bool _dark = true;

    public LedDraw(OrbScene scene)
    {
        Model = new LedModel(scene);
    }

    public OrbStyle Style => OrbStyle.Led;

    public IOrbStyleModel Model { get; }

    public OrbReadoutLayout Readout => OrbReadoutLayout.Led;

    public OrbBodyLook Body => OrbBodyLook.Standard;

    public bool SharedEffects => true;

    public OrbReadoutColors? BelowSurface => null;

    public void Configure(OrbGpuContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _ctx = context;
        _dark = context.Dark;
        var s = context.Size;
        _halfLength = GaugeLayout.SegmentLength * s / 2;
        _halfWidth = GaugeLayout.Radius * s * GaugeLayout.SegmentSpanDegrees * Math.PI / 180 / 2;
        _corner = Math.Min(2 * _halfLength, 2 * _halfWidth) * 0.24;
        for (var i = 0; i < Segments; i++)
        {
            var angle = GaugeLayout.Segment(i).MidDegrees;
            (_segmentX[i], _segmentY[i]) = context.Polar(angle, GaugeLayout.Radius);
            _segmentAngle[i] = angle;
            var color = HeatPalette.Sample(Math.Max(0.07, i / LastSegment));
            _segmentColor[i] = _dark ? color : OkLab.Darken(color, 0.06);
        }

        _tickRedline = double.NaN;
        ConfigureEffects(context);
    }

    public void Build(OrbDrawList list, OrbScene scene, in OrbFrame frame)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(scene);
        if (_ctx is not { } ctx)
        {
            return;
        }

        var plain = frame.Thumbnail || ctx.HighContrast;
        var shown = frame.Unavailable ? 0 : StyleMath.Clamp01(scene.GaugeHeat);
        var overdrive = frame.Unavailable ? 0 : StyleMath.Clamp01(frame.Overdrive);
        var lit = shown * Segments;
        var darkLeading = _dark ? OkLab.Mix(HeatPalette.WhiteHot, Rgba.White, 0.4 * overdrive) : default;
        if (!plain)
        {
            var redline = RedlineAmount(scene);
            AddHeatHaze(list, ctx, frame, redline);
            AddBloom(list, ctx, scene, frame, lit, overdrive, darkLeading);
            AddEdgeGlow(list, ctx, scene, frame, shown, overdrive, redline);
        }

        AddSegments(list, ctx, scene, frame, lit, overdrive, darkLeading);
        if (frame.Thumbnail)
        {
            return;
        }

        AddTicks(list, ctx, scene.Redline, shown);
        if (!ctx.HighContrast)
        {
            if (!frame.Unavailable)
            {
                AddComet(list, ctx, scene, frame);
            }

            AddParticles(list, ctx, scene, frame);
        }
    }

    /// <summary>Brightness of a segment (the last lit one partially) and its share of white-hot near the leading edge.</summary>
    private static (double Brightness, double Whiteness) Light(int index, double lit)
    {
        var brightness = Math.Clamp(lit - index, 0, 1);
        if (brightness <= 0)
        {
            return (0, 0);
        }

        var distance = lit - (index + 1);
        return (brightness, distance < 0 ? 1 : Math.Clamp(1 - distance, 0, 1));
    }

    /// <summary>The leading-segment color (DESIGN 2 token): dark white-hot toward white with overdrive, light heat toward amber.</summary>
    private Rgba Leading(int index, double overdrive, Rgba darkLeading) =>
        _dark ? darkLeading : OkLab.Mix(HeatPalette.Sample(index / LastSegment), HeatPalette.LightSparkHot, 0.2 + 0.3 * overdrive);

    private OrbGpuInstance Segment(int index, Rgba color, double core = 1, double glow = 0, double glowAlpha = 0) =>
        OrbGpuInstance.RoundedRect(_segmentX[index], _segmentY[index], _halfLength, _halfWidth, _segmentAngle[index], _corner, color,
            core, glow, glowAlpha);

    /// <summary>The bloom under the lit segments, white-hot at the leading edge.</summary>
    private void AddBloom(OrbDrawList list, OrbGpuContext ctx, OrbScene scene, in OrbFrame frame, double lit, double overdrive,
        Rgba darkLeading)
    {
        var bloom = frame.Effects.Bloom;
        var glowRadius = 0.02 * ctx.Size * bloom;
        var shimmer = scene.Shimmer;
        var flash = 1 + 0.5 * frame.Flash;
        var floor = _dark ? 0.3 : 0.2;
        for (var i = 0; i < Segments; i++)
        {
            var (brightness, whiteness) = Light(i, lit);
            if (brightness <= 0.01)
            {
                continue;
            }

            var color = _segmentColor[i];
            if (whiteness > 0)
            {
                color = OkLab.Mix(color, Leading(i, overdrive, darkLeading), whiteness * 0.8);
            }

            var alpha = brightness * (floor + 0.45 * whiteness) * shimmer[i] * flash;
            list.Add(Segment(i, color.WithAlpha(Math.Min(1, alpha * bloom)), core: 0, glow: glowRadius * (1 + 0.7 * whiteness),
                glowAlpha: 0.6), ctx.GlowBlend);
        }
    }

    /// <summary>The idle sign of life: a band of light travelling along the unlit segments.</summary>
    private double SweepBoost(int index, double? sweep, bool reduceMotion)
    {
        if (sweep is not { } u)
        {
            return 0;
        }

        if (reduceMotion)
        {
            return (_dark ? 0.1 : 0.06) * Math.Sin(Math.PI * u);
        }

        var d = index / LastSegment - (u * 1.3 - 0.15);
        return (_dark ? 0.16 : 0.1) * Math.Exp(-d * d * 90);
    }

    /// <summary>The crisp segments: unlit slate (lifted by the sweep) under the lit heat colors.</summary>
    private void AddSegments(OrbDrawList list, OrbGpuContext ctx, OrbScene scene, in OrbFrame frame, double lit, double overdrive,
        Rgba darkLeading)
    {
        var hc = ctx.HighContrast;
        var unlit = hc ? ctx.Contrast.GrayText.WithAlpha(0.6) : ctx.Tokens.UnlitSegment;
        var sweep = frame.Unavailable ? null : scene.IdleSweep;
        var reduceMotion = frame.Effects.ReduceMotion;
        var shimmer = scene.Shimmer;
        var flash = StyleMath.Clamp01(frame.Flash);
        for (var i = 0; i < Segments; i++)
        {
            var (brightness, whiteness) = Light(i, lit);
            if (brightness < 1)
            {
                var boost = SweepBoost(i, sweep, reduceMotion);
                list.Add(Segment(i, boost > 0 ? unlit.WithAlpha(unlit.A + boost) : unlit));
            }

            if (brightness <= 0)
            {
                continue;
            }

            Rgba color;
            if (hc)
            {
                color = ctx.Contrast.Highlight;
            }
            else
            {
                color = Scaled(_segmentColor[i], shimmer[i]);
                if (whiteness > 0)
                {
                    color = OkLab.Mix(color, Leading(i, overdrive, darkLeading), whiteness);
                }

                if (flash > 0)
                {
                    color = Rgba.Lerp(color, Rgba.White, 0.25 * flash);
                }
            }

            list.Add(Segment(i, color.WithAlpha(brightness)));
        }
    }

    /// <summary>Four tick dots just outside the arc at the tier thresholds, in the heat color once passed.</summary>
    private void AddTicks(OrbDrawList list, OrbGpuContext ctx, double redline, double shown)
    {
        if (redline != _tickRedline)
        {
            _tickRedline = redline;
            for (var i = 0; i < TickTiers.Length; i++)
            {
                var fraction = HeatMath.HeatAtThreshold(TickTiers[i], redline);
                _tickFraction[i] = fraction;
                (_tickX[i], _tickY[i]) = ctx.Polar(GaugeLayout.AngleForFraction(fraction), GaugeLayout.TickRadius);
            }
        }

        var hc = ctx.HighContrast;
        var idle = hc ? ctx.Contrast.WindowText.WithAlpha(0.55) : _dark ? Rgba.White.WithAlpha(0.26) : Rgba.Black.WithAlpha(0.22);
        var radius = 0.0085 * ctx.Size;
        for (var i = 0; i < TickTiers.Length; i++)
        {
            var fraction = _tickFraction[i];
            var passed = shown >= fraction;
            var color = !passed ? idle : hc ? ctx.Contrast.WindowText.WithAlpha(0.95) : ctx.Label(Math.Max(fraction, 0.1));
            var glow = passed && _dark && !hc ? 1.5 * ctx.Unit : 0;
            list.Add(OrbGpuInstance.Circle(_tickX[i], _tickY[i], radius, color, glow: glow, glowAlpha: 0.5));
        }
    }

    private static Rgba Scaled(Rgba color, double factor) => factor >= 1
        ? color
        : new Rgba(Math.Clamp(color.R * factor, 0, 1), Math.Clamp(color.G * factor, 0, 1), Math.Clamp(color.B * factor, 0, 1), color.A);
}
