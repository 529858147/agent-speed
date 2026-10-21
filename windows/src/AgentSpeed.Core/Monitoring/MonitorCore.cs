using System.Diagnostics;
using AgentSpeed.Core.Engine;
using AgentSpeed.Core.IO;
using AgentSpeed.Core.Parsing;

namespace AgentSpeed.Core.Monitoring;

internal interface IResponseObserver
{
    void OnResponse(ParsedResponse response, bool isCompletion, bool seeded);
}

internal sealed record SeedReport(long ElapsedMs, int FilesDiscovered, int FilesRead, long BytesRead, int Responses);

/// <summary>
/// Owns discovery, tailing, parsing and the engine for one configuration (METRICS 2 and 5). Single-threaded:
/// every public member must be called from the monitor thread; only seeding fans file reads out to the pool.
/// </summary>
internal sealed class MonitorCore
{
    public const long BacklogWindowMs = 15 * 60_000;
    public const long LargeFileBytes = 256L * 1024 * 1024;
    public const long LargeFileTailBytes = 64L * 1024 * 1024;
    public const long RecentWriteWindowMs = 2 * 60_000;

    private static readonly StringComparer PathComparer =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private readonly IReadOnlySet<SourceKind> _enabled;
    private readonly IResponseObserver? _observer;
    private readonly Deduper _deduper = new();
    private readonly SpeedEngine _engine;
    private readonly Dictionary<string, TrackedFile> _files = new(PathComparer);
    private readonly Dictionary<string, AgentLink> _links = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _parents = new(StringComparer.Ordinal);
    private readonly Collector _collector = new();
    private bool _linksChanged;
    private long _removedMalformedLines;
    private long _removedOversizedLines;
    private int _responsesSeen;
    private bool _partialHistory;

    public MonitorCore(
        MonitorConfiguration configuration,
        string codexRoot,
        string claudeRoot,
        string dshRoot,
        long todayStartMs,
        IResponseObserver? observer = null)
    {
        _enabled = configuration.EnabledSources;
        _observer = observer;
        _engine = new SpeedEngine(todayStartMs);
        CodexRoot = new SourceRoot(RootFamily.Codex, codexRoot, _enabled.Contains(SourceKind.Codex));
        ClaudeRoot = new SourceRoot(
            RootFamily.Claude,
            claudeRoot,
            _enabled.Contains(SourceKind.ClaudeCode) || _enabled.Contains(SourceKind.ClaudeDesktop));
        DshRoot = new SourceRoot(RootFamily.DeepSeek, dshRoot, _enabled.Contains(SourceKind.DeepSeek));
        RefreshAvailability();
    }

    public SourceRoot CodexRoot { get; }

    public SourceRoot ClaudeRoot { get; }

    public SourceRoot DshRoot { get; }

    public IEnumerable<SourceRoot> Roots
    {
        get
        {
            yield return CodexRoot;
            yield return ClaudeRoot;
            yield return DshRoot;
        }
    }

    public bool AnyEnabledRootAvailable => Roots.Any(r => r.Enabled && r.Available);

    public int TrackedFileCount => _files.Count;

    public void RefreshAvailability()
    {
        foreach (var root in Roots)
        {
            root.Available = SafeDirectoryExists(root.Path);
        }
    }

    /// <summary>Reads the startup backlog of every enabled, available root (METRICS 5).</summary>
    public SeedReport Seed(long nowMs, long todayStartMs, CancellationToken cancellationToken, Action<double>? progress = null)
    {
        var stopwatch = Stopwatch.StartNew();
        var plans = new List<ReadPlan>();
        var discovered = 0;
        foreach (var root in Roots)
        {
            if (!root.Enabled || root.Seeded)
            {
                continue;
            }

            root.Available = SafeDirectoryExists(root.Path);
            if (!root.Available)
            {
                continue;
            }

            var files = TranscriptDiscovery.Enumerate(root.Path, root.Naming, cancellationToken);
            discovered += files.Count;
            foreach (var file in files)
            {
                if (_files.ContainsKey(file.Path))
                {
                    continue;
                }

                var tracked = new TrackedFile(root, file.Path, _deduper);
                _files[file.Path] = tracked;
                if (PlanInitialRead(tracked, file, todayStartMs) is { } plan)
                {
                    plans.Add(plan);
                }
            }

            root.Seeded = true;
        }

        var (filesRead, bytesRead) = ReadPlans(plans, seeded: true, cancellationToken, progress);
        return new SeedReport(stopwatch.ElapsedMilliseconds, discovered, filesRead, bytesRead, _responsesSeen);
    }

