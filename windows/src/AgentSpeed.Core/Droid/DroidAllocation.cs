using AgentSpeed.Core.Parsing;

namespace AgentSpeed.Core.Droid;

/// <summary>The five counters of a sidecar's <c>tokenUsage</c>, each a token value of METRICS 3.2 (0 … 10^12).</summary>
internal readonly record struct DroidCounters(long Input, long CacheCreation, long CacheRead, long Output, long Thinking)
{
    public const int FieldCount = 5;

    public bool IsZero => Input == 0 && CacheCreation == 0 && CacheRead == 0 && Output == 0 && Thinking == 0;

    public long this[int field] => field switch
    {
        0 => Input,
        1 => CacheCreation,
        2 => CacheRead,
        3 => Output,
        4 => Thinking,
        _ => throw new ArgumentOutOfRangeException(nameof(field)),
    };

    /// <summary>Input fields are split by the input weight, output and thinking by the output weight.</summary>
    public static bool IsInputField(int field) => field < 3;

    public static DroidCounters FromFields(ReadOnlySpan<long> fields) => new(fields[0], fields[1], fields[2], fields[3], fields[4]);

    /// <summary>Field by field <c>max(0, a − b)</c>.</summary>
    public static DroidCounters Minus(in DroidCounters a, in DroidCounters b) => new(
        Math.Max(0, a.Input - b.Input),
        Math.Max(0, a.CacheCreation - b.CacheCreation),
        Math.Max(0, a.CacheRead - b.CacheRead),
        Math.Max(0, a.Output - b.Output),
        Math.Max(0, a.Thinking - b.Thinking));

    public static DroidCounters Max(in DroidCounters a, in DroidCounters b) => new(
        Math.Max(a.Input, b.Input),
        Math.Max(a.CacheCreation, b.CacheCreation),
        Math.Max(a.CacheRead, b.CacheRead),
        Math.Max(a.Output, b.Output),
        Math.Max(a.Thinking, b.Thinking));

    public static DroidCounters Add(in DroidCounters a, in DroidCounters b) => new(
        a.Input + b.Input,
        a.CacheCreation + b.CacheCreation,
        a.CacheRead + b.CacheRead,
        a.Output + b.Output,
        a.Thinking + b.Thinking);

    /// <summary>A share of the own counters as normalized tokens: the cache counts as input, thinking inside output.</summary>
    public TokenCounts ToTokens() => new(
        Input: TokenCounts.Clamp(Input + CacheCreation + CacheRead),
        CachedInput: CacheRead,
        CacheWriteInput: CacheCreation,
        Output: Output,
        Reasoning: Thinking);
}

/// <summary>Largest-remainder rounding in exact 64-bit integers (METRICS 4.16 Allocation).</summary>
internal static class DroidAllocation
{
    /// <summary>A weight is at most 8 MiB, so <c>total · weight</c> fits in 64 bits for every total up to 10^12.</summary>
    public const long MaxWeight = 8_388_608;

    /// <summary>The 64 KiB that stand in for droid's system prompt and tool schemas in every input weight.</summary>
    public const long ContextBase = 65_536;

    /// <summary>
    /// Splits <paramref name="total"/> by <paramref name="weights"/> (each 1 … <see cref="MaxWeight"/>): every share is
    /// <c>⌊total · w / W⌋</c>, and the units left over go one each to the largest remainders, ties to the earlier index.
    /// </summary>
    public static long[] Shares(long total, ReadOnlySpan<long> weights)
    {
        var shares = new long[weights.Length];
        if (weights.IsEmpty)
        {
            return shares;
        }

        long whole = 0;
        foreach (var weight in weights)
        {
            whole += weight;
        }

        if (whole <= 0)
        {
            return shares;
        }

        var remainders = new long[weights.Length];
        var given = 0L;
        for (var i = 0; i < weights.Length; i++)
        {
            var product = total * weights[i];
            shares[i] = product / whole;
            remainders[i] = product % whole;
            given += shares[i];
        }

        var left = total - given;
        if (left <= 0)
        {
            return shares;
        }

        var order = new int[weights.Length];
        for (var i = 0; i < order.Length; i++)
        {
            order[i] = i;
        }

        Array.Sort(order, (a, b) =>
        {
            var byRemainder = remainders[b].CompareTo(remainders[a]);
            return byRemainder != 0 ? byRemainder : a.CompareTo(b);
        });

        for (var i = 0; i < left && i < order.Length; i++)
        {
            shares[order[i]]++;
        }

        return shares;
    }
}
