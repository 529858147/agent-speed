using AgentSpeed.Core.Parsing;

namespace AgentSpeed.Core.Engine;

/// <summary>Incremental "today" aggregates (METRICS 6.6) that support replacing a response by key.</summary>
internal sealed class TodayLedger
{
    private const int IndexCapacity = 65_536;

    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly Queue<string> _order = new();
    private readonly SecondSeries _output = new();
    private readonly SecondSeries _total = new();
    private readonly TokenTotals[] _perSource = new TokenTotals[Enum.GetValues<SourceKind>().Length];
    private double _validOutput;
    private double _validSeconds;
    private long _validCount;
    private int _unfinalized;

    public TodayLedger(long startMs)
    {
        StartMs = startMs;
    }

    public long StartMs { get; private set; }

    public TokenTotals Totals { get; private set; }

    /// <summary>Σ output / Σ duration over today's measured responses, null when there are none.</summary>
    public double? AverageOutputSpeed => _validCount > 0 && _validSeconds > 0 ? Math.Max(0, _validOutput) / _validSeconds : null;

    /// <summary>Today's responses whose final usage was never written.</summary>
    public int UnfinalizedCount => Math.Max(0, _unfinalized);

    public TokenTotals ForSource(SourceKind source) => _perSource[(int)source];

    public (double Output, long? OutputAtMs, double Total) Peak()
    {
        var (output, at) = _output.TrailingPeak();
        var (total, _) = _total.TrailingPeak();
        return (output, at, total);
    }

    public void Reset(long startMs)
    {
        StartMs = startMs;
        _entries.Clear();
        _order.Clear();
        _output.Clear();
        _total.Clear();
        Array.Clear(_perSource);
        Totals = default;
        _validOutput = 0;
        _validSeconds = 0;
        _validCount = 0;
        _unfinalized = 0;
    }

    public void Upsert(ParsedResponse response)
    {
        var belongs = response.EndMs >= StartMs;
        if (_entries.TryGetValue(response.Key, out var previous))
        {
            Apply(previous, -1);
            if (!belongs)
            {
                _entries.Remove(response.Key);
                return;
            }
        }
        else if (!belongs)
        {
            return;
        }
        else
        {
            if (_order.Count >= IndexCapacity && _order.TryDequeue(out var oldest))
            {
                _entries.Remove(oldest);
            }

            _order.Enqueue(response.Key);
        }

        var entry = new Entry(
            response.Source,
            response.Tokens,
            response.Final,
            response.Final && SpeedMath.TryGetSpeedInterval(response, out var interval) ? interval : null);
        Apply(entry, 1);
        _entries[response.Key] = entry;
    }

    private void Apply(in Entry entry, int sign)
    {
        Totals = Add(Totals, entry.Tokens, sign);
        _perSource[(int)entry.Source] = Add(_perSource[(int)entry.Source], entry.Tokens, sign);
        if (!entry.Final)
        {
            _unfinalized += sign;
        }

        if (entry.Measured is not { } interval)
        {
            return;
        }

        _validCount += sign;
        _validOutput += sign * (double)entry.Tokens.Output;
        _validSeconds += sign * (interval.DurationMs / 1000.0);
        _output.AddInterval(interval.StartMs, interval.EndMs, sign * (double)entry.Tokens.Output, StartMs);
        _total.AddInterval(interval.StartMs, interval.EndMs, sign * (double)entry.Tokens.Total, StartMs);
    }

    private static TokenTotals Add(TokenTotals totals, in TokenCounts tokens, int sign) => new(
        totals.Requests + sign,
        totals.Input + sign * tokens.Input,
        totals.CachedInput + sign * tokens.CachedInput,
        totals.CacheWriteInput + sign * tokens.CacheWriteInput,
        totals.Output + sign * tokens.Output,
        totals.Reasoning + sign * tokens.Reasoning);

    /// <param name="Measured">The speed interval when the response is final and valid (METRICS 3.3).</param>
    private readonly record struct Entry(SourceKind Source, TokenCounts Tokens, bool Final, Interval? Measured);
}
