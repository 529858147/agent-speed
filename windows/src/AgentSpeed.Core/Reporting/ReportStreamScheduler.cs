namespace AgentSpeed.Core.Reporting;

/// <summary>
/// The stream scheduler of METRICS 11.9 on a monotonic millisecond clock: one long-lived connection, reports built when the
/// data changes, written at once and acknowledged one by one. The owner performs the connection's actions and reports its
/// events back (<see cref="Opened"/>, <see cref="Ready"/>, <see cref="Ack"/>, <see cref="Error"/>, <see cref="Closed"/>,
/// <see cref="Rejected"/>), only those of the current connection. One instance is one transport session.
/// </summary>
public sealed class ReportStreamScheduler
{
    public const long ConnectMs = 10_000;
    public const long ClosingMs = 1000;
    public const long StaleMs = 1000;
    public const long StableMs = 60_000;

    /// <summary>The reconnect delay of the 1st, 2nd … failure; the last one repeats.</summary>
    public static IReadOnlyList<long> BackoffMs { get; } = [500, 1000, 2000, 4000, 8000, 16_000, 30_000];

    private readonly Func<double> _jitter;
    private readonly List<Awaited> _awaiting = [];
    private bool _reporting = true;
    private long? _lastActive;
    private long? _previousTick;
    private long? _deadline;
    private long? _readyAt;
    private long? _windowStart;
    private long? _observed;
    private bool _dirty;
    private long? _lastBuild;
    private ReportActivity? _lastActivity;
    private bool _mismatched;
    private bool _waitingSettings;
    private int _rejectStrikes;
    private int _nonceStrikes;
    private int _signatureStrikes;
    private int _runConflicts;
    private int _oversize;

    /// <param name="settings">The document's stream settings, or null while the transport is unavailable.</param>
    /// <param name="jitter">The random reconnect factor in [0.8, 1.2]; the fixtures use 1.</param>
    public ReportStreamScheduler(ReportStreamSettings? settings, int heartbeatSeconds, bool paused = false, bool networkOff = false,
        Func<double>? jitter = null)
    {
        Settings = settings ?? ReportStreamSettings.Default;
        Limits = Settings;
        Available = settings is not null;
        HeartbeatMs = heartbeatSeconds * 1000L;
        Paused = paused;
        NetworkOff = networkOff;
        _jitter = jitter ?? (() => 0.8 + Random.Shared.NextDouble() * 0.4);
    }

    /// <summary>The document's settings; the next connection's <c>ready</c> must match them.</summary>
    public ReportStreamSettings Settings { get; private set; }

    /// <summary>The settings that govern the current connection (its <c>ready</c>).</summary>
    public ReportStreamSettings Limits { get; private set; }

    /// <summary><c>H</c> (<c>heartbeatSeconds</c>, METRICS 11.5).</summary>
    public long HeartbeatMs { get; set; }

    /// <summary>The document has the capability.</summary>
    public bool Available { get; private set; }

    public bool Paused { get; private set; }

    public bool NetworkOff { get; private set; }

    /// <summary>Reporting was turned off or the app quits (<see cref="ReportEvent.Stop"/>).</summary>
    public bool Ended => !_reporting;

    public ReportMode Mode { get; private set; } = ReportMode.Quiet;

    public ReportStreamConnection Connection { get; private set; }

    public int Seq { get; private set; }

    public int Failures { get; private set; }

    public long ReconnectAt { get; private set; } = long.MinValue;

    public long RateLimitedUntil { get; private set; } = long.MinValue;

    public ReportStreamStop? Stop { get; private set; }

    /// <summary>The time of the last delivered report.</summary>
    public long? LastDelivered { get; private set; }

    /// <summary>The pending wake-up; a tick at or after it consumes it.</summary>
    public long? WakeAt { get; private set; }

    /// <summary>The <c>rank</c> of the last delivery that carried one.</summary>
    public int? Rank { get; private set; }

    public int AwaitingCount => _awaiting.Count;

    /// <summary>Where the open window started (the preview's <c>windowMs</c>), or null before the first tick.</summary>
    public long? WindowStart => _windowStart;

    /// <summary>The open window's earliest observation (the preview's <c>realtime</c>), or null.</summary>
    public long? WindowObserved => _observed;

    public ReportStreamCounters Counters { get; } = new();

