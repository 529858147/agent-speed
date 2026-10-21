using AgentSpeed.Core;
using AgentSpeed.Presentation.Gauge;
using AgentSpeed.Presentation.Heat;
using AgentSpeed.Presentation.Motion;
using AgentSpeed.Presentation.Particles;

namespace AgentSpeed.Presentation.Orb;

public readonly record struct Shockwave(double Progress, double Strength, double Heat);

public readonly record struct TierTransition(SpeedTier From, SpeedTier To, double Progress);

public enum FrameDemand
{
    None,
    Slow,
    Full,
}

/// <summary>
/// Platform-neutral animation state of the orb (DESIGN section 2). The renderer reads the properties after each
/// <see cref="Update"/>; geometry is in units of S unless stated otherwise.
/// </summary>
public sealed class OrbScene
{
    public const double PulseDuration = 0.7;
    public const double PulseCoalesce = 0.15;
    public const double PeakRayDuration = 0.9;
    public const double PeakSweepDuration = 0.5;
    public const double PeakPillDuration = 1.8;
    public const double TierTransitionDuration = 0.35;
    public const double FlashDuration = 0.4;
    public const double IdleSweepInterval = 6;
    public const double IdleSweepDuration = 1.6;
    public const double ReadoutMinInterval = 0.15;
    public const int MaxAgentDots = 6;
    public const int MaxPulses = 8;

    private readonly Random _random;
    private readonly List<PulseState> _pulses = new(MaxPulses);
    private readonly float[] _shimmer = new float[GaugeLayout.SegmentCount];
    private readonly List<SourceKind> _agents = new(MaxAgentDots);

    private CriticalSpring _heat = new(0.6);
    private CriticalSpring _overdrive = new(0.6);
    private CriticalSpring _cometVisibility = new(0.35);
    private CriticalSpring _redlineAmount = new(0.4);
    private CriticalSpring _skew = new(0.35);
    private CriticalSpring _hoverBlend = new(0.25);
    private DampedSpring _hover = DampedSpring.Layout();

    private double _lastUpdate = double.NaN;
    private double _now;
    private double _targetValue;
    private SpeedTier _tier = SpeedTier.Idle;
    private bool _hovering;
    private double _breathPhase;
    private double _cometPhase;
    private double _flare;
    private double _flashStart = double.NegativeInfinity;
    private double _transitionStart = double.NegativeInfinity;
    private SpeedTier _previousTier = SpeedTier.Idle;
    private double _lastPulseAt = double.NegativeInfinity;
    private double _peakStart = double.NegativeInfinity;
    private double _nextShimmerAt;
    private double _nextJitterAt;
    private double _idleSince;
    private double _sweepStart = double.NegativeInfinity;
    private string _pendingText = "0";
    private double _pendingValue;
    private double _shownValue;
    private double _lastTextChange = double.NegativeInfinity;
    private double _sparkRate;
    private double _emberRate;

    public OrbScene(int? seed = null)
    {
        _random = seed is null ? new Random() : new Random(seed.Value);
        Particles = new ParticleSystem(ParticleSystem.DefaultCapacity, seed);
        Array.Fill(_shimmer, 1f);
        Readout.Set("0", 1, 0, animate: false);
    }

    public EffectsPolicy Effects { get; set; } = new(Settings.EffectsIntensity.Standard);

    public double Redline { get; set; } = HeatMath.DefaultRedline;

    public bool PulsesEnabled { get; set; } = true;

    public bool PeaksEnabled { get; set; } = true;

    public ParticleSystem Particles { get; }

    public RollingText Readout { get; } = new();

    public double Time => _now;

    public double Value => _shownValue;

    public SpeedTier Tier => _tier;

    public double Heat => Math.Clamp(_heat.Value, 0, 1);

    public double Overdrive => Math.Clamp(_overdrive.Value, 0, 1);

    /// <summary>Gauge fill heat, including the new-peak sweep.</summary>
    public double GaugeHeat
    {
        get
        {
            var sweep = (_now - _peakStart) / PeakSweepDuration;
            return sweep is >= 0 and < 1 && Effects.PeakBurst ? Heat * Easing.OutCubic(sweep) : Heat;
        }
    }

