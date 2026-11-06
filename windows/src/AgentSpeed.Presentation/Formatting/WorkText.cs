using AgentSpeed.Core.Work;
using AgentSpeed.Presentation.History;
using AgentSpeed.Presentation.Localization;

namespace AgentSpeed.Presentation.Formatting;

/// <summary>The work signals' wording on an agent row (DESIGN 4, Looping mark; METRICS 6.10).</summary>
public static class WorkText
{
    /// <summary>The row's first tooltip line and its spoken addition while the agent is looping; null otherwise.</summary>
    public static string? Looping(WorkSignals work, Localizer localizer)
    {
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(localizer);
        return work.Looping is { } count ? localizer.Format("work.looping", count) : null;
    }

    /// <summary>
    /// The row's closing tooltip lines: <c>work.repeating</c> while it repeats without failures, then <c>work.summary</c>
    /// (the last line) once there are enough recent actions for novelty.
    /// </summary>
    public static IEnumerable<string> Closing(WorkSignals work, Localizer localizer)
    {
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(localizer);
        if (work.Repeating is { } count)
        {
            yield return localizer.Format("work.repeating", count);
        }

        if (work.Novelty is { } novelty)
        {
            yield return localizer.Format("work.summary", HistoryText.Percent(novelty), work.Failed, work.Recovered);
        }
    }
}
