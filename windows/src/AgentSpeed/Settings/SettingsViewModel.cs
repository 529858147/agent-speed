using System.Collections.ObjectModel;
using System.Diagnostics;
using AgentSpeed.App;
using AgentSpeed.Controls;
using AgentSpeed.Core;
using AgentSpeed.Presentation.Heat;
using AgentSpeed.Presentation.Localization;
using AgentSpeed.Presentation.Settings;
using AgentSpeed.Services;
using AgentSpeed.Taskbar;

namespace AgentSpeed.Settings;

internal sealed class NavItem(string key, string glyph, string titleKey) : ObservableObject
{
    private string _title = string.Empty;

    public string Key { get; } = key;

    public string Glyph { get; } = glyph;

    public string TitleKey { get; } = titleKey;

    public string Title { get => _title; set => Set(ref _title, value); }
}

internal sealed class SourceSettingsRow(SourceKind kind) : ObservableObject
{
    private bool _enabled;
    private string _status = string.Empty;
    private string _name = string.Empty;

    public SourceKind Kind { get; } = kind;

    public string Name { get => _name; set => Set(ref _name, value); }

    public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }

    public string Status { get => _status; set => Set(ref _status, value); }
}

/// <summary>Live-applied settings. Every setter writes through <see cref="SettingsService"/> immediately.</summary>
internal sealed class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private readonly PreviewService _preview;
    private readonly Func<string?, string?> _pickFolder;
    private Localizer _loc;
    private NavItem _selected;
    private bool _syncing;
    private bool _launchAtLogin;
    private SpeedSnapshot _snapshot = SpeedSnapshot.Empty;
    private TaskbarPresence _taskbarPresence = TaskbarPresence.Hidden;
    private bool _reduceMotion;

    public SettingsViewModel(SettingsService settings, PreviewService preview, Localizer localizer, Func<string?, string?> pickFolder)
    {
        _settings = settings;
        _preview = preview;
        _loc = localizer;
        _pickFolder = pickFolder;

        Pages.Add(new NavItem("general", "", "settings.general"));
        Pages.Add(new NavItem("taskbar", "", "settings.taskbar"));
        Pages.Add(new NavItem("orb", "", "settings.orb"));
        Pages.Add(new NavItem("effects", "", "settings.effects"));
        Pages.Add(new NavItem("sources", "", "settings.sources"));
        Pages.Add(new NavItem("about", "", "settings.about"));
        _selected = Pages[0];

        foreach (var kind in Enum.GetValues<SourceKind>())
        {
            var row = new SourceSettingsRow(kind);
            row.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(SourceSettingsRow.Enabled) && !_syncing)
                {
                    _settings.Update(s => s.WithSourceEnabled(row.Kind, row.Enabled));
                }
            };
            Sources.Add(row);
        }

        PreviewCommand = new RelayCommand(StartPreview, () => !_preview.IsRunning);
        SizePresetCommand = new RelayCommand(p =>
        {
            if (p is string text && double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var size))
            {
                OrbSize = size;
            }
        });
        ChangeCodexCommand = new RelayCommand(() => ChangeFolder(isCodex: true));
        ChangeClaudeCommand = new RelayCommand(() => ChangeFolder(isCodex: false));
        ResetCodexCommand = new RelayCommand(() => _settings.Update(s => s with { CodexRoot = null }), () => _settings.Current.CodexRoot is not null);
        ResetClaudeCommand = new RelayCommand(() => _settings.Update(s => s with { ClaudeRoot = null }), () => _settings.Current.ClaudeRoot is not null);
        OpenRepositoryCommand = new RelayCommand(() => OpenUrl(AppPaths.RepositoryUrl));
        OpenLicenseCommand = new RelayCommand(() => OpenUrl(AppPaths.LicenseUrl));
        OpenLogsCommand = new RelayCommand(OpenLogs);

        _settings.Changed += (_, _) => Sync();
        _preview.StateChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(IsPreviewing));
            OnPropertyChanged(nameof(PreviewButtonText));
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        };
        _launchAtLogin = AutoStart.IsEnabled;
        Relocalize(localizer);
    }

    public ObservableCollection<NavItem> Pages { get; } = new();

    public ObservableCollection<SourceSettingsRow> Sources { get; } = new();

    public NavItem SelectedPage
    {
        get => _selected;
        set
        {
            if (value is not null && Set(ref _selected, value))
            {
                OnPropertyChanged(nameof(PageTitle));
                OnPropertyChanged(nameof(PageKey));
            }
        }
    }

    public string PageKey => _selected.Key;

    public string PageTitle => _selected.Title;

    public RelayCommand PreviewCommand { get; }

    public RelayCommand SizePresetCommand { get; }

    public RelayCommand ChangeCodexCommand { get; }

    public RelayCommand ChangeClaudeCommand { get; }

    public RelayCommand ResetCodexCommand { get; }

    public RelayCommand ResetClaudeCommand { get; }

    public RelayCommand OpenRepositoryCommand { get; }

    public RelayCommand OpenLicenseCommand { get; }

    public RelayCommand OpenLogsCommand { get; }

    private AppSettings S => _settings.Current;

    public int AppearanceIndex
    {
        get => (int)S.Appearance;
        set => Apply(s => s with { Appearance = (AppearanceMode)value });
    }

    public int LanguageIndex
    {
        get => (int)S.Language;
        set => Apply(s => s with { Language = (AppLanguage)value });
    }

    public bool LaunchAtLogin
    {
        get => _launchAtLogin;
        set
        {
            if (AutoStart.SetEnabled(value))
            {
                _launchAtLogin = value;
            }

            _launchAtLogin = AutoStart.IsEnabled;
            OnPropertyChanged();
        }
    }

    public int MetricIndex
    {
        get => (int)S.Metric;
        set => Apply(s => s with { Metric = (SpeedMetric)value });
    }

    public bool IsTotalMetric => S.Metric == SpeedMetric.Total;

    public bool IsOutputMetric => !IsTotalMetric;

    public double RedlineOutputValue
    {
        get => S.RedlineOutput;
        set => Apply(s => s.WithRedline(SpeedMetric.Output, value));
    }

    public double RedlineTotalMinimumLog => Math.Log10(HeatMath.MinRedlineTotal);

    public double RedlineTotalMaximumLog => Math.Log10(HeatMath.MaxRedlineTotal);

    /// <summary>The total-throughput redline spans two decades, so its slider is logarithmic.</summary>
    public double RedlineTotalLog
    {
        get => Math.Log10(S.RedlineTotal);
        set => Apply(s => s.WithRedline(SpeedMetric.Total, Math.Pow(10, value)));
    }

    public string RedlineText => $"{SpeedFormat.Speed(S.Redline)} {_loc["unit.tokS"]}";

    public string RedlineHint
    {
        get
        {
            var r = S.Redline;
            return _loc.Format("settings.redline.hint",
                SpeedFormat.Speed(HeatMath.Threshold(SpeedTier.Boost, r)),
                SpeedFormat.Speed(HeatMath.Threshold(SpeedTier.Overdrive, r)),
                SpeedFormat.Speed(HeatMath.Threshold(SpeedTier.Redline, r)));
        }
    }

    public bool ShowOnTaskbar
    {
        get => S.ShowOnTaskbar;
        set => Apply(s => s with { ShowOnTaskbar = value });
    }

    public int BarModeIndex
    {
        get => (int)S.BarMode;
        set => Apply(s => s with { BarMode = (BarDisplayMode)value });
    }

    public bool BarColoredGlyph
    {
        get => S.BarColoredGlyph;
        set => Apply(s => s with { BarColoredGlyph = value });
    }

    public bool BarShowUnit
    {
        get => S.BarShowUnit;
        set => Apply(s => s with { BarShowUnit = value });
    }

    public string TaskbarStatus => _taskbarPresence switch
    {
        TaskbarPresence.Floating => _loc["win.settings.taskbar.floating"],
        TaskbarPresence.Unavailable when S.ShowOnTaskbar => _loc["win.settings.taskbar.unavailable"],
        _ => string.Empty,
    };

    public bool HasTaskbarStatus => !string.IsNullOrEmpty(TaskbarStatus);

    public bool ShowOrb
    {
        get => S.ShowOrb;
        set => Apply(s => s with { ShowOrb = value });
    }

    public double OrbSize
    {
        get => S.OrbSize;
        set => Apply(s => s with { OrbSize = value });
    }

    public string OrbSizeText => $"{S.OrbSize:0}";

    public double OrbOpacityPercent
    {
        get => Math.Round(S.OrbOpacity * 100);
        set => Apply(s => s with { OrbOpacity = value / 100 });
    }

    public string OrbOpacityText => $"{Math.Round(S.OrbOpacity * 100):0}%";

    public bool OrbAlwaysOnTop
    {
        get => S.OrbAlwaysOnTop;
        set => Apply(s => s with { OrbAlwaysOnTop = value });
    }

    public bool OrbClickThrough
    {
        get => S.OrbClickThrough;
        set => Apply(s => s with { OrbClickThrough = value });
    }

    public bool OrbFadeWhenIdle
    {
        get => S.OrbFadeWhenIdle;
        set => Apply(s => s with { OrbFadeWhenIdle = value });
    }

    public int EffectsIndex
    {
        get => (int)S.Effects;
        set => Apply(s => s with { Effects = (EffectsIntensity)value });
    }

    public bool CelebratePeaks
    {
        get => S.CelebratePeaks;
        set => Apply(s => s with { CelebratePeaks = value });
    }

    public bool ResponsePulses
    {
        get => S.ResponsePulses;
        set => Apply(s => s with { ResponsePulses = value });
    }

    public bool ReduceMotion
    {
        get => _reduceMotion;
        set => Set(ref _reduceMotion, value);
    }

    public bool IsPreviewing => _preview.IsRunning;

    public string PreviewButtonText => _preview.IsRunning ? _loc["settings.effects.previewing"] : _loc["settings.effects.preview"];

    public string CodexPath => S.CodexRoot ?? LiveMonitor.DefaultCodexRoot;

    public string ClaudePath => S.ClaudeRoot ?? LiveMonitor.DefaultClaudeRoot;

    public bool CodexIsCustom => S.CodexRoot is not null;

    public bool ClaudeIsCustom => S.ClaudeRoot is not null;

    public string CodexStatus => StatusFor(SourceKind.Codex);

    public string ClaudeStatus
    {
        get
        {
            var code = _snapshot.Sources.FirstOrDefault(s => s.Kind == SourceKind.ClaudeCode);
            var desktop = _snapshot.Sources.FirstOrDefault(s => s.Kind == SourceKind.ClaudeDesktop);
            if (code is null && desktop is null)
            {
                return string.Empty;
            }

            var available = (code?.Available ?? false) || (desktop?.Available ?? false);
            if (!available)
            {
                return _loc["source.folderMissing"];
            }

            return _loc.Format("source.sessions", (code?.SessionCount ?? 0) + (desktop?.SessionCount ?? 0));
        }
    }

    public string VersionText => _loc.Format("settings.about.version", AppPaths.Version);

    public string WindowTitle => $"{_loc["app.name"]} · {_loc["settings.title"]}";

    public void Relocalize(Localizer localizer)
    {
        _loc = localizer;
        foreach (var page in Pages)
        {
            page.Title = localizer[page.TitleKey];
        }

        foreach (var row in Sources)
        {
            row.Name = localizer[Presentation.Formatting.DisplayText.SourceKey(row.Kind)];
        }

        OnPropertyChanged(string.Empty);
    }

    public void UpdateLive(SpeedSnapshot snapshot, TaskbarPresence presence)
    {
        _snapshot = snapshot;
        if (_taskbarPresence != presence)
        {
            _taskbarPresence = presence;
            OnPropertyChanged(nameof(TaskbarStatus));
            OnPropertyChanged(nameof(HasTaskbarStatus));
        }

        foreach (var row in Sources)
        {
            row.Status = StatusFor(row.Kind);
        }

        OnPropertyChanged(nameof(CodexStatus));
        OnPropertyChanged(nameof(ClaudeStatus));
    }

    public void Select(string key)
    {
        if (Pages.FirstOrDefault(p => p.Key == key) is { } page)
        {
            SelectedPage = page;
        }
    }

    private string StatusFor(SourceKind kind)
    {
        var status = _snapshot.Sources.FirstOrDefault(s => s.Kind == kind);
        if (status is null)
        {
            return string.Empty;
        }

        if (!status.Enabled)
        {
            return _loc["source.disabled"];
        }

        return status.Available ? _loc.Format("source.sessions", status.SessionCount) : _loc["source.folderMissing"];
    }

    private void Apply(Func<AppSettings, AppSettings> change)
    {
        if (_syncing)
        {
            return;
        }

        _settings.Update(change);
    }

    private void Sync()
    {
        _syncing = true;
        try
        {
            foreach (var row in Sources)
            {
                row.Enabled = S.IsSourceEnabled(row.Kind);
            }

            OnPropertyChanged(string.Empty);
        }
        finally
        {
            _syncing = false;
        }
    }

    private void StartPreview()
    {
        var s = S;
        var redline = s.Metric == SpeedMetric.Total ? s.RedlineTotal / 24 : s.RedlineOutput;
        _preview.Start(redline);
    }

    private void ChangeFolder(bool isCodex)
    {
        var current = isCodex ? CodexPath : ClaudePath;
        var picked = _pickFolder(Directory.Exists(current) ? current : null);
        if (string.IsNullOrWhiteSpace(picked) || !Path.IsPathFullyQualified(picked))
        {
            return;
        }

        _settings.Update(s => isCodex ? s with { CodexRoot = picked } : s with { ClaudeRoot = picked });
    }

    private static void OpenUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            AppLog.Warn("Could not open the browser.", ex);
        }
    }

    private static void OpenLogs()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.LogsDirectory);
            Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = false, ArgumentList = { AppPaths.LogsDirectory } });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("Could not open the logs folder.", ex);
        }
    }
}
