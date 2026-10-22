using System.Windows;

namespace AgentSpeed.History;

/// <summary>Column widths shared by the breakdown's caption row and its bars, so the numbers line up under their captions.</summary>
internal static class BreakdownColumns
{
    public static GridLength Name { get; } = new(168);

    public static GridLength Output { get; } = new(76);

    public static GridLength Share { get; } = new(56);

    public static double SpeedWidth => 104;
}
