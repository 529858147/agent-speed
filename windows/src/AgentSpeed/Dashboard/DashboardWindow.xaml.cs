using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using AgentSpeed.Interop;
using AgentSpeed.Orb;
using AgentSpeed.Presentation.Layout;

namespace AgentSpeed.Dashboard;

/// <summary>Flyout above the taskbar near the notification area (DESIGN section 4).</summary>
internal sealed partial class DashboardWindow : Window
{
    private const double FlyoutWidth = 368;
    private const double ScreenMargin = 12;

    private nint _hwnd;
    private TaskbarEdge _edge = TaskbarEdge.Bottom;
    private RECT _work;
    private double _scale = 1;
    private bool _placing;
    private bool _suppressRange;

    public DashboardWindow(DashboardViewModel viewModel, Func<double> clock)
    {
        InitializeComponent();
        DataContext = viewModel;
        Hero = new OrbView(clock)
        {
            Margin = new Thickness(-28),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        };
        HeroHost.Children.Add(Hero);
        SourceInitialized += OnSourceInitialized;
        Deactivated += (_, _) => Dismiss();
        SizeChanged += (_, _) => Reanchor();
        DpiChanged += (_, e) =>
        {
            _scale = e.NewDpi.DpiScaleX;
            Dispatcher.BeginInvoke(() => Reanchor());
        };
    }

    public event EventHandler? SettingsRequested;

    public event EventHandler? SourcesRequested;

    public event EventHandler? QuitRequested;

    public event EventHandler<bool>? OrbToggled;

    public event EventHandler<int>? RangeChanged;

    public OrbView Hero { get; }

    public long LastHiddenTick { get; private set; }

    public long LastShownTick { get; private set; }

    public bool IsShown => IsVisible;

    public void SetRange(int seconds)
    {
        _suppressRange = true;
        try
        {
            (seconds switch { 60 => Range60, 300 => Range300, _ => Range120 }).IsChecked = true;
        }
        finally
        {
            _suppressRange = false;
        }
    }

    public void SetOrbVisible(bool visible) => OrbToggle.IsChecked = visible;

    public void SetPreviewing(bool previewing) =>
        PreviewBadge.Visibility = previewing ? Visibility.Visible : Visibility.Collapsed;

    public LiveChart LiveChart => Chart;

    /// <summary>Shows the flyout on the monitor containing <paramref name="anchor"/> (physical pixels).</summary>
    public void ShowNear(POINT anchor, bool reduceMotion)
    {
        var monitor = Monitors.FromPoint(anchor) ?? Monitors.Primary();
        if (monitor is null)
        {
            return;
        }

        _work = monitor.WorkArea;
        _scale = monitor.Scale;
        _edge = TaskbarEdgeFor(monitor);
        MaxHeight = Math.Max(240, monitor.WorkArea.Height / _scale - 2 * ScreenMargin);

        var helper = new WindowInteropHelper(this);
        helper.EnsureHandle();
        _hwnd = helper.Handle;

        Root.Measure(new Size(FlyoutWidth, double.PositiveInfinity));
        Reanchor(Math.Min(Root.DesiredSize.Height, MaxHeight));

        if (!IsVisible)
        {
            Show();
        }

        Reanchor();
        LastShownTick = Environment.TickCount64;
        User32.SetForegroundWindow(_hwnd);
        Activate();
        Focus();
        Animate(reduceMotion);
    }

