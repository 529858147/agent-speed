using System.Windows.Media;
using AgentSpeed.Controls;
using AgentSpeed.Core;
using AgentSpeed.Modes;
using AgentSpeed.Presentation.History;
using AgentSpeed.Presentation.Localization;
using AgentSpeed.Presentation.Modes;

namespace AgentSpeed.History;

/// <summary>One of the hovered row's top models with its share of the row's output.</summary>
internal sealed record ModeModelView(string Name, string ShareText, double Fraction);

/// <summary>
/// One value of the effort and speed section (DESIGN 4b, 3b), updated in place so its bar resizes and its row springs to
/// a new place when the range or source changes.
/// </summary>
internal sealed class ModeRowView(string mode) : ObservableObject
{
    private string _token = string.Empty;
    private bool _isUnknown;
    private EffortCapsule? _glyph;
    private ModeHistoryTone _tone;
    private Brush? _barFill;
    private double _fraction;
    private string _shareText = string.Empty;
    private string _requestsText = string.Empty;
    private string _outputText = string.Empty;
    private string _outputToolTip = string.Empty;
    private string _speedText = string.Empty;
    private bool _hasSpeed;
    private IReadOnlyList<ModeModelView> _topModels = [];
    private string _accessibleName = string.Empty;

    public string Mode { get; } = mode;

    public string Token { get => _token; private set => Set(ref _token, value); }

    /// <summary>The unknown bucket: its token reads in tertiary text.</summary>
    public bool IsUnknown { get => _isUnknown; private set => Set(ref _isUnknown, value); }

    public EffortCapsule? Glyph { get => _glyph; private set => Set(ref _glyph, value); }

    public ModeHistoryTone Tone { get => _tone; private set => Set(ref _tone, value); }

    /// <summary>The share bar's fill; null for <see cref="ModeHistoryTone.Neutral"/>, which takes the secondary text color.</summary>
    public Brush? BarFill { get => _barFill; private set => Set(ref _barFill, value); }

    public double Fraction { get => _fraction; private set => Set(ref _fraction, value); }

    public string ShareText { get => _shareText; private set => Set(ref _shareText, value); }

    public string RequestsText { get => _requestsText; private set => Set(ref _requestsText, value); }

    public string OutputText { get => _outputText; private set => Set(ref _outputText, value); }

    public string OutputToolTip { get => _outputToolTip; private set => Set(ref _outputToolTip, value); }

    public string SpeedText { get => _speedText; private set => Set(ref _speedText, value); }

    public bool HasSpeed { get => _hasSpeed; private set => Set(ref _hasSpeed, value); }

    public IReadOnlyList<ModeModelView> TopModels { get => _topModels; private set => Set(ref _topModels, value); }

    public bool HasTopModels => TopModels.Count > 0;

    public string AccessibleName { get => _accessibleName; private set => Set(ref _accessibleName, value); }

    public void Apply(ModeHistoryRow row, ModeHistorySection section, Localizer loc, CapsuleAppearance appearance)
    {
        Token = ModeHistoryText.Token(row, section.Kind, loc);
        IsUnknown = row.IsUnknown;
        Glyph = section.Glyph(row);
        Tone = section.Tone(row);
        BarFill = Fill(Tone, appearance.Brushes);
        Fraction = row.Share;
        ShareText = HistoryText.Percent(row.Share);
        RequestsText = HistoryText.FullNumber(row.Requests, loc);
        OutputText = ModeHistoryText.Output(row);
        OutputToolTip = HistoryText.FullNumber(row.Output, loc);
        SpeedText = HistoryText.Speed(row.AverageSpeed, loc);
        HasSpeed = row.AverageSpeed is not null;
        var models = ModeHistoryText.TopModels(row, loc).Select(m => new ModeModelView(m.Name, m.Share, m.Fraction)).ToArray();
        if (!models.SequenceEqual(TopModels))
        {
            TopModels = models;
            OnPropertyChanged(nameof(HasTopModels));
        }

        AccessibleName = ModeHistoryText.Accessible(row, section.Kind, loc);
    }

    private static Brush? Fill(ModeHistoryTone tone, CapsuleBrushes brushes) => tone switch
    {
        ModeHistoryTone.Max => brushes.Heat(EffortCapsulePalette.MaxHeat),
        ModeHistoryTone.TopRung => brushes.Word(EffortTone.TopRung),
        ModeHistoryTone.Amber => brushes.Bolt,
        ModeHistoryTone.Unknown => brushes.Label(EffortCapsulePalette.UnknownBarOpacity),
        _ => null,
    };
}
