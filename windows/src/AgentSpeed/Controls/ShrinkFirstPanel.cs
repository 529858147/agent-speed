using System.Windows;
using System.Windows.Controls;

namespace AgentSpeed.Controls;

/// <summary>
/// One line of two children side by side: the second keeps its natural width right after the first, and the first gets
/// the room left (so it truncates first). An agent's detail and its traffic suffix (DESIGN 4, Room).
/// </summary>
internal sealed class ShrinkFirstPanel : Panel
{
    protected override Size MeasureOverride(Size availableSize)
    {
        var children = InternalChildren;
        if (children.Count != 2)
        {
            return MeasureAll(availableSize);
        }

        var (first, last) = (children[0], children[1]);
        last.Measure(new Size(double.PositiveInfinity, availableSize.Height));
        first.Measure(new Size(Math.Max(0, availableSize.Width - last.DesiredSize.Width), availableSize.Height));
        return new Size(first.DesiredSize.Width + last.DesiredSize.Width, Math.Max(first.DesiredSize.Height, last.DesiredSize.Height));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var x = 0.0;
        foreach (UIElement child in InternalChildren)
        {
            var width = Math.Min(child.DesiredSize.Width, Math.Max(0, finalSize.Width - x));
            child.Arrange(new Rect(x, 0, width, finalSize.Height));
            x += width;
        }

        return finalSize;
    }

    private Size MeasureAll(Size availableSize)
    {
        var size = default(Size);
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(availableSize);
            size = new Size(Math.Max(size.Width, child.DesiredSize.Width), Math.Max(size.Height, child.DesiredSize.Height));
        }

        return size;
    }
}
