using AgentSpeed.Presentation.Layout;

namespace AgentSpeed.Presentation.Boards;

/// <summary>A window a board keeps clear of, in physical pixels: an orb's disc or another board's square.</summary>
public readonly record struct BoardObstacle(PointD Center, double Half, bool Round)
{
    public static BoardObstacle Disc(PointD center, double radius) => new(center, radius, true);

    public static BoardObstacle Square(PointD center, double half) => new(center, half, false);
}

/// <summary>
/// The geometry of a board's square among orbs and boards (DESIGN 2c Data model: boards and orbs never overlap; the
/// board counts as its rounded square plus 12 pt). Physical pixels with an explicit display scale.
/// </summary>
public static class BoardSpace
{
    private const int MaxGridPoints = 250_000;

    /// <summary>The square keeps the 12 pt gap from <paramref name="other"/> (the square grown by a 12 pt rounded margin).</summary>
    public static bool IsClear(PointD center, double half, BoardObstacle other, double scale)
    {
        var gap = OrbGeometry.Gap * OrbGeometry.ValidScale(scale) - OrbGeometry.Tolerance;
        var dx = Math.Abs(center.X - other.Center.X);
        var dy = Math.Abs(center.Y - other.Center.Y);
        if (!other.Round)
        {
            var reach = half + other.Half + gap;
            return dx >= reach || dy >= reach;
        }

        var ox = Math.Max(0, dx - half);
        var oy = Math.Max(0, dy - half);
        return Math.Sqrt(ox * ox + oy * oy) >= other.Half + gap;
    }

    public static bool IsFree(PointD center, double half, IReadOnlyList<BoardObstacle> others, double scale)
    {
        ArgumentNullException.ThrowIfNull(others);
        foreach (var other in others)
        {
            if (!IsClear(center, half, other, scale))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The square lies inside the work area with the 12 pt margin.</summary>
    public static bool Fits(PointD center, double half, RectD work, double scale) => OrbGeometry.Fits(center, half, work, scale);

    /// <summary>The four touching slots around <paramref name="origin"/>: left, below, right, above (12 pt gap, centers aligned).</summary>
    public static IReadOnlyList<PointD> TouchingSlots(BoardObstacle origin, double half, double scale)
    {
        var d = origin.Half + OrbGeometry.Gap * OrbGeometry.ValidScale(scale) + half;
        var c = origin.Center;
        return [new PointD(c.X - d, c.Y), new PointD(c.X, c.Y + d), new PointD(c.X + d, c.Y), new PointD(c.X, c.Y - d)];
    }

    /// <summary>
    /// A new board next to the window it came from (DESIGN 2b Placement): the first touching slot inside the work area
    /// that keeps clear of every window; else the nearest free point of a 24 pt grid around it; else cascaded +24 / +24
    /// pt from it. Without an origin the board's default corner is the start.
    /// </summary>
    public static PointD PlaceNew(double half, BoardObstacle? origin, PointD fallback, IReadOnlyList<BoardObstacle> others, RectD work, double scale)
    {
        ArgumentNullException.ThrowIfNull(others);
        if (origin is not { } from)
        {
            return Resolve(fallback, half, others, work, scale);
        }

        var occupied = others.Contains(from) ? others : [.. others, from];
        foreach (var slot in TouchingSlots(from, half, scale))
        {
            if (Fits(slot, half, work, scale) && IsFree(slot, half, occupied, scale))
            {
                return slot;
            }
        }

        return GridSearch(from.Center, half, occupied, work, scale) ?? Cascade(from.Center, half, work, scale);
    }

    /// <summary>Keeps the center (clamped inside the work area) when it is free; else the nearest free grid point; never off-screen.</summary>
    public static PointD Resolve(PointD center, double half, IReadOnlyList<BoardObstacle> others, RectD work, double scale)
    {
        var start = OrbGeometry.Clamp(center, half, work, scale);
        return IsFree(start, half, others, scale) ? start : GridSearch(start, half, others, work, scale) ?? start;
    }

    /// <summary>The free point of a 24 pt grid through <paramref name="origin"/> nearest to it, or null when the display is full.</summary>
    public static PointD? GridSearch(PointD origin, double half, IReadOnlyList<BoardObstacle> others, RectD work, double scale)
    {
        ArgumentNullException.ThrowIfNull(others);
        var s = OrbGeometry.ValidScale(scale);
        var inset = half + OrbGeometry.Margin * s;
        var step = OrbGeometry.Grid * s;
        var lowX = Math.Ceiling((work.X + inset - origin.X) / step);
        var highX = Math.Floor((work.Right - inset - origin.X) / step);
        var lowY = Math.Ceiling((work.Y + inset - origin.Y) / step);
        var highY = Math.Floor((work.Bottom - inset - origin.Y) / step);
        if (!(lowX <= highX && lowY <= highY) || (highX - lowX + 1) * (highY - lowY + 1) > MaxGridPoints)
        {
            return null;
        }

        PointD? best = null;
        var bestDistance = double.PositiveInfinity;
        for (var j = lowY; j <= highY; j++)
        {
            for (var i = lowX; i <= highX; i++)
            {
                var point = new PointD(origin.X + i * step, origin.Y + j * step);
                var d = OrbGeometry.Distance(point, origin);
                if (d < bestDistance - 0.001 && IsFree(point, half, others, s))
                {
                    best = point;
                    bestDistance = d;
                }
            }
        }

        return best;
    }

    /// <summary>+24 / +24 pt (right and down) from <paramref name="origin"/>, kept inside the work area.</summary>
    public static PointD Cascade(PointD origin, double half, RectD work, double scale)
    {
        var step = OrbGeometry.Grid * OrbGeometry.ValidScale(scale);
        return OrbGeometry.Clamp(new PointD(origin.X + step, origin.Y + step), half, work, scale);
    }
}
