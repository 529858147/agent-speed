using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace AgentSpeed.History;

/// <summary>
/// A vertical stack whose children spring from their old place to a new one when the order changes (≤ 0.35 s); new
/// children fade in. With <see cref="ReduceMotion"/> moved children crossfade instead.
/// </summary>
internal sealed class SpringStackPanel : StackPanel
{
    public static readonly DependencyProperty ReduceMotionProperty = DependencyProperty.Register(
        nameof(ReduceMotion), typeof(bool), typeof(SpringStackPanel), new PropertyMetadata(false));

    private static readonly Duration MoveDuration = TimeSpan.FromMilliseconds(350);
    private static readonly Duration FadeDuration = TimeSpan.FromMilliseconds(200);
    private static readonly IEasingFunction Spring = Frozen(new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.2 });

    private readonly Dictionary<UIElement, double> _tops = new();

    public bool ReduceMotion
    {
        get => (bool)GetValue(ReduceMotionProperty);
        set => SetValue(ReduceMotionProperty, value);
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        var size = base.ArrangeOverride(arrangeSize);
        var animate = IsLoaded && IsVisible;
        var seen = new HashSet<UIElement>();
        var next = 0.0;
        foreach (UIElement child in InternalChildren)
        {
            seen.Add(child);
            var top = next;
            next += child.Visibility == Visibility.Collapsed ? 0 : child.DesiredSize.Height;
            var known = _tops.TryGetValue(child, out var previous);
            _tops[child] = top;
            if (!animate)
            {
                continue;
            }

            if (!known)
            {
                child.BeginAnimation(OpacityProperty, Fade(0));
            }
            else if (Math.Abs(previous - top) > 0.5)
            {
                if (ReduceMotion)
                {
                    child.BeginAnimation(OpacityProperty, Fade(0.4));
                    continue;
                }

                if (child.RenderTransform is not TranslateTransform shift || shift.IsFrozen)
                {
                    shift = new TranslateTransform();
                    child.RenderTransform = shift;
                }

                var from = previous - top + shift.Y;
                shift.BeginAnimation(TranslateTransform.YProperty,
                    new DoubleAnimation(from, 0, MoveDuration) { EasingFunction = Spring, FillBehavior = FillBehavior.Stop });
            }
        }

        foreach (var gone in _tops.Keys.Where(k => !seen.Contains(k)).ToArray())
        {
            _tops.Remove(gone);
        }

        return size;
    }

    private static DoubleAnimation Fade(double from) => new(from, 1, FadeDuration) { FillBehavior = FillBehavior.Stop };

    private static IEasingFunction Frozen(EasingFunctionBase easing)
    {
        easing.Freeze();
        return easing;
    }
}
