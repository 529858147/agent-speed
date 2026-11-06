using AgentSpeed.Presentation.Boards.Gpu;
using AgentSpeed.Presentation.Colors;
using AgentSpeed.Presentation.Orb.Gpu;

namespace AgentSpeed.Presentation.Boards.Effects;

/// <summary>An expanding ring (fx.js <c>shockwave</c>), canvas points.</summary>
public struct BoardWave
{
    public float X;
    public float Y;
    public float MaxRadius;
    public float Age;
    public float Life;
    public Rgba Color;
}

/// <summary>
/// The board's shockwaves (fx.js <c>shockwave</c>): a ring from 4 pt to its radius with an ease-out cubic, 2.5 → 0.5 pt wide
/// and fading from 0.85, drawn additively. A fixed pool; the oldest wave gives way when it is full.
/// </summary>
public sealed class BoardWaves
{
    public const int Capacity = 12;

    private readonly BoardWave[] _items = new BoardWave[Capacity];

    public int Count { get; private set; }

    public void Clear() => Count = 0;

    public void Add(double x, double y, Rgba color, double maxRadius, double life)
    {
        if (Count == Capacity)
        {
            Array.Copy(_items, 1, _items, 0, Capacity - 1);
            Count--;
        }

        _items[Count++] = new BoardWave { X = (float)x, Y = (float)y, MaxRadius = (float)maxRadius, Life = (float)Math.Max(0.05, life), Color = color };
    }

    public void Step(double dt)
    {
        var write = 0;
        for (var read = 0; read < Count; read++)
        {
            var wave = _items[read];
            wave.Age += (float)dt;
            if (wave.Age < wave.Life)
            {
                _items[write++] = wave;
            }
        }

        Count = write;
    }

    public void Emit(BoardDrawList list)
    {
        for (var i = 0; i < Count; i++)
        {
            ref readonly var wave = ref _items[i];
            var t = Math.Clamp(wave.Age / wave.Life, 0, 1);
            var eased = 1 - Math.Pow(1 - t, 3);
            var radius = Math.Max(0, 4 + (wave.MaxRadius - 4) * eased);
            var width = 2.5 * (1 - t) + 0.5;
            list.Add(OrbGpuInstance.Ring(wave.X, wave.Y, radius, width, wave.Color.WithAlpha((1 - t) * 0.85)), OrbBlend.Additive);
        }
    }
}
