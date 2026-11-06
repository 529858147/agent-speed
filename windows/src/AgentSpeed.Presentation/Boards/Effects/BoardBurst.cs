using AgentSpeed.Presentation.Colors;

namespace AgentSpeed.Presentation.Boards.Effects;

/// <summary>
/// Up to four colors a burst's particles take in turn (fx.js <c>colors[i % length]</c>), stored inline so emitting never
/// allocates.
/// </summary>
public readonly record struct BoardColors(Rgba C0, Rgba C1, Rgba C2, Rgba C3, int Count)
{
    public BoardColors(Rgba c0) : this(c0, c0, c0, c0, 1)
    {
    }

    public BoardColors(Rgba c0, Rgba c1) : this(c0, c1, c1, c1, 2)
    {
    }

    public BoardColors(Rgba c0, Rgba c1, Rgba c2, Rgba c3) : this(c0, c1, c2, c3, 4)
    {
    }

    public Rgba At(int index) => (Count <= 0 ? 0 : index % Count) switch
    {
        0 => C0,
        1 => C1,
        2 => C2,
        _ => C3,
    };
}

/// <summary>
/// The options of fx.js <c>burst</c>: <see cref="Count"/> particles from a point at <see cref="Speed"/> (pt/s, each 0.35–1.1×)
/// in <see cref="Angle"/> ± <see cref="Spread"/>/2 (radians, y down; NaN: any direction), living <see cref="Life"/> s
/// (0.6–1.2×), <see cref="Size"/> pt (0.6–1.4×), pulled by <see cref="Gravity"/> pt/s² and slowed by <see cref="Drag"/> per s.
/// </summary>
public readonly record struct BoardBurst(
    int Count,
    double Speed,
    double Life,
    BoardColors Colors,
    double Size = 1.8,
    double Gravity = 90,
    double Drag = 1.6,
    double Angle = double.NaN,
    double Spread = Math.Tau,
    bool Streak = true);
