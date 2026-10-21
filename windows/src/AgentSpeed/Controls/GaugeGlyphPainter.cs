using System.Windows;
using System.Windows.Media;
using AgentSpeed.Presentation.Colors;
using AgentSpeed.Presentation.Gauge;
using AgentSpeed.Theme;

namespace AgentSpeed.Controls;

/// <summary>The small 270° ring gauge used by the taskbar widget, the tray icon and empty states (DESIGN section 3).</summary>
internal static class GaugeGlyphPainter
{
    public static Geometry Arc(Point center, double radius, double startDegrees, double sweepDegrees)
    {
        sweepDegrees = Math.Clamp(sweepDegrees, 0, 359.9);
        var start = Polar(center, radius, startDegrees);
        var end = Polar(center, radius, startDegrees + sweepDegrees);
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(start, isFilled: false, isClosed: false);
            context.ArcTo(end, new Size(radius, radius), 0, sweepDegrees > 180, SweepDirection.Clockwise, isStroked: true, isSmoothJoin: false);
        }

        geometry.Freeze();
        return geometry;
    }

    public static Point Polar(Point center, double radius, double degrees)
    {
        var v = GaugeLayout.Polar(radius, degrees);
        return new Point(center.X + v.X, center.Y + v.Y);
    }

    /// <param name="labelColor">Text color of the surface; the track uses it at 25 %.</param>
    /// <param name="monochrome">Draw the fill in the label color instead of the heat color.</param>
    public static void Draw(
        DrawingContext dc, Rect bounds, double heat, Color labelColor, bool monochrome,
        double fillOpacity = 1, double glow = 0, double? strokeThickness = null, double trackAlpha = 0.25)
    {
        var size = Math.Min(bounds.Width, bounds.Height);
        if (size <= 0)
        {
            return;
        }

        var stroke = strokeThickness ?? Math.Max(1.5, size / 8);
        var radius = (size - stroke) / 2;
        var center = new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2 + radius * 0.1464);

        var trackColor = Color.FromArgb((byte)Math.Round(labelColor.A * trackAlpha), labelColor.R, labelColor.G, labelColor.B);
        var trackPen = MediaColors.FrozenPen(MediaColors.Frozen(trackColor), stroke, PenLineCap.Round);
        dc.DrawGeometry(null, trackPen, Arc(center, radius, GaugeLayout.StartDegrees, GaugeLayout.SweepDegrees));

        heat = Math.Clamp(heat, 0, 1);
        if (heat < 0.004)
        {
            return;
        }

        var fill = monochrome ? labelColor : HeatPalette.Sample(heat).ToColor();
        var arc = Arc(center, radius, GaugeLayout.StartDegrees, GaugeLayout.SweepDegrees * heat);

        if (glow > 0.01 && !monochrome)
        {
            for (var i = 3; i >= 1; i--)
            {
                var alpha = glow * fillOpacity * (0.10 + 0.05 * (3 - i));
                var pen = MediaColors.FrozenPen(MediaColors.Frozen(Color.FromArgb((byte)Math.Round(255 * alpha), fill.R, fill.G, fill.B)),
                    stroke + i * stroke * 0.9, PenLineCap.Round);
                dc.DrawGeometry(null, pen, arc);
            }
        }

        var fillColor = Color.FromArgb((byte)Math.Round(fill.A * Math.Clamp(fillOpacity, 0, 1)), fill.R, fill.G, fill.B);
        dc.DrawGeometry(null, MediaColors.FrozenPen(MediaColors.Frozen(fillColor), stroke, PenLineCap.Round), arc);
    }
}
