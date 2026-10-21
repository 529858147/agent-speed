using AgentSpeed.Presentation.Heat;
using AgentSpeed.Presentation.Settings;

namespace AgentSpeed.Taskbar;

/// <summary>Everything the taskbar thread needs to draw, passed as an immutable snapshot from the UI thread.</summary>
internal sealed record TaskbarState(
    bool Enabled,
    double Value,
    SpeedTier Tier,
    double Redline,
    BarDisplayMode Mode,
    bool ColoredGlyph,
    bool ShowUnit,
    bool TaskbarDark,
    bool HighContrast,
    bool ReduceMotion,
    string UnitText,
    string AccessibleText)
{
    public static TaskbarState Initial { get; } = new(
        false, 0, SpeedTier.Idle, HeatMath.DefaultRedline, BarDisplayMode.GlyphAndValue, true, true, true, false, false,
        "tok/s", "Agent Speed");

    /// <summary>True when a change affects the widget's size or layout (not just its content).</summary>
    public bool LayoutDiffers(TaskbarState other) =>
        Mode != other.Mode || ShowUnit != other.ShowUnit || UnitText != other.UnitText || Enabled != other.Enabled;
}

internal enum TaskbarPresence
{
    Hidden,
    Embedded,
    Floating,
    Unavailable,
}
