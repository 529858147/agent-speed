namespace AgentSpeed.Presentation.Formatting;

/// <summary>
/// The words of the taskbar's readout blocks (DESIGN 10, Menu bar and taskbar), in their order on the bar: the network
/// block's first, then the intelligence block's. The widget's label adds them after its own words and the tooltip's first
/// line after <c> · </c>.
/// </summary>
public static class BarReadouts
{
    /// <summary><paramref name="text"/> followed by each shown block's words, joined by <paramref name="separator"/>.</summary>
    public static string Append(string text, string separator, BarTrafficBlock? traffic, BarIntelligenceBlock? intelligence)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(separator);
        if (traffic?.Spoken is { } network)
        {
            text = text + separator + network;
        }

        return intelligence is null ? text : text + separator + intelligence.Spoken;
    }
}
