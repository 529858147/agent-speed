using AgentSpeed.Presentation.Boards.Gpu;
using AgentSpeed.Presentation.Colors;
using AgentSpeed.Presentation.Hero;
using AgentSpeed.Presentation.Orb.Gpu;

namespace AgentSpeed.Presentation.Boards.Effects;

/// <summary>One spark, ember or streak (canvas points, y down).</summary>
public struct BoardParticle
{
    public float X;
    public float Y;
    public float VelocityX;
    public float VelocityY;
    public float Age;
    public float Life;
    public float Size;
    public float Gravity;
    public float Drag;
    public bool Streak;
    public Rgba Color;

    public readonly float Fade => Life <= 0 ? 0 : Math.Clamp(1 - Age / Life, 0, 1);
}

/// <summary>
/// The board's sparks, embers and streak particles (fx.js <c>burst</c>, <c>streak</c> and the particle loop): one pool of
/// <see cref="Capacity"/> preallocated particles, drawn additively as round-capped streaks or dots; a full pool drops new
/// particles. Deterministic for a seed, never allocating after construction.
/// </summary>
public sealed class BoardParticles
{
    public const int Capacity = 768;

    /// <summary>Particles a streak releases along a row (fx.js <c>streak</c>).</summary>
    public const int StreakCount = 26;

    // A streak is drawn from where it was 2.5 frames before the last step to where it is (fx.js, at 60 fps).
    private const double TailSeconds = 3.5 / 60;

    private readonly BoardParticle[] _items = new BoardParticle[Capacity];
    private readonly HeroRandom _random;

    public BoardParticles(uint seed)
    {
        _random = new HeroRandom(seed);
    }

    public int Count { get; private set; }

    public ReadOnlySpan<BoardParticle> Items => _items.AsSpan(0, Count);

    public void Clear() => Count = 0;

    /// <summary>fx.js <c>burst</c> at (<paramref name="x"/>, <paramref name="y"/>), <paramref name="density"/> × the count.</summary>
    public void Burst(double x, double y, in BoardBurst burst, double density = 1)
    {
        var count = (int)Math.Round(Math.Max(0, burst.Count) * density);
        for (var i = 0; i < count && Count < Capacity; i++)
        {
            var baseAngle = double.IsNaN(burst.Angle) ? _random.Next() * Math.Tau : burst.Angle;
            var angle = baseAngle + (_random.Next() - 0.5) * burst.Spread;
            var speed = burst.Speed * (0.35 + _random.Next() * 0.75);
            _items[Count++] = new BoardParticle
            {
                X = (float)x,
                Y = (float)y,
                VelocityX = (float)(Math.Cos(angle) * speed),
                VelocityY = (float)(Math.Sin(angle) * speed),
                Life = (float)(burst.Life * (0.6 + _random.Next() * 0.6)),
                Size = (float)(burst.Size * (0.6 + _random.Next() * 0.8)),
                Gravity = (float)burst.Gravity,
                Drag = (float)burst.Drag,
                Streak = burst.Streak,
                Color = burst.Colors.At(i),
            };
        }
    }

    /// <summary>fx.js <c>streak</c>: particles along a row from <paramref name="x0"/> to <paramref name="x1"/> flying right.</summary>
    public void Streak(double x0, double y, double x1, Rgba color, double density = 1)
    {
        var count = (int)Math.Round(StreakCount * density);
        for (var i = 0; i < count && Count < Capacity; i++)
        {
            var x = x0 + _random.Next() * (x1 - x0);
            _items[Count++] = new BoardParticle
            {
                X = (float)x,
                Y = (float)(y + (_random.Next() - 0.5) * 8),
                VelocityX = (float)(220 + _random.Next() * 260),
                VelocityY = (float)((_random.Next() - 0.5) * 30),
                Life = (float)(0.35 + _random.Next() * 0.35),
                Size = (float)(1 + _random.Next() * 1.2),
                Gravity = 0,
                Drag = 2.5f,
                Streak = true,
                Color = i % 3 != 0 ? color : Rgba.White,
            };
        }
    }

    public void Step(double dt)
    {
        var t = (float)dt;
        var write = 0;
        for (var read = 0; read < Count; read++)
        {
            var p = _items[read];
            p.Age += t;
            if (p.Age >= p.Life)
            {
                continue;
            }

            p.VelocityX -= p.VelocityX * p.Drag * t;
            p.VelocityY -= p.VelocityY * p.Drag * t;
            p.VelocityY += p.Gravity * t;
            p.X += p.VelocityX * t;
            p.Y += p.VelocityY * t;
            _items[write++] = p;
        }

        Count = write;
    }

    public void Emit(BoardDrawList list)
    {
        foreach (ref readonly var p in Items)
        {
            var fade = p.Fade;
            if (fade <= 0.002)
            {
                continue;
            }

            var color = p.Color.WithAlpha(fade);
            if (p.Streak)
            {
                var tailX = p.X - p.VelocityX * TailSeconds;
                var tailY = p.Y - p.VelocityY * TailSeconds;
                list.Add(OrbGpuInstance.Capsule(tailX, tailY, p.X, p.Y, p.Size, color, color), OrbBlend.Additive);
            }
            else
            {
                list.Add(OrbGpuInstance.Circle(p.X, p.Y, p.Size, color), OrbBlend.Additive);
            }
        }
    }
}
