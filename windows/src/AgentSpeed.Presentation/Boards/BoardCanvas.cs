using AgentSpeed.Presentation.Boards.Effects;

namespace AgentSpeed.Presentation.Boards;

/// <summary>
/// The square canvas of a board's window (points): the rounded square centered with <see cref="Margin"/> on every side for
/// the effects that leave it (sparks, waves, the bloom; the particle canvas of <c>spec/prototypes/board/fx.js</c> is inset
/// −40 px). Board coordinates are from the square's top-left corner; canvas coordinates add the margin.
/// </summary>
public readonly record struct BoardCanvas(double Side, double Radius)
{
    public const double Margin = 40;

    /// <summary>
    /// The bloom fades out along the rounded square's outline to nothing this far outside it, 2 pt short of the window's edge
    /// (DESIGN 2c Perimeter).
    /// </summary>
    public const double BloomReach = Margin - 2;

    /// <summary>Inset of the ring and the comets' track from the square's edge (fx.js <c>inset</c>).</summary>
    public const double TrackInset = 0.8;

    public static BoardCanvas For(BoardStyle style, BoardSize size) => new(BoardSizes.Side(size), BoardMetrics.For(style, size).Radius);

    /// <summary>Side of the window's canvas.</summary>
    public double Extent => Side + 2 * Margin;

    public double Center => Extent / 2;

    public double Half => Side / 2;

    /// <summary>The comets' track and the ring, in canvas coordinates.</summary>
    public BoardPerimeter Track => new(Margin + TrackInset, Margin + TrackInset, Side - 2 * TrackInset, Side - 2 * TrackInset,
        Radius - TrackInset);

    /// <summary>Whether a canvas point lies on the rounded square (hit testing: the margin lets clicks through).</summary>
    public bool Contains(double x, double y)
    {
        var qx = Math.Abs(x - Center) - Half + Radius;
        var qy = Math.Abs(y - Center) - Half + Radius;
        var outside = Math.Sqrt(Math.Max(qx, 0) * Math.Max(qx, 0) + Math.Max(qy, 0) * Math.Max(qy, 0));
        return outside + Math.Min(Math.Max(qx, qy), 0) - Radius <= 0;
    }
}