    public ReportStreamLatency Latency { get; } = new();

    /// <summary>Forgets the pending wake-up (a wake-up that a listed tick of the same time replaced).</summary>
    public void ClearWake() => WakeAt = null;

    /// <summary>
    /// One tick at <paramref name="t"/>: an engine tick, a snapshot after a read, or a wake-up. <paramref name="changed"/> is the
    /// changed rule of METRICS 11.9 and <paramref name="observed"/> the tick's observation; <paramref name="blocked"/> covers the
    /// readiness the scheduler does not track (on, confirmed, registered, no error that needs the user).
    /// </summary>
    public List<ReportStreamAction> Tick(long t, bool active, bool changed = false, long? observed = null, bool blocked = false,
        IReadOnlyList<ReportEvent>? events = null)
    {
        var actions = new List<ReportStreamAction>();
        if (WakeAt is { } wake && t >= wake)
        {
            WakeAt = null;
        }

        var userLeave = false;
        foreach (var e in events ?? [])
        {
            switch (e)
            {
                case ReportEvent.Pause:
                    userLeave |= !Paused;
                    Paused = true;
                    break;
                case ReportEvent.Resume:
                    Paused = false;
                    break;
                case ReportEvent.NetworkOff:
                    NetworkOff = true;
                    if (Connection != ReportStreamConnection.Closed)
                    {
                        End(new ReportStreamAction.Abort(), actions);
                    }

                    break;
                case ReportEvent.NetworkOn:
                    NetworkOff = false;
                    break;
                case ReportEvent.Stop:
                    userLeave |= _reporting;
                    _reporting = false;
                    break;
            }
        }

        if (_previousTick is { } previousTick && t - previousTick > ReportScheduler.GapMs)
        {
            Discard(t, actions, keepingTick: true);
            if (Connection != ReportStreamConnection.Closed)
            {
                End(new ReportStreamAction.Abort(), actions);
            }
        }

        _previousTick = t;
        _windowStart ??= t;
        var previous = Mode;
        if (active)
        {
            _lastActive = t;
        }

        var since = _lastActive is { } last ? t - last : (long?)null;
        Mode = since is { } s && s < ReportScheduler.HoldMs ? ReportMode.Active
            : since is { } q && q < ReportScheduler.QuietMs ? ReportMode.Idle
            : ReportMode.Quiet;
        if (changed || !SameActivity(Mode, _lastActivity))
        {
            _dirty = true;
        }

        if (changed && observed is { } seen)
        {
            _observed = _observed is { } earliest ? Math.Min(earliest, seen) : seen;
        }

        if (Connection == ReportStreamConnection.Closing && t >= _deadline)
        {
            End(new ReportStreamAction.Abort(), actions);
        }

        if (userLeave && Connection == ReportStreamConnection.Open && previous != ReportMode.Quiet && t >= RateLimitedUntil)
        {
            Leave(t, actions);
        }

        if (!_reporting || NetworkOff || Paused || Stop is not null || !Available || blocked)
        {
            if (Connection == ReportStreamConnection.Open)
            {
                if (_awaiting.Count > 0)
                {
                    (Connection, _deadline) = (ReportStreamConnection.Closing, t + ClosingMs);
                }
                else
                {
                    End(new ReportStreamAction.Close(), actions);
                }
            }
            else if (Connection is ReportStreamConnection.Connecting or ReportStreamConnection.Opening)
            {
                End(new ReportStreamAction.Abort(), actions);
            }

            Discard(t, actions);
            return actions;
        }

        if (Mode == ReportMode.Quiet)
        {
            if (previous != ReportMode.Quiet && Connection == ReportStreamConnection.Open)
            {
                Leave(t, actions);
            }
            else if (Connection is ReportStreamConnection.Connecting or ReportStreamConnection.Opening)
            {
                End(new ReportStreamAction.Abort(), actions);
            }

            Discard(t, actions);
            return actions;
        }

        switch (Connection)
        {
            case ReportStreamConnection.Closed:
                if (t >= ReconnectAt && t >= RateLimitedUntil && !_waitingSettings)
                {
                    (Connection, _deadline) = (ReportStreamConnection.Connecting, t + ConnectMs);
                    actions.Add(new ReportStreamAction.Connect());
                }

                Discard(t, actions);
                return actions;
            case ReportStreamConnection.Connecting or ReportStreamConnection.Opening:
                if (t >= _deadline)
                {
                    End(new ReportStreamAction.Abort(), actions);
                    Failure(t, actions);
                }

                Discard(t, actions);
                return actions;
            case ReportStreamConnection.Closing:
                Discard(t, actions);
                return actions;
        }

        if (_awaiting.Any(report => t > report.Written + Limits.AckTimeoutMs))
        {
            End(new ReportStreamAction.Abort(), actions);
            Failure(t, actions);
            Discard(t, actions);
            return actions;
        }

        var due = _dirty && (_lastBuild is not { } built || t - built >= Limits.MinIntervalMs);
        due |= t - (_lastBuild ?? _readyAt ?? t) >= HeartbeatMs;
        if (due)
        {
            if (_awaiting.Count < Limits.MaxInFlight)
            {
                Build(t, Mode == ReportMode.Active ? ReportActivity.Active : ReportActivity.Idle, actions);
            }
            else
            {
                Discard(t, actions);
                _dirty = false;
                Counters.Saturated++;
                actions.Add(new ReportStreamAction.Saturated());
            }
        }
        else if (_dirty && _lastBuild is { } lastBuild)
        {
            var at = lastBuild + Limits.MinIntervalMs;
            if (WakeAt != at)
            {
                WakeAt = at;
                actions.Add(new ReportStreamAction.Wake(at));
            }
        }

        return actions;
    }

