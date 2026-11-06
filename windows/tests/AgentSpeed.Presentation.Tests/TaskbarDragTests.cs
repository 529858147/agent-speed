using AgentSpeed.Presentation.Layout;

namespace AgentSpeed.Presentation.Tests;

/// <summary>DESIGN 3, Position on the taskbar: a press is a click until it moves along the taskbar past the drag distance.</summary>
public class TaskbarDragTests
{
    private static readonly PointD Press = new(1000, 20);
    private static readonly RectD Widget = new(950, 0, 100, 48);

    [Fact]
    public void APressIsAClickUntilItMovesPastTheDragDistance()
    {
        var drag = new TaskbarDrag(Press, 30, vertical: false, threshold: 4);
        Assert.False(drag.Move(new PointD(996, 20)));
        Assert.False(drag.Move(new PointD(1004, 22)));
        Assert.False(drag.IsDragging);
        Assert.Null(drag.Requested);
        Assert.True(drag.IsClick(new PointD(1004, 22), Widget));
    }

    [Fact]
    public void MovementAcrossTheTaskbarNeverStartsADrag()
    {
        var drag = new TaskbarDrag(Press, 30, vertical: false, threshold: 4);
        Assert.False(drag.Move(new PointD(1002, 400)));
        Assert.False(drag.Move(new PointD(998, -300)));
        Assert.False(drag.IsDragging);
        Assert.False(drag.IsClick(new PointD(998, -300), Widget));
        Assert.True(drag.IsClick(new PointD(998, 30), Widget));
    }

    [Fact]
    public void PastTheDistanceTheWidgetKeepsTheGrabbedPointUnderThePointer()
    {
        var drag = new TaskbarDrag(Press, 30, vertical: false, threshold: 4);
        Assert.True(drag.Move(new PointD(995, 20)));
        Assert.Equal(35, drag.Requested);
        Assert.True(drag.Move(new PointD(999, 20)));
        Assert.Equal(31, drag.Requested);
        Assert.True(drag.Move(new PointD(1001, 60)));
        Assert.Equal(29, drag.Requested);
        Assert.True(drag.Move(new PointD(700, 24)));
        Assert.Equal(330, drag.Requested);
        Assert.True(drag.IsDragging);
    }

    [Fact]
    public void ADragNeverAsksForANegativeOffset()
    {
        var drag = new TaskbarDrag(Press, 30, vertical: false, threshold: 4);
        drag.Move(new PointD(1300, 20));
        Assert.Equal(0, drag.Requested);
        drag.Move(new PointD(980, 20));
        Assert.Equal(50, drag.Requested);
    }

    [Fact]
    public void AVerticalTaskbarDragsUpAndDown()
    {
        var drag = new TaskbarDrag(new PointD(30, 900), 0, vertical: true, threshold: 4);
        Assert.False(drag.Move(new PointD(400, 902)));
        Assert.True(drag.Move(new PointD(32, 880)));
        Assert.Equal(20, drag.Requested);
        drag.Move(new PointD(-200, 700));
        Assert.Equal(200, drag.Requested);
        drag.Move(new PointD(30, 950));
        Assert.Equal(0, drag.Requested);
    }

    [Fact]
    public void AReleaseAfterADragIsNeverAClick()
    {
        var drag = new TaskbarDrag(Press, 0, vertical: false, threshold: 4);
        drag.Move(new PointD(990, 20));
        drag.Move(new PointD(1000, 20));
        Assert.True(drag.IsDragging);
        Assert.False(drag.IsClick(Press, Widget));
    }

    [Fact]
    public void AReleaseOutsideTheWidgetIsNotAClick()
    {
        var drag = new TaskbarDrag(Press, 0, vertical: false, threshold: 4);
        Assert.False(drag.IsClick(new PointD(1000, 60), Widget));
        Assert.False(drag.IsClick(new PointD(1050, 20), Widget));
        Assert.True(drag.IsClick(new PointD(950, 0), Widget));
    }

    [Fact]
    public void InvalidInputsAreSafe()
    {
        var drag = new TaskbarDrag(Press, double.NaN, vertical: false, threshold: double.NaN);
        Assert.Equal(0, drag.StartOffset);
        Assert.False(drag.Move(new PointD(double.NaN, 20)));
        Assert.True(drag.Move(new PointD(999, 20)));
        Assert.Equal(1, drag.Requested);
        Assert.True(drag.Move(new PointD(double.PositiveInfinity, 20)));
        Assert.Equal(1, drag.Requested);

        Assert.Equal(0, new TaskbarDrag(Press, -50, vertical: false, threshold: -3).StartOffset);
    }
}
