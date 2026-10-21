using AgentSpeed.Core;
using AgentSpeed.Presentation.History;
using AgentSpeed.Presentation.Localization;

namespace AgentSpeed.Presentation.Tests.TestSupport;

/// <summary>Builders for daily history records and summaries used by the history tests.</summary>
internal static class HistoryData
{
    public static readonly DateOnly Today = new(2026, 10, 21);

    private static readonly StringTable Embedded = Localizer.LoadEmbeddedTable();

    public static Localizer For(string language) => new(Embedded, language);

    public static DateOnly D(int year, int month, int day) => new(year, month, day);

    public static DailyUsage Usage(
        string day,
        SourceKind source = SourceKind.Codex,
        long output = 1000,
        long requests = 10,
        long input = 50_000,
        long cached = 40_000,
        long measuredOutput = -1,
        long measuredMs = 10_000,
        double? peak = null,
        int agents = 1,
        long unfinalized = 0,
        long cacheWrite = 0,
        long reasoning = 0,
        params ModelUsage[] models) =>
        new(
            day,
            source,
            new TokenTotals(requests, input, cached, cacheWrite, output, reasoning),
            measuredOutput < 0 ? output : measuredOutput,
            measuredMs,
            unfinalized,
            agents,
            peak,
            models);

    public static DaySummary Summary(
        DateOnly day,
        long output = 1000,
        long requests = 10,
        long input = 50_000,
        long cached = 40_000,
        long measuredOutput = -1,
        long measuredMs = 10_000,
        double? peak = null,
        int agents = 1,
        IReadOnlyList<ModelUsage>? models = null,
        IReadOnlyList<SourceOutput>? sources = null) =>
        new(
            day,
            requests,
            input,
            cached,
            0,
            output,
            0,
            measuredOutput < 0 ? output : measuredOutput,
            measuredMs,
            0,
            agents,
            peak,
            models ?? [],
            sources ?? [new SourceOutput(SourceKind.Codex, output)]);

    public static string Day(DateOnly day) => HistoryRanges.FormatDay(day);
}
