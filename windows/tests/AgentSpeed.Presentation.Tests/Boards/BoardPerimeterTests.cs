using AgentSpeed.Presentation.Boards.Effects;

namespace AgentSpeed.Presentation.Tests.Boards;

public class BoardPerimeterTests
{
    private static readonly BoardPerimeter Square = new(10, 20, 100, 100, 20);

    [Fact]
    public void LengthIsTheStraightsAndFourQuarterArcs()
    {
        Assert.Equal(4 * 60 + 2 * Math.PI * 20, Square.Length, 6);
    }

    [Fact]
    public void StartsAtTheEndOfTheTopLeftCornerAndRunsClockwise()
    {
        var start = Square.Point(0);
        Assert.Equal(30, start.X, 6);
        Assert.Equal(20, start.Y, 6);

        var along = Square.Point(Square.Laps(30));
        Assert.Equal(60, along.X, 6);
        Assert.Equal(20, along.Y, 6);

        var right = Square.Point(Square.Laps(60 + Math.PI * 10 + 30));
        Assert.Equal(110, right.X, 6);
        Assert.Equal(70, right.Y, 6);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.13)]
    [InlineData(0.5)]
    [InlineData(0.77)]
    public void EveryPointLiesOnTheOutline(double laps)
    {
        var p = Square.Point(laps);
        var qx = Math.Max(Math.Abs(p.X - 60) - 50 + 20, 0);
        var qy = Math.Max(Math.Abs(p.Y - 70) - 50 + 20, 0);
        var inner = Math.Min(Math.Max(Math.Abs(p.X - 60) - 50 + 20, Math.Abs(p.Y - 70) - 50 + 20), 0);
        Assert.Equal(0, Math.Sqrt(qx * qx + qy * qy) + inner - 20, 6);
    }

    [Fact]
    public void WholeLapsWrap()
    {
        Assert.Equal(Square.Point(0.25), Square.Point(3.25));
        Assert.Equal(Square.Point(0.75), Square.Point(-0.25));
    }

    [Fact]
    public void OutwardPointsAwayFromTheCenter()
    {
        var top = Square.Outward(Square.Laps(30));
        Assert.Equal(0, top.X, 6);
        Assert.Equal(-1, top.Y, 6);
    }
}
