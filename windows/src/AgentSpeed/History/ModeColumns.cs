using System.Windows;

namespace AgentSpeed.History;

/// <summary>Column widths shared by the effort and speed section's captions and rows (DESIGN 4b, 3b).</summary>
internal static class ModeColumns
{
    public static GridLength Glyph { get; } = new(26);

    public static GridLength Token { get; } = new(112);

    public static GridLength Share { get; } = new(48);

    public static GridLength Requests { get; } = new(76);

    public static GridLength Output { get; } = new(76);

    public static GridLength Speed { get; } = new(104);
}
