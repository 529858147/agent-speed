namespace AgentSpeed.Presentation.Layout;

/// <summary>
/// The taskbar widget's stored position (DESIGN 3, Position on the taskbar): the distance in DIPs from its default spot
/// next to the notification area to its trailing edge, measured toward the taskbar's start. A distance from the
/// notification area follows the tray as it grows or shrinks, and DIPs keep it visually the same across DPI changes.
/// </summary>
public static class TaskbarPosition
{
    /// <summary>Largest stored distance (DIPs); wider than any taskbar.</summary>
    public const double MaxOffset = 10_000;

    /// <summary>Gap (DIPs) between the widget's trailing edge and the notification area at the default spot.</summary>
    public const double Gap = 4;

    /// <summary>The widget's far edge stays in the taskbar's trailing part: never closer to its start than this share.</summary>
    public const double ReservedStart = 0.25;

    /// <summary>0 for anything that is not a positive finite number; otherwise at most <see cref="MaxOffset"/>, rounded to 0.01.</summary>
    public static double Normalize(double offset) =>
        double.IsFinite(offset) && offset > 0 ? Math.Round(Math.Min(offset, MaxOffset), 2, MidpointRounding.AwayFromZero) : 0;

    /// <summary>Whole physical pixels for a stored distance at a DPI scale (pixels per DIP).</summary>
    public static double ToPixels(double offset, double scale) =>
        Math.Round(Normalize(offset) * ValidScale(scale), MidpointRounding.AwayFromZero);

    /// <summary>The stored distance for a physical offset; <see cref="ToPixels"/> gives the same pixels back at any scale up to 5.</summary>
    public static double ToDips(double pixels, double scale) => Normalize(pixels / ValidScale(scale));

    private static double ValidScale(double scale) => double.IsFinite(scale) && scale > 0 ? scale : 1;
}
