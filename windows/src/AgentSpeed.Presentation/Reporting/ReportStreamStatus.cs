using AgentSpeed.Core.Reporting;

namespace AgentSpeed.Presentation.Reporting;

/// <summary>
/// The status lines that only the WebSocket transport has (DESIGN 11, Status). A service without the capability is not one of
/// them: reports go over HTTP meanwhile and the line is HTTP's.
/// </summary>
public enum ReportStreamStatusKind
{
    /// <summary>Amber <c>report.status.wsReconnecting</c> (or <c>…Never</c> before any success).</summary>
    Reconnecting,

    /// <summary>Red <c>report.status.wsMismatch</c>, with <i>Try Again</i>.</summary>
    Mismatch,

    /// <summary>Red <c>report.status.wsReplaced</c>, with <i>Try Again</i>.</summary>
    Replaced,

    Incompatible,
    Nonce,
    ClientState,
}

public static class ReportStreamStatus
{
    /// <summary>
    /// The stream's own status, or null when the shared statuses apply (a stop that has a shared problem — API key, forbidden,
    /// key mismatch, upgrade, rejected, gone — is shown as that problem). Reconnecting needs a failure and a mode that connects
    /// (nothing connects while quiet). A connected socket alone never counts as reporting.
    /// </summary>
    public static ReportStreamStatusKind? Of(ReportStreamStop? stop, ReportStreamConnection connection, int failures, ReportMode mode) =>
        stop switch
        {
            ReportStreamStop.Mismatch => ReportStreamStatusKind.Mismatch,
            ReportStreamStop.Replaced => ReportStreamStatusKind.Replaced,
            ReportStreamStop.Incompatible => ReportStreamStatusKind.Incompatible,
            ReportStreamStop.Nonce => ReportStreamStatusKind.Nonce,
            ReportStreamStop.ClientState => ReportStreamStatusKind.ClientState,
            not null => null,
            null when failures > 0 && connection != ReportStreamConnection.Open && mode != ReportMode.Quiet => ReportStreamStatusKind.Reconnecting,
            _ => null,
        };

    /// <summary>The problem of the shared status line a stop stands for, or null for the stream's own stops.</summary>
    public static ReportProblem? ProblemOf(ReportStreamStop stop) => stop switch
    {
        ReportStreamStop.ApiKey => ReportProblem.ApiKey,
        ReportStreamStop.Forbidden => ReportProblem.Forbidden,
        ReportStreamStop.KeyMismatch => ReportProblem.KeyMismatch,
        ReportStreamStop.Upgrade => ReportProblem.Upgrade,
        ReportStreamStop.Rejected => ReportProblem.Rejected,
        ReportStreamStop.Gone => ReportProblem.Gone,
        _ => null,
    };

    /// <summary><i>Try Again</i> is offered (DESIGN 11, Status).</summary>
    public static bool OffersRetry(ReportStreamStatusKind? kind) => kind is ReportStreamStatusKind.Mismatch or ReportStreamStatusKind.Replaced;

    public static string Key(ReportStreamStatusKind kind, bool everDelivered) => kind switch
    {
        ReportStreamStatusKind.Reconnecting => everDelivered ? "report.status.wsReconnecting" : "report.status.wsReconnectingNever",
        ReportStreamStatusKind.Mismatch => "report.status.wsMismatch",
        ReportStreamStatusKind.Replaced => "report.status.wsReplaced",
        ReportStreamStatusKind.Incompatible => "report.status.wsIncompatible",
        ReportStreamStatusKind.Nonce => "report.status.wsNonce",
        _ => "report.status.wsClientState",
    };

    /// <summary>Red: the user has to act.</summary>
    public static bool NeedsUser(ReportStreamStatusKind kind) => kind is not ReportStreamStatusKind.Reconnecting;
}
