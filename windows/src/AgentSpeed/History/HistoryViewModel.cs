using System.Collections.ObjectModel;
using System.Windows.Media;
using System.Windows.Threading;
using AgentSpeed.Controls;
using AgentSpeed.Core;
using AgentSpeed.Presentation.Colors;
using AgentSpeed.Presentation.Formatting;
using AgentSpeed.Presentation.History;
using AgentSpeed.Presentation.Localization;
using AgentSpeed.Theme;

namespace AgentSpeed.History;

/// <summary>Why the window's data changed: a new filter or range animates; live data and texts update in place.</summary>
internal enum HistoryUpdate
{
    Filter,
    Live,
    Sort,
    Text,
}

/// <summary>
/// State of the Usage History window (DESIGN 4b): the filter, the range and everything derived from the hub's days.
/// Live updates from the hub are coalesced to at most four recomputations per second; today shown on its own is
/// re-read quietly every 30 seconds while the window is on screen.
/// </summary>
internal sealed class HistoryViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan CoalesceInterval = TimeSpan.FromMilliseconds(250);

    private readonly HistoryHub _hub;
    private readonly DispatcherTimer _pending;
    private readonly DispatcherTimer _clock;
    private readonly DispatcherTimer _messageTimer;
    private readonly DispatcherTimer _liveDay;
    private Localizer _loc;
    private long _lastRecompute;
    private SourceKind? _source;
    private HistoryRangeKind _rangeKind = HistoryRangeKind.Days30;
    private DayRange? _custom;
    private HistoryColumn _sortColumn = HistoryColumn.Date;
    private bool _sortDescending = true;
    private bool _syncingSources;
    private SourceOption? _selectedSource;
    private string _subtitle = string.Empty;
    private bool _isEmpty = true;
    private bool _isAnalyzing;
    private bool _isFailed;
    private double _progress;
    private string _stateText = string.Empty;
    private string _filesText = string.Empty;
    private string _message = string.Empty;
    private IReadOnlyList<LegendItem> _legend = [];
    private bool _isSingleDay;
    private bool _isInProgress;
    private string _chartTitle = string.Empty;
    private bool _hasBreakdown;
    private bool _shown;
    private DateTimeOffset? _quietSince;
    private bool _disposed;

    public HistoryViewModel(HistoryHub hub, Localizer loc, Dispatcher dispatcher)
    {
        _hub = hub;
        _loc = loc;
        _pending = new DispatcherTimer(CoalesceInterval, DispatcherPriority.Background, (_, _) =>
        {
            _pending!.Stop();
            Recompute(HistoryUpdate.Live);
        }, dispatcher);
        _pending.Stop();
        _clock = new DispatcherTimer(TimeSpan.FromSeconds(30), DispatcherPriority.Background, (_, _) => OnClock(), dispatcher);
        _messageTimer = new DispatcherTimer(TimeSpan.FromSeconds(6), DispatcherPriority.Background, (_, _) =>
        {
            _messageTimer!.Stop();
            Message = string.Empty;
        }, dispatcher);
        _messageTimer.Stop();
        _liveDay = new DispatcherTimer(LiveDay.RefreshInterval, DispatcherPriority.Background, (_, _) => RefreshLiveDay(), dispatcher);
        _liveDay.Stop();
        Cards = Enum.GetValues<SummaryMetric>().Select(m => new SummaryCardView(m)).ToArray();
        RetryCommand = new RelayCommand(() =>
        {
            _quietSince = null;
            _hub.Refresh();
        });
        _hub.Changed += OnHubChanged;
        Recompute(HistoryUpdate.Filter);
    }

    /// <summary>After every recomputation, on the UI thread.</summary>
    public event EventHandler<HistoryUpdate>? Updated;

    public Localizer Loc => _loc;

    public DateOnly Today { get; private set; }

    public SourceKind? Source => _source;

    public HistoryRangeKind RangeKind => _rangeKind;

    public DayRange Range { get; private set; }

    /// <summary>First and last day with data for the current filter (all sources when the filter has none).</summary>
    public DayRange? DataBounds { get; private set; }

    /// <summary>Days with data in the range, oldest first.</summary>
    public IReadOnlyList<DaySummary> Days { get; private set; } = [];

    /// <summary>The table rows: <see cref="Days"/> in the chosen sort order.</summary>
    public IReadOnlyList<DaySummary> Rows { get; private set; } = [];

    public HistoryBars Bars { get; private set; } = HistoryBars.Empty(DayRange.Single(DateOnly.FromDateTime(DateTime.Now)));

    public HistoryColumn SortColumn => _sortColumn;

    public bool SortDescending => _sortDescending;

    public ObservableCollection<SourceOption> SourceOptions { get; } = new();

    public IReadOnlyList<SummaryCardView> Cards { get; }

    public RelayCommand RetryCommand { get; }

    public SourceOption? SelectedSource
    {
        get => _selectedSource;
        set
        {
            if (value is null || _syncingSources || ReferenceEquals(value, _selectedSource))
            {
                return;
            }

            _selectedSource = value;
            OnPropertyChanged();
            if (value.Kind != _source)
            {
                _source = value.Kind;
                Recompute(HistoryUpdate.Filter);
            }
        }
    }

    public string Subtitle { get => _subtitle; private set => Set(ref _subtitle, value); }

    public bool IsEmpty { get => _isEmpty; private set => Set(ref _isEmpty, value); }

    public IReadOnlyList<LegendItem> Legend { get => _legend; private set => Set(ref _legend, value); }

    /// <summary>The bars of a single day by source (all sources) or by model (one source), largest first.</summary>
    public ObservableCollection<BreakdownRowView> Breakdown { get; } = new();

    /// <summary>One day is shown: the breakdown replaces the daily chart.</summary>
    public bool IsSingleDay { get => _isSingleDay; private set => Set(ref _isSingleDay, value); }

    /// <summary>The single day shown is today, whose figures are still growing.</summary>
    public bool IsInProgress { get => _isInProgress; private set => Set(ref _isInProgress, value); }

    /// <summary><c>每日輸出</c>, or <c>來源分布</c> / <c>模型分布</c> for one day.</summary>
    public string ChartTitle { get => _chartTitle; private set => Set(ref _chartTitle, value); }

    /// <summary>The single day has bars to show (a day with input but no output has none).</summary>
    public bool HasBreakdown { get => _hasBreakdown; private set => Set(ref _hasBreakdown, value); }

    public bool IsAnalyzing { get => _isAnalyzing; private set => Set(ref _isAnalyzing, value); }

    public bool IsFailed { get => _isFailed; private set => Set(ref _isFailed, value); }

    /// <summary>Analysis progress, 0..1.</summary>
    public double Progress { get => _progress; private set => Set(ref _progress, value); }

    /// <summary>Analyzing / updated / failed line of the footer; empty before the first analysis.</summary>
    public string StateText { get => _stateText; private set => Set(ref _stateText, value); }

    public string FilesText { get => _filesText; private set => Set(ref _filesText, value); }

    /// <summary>A short-lived notice (export failure) shown in the footer instead of the state.</summary>
    public string Message
    {
        get => _message;
        private set
        {
            if (Set(ref _message, value))
            {
                OnPropertyChanged(nameof(HasMessage));
            }
        }
    }

    public bool HasMessage => !string.IsNullOrEmpty(_message);

    /// <summary>The window is on screen (visible and not minimized); only then is today re-read.</summary>
    public void SetShown(bool shown)
    {
        if (_disposed || shown == _shown)
        {
            return;
        }

        _shown = shown;
        UpdateLiveDayTimer();
        RefreshLiveDay();
    }

    /// <summary>Re-reads today's logs quietly when today is shown on its own and the window is on screen.</summary>
    public void RefreshLiveDay()
    {
        if (_disposed || !_shown || !LiveDay.Shows(Range, Today) || _hub.RefreshHandler is null || !LiveDay.CanRefreshQuietly(_hub.State))
        {
            return;
        }

        _quietSince = DateTimeOffset.Now;
        _hub.Refresh();
    }

    public void SetSource(SourceKind? source)
    {
        if (source == _source && _selectedSource is not null)
        {
            return;
        }

        _source = source;
        Recompute(HistoryUpdate.Filter);
    }

    public void SetRange(HistoryRangeKind kind)
    {
        if (kind == HistoryRangeKind.Custom && _custom is null)
        {
            return;
        }

        if (kind == _rangeKind)
        {
            return;
        }

        _rangeKind = kind;
        Recompute(HistoryUpdate.Filter);
        RefreshLiveDay();
    }

    public void ApplyCustom(DateOnly from, DateOnly to)
    {
        var bounds = DataBounds ?? DayRange.Single(Today);
        _custom = HistoryRanges.Clamp(from, to, bounds.Start < Today ? bounds.Start : Today, Today);
        _rangeKind = HistoryRangeKind.Custom;
        Recompute(HistoryUpdate.Filter);
        RefreshLiveDay();
    }

    public void SortBy(HistoryColumn column)
    {
        if (column == _sortColumn)
        {
            _sortDescending = !_sortDescending;
        }
        else
        {
            _sortColumn = column;
            _sortDescending = HistoryTable.DefaultDescending(column);
        }

        Rows = HistoryTable.Sort(Days, _sortColumn, _sortDescending);
        Updated?.Invoke(this, HistoryUpdate.Sort);
    }

    public void Relocalize(Localizer loc)
    {
        _loc = loc;
        Recompute(HistoryUpdate.Text);
    }

    /// <summary>The rows as they are shown, as CSV bytes for Excel, with a suggested file name.</summary>
    public (string FileName, byte[] Bytes) ExportCsv() =>
        (HistoryCsv.FileName(Range, _source), HistoryCsv.Encode(HistoryCsv.Build(Rows, _source)));

    public void ShowMessage(string text)
    {
        Message = text;
        _messageTimer.Stop();
        _messageTimer.Start();
    }

    /// <summary>The day under index <paramref name="index"/> of the range, when it has data.</summary>
    public DaySummary? DayAt(int index)
    {
        if (index < 0 || index >= Range.DayCount)
        {
            return null;
        }

        var day = Range.DayAt(index);
        var lo = 0;
        var hi = Days.Count - 1;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            var cmp = Days[mid].Day.CompareTo(day);
            if (cmp == 0)
            {
                return Days[mid];
            }

            if (cmp < 0)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _hub.Changed -= OnHubChanged;
        _pending.Stop();
        _clock.Stop();
        _messageTimer.Stop();
        _liveDay.Stop();
    }

    private void OnHubChanged(object? sender, EventArgs e)
    {
        if (!_disposed)
        {
            ScheduleRecompute();
        }
    }

    private void ScheduleRecompute()
    {
        if (Environment.TickCount64 - _lastRecompute >= CoalesceInterval.TotalMilliseconds)
        {
            _pending.Stop();
            Recompute(HistoryUpdate.Live);
        }
        else if (!_pending.IsEnabled)
        {
            _pending.Start();
        }
    }

    private void OnClock()
    {
        if (DateOnly.FromDateTime(DateTime.Now) != Today)
        {
            Recompute(HistoryUpdate.Live);
        }
        else
        {
            UpdateState();
        }
    }

    private void Recompute(HistoryUpdate kind)
    {
        _lastRecompute = Environment.TickCount64;
        Today = DateOnly.FromDateTime(DateTime.Now);
        var usage = _hub.Days;
        DataBounds = HistoryAggregator.Bounds(usage, _source) ?? HistoryAggregator.Bounds(usage);
        Range = HistoryRanges.Resolve(_rangeKind, Today, DataBounds?.Start, _custom);
        Days = WithoutSourcePeaks(HistoryAggregator.Daily(usage, _source, Range));
        var previous = HistoryAggregator.Daily(usage, _source, HistoryRanges.Previous(Range));
        Bars = HistoryBars.From(Days, Range, _source);
        Rows = HistoryTable.Sort(Days, _sortColumn, _sortDescending);
        IsEmpty = Days.Count == 0;
        Subtitle = HistoryText.RangeSubtitle(Range, Today, _loc);
        IsSingleDay = Range.IsSingleDay;
        IsInProgress = Range.IsSingleDay && Range.Start == Today;
        UpdateBreakdown(DayBreakdown.For(usage, Range, _source), rebuild: kind == HistoryUpdate.Filter);

        var sparkBrush = _source is { } one ? HeatPalette.SourceColor(one).ToBrush() : null;
        var sparkDays = Range.IsSingleDay ? HistoryAggregator.Daily(usage, _source, HistoryRanges.SparkRange(Range)) : null;
        var cards = HistorySummary.Build(Days, previous, Range, Today, _loc, sparkDays);
        for (var i = 0; i < Cards.Count && i < cards.Count; i++)
        {
            var card = cards[i];
            var value = string.IsNullOrEmpty(card.Unit) ? card.Value : $"{card.Value} {card.Unit}";
            var accessible = HistoryText.JoinParts([HistoryText.Labelled(card.Label, value, _loc), card.Secondary], _loc);
            Cards[i].Apply(card, sparkBrush, HistoryText.Labelled(card.Label, card.FullValue, _loc), accessible);
        }

        Legend = Bars.Order.Count == 0 && _source is { } empty
            ? [new LegendItem(_loc[DisplayText.SourceKey(empty)], HeatPalette.SourceColor(empty).ToBrush())]
            : Bars.Order.Select(s => new LegendItem(_loc[DisplayText.SourceKey(s)], HeatPalette.SourceColor(s).ToBrush())).ToArray();
        UpdateSources(usage);
        UpdateState();
        UpdateLiveDayTimer();
        if (!_clock.IsEnabled)
        {
            _clock.Start();
        }

        Updated?.Invoke(this, kind);
    }

    /// <summary>
    /// Updates the breakdown bars in place (matched by source or model) so they grow to new lengths; a new filter or range
    /// rebuilds them so they grow from nothing.
    /// </summary>
    private void UpdateBreakdown(DayBreakdown? breakdown, bool rebuild)
    {
        ChartTitle = _loc[breakdown?.TitleKey ?? "history.chart.output"];
        HasBreakdown = breakdown is { Rows.Count: > 0 };
        if (rebuild || breakdown is null)
        {
            Breakdown.Clear();
        }

        if (breakdown is null)
        {
            return;
        }

        var existing = Breakdown.ToDictionary(r => r.Key, StringComparer.Ordinal);
        var inProgress = breakdown.Day == Today;
        for (var i = 0; i < breakdown.Rows.Count; i++)
        {
            var row = breakdown.Rows[i];
            if (!existing.Remove(row.Key, out var view))
            {
                view = new BreakdownRowView(row.Key);
                Breakdown.Insert(Math.Min(i, Breakdown.Count), view);
            }
            else if (Breakdown.IndexOf(view) is var at && at != i)
            {
                Breakdown.Move(at, i);
            }

            view.Apply(row, breakdown.Kind, inProgress, _loc);
        }

        foreach (var stale in existing.Values)
        {
            Breakdown.Remove(stale);
        }
    }

    /// <summary>A stored peak is the day's overall peak, so a single source never shows it (METRICS 8).</summary>
    private IReadOnlyList<DaySummary> WithoutSourcePeaks(IReadOnlyList<DaySummary> days) =>
        _source is null ? days : days.Select(d => d.Peak is null ? d : d with { Peak = null }).ToArray();

    private void UpdateSources(IReadOnlyList<DailyUsage> usage)
    {
        var withHistory = HistoryAggregator.SourcesWithHistory(usage);
        var kinds = new List<SourceKind?> { null };
        kinds.AddRange(Enum.GetValues<SourceKind>()
            .Where(k => withHistory.Contains(k) || k == _source)
            .Select(k => (SourceKind?)k));

        _syncingSources = true;
        try
        {
            if (!SourceOptions.Select(o => o.Kind).SequenceEqual(kinds))
            {
                SourceOptions.Clear();
                foreach (var kind in kinds)
                {
                    SourceOptions.Add(new SourceOption(kind));
                }
            }

            var allColors = (withHistory.Count > 0 ? withHistory : Enum.GetValues<SourceKind>())
                .Select(k => HeatPalette.SourceColor(k).ToColor())
                .ToArray();
            foreach (var option in SourceOptions)
            {
                option.Name = option.Kind is { } k ? _loc[DisplayText.SourceKey(k)] : _loc["history.allSources"];
                option.Colors = option.Kind is { } c ? [HeatPalette.SourceColor(c).ToColor()] : allColors;
            }
        }
        finally
        {
            _syncingSources = false;
        }

        var selected = SourceOptions.FirstOrDefault(o => o.Kind == _source) ?? SourceOptions[0];
        if (!ReferenceEquals(selected, _selectedSource))
        {
            _selectedSource = selected;
            OnPropertyChanged(nameof(SelectedSource));
        }
    }

    private void UpdateLiveDayTimer()
    {
        var run = _shown && !_disposed && LiveDay.Shows(Range, Today);
        if (run != _liveDay.IsEnabled)
        {
            _liveDay.IsEnabled = run;
        }
    }

    /// <summary>The footer line; a quiet refresh of today keeps the last line instead of showing its progress.</summary>
    private void UpdateState()
    {
        var state = _hub.State;
        if (_quietSince is { } since && LiveDay.EndsQuiet(state, since, DateTimeOffset.Now))
        {
            _quietSince = null;
        }

        if (LiveDay.HidesState(state, _quietSince is not null))
        {
            return;
        }

        IsAnalyzing = state.Kind == HistoryStateKind.Analyzing;
        IsFailed = state.Kind == HistoryStateKind.Failed;
        Progress = Math.Clamp(state.Progress, 0, 1);
        StateText = state.Kind switch
        {
            HistoryStateKind.Analyzing => HistoryText.Analyzing(state.Progress, _loc),
            HistoryStateKind.Ready => HistoryText.Updated(state.UpdatedAt ?? DateTimeOffset.Now, DateTimeOffset.Now, _loc),
            HistoryStateKind.Failed => _loc["win.history.failed"],
            _ => string.Empty,
        };
        FilesText = state.Kind == HistoryStateKind.Analyzing && state.FilesTotal > 0
            ? HistoryText.AnalyzingFiles(state.FilesDone, state.FilesTotal, _loc)
            : string.Empty;
    }
}
