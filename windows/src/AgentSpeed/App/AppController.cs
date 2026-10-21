using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using AgentSpeed.Core;
using AgentSpeed.Dashboard;
using AgentSpeed.Interop;
using AgentSpeed.Localization;
using AgentSpeed.Menus;
using AgentSpeed.Orb;
using AgentSpeed.Presentation.Colors;
using AgentSpeed.Presentation.Formatting;
using AgentSpeed.Presentation.Heat;
using AgentSpeed.Presentation.Localization;
using AgentSpeed.Presentation.Motion;
using AgentSpeed.Presentation.Settings;
using AgentSpeed.Presentation.Updates;
using AgentSpeed.Services;
using AgentSpeed.Settings;
using AgentSpeed.Taskbar;
using AgentSpeed.Theme;
using AgentSpeed.Tray;
using AgentSpeed.Updates;

namespace AgentSpeed.App;

/// <summary>Composition root: owns every service and surface, and wires data, settings and theme to them.</summary>
internal sealed class AppController : IAppCommands, IDisposable
{
    private const double HeroDiameter = 112;

    private readonly App _app;
    private readonly SingleInstance _instance;
    private readonly LaunchArguments _launch;
    private Dispatcher _ui = null!;
    private Localizer _loc = null!;
    private ThemeService _theme = null!;
    private SettingsService _settings = null!;
    private SpeedHub _hub = null!;
    private SpeedModel _speed = null!;
    private PreviewService _preview = null!;
    private LiveMonitor? _monitor;
    private ShellMessageWindow? _shell;
    private TrayIcon? _tray;
    private TaskbarWidget? _taskbar;
    private HoverTooltip? _tooltip;
    private MenuHost? _menu;
    private OrbWindow? _orb;
    private DashboardWindow? _dashboard;
    private readonly DashboardViewModel _dashboardModel = new();
    private SettingsWindow? _settingsWindow;
    private SettingsViewModel? _settingsModel;
    private UpdateService _updates = null!;
    private UpdateWindow? _updateWindow;
    private UpdateWindowViewModel? _updateWindowModel;
    private bool _balloonOpensUpdate;
    private string? _updateDoneText;
    private bool _updateDoneShown;
    private DispatcherTimer? _dashboardTimer;
    private bool _dashboardDirty;
    private bool _tooltipShown;
    private long _lastSettingsLiveUpdate;
    private bool _started;
    private bool _quitting;
    private bool _disposed;

    public AppController(App app, SingleInstance instance, LaunchArguments launch)
    {
        _app = app;
        _instance = instance;
        _launch = launch;
    }

    private AppSettings Settings => _settings.Current;

