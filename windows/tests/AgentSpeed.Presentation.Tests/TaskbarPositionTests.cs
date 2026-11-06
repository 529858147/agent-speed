using AgentSpeed.Presentation.Layout;

namespace AgentSpeed.Presentation.Tests;

/// <summary>DESIGN 3, Position on the taskbar: the offset from the default spot, its limits, DPI and the stored value.</summary>
public class TaskbarPositionTests
{
    private static readonly RectD Bar = new(0, 0, 1920, 48);
    private static readonly RectD Tray = new(1700, 0, 220, 48);
    private static readonly RectD VerticalBar = new(0, 0, 64, 1080);
    private static readonly RectD VerticalTray = new(0, 900, 64, 180);

    [Fact]
    public void AnOffsetMovesTheWidgetTowardTheTaskbarStart()
    {
        Assert.Equal(new RectD(1406, 0, 90, 48), PlacementMath.TaskbarWidget(Bar, Tray, 90, 4, vertical: false, offset: 200));
        Assert.Equal(new RectD(0, 556, 64, 40), PlacementMath.TaskbarWidget(VerticalBar, VerticalTray, 40, 4, vertical: true, offset: 300));
    }

    [Fact]
    public void ZeroOffsetIsTheDefaultSpot()
    {
        Assert.Equal(PlacementMath.TaskbarWidget(Bar, Tray, 90, 4, vertical: false),
            PlacementMath.TaskbarWidget(Bar, Tray, 90, 4, vertical: false, offset: 0));
        Assert.Equal(new RectD(1606, 0, 90, 48), PlacementMath.TaskbarWidget(Bar, Tray, 90, 4, vertical: false, offset: 0));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-5000)]
    [InlineData(double.NaN)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidOffsetsTakeTheDefaultSpot(double offset)
    {
        Assert.Equal(new RectD(1606, 0, 90, 48), PlacementMath.TaskbarWidget(Bar, Tray, 90, 4, vertical: false, offset));
        Assert.Equal(0, PlacementMath.TaskbarWidgetOffset(Bar, Tray, 90, 4, vertical: false, offset));
    }

    [Theory]
    [InlineData(200.4, 1406)]
    [InlineData(200.5, 1405)]
    [InlineData(0.49, 1606)]
    public void OffsetsLandOnWholePixels(double offset, double left)
    {
        var widget = PlacementMath.TaskbarWidget(Bar, Tray, 90, 4, vertical: false, offset);
        Assert.Equal(left, widget!.Value.X);
        Assert.Equal(90, widget.Value.Width);
    }

    [Fact]
    public void TheFarEdgeStopsAQuarterOfTheTaskbarFromItsStart()
    {
        // Limit 1920 / 4 = 480: the trailing edge 1696 minus 90 px of widget leaves 1126 px to travel.
        Assert.Equal(1126, PlacementMath.TaskbarWidgetOffset(Bar, Tray, 90, 4, vertical: false, 5000));
        Assert.Equal(new RectD(480, 0, 90, 48), PlacementMath.TaskbarWidget(Bar, Tray, 90, 4, vertical: false, offset: 5000));
        Assert.Equal(new RectD(480, 0, 90, 48), PlacementMath.TaskbarWidget(Bar, Tray, 90, 4, vertical: false, offset: 1126));
        Assert.Equal(new RectD(481, 0, 90, 48), PlacementMath.TaskbarWidget(Bar, Tray, 90, 4, vertical: false, offset: 1125));

        // Vertical: limit 270, trailing edge 896.
        Assert.Equal(586, PlacementMath.TaskbarWidgetOffset(VerticalBar, VerticalTray, 40, 4, vertical: true, 5000));
        Assert.Equal(new RectD(0, 270, 64, 40), PlacementMath.TaskbarWidget(VerticalBar, VerticalTray, 40, 4, vertical: true, offset: 5000));
    }

    [Fact]
    public void TheFarEdgeNeverPassesTheReservedStart()
    {
        var bars = new[] { new RectD(0, 0, 1366, 40), new RectD(-2560, 0, 2560, 48), new RectD(1920, 1392, 3841, 48), new RectD(0, 0, 1922, 72) };
        foreach (var bar in bars)
        {
            var tray = new RectD(bar.Right - 300, bar.Y, 300, bar.Height);
            var limit = bar.X + bar.Width * TaskbarPosition.ReservedStart;
            for (var length = 30; length <= 400; length += 37)
            {
                for (var offset = 0.0; offset <= 4000; offset += 63.7)
                {
                    var widget = PlacementMath.TaskbarWidget(bar, tray, length, 4, vertical: false, offset);
                    Assert.NotNull(widget);
                    var w = widget.Value;
                    Assert.True(w.X >= Math.Floor(limit), $"{bar} length {length} offset {offset}: left {w.X} < {limit}");
                    Assert.True(w.Right <= tray.X - 4, $"{bar} length {length} offset {offset}: right {w.Right}");
                    Assert.Equal(length, w.Width);
                    Assert.Equal(bar.Height, w.Height);
                }
            }
        }
    }

    [Fact]
    public void TheWidgetKeepsItsDistanceFromAGrowingOrShrinkingTray()
    {
        var before = PlacementMath.TaskbarWidget(Bar, Tray, 90, 4, vertical: false, offset: 250)!.Value;
        var grown = PlacementMath.TaskbarWidget(Bar, new RectD(1580, 0, 340, 48), 90, 4, vertical: false, offset: 250)!.Value;
        var shrunk = PlacementMath.TaskbarWidget(Bar, new RectD(1760, 0, 160, 48), 90, 4, vertical: false, offset: 250)!.Value;
        Assert.Equal(Tray.X - before.Right, 1580 - grown.Right);
        Assert.Equal(Tray.X - before.Right, 1760 - shrunk.Right);
        Assert.Equal(254, Tray.X - before.Right);
    }

    [Fact]
    public void AWiderWidgetGrowsAwayFromTheTray()
    {
        var narrow = PlacementMath.TaskbarWidget(Bar, Tray, 90, 4, vertical: false, offset: 300)!.Value;
        var wide = PlacementMath.TaskbarWidget(Bar, Tray, 240, 4, vertical: false, offset: 300)!.Value;
        Assert.Equal(narrow.Right, wide.Right);
        Assert.Equal(narrow.X - 150, wide.X);
    }

    [Fact]
    public void AnOffsetKeepsAShorterTraysHeight()
    {
        var widget = PlacementMath.TaskbarWidget(Bar, new RectD(1700, 4, 220, 40), 90, 4, vertical: false, offset: 100);
        Assert.Equal(new RectD(1506, 4, 90, 40), widget);
    }

    [Fact]
    public void AStoredOffsetThatNoLongerFitsIsLimitedNotDropped()
    {
        // Dragged far on a wide taskbar, then shown on a narrow one: as far as it fits.
        var narrow = new RectD(0, 0, 1366, 40);
        var tray = new RectD(1150, 0, 216, 40);
        var widget = PlacementMath.TaskbarWidget(narrow, tray, 120, 4, vertical: false, offset: 1500)!.Value;
        Assert.Equal(Math.Ceiling(1366 * 0.25), widget.X);
        Assert.Equal(120, widget.Width);
    }

    [Fact]
    public void NoRoomEvenAtTheDefaultSpotStaysNull()
    {
        Assert.Null(PlacementMath.TaskbarWidgetOffset(Bar, new RectD(400, 0, 1520, 48), 90, 4, vertical: false, 0));
        Assert.Null(PlacementMath.TaskbarWidget(Bar, new RectD(400, 0, 1520, 48), 90, 4, vertical: false, offset: 300));
        Assert.Null(PlacementMath.TaskbarWidgetOffset(default, Tray, 90, 4, vertical: false, 0));
        Assert.Null(PlacementMath.TaskbarWidgetOffset(Bar, Tray, 0, 4, vertical: false, 0));
        Assert.Null(PlacementMath.TaskbarWidgetOffset(Bar, Tray, double.NaN, 4, vertical: false, 0));
        Assert.Null(PlacementMath.TaskbarWidgetOffset(Bar, Tray, double.PositiveInfinity, 4, vertical: false, 0));
    }

    [Fact]
    public void WithoutATrayTheOffsetCountsFromTheTaskbarEnd()
    {
        Assert.Equal(new RectD(1630, 0, 90, 48), PlacementMath.TaskbarWidget(Bar, default, 90, 4, vertical: false, offset: 200));
        Assert.Equal(new RectD(0, 840, 64, 40), PlacementMath.TaskbarWidget(VerticalBar, default, 40, 4, vertical: true, offset: 200));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-3, 0)]
    [InlineData(double.NaN, 0)]
    [InlineData(double.PositiveInfinity, 0)]
    [InlineData(double.NegativeInfinity, 0)]
    [InlineData(12.3456, 12.35)]
    [InlineData(12.344, 12.34)]
    [InlineData(0.004, 0)]
    [InlineData(250, 250)]
    [InlineData(10_000, 10_000)]
    [InlineData(25_000, 10_000)]
    public void NormalizeKeepsAPositiveDistanceInRange(double input, double expected)
    {
        Assert.Equal(expected, TaskbarPosition.Normalize(input));
    }

    [Theory]
    [InlineData(100, 1, 100)]
    [InlineData(100, 1.5, 150)]
    [InlineData(66.67, 1.5, 100)]
    [InlineData(41.67, 2.4, 100)]
    [InlineData(100, 0, 100)]
    [InlineData(100, -2, 100)]
    [InlineData(100, double.NaN, 100)]
    [InlineData(-100, 2, 0)]
    public void ToPixelsScalesAndRounds(double offset, double scale, double pixels)
    {
        Assert.Equal(pixels, TaskbarPosition.ToPixels(offset, scale));
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(1.75)]
    [InlineData(2.0)]
    [InlineData(2.25)]
    [InlineData(2.4)]
    [InlineData(2.5)]
    [InlineData(3.0)]
    [InlineData(3.5)]
    [InlineData(4.0)]
    [InlineData(5.0)]
    public void ADroppedPositionComesBackOnTheSamePixel(double scale)
    {
        for (var pixels = 0; pixels <= 6000; pixels++)
        {
            var stored = TaskbarPosition.ToDips(pixels, scale);
            Assert.Equal(stored, TaskbarPosition.Normalize(stored));
            Assert.Equal(pixels, TaskbarPosition.ToPixels(stored, scale));
        }
    }

    [Fact]
    public void TheStoredDistanceLooksTheSameAtAnotherDpi()
    {
        var stored = TaskbarPosition.ToDips(200, 1.0);
        Assert.Equal(200, stored);
        Assert.Equal(300, TaskbarPosition.ToPixels(stored, 1.5));
        Assert.Equal(480, TaskbarPosition.ToPixels(stored, 2.4));
        Assert.Equal(0, TaskbarPosition.ToDips(-40, 1.5));
        Assert.Equal(TaskbarPosition.MaxOffset, TaskbarPosition.ToDips(1e9, 1));
    }

    [Fact]
    public void ADragCommitsWhereTheWidgetWasDroppedAndPlacesItThereAgain()
    {
        const double scale = 1.5;
        var gap = Math.Round(TaskbarPosition.Gap * scale);
        var bar = new RectD(0, 2088, 3840, 72);
        var tray = new RectD(3400, 2088, 440, 72);
        const int length = 180;

        var stored = 100.0;
        var start = PlacementMath.TaskbarWidgetOffset(bar, tray, length, gap, vertical: false, TaskbarPosition.ToPixels(stored, scale))!.Value;
        Assert.Equal(150, start);

        var drag = new TaskbarDrag(new PointD(3200, 2120), start, vertical: false, threshold: 6);
        Assert.True(drag.Move(new PointD(2777, 2140)));
        var during = PlacementMath.TaskbarWidget(bar, tray, length, gap, vertical: false, drag.Requested!.Value)!.Value;

        var dropped = PlacementMath.TaskbarWidgetOffset(bar, tray, length, gap, vertical: false, drag.Requested.Value)!.Value;
        stored = TaskbarPosition.ToDips(dropped, scale);
        Assert.Equal(382, stored);
        var after = PlacementMath.TaskbarWidget(bar, tray, length, gap, vertical: false, TaskbarPosition.ToPixels(stored, scale))!.Value;
        Assert.Equal(during, after);
        Assert.Equal(new RectD(2641, 2088, 180, 72), after);
    }

    [Fact]
    public void ADragPastTheLimitStoresTheLimit()
    {
        var drag = new TaskbarDrag(new PointD(1650, 24), 0, vertical: false, threshold: 4);
        drag.Move(new PointD(-500, 24));
        var dropped = PlacementMath.TaskbarWidgetOffset(Bar, Tray, 90, 4, vertical: false, drag.Requested!.Value);
        Assert.Equal(1126, dropped);
        Assert.Equal(1126, TaskbarPosition.ToDips(dropped!.Value, 1));
    }
}
