using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using AgentSpeed.Interop;
using AgentSpeed.Presentation.Layout;
using AgentSpeed.Presentation.Motion;
using AgentSpeed.Presentation.Settings;

namespace AgentSpeed.Orb;

/// <summary>
/// The floating orb window: borderless, per-pixel transparent, never activated. Outside the disc it lets clicks
/// through (WS_EX_TRANSPARENT toggled by cursor position); it drags with magnetic edge snapping.
/// </summary>
internal sealed class OrbWindow : Window
{
    private const double SnapThreshold = 24;
    private const double SnapMargin = 12;
    private const double DragSlop = 3;

    private readonly DispatcherTimer _passThroughPoll;
    private readonly DispatcherTimer _housekeeping;
    private nint _hwnd;
    private bool _clickThrough;
    private bool _passThrough;
    private bool _dragging;
    private bool _dragMoved;
    private POINT _dragStartCursor;
    private PointD _dragStartCenter;
    private PointD _center;
    private bool _hasCenter;
    private DampedSpring _snapX;
    private DampedSpring _snapY;
    private PointD _snapTarget;
    private bool _snapping;
    private bool _fadeWhenIdle;
    private bool _faded;
    private double _idleSince = double.NaN;
    private bool _reduceMotion;
    private readonly Func<double> _clock;

    public OrbWindow(Func<double> clock)
    {
        _clock = clock;
        View = new OrbView(clock);
        Content = View;
        Title = "Agent Speed";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.Manual;
        Topmost = true;
        Focusable = false;
        Style = new Style(typeof(Window));
        UseLayoutRounding = false;
        Cursor = Cursors.SizeAll;
        Left = -32000;
        Top = -32000;
        ApplySize(View.Diameter);

        _passThroughPoll = new DispatcherTimer(TimeSpan.FromMilliseconds(40), DispatcherPriority.Input, (_, _) => PollPassThrough(), Dispatcher);
        _passThroughPoll.Stop();
        _housekeeping = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, (_, _) => Housekeeping(), Dispatcher);

