namespace AgentSpeed.Presentation.Formatting;

/// <summary>
/// The hold before a thinking rate that reads 0 is hidden (DESIGN 10, Thinking hidden at zero): some sources never report
/// reasoning, so <c>思考 0 tok/s</c> says nothing, but a pause between reasoning bursts must not flicker. The rate shows
/// while it reads above 0; after 3 s at 0 it hides, and a rate above 0 brings it back at once. Feed it every sample.
/// </summary>
public sealed class ThinkingZeroHold
{
    public const double Seconds = 3;

    private double? _since;

    /// <summary>The rate as shown reads 0 (it rounds to <c>0.0</c> or is nothing at all).</summary>
    public static bool ReadsZero(double liveThinking) => DisplayText.IsNegligibleSpeed(liveThinking);

    /// <summary>Notes whether the shown rate reads 0 at <paramref name="time"/> (seconds); true once it has for <see cref="Seconds"/>.</summary>
    public bool Hides(bool zero, double time)
    {
        if (!zero)
        {
            _since = null;
            return false;
        }

        _since ??= time;
        return time >= _since + Seconds;
    }
}
