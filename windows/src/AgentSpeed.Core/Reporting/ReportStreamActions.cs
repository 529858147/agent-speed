namespace AgentSpeed.Core.Reporting;

/// <summary>The connection of the stream scheduler (METRICS 11.9, Scheduler state).</summary>
public enum ReportStreamConnection
{
    Closed,
    Connecting,
    Opening,
    Open,
    Closing,
}

/// <summary>Why a built report was not delivered.</summary>
public enum ReportStreamDrop
{
    /// <summary>Its observation was older than 1 s at build: never written.</summary>
    Stale,

    /// <summary>The service answered it with an <c>error</c>.</summary>
    Rejected,

    /// <summary>The connection ended before an answer: its outcome is not known and it is never resent.</summary>
    Unknown,
}

/// <summary>What ends automatic attempts until the user acts or the settings change (METRICS 11.9, Reactions).</summary>
public enum ReportStreamStop
{
    Nonce,
    KeyMismatch,
    ApiKey,
    Forbidden,
    ClientState,
    Upgrade,
    Incompatible,
    Rejected,
    Gone,
    Replaced,
    Mismatch,
}

/// <summary>The reactions of REPORTING 8.3 the stream scheduler asks its owner for.</summary>
public enum ReportStreamReaction
{
    CorrectClock,
    ReRegister,
    RecheckRegistration,
    Rename,
    NewRun,
    RefreshDiscovery,
}

/// <param name="Observed">The window's earliest observation (monotonic ms), or null when the window had no new data.</param>
public readonly record struct ReportStreamTicket(int Seq, ReportActivity Activity, long BuiltAt, long WindowMs, long? Observed)
{
    public long? ObservationAgeMs => Observed is { } observed ? BuiltAt - observed : null;
}

/// <summary>What the stream scheduler asks its owner to do, in order.</summary>
public abstract record ReportStreamAction
{
    private ReportStreamAction()
    {
    }

    /// <summary>Empty the window without a report; <see cref="KeepingTick"/> for the gap rule (METRICS 11.1).</summary>
    public sealed record Discard(bool KeepingTick) : ReportStreamAction;

    /// <summary>Open a connection (signed upgrade, 10 s to the <c>101</c>).</summary>
    public sealed record Connect : ReportStreamAction;

    /// <summary>Drop the socket without a close handshake.</summary>
    public sealed record Abort : ReportStreamAction;

    /// <summary>Send <c>close 1000</c> and end the connection.</summary>
    public sealed record Close : ReportStreamAction;

    /// <summary>Take the window and the instantaneous values into a report body.</summary>
    public sealed record Build(ReportStreamTicket Ticket) : ReportStreamAction;

    /// <summary>Sign the built body (a fresh nonce) and write it as one <c>report</c> message.</summary>
    public sealed record Write(int Seq) : ReportStreamAction;

    /// <param name="LatencyMs">Answer time − observation, when the report had one.</param>
    public sealed record Delivered(int Seq, ReportAckStatus Status, long? LatencyMs) : ReportStreamAction;

    public sealed record Dropped(int Seq, ReportStreamDrop Reason) : ReportStreamAction;

    /// <summary>A due window was lost because every slot was awaited.</summary>
    public sealed record Saturated : ReportStreamAction;

    public sealed record React(ReportStreamReaction Reaction) : ReportStreamAction;

    public sealed record Stop(ReportStreamStop Reason) : ReportStreamAction;

    /// <summary>A failure: no connection before <see cref="ReconnectAt"/>.</summary>
    public sealed record Backoff(long ReconnectAt) : ReportStreamAction;

    /// <summary><c>Retry-After</c>: no connection before <see cref="Until"/>.</summary>
    public sealed record Wait(long Until) : ReportStreamAction;

    /// <summary>Tick again at <see cref="At"/> (the end of the minimum interval).</summary>
    public sealed record Wake(long At) : ReportStreamAction;

    /// <summary>The action as the fixtures write it (without its time), or null for a discard. Never carries a body.</summary>
    public string? Text => this switch
    {
        Discard => null,
        Connect => "connect",
        Abort => "abort",
        Close => "close 1000",
        Build build => $"build {build.Ticket.Seq} {ReportBody.ActivityName(build.Ticket.Activity)} {build.Ticket.WindowMs} "
            + (build.Ticket.ObservationAgeMs is { } age ? age.ToString(System.Globalization.CultureInfo.InvariantCulture) : "-"),
        Write write => $"write {write.Seq}",
        Delivered delivered => $"delivered {delivered.Seq} {(delivered.Status == ReportAckStatus.Duplicate ? "duplicate" : "accepted")}",
        Dropped dropped => $"dropped {dropped.Seq} {Name(dropped.Reason)}",
        Saturated => "saturated",
        React react => $"react {Name(react.Reaction)}",
        Stop stop => $"stop {Name(stop.Reason)}",
        Backoff backoff => $"backoff {backoff.ReconnectAt}",
        Wait wait => $"wait {wait.Until}",
        Wake wake => $"wake {wake.At}",
        _ => null,
    };

    /// <summary>An enum value in the fixtures' spelling (<c>keyMismatch</c>, <c>refreshDiscovery</c> …).</summary>
    public static string Name<T>(T value)
        where T : struct, Enum
    {
        var text = value.ToString();
        return char.ToLowerInvariant(text[0]) + text[1..];
    }
}

/// <summary>The counters of a transport session (METRICS 11.9): they survive a new run, not a new session.</summary>
public sealed class ReportStreamCounters
{
    public int Built { get; internal set; }

    public int Accepted { get; internal set; }

    public int Duplicate { get; internal set; }

    public int Rejected { get; internal set; }

    public int Stale { get; internal set; }

    public int Saturated { get; internal set; }

    public int Unknown { get; internal set; }

    public int Unmatched { get; internal set; }

    public int Delivered => Accepted + Duplicate;

    /// <summary>Windows that did not arrive: rejected, dropped as stale or while every slot was awaited, outcome unknown.</summary>
    public int NotDelivered => Rejected + Stale + Saturated + Unknown;

    /// <summary>The counters in the fixtures' names, in order.</summary>
    public IReadOnlyList<(string Name, int Value)> All =>
    [
        ("built", Built), ("accepted", Accepted), ("duplicate", Duplicate), ("rejected", Rejected), ("stale", Stale),
        ("saturated", Saturated), ("unknown", Unknown), ("unmatched", Unmatched),
    ];
}
