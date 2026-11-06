using System.Globalization;
using System.Windows;
using System.Windows.Media;
using AgentSpeed.Presentation.Formatting;
using AgentSpeed.Theme;

namespace AgentSpeed.Dashboard;

/// <summary>
/// The intelligence column's width per language (DESIGN 4, Intelligence column): the wider of <c>888</c> at 12 DIP semibold
/// and the header caption at 11 DIP medium, in the UI face, rounded up. Measured once per language and caption.
/// </summary>
internal static class IntelligenceColumnMeasure
{
    private static readonly Dictionary<(string Language, string Caption), double> Cache = new();

    public static double Width(string languageCode, string caption)
    {
        ArgumentNullException.ThrowIfNull(caption);
        if (Cache.TryGetValue((languageCode, caption), out var cached))
        {
            return cached;
        }

        var family = AppFonts.Ui(languageCode);
        var width = AgentIntelligenceColumn.Width(
            Measure(AgentIntelligenceColumn.WidestNumber, family, FontWeights.SemiBold, AgentIntelligenceColumn.NumberSize),
            Measure(caption, family, FontWeights.Medium, AgentIntelligenceColumn.CaptionSize));
        if (Cache.Count > 16)
        {
            Cache.Clear();
        }

        Cache[(languageCode, caption)] = width;
        return width;
    }

    private static double Measure(string text, FontFamily family, FontWeight weight, double size) =>
        new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(family, FontStyles.Normal, weight, FontStretches.Normal), size, Brushes.Black, 1.0).WidthIncludingTrailingWhitespace;
}
