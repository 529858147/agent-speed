namespace AgentSpeed.Core;

public sealed record ModelUsage(string Model, long Requests, long Output);

/// <summary>One source's aggregates for one local calendar day (METRICS 8).</summary>
/// <param name="Day">Local calendar date, <c>YYYY-MM-DD</c>.</param>
/// <param name="Peak">Only for days observed live.</param>
public sealed record DailyUsage(
    string Day,
    SourceKind Source,
    TokenTotals Totals,
    long MeasuredOutput,
    long MeasuredMs,
    long Unfinalized,
    int Agents,
    double? Peak,
    IReadOnlyList<ModelUsage> Models)
{
    /// <summary>Output tok/s over the day's measured responses.</summary>
    public double? AverageSpeed => MeasuredMs > 0 ? MeasuredOutput / (MeasuredMs / 1000.0) : null;
}

public enum HistoryStateKind
{
    NotLoaded,
    Analyzing,
    Ready,
    Failed,
}

public readonly record struct HistoryState(
    HistoryStateKind Kind,
    double Progress = 0,
    int FilesDone = 0,
    int FilesTotal = 0,
    DateTimeOffset? UpdatedAt = null,
    string? Error = null)
{
    public static HistoryState NotLoaded => new(HistoryStateKind.NotLoaded);

    public static HistoryState Analyzing(double progress, int filesDone, int filesTotal) =>
        new(HistoryStateKind.Analyzing, progress, filesDone, filesTotal);

    public static HistoryState Ready(DateTimeOffset updatedAt) => new(HistoryStateKind.Ready, 1, UpdatedAt: updatedAt);

    public static HistoryState Failed(string error) => new(HistoryStateKind.Failed, Error: error);
}

/// <summary>
/// Hub the history window reads from; the history service and previews publish here. Notifications are raised on the
/// synchronization context captured at construction (the UI thread).
/// </summary>
public sealed class HistoryHub
{
    private readonly SynchronizationContext? _context;

    public HistoryHub(SynchronizationContext? context = null)
    {
        _context = context ?? SynchronizationContext.Current;
    }

    public HistoryState State { get; private set; } = HistoryState.NotLoaded;

    /// <summary>Every stored day of every source, sorted by day then source.</summary>
    public IReadOnlyList<DailyUsage> Days { get; private set; } = [];

    public event EventHandler? Changed;

    public Action? RefreshHandler { get; set; }

    public Action? ClearHandler { get; set; }

    public void Publish(HistoryState state, IReadOnlyList<DailyUsage>? days = null) => Post(() =>
    {
        State = state;
        if (days is not null)
        {
            Days = days;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    });

    public void Refresh() => RefreshHandler?.Invoke();

    public void Clear() => ClearHandler?.Invoke();

    private void Post(Action action)
    {
        if (_context is null || SynchronizationContext.Current == _context)
        {
            action();
        }
        else
        {
            _context.Post(static state => ((Action)state!)(), action);
        }
    }
}

public static class PreviewHistory
{
    /// <summary>Plausible history for previews and UI development.</summary>
    public static IReadOnlyList<DailyUsage> Days(DateTime? end = null, int count = 120)
    {
        var last = (end ?? DateTime.Now).Date;
        var random = new Random(42);
        (SourceKind Source, double Base, double Speed, string[] Models)[] profiles =
        [
            (SourceKind.Codex, 180_000, 42, ["gpt-6.1-sol", "gpt-6.1-sol-mini"]),
            (SourceKind.ClaudeCode, 140_000, 70, ["claude-opus-5-5", "claude-sonnet-5-5"]),
            (SourceKind.ClaudeDesktop, 260_000, 88, ["claude-opus-5-5"]),
            (SourceKind.DeepSeek, 90_000, 150, ["deepseek-flash", "deepseek-pro"]),
            (SourceKind.OpenCode, 60_000, 75, ["claude-opus-5-5"]),
            (SourceKind.Gemini, 40_000, 120, ["gemini-3-pro"]),
        ];
        var result = new List<DailyUsage>();
        for (var offset = count - 1; offset >= 0; offset--)
        {
            var date = last.AddDays(-offset);
            var weekend = date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
            for (var index = 0; index < profiles.Length; index++)
            {
                var (source, baseOutput, speed, models) = profiles[index];
                if (offset > count - 20 * (index + 1) && index > 3)
                {
                    continue;
                }

                if (random.NextDouble() < (weekend ? 0.45 : 0.12))
                {
                    continue;
                }

                var trend = 0.6 + 0.8 * (count - offset) / count;
                var output = (long)(baseOutput * trend * (0.4 + random.NextDouble() * 1.2));
                var requests = Math.Max(1, output / (long)(300 + random.NextDouble() * 500));
                var input = output * (long)(60 + random.NextDouble() * 90);
                var daySpeed = speed * (0.75 + random.NextDouble() * 0.5);
                var split = 0.55 + random.NextDouble() * 0.4;
                IReadOnlyList<ModelUsage> usage = models.Length == 1
                    ? [new ModelUsage(models[0], requests, output)]
                    : [
                        new ModelUsage(models[0], (long)(requests * split), (long)(output * split)),
                        new ModelUsage(models[1], requests - (long)(requests * split), output - (long)(output * split)),
                    ];
                result.Add(new DailyUsage(
                    date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                    source,
                    new TokenTotals(requests, input, (long)(input * 0.9), input / 40, output, output / 3),
                    output,
                    (long)(output / daySpeed * 1000),
                    0,
                    1 + (int)(random.NextDouble() * 6),
                    offset < 30 ? daySpeed * (2 + random.NextDouble() * 3) : null,
                    usage));
            }
        }

        return result;
    }
}
