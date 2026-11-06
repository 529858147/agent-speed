using AgentSpeed.Presentation.Boards.Gpu;
using AgentSpeed.Presentation.Motion;

namespace AgentSpeed.Presentation.Boards.Scene;

public enum BoardCalloutKind
{
    /// <summary><c>▲n</c> before the climber's value (glass and neon mint chip, timing green delta).</summary>
    Climb,

    /// <summary>Timing: the climber's position block flashes green.</summary>
    Block,

    /// <summary><c>boards.newPeak</c> above the number.</summary>
    NewPeak,

    /// <summary>Timing: the <c>boards.newLeader</c> banner across the hero.</summary>
    NewLeader,
}

/// <summary>
/// A short-lived piece of content (DESIGN 2c Overtake, New leader, Hero number): rasterized once when its event fires under
/// <see cref="Key"/>, then faded and moved by the scene.
/// </summary>
/// <param name="Text">The chip's text (<c>▲2</c>), the block's rank, or the new leader's project.</param>
/// <param name="Row">The row the callout belongs to.</param>
public sealed record BoardCallout(string Key, BoardCalloutKind Kind, string Text, string? Row, double Start)
{
    public double Seconds => Kind switch
    {
        BoardCalloutKind.Block => 0.9,
        BoardCalloutKind.NewPeak => 2.2,
        BoardCalloutKind.NewLeader => 1.9,
        _ => 1.6,
    };

    public double Progress(double now) => (now - Start) / Seconds;

    public bool Done(double now) => now - Start >= Seconds;
}

/// <summary>The scene's callouts: started by events, drawn while alive, dropped when done.</summary>
public sealed class BoardCallouts
{
    /// <summary>Room around a chip in its layer (<see cref="BoardCalloutKind.Climb"/>, <see cref="BoardCalloutKind.NewPeak"/>) for its glow.</summary>
    public const double ChipPad = 8;

    private readonly List<BoardCallout> _items = new(8);

    public IReadOnlyList<BoardCallout> Items => _items;

    public int Count => _items.Count;

    public bool Alive(double now)
    {
        foreach (var item in _items)
        {
            if (!item.Done(now))
            {
                return true;
            }
        }

        return false;
    }

    public void Add(BoardCallout callout)
    {
        ArgumentNullException.ThrowIfNull(callout);
        _items.RemoveAll(c => string.Equals(c.Key, callout.Key, StringComparison.Ordinal));
        _items.Add(callout);
    }

    public void Prune(double now)
    {
        for (var i = _items.Count - 1; i >= 0; i--)
        {
            if (_items[i].Done(now))
            {
                _items.RemoveAt(i);
            }
        }
    }

    public void Clear() => _items.Clear();

    /// <summary>
    /// A chip's opacity, horizontal shift and scale at <paramref name="t"/> (the prototypes' <c>chip</c>, <c>delta</c> and
    /// <c>peakchip</c> keyframes): it pops in, holds, then fades as it drifts on.
    /// </summary>
    public static (double Alpha, double Dx, double Dy, double Scale) Chip(BoardStyle style, BoardCalloutKind kind, double t)
    {
        t = Math.Clamp(t, 0, 1);
        var (inEnd, outStart) = kind == BoardCalloutKind.NewPeak || style == BoardStyle.Timing ? (0.12, kind == BoardCalloutKind.NewPeak ? 0.75 : 0.7)
            : (0.14, 0.75);
        if (t < inEnd)
        {
            var e = Easing.OutCubic(t / inEnd);
            var (dx, dy, scale) = kind == BoardCalloutKind.NewPeak ? (0.0, 6.0, 0.7) : style switch
            {
                BoardStyle.Timing => (8.0, 0.0, 1.0),
                BoardStyle.Neon => (6.0, 0.0, 0.6),
                _ => (-4.0, 0.0, 0.6),
            };
            return (e, dx * (1 - e), dy * (1 - e), scale + (1 - scale) * e);
        }

        if (t < outStart)
        {
            return (1, 0, 0, 1);
        }

        var o = Easing.OutCubic((t - outStart) / (1 - outStart));
        var (odx, ody, oscale) = kind == BoardCalloutKind.NewPeak ? (0.0, style == BoardStyle.Glass ? -4.0 : 0.0, 1.0) : style switch
        {
            BoardStyle.Timing => (-4.0, 0.0, 1.0),
            BoardStyle.Neon => (0.0, 0.0, 1.0),
            _ => (4.0, 0.0, 0.95),
        };
        return (1 - o, odx * o, ody * o, 1 + (oscale - 1) * o);
    }

    /// <summary>
    /// Timing's banner reveal (CSS <c>clip-path</c>): the visible part from its left, as fractions of its width — it wipes in
    /// from the left by 18 %, holds, and wipes out to the right from 78 %.
    /// </summary>
    public static (double From, double To) Wipe(double t)
    {
        t = Math.Clamp(t, 0, 1);
        if (t < 0.18)
        {
            return (0, Easing.OutCubic(t / 0.18));
        }

        return t < 0.78 ? (0, 1) : (Easing.OutCubic((t - 0.78) / 0.22), 1);
    }

    /// <summary>Emits a callout layer at its box's top-left (<paramref name="x"/>, <paramref name="y"/>), canvas points.</summary>
    public static void Emit(BoardDrawList list, in BoardLayerSlot slot, double x, double y, double alpha, double scale, RectBounds clip = default)
    {
        var instance = BoardLayerInstance.Create(slot, x + slot.Width * (1 - scale) / 2, y + slot.Height * (1 - scale) / 2, alpha, clip: clip);
        instance.Rect.Z *= (float)scale;
        instance.Rect.W *= (float)scale;
        list.AddLayer(instance);
    }
}