    /// <summary>Aura opacity factor before the appearance token scale (0.55 dark / 0.32 light).</summary>
    public double AuraIntensity
    {
        get
        {
            if (!Effects.Aura)
            {
                return 0;
            }

            var breath = Effects.AuraBreathing ? 1 + 0.15 * Math.Sin(2 * Math.PI * _breathPhase) : 1;
            return Math.Clamp(Heat * breath + _flare * Math.Max(0.35, Heat), 0, 1.4);
        }
    }

    public double CometDegrees => 270 + 360 * _cometPhase;

    public double CometOpacity => Math.Clamp((0.25 + 0.6 * Heat) * _cometVisibility.Value, 0, 1);

    /// <summary>0..1 brightness boost after a tier change.</summary>
    public double Flash
    {
        get
        {
            var t = (_now - _flashStart) / FlashDuration;
            return t is >= 0 and < 1 ? 1 - Easing.OutQuad(t) : 0;
        }
    }

    public ReadOnlySpan<float> Shimmer => _shimmer;

    /// <summary>Pulsing glow on the outer edge of the arc at redline (alpha 0..1).</summary>
    public double EdgeGlow
    {
        get
        {
            var amount = Math.Clamp(_redlineAmount.Value, 0, 1);
            if (amount <= 0.001 || !Effects.AuraBreathing)
            {
                return amount * 0.25;
            }

            return amount * (0.3 + 0.2 * Math.Sin(2 * Math.PI * 2.2 * _now));
        }
    }

    public TierTransition Transition => new(_previousTier, _tier,
        Math.Clamp((_now - _transitionStart) / TierTransitionDuration, 0, 1));

    public IEnumerable<Shockwave> Pulses => _pulses.Select(p => new Shockwave(
        Math.Clamp((_now - p.Start) / PulseDuration, 0, 1), p.Strength, p.Heat));

    public int PulseCount => _pulses.Count;

    /// <summary>New-peak ray burst progress 0..1, or null when inactive.</summary>
    public double? PeakRays
    {
        get
        {
            var t = (_now - _peakStart) / PeakRayDuration;
            return t is >= 0 and < 1 && Effects.PeakBurst ? t : null;
        }
    }

    /// <summary>New-peak pill age in seconds, or null when hidden.</summary>
    public double? PeakPillAge
    {
        get
        {
            var age = _now - _peakStart;
            return age is >= 0 and < PeakPillDuration ? age : null;
        }
    }

    /// <summary>Idle light sweep position 0..1 along the arc, or null.</summary>
    public double? IdleSweep
    {
        get
        {
            if (_tier != SpeedTier.Idle || !Effects.IdleSweep)
            {
                return null;
            }

            var t = (_now - _sweepStart) / IdleSweepDuration;
            return t is >= 0 and < 1 ? t : null;
        }
    }

    public double JitterX { get; private set; }

    public double JitterY { get; private set; }

    public double SkewDegrees => _skew.Value;

    /// <summary>Hover spring 0..1 (may overshoot slightly).</summary>
    public double Hover => Effects.HoverScale ? _hover.Value : _hovering ? 1 : 0;

    /// <summary>0..1 crossfade from the tier label to the info line.</summary>
    public double HoverBlend => Math.Clamp(_hoverBlend.Value, 0, 1);

    public IReadOnlyList<SourceKind> AgentDots => _agents;

    public int AgentOverflow { get; private set; }

    public double SparkRate => _sparkRate;

    public double EmberRate => _emberRate;

    public void SetTarget(double value, SpeedTier tier)
    {
        _targetValue = double.IsFinite(value) && value > 0 ? value : 0;
        _pendingValue = _targetValue;
        _pendingText = SpeedFormat.Speed(_targetValue);

        if (tier != _tier)
        {
            _previousTier = _tier;
            _tier = tier;
            _transitionStart = _now;
            if (tier != SpeedTier.Idle || _previousTier != SpeedTier.Idle)
            {
                _flashStart = _now;
            }

            if (tier == SpeedTier.Idle)
            {
                _idleSince = _now;
                _sweepStart = double.NegativeInfinity;
            }
        }
    }

