using System.Globalization;
using AgentSpeed.Core.Engine;
using AgentSpeed.Core.Monitoring;
using AgentSpeed.Core.Parsing;
using AgentSpeed.Core.Pricing;

namespace AgentSpeed.Core.History;

internal readonly record struct DayKey(string Day, SourceKind Source);

/// <summary>One model's sums within a day aggregate (METRICS 8).</summary>
/// <param name="Cost">Null when the contribution was stored before costs existed (METRICS 8, 9.7).</param>
internal readonly record struct ModelTotals(long Requests, long Output, long MeasuredOutput, long MeasuredMs, CostTally? Cost = null)
{
    public static ModelTotals operator +(ModelTotals a, ModelTotals b) => new(
        a.Requests + b.Requests,
        a.Output + b.Output,
        a.MeasuredOutput + b.MeasuredOutput,
        a.MeasuredMs + b.MeasuredMs,
        CostMerge.Merged(a.Cost, a.Requests, b.Cost, b.Requests));
}

internal static class CostMerge
{
    /// <summary>
    /// Sums two contributions. A part without cost data (stored before costs existed) adds its requests as unpriced,
    /// so a day mixing both shows a partial cost; only parts that all lack cost data stay without.
    /// </summary>
    public static CostTally? Merged(CostTally? a, long aRequests, CostTally? b, long bRequests)
    {
        if (aRequests == 0 && (a is null || a == CostTally.Zero))
        {
            return b;
        }

        if (bRequests == 0 && (b is null || b == CostTally.Zero))
        {
            return a;
        }

        return (a, b) switch
        {
            (null, null) => null,
            ({ } sum, null) => sum with { UnpricedRequests = sum.UnpricedRequests + bRequests },
            (null, { } sum) => sum with { UnpricedRequests = sum.UnpricedRequests + aRequests },
            ({ } x, { } y) => x + y,
        };
    }
}

/// <summary>Sums of one source's responses on one local day (METRICS 8), for one file or for all of them.</summary>
internal sealed class DayAggregate
{
    public long Requests { get; set; }

    public long Input { get; set; }

    public long CachedInput { get; set; }

    public long CacheWriteInput { get; set; }

    public long Output { get; set; }

    public long Reasoning { get; set; }

    public long MeasuredOutput { get; set; }

    public long MeasuredMs { get; set; }

    public long Unfinalized { get; set; }

    /// <summary>Distinct agents; a file is one agent, so per-file contributions carry 1 and totals are sums.</summary>
    public int Agents { get; set; }

    public Dictionary<string, ModelTotals> Models { get; } = new(StringComparer.Ordinal);

    /// <summary>Null when the contribution was stored before costs existed (METRICS 8, 9.7).</summary>
    public CostTally? Cost { get; set; }

    /// <summary>Some response has no known price (METRICS 9.7 re-analysis).</summary>
    public bool HasUnpriced => Cost is { UnpricedRequests: > 0 };

    /// <summary>Adds a response with its cost (null = unknown price).</summary>
    public void Add(ParsedResponse response, long? cost)
    {
        if (Cost is null && Requests == 0)
        {
            Cost = CostTally.Zero;
        }

        Cost = Cost?.Add(cost);
        var tokens = response.Tokens;
        Requests++;
        Input += tokens.Input;
        CachedInput += tokens.CachedInput;
        CacheWriteInput += tokens.CacheWriteInput;
        Output += tokens.Output;
        Reasoning += tokens.Reasoning;
        var model = response.Model ?? string.Empty;
        var totals = Models.TryGetValue(model, out var known) ? known : new ModelTotals(0, 0, 0, 0, CostTally.Zero);
        totals = totals with { Requests = totals.Requests + 1, Output = totals.Output + tokens.Output, Cost = totals.Cost?.Add(cost) };
        if (response.Final && SpeedMath.TryGetSpeedInterval(response, out var interval))
        {
            MeasuredOutput += tokens.Output;
            MeasuredMs += interval.DurationMs;
            totals = totals with
            {
                MeasuredOutput = totals.MeasuredOutput + tokens.Output,
                MeasuredMs = totals.MeasuredMs + interval.DurationMs,
            };
        }

        if (!response.Final)
        {
            Unfinalized++;
        }

        Models[model] = totals;
    }

