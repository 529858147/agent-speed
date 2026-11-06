namespace AgentSpeed.Core.Unpriced;

/// <summary>One row of the history store's <c>contribution_unpriced</c> side table (METRICS 9.10).</summary>
/// <param name="Version">The analysis version that wrote it.</param>
public readonly record struct UnpricedSideRow(string Model, string? Provider, bool Fast, long Requests, int Version);

/// <summary>A stored contribution's unpriced facts after <i>Reading</i>.</summary>
/// <param name="NoCostData">The contribution's requests when it has no cost data (stored before costs existed).</param>
/// <param name="Derived">The unpriced requests whose facts come from the per-model counts, without provider or fast flag.</param>
public sealed record UnpricedReading(long NoCostData, long Derived, IReadOnlyList<UnpricedCount> Facts)
{
    public static UnpricedReading None { get; } = new(0, 0, []);

    /// <summary>
    /// METRICS 9.10 <i>Reading</i> of one stored contribution (file, day, source): its valid side rows are the facts;
    /// otherwise they are derived from the per-model unpriced counts, in the models' UTF-8 order, at most what is left of
    /// <paramref name="unpriced"/>, the rest under no model.
    /// </summary>
    /// <param name="fileVersion">The file's <c>files.version</c>.</param>
    /// <param name="unpriced">The contribution's <c>unpriced_requests</c>; null without cost data.</param>
    /// <param name="models">Raw model → its <c>unpriced_requests</c> (null counts as none).</param>
    public static UnpricedReading Read(
        int fileVersion,
        long requests,
        long? unpriced,
        IEnumerable<KeyValuePair<string, long?>> models,
        IReadOnlyList<UnpricedSideRow> side)
    {
        if (unpriced is not { } count)
        {
            return new UnpricedReading(requests, 0, []);
        }

        if (count <= 0)
        {
            return None;
        }

        var tally = new Dictionary<UnpricedFact, long>();
        if (side.Count > 0 && side.All(r => r.Version == fileVersion && r.Requests >= 1) && side.Sum(r => r.Requests) == count)
        {
            foreach (var row in side)
            {
                UnpricedFacts.Add(tally, UnpricedFact.Of(row.Model, row.Provider, row.Fast), row.Requests);
            }

            return new UnpricedReading(0, 0, UnpricedFacts.Sorted(tally));
        }

        var left = count;
        foreach (var (model, modelUnpriced) in models.OrderBy(m => m.Key, Utf8Order.Instance))
        {
            var take = Math.Min(Math.Max(modelUnpriced ?? 0, 0), left);
            if (take > 0)
            {
                UnpricedFacts.Add(tally, UnpricedFact.Of(model, null, false), take);
                left -= take;
            }
        }

        if (left > 0)
        {
            UnpricedFacts.Add(tally, UnpricedFact.Of(string.Empty, null, false), left);
        }

        return new UnpricedReading(0, count, UnpricedFacts.Sorted(tally));
    }
}