    /// <summary>Handles paths reported by a watcher. Returns true when anything was read.</summary>
    public bool ProcessPaths(IEnumerable<string> paths, long nowMs, long todayStartMs)
    {
        var changed = false;
        foreach (var path in paths)
        {
            if (_files.TryGetValue(path, out var tracked))
            {
                if (!SafeFileExists(path))
                {
                    Remove(tracked);
                    changed = true;
                }
                else
                {
                    changed |= ReadLive(tracked);
                }

                continue;
            }

            foreach (var root in Roots)
            {
                if (root.Enabled && root.Seeded && TranscriptDiscovery.IsAcceptablePath(root.Path, path, root.Naming))
                {
                    changed |= AddRuntimeFile(root, path, todayStartMs);
                    break;
                }
            }
        }

        return changed;
    }

    /// <summary>1-second fallback: stats files modified in the last 2 minutes.</summary>
    public bool PollRecent(long nowMs)
    {
        var cutoff = DateTime.UnixEpoch.AddMilliseconds(nowMs - RecentWriteWindowMs);
        List<TrackedFile>? due = null;
        foreach (var tracked in _files.Values)
        {
            if (tracked.KnownWriteUtc >= cutoff)
            {
                (due ??= []).Add(tracked);
            }
        }

        if (due is null)
        {
            return false;
        }

        var changed = false;
        foreach (var tracked in due)
        {
            var info = new FileInfo(tracked.Path);
            if (!info.Exists)
            {
                Remove(tracked);
                changed = true;
            }
            else if (info.Length != tracked.KnownLength || info.LastWriteTimeUtc != tracked.KnownWriteUtc)
            {
                changed |= ReadLive(tracked);
            }
        }

        return changed;
    }

    /// <summary>10-second full rediscovery; also seeds roots that appeared after startup.</summary>
    public bool Rediscover(long nowMs, long todayStartMs, CancellationToken cancellationToken)
    {
        var changed = false;
        foreach (var root in Roots)
        {
            if (!root.Enabled)
            {
                continue;
            }

            var available = SafeDirectoryExists(root.Path);
            changed |= available != root.Available;
            root.Available = available;
            if (!available)
            {
                continue;
            }

            if (!root.Seeded)
            {
                Seed(nowMs, todayStartMs, cancellationToken);
                changed = true;
                continue;
            }

            var seen = new HashSet<string>(PathComparer);
            foreach (var file in TranscriptDiscovery.Enumerate(root.Path, root.Naming, cancellationToken))
            {
                seen.Add(file.Path);
                if (!_files.TryGetValue(file.Path, out var tracked))
                {
                    changed |= AddRuntimeFile(root, file.Path, todayStartMs, file);
                }
                else if (file.Length != tracked.KnownLength || file.LastWriteUtc != tracked.KnownWriteUtc)
                {
                    changed |= ReadLive(tracked);
                }
            }

            foreach (var tracked in _files.Values.Where(f => f.Root == root && !seen.Contains(f.Path)).ToList())
            {
                Remove(tracked);
                changed = true;
            }
        }

        return changed;
    }

    /// <summary>Files of that source with at least one line today.</summary>
    public int SessionCount(SourceKind kind, long todayStartMs)
    {
        var count = 0;
        foreach (var tracked in _files.Values)
        {
            if (tracked.Parser.Source == kind && tracked.Parser.LastLineMs >= todayStartMs)
            {
                count++;
            }
        }

        return count;
    }

