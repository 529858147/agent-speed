using AgentSpeed.Core;

namespace AgentSpeed.Presentation.History;

/// <summary>Turns stored per-source days (<see cref="DailyUsage"/>) into what the Usage History window shows.</summary>
public static class HistoryAggregator
{
    /// <summary>
    /// Days with data inside <paramref name="range"/> for one source (or every source when null), oldest first.
    /// Records with an unreadable day or without any usage are ignored.
    /// </summary>
    public static IReadOnlyList<DaySummary> Daily(IEnumerable<DailyUsage> usage, SourceKind? source, DayRange? range = null)
    {
        ArgumentNullException.ThrowIfNull(usage);
        var groups = new SortedDictionary<DateOnly, List<DailyUsage>>();
        foreach (var record in usage)
        {
            if (record is null || (source is { } only && record.Source != only) || !HasUsage(record)
                || !HistoryRanges.TryParseDay(record.Day, out var day) || (range is { } r && !r.Contains(day)))
            {
                continue;
            }

            if (!groups.TryGetValue(day, out var list))
            {
                list = [];
                groups[day] = list;
            }

            list.Add(record);
        }

        var result = new List<DaySummary>(groups.Count);
        foreach (var (day, records) in groups)
        {
            result.Add(Summarize(day, records));
        }

        return result;
    }

    public static HistoryTotals Totals(IEnumerable<DaySummary> days)
    {
        ArgumentNullException.ThrowIfNull(days);
        long requests = 0, input = 0, cached = 0, output = 0, measuredOutput = 0, measuredMs = 0;
        var active = 0;
        DaySummary? busiest = null;
        foreach (var day in days)
        {
            requests += day.Requests;
            input += day.Input;
            cached += day.CachedInput;
            output += day.Output;
            measuredOutput += day.MeasuredOutput;
            measuredMs += day.MeasuredMs;
            active++;
            if (busiest is null || day.Output > busiest.Output || (day.Output == busiest.Output && day.Day > busiest.Day))
            {
                busiest = day;
            }
        }

        return active == 0 ? HistoryTotals.Empty : new HistoryTotals(requests, input, cached, output, measuredOutput, measuredMs, active, busiest);
    }

    /// <summary>Sources with at least one day of usage, in <see cref="SourceKind"/> order.</summary>
    public static IReadOnlyList<SourceKind> SourcesWithHistory(IEnumerable<DailyUsage> usage)
    {
        ArgumentNullException.ThrowIfNull(usage);
        var found = new HashSet<SourceKind>();
        foreach (var record in usage)
        {
            if (record is not null && HasUsage(record))
            {
                found.Add(record.Source);
            }
        }

        return Enum.GetValues<SourceKind>().Where(found.Contains).ToArray();
    }

    /// <summary>First and last day with usage, or null when there is none.</summary>
    public static DayRange? Bounds(IEnumerable<DailyUsage> usage, SourceKind? source = null)
    {
        ArgumentNullException.ThrowIfNull(usage);
        DateOnly? first = null, last = null;
        foreach (var record in usage)
        {
            if (record is null || (source is { } only && record.Source != only) || !HasUsage(record)
                || !HistoryRanges.TryParseDay(record.Day, out var day))
            {
                continue;
            }

            if (first is null || day < first)
            {
                first = day;
            }

            if (last is null || day > last)
            {
                last = day;
            }
        }

        return first is { } f && last is { } l ? new DayRange(f, l) : null;
    }

    /// <summary>
    /// One value per day of <paramref name="range"/>: <paramref name="value"/> for days with data,
    /// <paramref name="missing"/> for the others.
    /// </summary>
    public static double?[] Series(IReadOnlyList<DaySummary> days, DayRange range, Func<DaySummary, double?> value, double? missing = 0)
    {
        ArgumentNullException.ThrowIfNull(days);
        ArgumentNullException.ThrowIfNull(value);
        var series = new double?[range.DayCount];
        Array.Fill(series, missing);
        foreach (var day in days)
        {
            if (range.Contains(day.Day))
            {
                series[range.IndexOf(day.Day)] = value(day);
            }
        }

        return series;
    }

    /// <summary>Relative change from <paramref name="previous"/>; null when there is nothing to compare with.</summary>
    public static double? Change(double? current, double? previous)
    {
        if (current is not { } now || previous is not { } before || !double.IsFinite(now) || !double.IsFinite(before) || before <= 0)
        {
            return null;
        }

        return (now - before) / before;
    }

    private static bool HasUsage(DailyUsage record) =>
        record.Totals.Requests > 0 || record.Totals.Output > 0 || record.Totals.Input > 0;

    private static DaySummary Summarize(DateOnly day, List<DailyUsage> records)
    {
        long requests = 0, input = 0, cached = 0, cacheWrite = 0, output = 0, reasoning = 0, measuredOutput = 0, measuredMs = 0, unfinalized = 0;
        var agents = 0;
        double? peak = null;
        var models = new Dictionary<string, (long Requests, long Output)>(StringComparer.Ordinal);
        var sources = new Dictionary<SourceKind, long>();
        foreach (var record in records)
        {
            var totals = record.Totals;
            requests += Math.Max(0, totals.Requests);
            input += Math.Max(0, totals.Input);
            cached += Math.Max(0, totals.CachedInput);
            cacheWrite += Math.Max(0, totals.CacheWriteInput);
            output += Math.Max(0, totals.Output);
            reasoning += Math.Max(0, totals.Reasoning);
            measuredOutput += Math.Max(0, record.MeasuredOutput);
            measuredMs += Math.Max(0, record.MeasuredMs);
            unfinalized += Math.Max(0, record.Unfinalized);
            agents += Math.Max(0, record.Agents);
            if (record.Peak is { } p && double.IsFinite(p) && p > 0 && (peak is null || p > peak))
            {
                peak = p;
            }

            sources[record.Source] = sources.GetValueOrDefault(record.Source) + Math.Max(0, totals.Output);
            foreach (var model in record.Models ?? [])
            {
                if (string.IsNullOrWhiteSpace(model.Model))
                {
                    continue;
                }

                var current = models.GetValueOrDefault(model.Model);
                models[model.Model] = (current.Requests + Math.Max(0, model.Requests), current.Output + Math.Max(0, model.Output));
            }
        }

        var modelList = models
            .Select(m => new ModelUsage(m.Key, m.Value.Requests, m.Value.Output))
            .OrderByDescending(m => m.Output)
            .ThenByDescending(m => m.Requests)
            .ThenBy(m => m.Model, StringComparer.Ordinal)
            .ToArray();
        var sourceList = sources
            .Where(s => s.Value > 0)
            .OrderBy(s => s.Key)
            .Select(s => new SourceOutput(s.Key, s.Value))
            .ToArray();
        return new DaySummary(day, requests, input, cached, cacheWrite, output, reasoning, measuredOutput, measuredMs, unfinalized,
            agents, peak, modelList, sourceList);
    }
}
