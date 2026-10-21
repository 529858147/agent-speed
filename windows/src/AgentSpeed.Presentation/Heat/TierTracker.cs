namespace AgentSpeed.Presentation.Heat;

/// <summary>
/// Tier state with hysteresis: a tier is entered at its threshold and left at 90 % of it, must hold
/// <see cref="HoldSeconds"/> to be shown, and idle needs the value below the cruise threshold for
/// <see cref="IdleSeconds"/>.
/// </summary>
public sealed class TierTracker
{
    public const double DefaultHoldSeconds = 0.8;
    public const double DefaultIdleSeconds = 3;
    public const double ExitRatio = 0.9;

    private double? _belowCruiseSince;
    private SpeedTier? _pendingTier;
    private double _pendingSince;

    public TierTracker(double holdSeconds = DefaultHoldSeconds, double idleSeconds = DefaultIdleSeconds)
    {
        HoldSeconds = holdSeconds;
        IdleSeconds = idleSeconds;
    }

    public double HoldSeconds { get; }

    public double IdleSeconds { get; }

    public SpeedTier Displayed { get; private set; } = SpeedTier.Idle;

    /// <summary>Tier after hysteresis, before the hold delay.</summary>
    public SpeedTier Candidate { get; private set; } = SpeedTier.Idle;

    /// <summary>Earliest time at which <see cref="Update"/> could change the displayed tier without new data.</summary>
    public double? NextDeadline
    {
        get
        {
            double? deadline = null;
            if (_pendingTier is not null)
            {
                deadline = _pendingSince + HoldFor(_pendingTier.Value);
            }

            if (_belowCruiseSince is { } since && Candidate != SpeedTier.Idle)
            {
                var idleAt = since + IdleSeconds;
                deadline = deadline is null ? idleAt : Math.Min(deadline.Value, idleAt);
            }

            return deadline;
        }
    }

    public void Reset(SpeedTier tier = SpeedTier.Idle)
    {
        Displayed = tier;
        Candidate = tier;
        _pendingTier = null;
        _belowCruiseSince = null;
    }

    /// <returns>True when <see cref="Displayed"/> changed.</returns>
    public bool Update(double value, double redline, double nowSeconds)
    {
        if (!double.IsFinite(value) || value < 0)
        {
            value = 0;
        }

        if (value < HeatMath.CruiseThreshold)
        {
            _belowCruiseSince ??= nowSeconds;
        }
        else
        {
            _belowCruiseSince = null;
        }

        Candidate = ComputeCandidate(value, redline, nowSeconds);

        if (Candidate == Displayed)
        {
            _pendingTier = null;
            return false;
        }

        if (_pendingTier != Candidate)
        {
            _pendingTier = Candidate;
            _pendingSince = nowSeconds;
        }

        if (nowSeconds - _pendingSince + 1e-9 >= HoldFor(Candidate))
        {
            Displayed = Candidate;
            _pendingTier = null;
            return true;
        }

        return false;
    }

    private double HoldFor(SpeedTier tier) => tier == SpeedTier.Idle ? 0 : HoldSeconds;

    private SpeedTier ComputeCandidate(double value, double redline, double nowSeconds)
    {
        if (value < HeatMath.CruiseThreshold)
        {
            var idle = _belowCruiseSince is { } since && nowSeconds - since + 1e-9 >= IdleSeconds;
            return idle || Candidate == SpeedTier.Idle ? SpeedTier.Idle : SpeedTier.Cruise;
        }

        var raw = HeatMath.RawTier(value, redline);
        if (raw >= Candidate)
        {
            return raw;
        }

        for (var tier = Candidate; tier > SpeedTier.Cruise; tier--)
        {
            if (value >= HeatMath.Threshold(tier, redline) * ExitRatio)
            {
                return tier;
            }
        }

        return SpeedTier.Cruise;
    }
}