    public void Merge(DayAggregate other)
    {
        Cost = CostMerge.Merged(Cost, Requests, other.Cost, other.Requests);
        Requests += other.Requests;
        Input += other.Input;
        CachedInput += other.CachedInput;
        CacheWriteInput += other.CacheWriteInput;
        Output += other.Output;
        Reasoning += other.Reasoning;
        MeasuredOutput += other.MeasuredOutput;
        MeasuredMs += other.MeasuredMs;
        Unfinalized += other.Unfinalized;
        Agents += other.Agents;
        foreach (var (model, usage) in other.Models)
        {
            Models[model] = Models.TryGetValue(model, out var known) ? known + usage : usage;
        }
    }

    /// <summary>Models ordered by output, largest first, then by name.</summary>
    public DailyUsage ToUsage(DayKey key, double? peak, int? priceVersion = null) => new(
        key.Day,
        key.Source,
        new TokenTotals(Requests, Input, CachedInput, CacheWriteInput, Output, Reasoning),
        MeasuredOutput,
        MeasuredMs,
        Unfinalized,
        Agents,
        peak,
        [.. Models
            .OrderByDescending(m => m.Value.Output)
            .ThenBy(m => m.Key, StringComparer.Ordinal)
            .Select(m => new ModelUsage(m.Key, m.Value.Requests, m.Value.Output, m.Value.MeasuredOutput, m.Value.MeasuredMs)
            {
                Cost = m.Value.Cost,
            })])
    {
        Cost = Cost,
        PriceVersion = Cost is null ? null : priceVersion,
    };

    /// <summary>
    /// One file's contributions: its responses grouped by local day of <c>endMs</c>, all under the file's source, each
    /// response priced with the table of its date (METRICS 9.7).
    /// </summary>
    public static Dictionary<DayKey, DayAggregate> Contributions(
        IEnumerable<ParsedResponse> responses, SourceKind source, DayLabeler labeler, PriceBook pricing)
    {
        var days = new Dictionary<DayKey, DayAggregate>();
        foreach (var response in responses)
        {
            if (labeler.Label(response.EndMs) is not { } day)
            {
                continue;
            }

            var key = new DayKey(day, source);
            if (!days.TryGetValue(key, out var aggregate))
            {
                days[key] = aggregate = new DayAggregate { Agents = 1 };
            }

            aggregate.Add(response, pricing.Cost(response, day));
        }

        return days;
    }
}

/// <summary>Local calendar dates (<c>YYYY-MM-DD</c>) of Unix milliseconds in one time zone, caching the last day.</summary>
internal sealed class DayLabeler(TimeZoneInfo zone)
{
    private const long MinMs = -62_135_596_800_000 + 2 * 86_400_000L;
    private const long MaxMs = 253_402_300_799_999 - 2 * 86_400_000L;
    private const long NextDayProbeMs = 36 * 3_600_000L;

    private long _start = 1;
    private long _end;
    private string _label = string.Empty;

    public TimeZoneInfo Zone { get; } = zone;

    public string? Label(long unixMs)
    {
        if (unixMs >= _start && unixMs < _end)
        {
            return _label;
        }

        if (unixMs is < MinMs or > MaxMs)
        {
            return null;
        }

        var local = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(unixMs), Zone);
        _label = local.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        _start = LocalDay.StartMs(unixMs, Zone);
        _end = LocalDay.StartMs(_start + NextDayProbeMs, Zone);
        if (unixMs < _start || unixMs >= _end)
        {
            _start = 1;
            _end = 0;
        }

        return _label;
    }
}