    public void Start()
    {
        _ui = Dispatcher.CurrentDispatcher;
        var phases = new StartupPhases();

        _settings = new SettingsService(_ui);
        _settings.Load();

        _loc = new Localizer(Localizer.LoadEmbeddedTable(), ResolveLanguage(Settings.Language));
        LocalizationSource.Instance.Attach(_loc);
        phases.Mark("settings+strings");

        _theme = new ThemeService(_ui, Settings.Appearance);
        _app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/AgentSpeed;component/Theme/Styles.xaml", UriKind.Absolute),
        });
        AppFonts.Apply(_app.Resources, _loc.LanguageCode);
        phases.Mark("styles");

        _hub = new SpeedHub(new DispatcherSynchronizationContext(_ui));
        _speed = new SpeedModel(_hub, _ui);
        _speed.Configure(Settings.Metric, Settings.Redline);
        _preview = new PreviewService(_hub, _ui);

        _shell = new ShellMessageWindow();
        _shell.TaskbarCreated += (_, _) => _taskbar?.Reembed();
        _shell.DisplayChanged += (_, _) => OnDisplayChanged();
        _shell.SettingChanged += (_, area) => OnSystemSettingChanged(area);
        _shell.SessionEnding += (_, _) => Quit();
        _app.SessionEnding += (_, _) => Quit();

        _tray = new TrayIcon(_shell, _ui);
        _tray.Invoked += (_, _) => ToggleDashboard(TrayAnchor());
        _tray.ContextMenuRequested += (_, _) => ShowMenu();
        _tray.BalloonClicked += (_, _) => OnBalloonClicked();
        _tray.Show(TrayState());
        phases.Mark("tray");

        _updates = new UpdateService(_ui, _settings, AppPaths.Version);
        _updates.Changed += (_, _) => OnUpdatesChanged();
        _updates.NotificationDue += (_, release) => ShowBalloon(UpdateText.AvailableTitle(release, _loc), _loc["update.notification.body"], opensUpdate: true);
        _updates.RelaunchRequested += (_, _) => Quit();

        _menu = new MenuHost();
        _tooltip = new HoverTooltip();

        _taskbar = new TaskbarWidget(_ui);
        _taskbar.Invoked += (_, bounds) => ToggleDashboard(Center(bounds));
        _taskbar.MenuRequested += (_, _) => ShowMenu();
        _taskbar.HoverChanged += (_, hover) => OnTaskbarHover(hover.IsOver, hover.Bounds);
        _taskbar.PresenceChanged += (_, _) => _settingsModel?.UpdateLive(_speed.Snapshot, _taskbar.Presence);
        _taskbar.Start();
        phases.Mark("taskbar");

        _speed.Changed += (_, _) => OnSpeedChanged();
        _speed.EventRaised += (_, e) => OnSpeedEvent(e);
        _settings.Changed += (_, e) => OnSettingsChanged(e.Previous, e.Current);
        _theme.Changed += (_, _) => OnThemeChanged();
        _loc.LanguageChanged += (_, _) => OnLanguageChanged();
        _preview.StateChanged += (_, _) => _dashboard?.SetPreviewing(_preview.IsRunning);

        _dashboardTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (_, _) => FlushDashboard(), _ui);
        _dashboardTimer.Stop();

        if (Settings.ShowOrb)
        {
            ShowOrb();
        }

        phases.Mark("orb");
        UpdateTaskbar();
        StartMonitor();
        phases.Mark("monitor");
        _instance.Listen(() => _ui.BeginInvoke(() => ShowDashboard(CursorAnchor())));
        _started = true;
        AppLog.Info($"Started (language {_loc.LanguageCode}, {(_theme.IsDark ? "dark" : "light")} theme). {phases}");

        // Off the critical path: the Fluent dictionaries (only needed by menus, the flyout and settings) and housekeeping.
        _ui.BeginInvoke(DispatcherPriority.Background, () =>
        {
            var fluent = System.Diagnostics.Stopwatch.StartNew();
            _theme.EnableFluent();
            AutoStart.Refresh();
            AppLog.Info($"Fluent theme ready in {fluent.ElapsedMilliseconds} ms.");
            StartUpdates();
        });
    }

    public void OpenUpdate()
    {
        _updates.Present();
        ShowUpdateWindow();
    }

    public void CheckForUpdates()
    {
        _ = _updates.CheckAsync(manual: true);
        ShowUpdateWindow();
    }

    public void ToggleDashboardFromMenu() => ShowDashboard(CursorAnchor());

    public void UpdateSettings(Func<AppSettings, AppSettings> change) => _settings.Update(change);

    public void OpenSettings(string? page = null)
    {
        _dashboard?.Dismiss();
        if (_settingsWindow is null)
        {
            _settingsModel ??= new SettingsViewModel(_settings, _preview, _loc, initial => SettingsWindow.PickFolder(_settingsWindow, initial),
                _updates, OpenUpdate);
            _settingsModel.ReduceMotion = _theme.ReduceMotion;
            _settingsModel.UpdateLive(_speed.Snapshot, _taskbar?.Presence ?? TaskbarPresence.Hidden);
            _settingsWindow = new SettingsWindow(_settingsModel) { Language = AppFonts.XmlLanguageFor(_loc.LanguageCode) };
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        }

        if (page is not null)
        {
            _settingsWindow.ShowPage(page);
        }

        if (!_settingsWindow.IsVisible)
        {
            _settingsWindow.Show();
        }

        if (_settingsWindow.WindowState == WindowState.Minimized)
        {
            _settingsWindow.WindowState = WindowState.Normal;
        }

        _settingsWindow.Activate();
    }

    public void Quit()
    {
        if (_quitting)
        {
            return;
        }

        _quitting = true;
        AppLog.Info("Quitting.");
        Shutdown();
        _app.Shutdown();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Shutdown();
        _disposed = true;
    }

    private void Shutdown()
    {
        if (_disposed || !_started)
        {
            return;
        }

        _disposed = true;
        Try(() => _settings.Flush());
        Try(() => _preview.Stop());
        Try(() => _monitor?.Dispose());
        Try(() => _taskbar?.Dispose());
        Try(() => _tray?.Dispose());
        Try(() => _dashboardTimer?.Stop());
        Try(() => _menu?.Close());
        Try(() => _tooltip?.Close());
        Try(() => _orb?.Close());
        Try(() => _dashboard?.CloseForShutdown());
        Try(() => _settingsWindow?.Close());
        Try(() => _updateWindow?.Close());
        Try(() => _updates?.Dispose());
        Try(() => _speed.Dispose());
        Try(() => _theme.Dispose());
        Try(() => _shell?.Dispose());
    }

    private static void Try(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            AppLog.Warn("Cleanup step failed.", ex);
        }
    }

    private void StartMonitor()
    {
        try
        {
            _monitor = new LiveMonitor(_hub, Settings.ToMonitorConfiguration());
            _monitor.Start();
        }
        catch (Exception ex)
        {
            AppLog.Error("The live monitor failed to start.", ex);
        }
    }

    private static string ResolveLanguage(AppLanguage language) =>
        LanguageResolver.Resolve(language, CultureInfo.CurrentUICulture);

    // ---- Data flow ----

    private void OnSpeedChanged()
    {
        var snapshot = _speed.Snapshot;
        var value = _speed.Value;
        var tier = _speed.Tier;
        var generating = snapshot.Agents.Where(a => a.Phase == AgentPhase.Generating).Select(a => a.Source).ToArray();
        var accessible = DisplayText.Accessibility(value, tier, _loc);

        if (_orb is { } orb)
        {
            orb.View.SetData(value, tier, generating, accessible);
            orb.View.SetInfoLine(DisplayText.TodaySummary(snapshot, _loc));
            orb.NoteActivity(tier != SpeedTier.Idle);
        }

        if (_dashboard is { IsShown: true } dashboard)
        {
            dashboard.Hero.SetData(value, tier, generating, accessible);
            _dashboardDirty = true;
            if (!_dashboardTimer!.IsEnabled)
            {
                FlushDashboard();
                _dashboardTimer.Start();
            }
        }

        UpdateTaskbar();
        _tray?.Update(TrayState());

        if (_tooltipShown)
        {
            _tooltip?.SetText(DisplayText.SpeedLine(value, tier, _loc), DisplayText.TodaySummary(snapshot, _loc));
        }

        if (_settingsModel is not null && _settingsWindow is not null && Environment.TickCount64 - _lastSettingsLiveUpdate > 1000)
        {
            _lastSettingsLiveUpdate = Environment.TickCount64;
            _settingsModel.UpdateLive(snapshot, _taskbar?.Presence ?? TaskbarPresence.Hidden);
        }
    }

    private void OnSpeedEvent(SpeedEvent speedEvent)
    {
        switch (speedEvent)
        {
            case SpeedEvent.ResponseCompleted completed:
                var speed = completed.Response.Speed ?? _speed.Value;
                var tokens = _speed.Metric == SpeedMetric.Total ? completed.Response.TotalTokens : completed.Response.OutputTokens;
                var pulseSpeed = _speed.Metric == SpeedMetric.Total && completed.Response.Duration is { TotalSeconds: > 0 } d
                    ? completed.Response.TotalTokens / d.TotalSeconds
                    : speed;
                _orb?.View.Pulse(pulseSpeed, tokens);
                if (_dashboard is { IsShown: true } dashboard)
                {
                    dashboard.Hero.Pulse(pulseSpeed, tokens);
                }

                break;
            case SpeedEvent.NewPeak:
                _orb?.View.Peak();
                if (_dashboard is { IsShown: true } shown)
                {
                    shown.Hero.Peak();
                }

                break;
        }
    }

    private void FlushDashboard()
    {
        if (_dashboard is not { IsShown: true } || !_dashboardDirty)
        {
            _dashboardTimer?.Stop();
            return;
        }

        RefreshDashboard();
    }

    private void RefreshDashboard()
    {
        if (_dashboard is not { } dashboard)
        {
            return;
        }

        _dashboardDirty = false;
        var snapshot = _speed.Snapshot;
        _dashboardModel.ReduceMotion = _theme.ReduceMotion;
        _dashboardModel.Update(snapshot, _speed.Value, _speed.Tier, _speed.Redline, _speed.Metric, _loc, _theme.IsDark);
        var peak = snapshot.PeakToday.ValueFor(_speed.Metric);
        var summary = $"{DisplayText.SpeedLine(_speed.Value, _speed.Tier, _loc)} · {_loc["today.peak"]} {SpeedFormat.Speed(peak)}";
        dashboard.LiveChart.SetData(snapshot.History, _speed.Metric, _speed.Redline, peak, Settings.ChartSeconds,
            _loc["win.chart.peak"], _loc["unit.tokS"], summary);
    }

    // ---- Surfaces ----

    private void ShowOrb()
    {
        if (_orb is null)
        {
            _orb = new OrbWindow(() => _speed.Now) { Language = AppFonts.XmlLanguageFor(_loc.LanguageCode) };
            _orb.DashboardRequested += (_, _) => ShowDashboard(OrbAnchor());
            _orb.MenuRequested += (_, _) => ShowMenu();
            _orb.PositionCommitted += (_, placement) =>
                _settings.Update(s => s.WithOrbAnchor(placement.MonitorKey, placement.Anchor));
            _orb.Closed += (_, _) => _orb = null;
            ConfigureOrb();
            var anchor = Settings.OrbMonitor is { } key && Settings.OrbAnchors.TryGetValue(key, out var a) ? a : (OrbAnchor?)null;
            _orb.RestorePlacement(Settings.OrbMonitor, anchor);
        }

        if (!_orb.IsVisible)
        {
            _orb.Show();
        }

        OnSpeedChanged();
    }

    private void HideOrb()
    {
        if (_orb is { } orb)
        {
            _orb = null;
            orb.Close();
        }
    }

    private void ConfigureOrb()
    {
        var s = Settings;
        _orb?.Configure(BuildOrbConfig(s.OrbSize, null), s.OrbAlwaysOnTop, s.OrbClickThrough, s.OrbFadeWhenIdle, _theme.ReduceMotion);
        _dashboard?.Hero.Configure(BuildOrbConfig(HeroDiameter, EffectsIntensity.Soft) with { BodyOpacity = 1 });
    }

    private OrbConfig BuildOrbConfig(double diameter, EffectsIntensity? cap)
    {
        var s = Settings;
        var policy = new EffectsPolicy(s.Effects, _theme.ReduceMotion, _theme.HighContrast);
        if (cap is { } c)
        {
            policy = policy.CappedAt(c);
        }

        var labels = Enum.GetValues<SpeedTier>().Select(t => DisplayText.TierLabel(t, _loc)).ToArray();
        return new OrbConfig(diameter, _theme.IsDark ? OrbAppearance.Dark : OrbAppearance.Light, s.OrbOpacity, policy,
            _theme.HighContrast, !_theme.TransparencyEnabled, s.ResponsePulses, s.CelebratePeaks, _speed.Redline,
            _loc["unit.tokS"], _loc["peak.new"], labels);
    }

    private void UpdateTaskbar()
    {
        if (_taskbar is null)
        {
            return;
        }

        var s = Settings;
        _taskbar.Update(new TaskbarState(
            s.ShowOnTaskbar,
            _speed.Value,
            _speed.Tier,
            _speed.Redline,
            s.BarMode,
            s.BarColoredGlyph,
            s.BarShowUnit,
            _theme.TaskbarIsDark,
            _theme.HighContrast,
            _theme.ReduceMotion,
            _loc["unit.tokS"],
            $"{_loc["app.name"]}: {DisplayText.Accessibility(_speed.Value, _speed.Tier, _loc)}"));
    }

    private TrayVisualState TrayState() => new(
        HeatMath.Heat(_speed.Value, _speed.Redline),
        _theme.TaskbarIsDark,
        _theme.HighContrast,
        !Settings.BarColoredGlyph,
        $"{_loc["app.name"]}\n{DisplayText.SpeedLine(_speed.Value, _speed.Tier, _loc)}");

    private void ShowMenu()
    {
        if (_menu is null || _quitting)
        {
            return;
        }

        HideTooltip();
        _menu.Show(AppMenu.Build(this, Settings, _loc, _updates.Available));
    }

    private void ToggleDashboard(POINT anchor)
    {
        if (_dashboard is { IsShown: true } shown)
        {
            // Ignore the second half of a double click that just opened the flyout.
            if (Environment.TickCount64 - shown.LastShownTick > 350)
            {
                shown.Dismiss();
            }

            return;
        }

        // A click on the tray or taskbar first deactivates (and hides) an open flyout; do not reopen it.
        if (_dashboard is not null && Environment.TickCount64 - _dashboard.LastHiddenTick < 350)
        {
            return;
        }

        ShowDashboard(anchor);
    }

    private void ShowDashboard(POINT anchor)
    {
        if (_quitting)
        {
            return;
        }

        HideTooltip();
        if (_dashboard is null)
        {
            _dashboard = new DashboardWindow(_dashboardModel, () => _speed.Now) { Language = AppFonts.XmlLanguageFor(_loc.LanguageCode) };
            _dashboard.SettingsRequested += (_, _) => OpenSettings();
            _dashboard.SourcesRequested += (_, _) => OpenSettings("sources");
            _dashboard.QuitRequested += (_, _) => Quit();
            _dashboard.OrbToggled += (_, visible) => _settings.Update(s => s with { ShowOrb = visible });
            _dashboard.RangeChanged += (_, seconds) => _settings.Update(s => s with { ChartSeconds = seconds });
            _dashboard.UpdateRequested += (_, _) => OpenUpdate();
            _dashboard.IsVisibleChanged += (_, _) =>
            {
                if (_dashboard?.IsVisible != true)
                {
                    _dashboardTimer?.Stop();
                    if (_updateDoneShown)
                    {
                        _updateDoneText = null;
                        _dashboard?.SetUpdateDone(null);
                    }
                }
            };
            ConfigureOrb();
        }

        _dashboard.SetRange(Settings.ChartSeconds);
        _dashboard.SetOrbVisible(Settings.ShowOrb);
        _dashboard.SetPreviewing(_preview.IsRunning);
        _dashboard.SetUpdate(UpdatePillText());
        _dashboard.SetUpdateDone(_updateDoneText);
        _updateDoneShown = _updateDoneText is not null;
        _dashboard.ApplyBackground();
        RefreshDashboard();
        _dashboard.ShowNear(anchor, _theme.ReduceMotion);
        OnSpeedChanged();
    }

    private void OnTaskbarHover(bool isOver, RECT bounds)
    {
        if (_tooltip is null)
        {
            return;
        }

        if (!isOver || (_dashboard?.IsShown ?? false) || (_menu?.IsOpen ?? false))
        {
            HideTooltip();
            return;
        }

        _tooltipShown = true;
        _tooltip.Language = AppFonts.XmlLanguageFor(_loc.LanguageCode);
        _tooltip.ShowFor(bounds, DisplayText.SpeedLine(_speed.Value, _speed.Tier, _loc), DisplayText.TodaySummary(_speed.Snapshot, _loc),
            _theme.ReduceMotion);
    }

    private void HideTooltip()
    {
        _tooltipShown = false;
        _tooltip?.HideTip();
    }

    // ---- Changes ----

    private void OnSettingsChanged(AppSettings previous, AppSettings current)
    {
        if (previous.Appearance != current.Appearance)
        {
            _theme.Mode = current.Appearance;
        }

        _updates.OnSettingsChanged(previous, current);

        if (previous.Language != current.Language)
        {
            _loc.LanguageCode = ResolveLanguage(current.Language);
        }

        if (previous.Metric != current.Metric || previous.Redline != current.Redline)
        {
            _speed.Configure(current.Metric, current.Redline);
        }

        if (!previous.ToMonitorConfiguration().Equals(current.ToMonitorConfiguration()))
        {
            try
            {
                _monitor?.Update(current.ToMonitorConfiguration());
            }
            catch (Exception ex)
            {
                AppLog.Error("Updating the live monitor configuration failed.", ex);
            }
        }

        if (current.ShowOrb && _orb is null)
        {
            ShowOrb();
        }
        else if (!current.ShowOrb && _orb is not null)
        {
            HideOrb();
        }

        ConfigureOrb();
        UpdateTaskbar();
        _tray?.Update(TrayState());
        _dashboard?.SetOrbVisible(current.ShowOrb);
        if (previous.ChartSeconds != current.ChartSeconds)
        {
            _dashboard?.SetRange(current.ChartSeconds);
        }

        _dashboardDirty = true;
        FlushDashboard();
    }

    private void OnThemeChanged()
    {
        ConfigureOrb();
        UpdateTaskbar();
        _tray?.Update(TrayState());
        _dashboard?.ApplyBackground();
        if (_settingsModel is not null)
        {
            _settingsModel.ReduceMotion = _theme.ReduceMotion;
        }

        _dashboardDirty = true;
        FlushDashboard();
    }

    private void OnLanguageChanged()
    {
        AppFonts.Apply(_app.Resources, _loc.LanguageCode);
        var language = AppFonts.XmlLanguageFor(_loc.LanguageCode);
        foreach (Window window in _app.Windows)
        {
            window.Language = language;
        }

        _settingsModel?.Relocalize(_loc);
        _updateWindowModel?.Relocalize(_loc);
        _dashboard?.SetUpdate(UpdatePillText());
        ConfigureOrb();
        UpdateTaskbar();
        _tray?.Update(TrayState());
        OnSpeedChanged();
        _dashboardDirty = true;
        FlushDashboard();
    }

    private void OnDisplayChanged()
    {
        _orb?.Revalidate();
        _taskbar?.Reposition();
        _tray?.Refresh();
        _dashboard?.Dismiss();
    }

    private void OnSystemSettingChanged(string? area)
    {
        _theme.QueueRefresh();
        _taskbar?.Reposition();
        if (area is "WindowMetrics" or "TraySettings")
        {
            _tray?.Refresh();
        }
    }

    // ---- Updates ----

    private void StartUpdates()
    {
        _updates.Start();
        _ = Task.Run(UpdateRelaunch.CleanupLeftovers);
        if (_launch.AfterUpdate && !string.Equals(_launch.PreviousVersion, AppPaths.Version, StringComparison.Ordinal))
        {
            _updateDoneText = _loc.Format("update.done", AppPaths.Version);
            ShowBalloon(_loc["app.name"], _updateDoneText, opensUpdate: false);
        }

        if (_launch.CheckUpdatesNow)
        {
            _ = CheckNowAtStartupAsync();
        }
    }

    /// <summary>Development flag <c>--check-updates-now</c>: a manual check that opens the window when something is found.</summary>
    private async Task CheckNowAtStartupAsync()
    {
        AppLog.Info("--check-updates-now: checking for updates.");
        try
        {
            await _updates.CheckAsync(manual: true);
            if (_updates.Available is not null && !_quitting)
            {
                OpenUpdate();
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            AppLog.Warn($"--check-updates-now failed: {UpdateLog.Describe(ex)}");
        }
    }

    private void ShowUpdateWindow()
    {
        if (_quitting)
        {
            return;
        }

        _dashboard?.Dismiss();
        if (_updateWindow is null)
        {
            _updateWindowModel = new UpdateWindowViewModel(_updates, _loc);
            _updateWindow = new UpdateWindow(_updateWindowModel) { Language = AppFonts.XmlLanguageFor(_loc.LanguageCode) };
            _updateWindow.Closed += (_, _) =>
            {
                _updateWindow = null;
                _updateWindowModel = null;
            };
        }

        if (!_updateWindow.IsVisible)
        {
            _updateWindow.Show();
        }

        if (_updateWindow.WindowState == WindowState.Minimized)
        {
            _updateWindow.WindowState = WindowState.Normal;
        }

        _updateWindow.Activate();
    }

    private void OnUpdatesChanged() => _dashboard?.SetUpdate(UpdatePillText());

    private string? UpdatePillText() => _updates.Available is { } release ? UpdateText.Pill(release, _loc) : null;

    private void ShowBalloon(string title, string text, bool opensUpdate)
    {
        _balloonOpensUpdate = opensUpdate;
        _tray?.ShowBalloon(title, text);
    }

    private void OnBalloonClicked()
    {
        if (_balloonOpensUpdate && _updates.Available is not null)
        {
            OpenUpdate();
        }
    }

    // ---- Anchors (physical pixels) ----

    private POINT TrayAnchor()
    {
        if (_tray?.GetBounds() is { } rect)
        {
            return Center(rect);
        }

        return CursorAnchor();
    }

    private POINT OrbAnchor() =>
        _orb is { } orb ? new POINT((int)orb.Center.X, (int)orb.Center.Y) : CursorAnchor();

    private static POINT CursorAnchor() => User32.GetCursorPos(out var point) ? point : new POINT(0, 0);

    private static POINT Center(RECT rect) => new((rect.Left + rect.Right) / 2, (rect.Top + rect.Bottom) / 2);
}
