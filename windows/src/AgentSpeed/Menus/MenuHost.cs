using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using AgentSpeed.Interop;

namespace AgentSpeed.Menus;

/// <summary>
/// Shows a Fluent context menu at the cursor from places that have no focusable window (tray, taskbar, orb).
/// A tiny invisible host window takes foreground so the menu gets keyboard input and closes on outside clicks.
/// </summary>
internal sealed class MenuHost
{
    private readonly Window _host;
    private ContextMenu? _open;

    public MenuHost()
    {
        _host = new Window
        {
            Width = 1,
            Height = 1,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = System.Windows.Media.Brushes.Transparent,
            ShowInTaskbar = false,
            ShowActivated = true,
            Topmost = true,
            ResizeMode = ResizeMode.NoResize,
            Left = -10000,
            Top = -10000,
            Style = new Style(typeof(Window)),
            Title = "Agent Speed",
        };
        _host.SourceInitialized += (_, _) =>
            WindowEffects.AddExtendedStyle(new WindowInteropHelper(_host).Handle, WS_EX.TOOLWINDOW);
    }

    public bool IsOpen => _open?.IsOpen == true;

    public void Show(ContextMenu menu)
    {
        _open?.SetCurrentValue(ContextMenu.IsOpenProperty, false);

        if (User32.GetCursorPos(out var cursor))
        {
            var helper = new WindowInteropHelper(_host);
            helper.EnsureHandle();
            User32.SetWindowPos(helper.Handle, HWND.TOPMOST, cursor.X, cursor.Y, 1, 1, SWP.NOACTIVATE);
        }

        if (!_host.IsVisible)
        {
            _host.Show();
        }

        var handle = new WindowInteropHelper(_host).Handle;
        User32.SetForegroundWindow(handle);
        _host.Activate();

        menu.PlacementTarget = _host;
        menu.Placement = PlacementMode.MousePoint;
        menu.Closed += OnClosed;
        _open = menu;
        menu.IsOpen = true;
    }

    public void Close()
    {
        if (_open is { } menu)
        {
            menu.IsOpen = false;
        }
    }

    private void OnClosed(object sender, RoutedEventArgs e)
    {
        if (sender is ContextMenu menu)
        {
            menu.Closed -= OnClosed;
            if (ReferenceEquals(menu, _open))
            {
                _open = null;
                _host.Hide();
            }
        }
    }
}