    public IReadOnlyList<MonitorWarning> Warnings(int unfinalizedToday = 0)
    {
        var warnings = new List<MonitorWarning>();
        if (_partialHistory)
        {
            warnings.Add(new MonitorWarning.PartialHistory());
        }

        var unreadable = UnreadableFileCount;
        if (unreadable > 0)
        {
            warnings.Add(new MonitorWarning.UnreadableFiles(unreadable));
        }

        var malformed = MalformedLineCount;
        if (malformed > 0)
        {
            warnings.Add(new MonitorWarning.MalformedLines((int)Math.Min(malformed, int.MaxValue)));
        }

        if (unfinalizedToday > 0)
        {
            warnings.Add(new MonitorWarning.UnfinalizedUsage(unfinalizedToday));
        }

        return warnings;
    }

    public long MalformedLineCount => _removedMalformedLines + _files.Values.Sum(f => f.Parser.MalformedLines);

    public long OversizedLineCount => _removedOversizedLines + _files.Values.Sum(f => f.Tailer.OversizedLines);

    public int UnreadableFileCount => _files.Values.Count(f => f.Unreadable);

    public bool PartialHistory => _partialHistory;

    public (SpeedSnapshot Snapshot, IReadOnlyList<SpeedEvent> Events) BuildSnapshot(
        long nowMs, long todayStartMs, MonitorState state, bool observeLive)
    {
        ResolveParents();
        var frame = _engine.Compute(nowMs, todayStartMs, observeLive);
        var agents = frame.Agents
            .Select(a => new AgentStatus(
                a.Info.Id,
                a.Info.Source,
                a.Info.Project ?? string.Empty,
                a.Info.Model,
                a.Phase,
                a.Info.IsSubagent,
                a.Live,
                a.RecentSpeed,
                DateTimeOffset.FromUnixTimeMilliseconds(a.Info.LastLineMs ?? nowMs),
                a.ParentId))
            .ToList();

        var sources = Enum.GetValues<SourceKind>()
            .Select(kind => BuildSourceStatus(kind, todayStartMs, frame.SourceLive[(int)kind],
                frame.SourceToday[(int)kind], agents.Count(a => a.Source == kind && a.Phase == AgentPhase.Generating)))
            .ToList();

        var snapshot = new SpeedSnapshot(
            DateTimeOffset.FromUnixTimeMilliseconds(nowMs),
            state,
            frame.Live,
            frame.EstimatedShare,
            frame.PeakToday,
            frame.PeakTodayAtMs is { } at ? DateTimeOffset.FromUnixTimeMilliseconds(at) : null,
            frame.AverageToday,
            frame.Today,
            sources,
            agents,
            frame.GeneratingCount,
            frame.ToolsCount,
            frame.History,
            frame.LastResponse,
            Warnings(frame.UnfinalizedToday));
        return (snapshot, frame.Events);
    }

    /// <summary>Snapshot while seeding: no measurements yet, only the state and the sources.</summary>
    public SpeedSnapshot BuildStartingSnapshot(long nowMs, long todayStartMs, double progress)
    {
        var sources = Enum.GetValues<SourceKind>()
            .Select(kind => BuildSourceStatus(kind, todayStartMs, Rate.Zero, TokenTotals.Zero, 0))
            .ToList();
        return SpeedSnapshot.Empty with
        {
            Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(nowMs),
            State = MonitorState.Starting(Math.Clamp(progress, 0, 1)),
            Sources = sources,
        };
    }

    private SourceStatus BuildSourceStatus(SourceKind kind, long todayStartMs, Rate live, TokenTotals today, int activeAgents)
    {
        var root = kind switch
        {
            SourceKind.Codex => CodexRoot,
            SourceKind.DeepSeek => DshRoot,
            _ => ClaudeRoot,
        };
        var enabled = _enabled.Contains(kind);
        return new SourceStatus(
            kind,
            enabled,
            root.Path,
            root.Available,
            root.Enabled ? SessionCount(kind, todayStartMs) : 0,
            enabled ? live : Rate.Zero,
            enabled ? today : TokenTotals.Zero,
            enabled ? activeAgents : 0);
    }

