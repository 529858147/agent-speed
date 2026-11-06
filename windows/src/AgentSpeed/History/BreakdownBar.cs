using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace AgentSpeed.History;

/// <summary>
/// A horizontal bar of a day's breakdown on a faint track. It grows to a new length with an ease-out (≤ 0.45 s) and
/// jumps with Reduce Motion; today's bars are hatched like today's bar in the daily chart.
/// </summary>
internal sealed class BreakdownBar : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(BreakdownBar), new FrameworkPropertyMetadata(0.0, OnValueChanged));

    public static readonly DependencyProperty ColorProperty = DependencyProperty.Register(
        nameof(Color), typeof(Color), typeof(BreakdownBar), new FrameworkPropertyMetadata(Colors.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>A fill in place of <see cref="Color"/>, for example a gradient; null uses the color.</summary>
    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(BreakdownBar), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Changes of length spring in 0.35 s instead of easing out in 0.45 s.</summary>
    public static readonly DependencyProperty SpringProperty = DependencyProperty.Register(
        nameof(Spring), typeof(bool), typeof(BreakdownBar), new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty HatchedProperty = DependencyProperty.Register(
        nameof(Hatched), typeof(bool), typeof(BreakdownBar), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackProperty = DependencyProperty.Register(
        nameof(Track), typeof(Brush), typeof(BreakdownBar), new FrameworkPropertyMetadata(Brushes.LightGray, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Set on an ancestor; bars below it stop animating.</summary>
    public static readonly DependencyProperty ReduceMotionProperty = DependencyProperty.RegisterAttached(
        "ReduceMotion", typeof(bool), typeof(BreakdownBar), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

    /// <summary>Set on an ancestor; bars below it use the system highlight color.</summary>
    public static readonly DependencyProperty HighContrastProperty = DependencyProperty.RegisterAttached(
        "HighContrast", typeof(bool), typeof(BreakdownBar),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly DependencyProperty ShownProperty = DependencyProperty.Register(
        "Shown", typeof(double), typeof(BreakdownBar), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly Duration GrowDuration = TimeSpan.FromMilliseconds(450);
    private static readonly Duration SpringDuration = TimeSpan.FromMilliseconds(350);
    private static readonly IEasingFunction GrowEasing = Frozen(new CubicEase { EasingMode = EasingMode.EaseOut });
    private static readonly IEasingFunction SpringEasing = Frozen(new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.25 });

    private bool _entered;

    public BreakdownBar()
    {
        Loaded += (_, _) =>
        {
            if (!_entered)
            {
                _entered = true;
                Animate(from: 0);
            }
        };
    }

    /// <summary>The bar's length as a fraction (0..1) of the track.</summary>
    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public Color Color
    {
        get => (Color)GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    public Brush? Fill
    {
        get => (Brush?)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public bool Spring
    {
        get => (bool)GetValue(SpringProperty);
        set => SetValue(SpringProperty, value);
    }

    public bool Hatched
    {
        get => (bool)GetValue(HatchedProperty);
        set => SetValue(HatchedProperty, value);
    }

    public Brush Track
    {
        get => (Brush)GetValue(TrackProperty);
        set => SetValue(TrackProperty, value);
    }

    public static bool GetReduceMotion(DependencyObject element) => (bool)element.GetValue(ReduceMotionProperty);

    public static void SetReduceMotion(DependencyObject element, bool value) => element.SetValue(ReduceMotionProperty, value);

    public static bool GetHighContrast(DependencyObject element) => (bool)element.GetValue(HighContrastProperty);

    public static void SetHighContrast(DependencyObject element, bool value) => element.SetValue(HighContrastProperty, value);

    protected override Size MeasureOverride(Size availableSize) => new(0, 10);

    protected override void OnRender(DrawingContext dc)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var radius = Math.Min(3, height / 2);
        dc.DrawRoundedRectangle(Track, null, new Rect(0, 0, width, height), radius, radius);
        var shown = (double)GetValue(ShownProperty);
        var length = double.IsFinite(shown) ? Math.Clamp(shown, 0, 1) * width : 0;
        if (length > 0.25)
        {
            var highContrast = GetHighContrast(this);
            var brush = Fill is { } fill && !highContrast
                ? fill
                : BarBrushes.For(highContrast ? SystemColors.HighlightColor : Color, gradient: false, Hatched);
            dc.DrawRoundedRectangle(brush, null, new Rect(0, 0, Math.Max(length, Math.Min(width, 2 * radius)), height), radius, radius);
        }
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((BreakdownBar)d).Animate(from: null);

    /// <summary>Moves the drawn length to <see cref="Value"/>, from <paramref name="from"/> or from where it is now.</summary>
    private void Animate(double? from)
    {
        var target = double.IsFinite(Value) ? Math.Clamp(Value, 0, 1) : 0;
        if (!IsLoaded || GetReduceMotion(this))
        {
            BeginAnimation(ShownProperty, null);
            SetValue(ShownProperty, target);
            return;
        }

        var animation = new DoubleAnimation(target, Spring ? SpringDuration : GrowDuration) { EasingFunction = Spring ? SpringEasing : GrowEasing };
        if (from is { } start)
        {
            animation.From = start;
        }

        animation.Freeze();
        BeginAnimation(ShownProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private static IEasingFunction Frozen(EasingFunctionBase easing)
    {
        easing.Freeze();
        return easing;
    }
}
