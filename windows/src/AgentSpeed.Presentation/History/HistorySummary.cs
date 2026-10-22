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
/// <param name="Secondary">Active days, busiest day or the change against the previous period or day.</param>
/// <param name="Change">The change shown in <paramref name="Secondary"/>, when it is one (colored green/red).</param>
/// <param name="Spark">One value per day of <see cref="HistoryRanges.SparkRange"/>; null where the line breaks.</param>
/// <param name="Highlight">Index in <paramref name="Spark"/> of the selected day, for a single-day range.</param>
public sealed record SummaryCard(
    SummaryMetric Metric,
    string Label,
    string Value,
    string Unit,
    string FullValue,
    string Secondary,
    double? Change,
    IReadOnlyList<double?> Spark,
    int? Highlight = null);

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
    /// <param name="previous">Days with data in the equally long period before it (the day before a single day).</param>
    /// <param name="sparkDays">
    /// Days with data in <see cref="HistoryRanges.SparkRange"/>, for a single day whose sparklines show the week up to it;
    /// null uses <paramref name="days"/>.
    /// </param>
    public static IReadOnlyList<SummaryCard> Build(IReadOnlyList<DaySummary> days, IReadOnlyList<DaySummary> previous, DayRange range,
        DateOnly today, Localizer loc, IReadOnlyList<DaySummary>? sparkDays = null)
    {
        ArgumentNullException.ThrowIfNull(days);
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(loc);
        var totals = HistoryAggregator.Totals(days);
        var before = HistoryAggregator.Totals(previous);
        var unit = loc["unit.tokS"];
        var lines = range.IsSingleDay ? DayLines(totals, before, loc) : PeriodLines(days, totals, before, today, loc);

        var sparkRange = HistoryRanges.SparkRange(range);
        var spark = sparkDays ?? days;
        int? highlight = range.IsSingleDay ? sparkRange.IndexOf(range.Start) : null;

        return
        [
            new SummaryCard(SummaryMetric.Output, loc[LabelKey(SummaryMetric.Output)], SpeedFormat.Tokens(totals.Output), string.Empty,
                HistoryText.FullNumber(totals.Output, loc), lines.Output.Text, lines.Output.Change,
                HistoryAggregator.Series(spark, sparkRange, d => d.Output), highlight),
            new SummaryCard(SummaryMetric.Input, loc[LabelKey(SummaryMetric.Input)], SpeedFormat.Tokens(totals.Input), string.Empty,
                HistoryText.FullNumber(totals.Input, loc), lines.Input.Text, lines.Input.Change,
                HistoryAggregator.Series(spark, sparkRange, d => d.Input), highlight),
            new SummaryCard(SummaryMetric.Requests, loc[LabelKey(SummaryMetric.Requests)], SpeedFormat.Tokens(totals.Requests), string.Empty,
                HistoryText.FullNumber(totals.Requests, loc), lines.Requests.Text, lines.Requests.Change,
                HistoryAggregator.Series(spark, sparkRange, d => d.Requests), highlight),
            new SummaryCard(SummaryMetric.Speed, loc[LabelKey(SummaryMetric.Speed)],
                totals.AverageSpeed is { } speed ? SpeedFormat.Speed(speed) : "—", totals.AverageSpeed is null ? string.Empty : unit,
                HistoryText.Speed(totals.AverageSpeed, loc), lines.Speed.Text, lines.Speed.Change,
                HistoryAggregator.Series(spark, sparkRange, d => d.AverageSpeed, missing: null), highlight),
        ];
    }

    /// <summary>A range of days: the change against the previous period, the busiest day and the active days.</summary>
    private static CardLines PeriodLines(IReadOnlyList<DaySummary> days, HistoryTotals totals, HistoryTotals before, DateOnly today,
        Localizer loc)
    {
        var outputChange = HistoryAggregator.Change(totals.Output, before.Output);
        var outputSecondary = outputChange is { } oc
            ? HistoryText.VersusPrevious(oc, loc)
            : totals.BusiestDay is { Output: > 0 } busiest ? HistoryText.BusiestDay(busiest.Day, today, loc) : string.Empty;

        var busiestInput = days.Count == 0 ? null : days.MaxBy(d => (d.Input, d.Day.DayNumber));
        var inputSecondary = busiestInput is { Input: > 0 } bi ? HistoryText.BusiestDay(bi.Day, today, loc) : string.Empty;

        var speedChange = HistoryAggregator.Change(totals.AverageSpeed, before.AverageSpeed);
        return new CardLines(
            new CardLine(outputSecondary, outputChange),
            new CardLine(inputSecondary, null),
            new CardLine(totals.ActiveDays > 0 ? HistoryText.ActiveDays(totals.ActiveDays, loc) : string.Empty, null),
            new CardLine(speedChange is { } sc ? HistoryText.VersusPrevious(sc, loc) : string.Empty, speedChange));
    }

    /// <summary>A single day: every card compares with the day before (busiest and active days say nothing about one day).</summary>
    private static CardLines DayLines(HistoryTotals totals, HistoryTotals before, Localizer loc)
    {
        CardLine Versus(double? current, double? previous)
        {
            var change = HistoryAggregator.Change(current, previous);
            return new CardLine(change is { } c ? HistoryText.VersusPreviousDay(c, loc) : string.Empty, change);
        }

        return new CardLines(
            Versus(totals.Output, before.Output),
            Versus(totals.Input, before.Input),
            Versus(totals.Requests, before.Requests),
            Versus(totals.AverageSpeed, before.AverageSpeed));
    }

    private readonly record struct CardLine(string Text, double? Change);

    private readonly record struct CardLines(CardLine Output, CardLine Input, CardLine Requests, CardLine Speed);
}
