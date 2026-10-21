using AgentSpeed.Core;
using AgentSpeed.Presentation.Localization;

namespace AgentSpeed.Presentation.History;

public enum SummaryMetric
{
    Output,
    Input,
    Requests,
    Speed,
}

/// <param name="Value">Compact value (METRICS 7).</param>
/// <param name="Unit">Unit drawn after the value (speed only).</param>
/// <param name="FullValue">Exact value for the tooltip.</param>
/// <param name="Secondary">Active days, busiest day or the change against the previous period.</param>
/// <param name="Change">The change shown in <paramref name="Secondary"/>, when it is one (colored green/red).</param>
/// <param name="Spark">One value per day of the range; null where the line breaks.</param>
public sealed record SummaryCard(
    SummaryMetric Metric,
    string Label,
    string Value,
    string Unit,
    string FullValue,
    string Secondary,
    double? Change,
    IReadOnlyList<double?> Spark);

/// <summary>The four summary cards (DESIGN 4b, item 2).</summary>
public static class HistorySummary
{
    public static string LabelKey(SummaryMetric metric) => metric switch
    {
        SummaryMetric.Output => "history.summary.output",
        SummaryMetric.Input => "history.summary.input",
        SummaryMetric.Requests => "history.summary.requests",
        _ => "history.summary.speed",
    };

    /// <param name="days">Days with data in <paramref name="range"/>.</param>
    /// <param name="previous">Days with data in the equally long period before it.</param>
    public static IReadOnlyList<SummaryCard> Build(IReadOnlyList<DaySummary> days, IReadOnlyList<DaySummary> previous, DayRange range,
        DateOnly today, Localizer loc)
    {
        ArgumentNullException.ThrowIfNull(days);
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(loc);
        var totals = HistoryAggregator.Totals(days);
        var before = HistoryAggregator.Totals(previous);
        var unit = loc["unit.tokS"];

        var outputChange = HistoryAggregator.Change(totals.Output, before.Output);
        var outputSecondary = outputChange is { } oc
            ? HistoryText.VersusPrevious(oc, loc)
            : totals.BusiestDay is { Output: > 0 } busiest ? HistoryText.BusiestDay(busiest.Day, today, loc) : string.Empty;

        var busiestInput = days.Count == 0 ? null : days.MaxBy(d => (d.Input, d.Day.DayNumber));
        var inputSecondary = busiestInput is { Input: > 0 } bi ? HistoryText.BusiestDay(bi.Day, today, loc) : string.Empty;

        var speedChange = HistoryAggregator.Change(totals.AverageSpeed, before.AverageSpeed);

        return
        [
            new SummaryCard(SummaryMetric.Output, loc[LabelKey(SummaryMetric.Output)], SpeedFormat.Tokens(totals.Output), string.Empty,
                HistoryText.FullNumber(totals.Output, loc), outputSecondary, outputChange,
                HistoryAggregator.Series(days, range, d => d.Output)),
            new SummaryCard(SummaryMetric.Input, loc[LabelKey(SummaryMetric.Input)], SpeedFormat.Tokens(totals.Input), string.Empty,
                HistoryText.FullNumber(totals.Input, loc), inputSecondary, null,
                HistoryAggregator.Series(days, range, d => d.Input)),
            new SummaryCard(SummaryMetric.Requests, loc[LabelKey(SummaryMetric.Requests)], SpeedFormat.Tokens(totals.Requests), string.Empty,
                HistoryText.FullNumber(totals.Requests, loc), totals.ActiveDays > 0 ? HistoryText.ActiveDays(totals.ActiveDays, loc) : string.Empty,
                null, HistoryAggregator.Series(days, range, d => d.Requests)),
            new SummaryCard(SummaryMetric.Speed, loc[LabelKey(SummaryMetric.Speed)],
                totals.AverageSpeed is { } speed ? SpeedFormat.Speed(speed) : "—", totals.AverageSpeed is null ? string.Empty : unit,
                HistoryText.Speed(totals.AverageSpeed, loc), speedChange is { } sc ? HistoryText.VersusPrevious(sc, loc) : string.Empty,
                speedChange, HistoryAggregator.Series(days, range, d => d.AverageSpeed, missing: null)),
        ];
    }
}
