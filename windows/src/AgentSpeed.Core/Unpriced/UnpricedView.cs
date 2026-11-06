using AgentSpeed.Core.Pricing;

namespace AgentSpeed.Core.Unpriced;

/// <summary>
/// The unpriced facts of one day and source (METRICS 9.10 <i>Rows</i>): a History day after <i>Reading</i>, or the
/// engine's today of one source.
/// </summary>
/// <param name="Day">Local date, <c>YYYY-MM-DD</c>.</param>
/// <param name="Requests">All requests of the day and source.</param>
public sealed record UnpricedPart(string Day, SourceKind Source, long Requests, long NoCostData, long Derived, IReadOnlyList<UnpricedCount> Facts)
{
    /// <summary>A stored day of the history (its facts already read, METRICS 9.10 <i>Reading</i>).</summary>
    public static UnpricedPart Of(DailyUsage day)
    {
        ArgumentNullException.ThrowIfNull(day);
        var unpriced = day.Unpriced;
        return new UnpricedPart(day.Day, day.Source, Math.Max(0, day.Totals.Requests), unpriced.NoCostData, unpriced.Derived, unpriced.Facts);
    }

    /// <summary>
    /// The live view (METRICS 9.10 <i>Live</i>): one part per source with requests today, its facts from the engine's day
    /// ledger. Disabled sources carry nothing.
    /// </summary>
    public static IReadOnlyList<UnpricedPart> Live(IEnumerable<SourceStatus> sources, string today)
    {
        ArgumentNullException.ThrowIfNull(sources);
        return [.. sources
            .Where(s => s.Enabled && s.Today.Requests > 0)
            .OrderBy(s => s.Kind)
            .Select(s => new UnpricedPart(today, s.Kind, s.Today.Requests, 0, 0, s.UnpricedFacts))];
    }
}

/// <summary>
/// The facts of one (source, model, provider, fast, no date) that have a reason under the current table (METRICS 9.10
/// <i>Rows</i>).
/// </summary>
/// <param name="First">The earliest day of its facts.</param>
/// <param name="Last">The latest day of its facts.</param>
public sealed record UnpricedRow(
    SourceKind Source,
    UnpricedFact Fact,
    UnpricedDiagnosis Diagnosis,
    long Requests,
    string First,
    string Last)
{
    public UnpricedReason Reason => Diagnosis.Reason;
}

/// <summary>One source of a view with its totals and rows.</summary>
/// <param name="Unpriced">Σ of its facts.</param>
/// <param name="PricedNow">Σ of its facts the current table prices.</param>
public sealed record UnpricedGroup(
    SourceKind Source,
    long Requests,
    long Unpriced,
    long PricedNow,
    long NoCostData,
    long Derived,
    IReadOnlyList<UnpricedRow> Rows)
{
    /// <summary>Σ requests of the rows: the unpriced requests that still have a reason.</summary>
    public long RowRequests => Rows.Sum(r => r.Requests);
}

/// <summary>
/// Why a range's requests have no price, per source (METRICS 9.10 <i>Rows</i>). Reasons are computed with the current
/// table every time a view is built, so a model a newer table prices moves to <see cref="UnpricedGroup.PricedNow"/>.
/// </summary>
public sealed record UnpricedView(IReadOnlyList<UnpricedGroup> Groups)
{
    public static UnpricedView Empty { get; } = new([]);

    public long Requests => Groups.Sum(g => g.Requests);

    public long Unpriced => Groups.Sum(g => g.Unpriced);

    public long PricedNow => Groups.Sum(g => g.PricedNow);

    public long NoCostData => Groups.Sum(g => g.NoCostData);

    public long Derived => Groups.Sum(g => g.Derived);

    public int RowCount => Groups.Sum(g => g.Rows.Count);

    /// <summary>
    /// The parts whose day lies in [<paramref name="first"/>, <paramref name="last"/>], for <paramref name="source"/> or
    /// every source, without the <paramref name="excluded"/> ones; sources by <c>unpriced</c> descending then source order,
    /// rows by requests descending then the fact order.
    /// </summary>
    public static UnpricedView Build(
        IEnumerable<UnpricedPart> parts,
        PriceTable current,
        string first,
        string last,
        SourceKind? source = null,
        SourceSet excluded = default)
    {
        ArgumentNullException.ThrowIfNull(parts);
        ArgumentNullException.ThrowIfNull(current);
        var groups = new Dictionary<SourceKind, GroupTally>();
        var rows = new Dictionary<(SourceKind, UnpricedFact), RowTally>();
        var diagnoses = new Dictionary<UnpricedFact, UnpricedDiagnosis?>();
        foreach (var part in parts)
        {
            if (string.CompareOrdinal(part.Day, first) < 0 || string.CompareOrdinal(part.Day, last) > 0
                || excluded.Contains(part.Source) || (source is { } only && part.Source != only))
            {
                continue;
            }

            if (!groups.TryGetValue(part.Source, out var group))
            {
                groups[part.Source] = group = new GroupTally();
            }

            group.Requests += part.Requests;
            group.NoCostData += part.NoCostData;
            group.Derived += part.Derived;
            foreach (var (fact, requests) in part.Facts)
            {
                group.Unpriced += requests;
                if (!diagnoses.TryGetValue(fact, out var diagnosis))
                {
                    diagnosis = fact.NoDate ? UnpricedDiagnosis.NoDate : current.Resolver.Diagnose(fact.Model, fact.Provider, fact.Fast);
                    diagnoses[fact] = diagnosis;
                }

                if (diagnosis is null)
                {
                    group.PricedNow += requests;
                    continue;
                }

                var key = (part.Source, fact);
                if (!rows.TryGetValue(key, out var row))
                {
                    rows[key] = row = new RowTally(diagnosis, part.Day);
                }

                row.Requests += requests;
                if (string.CompareOrdinal(part.Day, row.First) < 0)
                {
                    row.First = part.Day;
                }

                if (string.CompareOrdinal(part.Day, row.Last) > 0)
                {
                    row.Last = part.Day;
                }
            }
        }

        var result = new List<UnpricedGroup>(groups.Count);
        foreach (var (kind, group) in groups.OrderByDescending(g => g.Value.Unpriced).ThenBy(g => g.Key))
        {
            var mine = rows
                .Where(r => r.Key.Item1 == kind)
                .Select(r => new UnpricedRow(kind, r.Key.Item2, r.Value.Diagnosis, r.Value.Requests, r.Value.First, r.Value.Last))
                .ToList();
            mine.Sort(static (a, b) => a.Requests != b.Requests ? b.Requests.CompareTo(a.Requests) : UnpricedFact.Compare(a.Fact, b.Fact));
            result.Add(new UnpricedGroup(kind, group.Requests, group.Unpriced, group.PricedNow, group.NoCostData, group.Derived, mine));
        }

        return new UnpricedView(result);
    }

    private sealed class GroupTally
    {
        public long Requests { get; set; }

        public long Unpriced { get; set; }

        public long PricedNow { get; set; }

        public long NoCostData { get; set; }

        public long Derived { get; set; }
    }

    private sealed class RowTally(UnpricedDiagnosis diagnosis, string day)
    {
        public UnpricedDiagnosis Diagnosis { get; } = diagnosis;

        public long Requests { get; set; }

        public string First { get; set; } = day;

        public string Last { get; set; } = day;
    }
}
