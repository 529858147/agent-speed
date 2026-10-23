using AgentSpeed.Core;
using AgentSpeed.Presentation.Colors;
using AgentSpeed.Presentation.Heat;
using AgentSpeed.Presentation.Motion;
using AgentSpeed.Presentation.Orb.Styles;

namespace AgentSpeed.Presentation.Orb.Gpu;

/// <summary>The visual configuration of one GPU orb.</summary>
/// <param name="Size">Diameter S in points.</param>
/// <param name="Redline">The orb's effective redline for the selected metric.</param>
/// <param name="Sources">Tracked sources for the source bezel; null for all sources (plain rim).</param>
/// <param name="Unavailable">Tracked sources that are disabled or not detected (dashed bezel arcs).</param>
public sealed record OrbGpuConfig(
    OrbStyle Style,
    double Size,
    EffectsPolicy Effects,
    bool PulsesEnabled,
    bool PeaksEnabled,
    double Redline,
    OrbReadoutStrings Strings,
    IReadOnlyList<SourceKind>? Sources,
    SourceSet Unavailable);

/// <summary>
/// One floating orb's instrument for the GPU (DESIGN 2, 2a): the shared scene, the style's draw builder (two during a style
/// crossfade), the readout and the frame demand. Platform-neutral: each <see cref="Render"/> fills <see cref="List"/>, which
/// the app encodes with Direct3D; the same code drives the deterministic capture harness and the tests.
/// </summary>
public sealed class OrbGpuOrb
{
    public const double CrossfadeSeconds = 0.25;

    private readonly uint _seed;
    private OrbGpuConfig? _config;
    private OrbGpuContext? _context;
    private IOrbStyleDraw? _style;
    private IOrbStyleDraw? _outgoing;
    private double _crossfadeStart = double.NegativeInfinity;
    private double _lastRender = double.NaN;
    private double _lastOutgoing = double.NaN;
    private bool _snapNext = true;
    private bool _unavailable;
    private CriticalSpring _highlight = new(0.15);
    private double _lastValue = double.NaN;
    private SpeedTier _lastTier = (SpeedTier)(-1);
    private SpeedTitle _lastTitle = (SpeedTitle)(-1);
    private string? _lastUnavailable;
    private readonly List<SourceKind> _lastAgents = new(8);

    public OrbGpuOrb(uint seed, IOrbGlyphs glyphs)
    {
        _seed = seed;
        Scene = new OrbScene((int)(seed & 0x7FFF_FFFF)) { SharedSignOfLife = true };
        Readout = new OrbReadoutDraw(glyphs);
    }

    public OrbScene Scene { get; }

    public OrbReadoutDraw Readout { get; }

    public OrbDrawList List { get; } = new();

    public OrbGpuConfig? Config => _config;

    public OrbGpuContext? Context => _context;

    public IOrbStyleDraw? Style => _style;

    public double Diameter => _config?.Size ?? 148;

    /// <summary>The frame class after the last render (DESIGN 2a frame table).</summary>
    public FrameClass Demand { get; private set; } = FrameClass.Fps60;

    /// <summary>Settings → 懸浮窗 rings this orb (DESIGN 2b).</summary>
    public bool Highlighted { get; set; }

    /// <summary>The accent color of the highlight ring.</summary>
    public Rgba Accent { get; set; } = Rgba.FromHex("#3B82F6");

    /// <summary>When static, the time (shared clock) of the next scheduled change.</summary>
    public double? NextWake => Scene.NextWake;

    public bool IsConfigured => _context is not null && _style is not null;

    /// <summary>The next frame shows the current state without catch-up animation (after a pause or on creation).</summary>
    public void SnapNext() => _snapNext = true;

    public void Configure(OrbGpuConfig config, OrbGpuContext context)
    {
        var previous = _config;
        _config = config;
        _context = context;
        Scene.Effects = config.Effects;
        Scene.Redline = config.Redline;
        Scene.PulsesEnabled = config.PulsesEnabled;
        Scene.PeaksEnabled = config.PeaksEnabled;
        Scene.LedEffects = config.Style == OrbStyle.Led;
        Scene.SignOfLifeDuration = config.Style switch
        {
            OrbStyle.Minimal => 0,
            OrbStyle.Liquid => LiquidModel.SignOfLifeDuration,
            _ => 0.9,
        };

        if (_style is null || _style.Style != config.Style)
        {
            var incoming = OrbStyleDraws.Create(config.Style, Scene, _seed);
            if (_style is not null && previous is not null && config.Effects.SlideTransitions)
            {
                _outgoing = _style;
                _lastOutgoing = _lastRender;
                _crossfadeStart = Scene.Time;
            }
            else
            {
                _outgoing = null;
            }

            _style = incoming;
            _snapNext = true;
        }

        _style.Configure(context);
        _outgoing?.Configure(context);
        Readout.Configure(context, config.Strings);
    }

