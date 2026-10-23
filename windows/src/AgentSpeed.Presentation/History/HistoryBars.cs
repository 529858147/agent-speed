using AgentSpeed.Core;

namespace AgentSpeed.Presentation.History;

/// <summary>
/// What the daily chart draws: one stack of per-source output (or cost in display units) per day of a range (bottom
/// first) and the day's average speed (none for cost). Frames are keyed by date and source, so a transition can morph
/// one range, filter or metric into another.
/// </summary>
public sealed class HistoryBars
{
    private readonly double[][] _values;
    private readonly double?[] _speeds;
    private readonly bool[]? _present;

    /// <param name="present">Which days have data (a day without a bar can still have data); null: the days with a bar.</param>
    public HistoryBars(DayRange range, IReadOnlyList<SourceKind> order, double[][] values, double?[] speeds,
        HistoryChartMetric metric = HistoryChartMetric.Output, bool[]? present = null)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(speeds);
        if (values.Length != range.DayCount || speeds.Length != range.DayCount || (present is not null && present.Length != range.DayCount))
        {
            throw new ArgumentException("One value set and one speed per day of the range are required.", nameof(values));
        }

        Range = range;
        Order = order;
        Metric = metric;
        _values = values;
        _speeds = speeds;
        _present = present;
        var max = 0.0;
        var maxSpeed = 0.0;
        for (var i = 0; i < values.Length; i++)
        {
            max = Math.Max(max, Total(i));
            if (speeds[i] is { } speed && double.IsFinite(speed))
            {
                maxSpeed = Math.Max(maxSpeed, speed);
            }
        }

        Max = max;
        MaxSpeed = maxSpeed;
    }

    public DayRange Range { get; }

    /// <summary>Output tokens with the speed line, or cost in the display currency without it.</summary>
    public HistoryChartMetric Metric { get; }

    /// <summary>Stacking order, bottom first.</summary>
    public IReadOnlyList<SourceKind> Order { get; }

    public int Count => _values.Length;

    /// <summary>Largest daily total.</summary>
    public double Max { get; }

    /// <summary>Largest daily average speed (0 without measurements).</summary>
    public double MaxSpeed { get; }

    public bool HasSpeed => MaxSpeed > 0;

    /// <summary>Output per source of day <paramref name="index"/>, in <see cref="Order"/>.</summary>
    public IReadOnlyList<double> Values(int index) => _values[index];

    public double? Speed(int index) => _speeds[index];

    public IReadOnlyList<double?> Speeds => _speeds;

    public double Total(int index)
    {
        var sum = 0.0;
        foreach (var value in _values[index])
        {
            sum += value;
        }

        return sum;
    }

    /// <summary>Day <paramref name="index"/> has data (even when its bar is empty, as an unpriced day in the cost chart).</summary>
    public bool HasData(int index) => index >= 0 && index < _values.Length && (_present?[index] ?? Total(index) > 0);

    public double ValueOf(DateOnly day, SourceKind source)
    {
        if (!Range.Contains(day))
        {
            return 0;
        }

        for (var k = 0; k < Order.Count; k++)
        {
            if (Order[k] == source)
            {
                return _values[Range.IndexOf(day)][k];
            }
        }

        return 0;
    }

    public static HistoryBars Empty(DayRange range, HistoryChartMetric metric = HistoryChartMetric.Output) =>
        new(range, [], Enumerable.Range(0, range.DayCount).Select(_ => Array.Empty<double>()).ToArray(), new double?[range.DayCount], metric);

    /// <param name="days">Days with data (any order); days outside <paramref name="range"/> are ignored.</param>
    /// <param name="source">The filter: one bar of that source, or (null) a stack of every source.</param>
    /// <param name="money">Converts the cost metric to display units; required for <see cref="HistoryChartMetric.Cost"/>.</param>
    public static HistoryBars From(IReadOnlyList<DaySummary> days, DayRange range, SourceKind? source,
        HistoryChartMetric metric = HistoryChartMetric.Output, HistoryMoney? money = null)
    {
        ArgumentNullException.ThrowIfNull(days);
        var cost = metric == HistoryChartMetric.Cost;
        if (cost && money is null)
        {
            throw new ArgumentNullException(nameof(money), "The cost metric needs the display currency.");
        }

        var inRange = days.Where(d => range.Contains(d.Day)).ToArray();
        IReadOnlyList<SourceKind> order = source is { } only ? [only] : HistoryChartMath.StackOrder(inRange, metric);
        var values = new double[range.DayCount][];
        var speeds = new double?[range.DayCount];
        var present = new bool[range.DayCount];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = new double[order.Count];
        }

        foreach (var day in inRange)
        {
            var index = range.IndexOf(day.Day);
            present[index] = true;
            for (var k = 0; k < order.Count; k++)
            {
                values[index][k] = cost
                    ? money!.Value(source is null ? day.CostMicroUsdOf(order[k]) : day.Cost.MicroUsd)
                    : source is null ? day.OutputOf(order[k]) : day.Output;
            }

            speeds[index] = cost ? null : day.AverageSpeed;
        }

        return new HistoryBars(range, order, values, speeds, metric, present);
    }

    /// <summary>
    /// Every value times <paramref name="factor"/>: a frame of one metric expressed on another metric's scale, so a switch
    /// between output and cost morphs the bars' heights rather than their units.
    /// </summary>
    public HistoryBars Scaled(double factor)
    {
        var f = double.IsFinite(factor) && factor > 0 ? factor : 0;
        var values = _values.Select(row => row.Select(v => v * f).ToArray()).ToArray();
        return new HistoryBars(Range, Order, values, (double?[])_speeds.Clone(), Metric, (bool[]?)_present?.Clone());
    }

    /// <summary>
    /// The frame <paramref name="t"/> of the way from <paramref name="from"/> to <paramref name="to"/>: the target's days
    /// and speeds, every source of both frames (the leaving ones on top, shrinking away), values matched by date.
    /// <paramref name="t"/> may overshoot 1 (springs); values never go below 0.
    /// </summary>
    public static HistoryBars Blend(HistoryBars from, HistoryBars to, double t)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        var order = to.Order.Concat(from.Order.Where(s => !to.Order.Contains(s))).ToArray();
        var values = new double[to.Count][];
        for (var i = 0; i < values.Length; i++)
        {
            var day = to.Range.DayAt(i);
            var row = new double[order.Length];
            for (var k = 0; k < order.Length; k++)
            {
                var a = from.ValueOf(day, order[k]);
                var b = k < to.Order.Count ? to._values[i][k] : 0;
                row[k] = Math.Max(0, a + (b - a) * t);
            }

            values[i] = row;
        }

        return new HistoryBars(to.Range, order, values, (double?[])to._speeds.Clone(), to.Metric, (bool[]?)to._present?.Clone());
    }
}
