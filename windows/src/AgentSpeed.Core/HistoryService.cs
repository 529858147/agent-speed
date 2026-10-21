using System.Diagnostics;
using System.Globalization;
using AgentSpeed.Core.History;
using AgentSpeed.Core.IO.Sqlite;
using AgentSpeed.Core.Monitoring;

namespace AgentSpeed.Core;

/// <summary>
/// Keeps the daily usage history (METRICS 8) on a below-normal-priority background thread and publishes it to a
/// <see cref="HistoryHub"/>. Runs at start, every 10 minutes, after local midnight and on <see cref="HistoryHub.Refresh"/>;
/// <see cref="HistoryHub.Clear"/> deletes the store and recomputes. Never touches the live monitor.
/// </summary>
public sealed class HistoryService : IDisposable
{
    private static readonly TimeSpan RunInterval = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan MidnightDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan StateInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan PartialDaysInterval = TimeSpan.FromSeconds(2);

    /// <summary>A run that takes longer than this shows its progress; quick periodic runs stay quiet.</summary>
    private static readonly TimeSpan QuietRunLimit = TimeSpan.FromSeconds(1);

    private const long BigRunBytes = 64L * 1024 * 1024;

    private readonly HistoryHub _hub;
    private readonly string _storePath;
    private readonly TimeZoneInfo? _zone;
    private readonly HistoryOptions _options;
    private readonly object _gate = new();
    private readonly AutoResetEvent _wake = new(false);
    private readonly Dictionary<string, double> _livePeaks = new(StringComparer.Ordinal);
    private MonitorConfiguration _configuration;
    private Thread? _thread;
    private CancellationTokenSource? _stop;
    private CancellationTokenSource? _run;
    private bool _refreshRequested;
    private bool _clearRequested;
    private bool _disposed;

    public HistoryService(HistoryHub hub, MonitorConfiguration configuration, string? storePath = null)
        : this(hub, configuration, storePath, zone: null, options: null)
    {
    }

    internal HistoryService(
        HistoryHub hub, MonitorConfiguration configuration, string? storePath, TimeZoneInfo? zone, HistoryOptions? options)
    {
        ArgumentNullException.ThrowIfNull(hub);
        ArgumentNullException.ThrowIfNull(configuration);
        _hub = hub;
        _configuration = configuration;
        _storePath = string.IsNullOrWhiteSpace(storePath) ? DefaultStorePath : Path.GetFullPath(storePath);
        _zone = zone;
        _options = options ?? new HistoryOptions();
    }

    /// <summary><c>%LOCALAPPDATA%\AgentSpeed\history.sqlite</c>.</summary>
    public static string DefaultStorePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AgentSpeed", "history.sqlite");

    public string StorePath => _storePath;

