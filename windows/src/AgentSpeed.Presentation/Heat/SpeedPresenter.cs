using AgentSpeed.Core;

namespace AgentSpeed.Presentation.Heat;

/// <summary>Turns hub snapshots into the value and tier every surface shows. Time is injected for testability.</summary>
public sealed class SpeedPresenter
{
    private readonly TierTracker _tracker;

    public SpeedPresenter(TierTracker? tracker = null)
    {
        _tracker = tracker ?? new TierTracker();
    }

    public SpeedMetric Metric { get; private set; } = SpeedMetric.Output;

    public double Redline { get; private set; } = HeatMath.DefaultRedline;

    public SpeedSnapshot Snapshot { get; private set; } = SpeedSnapshot.Empty;

    public double Value => Sanitize(Snapshot.Live.ValueFor(Metric));

    public SpeedTier Tier => _tracker.Displayed;

    public double Heat => HeatMath.Heat(Value, Redline);

    public double Overdrive => HeatMath.Overdrive(Value, Redline);

    public double? NextDeadline => _tracker.NextDeadline;

    /// <returns>True when the displayed tier changed.</returns>
    public bool Update(SpeedSnapshot snapshot, double nowSeconds)
    {
        Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        return _tracker.Update(Value, Redline, nowSeconds);
    }

    public bool Configure(SpeedMetric metric, double redline, double nowSeconds)
    {
        Metric = metric;
        Redline = HeatMath.ClampRedline(metric, redline);
        return _tracker.Update(Value, Redline, nowSeconds);
    }

    public bool Tick(double nowSeconds) => _tracker.Update(Value, Redline, nowSeconds);

    public double ValueFor(Rate rate) => Sanitize(rate.ValueFor(Metric));

    private static double Sanitize(double value) => double.IsFinite(value) && value > 0 ? value : 0;
}