    /// <summary>The <c>101</c> with the subprotocol: <c>connecting</c> → <c>opening</c>, 10 s to a valid <c>ready</c>.</summary>
    public List<ReportStreamAction> Opened(long t)
    {
        if (Connection == ReportStreamConnection.Connecting)
        {
            (Connection, _deadline) = (ReportStreamConnection.Opening, t + ConnectMs);
        }

        return [];
    }

    /// <summary>A valid <c>ready</c>: limits equal to the document's open the connection; others close it and refresh the document once.</summary>
    public List<ReportStreamAction> Ready(long t, ReportStreamSettings limits)
    {
        var actions = new List<ReportStreamAction>();
        if (Connection != ReportStreamConnection.Opening)
        {
            return actions;
        }

        if (limits == Settings)
        {
            (Connection, _deadline, _readyAt, _mismatched) = (ReportStreamConnection.Open, null, t, false);
            Limits = limits;
            Discard(t, actions);
            (_dirty, _lastBuild) = (true, null);
            return actions;
        }

        End(new ReportStreamAction.Close(), actions);
        if (_mismatched)
        {
            Halt(t, ReportStreamStop.Mismatch, actions);
        }
        else
        {
            (_mismatched, _waitingSettings) = (true, true);
            actions.Add(new ReportStreamAction.React(ReportStreamReaction.RefreshDiscovery));
        }

        return actions;
    }

    /// <summary>A refreshed document: its stream settings, or null when it lacks the capability.</summary>
    public List<ReportStreamAction> SettingsChanged(long t, ReportStreamSettings? settings)
    {
        var actions = new List<ReportStreamAction>();
        if (settings is null)
        {
            if (_waitingSettings)
            {
                _waitingSettings = false;
                Halt(t, ReportStreamStop.Mismatch, actions);
            }
            else
            {
                Available = false;
            }

            return actions;
        }

        (Settings, Available, _waitingSettings) = (settings, true, false);
        return actions;
    }

    /// <summary>The forced refresh after a differing <c>ready</c> failed.</summary>
    public List<ReportStreamAction> RefreshFailed(long t)
    {
        var actions = new List<ReportStreamAction>();
        if (_waitingSettings)
        {
            _waitingSettings = false;
            Halt(t, ReportStreamStop.Mismatch, actions);
        }

        return actions;
    }

    /// <summary><i>Try Again</i>: clears a stop, the strikes, the mismatch flags and the failures; connects at the next tick.</summary>
    public List<ReportStreamAction> Retry(long t)
    {
        (Stop, _mismatched, _waitingSettings) = (null, false, false);
        ResetStrikes();
        (Failures, ReconnectAt) = (0, t);
        return [];
    }

