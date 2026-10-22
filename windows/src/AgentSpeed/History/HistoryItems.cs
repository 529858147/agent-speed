using System.Windows.Media;
using AgentSpeed.Controls;
using AgentSpeed.Core;
using AgentSpeed.Presentation.Colors;
using AgentSpeed.Presentation.Formatting;
using AgentSpeed.Presentation.History;
using AgentSpeed.Presentation.Localization;
using AgentSpeed.Theme;

namespace AgentSpeed.History;

/// <summary>An entry of the source picker: all sources (null) or one source.</summary>
internal sealed class SourceOption(SourceKind? kind) : ObservableObject
{
    private string _name = string.Empty;
    private IReadOnlyList<Color> _colors = [];

    public SourceKind? Kind { get; } = kind;

    public string Name { get => _name; set => Set(ref _name, value); }

    public IReadOnlyList<Color> Colors { get => _colors; set => Set(ref _colors, value); }

    public override string ToString() => Name;
}

/// <summary>A legend chip of the chart: a source, or the single filtered source.</summary>
internal sealed record LegendItem(string Name, Brush Brush);

/// <summary>One summary card, updated in place so its sparkline and texts change without rebuilding the card.</summary>
internal sealed class SummaryCardView(SummaryMetric metric) : ObservableObject
{
    private string _label = string.Empty;
    private string _value = string.Empty;
    private string _unit = string.Empty;
    private string _tooltip = string.Empty;
    private string _secondary = string.Empty;
    private string _secondaryLead = string.Empty;
    private string _secondaryChange = string.Empty;
    private string _secondaryTail = string.Empty;
    private bool _isUp;
    private bool _isDown;
    private IReadOnlyList<double?> _spark = [];
    private int _sparkHighlight = -1;
    private Brush? _sparkBrush;
    private string _accessibleName = string.Empty;

    public SummaryMetric Metric { get; } = metric;

    public string Label { get => _label; private set => Set(ref _label, value); }

    public string Value { get => _value; private set => Set(ref _value, value); }

    public string Unit { get => _unit; private set => Set(ref _unit, value); }

    public bool HasUnit => !string.IsNullOrEmpty(_unit);

    public string Tooltip { get => _tooltip; private set => Set(ref _tooltip, value); }

    public string Secondary { get => _secondary; private set => Set(ref _secondary, value); }

    /// <summary>The secondary line split around the change, so only the change itself is colored.</summary>
    public string SecondaryLead { get => _secondaryLead; private set => Set(ref _secondaryLead, value); }

    public string SecondaryChange { get => _secondaryChange; private set => Set(ref _secondaryChange, value); }

    public string SecondaryTail { get => _secondaryTail; private set => Set(ref _secondaryTail, value); }

    /// <summary>The secondary line is a rise against the previous period (green).</summary>
    public bool IsUp { get => _isUp; private set => Set(ref _isUp, value); }

    /// <summary>The secondary line is a drop against the previous period (red).</summary>
    public bool IsDown { get => _isDown; private set => Set(ref _isDown, value); }

    public IReadOnlyList<double?> Spark { get => _spark; private set => Set(ref _spark, value); }

    /// <summary>Index of the selected day in <see cref="Spark"/> for a single-day range; -1 otherwise.</summary>
    public int SparkHighlight { get => _sparkHighlight; private set => Set(ref _sparkHighlight, value); }

    /// <summary>The source color when one source is shown; null uses the label color.</summary>
    public Brush? SparkBrush { get => _sparkBrush; private set => Set(ref _sparkBrush, value); }

    public bool HasSparkBrush => _sparkBrush is not null;

    public string AccessibleName { get => _accessibleName; private set => Set(ref _accessibleName, value); }

