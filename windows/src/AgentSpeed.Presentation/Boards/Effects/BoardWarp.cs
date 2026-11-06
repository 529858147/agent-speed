using AgentSpeed.Presentation.Boards.Gpu;
using AgentSpeed.Presentation.Colors;
using AgentSpeed.Presentation.Hero;
using AgentSpeed.Presentation.Orb.Gpu;

namespace AgentSpeed.Presentation.Boards.Effects;

/// <summary>
/// Neon's hyperspace streaks behind the content (DESIGN 2c Style ambience; fx.js <c>warp</c>): from h = 0.2, 20 → 90 stars
/// (× the density) fly out of a point at 72 % / 28 % of the board, faster and longer with heat and overdrive, colored by
/// the heat palette, drawn additively and clipped to the rounded square. A fixed pool.
/// </summary>
public sealed class BoardWarp
{
    public const double MinHeat = 0.2;
    public const int Capacity = 136;

    private readonly Star[] _stars = new Star[Capacity];
    private readonly HeroRandom _random;
    private int _count;

    public BoardWarp(uint seed)
    {
        _random = new HeroRandom(seed);
    }

    public int Count => _count;

    /// <summary>Whether the streaks run at <paramref name="heat"/>.</summary>
    public static bool Runs(double heat) => heat > MinHeat;

    public static int Wanted(double heat, double density) => Math.Min(Capacity, (int)Math.Round((20 + heat * 70) * density));

    public void Clear() => _count = 0;

    /// <summary>Advances the streaks of a board of <paramref name="side"/> points.</summary>
    public void Step(double dt, double heat, double overdrive, double density, double side)
    {
        if (!Runs(heat))
        {
            _count = 0;
            return;
        }

        var want = Wanted(heat, density);
        while (_count < want)
        {
            _stars[_count++] = new Star(_random.Next() * Math.Tau, _random.Next() * 40);
        }

        _count = Math.Min(_count, want);
        var speed = 60 + heat * 520 + overdrive * 400;
        var reach = side * Math.Sqrt(2);
        for (var i = 0; i < _count; i++)
        {
            ref var star = ref _stars[i];
            star.Distance += speed * dt * (0.4 + star.Distance / 140);
            if (star.Distance > reach)
            {
                star.Distance = _random.Next() * 20;
                star.Angle = _random.Next() * Math.Tau;
            }
        }
    }

    public void Emit(BoardDrawList list, double left, double top, double side, double heat)
    {
        if (_count == 0)
        {
            return;
        }

        var cx = left + side * 0.72;
        var cy = top + side * 0.28;
        var width = 0.8 + heat * 0.8;
        for (var i = 0; i < _count; i++)
        {
            ref readonly var star = ref _stars[i];
            var cos = Math.Cos(star.Angle);
            var sin = Math.Sin(star.Angle);
            var x = cx + cos * star.Distance;
            var y = cy + sin * star.Distance;
            var tail = Math.Min(60, 6 + star.Distance * 0.18 * (0.3 + heat));
            var alpha = Math.Min(1, star.Distance / 120) * (0.25 + heat * 0.55);
            var color = HeatPalette.Sample(Math.Min(1, heat + star.Angle % 0.3)).WithAlpha(alpha);
            list.Add(OrbGpuInstance.Capsule(x - cos * tail, y - sin * tail, x, y, width, color, color), OrbBlend.Additive);
        }
    }

    private record struct Star(double Angle, double Distance);
}
