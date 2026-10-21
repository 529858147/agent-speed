using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
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

    private readonly DashboardViewModel _model;
    private readonly HeightSpring _agentsHeight;
    private nint _hwnd;
    private TaskbarEdge _edge = TaskbarEdge.Bottom;
    private RECT _work;
    private double _scale = 1;
    private bool _placing;
    private bool _suppressRange;
    private bool _toggling;
    private bool _collapsing;
    private double _collapseFromHeight;
    private double _collapseFromOffset;

    public DashboardWindow(DashboardViewModel viewModel, Func<double> clock)
    {
        InitializeComponent();
        DataContext = viewModel;
        _model = viewModel;
        _agentsHeight = new HeightSpring(AgentsViewport);
        _agentsHeight.Stepped += OnAgentsStepped;
        _agentsHeight.Settled += OnAgentsSettled;
        _agentsHeight.Snap(viewModel.AgentsViewportHeight);
        viewModel.PropertyChanged += OnModelChanged;
        TurnChevron(animate: false);
        UpdateAgentsScrollBar();
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible)
            {
                _agentsHeight.Finish();
            }
        };
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

    /// <summary>The agents list was expanded (true) or collapsed (false) by the user.</summary>
    public event EventHandler<bool>? AgentsExpandedChanged;

    /// <summary>The update pill was clicked.</summary>
    public event EventHandler? UpdateRequested;

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

    /// <summary>Shows the footer pill (<c>update.available.short</c>) when <paramref name="text"/> is set.</summary>
    public void SetUpdate(string? text)
    {
        UpdatePill.Visibility = text is null ? Visibility.Collapsed : Visibility.Visible;
        UpdatePillText.Text = text ?? string.Empty;
        System.Windows.Automation.AutomationProperties.SetName(UpdatePill, text ?? string.Empty);
    }

    /// <summary>The one-time <c>update.done</c> line after an update; null hides it.</summary>
    public void SetUpdateDone(string? text)
    {
        UpdateDone.Visibility = text is null ? Visibility.Collapsed : Visibility.Visible;
        UpdateDoneText.Text = text ?? string.Empty;
    }

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

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(DashboardViewModel.AgentsViewportHeight) when !_toggling && !_collapsing:
                if (_agentsHeight.IsRunning)
                {
                    _agentsHeight.Retarget(_model.AgentsViewportHeight);
                }
                else
                {
                    _agentsHeight.Snap(_model.AgentsViewportHeight);
                }

                break;
            case nameof(DashboardViewModel.CollapsedAgentsHeight) when _collapsing:
                _agentsHeight.Retarget(_model.CollapsedAgentsHeight);
                break;
            case nameof(DashboardViewModel.AgentsExpanded):
                TurnChevron(animate: IsVisible && !_model.ReduceMotion);
                break;
            case nameof(DashboardViewModel.AgentsScroll):
                UpdateAgentsScrollBar();
                break;
        }
    }

    private void OnAgentsToggle(object sender, RoutedEventArgs e)
    {
        var animate = IsVisible && !_model.ReduceMotion;
        var from = AgentsViewport.ActualHeight;
        var expand = !_model.AgentsExpanded;
        _toggling = true;
        try
        {
            if (expand)
            {
                _collapsing = false;
                _model.AgentsExpanded = true;
                _model.HoldAllAgents = false;
            }
            else
            {
                // Rows stay until the height settles, so the list closes over them instead of leaving a gap.
                _model.HoldAllAgents = animate;
                _model.AgentsExpanded = false;
            }
        }
        finally
        {
            _toggling = false;
        }

        if (!animate)
        {
            AgentsViewport.ScrollToVerticalOffset(0);
            _agentsHeight.Snap(_model.AgentsViewportHeight);
        }
        else if (expand)
        {
            _agentsHeight.AnimateTo(from, _model.AgentsViewportHeight);
        }
        else
        {
            _collapsing = true;
            _collapseFromHeight = from;
            _collapseFromOffset = AgentsViewport.VerticalOffset;
            _agentsHeight.AnimateTo(from, _model.CollapsedAgentsHeight);
        }

        UpdateAgentsScrollBar();
        AgentsExpandedChanged?.Invoke(this, expand);
    }

    private void OnAgentsStepped(object? sender, double height)
    {
        if (!_collapsing)
        {
            return;
        }

        var span = _collapseFromHeight - _agentsHeight.Target;
        var remaining = span > 0 ? Math.Clamp((height - _agentsHeight.Target) / span, 0, 1) : 0;
        AgentsViewport.ScrollToVerticalOffset(_collapseFromOffset * remaining);
    }

    private void OnAgentsSettled(object? sender, EventArgs e)
    {
        if (_collapsing)
        {
            _collapsing = false;
            AgentsViewport.ScrollToVerticalOffset(0);
            _model.HoldAllAgents = false;
        }

        UpdateAgentsScrollBar();
    }

    /// <summary>The scroll bar appears only once the expanded list rests in its 12-row viewport.</summary>
    private void UpdateAgentsScrollBar() =>
        AgentsViewport.VerticalScrollBarVisibility = _model.AgentsScroll && !_agentsHeight.IsRunning
            ? ScrollBarVisibility.Auto
            : ScrollBarVisibility.Hidden;

    private void TurnChevron(bool animate)
    {
        var angle = _model.AgentsExpanded ? 180 : 0;
        if (animate)
        {
            var turn = new DoubleAnimation(angle, TimeSpan.FromMilliseconds(260))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            };
            AgentsChevron.BeginAnimation(RotateTransform.AngleProperty, turn);
        }
        else
        {
            AgentsChevron.BeginAnimation(RotateTransform.AngleProperty, null);
            AgentsChevron.Angle = angle;
        }
    }

    /// <summary>Hands the wheel to the flyout when the agents list cannot scroll that way.</summary>
    private void OnAgentsWheel(object sender, MouseWheelEventArgs e)
    {
        var offset = AgentsViewport.VerticalOffset;
        var canScroll = _model.AgentsScroll && !_agentsHeight.IsRunning
            && (e.Delta > 0 ? offset > 0.5 : offset < AgentsViewport.ScrollableHeight - 0.5);
        if (canScroll)
        {
            return;
        }

        e.Handled = true;
        Scroller.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
        {
            RoutedEvent = MouseWheelEvent,
            Source = sender,
        });
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

    private void OnUpdate(object sender, RoutedEventArgs e)
    {
        Dismiss();
        UpdateRequested?.Invoke(this, EventArgs.Empty);
    }
}