    /// <summary>Updates the tracked sources' bezel; true when it changed.</summary>
    public bool SetIdentity(IReadOnlyList<SourceKind>? sources, SourceSet unavailable)
    {
        if (_config is null || (SameSources(_config.Sources, sources) && _config.Unavailable == unavailable))
        {
            return false;
        }

        _config = _config with { Sources = sources, Unavailable = unavailable };
        return true;
    }

    /// <param name="unavailableTitle">Every source is unavailable: the value reads an en dash and this title.</param>
    /// <returns>True when anything changed (the caller asks for a frame).</returns>
    public bool SetData(double now, double value, SpeedTier tier, SpeedTitle title, IReadOnlyList<SourceKind> generating, string infoLine,
        string? unavailableTitle)
    {
        var target = double.IsFinite(value) && value > 0 ? value : 0;
        if (target == _lastValue && tier == _lastTier && title == _lastTitle && infoLine == Readout.InfoLine
            && unavailableTitle == _lastUnavailable && SameAgents(generating))
        {
            return false;
        }

        _lastValue = target;
        _lastTier = tier;
        _lastTitle = title;
        _lastUnavailable = unavailableTitle;
        _lastAgents.Clear();
        _lastAgents.AddRange(generating);
        Scene.Update(Math.Max(Scene.Time, now));
        var unavailable = unavailableTitle is not null;
        Scene.SetTarget(unavailable ? 0 : value, unavailable ? SpeedTier.Idle : tier, unavailable ? SpeedTitle.Idle : title);
        Scene.SetAgents(unavailable ? [] : generating);
        Readout.InfoLine = unavailable ? string.Empty : infoLine;
        Readout.UnavailableTitle = unavailableTitle;
        _unavailable = unavailable;
        return true;
    }

    public void SetHover(double now, bool hovering)
    {
        Scene.Update(Math.Max(Scene.Time, now));
        Scene.SetHover(hovering);
    }

    public void Pulse(double now, double speed, long outputTokens)
    {
        Scene.Update(Math.Max(Scene.Time, now));
        if (Scene.PulsesEnabled && Scene.Effects.Pulses)
        {
            var strength = Math.Clamp(Math.Log10(Math.Max(outputTokens, 1)) / 3, 0.3, 1.25);
            _style?.Model.Pulse(HeatMath.Heat(speed, Scene.Redline), strength);
        }

        Scene.TriggerPulse(speed, outputTokens);
    }

    public void Peak(double now)
    {
        Scene.Update(Math.Max(Scene.Time, now));
        if (Scene.PeaksEnabled)
        {
            _style?.Model.Peak();
        }

        Scene.TriggerPeak();
    }

