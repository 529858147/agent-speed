using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using AgentSpeed.Modes;

namespace AgentSpeed.Dashboard;

/// <summary>
/// The agent row's looping mark (DESIGN 4): an amber Segoe Fluent Sync glyph, 11 DIP, 4 DIP before the project name while
/// the agent goes in circles (METRICS 6.10). Fades in 0.25 s and out 0.6 s; Reduce Motion shows and hides it at once.
/// </summary>
internal sealed class LoopingMark : TextBlock
{
    public const string Glyph = "";
    public const double Size = 11;
    public const double Gap = 4;

    public static readonly DependencyProperty IsLoopingProperty = DependencyProperty.Register(
        nameof(IsLooping), typeof(bool), typeof(LoopingMark), new PropertyMetadata(false, (d, _) => ((LoopingMark)d).Show(animate: true)));

    public static readonly DependencyProperty AppearanceProperty = DependencyProperty.Register(
        nameof(Appearance), typeof(CapsuleAppearance), typeof(LoopingMark),
        new PropertyMetadata(default(CapsuleAppearance), (d, _) => ((LoopingMark)d).Paint()));

    private static readonly Duration FadeIn = new(TimeSpan.FromSeconds(0.25));
    private static readonly Duration FadeOut = new(TimeSpan.FromSeconds(0.6));

    public LoopingMark()
    {
        Text = Glyph;
        FontSize = Size;
        Margin = new Thickness(0, 0, Gap, 0);
        VerticalAlignment = VerticalAlignment.Center;
        SetResourceReference(FontFamilyProperty, "AppSymbolFontFamily");
        Paint();
        Show(animate: false);
    }

    public bool IsLooping
    {
        get => (bool)GetValue(IsLoopingProperty);
        set => SetValue(IsLoopingProperty, value);
    }

    public CapsuleAppearance Appearance
    {
        get => (CapsuleAppearance)GetValue(AppearanceProperty);
        set => SetValue(AppearanceProperty, value);
    }

    /// <summary>The row's label says <c>work.looping</c>; the glyph itself is decoration.</summary>
    protected override AutomationPeer OnCreateAutomationPeer() => new DecorativePeer(this);

    private void Paint() => Foreground = Appearance.Brushes.Bolt;

    private void Show(bool animate)
    {
        var shown = IsLooping;
        var from = Opacity;
        var to = shown ? 1.0 : 0.0;
        BeginAnimation(OpacityProperty, null);
        Opacity = to;
        if (shown)
        {
            Visibility = Visibility.Visible;
        }

        if (!animate || Appearance.ReduceMotion || !IsLoaded || Math.Abs(from - to) < 0.001)
        {
            Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
            return;
        }

        var fade = new DoubleAnimation(from, to, shown ? FadeIn : FadeOut)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop,
        };
        if (!shown)
        {
            fade.Completed += (_, _) =>
            {
                if (!IsLooping)
                {
                    Visibility = Visibility.Collapsed;
                }
            };
        }

        BeginAnimation(OpacityProperty, fade);
    }

    private sealed class DecorativePeer(LoopingMark owner) : TextBlockAutomationPeer(owner)
    {
        protected override bool IsControlElementCore() => false;

        protected override bool IsContentElementCore() => false;
    }
}
