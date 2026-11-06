using AgentSpeed.Presentation.Boards.Gpu;
using AgentSpeed.Presentation.Colors;
using AgentSpeed.Presentation.Motion;
using AgentSpeed.Presentation.Orb.Gpu;

namespace AgentSpeed.Presentation.Boards.Effects;

/// <summary>One stop of a sweep's gradient: where it lies in the CSS image (0–1) and its color, or the climber's color at that alpha.</summary>
public readonly record struct BoardSweepStop(double At, Rgba Color, bool Source = false);

/// <summary>
/// A style's sweep (the prototypes' <c>.sweep</c> / <c>.wipe</c>): a gradient image <paramref name="Size"/> rows wide at the CSS
/// <paramref name="Angle"/> slid across the row from CSS background position <paramref name="From"/> to <paramref name="To"/> with
/// <c>cubic-bezier(EaseX1, EaseY1, .2, 1)</c>, inside the row's rounded box (<paramref name="Corner"/>, inset <paramref name="Inset"/>
/// at the top and bottom), added to the row (screen) or laid over it (timing's multiply on light).
/// </summary>
public sealed record BoardSweepLook(double Size, double From, double To, double Seconds, IReadOnlyList<BoardSweepStop> Stops, bool Additive = true,
    double Angle = 90, double Corner = 0, double Inset = 0, double EaseX1 = 0.3, double EaseY1 = 0.7);

/// <summary>A band of light crossing one row (canvas points).</summary>
public struct BoardSweep
{
    public float Left;
    public float Right;
    public float CenterY;
    public float HalfHeight;
    public float Age;
    public BoardSweepLook Look;
    public Rgba Source;
}

/// <summary>
/// The light sweeps across overtaking rows (DESIGN 2c Overtake), each in its style's gradient and the climber's source color,
/// clipped to the row. Rows move while they play, so the host passes each sweep's current row center with <see cref="Follow"/>.
/// </summary>
public sealed class BoardSweeps
{
    public const int Capacity = 8;

    private readonly BoardSweep[] _items = new BoardSweep[Capacity];
    private readonly string?[] _keys = new string?[Capacity];

    public int Count { get; private set; }

    public void Clear()
    {
        Count = 0;
        Array.Clear(_items);
        Array.Clear(_keys);
    }

    public void Add(string key, double left, double right, double centerY, double height, Rgba source, BoardSweepLook look)
    {
        ArgumentNullException.ThrowIfNull(look);
        if (Count == Capacity)
        {
            RemoveAt(0);
        }

        _keys[Count] = key;
        _items[Count++] = new BoardSweep
        {
            Left = (float)left,
            Right = (float)right,
            CenterY = (float)centerY,
            HalfHeight = (float)Math.Max(1, height / 2 - look.Inset),
            Look = look,
            Source = source,
        };
    }

    /// <summary>Moves the sweeps of <paramref name="key"/> with their row.</summary>
    public void Follow(string key, double centerY)
    {
        for (var i = 0; i < Count; i++)
        {
            if (string.Equals(_keys[i], key, StringComparison.Ordinal))
            {
                _items[i].CenterY = (float)centerY;
            }
        }
    }

    public void Step(double dt)
    {
        var write = 0;
        for (var read = 0; read < Count; read++)
        {
            var sweep = _items[read];
            sweep.Age += (float)dt;
            if (sweep.Age < sweep.Look.Seconds)
            {
                _keys[write] = _keys[read];
                _items[write++] = sweep;
            }
        }

        for (var i = write; i < Count; i++)
        {
            _keys[i] = null;
            _items[i] = default;
        }

        Count = write;
    }

    public void Emit(BoardDrawList list)
    {
        ArgumentNullException.ThrowIfNull(list);
        for (var i = 0; i < Count; i++)
        {
            ref readonly var sweep = ref _items[i];
            var look = sweep.Look;
            var t = Easing.CubicBezier(look.EaseX1, look.EaseY1, 0.2, 1, Math.Clamp(sweep.Age / look.Seconds, 0, 1));
            var width = (double)sweep.Right - sweep.Left;
            var half = width / 2;
            var center = sweep.Left + half;
            var image = width * look.Size;

            // CSS background-position p puts the image's left edge at (1 − size) × p row widths; the gradient line of an image at
            // angle θ is |w sin θ| + |h cos θ| long and centered on the image, its stops measured along the line.
            var left = sweep.Left + width * (1 - look.Size) * (look.From + (look.To - look.From) * t);
            var angle = (look.Angle - 90) * Math.PI / 180;
            var cos = Math.Cos(angle);
            var length = image * Math.Abs(cos) + 2 * sweep.HalfHeight * Math.Abs(Math.Sin(angle));
            var offset = (left + image / 2 - center) * cos;
            var reach = half * Math.Abs(cos) + sweep.HalfHeight * Math.Abs(Math.Sin(angle));
            var blend = look.Additive ? OrbBlend.Additive : OrbBlend.Over;
            var stops = look.Stops;
            for (var s = 1; s < stops.Count; s++)
            {
                var a = stops[s - 1];
                var b = stops[s];
                var from = offset + (a.At - 0.5) * length;
                var to = offset + (b.At - 0.5) * length;
                if (to <= -reach || from >= reach || to - from < 0.01)
                {
                    continue;
                }

                list.Add(OrbGpuInstance.RoundedRect(center, sweep.CenterY, half, sweep.HalfHeight, 0, look.Corner, Resolve(a, sweep.Source))
                    .Band(Resolve(b, sweep.Source), look.Angle - 90, from, to), blend);
            }
        }
    }

    private static Rgba Resolve(in BoardSweepStop stop, Rgba source) => stop.Source ? source.WithAlpha(stop.Color.A) : stop.Color;

    private void RemoveAt(int index)
    {
        Array.Copy(_items, index + 1, _items, index, Count - index - 1);
        Array.Copy(_keys, index + 1, _keys, index, Count - index - 1);
        Count--;
        _keys[Count] = null;
    }
}
