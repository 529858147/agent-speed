using System.Windows;
using System.Windows.Media;

namespace AgentSpeed.Controls;

/// <summary>Muted ring gauge for empty states, drawn in the inherited foreground color.</summary>
internal sealed class GaugeGlyph : FrameworkElement
{
    public static readonly DependencyProperty HeatProperty = DependencyProperty.Register(
        nameof(Heat), typeof(double), typeof(GaugeGlyph), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ForegroundProperty = System.Windows.Documents.TextElement.ForegroundProperty.AddOwner(
        typeof(GaugeGlyph), new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.Inherits));

    public double Heat
    {
        get => (double)GetValue(HeatProperty);
        set => SetValue(HeatProperty, value);
    }

    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var color = Foreground is SolidColorBrush solid ? solid.Color : Colors.Gray;
        GaugeGlyphPainter.Draw(drawingContext, new Rect(0, 0, ActualWidth, ActualHeight), Heat, color, monochrome: Heat <= 0,
            strokeThickness: Math.Max(1.5, Math.Min(ActualWidth, ActualHeight) / 9), trackAlpha: 0.45);
    }
}