    private ReadPlan? PlanInitialRead(TrackedFile tracked, DiscoveredFile file, long todayStartMs)
    {
        tracked.KnownLength = file.Length;
        tracked.KnownWriteUtc = file.LastWriteUtc;
        var modifiedMs = new DateTimeOffset(DateTime.SpecifyKind(file.LastWriteUtc, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
        if (modifiedMs < todayStartMs - BacklogWindowMs)
        {
            tracked.Tailer.StartAt(file.Length, StartAlignment.SkipPartialLine);
            return null;
        }

        // A frame stream cannot be entered in the middle (METRICS 5.1).
        if (file.Length > LargeFileBytes && tracked.Tailer is ZstdFrameTailer)
        {
            _partialHistory = true;
            tracked.Tailer.StartAt(file.Length);
            return null;
        }

        if (file.Length > LargeFileBytes)
        {
            _partialHistory = true;
            return new ReadPlan(
                tracked, file.LastWriteUtc, file.Length - LargeFileTailBytes, StartAlignment.SkipToNextLine, file.Length);
        }

        return new ReadPlan(tracked, file.LastWriteUtc, 0, StartAlignment.None, file.Length);
    }

    private (int Files, long Bytes) ReadPlans(
        List<ReadPlan> plans, bool seeded, CancellationToken cancellationToken, Action<double>? progress)
    {
        plans.Sort(static (a, b) =>
        {
            var byTime = a.LastWriteUtc.CompareTo(b.LastWriteUtc);
            return byTime != 0 ? byTime : string.CompareOrdinal(a.File.Path, b.File.Path);
        });

        var totalBytes = Math.Max(1, plans.Sum(p => Math.Max(0, p.ExpectedLength - p.StartOffset)));
        var degree = Math.Clamp(Environment.ProcessorCount - 1, 1, 8);
        var inFlight = new Queue<(ReadPlan Plan, Task<Prepared> Task)>();
        long doneBytes = 0, bytesRead = 0;
        var filesRead = 0;
        var next = 0;
        try
        {
            while (next < plans.Count || inFlight.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                while (next < plans.Count && inFlight.Count < degree)
                {
                    var plan = plans[next++];
                    plan.File.Tailer.StartAt(plan.StartOffset, plan.Align);
                    inFlight.Enqueue((plan, Task.Run(() => Prepare(plan.File, cancellationToken), cancellationToken)));
                }

                var (current, task) = inFlight.Dequeue();
                Prepared prepared;
                try
                {
                    prepared = task.GetAwaiter().GetResult();
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception e)
                {
                    HandleReadFailure(current.File, e);
                    continue;
                }

                if (!TryCommit(current.File, prepared, seeded))
                {
                    continue;
                }

                filesRead++;
                bytesRead += prepared.Result.BytesRead;
                doneBytes += Math.Max(0, current.ExpectedLength - current.StartOffset);
                progress?.Invoke((double)doneBytes / totalBytes);
            }
        }
        finally
        {
            if (inFlight.Count > 0)
            {
                try
                {
                    Task.WaitAll([.. inFlight.Select(i => i.Task)], TimeSpan.FromSeconds(5));
                }
                catch (AggregateException)
                {
                }
            }
        }

        return (filesRead, bytesRead);
    }

    private static Prepared Prepare(TrackedFile tracked, CancellationToken cancellationToken)
    {
        var batch = tracked.Parser.NewBatch();
        var result = tracked.Tailer.ReadAppended(batch, cancellationToken);
        return new Prepared(batch, result);
    }

    private bool TryCommit(TrackedFile tracked, Prepared prepared, bool seeded)
    {
        try
        {
            Commit(tracked, prepared, seeded);
            return true;
        }
        catch (Exception)
        {
            tracked.Unreadable = true;
            _collector.Items.Clear();
            return false;
        }
    }

    private void Commit(TrackedFile tracked, Prepared prepared, bool seeded)
    {
        if (prepared.Result.WasReset)
        {
            tracked.Parser.Reset();
        }

        _collector.Items.Clear();
        prepared.Batch.ApplyTo(_collector);
        tracked.KnownLength = prepared.Result.Length;
        tracked.KnownWriteUtc = prepared.Result.LastWriteUtc;
        tracked.Unreadable = false;
        PushAgent(tracked);
        foreach (var (response, isCompletion) in _collector.Items)
        {
            _responsesSeen++;
            if (_enabled.Contains(response.Source))
            {
                _engine.Upsert(response, isCompletion, seeded);
            }

            _observer?.OnResponse(response, isCompletion, seeded);
        }

        _collector.Items.Clear();
    }

    private bool ReadLive(TrackedFile tracked)
    {
        try
        {
            var prepared = Prepare(tracked, CancellationToken.None);
            return TryCommit(tracked, prepared, seeded: false)
                && (prepared.Result.BytesRead > 0 || prepared.Result.WasReset);
        }
        catch (Exception e)
        {
            HandleReadFailure(tracked, e);
            return false;
        }
    }

    private bool AddRuntimeFile(SourceRoot root, string path, long todayStartMs, DiscoveredFile? known = null)
    {
        DiscoveredFile file;
        if (known is { } discovered)
        {
            file = discovered;
        }
        else
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                return false;
            }

            file = new DiscoveredFile(info.FullName, info.Length, info.LastWriteTimeUtc);
        }

        if (_files.ContainsKey(file.Path))
        {
            return false;
        }

        var tracked = new TrackedFile(root, file.Path, _deduper);
        _files[file.Path] = tracked;
        if (PlanInitialRead(tracked, file, todayStartMs) is not { } plan)
        {
            return false;
        }

        tracked.Tailer.StartAt(plan.StartOffset, plan.Align);
        return ReadLive(tracked);
    }

