using AgentSpeed.Presentation.Settings;

namespace AgentSpeed.Presentation.Reporting;

/// <summary>
/// Follows the selected service's effective transport (METRICS 11.9) so that a change of it — the user's choice, or a stored
/// or refreshed document that adds or drops the capability — is handled as a change of transport. The first look at a
/// service, or another service, is not a change.
/// </summary>
public sealed class ReportTransportTracker
{
    private string? _origin;

    public ReportTransportKind Current { get; private set; } = ReportTransportKind.Http;

    /// <returns>True when <paramref name="effective"/> differs from the transport last seen for the same origin.</returns>
    public bool Update(string? origin, ReportTransportKind effective)
    {
        var changed = origin is not null && string.Equals(origin, _origin, StringComparison.Ordinal) && effective != Current;
        _origin = origin;
        Current = effective;
        return changed;
    }
}
