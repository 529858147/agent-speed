using AgentSpeed.Presentation.Formatting;

namespace AgentSpeed.Presentation.Boards.Live;

/// <summary>
/// 思考 hidden at zero on a board's sub-gauge (DESIGN 2c, Thinking hidden at zero): while <c>thinking</c> and another hero
/// readout are on, a shown 思考 that reads 0 under a live value for 3 s hides; a value above 0 brings it back at once. The
/// quiet state changes nothing (a hidden 思考 stays hidden, a shown one reads <c>–</c>); switching <c>thinking</c> on shows
/// it; with 思考 the only hero readout it never hides. Feed it every tick.
/// </summary>
public sealed class BoardThinkingHold
{
    private double? _since;

    public bool Hidden { get; private set; }

    /// <summary>Notes one tick at <paramref name="time"/> (seconds); returns whether 思考 is hidden.</summary>
    public bool Update(BoardReadouts readouts, BoardHeroReadouts hero, double time)
    {
        ArgumentNullException.ThrowIfNull(hero);
        if (!readouts.Thinking || !(readouts.Intelligence || readouts.Cost))
        {
            _since = null;
            return Hidden = false;
        }

        if (hero.Quiet)
        {
            return Hidden;
        }

        if (!hero.ThinkingZero)
        {
            _since = null;
            return Hidden = false;
        }

        _since ??= time;
        return Hidden = time >= _since.Value + ThinkingZeroHold.Seconds - 1e-9;
    }

    public void Reset()
    {
        _since = null;
        Hidden = false;
    }
}
