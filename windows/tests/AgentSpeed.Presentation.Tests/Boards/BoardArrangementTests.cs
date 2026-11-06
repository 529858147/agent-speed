using AgentSpeed.Presentation.Boards;
using AgentSpeed.Presentation.Layout;
using AgentSpeed.Presentation.Settings;
using AgentSpeed.Presentation.Settings.Boards;
using AgentSpeed.Presentation.Settings.Orbs;

namespace AgentSpeed.Presentation.Tests.Boards;

public class BoardArrangementTests
{
    private static readonly OrbDisplay Main = new("MAIN", new RectD(0, 0, 1920, 1040), 1, true);
    private static readonly OrbDisplay Side = new("SIDE", new RectD(1920, 0, 1280, 1000), 1.5, false);

    private static BoardsDocument Document(params BoardDefinition[] boards) => new(boards);

    [Fact]
    public void ABoardWithoutARecordStartsAtTheTopRightAndEachFurtherOneSteps()
    {
        var result = BoardArrangement.Resolve(Document(new("a"), new("b")), [Main], []);
        var a = result.Locations["a"];
        var b = result.Locations["b"];

        Assert.Equal(BoardPlacement.Default(BoardSize.M, Main.Work, 1), a.Center);
        Assert.Equal("MAIN", a.Display);
        Assert.Equal(170, a.Half);
        Assert.True(BoardSpace.IsClear(b.Center, b.Half, a.Obstacle, 1));
    }

    [Fact]
    public void ACommittedPositionIsRestoredInTheSameArrangement()
    {
        var document = Document(new BoardDefinition("a"));
        var committed = BoardArrangement.Commit(document, [Main], "a", new PointD(600, 500));
        Assert.NotNull(committed.RecordFor("MAIN", "a"));

        var result = BoardArrangement.Resolve(committed, [Main], []);
        Assert.Equal(600, result.Locations["a"].Center.X, 1);
        Assert.Equal(500, result.Locations["a"].Center.Y, 1);
    }

    [Fact]
    public void ACommittedPositionSurvivesARestart()
    {
        var committed = BoardArrangement.Commit(Document(new BoardDefinition("a"), new BoardDefinition("b")), [Main, Side], "b", new PointD(2600, 400));
        var json = SettingsSerializer.Serialize(AppSettings.Default with { Boards = committed });
        var back = SettingsSerializer.Deserialize(json).Boards;

        Assert.Equal(committed, back);
        var location = BoardArrangement.Resolve(back, [Main, Side], []).Locations["b"];
        Assert.Equal("SIDE", location.Display);
        Assert.Equal(2600, location.Center.X, 1);
        Assert.Equal(400, location.Center.Y, 1);
    }

    [Fact]
    public void ANewArrangementKeepsTheRecordOfAStillConnectedDisplay()
    {
        var document = BoardArrangement.Commit(Document(new BoardDefinition("a")), [Main], "a", new PointD(600, 500));
        var result = BoardArrangement.Resolve(document, [Main, Side], []);

        Assert.Equal("MAIN", result.Locations["a"].Display);
        Assert.Equal(600, result.Locations["a"].Center.X, 1);
        Assert.NotNull(result.Document.RecordFor(OrbArrangement.Key([Main, Side]), "a"));
    }

    [Fact]
    public void ABoardWhoseDisplayIsGoneMovesToTheMainDisplay()
    {
        var document = BoardArrangement.Commit(Document(new BoardDefinition("a")), [Main, Side], "a", new PointD(2600, 400));
        Assert.Equal("SIDE", document.RecordFor(OrbArrangement.Key([Main, Side]), "a")!.Value.Display);

        var result = BoardArrangement.Resolve(document, [Main], []);
        var location = result.Locations["a"];
        Assert.Equal("MAIN", location.Display);
        Assert.True(OrbGeometry.Fits(location.Center, location.Half, Main.Work, 1));
    }