    public void SetAgents(IEnumerable<SourceKind> generating)
    {
        ArgumentNullException.ThrowIfNull(generating);
        _agents.Clear();
        var total = 0;
        foreach (var source in generating)
        {
            if (_agents.Count < MaxAgentDots)
            {
                _agents.Add(source);
            }

            total++;
        }

        AgentOverflow = Math.Max(0, total - MaxAgentDots);
    }

    public void SetHover(bool hovering) => _hovering = hovering;

    /// <returns>False when the pulse was coalesced into a recent one.</returns>
    public bool TriggerPulse(double speed, long outputTokens)
    {
        if (!PulsesEnabled || !Effects.Pulses)
        {
            return false;
        }

        var strength = Math.Clamp(Math.Log10(Math.Max(outputTokens, 1)) / 3, 0.3, 1.25);
        _flare = Math.Min(1, _flare + 0.35 * strength);
        if (_now - _lastPulseAt < PulseCoalesce)
        {
            return false;
        }

        _lastPulseAt = _now;
        if (_pulses.Count >= MaxPulses)
        {
            _pulses.RemoveAt(0);
        }

        _pulses.Add(new PulseState(_now, strength, HeatMath.Heat(speed, Redline)));
        return true;
    }

    public void TriggerPeak()
    {
        if (PeaksEnabled)
        {
            _peakStart = _now;
        }
    }

    public void Update(double nowSeconds)
    {
        var dt = double.IsNaN(_lastUpdate) ? 0 : nowSeconds - _lastUpdate;
        if (dt < 0 || dt > 0.25)
        {
            dt = 1 / 60.0;
        }

        if (double.IsNaN(_lastUpdate))
        {
            _idleSince = nowSeconds;
            _transitionStart = double.NegativeInfinity;
        }

        _lastUpdate = nowSeconds;
        _now = nowSeconds;

        var targetHeat = HeatMath.Heat(_targetValue, Redline);
        var targetOverdrive = HeatMath.Overdrive(_targetValue, Redline);
        _heat.Step(targetHeat, dt);
        _overdrive.Step(targetOverdrive, dt);

        var heat = Heat;
        _breathPhase = (_breathPhase + dt / Easing.Lerp(4, 0.7, heat)) % 1;
        _flare *= Math.Exp(-dt / 0.25);
        if (_flare < 0.002)
        {
            _flare = 0;
        }

        var cometOn = Effects.Comet && _tier != SpeedTier.Idle;
        _cometVisibility.Step(cometOn ? 1 : 0, dt);
        if (_cometVisibility.Value > 0.001)
        {
            _cometPhase = (_cometPhase + dt / Easing.Lerp(6, 0.7, heat)) % 1;
        }

        _redlineAmount.Step(_tier == SpeedTier.Redline ? 1 : 0, dt);
        _skew.Step(Effects.Skew && _tier >= SpeedTier.Overdrive ? -6 * heat : 0, dt);
        _hover.Step(_hovering ? 1 : 0, dt);
        _hoverBlend.Step(_hovering ? 1 : 0, dt);

        UpdateReadout();
        UpdateShimmer();
        UpdateJitter();
        UpdatePulses();
        UpdateIdleSweep();
        UpdateParticles(dt);
    }

