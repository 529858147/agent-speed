using AgentSpeed.Presentation.Layout;
using AgentSpeed.Presentation.Settings.Boards;
using AgentSpeed.Presentation.Settings.Orbs;

namespace AgentSpeed.Presentation.Boards;

/// <summary>
/// Where a board's window goes (DESIGN 2b, 2c Interaction and window), in physical pixels of the rounded square's center:
/// a release within 24 pt of a work-area edge snaps to the 12 pt margin as orbs do, positions are kept per display
/// arrangement as edge-anchored or fractional records, and a board without one starts at the work area's top-right.
/// </summary>
public static class BoardPlacement
{
    /// <summary>Half the square's side in pixels: what snapping and clamping keep inside the work area.</summary>
    public static double HalfSide(BoardSize size, double scale) => BoardSizes.Side(size) / 2 * OrbGeometry.ValidScale(scale);

    /// <summary>The center a drag released at <paramref name="center"/> settles on.</summary>
    public static PointD Release(PointD center, BoardSize size, RectD work, double scale) =>
        OrbGeometry.SnapToEdges(center, HalfSide(size, scale), work, scale);

    /// <summary>The record saved for that center on <paramref name="display"/>.</summary>
    public static OrbPositionRecord Record(string display, PointD center, BoardSize size, RectD work, double scale) =>
        OrbPositionRecord.FromCenter(display, center, HalfSide(size, scale), work, scale);

    /// <summary>The saved center of the board in this arrangement, or null when there is none.</summary>
    public static PointD? Restore(BoardsDocument document, string arrangement, BoardDefinition board, RectD work, double scale) =>
        document.RecordFor(arrangement, board.Id) is { } record ? record.ToCenter(work, scale, HalfSide(board.Size, scale)) : null;

    /// <summary>A board without a saved place: the top-right corner, each further board one step down and left.</summary>
    public static PointD Default(BoardSize size, RectD work, double scale, int index = 0)
    {
        var s = OrbGeometry.ValidScale(scale);
        var half = HalfSide(size, scale);
        var step = OrbGeometry.Grid * 2 * s * Math.Max(0, index);
        var center = new PointD(work.Right - OrbGeometry.DefaultInset * s - half - step, work.Y + OrbGeometry.DefaultInset * s + half + step);
        return PlacementMath.ClampDisc(center, half, work, OrbGeometry.Margin * s);
    }
}
