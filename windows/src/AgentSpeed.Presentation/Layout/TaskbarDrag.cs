namespace AgentSpeed.Presentation.Layout;

/// <summary>
/// One left-button press on the taskbar widget (DESIGN 3, Position on the taskbar), in physical pixels. It stays a click
/// until the pointer has moved along the taskbar by more than the system drag distance; from then on it is a drag whose
/// requested offset keeps the grabbed point under the pointer. Movement across the taskbar never starts a drag.
/// </summary>
public sealed class TaskbarDrag
{
    private readonly PointD _press;
    private readonly bool _vertical;
    private readonly double _threshold;

    /// <param name="press">Where the button went down (screen pixels).</param>
    /// <param name="startOffset">The offset the widget had at the press, as placed (already within the taskbar's limits).</param>
    /// <param name="vertical">The taskbar is vertical: the widget moves up and down.</param>
    /// <param name="threshold">The system drag distance along the taskbar (<c>SM_CXDRAG</c> or <c>SM_CYDRAG</c>).</param>
    public TaskbarDrag(PointD press, double startOffset, bool vertical, double threshold)
    {
        _press = press;
        _vertical = vertical;
        _threshold = double.IsFinite(threshold) ? Math.Max(0, threshold) : 0;
        StartOffset = double.IsFinite(startOffset) ? Math.Max(0, startOffset) : 0;
    }

    public double StartOffset { get; }

    public bool IsDragging { get; private set; }

    /// <summary>The offset the pointer asks for (not yet limited to the taskbar); null while the press is still a click.</summary>
    public double? Requested { get; private set; }

    /// <summary>Follows the pointer; true while the press is a drag.</summary>
    public bool Move(PointD cursor)
    {
        // Toward the taskbar's start (left, or up when vertical) is a positive offset.
        var along = _vertical ? _press.Y - cursor.Y : _press.X - cursor.X;
        if (!double.IsFinite(along) || (!IsDragging && Math.Abs(along) <= _threshold))
        {
            return IsDragging;
        }

        IsDragging = true;
        Requested = Math.Max(0, StartOffset + along);
        return true;
    }

    /// <summary>A release without a drag over the widget is a click.</summary>
    public bool IsClick(PointD release, RectD widget) => !IsDragging && widget.Contains(release);
}