    public void Apply(SummaryCard card, Brush? sparkBrush, string tooltip, string accessibleName)
    {
        Label = card.Label;
        Value = card.Value;
        if (Set(ref _unit, card.Unit, nameof(Unit)))
        {
            OnPropertyChanged(nameof(HasUnit));
        }

        Tooltip = tooltip;
        Secondary = card.Secondary;
        var change = card.Change is { } ratio ? HistoryText.Change(ratio) : string.Empty;
        var at = change.Length > 0 ? card.Secondary.IndexOf(change, StringComparison.Ordinal) : -1;
        SecondaryLead = at >= 0 ? card.Secondary[..at] : card.Secondary;
        SecondaryChange = at >= 0 ? change : string.Empty;
        SecondaryTail = at >= 0 ? card.Secondary[(at + change.Length)..] : string.Empty;
        IsUp = card.Change is > 0.0005;
        IsDown = card.Change is < -0.0005;
        Spark = card.Spark;
        SparkHighlight = card.Highlight ?? -1;
        if (Set(ref _sparkBrush, sparkBrush, nameof(SparkBrush)))
        {
            OnPropertyChanged(nameof(HasSparkBrush));
        }

        AccessibleName = accessibleName;
    }
}

/// <summary>One bar of the single-day breakdown, updated in place so its bar can grow to the new length.</summary>
internal sealed class BreakdownRowView(string key) : ObservableObject
{
    private SourceKind _source;
    private bool _isSource;
    private string _name = string.Empty;
    private Brush _dotBrush = Brushes.Gray;
    private Color _barColor = Colors.Gray;
    private bool _hatched;
    private double _fraction;
    private string _outputText = string.Empty;
    private string _shareText = string.Empty;
    private string _speedText = string.Empty;
    private string _toolTip = string.Empty;
    private string _helpText = string.Empty;
    private string _accessibleName = string.Empty;

    public string Key { get; } = key;

    public SourceKind Source { get => _source; private set => Set(ref _source, value); }

    /// <summary>A source bar: activating it shows that source's models.</summary>
    public bool IsSource { get => _isSource; private set => Set(ref _isSource, value); }

    public string Name { get => _name; private set => Set(ref _name, value); }

    public Brush DotBrush { get => _dotBrush; private set => Set(ref _dotBrush, value); }

    public Color BarColor { get => _barColor; private set => Set(ref _barColor, value); }

    /// <summary>Today's figures are still in progress.</summary>
    public bool Hatched { get => _hatched; private set => Set(ref _hatched, value); }

    /// <summary>Length relative to the largest bar.</summary>
    public double Fraction { get => _fraction; private set => Set(ref _fraction, value); }

    public string OutputText { get => _outputText; private set => Set(ref _outputText, value); }

    public string ShareText { get => _shareText; private set => Set(ref _shareText, value); }

    /// <summary><c>42 tok/s</c>, or <c>history.noSpeed</c> when nothing was measured.</summary>
    public string SpeedText { get => _speedText; private set => Set(ref _speedText, value); }

    /// <summary>Exact values (and, on a source bar, what activating it does).</summary>
    public string ToolTip { get => _toolTip; private set => Set(ref _toolTip, value); }

    public string HelpText { get => _helpText; private set => Set(ref _helpText, value); }

    public string AccessibleName { get => _accessibleName; private set => Set(ref _accessibleName, value); }

    public void Apply(BreakdownRow row, BreakdownKind kind, bool inProgress, Localizer loc)
    {
        var color = (row.OtherModels ? HeatPalette.Neutral : HeatPalette.SourceColor(row.Source)).ToColor();
        Source = row.Source;
        IsSource = kind == BreakdownKind.Sources;
        Name = HistoryText.BreakdownName(row, loc);
        if (BarColor != color)
        {
            BarColor = color;
            DotBrush = MediaColors.Frozen(color);
        }

        Hatched = inProgress;
        Fraction = row.Fraction;
        OutputText = SpeedFormat.Tokens(row.Output);
        ShareText = HistoryText.Percent(row.Share);
        SpeedText = HistoryText.Speed(row.Speed, loc);
        var value = HistoryText.BreakdownValue(row, loc);
        HelpText = IsSource ? loc["win.history.breakdown.sourceHint"] : string.Empty;
        ToolTip = IsSource ? $"{value}{Environment.NewLine}{HelpText}" : value;
        AccessibleName = HistoryText.Accessible(row, loc);
    }
}
