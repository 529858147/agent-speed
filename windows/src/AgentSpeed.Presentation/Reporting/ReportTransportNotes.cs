using AgentSpeed.Core.Reporting;
using AgentSpeed.Presentation.Settings;

namespace AgentSpeed.Presentation.Reporting;

/// <summary>
/// What the transport row says under its control (DESIGN 11, item 2b): <c>report.transport.hint</c> for HTTP; with WebSocket
/// <c>report.transport.source</c>, replaced by the secondary note <c>report.status.wsUnsupported</c> (reports go over HTTP
/// meanwhile) when the known document lacks a valid capability, and the amber <c>report.transport.throttled</c> when the
/// service's <c>minIntervalMs</c> is above 50.
/// </summary>
/// <param name="CaptionKey">The secondary caption, or null when the unsupported note replaces it.</param>
/// <param name="Unsupported">Show <c>report.status.wsUnsupported</c>, led by the information glyph.</param>
/// <param name="ThrottledMs">Show <c>report.transport.throttled</c> with this interval.</param>
public sealed record ReportTransportNotes(string? CaptionKey, bool Unsupported, int? ThrottledMs)
{
    public const int LiveIntervalMs = 50;

    /// <param name="document">The known discovery document; null before the first fetch (nothing is said about support then).</param>
    public static ReportTransportNotes For(ReportTransportKind transport, ReportDiscovery? document)
    {
        if (transport == ReportTransportKind.Http)
        {
            return new ReportTransportNotes("report.transport.hint", false, null);
        }

        if (document is not null && document.WebSocket is null)
        {
            return new ReportTransportNotes(null, true, null);
        }

        return new ReportTransportNotes("report.transport.source", false,
            document?.WebSocket is { MinIntervalMs: > LiveIntervalMs and var interval } ? interval : null);
    }
}
