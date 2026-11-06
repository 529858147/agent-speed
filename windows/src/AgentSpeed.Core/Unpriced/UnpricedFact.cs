using AgentSpeed.Core.Pricing;

namespace AgentSpeed.Core.Unpriced;

/// <summary>
/// What a response without a known cost says about itself (METRICS 9.10 <i>Facts</i>): its trimmed model (empty when
/// none), its provider (null when missing or empty), its fast flag, and live only whether its <c>endMs</c> has no date.
/// </summary>
public readonly record struct UnpricedFact(string Model, string? Provider, bool Fast, bool NoDate = false)
{
    /// <summary>The fact of a response's raw values: the model trimmed as in METRICS 4.9, an empty provider as none.</summary>
    public static UnpricedFact Of(string? model, string? provider, bool fast, bool noDate = false) =>
        new(model is null ? string.Empty : ModelResolver.Strip(model), string.IsNullOrEmpty(provider) ? null : provider, fast, noDate);

    /// <summary>
    /// The fact order of METRICS 9.10 <i>Rows</i>: the model's UTF-8 bytes, then provider (null first, then UTF-8 bytes),
    /// then fast (false first), then no date (false first).
    /// </summary>
    public static int Compare(UnpricedFact a, UnpricedFact b)
    {
        var order = Utf8Order.Instance.Compare(a.Model, b.Model);
        if (order != 0)
        {
            return order;
        }

        order = (a.Provider is not null).CompareTo(b.Provider is not null);
        if (order != 0)
        {
            return order;
        }

        order = Utf8Order.Instance.Compare(a.Provider ?? string.Empty, b.Provider ?? string.Empty);
        if (order != 0)
        {
            return order;
        }

        order = a.Fast.CompareTo(b.Fast);
        return order != 0 ? order : a.NoDate.CompareTo(b.NoDate);
    }
}

/// <summary>Requests of one unpriced fact.</summary>
public readonly record struct UnpricedCount(UnpricedFact Fact, long Requests);

/// <summary>Tallies of unpriced facts.</summary>
public static class UnpricedFacts
{
    /// <summary>The facts with requests, in the fact order.</summary>
    public static IReadOnlyList<UnpricedCount> Sorted(IEnumerable<KeyValuePair<UnpricedFact, long>> tally)
    {
        var list = new List<UnpricedCount>();
        foreach (var (fact, requests) in tally)
        {
            if (requests > 0)
            {
                list.Add(new UnpricedCount(fact, requests));
            }
        }

        list.Sort(static (a, b) => UnpricedFact.Compare(a.Fact, b.Fact));
        return list;
    }

    /// <summary>Adds <paramref name="requests"/> (negative removes) and drops a fact that reaches zero.</summary>
    public static void Add(Dictionary<UnpricedFact, long> tally, UnpricedFact fact, long requests)
    {
        var total = tally.GetValueOrDefault(fact) + requests;
        if (total == 0)
        {
            tally.Remove(fact);
        }
        else
        {
            tally[fact] = total;
        }
    }

    public static long Sum(IEnumerable<UnpricedCount> facts)
    {
        long sum = 0;
        foreach (var fact in facts)
        {
            sum += fact.Requests;
        }

        return sum;
    }
}