    /// <summary>An <c>ack</c>; <paramref name="matches"/> is false when its run id or body hash is not the awaited report's.</summary>
    public List<ReportStreamAction> Ack(long t, int seq, ReportAckStatus status, bool matches = true, int? rank = null)
    {
        var actions = new List<ReportStreamAction>();
        if (Connection is not (ReportStreamConnection.Open or ReportStreamConnection.Closing))
        {
            return actions;
        }

        var index = matches ? _awaiting.FindIndex(r => r.Seq == seq) : -1;
        if (index < 0)
        {
            Counters.Unmatched++;
            return actions;
        }

        var report = _awaiting[index];
        _awaiting.RemoveAt(index);
        if (status == ReportAckStatus.Duplicate)
        {
            Counters.Duplicate++;
        }
        else
        {
            Counters.Accepted++;
        }

        LastDelivered = t;
        Rank = rank ?? Rank;
        long? latency = report.Observed is { } observed ? t - observed : null;
        if (latency is { } sample)
        {
            Latency.Add(sample);
        }

        actions.Add(new ReportStreamAction.Delivered(seq, status, latency));
        ResetStrikes();
        if (t - _readyAt >= StableMs)
        {
            Failures = 0;
        }

        if (Connection == ReportStreamConnection.Closing && _awaiting.Count == 0)
        {
            End(new ReportStreamAction.Close(), actions);
        }

        return actions;
    }

    /// <summary>A written report whose message does not fit <c>maxMessageBytes</c> even without models and sources: dropped as stale.</summary>
    public List<ReportStreamAction> Unsendable(int seq)
    {
        var actions = new List<ReportStreamAction>();
        var index = _awaiting.FindIndex(r => r.Seq == seq);
        if (index < 0)
        {
            return actions;
        }

        _awaiting.RemoveAt(index);
        Counters.Stale++;
        actions.Add(new ReportStreamAction.Dropped(seq, ReportStreamDrop.Stale));
        if (Connection == ReportStreamConnection.Closing && _awaiting.Count == 0)
        {
            End(new ReportStreamAction.Close(), actions);
        }

        return actions;
    }

    /// <summary>An <c>error</c>; <paramref name="seq"/> is null for <c>"run": null</c> or another run's id.</summary>
    public List<ReportStreamAction> Error(long t, int? seq, string code, int httpStatus, int? retryAfterSeconds = null, bool fatal = false)
    {
        var actions = new List<ReportStreamAction>();
        if (Connection is not (ReportStreamConnection.Open or ReportStreamConnection.Closing))
        {
            return actions;
        }

        var index = seq is { } number ? _awaiting.FindIndex(r => r.Seq == number) : -1;
        if (index >= 0)
        {
            actions.Add(new ReportStreamAction.Dropped(_awaiting[index].Seq, ReportStreamDrop.Rejected));
            _awaiting.RemoveAt(index);
            Counters.Rejected++;
        }

        ReactTo(t, code, httpStatus, retryAfterSeconds, actions);
        if (fatal && Connection != ReportStreamConnection.Closed)
        {
            End(new ReportStreamAction.Abort(), actions);
            Failure(t, actions);
        }
        else if (Connection == ReportStreamConnection.Closing && _awaiting.Count == 0)
        {
            End(new ReportStreamAction.Close(), actions);
        }

        return actions;
    }

    /// <summary>A refused handshake; <paramref name="code"/> from its v1 error body, else classified by the status.</summary>
    public List<ReportStreamAction> Rejected(long t, int status, string? code = null, int? retryAfterSeconds = null)
    {
        var actions = new List<ReportStreamAction>();
        if (Connection != ReportStreamConnection.Connecting)
        {
            return actions;
        }

        End(null, actions);
        code ??= ReportStreamRejection.Classify(status, [], retryAfterSeconds?.ToString(System.Globalization.CultureInfo.InvariantCulture)).Code;
        if (!ReactTo(t, code, status, retryAfterSeconds, actions))
        {
            Failure(t, actions);
        }

        return actions;
    }

