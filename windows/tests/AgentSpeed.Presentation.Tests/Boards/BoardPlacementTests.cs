using AgentSpeed.Presentation.Boards;
using AgentSpeed.Presentation.Layout;
using AgentSpeed.Presentation.Settings.Boards;

namespace AgentSpeed.Presentation.Tests.Boards;

public class BoardPlacementTests
{
    private static readonly RectD Work = new(0, 0, 3840, 2100);

    [Fact]
    public void AReleaseNearAnEdgeSnapsToTheMargin()
    {
        // M at 2×: half side 340 px; 20 pt from the right edge (40 px) is within 24 pt.
        var released = BoardPlacement.Release(new PointD(3840 - 40 - 340, 1000), BoardSize.M, Work, 2);

        Assert.Equal(3840 - 24 - 340, released.X, 6);
        Assert.Equal(1000, released.Y, 6);
    }

    [Fact]
    public void AReleaseAwayFromTheEdgesStays()
    {
        var center = new PointD(1500, 900);

        Assert.Equal(center, BoardPlacement.Release(center, BoardSize.L, Work, 2.4));
    }

    [Fact]
    public void PositionsRoundTripPerArrangement()
    {
        var board = new BoardDefinition("b1") { Size = BoardSize.S };
        var document = new BoardsDocument([board]);
        var center = BoardPlacement.Release(new PointD(3840 - 30 - 280, 600), BoardSize.S, Work, 2);
        document = document.WithPosition("A", board.Id, BoardPlacement.Record("D1", center, BoardSize.S, Work, 2));

        var restored = BoardPlacement.Restore(document, "A", board, Work, 2);

        Assert.NotNull(restored);
        Assert.Equal(center.X, restored.Value.X, 0.05);
        Assert.Equal(center.Y, restored.Value.Y, 0.05);
        Assert.Null(BoardPlacement.Restore(document, "B", board, Work, 2));
    }

    [Fact]
    public void TheDefaultIsTopRightAndStaysInside()
    {
        var first = BoardPlacement.Default(BoardSize.M, Work, 2);
        var clamped = BoardPlacement.Default(BoardSize.L, new RectD(0, 0, 1920, 1080), 2.4, index: 3);

        Assert.Equal(3840 - 48 - 340, first.X, 6);
        Assert.Equal(48 + 340, first.Y, 6);
        Assert.Equal(1920 - 57.6 - 504 - 345.6, clamped.X, 6);
        Assert.Equal(1080 - 28.8 - 504, clamped.Y, 6);
    }
}
