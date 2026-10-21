using System.Windows;
using System.Windows.Threading;
using AgentSpeed.Core;
using AgentSpeed.Presentation.Localization;
using AgentSpeed.Services;
using AgentSpeed.Theme;

namespace AgentSpeed.History;

/// <summary>
/// Owns the single Usage History window: opens or focuses it with a filter, remembers its frame, follows the theme
/// and language, and (developer option <c>--preview-history</c>) fills the hub with generated days.
/// </summary>
internal sealed class HistoryController : IDisposable
{
    private const int PreviewSteps = 12;
    private const int PreviewFiles = 340;

    private readonly HistoryHub _hub;
    private readonly SettingsService _settings;
    private readonly ThemeService _theme;
    private readonly Localizer _loc;
    private readonly Dispatcher _dispatcher;
    private HistoryWindow? _window;
    private HistoryViewModel? _model;
    private DispatcherTimer? _preview;
    private bool _disposed;

    public HistoryController(HistoryHub hub, SettingsService settings, ThemeService theme, Localizer loc)
    {
        _hub = hub;
        _settings = settings;
        _theme = theme;
        _loc = loc;
        _dispatcher = Dispatcher.CurrentDispatcher;
    }

    public bool IsOpen => _window is not null;

    /// <summary>Opens the window (or brings it forward) showing <paramref name="source"/>, or every source when null.</summary>
    public void Open(SourceKind? source)
    {
        if (_disposed)
        {
            return;
        }

        if (_window is null || _model is null)
        {
            var model = new HistoryViewModel(_hub, _loc, _dispatcher);
            model.SetSource(source);
            var window = new HistoryWindow(model, _theme.ReduceMotion, _theme.HighContrast)
            {
                Language = AppFonts.XmlLanguageFor(_loc.LanguageCode),
            };
            window.Closing += (_, _) => SaveFrame(window);
            window.Closed += (_, _) =>
            {
                model.Dispose();
                if (ReferenceEquals(_window, window))
                {
                    _window = null;
                    _model = null;
                }
            };
            window.RestoreFrame(_settings.Current.HistoryWindow);
            _model = model;
            _window = window;
            window.Show();
        }
        else
        {
            _model.SetSource(source);
            if (!_window.IsVisible)
            {
                _window.Show();
            }

            if (_window.WindowState == WindowState.Minimized)
            {
                _window.WindowState = WindowState.Normal;
            }
        }

        _window.Activate();
    }

    /// <summary>Developer option: publishes generated history as a short simulated analysis (about 3 s).</summary>
    public void StartPreview()
    {
        StopPreview();
        var days = PreviewHistory.Days(DateTime.Now);
        var newest = DateTime.Now.Date;
        var step = 0;
        _hub.Publish(HistoryState.Analyzing(0, 0, PreviewFiles), []);
        _preview = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (_, _) =>
        {
            step++;
            if (step >= PreviewSteps)
            {
                StopPreview();
                _hub.Publish(HistoryState.Ready(DateTimeOffset.Now), days);
                return;
            }

            var progress = step / (double)PreviewSteps;
            var oldest = newest.AddDays(-Math.Ceiling(120 * progress));
            var known = days.Where(d => DateTime.TryParse(d.Day, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var date) && date >= oldest).ToArray();
            _hub.Publish(HistoryState.Analyzing(progress, (int)(PreviewFiles * progress), PreviewFiles), known);
        }, _dispatcher);
        _preview.Start();
    }

    public void OnThemeChanged() => _window?.ApplyTheme(_theme.ReduceMotion, _theme.HighContrast);

    public void OnLanguageChanged()
    {
        if (_window is null || _model is null)
        {
            return;
        }

        _window.Language = AppFonts.XmlLanguageFor(_loc.LanguageCode);
        _model.Relocalize(_loc);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopPreview();
        _window?.Close();
    }

    private void StopPreview()
    {
        _preview?.Stop();
        _preview = null;
    }

    private void SaveFrame(HistoryWindow window)
    {
        if (window.CaptureFrame() is { } frame)
        {
            _settings.Update(s => s with { HistoryWindow = frame });
        }
    }
}
