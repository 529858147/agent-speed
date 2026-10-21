using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Media;
using System.Windows.Threading;
using AgentSpeed.Core;
using AgentSpeed.Presentation.Colors;
using AgentSpeed.Presentation.Gauge;
using AgentSpeed.Presentation.Heat;
using AgentSpeed.Presentation.Motion;
using AgentSpeed.Presentation.Orb;
using AgentSpeed.Presentation.Particles;
using AgentSpeed.Theme;

namespace AgentSpeed.Orb;

/// <summary>
/// The orb instrument (DESIGN section 2) drawn with retained DrawingVisual layers. Only layers that change are
/// re-recorded; frames run only while the scene animates, and never while the view is hidden.
/// </summary>
internal sealed class OrbView : FrameworkElement
{
    private static readonly Typeface ValueFace = new(new FontFamily("Segoe UI Variable Display, Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
    private static readonly Typeface UnitFace = new(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.Medium, FontStretches.Normal);
    private static readonly Typeface LabelFace = new(new FontFamily("Segoe UI Variable Text, Segoe UI, Microsoft JhengHei UI, Microsoft YaHei UI, Yu Gothic UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    private readonly ContainerVisual _root = new();
    private readonly DrawingVisual _aura = new();
    private readonly DrawingVisual _body = new();
    private readonly DrawingVisual _gauge = new();
    private readonly DrawingVisual _comet = new();
    private readonly DrawingVisual _particles = new();
    private readonly DrawingVisual _readout = new();
    private readonly DrawingVisual _overlay = new();
    private readonly ScaleTransform _hoverScale = new(1, 1);
    private readonly TextCache _text = new();
    private readonly FrameDriver _driver;
    private readonly Func<double> _clock;
    private readonly DispatcherTimer _accessibilityTimer;

    private OrbConfig _config = OrbConfig.Default;
    private OrbTokens _tokens = OrbTokens.Dark;
    private OrbBrushes _brushes;
    private CriticalSpring _valueWidth = new(0.25);
    private string _infoLine = string.Empty;
    private string _accessibleName = string.Empty;
    private string _pendingAccessibleName = string.Empty;
    private bool _bodyDirty = true;
    private bool _hasDrawnStatic;
    private bool _isActive = true;
    private double _lastReadoutTime;
    private bool _hovering;
    private SpeedTier _lastTier = (SpeedTier)(-1);
    private string _lastText = string.Empty;
    private double _lastValue = double.NaN;
    private SourceKind[] _lastAgents = [];
    private long _auraKey = long.MinValue;
    private long _gaugeKey = long.MinValue;
    private long _readoutKey = long.MinValue;
    private bool _cometEmpty;
    private bool _particlesEmpty;
    private bool _overlayEmpty;
    private double _lastScale = double.NaN;
    private double _valueWidthTarget;

    public OrbView(Func<double> clock)
    {
        _clock = clock;
        Scene = new OrbScene();
        _brushes = new OrbBrushes(_config.Diameter);
        _root.Children.Add(_aura);
        _root.Children.Add(_body);
        _root.Children.Add(_gauge);
        _root.Children.Add(_comet);
        _root.Children.Add(_particles);
        _root.Children.Add(_readout);
        _root.Children.Add(_overlay);
        _root.Transform = _hoverScale;
        AddVisualChild(_root);

        TextOptions.SetTextRenderingMode(_readout, TextRenderingMode.Grayscale);
        TextOptions.SetTextRenderingMode(_overlay, TextRenderingMode.Grayscale);

        _driver = new FrameDriver(Dispatcher, RenderFrame, Demand, () => Scene.NextWake, _clock);
        _accessibilityTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => FlushAccessibleName(), Dispatcher);
        _accessibilityTimer.Stop();

        IsVisibleChanged += (_, _) => UpdateActive();
        Loaded += (_, _) => UpdateActive();
        Unloaded += (_, _) => _driver.StopAll();
        Focusable = false;
        SnapsToDevicePixels = false;
    }

    public OrbScene Scene { get; }

    public double Diameter => _config.Diameter;

    /// <summary>Spoken text such as <c>每秒 128 個 token，加速</c>; updated at most once per second.</summary>
    public string AccessibleName => _accessibleName;

    /// <summary>External pause (for example, the window is cloaked on another virtual desktop).</summary>
    public bool Paused
    {
        get => !_isActive;
        set
        {
            _pausedExternally = value;
            UpdateActive();
        }
    }

    private bool _pausedExternally;

    public void Configure(OrbConfig config)
    {
        var sizeChanged = Math.Abs(config.Diameter - _config.Diameter) > 0.01;
        _config = config;
        _tokens = OrbTokens.For(config.Appearance);
        Scene.Effects = config.Effects;
        Scene.Redline = config.Redline;
        Scene.PulsesEnabled = config.PulsesEnabled;
        Scene.PeaksEnabled = config.PeaksEnabled;
        if (sizeChanged || _brushes.Diameter != config.Diameter)
        {
            _brushes = new OrbBrushes(config.Diameter);
            InvalidateMeasure();
        }

        _text.Clear();
        _bodyDirty = true;
        _hasDrawnStatic = false;
        InvalidateLayers();
        _driver.Kick();
    }

    private void InvalidateLayers()
    {
        _auraKey = long.MinValue;
        _gaugeKey = long.MinValue;
        _readoutKey = long.MinValue;
        _cometEmpty = false;
        _particlesEmpty = false;
        _overlayEmpty = false;
    }

    public void SetData(double value, SpeedTier tier, IEnumerable<SourceKind> generating, string accessibleName)
    {
        _pendingAccessibleName = accessibleName;
        if (_pendingAccessibleName != _accessibleName && !_accessibilityTimer.IsEnabled)
        {
            FlushAccessibleName();
            _accessibilityTimer.Start();
        }

        // Idle snapshots arrive twice a second; when nothing visible changed, do not wake the renderer.
        var agents = generating as IReadOnlyCollection<SourceKind> ?? generating.ToArray();
        var target = double.IsFinite(value) && value > 0 ? value : 0;
        var text = SpeedFormat.Speed(target);
        if (tier == _lastTier && text == _lastText && Math.Abs(target - _lastValue) <= 1e-6 * Math.Max(1, target)
            && agents.SequenceEqual(_lastAgents))
        {
            return;
        }

        _lastTier = tier;
        _lastText = text;
        _lastValue = target;
        _lastAgents = agents.ToArray();
        Scene.Update(_clock());
        Scene.SetTarget(value, tier);
        Scene.SetAgents(_lastAgents);
        _driver.Kick();
    }

    public void SetInfoLine(string text)
    {
        if (_infoLine == text)
        {
            return;
        }

        _infoLine = text;
        if (Scene.HoverBlend > 0.001 || _hovering)
        {
            _driver.Kick();
        }
    }

    public void SetHover(bool hovering)
    {
        if (_hovering == hovering)
        {
            return;
        }

        _hovering = hovering;
        Scene.Update(_clock());
        Scene.SetHover(hovering);
        _driver.Kick();
    }

    public void Pulse(double speed, long outputTokens)
    {
        Scene.Update(_clock());
        Scene.TriggerPulse(speed, outputTokens);
        _driver.Kick();
    }

    public void Peak()
    {
        Scene.Update(_clock());
        Scene.TriggerPeak();
        _driver.Kick();
    }

    protected override int VisualChildrenCount => 1;

    protected override Visual GetVisualChild(int index) =>
        index == 0 ? _root : throw new ArgumentOutOfRangeException(nameof(index));

    protected override Size MeasureOverride(Size availableSize) => new(Diameter * 1.5, Diameter * 1.5);

    protected override Size ArrangeOverride(Size finalSize) => new(Diameter * 1.5, Diameter * 1.5);

    protected override AutomationPeer OnCreateAutomationPeer() => new OrbAutomationPeer(this);

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        _text.PixelsPerDip = newDpi.PixelsPerDip;
        _hasDrawnStatic = false;
        _driver.Kick();
    }

    private void UpdateActive()
    {
        _isActive = IsVisible && !_pausedExternally;
        _driver.Enabled = _isActive;
    }

    private void FlushAccessibleName()
    {
        if (_pendingAccessibleName == _accessibleName)
        {
            _accessibilityTimer.Stop();
            return;
        }

        _accessibleName = _pendingAccessibleName;
        if (UIElementAutomationPeer.FromElement(this) is { } peer)
        {
            peer.RaisePropertyChangedEvent(AutomationElementIdentifiers.NameProperty, string.Empty, _accessibleName);
        }
    }

    private void RenderFrame()
    {
        _text.PixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        Scene.Update(_clock());

        var hover = Scene.Hover;
        var scale = Math.Round((1 + 0.03 * hover) * 2000) / 2000;
        if (scale != _lastScale)
        {
            var c = _brushes.Center;
            _hoverScale.CenterX = c.X;
            _hoverScale.CenterY = c.Y;
            _hoverScale.ScaleX = scale;
            _hoverScale.ScaleY = scale;
            _lastScale = scale;
        }

        if (_bodyDirty || !_hasDrawnStatic)
        {
            DrawBody();
            _bodyDirty = false;
            _hasDrawnStatic = true;
            InvalidateLayers();
        }

        // Each layer is re-recorded only when its inputs changed, so an idle frame touches (and dirties) nothing.
        var auraKey = AuraKey();
        if (auraKey != _auraKey)
        {
            _auraKey = auraKey;
            DrawAura();
        }

        var gaugeKey = GaugeKey();
        if (gaugeKey == long.MaxValue || gaugeKey != _gaugeKey)
        {
            _gaugeKey = gaugeKey;
            DrawGauge();
        }

        var cometEmpty = Scene.CometOpacity <= 0.005 || _config.HighContrast;
        if (!(cometEmpty && _cometEmpty))
        {
            _cometEmpty = cometEmpty;
            DrawComet();
        }

        var particlesEmpty = Scene.Particles.Count == 0;
        if (!(particlesEmpty && _particlesEmpty))
        {
            _particlesEmpty = particlesEmpty;
            DrawParticles();
        }

        var readoutKey = ReadoutKey();
        if (readoutKey == long.MaxValue || readoutKey != _readoutKey)
        {
            _readoutKey = readoutKey;
            DrawReadout();
        }

        var overlayEmpty = _config.HighContrast || (Scene.PulseCount == 0 && Scene.PeakRays is null);
        if (!(overlayEmpty && _overlayEmpty))
        {
            _overlayEmpty = overlayEmpty;
            DrawOverlay();
        }
    }

    private FrameDemand Demand() =>
        !_valueWidth.IsSettled(_valueWidthTarget, 0.05) ? FrameDemand.Full : Scene.Demand;

    private long AuraKey()
    {
        var intensity = Scene.AuraIntensity * _tokens.AuraOpacityScale;
        if (intensity <= 0.004 || _config.HighContrast)
        {
            return -1;
        }

        return HeatPalette.Bucket(Scene.Heat, 48) * 100_000L + (long)Math.Round(intensity * 1000);
    }

    /// <summary>Change key of the gauge layer; <see cref="long.MaxValue"/> means it animates every frame.</summary>
    private long GaugeKey()
    {
        var animated = (Scene.Effects.Shimmer && Scene.Tier == SpeedTier.Redline)
            || (Scene.Effects.HeatDistortion && Scene.Tier == SpeedTier.Redline)
            || Scene.Flash > 0
            || Scene.EdgeGlow > 0.01;
        if (animated)
        {
            return long.MaxValue;
        }

        var sweep = Scene.IdleSweep is { } p ? (long)Math.Round(p * 1000) + 1 : 0;
        return (long)Math.Round(Scene.GaugeHeat * 100_000) * 10_000 + sweep;
    }

    /// <summary>Change key of the readout layer; <see cref="long.MaxValue"/> means it animates every frame.</summary>
    private long ReadoutKey()
    {
        var now = Scene.Time;
        var animated = Scene.Readout.IsAnimating(now)
            || Scene.Transition.Progress < 1
            || Scene.PeakPillAge is not null
            || Scene.JitterX != 0 || Scene.JitterY != 0
            || (Scene.AgentDots.Count > 0 && Scene.Effects.AuraBreathing)
            || (Scene.HoverBlend > 0.001 && Scene.HoverBlend < 0.999)
            || !_valueWidth.IsSettled(_valueWidthTarget, 0.05);
        if (animated)
        {
            return long.MaxValue;
        }

        var hash = new HashCode();
        hash.Add(Scene.Readout.Current);
        hash.Add(Scene.Tier);
        hash.Add(Math.Round(Scene.Heat * 63));
        hash.Add(Math.Round(Scene.Overdrive * 63));
        hash.Add(Math.Round(Scene.SkewDegrees * 20));
        hash.Add(Scene.HoverBlend > 0.5);
        hash.Add(_infoLine);
        hash.Add(Scene.AgentDots.Count);
        hash.Add(Scene.AgentOverflow);
        foreach (var agent in Scene.AgentDots)
        {
            hash.Add(agent);
        }

        return hash.ToHashCode() & 0x7FFF_FFFFL;
    }

    private void DrawAura()
    {
        using var dc = _aura.RenderOpen();
        var intensity = Scene.AuraIntensity * _tokens.AuraOpacityScale;
        if (intensity <= 0.004 || _config.HighContrast)
        {
            return;
        }

        var s = Diameter;
        var c = _brushes.Center;
        dc.PushOpacity(Math.Clamp(intensity, 0, 1));
        dc.DrawEllipse(_brushes.Aura(Scene.Heat), null, c, s * 0.75, s * 0.75);
        dc.Pop();
    }

    private void DrawBody()
    {
        using var dc = _body.RenderOpen();
        var s = Diameter;
        var c = _brushes.Center;
        var r = s / 2;

        if (_config.HighContrast)
        {
            dc.DrawEllipse(SystemColors.WindowBrush, new Pen(SystemColors.WindowTextBrush, 1.5), c, r - 0.75, r - 0.75);
            return;
        }

        var t = _tokens;
        var shadowRadius = r + t.ShadowBlur * s;
        var shadow = new RadialGradientBrush
        {
            GradientStops =
            {
                new GradientStop(t.ShadowColor.ToColor(), 0),
                new GradientStop(t.ShadowColor.ToColor(), (r - t.ShadowBlur * s * 0.5) / shadowRadius),
                new GradientStop(t.ShadowColor.ToColor(0.45), r / shadowRadius),
                new GradientStop(t.ShadowColor.ToColor(0.12), (r + t.ShadowBlur * s * 0.5) / shadowRadius),
                new GradientStop(Colors.Transparent, 1),
            },
        };
        shadow.Freeze();
        dc.DrawEllipse(shadow, null, new Point(c.X, c.Y + t.ShadowOffset * s), shadowRadius, shadowRadius);

        var alpha = _config.SolidBody ? 1 : t.BodyAlpha * Math.Clamp(_config.BodyOpacity, 0.4, 1);
        var body = new LinearGradientBrush(t.BodyTop.ToColor(alpha), t.BodyBottom.ToColor(alpha), 90);
        body.Freeze();
        dc.DrawEllipse(body, null, c, r, r);

        var innerShadow = new RadialGradientBrush
        {
            Center = new Point(0.5, 0.56),
            GradientOrigin = new Point(0.5, 0.62),
            GradientStops =
            {
                new GradientStop(Colors.Transparent, 0),
                new GradientStop(Colors.Transparent, 0.78),
                new GradientStop(Color.FromArgb(t.Appearance == OrbAppearance.Dark ? (byte)70 : (byte)22, 0, 0, 0), 1),
            },
        };
        innerShadow.Freeze();
        dc.DrawEllipse(innerShadow, null, c, r, r);

        var lens = new RadialGradientBrush
        {
            GradientStops =
            {
                new GradientStop(Color.FromArgb(t.Appearance == OrbAppearance.Dark ? (byte)16 : (byte)90, 255, 255, 255), 0),
                new GradientStop(Colors.Transparent, 1),
            },
        };
        lens.Freeze();
        dc.DrawEllipse(lens, null, new Point(c.X - 0.17 * s, c.Y - 0.19 * s), 0.3 * s, 0.24 * s);

        var rim = new LinearGradientBrush(t.RimTop.ToColor(), t.RimTop.ToColor(0), 90);
        rim.Freeze();
        dc.DrawEllipse(null, MediaColors.FrozenPen(rim, 1), c, r - 0.5, r - 0.5);

        if (t.OuterStroke is { } outer)
        {
            dc.DrawEllipse(null, MediaColors.FrozenPen(outer.ToBrush(), 1), c, r + 0.5, r + 0.5);
        }
    }

    private void DrawGauge()
    {
        using var dc = _gauge.RenderOpen();
        var s = Diameter;
        var c = _brushes.Center;
        var heat = Scene.GaugeHeat;
        var flash = Scene.Flash;
        var shimmer = Scene.Shimmer;
        var sweep = Scene.IdleSweep;
        var hc = _config.HighContrast;
        var unlit = hc ? SystemColors.GrayTextColor.ToRgba().WithAlpha(0.6) : _tokens.UnlitSegment;
        var leading = GaugeLayout.LeadingIndex(heat);

        for (var i = 0; i < GaugeLayout.SegmentCount; i++)
        {
            var geometry = _brushes.Segments[i];
            var unlitAlpha = 1.0;
            if (sweep is { } p)
            {
                var position = i / (double)(GaugeLayout.SegmentCount - 1);
                var distance = Scene.Effects.SlideTransitions ? Math.Abs(position - (p * 1.3 - 0.15)) : 0;
                var strength = Scene.Effects.SlideTransitions ? Math.Exp(-distance * distance * 90) : Math.Sin(Math.PI * p) * 0.6;
                unlitAlpha += 1.6 * strength;
            }

            dc.DrawGeometry(_brushes.Solid.Get(unlit, unlitAlpha), null, geometry);

            var fill = GaugeLayout.SegmentFill(heat, i);
            if (fill <= 0.002)
            {
                continue;
            }

            Rgba color;
            if (hc)
            {
                color = SystemColors.HighlightColor.ToRgba();
            }
            else if (i == leading)
            {
                color = _tokens.LeadingSegment(heat);
            }
            else
            {
                color = HeatPalette.Sample(GaugeLayout.SegmentHeat(i));
                if (flash > 0)
                {
                    color = OkLab.Mix(color, HeatPalette.WhiteHot, 0.25 * flash);
                }
            }

            var brightness = fill * shimmer[i];
            dc.DrawGeometry(_brushes.Solid.Get(color, brightness), null, geometry);
        }

        if (!hc && leading >= 0)
        {
            var arc = GaugeLayout.Segment(leading);
            var center = OrbBrushes.Polar(c, arc.MidRadius * s, arc.MidDegrees);
            var bloomRadius = (GaugeLayout.SegmentLength * 0.5 + 0.03) * s * Scene.Effects.Bloom;
            var bloomColor = _tokens.Appearance == OrbAppearance.Dark
                ? OkLab.Mix(HeatPalette.Sample(Q(heat)), HeatPalette.WhiteHot, 0.35)
                : HeatPalette.Sample(Q(heat));
            dc.PushOpacity(Math.Clamp(0.55 + 0.45 * GaugeLayout.SegmentFill(heat, leading), 0, 1) * (0.6 + 0.4 * heat));
            dc.DrawEllipse(_brushes.Bloom(bloomColor), null, center, bloomRadius, bloomRadius);
            dc.Pop();
        }

        var edgeGlow = Scene.EdgeGlow;
        if (!hc && edgeGlow > 0.01 && heat > 0.02)
        {
            var end = GaugeLayout.AngleForFraction(heat);
            var glowColor = HeatPalette.Highlight(Q(heat), Q(Scene.Overdrive));
            var arc = OrbBrushes.Arc(c, (GaugeLayout.OuterRadius + 0.012) * s, GaugeLayout.StartDegrees, end);
            dc.DrawGeometry(null, MediaColors.FrozenPen(_brushes.Solid.Get(glowColor, edgeGlow * 0.35), 0.05 * s, PenLineCap.Round), arc);
            dc.DrawGeometry(null, MediaColors.FrozenPen(_brushes.Solid.Get(glowColor, edgeGlow * 0.6), 0.016 * s, PenLineCap.Round), arc);
        }

        foreach (var (_, fraction) in GaugeLayout.Ticks(_config.Redline))
        {
            var lit = heat >= fraction - 0.0001;
            var point = OrbBrushes.Polar(c, GaugeLayout.TickRadius * s, GaugeLayout.AngleForFraction(fraction));
            var color = hc
                ? SystemColors.WindowTextColor.ToRgba()
                : lit ? HeatPalette.Sample(fraction) : _tokens.SecondaryText;
            var radius = Math.Max(0.9, 0.0085 * s);
            dc.DrawEllipse(_brushes.Solid.Get(color, lit ? 0.95 : 0.55), null, point, radius, radius);
        }

        if (Scene.Effects.HeatDistortion && Scene.Tier == SpeedTier.Redline && !hc)
        {
            var t = Scene.Time;
            var color = HeatPalette.Highlight(Q(heat), Q(Scene.Overdrive));
            for (var k = 0; k < 3; k++)
            {
                var radius = (0.515 + 0.03 * k + 0.008 * Math.Sin(t * 5.3 + k * 2.1)) * s;
                dc.DrawEllipse(null, MediaColors.FrozenPen(_brushes.Solid.Get(color, 0.05 + 0.03 * Math.Sin(t * 3.7 + k)), 0.012 * s), c, radius, radius * (1 + 0.01 * Math.Sin(t * 4.1 + k)));
            }
        }
    }

    private void DrawComet()
    {
        using var dc = _comet.RenderOpen();
        var opacity = Scene.CometOpacity;
        if (opacity <= 0.005 || _config.HighContrast)
        {
            return;
        }

        var s = Diameter;
        var c = _brushes.Center;
        var head = Scene.CometDegrees;
        var heat = Q(Scene.Heat);
        var color = HeatPalette.Sample(heat);
        var hot = _tokens.Appearance == OrbAppearance.Dark ? HeatPalette.WhiteHot : OkLab.Darken(color, 0.1);
        const int pieces = 16;
        const double length = 40;
        var radius = GaugeLayout.CometRadius * s;

        for (var i = 0; i < pieces; i++)
        {
            var t0 = i / (double)pieces;
            var t1 = (i + 1) / (double)pieces;
            var a0 = head - length + length * t0;
            var a1 = head - length + length * t1 + 0.4;
            var gap = GapFade((a0 + a1) / 2);
            if (gap <= 0.01)
            {
                continue;
            }

            var mix = t1 * t1;
            var pieceColor = OkLab.Mix(color, hot, Math.Max(0, (t1 - 0.6) / 0.4));
            var alpha = opacity * Math.Pow(t1, 1.6) * gap;
            var thickness = (0.006 + 0.012 * mix) * s;
            dc.DrawGeometry(null, MediaColors.FrozenPen(_brushes.Solid.Get(pieceColor, alpha), thickness),
                OrbBrushes.Arc(c, radius, a0, a1));
        }

        var headGap = GapFade(head);
        if (headGap > 0.01)
        {
            var point = OrbBrushes.Polar(c, radius, head);
            var glow = 0.035 * s * Scene.Effects.Bloom;
            dc.PushOpacity(Math.Clamp(opacity * headGap, 0, 1));
            dc.DrawEllipse(_brushes.Bloom(hot), null, point, glow, glow);
            dc.Pop();
        }
    }

    /// <summary>Quantizes a 0..1 value so per-frame colors reuse cached brushes.</summary>
    private static double Q(double value) => Math.Round(Math.Clamp(value, 0, 1) * 63) / 63;

    private static double GapFade(double degrees)
    {
        var a = ((degrees % 360) + 360) % 360;
        var distance = Math.Abs(a - 90);
        return Easing.SmoothStep(38, 62, distance);
    }

    private void DrawParticles()
    {
        using var dc = _particles.RenderOpen();
        var particles = Scene.Particles.Particles;
        if (particles.Length == 0)
        {
            return;
        }

        var s = Diameter;
        var c = _brushes.Center;
        var dark = _tokens.Appearance == OrbAppearance.Dark;
        var sizeScale = _tokens.ParticleSizeScale * Math.Clamp(s / 148, 0.75, 1.4);
        foreach (ref readonly var p in particles)
        {
            var progress = p.Progress;
            var point = new Point(c.X + p.X * s, c.Y + p.Y * s);
            var color = ParticleColor(p, progress, dark);
            var alpha = p.Kind == ParticleKind.Spark
                ? Math.Pow(1 - progress, 1.3)
                : Math.Sin(Math.PI * Math.Min(1, progress * 1.15)) * 0.9;
            var radius = p.Size * 0.5 * sizeScale * (p.Kind == ParticleKind.Ember ? 1 - 0.35 * progress : 1);
            if (dark)
            {
                dc.DrawEllipse(_brushes.Solid.Get(color, alpha * 0.22), null, point, radius * 2.4, radius * 2.4);
            }

            dc.DrawEllipse(_brushes.Solid.Get(color, alpha), null, point, radius, radius);
        }
    }

    private Rgba ParticleColor(in Particle p, double progress, bool dark)
    {
        var heat = Math.Round(p.Heat * 23) / 23;
        var step = Math.Round(progress * 7) / 7;
        var baseColor = HeatPalette.Sample(heat);
        var hot = dark ? HeatPalette.WhiteHot : HeatPalette.LightSparkHot;
        return p.Kind == ParticleKind.Spark
            ? OkLab.Mix(hot, baseColor, Math.Min(1, step * 1.4))
            : OkLab.Mix(HeatPalette.Highlight(heat, 0.3), baseColor, step);
    }

    private void DrawReadout()
    {
        _text.TrimStyles();
        using var dc = _readout.RenderOpen();
        var s = Diameter;
        var c = _brushes.Center;
        var now = Scene.Time;
        var hc = _config.HighContrast;
        var idle = Scene.Tier == SpeedTier.Idle && Scene.Value <= 0;

        var valueColor = hc ? SystemColors.WindowTextColor.ToRgba() : idle ? _tokens.IdleValueText : _tokens.ValueText;
        var secondary = hc ? SystemColors.WindowTextColor.ToRgba() : _tokens.SecondaryText;
        var valueStyle = _text.Style(ValueFace, 0.24 * s, _brushes.Solid.Get(valueColor));
        var capHeight = (ValueFace.CapsHeight > 0 ? ValueFace.CapsHeight : 0.7) * 0.24 * s;
        var baseline = c.Y - 0.035 * s + capHeight / 2;

        var digitCell = 0.0;
        for (var d = '0'; d <= '9'; d++)
        {
            digitCell = Math.Max(digitCell, _text.Get(d.ToString(), valueStyle).WidthIncludingTrailingWhitespace);
        }

        var readout = Scene.Readout;
        var targetWidth = 0.0;
        foreach (var ch in readout.Current)
        {
            targetWidth += char.IsDigit(ch) ? digitCell : _text.Get(ch.ToString(), valueStyle).WidthIncludingTrailingWhitespace;
        }

        _valueWidthTarget = targetWidth;
        var dt = Math.Clamp(now - _lastReadoutTime, 0, 0.1);
        _lastReadoutTime = now;
        if (Scene.Effects.SlideTransitions && _valueWidth.Value > 0)
        {
            _valueWidth.Step(targetWidth, dt);
        }
        else
        {
            _valueWidth.Snap(targetWidth);
        }

        var right = c.X + _valueWidth.Value / 2;

        dc.PushTransform(new TranslateTransform(Scene.JitterX, Scene.JitterY));
        dc.PushTransform(new SkewTransform(Scene.SkewDegrees, 0, c.X, baseline - capHeight / 2));

        var glow = !hc && Scene.Effects.ValueGlow && Scene.Tier >= SpeedTier.Boost;
        var glowColor = HeatPalette.Highlight(Q(Scene.Heat), Q(Scene.Overdrive));
        // The roll is clipped to the digits' own line box so nothing ever crosses the unit or the pill.
        var clip = new RectangleGeometry(new Rect(c.X - 0.46 * s, baseline - capHeight - 0.02 * s, 0.92 * s, capHeight + 0.034 * s));
        clip.Freeze();
        var travel = capHeight * 1.12;
        var glowTint = glow ? glowColor : (Rgba?)null;

        var x = right;
        for (var slot = 0; slot < readout.SlotCount; slot++)
        {
            var info = readout.Slot(slot, now);
            var currentChar = info.Current;
            var cell = currentChar is { } cc
                ? char.IsDigit(cc) ? digitCell : _text.Get(cc.ToString(), valueStyle).WidthIncludingTrailingWhitespace
                : info.Previous is { } pc2 && char.IsDigit(pc2) ? digitCell : 0;
            x -= cell;

            if (!info.IsChanging)
            {
                if (currentChar is { } resting)
                {
                    DrawChar(dc, resting, valueStyle, x, cell, baseline, 1, digitCell, glowTint, s);
                }

                continue;
            }

            var eased = RollingText.Ease(info.Progress);
            dc.PushClip(clip);
            if (info.Previous is { } previous)
            {
                DrawChar(dc, previous, valueStyle, x, cell, baseline + travel * info.PreviousPosition(eased),
                    info.PreviousOpacity(eased), digitCell, glowTint, s);
            }

            if (currentChar is { } current)
            {
                DrawChar(dc, current, valueStyle, x, cell, baseline + travel * info.CurrentPosition(eased),
                    info.CurrentOpacity(eased), digitCell, glowTint, s);
            }

            dc.Pop();
        }

        dc.Pop();
        dc.Pop();

        var unitStyle = _text.Style(UnitFace, 0.072 * s, _brushes.Solid.Get(secondary));
        DrawTracked(dc, _config.UnitText, unitStyle, c.X, baseline + 0.105 * s, 0.5 * s / 148, 1);

        DrawTierLabel(dc, c, baseline + 0.19 * s, hc);
        DrawAgentDots(dc, c, s, hc);
        DrawPeakPill(dc, c, s, baseline - capHeight - 0.075 * s);
    }

    private void DrawChar(DrawingContext dc, char ch, int style, double x, double cell, double baseline, double alpha,
        double digitCell, Rgba? glow, double s)
    {
        if (alpha <= 0.01)
        {
            return;
        }

        var text = ch.ToString();
        var formatted = _text.Get(text, style);
        var offsetX = char.IsDigit(ch) ? (digitCell - formatted.WidthIncludingTrailingWhitespace) / 2 : (cell - formatted.WidthIncludingTrailingWhitespace) / 2;
        var origin = new Point(x + offsetX, baseline - formatted.Baseline);

        if (glow is { } g)
        {
            var outline = _text.Outline(text, style);
            dc.PushTransform(new TranslateTransform(origin.X, origin.Y));
            var radius = 0.035 * s * Scene.Effects.Bloom;
            dc.DrawGeometry(null, MediaColors.FrozenPen(_brushes.Solid.Get(g, 0.10 * alpha), radius * 1.6, PenLineCap.Round), outline);
            dc.DrawGeometry(null, MediaColors.FrozenPen(_brushes.Solid.Get(g, 0.16 * alpha), radius * 0.9, PenLineCap.Round), outline);
            dc.DrawGeometry(null, MediaColors.FrozenPen(_brushes.Solid.Get(g, 0.22 * alpha), radius * 0.35, PenLineCap.Round), outline);
            dc.Pop();
        }

        if (alpha < 0.999)
        {
            dc.PushOpacity(alpha);
        }

        dc.DrawText(formatted, origin);
        if (alpha < 0.999)
        {
            dc.Pop();
        }
    }

    /// <summary>Draws text centered on <paramref name="centerX"/> with extra letter spacing (tracking) in DIP.</summary>
    private void DrawTracked(DrawingContext dc, string text, int style, double centerX, double baseline, double tracking, double alpha)
    {
        if (string.IsNullOrEmpty(text) || alpha <= 0.01)
        {
            return;
        }

        var useTracking = tracking > 0.01 && IsLatin(text);
        double width;
        if (!useTracking)
        {
            var whole = _text.Get(text, style);
            width = whole.WidthIncludingTrailingWhitespace;
            if (alpha < 0.999)
            {
                dc.PushOpacity(alpha);
            }

            dc.DrawText(whole, new Point(centerX - width / 2, baseline - whole.Baseline));
            if (alpha < 0.999)
            {
                dc.Pop();
            }

            return;
        }

        width = 0;
        foreach (var ch in text)
        {
            width += _text.Get(ch.ToString(), style).WidthIncludingTrailingWhitespace + tracking;
        }

        width -= tracking;
        var x = centerX - width / 2;
        if (alpha < 0.999)
        {
            dc.PushOpacity(alpha);
        }

        foreach (var ch in text)
        {
            var glyph = _text.Get(ch.ToString(), style);
            dc.DrawText(glyph, new Point(x, baseline - glyph.Baseline));
            x += glyph.WidthIncludingTrailingWhitespace + tracking;
        }

        if (alpha < 0.999)
        {
            dc.Pop();
        }
    }

    private void DrawTierLabel(DrawingContext dc, Point c, double baseline, bool hc)
    {
        var s = Diameter;
        var size = 0.062 * s;
        var tracking = 1.6 * s / 148;
        var heat = Math.Round(Scene.Heat * 31) / 31;
        var labelColor = hc ? SystemColors.WindowTextColor.ToRgba()
            : Scene.Tier == SpeedTier.Idle ? _tokens.SecondaryText : _tokens.TierLabel(Math.Max(heat, 0.08));
        var style = _text.Style(LabelFace, size, _brushes.Solid.Get(labelColor));
        var hoverBlend = Scene.HoverBlend;
        var labelAlpha = 1 - hoverBlend;

        var transition = Scene.Transition;
        if (transition.Progress < 1 && transition.From != transition.To)
        {
            var t = Easing.OutCubic(transition.Progress);
            var rise = Scene.Effects.SlideTransitions ? size * 1.1 : 0;
            var fromColor = hc ? labelColor : transition.From == SpeedTier.Idle ? _tokens.SecondaryText : _tokens.TierLabel(Math.Max(heat, 0.08));
            var fromStyle = _text.Style(LabelFace, size, _brushes.Solid.Get(fromColor));
            DrawTracked(dc, _config.TierLabel(transition.From), fromStyle, c.X, baseline - rise * t, tracking, (1 - t) * labelAlpha);
            DrawTracked(dc, _config.TierLabel(transition.To), style, c.X, baseline + rise * (1 - t), tracking, t * labelAlpha);
        }
        else
        {
            DrawTracked(dc, _config.TierLabel(Scene.Tier), style, c.X, baseline, tracking, labelAlpha);
        }

        if (hoverBlend > 0.01 && !string.IsNullOrEmpty(_infoLine))
        {
            var secondary = hc ? SystemColors.WindowTextColor.ToRgba() : _tokens.SecondaryText;
            var infoStyle = _text.Style(LabelFace, size * 0.95, _brushes.Solid.Get(secondary));
            var info = _text.Get(_infoLine, infoStyle);
            var maxWidth = s * 0.7;
            if (info.WidthIncludingTrailingWhitespace > maxWidth)
            {
                info.MaxTextWidth = maxWidth;
                info.Trimming = TextTrimming.CharacterEllipsis;
                info.MaxLineCount = 1;
            }

            dc.PushOpacity(hoverBlend);
            var width = Math.Min(info.WidthIncludingTrailingWhitespace, maxWidth);
            dc.DrawText(info, new Point(c.X - width / 2, baseline - info.Baseline));
            dc.Pop();
        }
    }

    private void DrawAgentDots(DrawingContext dc, Point c, double s, bool hc)
    {
        var agents = Scene.AgentDots;
        if (agents.Count == 0)
        {
            return;
        }

        var unit = s / 148;
        var radius = Math.Max(1.5, 2 * unit);
        var spacing = 8 * unit;
        var overflow = Scene.AgentOverflow;
        var count = agents.Count + (overflow > 0 ? 1 : 0);
        var x = c.X - (count - 1) * spacing / 2;
        var y = c.Y + 0.355 * s;
        var pulse = Scene.Effects.AuraBreathing;
        for (var i = 0; i < agents.Count; i++)
        {
            var alpha = pulse ? 0.62 + 0.38 * (0.5 + 0.5 * Math.Sin(Scene.Time * 4.2 + i * 1.3)) : 0.9;
            var color = hc ? SystemColors.WindowTextColor.ToRgba() : HeatPalette.SourceColor(agents[i]);
            dc.DrawEllipse(_brushes.Solid.Get(color, alpha), null, new Point(x + i * spacing, y), radius, radius);
        }

        if (overflow > 0)
        {
            var secondary = hc ? SystemColors.WindowTextColor.ToRgba() : _tokens.SecondaryText;
            var style = _text.Style(UnitFace, Math.Max(7, 0.05 * s), _brushes.Solid.Get(secondary));
            var text = _text.Get("+" + overflow.ToString(System.Globalization.CultureInfo.InvariantCulture), style);
            dc.DrawText(text, new Point(x + agents.Count * spacing - radius, y - text.Height / 2));
        }
    }

    private void DrawPeakPill(DrawingContext dc, Point c, double s, double centerY)
    {
        if (Scene.PeakPillAge is not { } age || string.IsNullOrEmpty(_config.PeakText))
        {
            return;
        }

        var appear = Scene.Effects.SlideTransitions ? Easing.OutBack(Math.Min(1, age / 0.45), 2.2) : Math.Min(1, age / 0.2);
        var fade = age > OrbScene.PeakPillDuration - 0.3 ? (OrbScene.PeakPillDuration - age) / 0.3 : 1;
        var alpha = Math.Clamp(Math.Min(age / 0.12, 1) * fade, 0, 1);
        if (alpha <= 0.01)
        {
            return;
        }

        var style = _text.Style(LabelFace, 0.056 * s, _brushes.Solid.Get(Rgba.FromHex("#1A1300")));
        var text = _text.Get(_config.PeakText, style);
        var width = text.WidthIncludingTrailingWhitespace + 0.07 * s;
        var height = text.Height + 0.022 * s;
        var scale = Scene.Effects.SlideTransitions ? 0.6 + 0.4 * appear : 1;

        dc.PushOpacity(alpha);
        dc.PushTransform(new ScaleTransform(scale, scale, c.X, centerY));
        var rect = new Rect(c.X - width / 2, centerY - height / 2, width, height);
        dc.DrawRoundedRectangle(_brushes.Bloom(HeatPalette.Gold), null, Inflate(rect, 0.04 * s), height, height);
        dc.DrawRoundedRectangle(_brushes.Solid.Get(HeatPalette.Gold), null, rect, height / 2, height / 2);
        dc.DrawText(text, new Point(c.X - text.WidthIncludingTrailingWhitespace / 2, centerY - text.Height / 2));
        dc.Pop();
        dc.Pop();
    }

    private void DrawOverlay()
    {
        using var dc = _overlay.RenderOpen();
        if (_config.HighContrast)
        {
            return;
        }

        var s = Diameter;
        var c = _brushes.Center;
        var dark = _tokens.Appearance == OrbAppearance.Dark;
        foreach (var wave in Scene.Pulses)
        {
            var eased = Easing.OutCubic(wave.Progress);
            var radius = Scene.Effects.PulseExpands ? (0.5 + 0.24 * eased) * s : 0.51 * s;
            var strength = Math.Clamp(wave.Strength, 0.3, 1.25);
            var thickness = Math.Max(0.3, 2 * strength * (1 - eased));
            var alpha = 0.6 * (1 - eased) * Math.Min(1, strength);
            var color = dark ? OkLab.Mix(HeatPalette.Sample(wave.Heat), HeatPalette.WhiteHot, 0.15) : HeatPalette.Sample(wave.Heat);
            dc.DrawEllipse(null, MediaColors.FrozenPen(_brushes.Solid.Get(color, alpha), thickness), c, radius, radius);
        }

        if (Scene.PeakRays is { } t)
        {
            var reach = Scene.Effects.SlideTransitions ? Easing.OutCubic(t) : 1;
            var alpha = Math.Pow(1 - t, 1.5);
            var pen = MediaColors.FrozenPen(_brushes.Solid.Get(HeatPalette.Gold, alpha), Math.Max(1, 1.6 * s / 148), PenLineCap.Round);
            for (var i = 0; i < 24; i++)
            {
                var angle = i * 15 + 7.5;
                var inner = (0.505 + 0.02 * reach) * s;
                var outer = (0.52 + 0.2 * reach * (i % 2 == 0 ? 1 : 0.7)) * s;
                dc.DrawLine(pen, OrbBrushes.Polar(c, inner, angle), OrbBrushes.Polar(c, outer, angle));
            }
        }
    }

    private static Rect Inflate(Rect rect, double amount)
    {
        rect.Inflate(amount, amount);
        return rect;
    }

    private static bool IsLatin(string text)
    {
        foreach (var ch in text)
        {
            if (ch > 0x024F)
            {
                return false;
            }
        }

        return true;
    }

    private sealed class OrbAutomationPeer(OrbView owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override string GetNameCore() => ((OrbView)Owner).AccessibleName;

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;

        protected override string GetClassNameCore() => "AgentSpeedOrb";

        protected override bool IsContentElementCore() => true;

        protected override bool IsControlElementCore() => true;
    }
}