    private void PushAgent(TrackedFile tracked)
    {
        var parser = tracked.Parser;
        if (parser.Version == tracked.PushedVersion)
        {
            return;
        }

        tracked.PushedVersion = parser.Version;
        if (parser.LastLineMs is null)
        {
            return;
        }

        if (!_enabled.Contains(parser.Source))
        {
            _engine.RemoveAgent(parser.AgentId);
            _linksChanged |= _links.Remove(parser.AgentId);
            return;
        }

        var link = AgentLink.Of(parser);
        if (!_links.TryGetValue(parser.AgentId, out var known) || known != link)
        {
            _links[parser.AgentId] = link;
            _linksChanged = true;
        }

        _engine.UpdateAgent(new AgentInfo(
            parser.AgentId,
            parser.Source,
            parser.Project,
            parser.Model,
            parser.IsSubagent,
            parser.Phase,
            parser.InFlightStartMs,
            parser.LastLineMs,
            _parents.GetValueOrDefault(parser.AgentId)));
    }

    /// <summary>Re-resolves parent links over the engine's agents after any of them changed (METRICS 6.7).</summary>
    private void ResolveParents()
    {
        if (!_linksChanged)
        {
            return;
        }

        _linksChanged = false;
        var resolved = ParentLinks.Resolve(_links.Values, PathComparer);
        foreach (var id in _links.Keys)
        {
            var parent = resolved.GetValueOrDefault(id);
            if (!string.Equals(parent, _parents.GetValueOrDefault(id), StringComparison.Ordinal))
            {
                _engine.SetParent(id, parent);
            }
        }

        _parents.Clear();
        foreach (var (id, parent) in resolved)
        {
            _parents[id] = parent;
        }
    }

    private void HandleReadFailure(TrackedFile tracked, Exception error)
    {
        if (error is FileNotFoundException or DirectoryNotFoundException)
        {
            Remove(tracked);
        }
        else
        {
            tracked.Unreadable = true;
        }
    }

    private void Remove(TrackedFile tracked)
    {
        if (_files.Remove(tracked.Path))
        {
            _removedMalformedLines += tracked.Parser.MalformedLines;
            _removedOversizedLines += tracked.Tailer.OversizedLines;
        }
    }

    private static bool SafeDirectoryExists(string path)
    {
        try
        {
            return path.Length > 0 && Directory.Exists(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    private static bool SafeFileExists(string path)
    {
        try
        {
            return File.Exists(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    private readonly record struct ReadPlan(
        TrackedFile File, DateTime LastWriteUtc, long StartOffset, StartAlignment Align, long ExpectedLength);

    private readonly record struct Prepared(ILineBatch Batch, TailReadResult Result);

    private sealed class Collector : IResponseSink
    {
        public List<(ParsedResponse Response, bool IsCompletion)> Items { get; } = [];

        public void OnResponse(ParsedResponse response, bool isCompletion) => Items.Add((response, isCompletion));
    }
}
