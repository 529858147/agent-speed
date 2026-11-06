using AgentSpeed.Core.Reporting;
using AgentSpeed.Presentation.Localization;

namespace AgentSpeed.Presentation.Reporting;

/// <summary>
/// The WebSocket line under the status (DESIGN 11, Status): <c>report.ws.stats</c> with the latency p50 and p95 of the
/// session's samples (METRICS 11.9, Latency) and the delivered and not delivered counts, and <c>report.ws.counters</c> with
/// every counter for its tooltip.
/// </summary>
public static class ReportStreamStats
{
    public const string MillisecondsKey = "win.report.ws.milliseconds";

    /// <summary>The stats line, or null while the session delivered and lost nothing.</summary>
    public static string? Line(Localizer loc, ReportStreamCounters counters, ReportStreamLatency latency)
    {
        ArgumentNullException.ThrowIfNull(loc);
        ArgumentNullException.ThrowIfNull(counters);
        ArgumentNullException.ThrowIfNull(latency);
        if (counters.Delivered == 0 && counters.NotDelivered == 0)
        {
            return null;
        }

        return loc.Format("report.ws.stats", Latency(loc, latency.P50), Latency(loc, latency.P95), Count(loc, counters.Delivered),
            Count(loc, counters.NotDelivered));
    }

    public static string Tooltip(Localizer loc, ReportStreamCounters counters)
    {
        ArgumentNullException.ThrowIfNull(loc);
        ArgumentNullException.ThrowIfNull(counters);
        return loc.Format("report.ws.counters", Count(loc, counters.Built), Count(loc, counters.Accepted), Count(loc, counters.Duplicate),
            Count(loc, counters.Rejected), Count(loc, counters.Stale), Count(loc, counters.Saturated), Count(loc, counters.Unknown));
    }

    /// <summary>A latency in whole milliseconds, or <c>report.ws.noLatency</c> without a sample.</summary>
    public static string Latency(Localizer loc, long? milliseconds)
    {
        ArgumentNullException.ThrowIfNull(loc);
        return milliseconds is { } ms ? loc.Format(MillisecondsKey, Count(loc, Math.Max(0, ms))) : loc["report.ws.noLatency"];
    }

    private static string Count(Localizer loc, long value) => value.ToString("N0", loc.Culture);
}