    public void Dismiss()
    {
        if (!IsVisible)
        {
            return;
        }

        InfoPopup.IsOpen = false;
        LastHiddenTick = Environment.TickCount64;
        Hide();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
        {
            if (InfoPopup.IsOpen)
            {
                InfoPopup.IsOpen = false;
            }
            else
            {
                Dismiss();
            }

            e.Handled = true;
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // Alt+F4 hides the flyout; it is reused for the app's lifetime.
        if (!_allowClose)
        {
            e.Cancel = true;
            Dismiss();
        }

        base.OnClosing(e);
    }

    private bool _allowClose;

    public void CloseForShutdown()
    {
        _allowClose = true;
        Close();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        WindowEffects.SetCornerPreference(_hwnd, DWMWCP.ROUND);
        ApplyBackground();
    }

    /// <summary>Mica where supported (applied by the Fluent theme); a solid surface elsewhere.</summary>
    public void ApplyBackground()
    {
        var backdrop = WindowEffects.SupportsSystemBackdrop && !SystemParameters.HighContrast;
        if (backdrop)
        {
            Root.ClearValue(Border.BackgroundProperty);
            Root.Background = System.Windows.Media.Brushes.Transparent;
        }
        else
        {
            Root.SetResourceReference(Border.BackgroundProperty, "SolidBackgroundFillColorBaseBrush");
        }
    }

    private void Reanchor() => Reanchor(ActualHeight);

    private void Reanchor(double heightDip)
    {
        if (_hwnd == 0 || _placing || heightDip <= 0 || _work.IsEmpty)
        {
            return;
        }

        _placing = true;
        try
        {
            var work = new RectD(_work.Left, _work.Top, _work.Width, _work.Height);
            var rect = PlacementMath.Flyout(work, _edge, FlyoutWidth * _scale, heightDip * _scale, ScreenMargin * _scale);
            User32.SetWindowPos(_hwnd, HWND.TOPMOST, (int)rect.X, (int)rect.Y, (int)rect.Width, (int)rect.Height,
                SWP.NOACTIVATE);
        }
        finally
        {
            _placing = false;
        }
    }

    private void Animate(bool reduceMotion)
    {
        var duration = TimeSpan.FromMilliseconds(reduceMotion ? 120 : 260);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        Root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = ease });
        if (!reduceMotion)
        {
            var from = _edge switch
            {
                TaskbarEdge.Top => -14,
                TaskbarEdge.Left or TaskbarEdge.Right => 0,
                _ => 14,
            };
            Slide.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, new DoubleAnimation(from, 0, duration) { EasingFunction = ease });
        }
        else
        {
            Slide.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, null);
            Slide.Y = 0;
        }
    }

    private static TaskbarEdge TaskbarEdgeFor(MonitorInfo monitor)
    {
        var data = new APPBARDATA { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<APPBARDATA>() };
        if (Shell32.SHAppBarMessage(ABM.GETTASKBARPOS, ref data) != 0)
        {
            var taskbarMonitor = Monitors.FromRect(data.rc);
            if (taskbarMonitor is null || taskbarMonitor.Handle == monitor.Handle)
            {
                return data.uEdge switch
                {
                    ABE.LEFT => TaskbarEdge.Left,
                    ABE.TOP => TaskbarEdge.Top,
                    ABE.RIGHT => TaskbarEdge.Right,
                    _ => TaskbarEdge.Bottom,
                };
            }
        }

        return TaskbarEdge.Bottom;
    }

    private void OnRangeChecked(object sender, RoutedEventArgs e)
    {
        if (_suppressRange || sender is not RadioButton { Tag: string tag } || !int.TryParse(tag, out var seconds))
        {
            return;
        }

        RangeChanged?.Invoke(this, seconds);
    }

    private void OnOrbToggled(object sender, RoutedEventArgs e) => OrbToggled?.Invoke(this, OrbToggle.IsChecked == true);

    private void OnInfo(object sender, RoutedEventArgs e) => InfoPopup.IsOpen = !InfoPopup.IsOpen;

    private void OnSettings(object sender, RoutedEventArgs e)
    {
        Dismiss();
        SettingsRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnOpenSources(object sender, RoutedEventArgs e)
    {
        Dismiss();
        SourcesRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnQuit(object sender, RoutedEventArgs e) => QuitRequested?.Invoke(this, EventArgs.Empty);
}
