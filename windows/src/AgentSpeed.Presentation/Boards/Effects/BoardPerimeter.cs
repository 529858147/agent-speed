using AgentSpeed.Presentation.Layout;

namespace AgentSpeed.Presentation.Boards.Effects;

/// <summary>
/// The rounded square's outline as a closed path (the comets' track, DESIGN 2c Perimeter; <c>perimeterPoint</c> of
/// <c>spec/prototypes/board/fx.js</c>): a position in laps runs clockwise from the end of the top-left corner.
/// </summary>
public readonly struct BoardPerimeter
{
    private readonly double _straightW;
    private readonly double _straightH;
    private readonly double _arc;

    public BoardPerimeter(double left, double top, double width, double height, double radius)
    {
        Left = left;
        Top = top;
        Width = Math.Max(0, width);
        Height = Math.Max(0, height);
        Radius = Math.Clamp(radius, 0, Math.Min(Width, Height) / 2);
        _straightW = Width - 2 * Radius;
        _straightH = Height - 2 * Radius;
        _arc = Math.PI * Radius / 2;
        Length = 2 * _straightW + 2 * _straightH + 4 * _arc;
    }

    public double Left { get; }

    public double Top { get; }

    public double Width { get; }

    public double Height { get; }

    public double Radius { get; }

    /// <summary>Length of the outline in points.</summary>
    public double Length { get; }

    public PointD Center => new(Left + Width / 2, Top + Height / 2);

    /// <summary>The point at <paramref name="laps"/> (any real; whole laps wrap).</summary>
    public PointD Point(double laps)
    {
        if (Length <= 0)
        {
            return new PointD(Left, Top);
        }

        var d = (laps - Math.Floor(laps)) * Length;
        var r = Radius;
        var (x, y) = (Left, Top);
        if (d <= _straightW)
        {
            return new PointD(x + r + d, y);
        }

        d -= _straightW;
        if (d <= _arc)
        {
            return Corner(x + Width - r, y + r, -Math.PI / 2, d);
        }

        d -= _arc;
        if (d <= _straightH)
        {
            return new PointD(x + Width, y + r + d);
        }

        d -= _straightH;
        if (d <= _arc)
        {
            return Corner(x + Width - r, y + Height - r, 0, d);
        }

        d -= _arc;
        if (d <= _straightW)
        {
            return new PointD(x + Width - r - d, y + Height);
        }

        d -= _straightW;
        if (d <= _arc)
        {
            return Corner(x + r, y + Height - r, Math.PI / 2, d);
        }

        d -= _arc;
        if (d <= _straightH)
        {
            return new PointD(x, y + Height - r - d);
        }

        d -= _straightH;
        return Corner(x + r, y + r, Math.PI, Math.Min(d, _arc));
    }

    /// <summary>The unit direction from the center to the point at <paramref name="laps"/> (sparks fly outward along it).</summary>
    public PointD Outward(double laps)
    {
        var p = Point(laps);
        var c = Center;
        var dx = p.X - c.X;
        var dy = p.Y - c.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        return length > 0 ? new PointD(dx / length, dy / length) : new PointD(0, -1);
    }

    /// <summary>Laps covered by <paramref name="points"/> of outline.</summary>
    public double Laps(double points) => Length > 0 ? points / Length : 0;

    private PointD Corner(double cx, double cy, double start, double along)
    {
        var angle = start + (Radius > 0 ? along / Radius : 0);
        return new PointD(cx + Math.Cos(angle) * Radius, cy + Math.Sin(angle) * Radius);
    }
}
