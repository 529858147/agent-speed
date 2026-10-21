using System.Globalization;
using AgentSpeed.Core.Engine;
using AgentSpeed.Core.Monitoring;
using AgentSpeed.Core.Parsing;

namespace AgentSpeed.Core.History;

internal readonly record struct DayKey(string Day, SourceKind Source);

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

    public Dictionary<string, (long Requests, long Output)> Models { get; } = new(StringComparer.Ordinal);

    public void Add(ParsedResponse response)
    {
        var tokens = response.Tokens;
        Requests++;
        Input += tokens.Input;
        CachedInput += tokens.CachedInput;
        CacheWriteInput += tokens.CacheWriteInput;
        Output += tokens.Output;
        Reasoning += tokens.Reasoning;
        if (response.Final && SpeedMath.TryGetSpeedInterval(response, out var interval))
        {
            MeasuredOutput += tokens.Output;
            MeasuredMs += interval.DurationMs;
        }

        if (!response.Final)
        {
            Unfinalized++;
        }

        var model = response.Model ?? string.Empty;
        var (requests, output) = Models.GetValueOrDefault(model);
        Models[model] = (requests + 1, output + tokens.Output);
    }

    public void Merge(DayAggregate other)
    {
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
            var (requests, output) = Models.GetValueOrDefault(model);
            Models[model] = (requests + usage.Requests, output + usage.Output);
        }
    }

    /// <summary>Models ordered by output, largest first, then by name.</summary>
    public DailyUsage ToUsage(DayKey key, double? peak) => new(
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
            .Select(m => new ModelUsage(m.Key, m.Value.Requests, m.Value.Output))]);

    /// <summary>One file's contributions: its responses grouped by local day of <c>endMs</c>, all under the file's source.</summary>
    public static Dictionary<DayKey, DayAggregate> Contributions(
        IEnumerable<ParsedResponse> responses, SourceKind source, DayLabeler labeler)
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

            aggregate.Add(response);
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
