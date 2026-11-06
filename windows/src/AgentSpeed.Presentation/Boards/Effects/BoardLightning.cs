using System.Numerics;
using AgentSpeed.Presentation.Boards.Gpu;
using AgentSpeed.Presentation.Colors;
using AgentSpeed.Presentation.Hero;
using AgentSpeed.Presentation.Orb.Gpu;

namespace AgentSpeed.Presentation.Boards.Effects;

/// <summary>
/// Branching lightning (fx.js <c>lightning</c>): a jagged main path of 7–10 segments between two points, displaced across
/// its direction by up to 18 % of its length (least at the ends), and branches from its inner joints, flickering out over
/// its life. Drawn as stroked polylines of three additive layers (6, 2.2 and 1 pt wide at 0.25, 0.8 and 1; branches 0.6×).
/// Preallocated paths; the oldest bolt gives way when the pool is full.
/// </summary>
public sealed class BoardLightning
{
    public const int Capacity = 8;
    public const int MaxBranches = 4;
    private const int MaxPoints = 12;
    private const int PathsPerBolt = MaxBranches + 1;

    private readonly Bolt[] _bolts = new Bolt[Capacity];
    private readonly HeroRandom _random;

    public BoardLightning(uint seed)
    {
        _random = new HeroRandom(seed);
        for (var i = 0; i < Capacity; i++)
        {
            _bolts[i] = new Bolt();
        }
    }

    public int Count { get; private set; }

    public void Clear() => Count = 0;

    public void Strike(double x0, double y0, double x1, double y1, Rgba color, int branches = 2, double life = 0.32)
    {
        if (Count == Capacity)
        {
            var oldest = _bolts[0];
            Array.Copy(_bolts, 1, _bolts, 0, Capacity - 1);
            _bolts[Capacity - 1] = oldest;
            Count--;
        }

        var bolt = _bolts[Count++];
        bolt.Age = 0;
        bolt.Life = Math.Max(0.05, life);
        bolt.Color = color;
        bolt.PathCount = 1;
        Make(bolt, 0, x0, y0, x1, y1);
        var main = bolt.Lengths[0];
        for (var i = 0; i < Math.Clamp(branches, 0, MaxBranches) && main > 3; i++)
        {
            var k = 2 + (int)Math.Floor(_random.Next() * (main - 3));
            var from = bolt.Points[k];
            var path = bolt.PathCount++;
            Make(bolt, path, from.X, from.Y, from.X + (_random.Next() - 0.3) * 60, from.Y + (_random.Next() - 0.2) * 50);
        }
    }

    public void Step(double dt)
    {
        var write = 0;
        for (var read = 0; read < Count; read++)
        {
            var bolt = _bolts[read];
            bolt.Age += dt;
            if (bolt.Age < bolt.Life)
            {
                _bolts[read] = _bolts[write];
                _bolts[write++] = bolt;
            }
        }

        Count = write;
    }

    public void Emit(BoardDrawList list, bool glows)
    {
        for (var b = 0; b < Count; b++)
        {
            var bolt = _bolts[b];
            var flicker = (1 - bolt.Age / bolt.Life) * (0.65 + 0.35 * Math.Sin(bolt.Age * 90));
            for (var path = 0; path < bolt.PathCount; path++)
            {
                var scale = path == 0 ? 1 : 0.6;
                var start = path * MaxPoints;
                var count = bolt.Lengths[path];
                for (var i = 0; i + 1 < count; i++)
                {
                    var a = bolt.Points[start + i];
                    var c = bolt.Points[start + i + 1];
                    var startCut = i > 0 ? Bisector(bolt.Points[start + i - 1], a, c) : Vector2.Zero;
                    var endCut = i + 2 < count ? Bisector(a, c, bolt.Points[start + i + 2]) : Vector2.Zero;
                    list.Add(OrbGpuInstance.Stroke(a.X, a.Y, c.X, c.Y, glows ? 3 * scale : 0, 1.1 * scale, 0.5 * scale, bolt.Color, Rgba.White,
                        glows ? 0.25 * flicker * scale : 0, 0.8 * flicker * scale, flicker * scale, additive: true, startCut, endCut),
                        OrbBlend.Additive);
                }
            }
        }
    }

    private void Make(Bolt bolt, int path, double ax, double ay, double bx, double by)
    {
        var n = 7 + (int)Math.Floor(_random.Next() * 4);
        var length = Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));
        var nx = -(by - ay) / (length > 0 ? length : 1);
        var ny = (bx - ax) / (length > 0 ? length : 1);
        var start = path * MaxPoints;
        bolt.Points[start] = new Vector2((float)ax, (float)ay);
        for (var i = 1; i < n; i++)
        {
            var t = i / (double)n;
            var jitter = (_random.Next() - 0.5) * length * 0.18 * (1 - Math.Abs(t - 0.5));
            bolt.Points[start + i] = new Vector2((float)(ax + (bx - ax) * t + nx * jitter), (float)(ay + (by - ay) * t + ny * jitter));
        }

        bolt.Points[start + n] = new Vector2((float)bx, (float)by);
        bolt.Lengths[path] = n + 1;
    }

    private static Vector2 Bisector(Vector2 before, Vector2 joint, Vector2 after)
    {
        var d0 = Vector2.Normalize(joint - before);
        var d1 = Vector2.Normalize(after - joint);
        var sum = d0 + d1;
        return sum.LengthSquared() > 1e-6f && float.IsFinite(sum.X) ? Vector2.Normalize(sum) : float.IsFinite(d1.X) ? d1 : Vector2.Zero;
    }

    private sealed class Bolt
    {
        public readonly Vector2[] Points = new Vector2[PathsPerBolt * MaxPoints];
        public readonly int[] Lengths = new int[PathsPerBolt];
        public int PathCount;
        public double Age;
        public double Life;
        public Rgba Color;
    }
}
