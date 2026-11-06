using AgentSpeed.Core;
using AgentSpeed.Presentation.Localization;
using AgentSpeed.Presentation.Modes;

namespace AgentSpeed.Presentation.History;

/// <summary>The words of the history's effort and speed rows (DESIGN 4b, 3b).</summary>
public static class ModeHistoryText
{
    /// <summary>The token (a speed through <c>speed.value.*</c> when one exists); <c>modes.unrecorded</c> for unknown.</summary>
    public static string Token(ModeHistoryRow row, ExecutionModeKind kind, Localizer loc)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(loc);
        if (row.IsUnknown)
        {
            return loc["modes.unrecorded"];
        }

        return kind == ExecutionModeKind.Speed ? EffortCapsule.SpeedValue(row.Mode, loc) : row.Mode;
    }

    /// <summary>A row read as one label: token, share, requests, output and average speed.</summary>
    public static string Accessible(ModeHistoryRow row, ExecutionModeKind kind, Localizer loc)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(loc);
        return HistoryText.JoinParts([
            Token(row, kind, loc),
            HistoryText.Percent(row.Share),
            HistoryText.Labelled(loc["history.table.requests"], HistoryText.FullNumber(row.Requests, loc), loc),
            HistoryText.Labelled(loc["history.table.output"], HistoryText.FullNumber(row.Output, loc), loc),
            HistoryText.Labelled(loc["history.table.speed"], HistoryText.Speed(row.AverageSpeed, loc), loc),
        ], loc);
    }

    /// <summary>The hover card's lines: up to three models, each with its share of the row's output.</summary>
    public static IReadOnlyList<(string Name, string Share, double Fraction)> TopModels(ModeHistoryRow row, Localizer loc)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(loc);
        return row.TopModels
            .Take(ModeHistory.TopModelCount)
            .Select(m => (string.IsNullOrEmpty(m.Model) ? loc["modes.unrecorded"] : m.Model, HistoryText.Percent(m.Share), m.Share))
            .ToArray();
    }

    /// <summary>The output as the table shows it (<c>1.2M</c>).</summary>
    public static string Output(ModeHistoryRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return SpeedFormat.Tokens(row.Output);
    }
}