    [Fact]
    public void BoardsNeverOverlapOrbsOrEachOther()
    {
        var a = new BoardDefinition("a");
        var b = new BoardDefinition("b");
        var document = BoardArrangement.Commit(Document(a, b), [Main], "a", new PointD(800, 500));
        document = BoardArrangement.Commit(document, [Main], "b", new PointD(820, 520));
        var orb = new OrbDisc(new PointD(1000, 500), 74);

        var result = BoardArrangement.Resolve(document, [Main], [orb]);
        var la = result.Locations["a"];
        var lb = result.Locations["b"];

        Assert.True(BoardSpace.IsClear(la.Center, la.Half, BoardObstacle.Disc(orb.Center, orb.Radius), 1));
        Assert.True(BoardSpace.IsClear(lb.Center, lb.Half, BoardObstacle.Disc(orb.Center, orb.Radius), 1));
        Assert.True(BoardSpace.IsClear(lb.Center, lb.Half, la.Obstacle, 1));
    }

    [Fact]
    public void HiddenBoardsAreNoObstacles()
    {
        var hidden = new BoardDefinition("h") { Visible = false };
        var document = BoardArrangement.Commit(Document(hidden, new BoardDefinition("v")), [Main], "h", new PointD(800, 500));
        document = BoardArrangement.Commit(document, [Main], "v", new PointD(800, 500));

        var result = BoardArrangement.Resolve(document, [Main], []);
        Assert.Equal(800, result.Locations["v"].Center.X, 1);
        Assert.Equal(500, result.Locations["v"].Center.Y, 1);
    }

    [Fact]
    public void ANewBoardTakesTheFirstFreeTouchingSlotOfItsOrigin()
    {
        var (document, board) = Document(new BoardDefinition("a")).Add("n")!.Value;
        var origin = BoardObstacle.Disc(new PointD(1000, 500), 74);
        var result = BoardArrangement.PlaceNew(document, [Main], board.Id, origin, [origin]);

        // Left first: 74 + 12 + 170 to the left, centers aligned.
        Assert.Equal(new PointD(1000 - 256, 500), result.Locations["n"].Center);
        Assert.NotNull(result.Document.RecordFor("MAIN", "n"));
    }

    [Fact]
    public void ANewBoardSkipsSlotsOutsideTheWorkArea()
    {
        var (document, board) = Document().Add("n")!.Value;
        var origin = BoardObstacle.Disc(new PointD(100, 500), 74);
        var center = BoardArrangement.PlaceNew(document, [Main], board.Id, origin, [origin]).Locations["n"].Center;

        // Left does not fit, nor does below (its left edge would leave the work area); right does.
        Assert.Equal(new PointD(100 + 256, 500), center);
    }

    [Fact]
    public void ANewBoardWithoutAnOriginGoesToTheMainDisplaysCorner()
    {
        var (document, board) = Document().Add("n")!.Value;
        var center = BoardArrangement.PlaceNew(document, [Side, Main], board.Id, null, []).Locations["n"].Center;
        Assert.Equal(BoardPlacement.Default(BoardSize.M, Main.Work, 1), center);
    }

    [Fact]
    public void AReleaseOnAnotherWindowSlidesToTheNearestFreeSpot()
    {
        var board = new BoardDefinition("a");
        var other = BoardObstacle.Square(new PointD(800, 500), 170);
        var settled = BoardArrangement.Release(board, [Main], [other], new PointD(810, 505));

        Assert.True(BoardSpace.IsClear(settled, 170, other, 1));
        Assert.True(OrbGeometry.Fits(settled, 170, Main.Work, 1));

        var free = BoardArrangement.Release(board, [Main], [other], new PointD(400, 300));
        Assert.Equal(new PointD(400, 300), free);
    }

    [Fact]
    public void ASquareKeepsTheGapFromADiscAtItsCorner()
    {
        var center = new PointD(500, 500);
        // A disc of radius 40 off the corner: clear only beyond 40 + 12 from the corner point.
        var corner = new PointD(600, 600);
        var near = BoardObstacle.Disc(new PointD(corner.X + 30, corner.Y + 30), 40);
        var far = BoardObstacle.Disc(new PointD(corner.X + 40, corner.Y + 40), 40);

        Assert.False(BoardSpace.IsClear(center, 100, near, 1));
        Assert.True(BoardSpace.IsClear(center, 100, far, 1));
    }
}
