using AgentSpeed.Core;

namespace AgentSpeed.Presentation.History;

public enum BreakdownKind
{
    /// <summary>One bar per source (all sources selected).</summary>
    Sources,

    /// <summary>One bar per model of the selected source.</summary>
    Models,
}

/// <summary>One bar of a day's breakdown.</summary>
/// <param name="Model">The model name; null on a source bar and on <paramref name="OtherModels"/>.</param>
/// <param name="Share">Output as a share (0..1) of the day's output; the bars' shares add up to 1.</param>
/// <param name="Fraction">Output relative to the largest bar (0..1), the bar's length.</param>
/// <param name="Speed">Σ measured output ÷ Σ measured time (per source, or per model, METRICS 8); null when nothing was measured.</param>
/// <param name="OtherModels">Output no named model accounts for (responses without a model, days stored before per-model time).</param>
public sealed record BreakdownRow(
    SourceKind Source,
    string? Model,
    long Output,
    long Requests,
    double Share,
    double Fraction,
    double? Speed,
    bool OtherModels = false)
{
    /// <summary>Stable identity of the bar while its values change.</summary>
    public string Key => OtherModels ? "\0other" : Model ?? Source.ToString();
}

/// <summary>
/// What a single-day range shows instead of the daily chart (DESIGN 4b, toolbar): the day's output by source, or by
/// model for one source, most output first.
/// </summary>
/// <param name="Output">Σ output of the bars.</param>
public sealed record DayBreakdown(BreakdownKind Kind, DateOnly Day, long Output, IReadOnlyList<BreakdownRow> Rows)
{
    public string TitleKey => Kind == BreakdownKind.Sources ? "history.chart.bySource" : "history.chart.byModel";

    /// <summary>The breakdown a range shows: by source with every source, by model with one; null for several days.</summary>
    public static DayBreakdown? For(IEnumerable<DailyUsage> usage, DayRange range, SourceKind? source)
    {
        if (!range.IsSingleDay)
        {
            return null;
        }

        return source is { } only ? ByModel(usage, range.Start, only) : BySource(usage, range.Start);
    }

    /// <summary>Every source with usage on <paramref name="day"/>.</summary>
    public static DayBreakdown BySource(IEnumerable<DailyUsage> usage, DateOnly day)
    {
        ArgumentNullException.ThrowIfNull(usage);
        var tallies = new Dictionary<SourceKind, Tally>();
        foreach (var record in OfDay(usage, day, source: null))
        {
            tallies[record.Source] = tallies.GetValueOrDefault(record.Source)
                .Add(new Tally(record.Totals.Output, record.Totals.Requests, record.MeasuredOutput, record.MeasuredMs));
        }

        var bars = tallies.Select(t => new Bar(t.Key, null, false, t.Value)).ToList();
        return Build(BreakdownKind.Sources, day, bars);
    }

    /// <summary>
    /// The models of one source on <paramref name="day"/>, each with its own measured output and time; what no named
    /// model accounts for is gathered in one last bar.
    /// </summary>
    public static DayBreakdown ByModel(IEnumerable<DailyUsage> usage, DateOnly day, SourceKind source)
    {
        ArgumentNullException.ThrowIfNull(usage);
        var models = new Dictionary<string, Tally>(StringComparer.Ordinal);
        var other = new Tally();
        foreach (var record in OfDay(usage, day, source))
        {
            var named = new Tally();
            foreach (var model in record.Models ?? [])
            {
                var name = model.Model?.Trim();
                if (string.IsNullOrEmpty(name) || (model.Output <= 0 && model.Requests <= 0))
                {
                    continue;
                }

                var part = new Tally(model.Output, model.Requests, model.MeasuredOutput, model.MeasuredMs);
                models[name] = models.GetValueOrDefault(name).Add(part);
                named = named.Add(part);
            }

            var rest = new Tally(
                record.Totals.Output - named.Output,
                record.Totals.Requests - named.Requests,
                record.MeasuredOutput - named.MeasuredOutput,
                record.MeasuredMs - named.MeasuredMs);
            if (rest.Output > 0)
            {
                other = other.Add(rest);
            }
        }

        var bars = models.Select(m => new Bar(source, m.Key, false, m.Value)).ToList();
        if (other.Output > 0)
        {
            bars.Add(new Bar(source, null, true, other));
        }

        return Build(BreakdownKind.Models, day, bars);
    }

    private static IEnumerable<DailyUsage> OfDay(IEnumerable<DailyUsage> usage, DateOnly day, SourceKind? source)
    {
        var text = HistoryRanges.FormatDay(day);
        return usage.Where(r => r is not null && (source is null || r.Source == source) && HistoryAggregator.HasUsage(r)
            && string.Equals(r.Day, text, StringComparison.Ordinal));
    }

    private static DayBreakdown Build(BreakdownKind kind, DateOnly day, List<Bar> bars)
    {
        bars.Sort(Precedes);
        var total = bars.Sum(b => b.Tally.Output);
        var largest = bars.Count > 0 ? bars.Max(b => b.Tally.Output) : 0;
        var rows = bars
            .Select(b => new BreakdownRow(b.Source, b.Model, b.Tally.Output, b.Tally.Requests,
                total > 0 ? (double)b.Tally.Output / total : 0,
                largest > 0 ? (double)b.Tally.Output / largest : 0,
                b.Tally.Speed,
                b.Other))
            .ToArray();
        return new DayBreakdown(kind, day, total, rows);
    }

    /// <summary>Most output first, then most requests, then source order or model name; other models always last.</summary>
    private static int Precedes(Bar a, Bar b)
    {
        if (a.Other != b.Other)
        {
            return a.Other ? 1 : -1;
        }

        var order = b.Tally.Output.CompareTo(a.Tally.Output);
        if (order == 0)
        {
            order = b.Tally.Requests.CompareTo(a.Tally.Requests);
        }

        if (order == 0)
        {
            order = a.Model is not null && b.Model is not null
                ? StringComparer.OrdinalIgnoreCase.Compare(a.Model, b.Model)
                : a.Source.CompareTo(b.Source);
        }

        return order;
    }

    private readonly record struct Bar(SourceKind Source, string? Model, bool Other, Tally Tally);

    /// <summary>Counts of one bar; negative inputs count as zero.</summary>
    private readonly record struct Tally(long Output, long Requests, long MeasuredOutput, long MeasuredMs)
    {
        public double? Speed => MeasuredMs > 0 && MeasuredOutput > 0 ? MeasuredOutput / (MeasuredMs / 1000.0) : null;

        public Tally Add(Tally part) => new(
            Output + Math.Max(0, part.Output),
            Requests + Math.Max(0, part.Requests),
            MeasuredOutput + Math.Max(0, part.MeasuredOutput),
            MeasuredMs + Math.Max(0, part.MeasuredMs));
    }
}