        SourceInitialized += OnSourceInitialized;
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => PlaceAt(_center));
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                _housekeeping.Start();
            }
            else
            {
                _housekeeping.Stop();
                _passThroughPoll.Stop();
            }
        };
        Closed += (_, _) =>
        {
            _housekeeping.Stop();
            _passThroughPoll.Stop();
            CompositionTarget.Rendering -= OnSnapFrame;
        };
    }

    public OrbView View { get; }

    /// <summary>Raised on double-click.</summary>
    public event EventHandler? DashboardRequested;

    /// <summary>Right-click; the menu opens at the cursor.</summary>
    public event EventHandler? MenuRequested;

    /// <summary>The user finished moving the orb: monitor key and the center as a work-area fraction.</summary>
    public event EventHandler<(string MonitorKey, OrbAnchor Anchor)>? PositionCommitted;

    public PointD Center => _center;

    public void Configure(OrbConfig config, bool alwaysOnTop, bool clickThrough, bool fadeWhenIdle, bool reduceMotion)
    {
        var sizeChanged = Math.Abs(config.Diameter - View.Diameter) > 0.01;
        View.Configure(config);
        _reduceMotion = reduceMotion;
        if (sizeChanged)
        {
            ApplySize(config.Diameter);
            if (_hasCenter)
            {
                PlaceAt(_center);
            }
        }

        if (Topmost != alwaysOnTop)
        {
            Topmost = alwaysOnTop;
        }

        _fadeWhenIdle = fadeWhenIdle;
        if (!fadeWhenIdle)
        {
            Restore();
        }

        SetClickThrough(clickThrough);
    }

    /// <summary>Places the orb from the persisted anchor, validated against the current monitors.</summary>
    public void RestorePlacement(string? monitorKey, OrbAnchor? anchor)
    {
        var monitors = Monitors.All();
        var monitor = monitors.FirstOrDefault(m => string.Equals(m.Key, monitorKey, StringComparison.OrdinalIgnoreCase))
            ?? monitors.FirstOrDefault(m => m.IsPrimary)
            ?? monitors.FirstOrDefault();
        if (monitor is null)
        {
            return;
        }

        var radius = View.Diameter / 2 * monitor.Scale;
        var margin = SnapMargin * monitor.Scale;
        var found = anchor is not null && string.Equals(monitor.Key, monitorKey, StringComparison.OrdinalIgnoreCase);
        var center = found
            ? PlacementMath.ClampDisc(PlacementMath.FromFraction(anchor!.Value.X, anchor.Value.Y, monitor.WorkRect), radius, monitor.WorkRect, margin)
            : PlacementMath.DefaultOrbCenter(monitor.WorkRect, radius, margin * 2);
        PlaceAt(center);
    }

    /// <summary>After a display change, keeps the orb on a visible work area.</summary>
    public void Revalidate()
    {
        if (!_hasCenter)
        {
            return;
        }

        var monitors = Monitors.All();
        var works = monitors.Select(m => m.WorkRect).ToArray();
        var index = PlacementMath.NearestIndex(_center, works);
        if (index < 0)
        {
            return;
        }

        var monitor = monitors[index];
        var radius = View.Diameter / 2 * monitor.Scale;
        PlaceAt(PlacementMath.ClampDisc(_center, radius, monitor.WorkRect, SnapMargin * monitor.Scale * 0.5));
    }

    public void NoteActivity(bool active)
    {
        if (active)
        {
            _idleSince = double.NaN;
            Restore();
        }
        else if (double.IsNaN(_idleSince))
        {
            _idleSince = _clock();
        }
    }

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        UpdateHover(e.GetPosition(View));
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (!_dragging)
        {
            View.SetHover(false);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging)
        {
            ContinueDrag();
            return;
        }

        UpdateHover(e.GetPosition(View));
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (!IsInsideDisc(e.GetPosition(View)))
        {
            return;
        }

        if (e.ClickCount >= 2)
        {
            _dragging = false;
            DashboardRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            return;
        }

        if (User32.GetCursorPos(out var cursor))
        {
            StopSnap();
            _dragging = true;
            _dragMoved = false;
            _dragStartCursor = cursor;
            _dragStartCenter = _center;
            CaptureMouse();
            e.Handled = true;
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        ReleaseMouseCapture();
        if (_dragMoved)
        {
            SnapToEdges();
        }

        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (_dragging)
        {
            _dragging = false;
            if (_dragMoved)
            {
                SnapToEdges();
            }
        }
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        if (IsInsideDisc(e.GetPosition(View)))
        {
            MenuRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        View.Paused = true;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        WindowEffects.AddExtendedStyle(_hwnd, WS_EX.TOOLWINDOW | WS_EX.NOACTIVATE);
        SetClickThrough(_clickThrough, force: true);
        if (_hasCenter)
        {
            PlaceAt(_center);
        }
    }

    private void ApplySize(double diameter)
    {
        Width = diameter * 1.5;
        Height = diameter * 1.5;
    }

    private void PlaceAt(PointD center)
    {
        _center = center;
        _hasCenter = true;
        if (_hwnd == 0)
        {
            return;
        }

        var monitor = Monitors.FromPoint(new POINT((int)Math.Round(center.X), (int)Math.Round(center.Y)));
        var scale = monitor?.Scale ?? VisualTreeHelper.GetDpi(this).DpiScaleX;
        var size = (int)Math.Round(View.Diameter * 1.5 * scale);
        var left = (int)Math.Round(center.X - size / 2.0);
        var top = (int)Math.Round(center.Y - size / 2.0);
        User32.SetWindowPos(_hwnd, 0, left, top, size, size, SWP.NOZORDER | SWP.NOACTIVATE | SWP.NOOWNERZORDER);
    }

    private void ContinueDrag()
    {
        if (!User32.GetCursorPos(out var cursor))
        {
            return;
        }

        var dx = cursor.X - _dragStartCursor.X;
        var dy = cursor.Y - _dragStartCursor.Y;
        if (!_dragMoved && Math.Abs(dx) < DragSlop && Math.Abs(dy) < DragSlop)
        {
            return;
        }

        _dragMoved = true;
        PlaceAt(new PointD(_dragStartCenter.X + dx, _dragStartCenter.Y + dy));
    }

    private void SnapToEdges()
    {
        var monitors = Monitors.All();
        var index = PlacementMath.NearestIndex(_center, monitors.Select(m => m.WorkRect).ToArray());
        if (index < 0)
        {
            return;
        }

        var monitor = monitors[index];
        var radius = View.Diameter / 2 * monitor.Scale;
        var target = PlacementMath.Snap(_center, radius, monitor.WorkRect, SnapThreshold * monitor.Scale, SnapMargin * monitor.Scale);
        _snapTarget = target;

        if (_reduceMotion || (Math.Abs(target.X - _center.X) < 0.5 && Math.Abs(target.Y - _center.Y) < 0.5))
        {
            PlaceAt(target);
            Commit(monitor);
            return;
        }

        _snapX = new DampedSpring(0.45, 0.82, _center.X);
        _snapY = new DampedSpring(0.45, 0.82, _center.Y);
        _snapping = true;
        _lastSnapTime = _clock();
        CompositionTarget.Rendering += OnSnapFrame;
    }

    private double _lastSnapTime;

    private void OnSnapFrame(object? sender, EventArgs e)
    {
        var now = _clock();
        var dt = Math.Clamp(now - _lastSnapTime, 0, 0.05);
        _lastSnapTime = now;
        _snapX.Step(_snapTarget.X, dt);
        _snapY.Step(_snapTarget.Y, dt);
        if (_snapX.IsSettled(_snapTarget.X, 0.3) && _snapY.IsSettled(_snapTarget.Y, 0.3))
        {
            StopSnap();
            PlaceAt(_snapTarget);
            if (Monitors.FromPoint(new POINT((int)_snapTarget.X, (int)_snapTarget.Y)) is { } monitor)
            {
                Commit(monitor);
            }

            return;
        }

        PlaceAt(new PointD(_snapX.Value, _snapY.Value));
    }

    private void StopSnap()
    {
        if (_snapping)
        {
            _snapping = false;
            CompositionTarget.Rendering -= OnSnapFrame;
        }
    }

    private void Commit(MonitorInfo monitor)
    {
        var (fx, fy) = PlacementMath.ToFraction(_center, monitor.WorkRect);
        PositionCommitted?.Invoke(this, (monitor.Key, new OrbAnchor(fx, fy)));
    }

    private bool IsInsideDisc(Point position)
    {
        var c = View.Diameter * 0.75;
        var r = View.Diameter * 0.5 * (1 + 0.03 * Math.Max(0, View.Scene.Hover)) + 1;
        var dx = position.X - c;
        var dy = position.Y - c;
        return dx * dx + dy * dy <= r * r;
    }

    private void UpdateHover(Point position)
    {
        var inside = IsInsideDisc(position);
        View.SetHover(inside);
        if (inside)
        {
            Restore();
        }
        else if (!_clickThrough)
        {
            SetPassThrough(true);
        }
    }

    private void PollPassThrough()
    {
        if (!_passThrough || _hwnd == 0 || !User32.GetCursorPos(out var cursor) || !User32.GetWindowRect(_hwnd, out var rect))
        {
            _passThroughPoll.Stop();
            return;
        }

        var insideWindow = cursor.X >= rect.Left && cursor.X < rect.Right && cursor.Y >= rect.Top && cursor.Y < rect.Bottom;
        if (!insideWindow)
        {
            SetPassThrough(false);
            return;
        }

        var scale = rect.Width / Math.Max(1, ActualWidth);
        var local = new Point((cursor.X - rect.Left) / scale, (cursor.Y - rect.Top) / scale);
        if (IsInsideDisc(local))
        {
            SetPassThrough(false);
            View.SetHover(true);
            Restore();
        }
    }

    private void SetPassThrough(bool enabled)
    {
        if (_clickThrough || _passThrough == enabled)
        {
            return;
        }

        _passThrough = enabled;
        ApplyTransparentStyle(enabled);
        if (enabled)
        {
            View.SetHover(false);
            _passThroughPoll.Start();
        }
        else
        {
            _passThroughPoll.Stop();
        }
    }

    private void SetClickThrough(bool enabled, bool force = false)
    {
        if (!force && _clickThrough == enabled)
        {
            return;
        }

        _clickThrough = enabled;
        _passThrough = false;
        _passThroughPoll.Stop();
        ApplyTransparentStyle(enabled);
        if (enabled)
        {
            View.SetHover(false);
        }
    }

    private void ApplyTransparentStyle(bool transparent)
    {
        if (_hwnd == 0)
        {
            return;
        }

        if (transparent)
        {
            WindowEffects.AddExtendedStyle(_hwnd, WS_EX.TRANSPARENT | WS_EX.LAYERED, frameChanged: false);
        }
        else
        {
            WindowEffects.AddExtendedStyle(_hwnd, 0, WS_EX.TRANSPARENT, frameChanged: false);
        }
    }

    private void Housekeeping()
    {
        View.Paused = WindowEffects.IsCloaked(_hwnd);

        if (_fadeWhenIdle && !_faded && !double.IsNaN(_idleSince) && _clock() - _idleSince >= 60 && View.Scene.HoverBlend < 0.01)
        {
            _faded = true;
            var animation = new DoubleAnimation(0.45, _reduceMotion ? TimeSpan.Zero : TimeSpan.FromSeconds(0.6))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
            };
            BeginAnimation(OpacityProperty, animation);
        }
    }

    private void Restore()
    {
        if (!_faded)
        {
            return;
        }

        _faded = false;
        BeginAnimation(OpacityProperty, null);
        Opacity = 1;
    }
}