    /// <summary>The connection ended with <paramref name="code"/> (sent by the service, or 1006 for the network).</summary>
    public List<ReportStreamAction> Closed(long t, int code)
    {
        var actions = new List<ReportStreamAction>();
        if (Connection == ReportStreamConnection.Closed)
        {
            return actions;
        }

        var was = Connection;
        End(null, actions);
        if (was == ReportStreamConnection.Closing)
        {
            return actions;
        }

        switch (code)
        {
            case 4001:
                Halt(t, ReportStreamStop.ApiKey, actions);
                break;
            case 4003:
                Halt(t, ReportStreamStop.Forbidden, actions);
                break;
            case 4004:
                actions.Add(new ReportStreamAction.React(ReportStreamReaction.ReRegister));
                Failure(t, actions);
                break;
            case 4009:
                Halt(t, ReportStreamStop.Replaced, actions);
                break;
            case 4410:
                Halt(t, ReportStreamStop.Gone, actions);
                break;
            case 4429 or 4503:
                Wait(t, null, actions);
                break;
            case 1002 or 1003 or 1007:
                Halt(t, ReportStreamStop.Incompatible, actions);
                break;
            case 1009:
                actions.Add(new ReportStreamAction.React(ReportStreamReaction.RefreshDiscovery));
                if (++_oversize >= 3)
                {
                    Halt(t, ReportStreamStop.Incompatible, actions);
                }
                else
                {
                    Failure(t, actions);
                }

                break;
            default:
                Failure(t, actions);
                break;
        }

        return actions;
    }

    /// <summary>The reaction table; true when it ended the attempt (a failure, a wait, a stop or a fresh start).</summary>
    private bool ReactTo(long t, string? code, int? status, int? retryAfterSeconds, List<ReportStreamAction> actions)
    {
        switch (code)
        {
            case "timestamp_out_of_window":
                actions.Add(new ReportStreamAction.React(ReportStreamReaction.CorrectClock));
                return false;
            case "nonce_replayed":
                if (++_nonceStrikes >= 3)
                {
                    Halt(t, ReportStreamStop.Nonce, actions);
                    return true;
                }

                return false;
            case "signature_invalid":
                actions.Add(new ReportStreamAction.React(ReportStreamReaction.RecheckRegistration));
                if (++_signatureStrikes >= 3)
                {
                    Halt(t, ReportStreamStop.KeyMismatch, actions);
                    return true;
                }

                if (Connection != ReportStreamConnection.Closed)
                {
                    End(new ReportStreamAction.Close(), actions);
                }

                Failure(t, actions);
                return true;
            case "api_key_required" or "api_key_invalid":
                Halt(t, ReportStreamStop.ApiKey, actions);
                return true;
            case "forbidden":
                Halt(t, ReportStreamStop.Forbidden, actions);
                return true;
            case "unknown_device":
                actions.Add(new ReportStreamAction.React(ReportStreamReaction.ReRegister));
                if (Connection != ReportStreamConnection.Closed)
                {
                    End(new ReportStreamAction.Abort(), actions);
                }

                Failure(t, actions);
                return true;
            case "username_mismatch":
                actions.Add(new ReportStreamAction.React(ReportStreamReaction.Rename));
                return false;
            case "seq_conflict":
                if (Connection != ReportStreamConnection.Closed)
                {
                    End(new ReportStreamAction.Abort(), actions);
                }

                actions.Add(new ReportStreamAction.React(ReportStreamReaction.NewRun));
                NewRun(t);
                ReconnectAt = t;
                return true;
            case "run_conflict":
                if (Connection != ReportStreamConnection.Closed)
                {
                    End(new ReportStreamAction.Close(), actions);
                }

                if (++_runConflicts >= 2)
                {
                    Halt(t, ReportStreamStop.ClientState, actions);
                }
                else
                {
                    ReconnectAt = t;
                }

                return true;
            case "version_unsupported":
                actions.Add(new ReportStreamAction.React(ReportStreamReaction.RefreshDiscovery));
                Halt(t, ReportStreamStop.Upgrade, actions);
                return true;
            case "payload_too_large":
                actions.Add(new ReportStreamAction.React(ReportStreamReaction.RefreshDiscovery));
                if (++_oversize >= 3)
                {
                    Halt(t, ReportStreamStop.Incompatible, actions);
                    return true;
                }

                return false;
            case "report_stale":
                return false;
        }

        if (code == "rate_limited" || (status == 503 && retryAfterSeconds is not null))
        {
            if (Connection != ReportStreamConnection.Closed)
            {
                End(new ReportStreamAction.Abort(), actions);
            }

            Wait(t, retryAfterSeconds, actions);
            return true;
        }

        if (code == "gone")
        {
            Halt(t, ReportStreamStop.Gone, actions);
            return true;
        }

        if (status is >= 400 and < 500)
        {
            if (++_rejectStrikes >= 3)
            {
                Halt(t, ReportStreamStop.Rejected, actions);
                return true;
            }

            return false;
        }

        if (Connection != ReportStreamConnection.Closed)
        {
            End(new ReportStreamAction.Abort(), actions);
        }

        Failure(t, actions);
        return true;
    }