    public FrameDemand Demand
    {
        get
        {
            var targetHeat = HeatMath.Heat(_targetValue, Redline);
            var full =
                !_heat.IsSettled(targetHeat, 0.0005)
                || !_overdrive.IsSettled(HeatMath.Overdrive(_targetValue, Redline), 0.0005)
                || (Effects.AuraBreathing && Heat > 0.004)
                || _cometVisibility.Value > 0.001 && (Effects.Comet || !_cometVisibility.IsSettled(0))
                || Particles.Count > 0 || _sparkRate > 0 || _emberRate > 0
                || _pulses.Count > 0 || _flare > 0
                || _now - _peakStart < Math.Max(PeakPillDuration, PeakRayDuration)
                || _now - _transitionStart < TierTransitionDuration
                || Flash > 0
                || Readout.IsAnimating(_now)
                || _pendingText != Readout.Current
                || !_hover.IsSettled(_hovering ? 1 : 0) || !_hoverBlend.IsSettled(_hovering ? 1 : 0)
                || _redlineAmount.Value > 0.001 && Effects.AuraBreathing
                || !_redlineAmount.IsSettled(_tier == SpeedTier.Redline ? 1 : 0)
                || !_skew.IsSettled(Effects.Skew && _tier >= SpeedTier.Overdrive ? -6 * targetHeat : 0, 0.01)
                || _agents.Count > 0 && Effects.AuraBreathing;
            if (full)
            {
                return FrameDemand.Full;
            }

            return IdleSweep is not null ? FrameDemand.Slow : FrameDemand.None;
        }
    }

    /// <summary>When nothing animates, the time of the next scheduled visual change (idle sweep).</summary>
    public double? NextWake
    {
        get
        {
            if (_tier != SpeedTier.Idle || !Effects.IdleSweep)
            {
                return null;
            }

            var last = double.IsNegativeInfinity(_sweepStart) ? _idleSince : _sweepStart;
            return last + IdleSweepInterval;
        }
    }

    private void UpdateReadout()
    {
        if (_pendingText == Readout.Current || _now - _lastTextChange < ReadoutMinInterval)
        {
            return;
        }

        var direction = _pendingValue >= _shownValue ? 1 : -1;
        Readout.Set(_pendingText, direction, _now, Effects.RollingDigits);
        _shownValue = _pendingValue;
        _lastTextChange = _now;
    }

    private void UpdateShimmer()
    {
        var active = Effects.Shimmer && _tier == SpeedTier.Redline;
        if (!active)
        {
            if (_shimmer[0] != 1f || _shimmer[^1] != 1f)
            {
                Array.Fill(_shimmer, 1f);
            }

            return;
        }

        if (_now < _nextShimmerAt)
        {
            return;
        }

        _nextShimmerAt = _now + 1 / 12.0;
        for (var i = 0; i < _shimmer.Length; i++)
        {
            _shimmer[i] = (float)(0.75 + 0.25 * _random.NextDouble());
        }
    }

    private void UpdateJitter()
    {
        if (!(Effects.Jitter && _tier == SpeedTier.Redline))
        {
            JitterX = 0;
            JitterY = 0;
            return;
        }

        if (_now < _nextJitterAt)
        {
            return;
        }

        _nextJitterAt = _now + 1 / 30.0;
        JitterX = (_random.NextDouble() * 2 - 1) * 0.6;
        JitterY = (_random.NextDouble() * 2 - 1) * 0.6;
    }

    private void UpdatePulses()
    {
        _pulses.RemoveAll(p => _now - p.Start >= PulseDuration);
    }

    private void UpdateIdleSweep()
    {
        if (_tier != SpeedTier.Idle || !Effects.IdleSweep)
        {
            return;
        }

        var last = double.IsNegativeInfinity(_sweepStart) ? _idleSince : _sweepStart;
        if (_now - last >= IdleSweepInterval)
        {
            _sweepStart = _now;
        }
    }

    private void UpdateParticles(double dt)
    {
        var heat = Heat;
        if (Effects.Particles)
        {
            _sparkRate = heat >= 0.45 ? 120 * ((heat - 0.45) / 0.55) * Effects.ParticleRate : 0;
            _emberRate = _tier == SpeedTier.Redline ? 90 * Math.Max(Overdrive, 0.3) * Effects.ParticleRate : 0;
        }
        else
        {
            _sparkRate = 0;
            _emberRate = 0;
        }

        var leading = GaugeLayout.AngleForFraction(GaugeHeat);
        Particles.Emit(new EmissionParameters(heat, Overdrive, _sparkRate, _emberRate, leading, GaugeLayout.StartDegrees), dt);
        Particles.Step(dt);
    }

    private readonly record struct PulseState(double Start, double Strength, double Heat);
}
