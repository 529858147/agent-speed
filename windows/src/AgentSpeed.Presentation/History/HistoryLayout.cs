namespace AgentSpeed.Presentation.History;

/// <summary>Responsive layout rules of the Usage History window.</summary>
public static class HistoryLayout
{
    /// <summary>Content narrower than this shows the summary cards in two rows of two.</summary>
    public const double FourCardsMinWidth = 820;

    public static int CardColumns(double contentWidth) =>
        double.IsFinite(contentWidth) && contentWidth < FourCardsMinWidth ? 2 : 4;

    /// <summary>
    /// Places a tooltip card beside a slot: to the right when it fits, else to the left, else wherever it overlaps the
    /// least; vertically centred on <paramref name="anchorY"/>. The card always stays inside the bounds.
    /// </summary>
    public static (double X, double Y) TooltipPosition(double slotLeft, double slotRight, double anchorY, double cardWidth,
        double cardHeight, double boundsWidth, double boundsHeight, double gap = 12)
    {
        var maxX = Math.Max(0, boundsWidth - cardWidth);
        var maxY = Math.Max(0, boundsHeight - cardHeight);
        double x;
        if (slotRight + gap + cardWidth <= boundsWidth)
        {
            x = slotRight + gap;
        }
        else if (slotLeft - gap - cardWidth >= 0)
        {
            x = slotLeft - gap - cardWidth;
        }
        else
        {
            var center = (slotLeft + slotRight) / 2;
            x = center < boundsWidth / 2 ? maxX : 0;
        }

        var y = anchorY - cardHeight / 2;
        return (Math.Clamp(x, 0, maxX), Math.Clamp(double.IsFinite(y) ? y : 0, 0, maxY));
    }

    /// <summary>
    /// Rows to draw for the visible part of a list that starts <paramref name="visibleTop"/> pixels above the viewport
    /// top, plus <paramref name="overscan"/> rows on each side. Empty (First &gt; Last) when nothing is visible.
    /// </summary>
    public static (int First, int Last) RowWindow(double visibleTop, double visibleHeight, double rowHeight, int rowCount, int overscan)
    {
        if (rowCount <= 0 || rowHeight <= 0 || !double.IsFinite(visibleTop) || !double.IsFinite(visibleHeight))
        {
            return (0, -1);
        }

        var first = (int)Math.Floor(visibleTop / rowHeight) - overscan;
        var last = (int)Math.Ceiling((visibleTop + Math.Max(0, visibleHeight)) / rowHeight) + overscan;
        first = Math.Clamp(first, 0, rowCount - 1);
        last = Math.Clamp(last, 0, rowCount - 1);
        return first > last ? (0, -1) : (first, last);
    }

    /// <summary>Scroll offset that brings a row to the middle of the viewport, within the scrollable range.</summary>
    public static double CenteredOffset(double rowTop, double rowHeight, double viewportHeight, double scrollableHeight)
    {
        var target = rowTop + rowHeight / 2 - viewportHeight / 2;
        return Math.Clamp(double.IsFinite(target) ? target : 0, 0, Math.Max(0, scrollableHeight));
    }
}