    public void Start()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_thread is not null)
            {
                return;
            }

            var stop = new CancellationTokenSource();
            _stop = stop;
            _hub.RefreshHandler = Refresh;
            _hub.ClearHandler = Clear;
            _thread = new Thread(() => Run(stop.Token))
            {
                IsBackground = true,
                Name = "Agent Speed history",
                Priority = ThreadPriority.BelowNormal,
            };
            _thread.Start();
        }
    }

    public void Stop()
    {
        Thread? thread;
        CancellationTokenSource? stop;
        lock (_gate)
        {
            thread = _thread;
            stop = _stop;
            _thread = null;
            _stop = null;
            CancelRun();
            if (_hub.RefreshHandler == Refresh)
            {
                _hub.RefreshHandler = null;
            }

            if (_hub.ClearHandler == Clear)
            {
                _hub.ClearHandler = null;
            }
        }

        if (thread is null || stop is null)
        {
            return;
        }

        stop.Cancel();
        Signal();
        if (thread != Thread.CurrentThread && thread.Join(TimeSpan.FromSeconds(10)))
        {
            stop.Dispose();
        }
    }

    /// <summary>Applies new settings; the history is analysed again in the background when anything changed.</summary>
    public void Update(MonitorConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        lock (_gate)
        {
            if (configuration.Equals(_configuration))
            {
                return;
            }

            _configuration = configuration;
            _refreshRequested = true;
            CancelRun();
        }

        Signal();
    }

    /// <summary>
    /// Records the live peak of today from a running snapshot (call it for live snapshots, not previews); stored with
    /// the next run.
    /// </summary>
    public void ObserveLive(SpeedSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.State.Kind != MonitorStateKind.Running || snapshot.Timestamp == DateTimeOffset.MinValue)
        {
            return;
        }

        var peak = snapshot.PeakToday.Output;
        if (!double.IsFinite(peak) || peak <= 0)
        {
            return;
        }

        var day = TimeZoneInfo.ConvertTime(snapshot.Timestamp, _zone ?? TimeZoneInfo.Local)
            .Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        lock (_livePeaks)
        {
            if (peak > _livePeaks.GetValueOrDefault(day))
            {
                _livePeaks[day] = peak;
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        Stop();
        _wake.Dispose();
    }

    private void Refresh()
    {
        lock (_gate)
        {
            _refreshRequested = true;
        }

        Signal();
    }

    private void Clear()
    {
        lock (_gate)
        {
            _clearRequested = true;
            CancelRun();
        }

        Signal();
    }

    private void CancelRun()
    {
        try
        {
            _run?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void Signal()
    {
        try
        {
            _wake.Set();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void Run(CancellationToken stopToken)
    {
        HistoryStore? store = null;
        HistoryAnalyzer? analyzer = null;
        var first = true;
        try
        {
            while (!stopToken.IsCancellationRequested)
            {
                bool clear;
                MonitorConfiguration configuration;
                lock (_gate)
                {
                    clear = _clearRequested;
                    _clearRequested = false;
                    _refreshRequested = false;
                    configuration = _configuration;
                }

                var nextDelay = RunInterval;
                try
                {
                    if (clear)
                    {
                        store?.Dispose();
                        store = null;
                        analyzer = null;
                        HistoryStore.Delete(_storePath);
                        lock (_livePeaks)
                        {
                            _livePeaks.Clear();
                        }

                        first = true;
                    }

                    if (analyzer is null)
                    {
                        store = HistoryStore.Open(_storePath);
                        analyzer = new HistoryAnalyzer(store, _options);
                    }

                    var completed = Analyze(analyzer, configuration, first, stopToken);
                    first &= !completed;
                }
                catch (OperationCanceledException) when (stopToken.IsCancellationRequested)
                {
                    break;
                }
                catch (OperationCanceledException)
                {
                    // Superseded by a configuration change or a clear; the next run starts right away.
                }
                catch (Exception e)
                {
                    // Never let the background thread take the app down; report and retry later.
                    store?.Dispose();
                    store = null;
                    analyzer = null;
                    _hub.Publish(HistoryState.Failed(FailureMessage(e)));
                    nextDelay = RetryDelay;
                }

                WaitForNextRun(nextDelay, stopToken);
            }
        }
        finally
        {
            if (analyzer is not null)
            {
                try
                {
                    analyzer.RecordPeaks(TakeLivePeaks());
                }
                catch (SqliteException)
                {
                }
            }

            store?.Dispose();
        }
    }

    /// <summary>One analysis run; false when it was cancelled before finishing.</summary>
    private bool Analyze(HistoryAnalyzer analyzer, MonitorConfiguration configuration, bool first, CancellationToken stopToken)
    {
        using var run = CancellationTokenSource.CreateLinkedTokenSource(stopToken);
        lock (_gate)
        {
            if (_clearRequested || !_configuration.Equals(configuration))
            {
                return false;
            }

            _run = run;
        }

        try
        {
            var enabled = configuration.EnabledSources;
            var zone = _zone ?? TimeZoneInfo.Local;
            if (first)
            {
                _hub.Publish(HistoryState.Analyzing(0, 0, 0), Days(analyzer, enabled));
            }

            var started = Stopwatch.GetTimestamp();
            var lastState = TimeSpan.Zero;
            var lastDays = TimeSpan.Zero;
            HistoryReport report;
            report = analyzer.Run(SourceLocations.Resolve(configuration), enabled, zone, run.Token, progress =>
            {
                var elapsed = Stopwatch.GetElapsedTime(started);
                var visible = first || progress.BytesTotal >= BigRunBytes || elapsed >= QuietRunLimit;
                if (!visible || elapsed - lastState < StateInterval)
                {
                    return;
                }

                lastState = elapsed;
                var state = HistoryState.Analyzing(progress.Fraction, progress.FilesDone, progress.FilesTotal);
                if (elapsed - lastDays >= PartialDaysInterval)
                {
                    lastDays = elapsed;
                    _hub.Publish(state, Days(analyzer, enabled));
                }
                else
                {
                    _hub.Publish(state);
                }
            });

            analyzer.RecordPeaks(TakeLivePeaks());
            if (report.Cancelled)
            {
                return false;
            }

            _hub.Publish(HistoryState.Ready(DateTimeOffset.Now), Days(analyzer, enabled));
            if (report.BytesAnalyzed >= BigRunBytes)
            {
                // A large analysis decoded gigabytes; return the freed memory instead of keeping it committed.
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            }

            return true;
        }
        finally
        {
            lock (_gate)
            {
                _run = null;
            }
        }
    }

    private List<DailyUsage> Days(HistoryAnalyzer analyzer, IReadOnlySet<SourceKind> enabled)
    {
        Dictionary<string, double> live;
        lock (_livePeaks)
        {
            live = new Dictionary<string, double>(_livePeaks, StringComparer.Ordinal);
        }

        return analyzer.Days(enabled, live);
    }

    private Dictionary<string, double> TakeLivePeaks()
    {
        lock (_livePeaks)
        {
            return new Dictionary<string, double>(_livePeaks, StringComparer.Ordinal);
        }
    }

    private void WaitForNextRun(TimeSpan interval, CancellationToken stopToken)
    {
        var zone = _zone ?? TimeZoneInfo.Local;
        var due = Stopwatch.GetTimestamp();
        while (!stopToken.IsCancellationRequested)
        {
            lock (_gate)
            {
                if (_refreshRequested || _clearRequested)
                {
                    return;
                }
            }

            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var midnight = LocalDay.StartMs(LocalDay.StartMs(now, zone) + 36 * 3_600_000L, zone) - now;
            var untilMidnight = TimeSpan.FromMilliseconds(Math.Max(0, midnight)) + MidnightDelay;
            var remaining = interval - Stopwatch.GetElapsedTime(due);
            var wait = remaining < untilMidnight ? remaining : untilMidnight;
            if (wait <= TimeSpan.Zero)
            {
                return;
            }

            // Wake at least every minute so clock and time zone changes are noticed.
            if (WaitHandle.WaitAny([_wake, stopToken.WaitHandle], wait < TimeSpan.FromMinutes(1) ? wait : TimeSpan.FromMinutes(1))
                == WaitHandle.WaitTimeout && wait <= TimeSpan.FromMinutes(1))
            {
                return;
            }
        }
    }

    private static string FailureMessage(Exception error) => error switch
    {
        SqliteException { IsBusy: true } => "The history store is in use.",
        SqliteException => "The history store could not be opened.",
        _ => "The history store is not accessible.",
    };
}