    /// <summary>Advances everything to <paramref name="now"/> and fills <see cref="List"/> with the frame.</summary>
    /// <param name="contentScale">Extra scale about the disc center (appear and vanish), times the hover scale.</param>
    /// <param name="opacity">Opacity of the whole orb (the unavailable dim, the idle fade, appear and vanish).</param>
    public void Render(double now, double contentScale = 1, double opacity = 1)
    {
        var list = List;
        if (_context is not { } ctx || _style is not { } style || _config is not { } config)
        {
            list.Reset();
            return;
        }

        var dt = double.IsNaN(_lastRender) ? 0 : Math.Clamp(now - _lastRender, 0, 0.1);
        _lastRender = now;
        var snap = _snapNext;
        if (snap)
        {
            Scene.Snap(now);
        }
        else
        {
            Scene.Update(now);
        }

        var frame = Frame(now, dt, config);
        if (snap)
        {
            style.Model.Snap(frame);
            _snapNext = false;
        }
        else
        {
            style.Model.Update(frame);
        }

        Readout.Step(Scene, dt, snap);
        _highlight.Step(Highlighted ? 1 : 0, dt > 0 ? dt : 1 / 60.0);

        var mix = 1.0;
        var outgoingFrame = frame;
        if (_outgoing is { } outgoing)
        {
            mix = Math.Clamp((Scene.Time - _crossfadeStart) / CrossfadeSeconds, 0, 1);
            if (mix >= 1)
            {
                _outgoing = null;
            }
            else
            {
                var outDt = double.IsNaN(_lastOutgoing) ? 0 : Math.Clamp(now - _lastOutgoing, 0, 0.1);
                _lastOutgoing = now;
                outgoingFrame = Frame(now, outDt, config);
                outgoing.Model.Update(outgoingFrame);
            }
        }

        list.Reset();
        list.Canvas = ctx.Canvas;
        list.Scale = ctx.Scale;
        list.CenterX = ctx.CenterX;
        list.CenterY = ctx.CenterY;
        list.ContentScale = contentScale * Math.Round((1 + 0.03 * Scene.Hover) * 2000) / 2000;
        list.Opacity = Math.Clamp(opacity, 0, 1);
        var thumbnail = ctx.Size < 64;
        var crossfading = _outgoing is not null;

        if (!thumbnail)
        {
            if (crossfading && _outgoing!.SharedEffects)
            {
                list.AlphaScale = (float)(1 - mix);
                OrbSharedDraw.Aura(list, ctx, Scene, frame.Unavailable ? 0 : Scene.Heat);
            }

            if (style.SharedEffects)
            {
                list.AlphaScale = (float)mix;
                OrbSharedDraw.Aura(list, ctx, Scene, frame.Unavailable ? 0 : Scene.Heat);
            }
        }

        list.AlphaScale = 1;
        OrbSharedDraw.Body(list, ctx, style.Body, config.Sources is { Count: > 0 });
        if (crossfading)
        {
            Instrument(list, ctx, _outgoing!, outgoingFrame, 1 - mix, thumbnail);
        }

        Instrument(list, ctx, style, frame, mix, thumbnail);
        list.AlphaScale = 1;
        OrbSharedDraw.Bezel(list, ctx, config.Sources, config.Unavailable);
        if (crossfading)
        {
            Labels(list, ctx, _outgoing!, now, 1 - mix);
        }

        Labels(list, ctx, style, now, mix);
        list.AlphaScale = 1;
        OrbSharedDraw.Highlight(list, ctx, Accent, _highlight.Value);

        var demand = FramePolicy.Max(Scene.SharedFrameClass(config.Style == OrbStyle.Minimal), style.Model.Demand);
        if (crossfading || !_highlight.IsSettled(Highlighted ? 1 : 0, 0.01))
        {
            demand = FramePolicy.Max(demand, FrameClass.Fps60);
        }

        if (Readout.IsMoving(Scene))
        {
            demand = FramePolicy.Max(demand, FrameClass.Fps30);
        }

        Demand = demand;
    }

    private void Instrument(OrbDrawList list, OrbGpuContext ctx, IOrbStyleDraw style, in OrbFrame frame, double alpha, bool thumbnail)
    {
        list.AlphaScale = (float)alpha;
        if (alpha <= 0.002)
        {
            return;
        }

        if (style.SharedEffects && !thumbnail)
        {
            OrbSharedDraw.Pulses(list, ctx, Scene);
            OrbSharedDraw.PeakRays(list, ctx, Scene);
        }

        style.Build(list, Scene, frame);
        list.ClipRadius = 0;
        list.ClipFeather = 0;
    }

    private void Labels(OrbDrawList list, OrbGpuContext ctx, IOrbStyleDraw style, double now, double alpha)
    {
        list.AlphaScale = (float)alpha;
        if (alpha <= 0.002)
        {
            return;
        }

        OrbSharedDraw.AgentDots(list, ctx, Scene, style.Readout, now, _unavailable);
        var standard = OrbReadoutColors.Standard;
        if (style.BelowSurface is { } below && list.HasLiquid)
        {
            Readout.Add(list, Scene, style.Readout, standard, OrbTextClip.Liquid(below: false), pill: true);
            Readout.Add(list, Scene, style.Readout, below, OrbTextClip.Liquid(below: true), pill: false);
        }
        else
        {
            Readout.Add(list, Scene, style.Readout, standard, OrbTextClip.None, pill: true);
        }
    }

    private OrbFrame Frame(double now, double dt, OrbGpuConfig config) => new(
        now,
        dt,
        Scene.Heat,
        Scene.Overdrive,
        Scene.HeatRaw,
        Scene.OverdriveRaw,
        Scene.Tier,
        config.Size,
        config.Effects,
        _context?.Dark ?? true,
        Scene.Flash,
        _unavailable ? null : Scene.SignOfLifeAge,
        _unavailable);

    private bool SameAgents(IReadOnlyList<SourceKind> generating)
    {
        if (generating.Count != _lastAgents.Count)
        {
            return false;
        }

        for (var i = 0; i < generating.Count; i++)
        {
            if (generating[i] != _lastAgents[i])
            {
                return false;
            }
        }

        return true;
    }

    private static bool SameSources(IReadOnlyList<SourceKind>? a, IReadOnlyList<SourceKind>? b)
    {
        if (a is null || b is null)
        {
            return a is null && b is null;
        }

        if (a.Count != b.Count)
        {
            return false;
        }

        for (var i = 0; i < a.Count; i++)
        {
            if (a[i] != b[i])
            {
                return false;
            }
        }

        return true;
    }
}
