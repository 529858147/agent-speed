namespace AgentSpeed.Core.Reporting;

/// <summary>The latency samples of a transport session (METRICS 11.9, Latency): answer time − observation, the last 512.</summary>
public sealed class ReportStreamLatency
{
    public const int Capacity = 512;

    private readonly Queue<long> _samples = new(Capacity);

    public int Count => _samples.Count;

    public IReadOnlyList<long> Samples => [.. _samples];

    public long? P50 => Percentile(Samples, 50);

    public long? P95 => Percentile(Samples, 95);

    public void Add(long sample)
    {
        if (_samples.Count == Capacity)
        {
            _samples.Dequeue();
        }

        _samples.Enqueue(sample);
    }

    /// <summary>Nearest rank: the smallest sample with at least <paramref name="p"/> % of the samples at or below it; null without samples.</summary>
    public static long? Percentile(IReadOnlyList<long> samples, int p)
    {
        if (samples.Count == 0)
        {
            return null;
        }

        var ordered = samples.Order().ToArray();
        var rank = (p * ordered.Length + 99) / 100;
        return ordered[Math.Max(1, rank) - 1];
    }
}
