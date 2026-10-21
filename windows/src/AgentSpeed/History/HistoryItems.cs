using System.Windows.Media;
using AgentSpeed.Controls;
using AgentSpeed.Core;
using AgentSpeed.Presentation.History;

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
        if (Set(ref _sparkBrush, sparkBrush, nameof(SparkBrush)))
        {
            OnPropertyChanged(nameof(HasSparkBrush));
        }

        AccessibleName = accessibleName;
    }
}