    private void NewRun(long t)
    {
        (Seq, _windowStart, _observed, _dirty) = (0, t, null, false);
        (_lastBuild, _lastActivity) = (null, null);
    }

    private void Discard(long t, List<ReportStreamAction> actions, bool keepingTick = false)
    {
        (_windowStart, _observed) = (t, null);
        actions.Add(new ReportStreamAction.Discard(keepingTick));
    }

    /// <summary>The connection ends: every awaited report is dropped as unknown (never resent).</summary>
    private void End(ReportStreamAction? action, List<ReportStreamAction> actions)
    {
        if (action is not null)
        {
            actions.Add(action);
        }

        foreach (var report in _awaiting)
        {
            actions.Add(new ReportStreamAction.Dropped(report.Seq, ReportStreamDrop.Unknown));
            Counters.Unknown++;
        }

        _awaiting.Clear();
        (Connection, _deadline, _readyAt) = (ReportStreamConnection.Closed, null, null);
    }

    private void Failure(long t, List<ReportStreamAction> actions)
    {
        Failures++;
        var delay = BackoffMs[Math.Min(Failures, BackoffMs.Count) - 1];
        ReconnectAt = t + (long)Math.Round(delay * _jitter());
        actions.Add(new ReportStreamAction.Backoff(ReconnectAt));
    }

    private void Wait(long t, int? seconds, List<ReportStreamAction> actions)
    {
        var wait = seconds is { } given ? Math.Clamp(given, 5, 3600) : 60;
        RateLimitedUntil = Math.Max(RateLimitedUntil, t + wait * 1000L);
        actions.Add(new ReportStreamAction.Wait(RateLimitedUntil));
    }

    private void Halt(long t, ReportStreamStop reason, List<ReportStreamAction> actions)
    {
        Stop = reason;
        actions.Add(new ReportStreamAction.Stop(reason));
        if (Connection != ReportStreamConnection.Closed)
        {
            End(new ReportStreamAction.Abort(), actions);
        }
    }

    /// <summary>Builds <c>leaving</c> when a slot is free, writes it and closes within a second.</summary>
    private void Leave(long t, List<ReportStreamAction> actions)
    {
        if (_awaiting.Count < Limits.MaxInFlight)
        {
            Build(t, ReportActivity.Leaving, actions);
        }

        (Connection, _deadline) = (ReportStreamConnection.Closing, t + ClosingMs);
        if (_awaiting.Count == 0)
        {
            End(new ReportStreamAction.Close(), actions);
        }
    }

    private void Build(long t, ReportActivity activity, List<ReportStreamAction> actions)
    {
        Seq++;
        var ticket = new ReportStreamTicket(Seq, activity, t, t - (_windowStart ?? t), _observed);
        actions.Add(new ReportStreamAction.Build(ticket));
        Counters.Built++;
        (_windowStart, _observed, _dirty) = (t, null, false);
        (_lastBuild, _lastActivity) = (t, activity);
        if (ticket.ObservationAgeMs > StaleMs)
        {
            actions.Add(new ReportStreamAction.Dropped(ticket.Seq, ReportStreamDrop.Stale));
            Counters.Stale++;
            return;
        }

        _awaiting.Add(new Awaited(ticket.Seq, ticket.Observed, t));
        actions.Add(new ReportStreamAction.Write(ticket.Seq));
    }

    private void ResetStrikes() => (_rejectStrikes, _nonceStrikes, _signatureStrikes, _runConflicts, _oversize) = (0, 0, 0, 0, 0);

    private static bool SameActivity(ReportMode mode, ReportActivity? activity) =>
        (mode, activity) is (ReportMode.Active, ReportActivity.Active) or (ReportMode.Idle, ReportActivity.Idle);

    private readonly record struct Awaited(int Seq, long? Observed, long Written);
}
