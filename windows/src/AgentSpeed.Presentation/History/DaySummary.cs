using AgentSpeed.Core;

namespace AgentSpeed.Presentation.History;

public readonly record struct SourceOutput(SourceKind Source, long Output);

/// <summary>
/// One calendar day of usage for the selected source, or summed over every source (METRICS 8). Speeds are always
/// Σ measured output ÷ Σ measured time, never an average of averages.
/// </summary>
/// <param name="Peak">Highest stored live peak of the day; only days observed live have one.</param>
/// <param name="Models">Every model of the day, most output first.</param>
/// <param name="Sources">Output per source (non-zero only), for stacking.</param>
public sealed record DaySummary(
    DateOnly Day,
    long Requests,
    long Input,
    long CachedInput,
    long CacheWriteInput,
    long Output,
    long Reasoning,
    long MeasuredOutput,
    long MeasuredMs,
    long Unfinalized,
    int Agents,
    double? Peak,
    IReadOnlyList<ModelUsage> Models,
    IReadOnlyList<SourceOutput> Sources)
{
    /// <summary>Output tok/s over the day's measured responses.</summary>
    public double? AverageSpeed => MeasuredMs > 0 ? MeasuredOutput / (MeasuredMs / 1000.0) : null;

    /// <summary>Share of input served from the cache (cachedInput ÷ input).</summary>
    public double? CacheHitRatio => Input > 0 ? Math.Clamp((double)CachedInput / Input, 0, 1) : null;

    public long OutputOf(SourceKind source)
    {
        foreach (var entry in Sources)
        {
            if (entry.Source == source)
            {
                return entry.Output;
            }
        }

        return 0;
    }
}

/// <summary>Totals of a range of <see cref="DaySummary"/> rows.</summary>
public sealed record HistoryTotals(
    long Requests,
    long Input,
    long CachedInput,
    long Output,
    long MeasuredOutput,
    long MeasuredMs,
    int ActiveDays,
    DaySummary? BusiestDay)
{
    public static HistoryTotals Empty { get; } = new(0, 0, 0, 0, 0, 0, 0, null);

    /// <summary>Σ measured output ÷ Σ measured time over the whole range.</summary>
    public double? AverageSpeed => MeasuredMs > 0 ? MeasuredOutput / (MeasuredMs / 1000.0) : null;

    public bool HasData => ActiveDays > 0;
}
