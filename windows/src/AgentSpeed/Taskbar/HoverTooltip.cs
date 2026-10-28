using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using AgentSpeed.Interop;
using AgentSpeed.Presentation.Formatting;
using AgentSpeed.Presentation.Layout;
using AgentSpeed.Theme;

namespace AgentSpeed.Taskbar;

/// <summary>A read-only, non-activating taskbar detail card. Hidden cards do no formatting or text updates.</summary>
internal sealed class HoverTooltip : Window
{
    private const double ShadowMargin = 14;
    private readonly TextBlock _primary = Text(14, primary: true, bold: true);
    private readonly TextBlock _status = Text(12);
    private readonly TextBlock _measurement = Text(11.5);
    private readonly Grid _sources = new();
    private readonly List<(TextBlock Label, TextBlock Value)> _sourceRows = [];
    private readonly TextBlock _todayLabel = Text(12);
    private readonly TextBlock _today = Text(12, primary: true);
    private readonly TextBlock _liveLabel = Text(12);
    private readonly TextBlock _live = Text(12, primary: true);
    private readonly TextBlock _speedScope = Text(11.5);
    private readonly TextBlock _costScope = Text(11.5);
    private readonly TextBlock _subscriptions = Text(11.5);
    private readonly TextBlock _notice = Text(11.5);
    private readonly TextBlock _hint = Text(11.5);
    private readonly TextBlock _actions = Text(11);
    private readonly TextBlock _traffic = Text(11.5);
    private readonly ScrollViewer _viewport;
    private RECT _anchor;

    public HoverTooltip()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        Width = 348;
        SizeToContent = SizeToContent.Height;
        Focusable = false;
        IsHitTestVisible = false;
        Style = new Style(typeof(Window));
        Left = Top = -32000;
        Title = "Agent Speed";

        var stack = new StackPanel();
        var title = Text(12, primary: true, bold: true);
        title.Text = "Agent Speed";
        title.Margin = new Thickness(0, 0, 0, 6);
        stack.Children.Add(title);
        stack.Children.Add(_primary);
        stack.Children.Add(_measurement);
        stack.Children.Add(_status);
        _sources.ColumnDefinitions.Add(new ColumnDefinition());
        _sources.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _sources.Margin = new Thickness(0, 8, 0, 0);
        stack.Children.Add(_sources);
        stack.Children.Add(Divider());
        var costs = new Grid();
        costs.ColumnDefinitions.Add(new ColumnDefinition());
        costs.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        for (var i = 0; i < 2; i++) costs.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        AddRow(costs, _todayLabel, _today, 0);
        AddRow(costs, _liveLabel, _live, 1);
        stack.Children.Add(costs);
        stack.Children.Add(Divider());
        foreach (var block in new[] { _speedScope, _costScope, _subscriptions, _notice, _traffic, _hint }) stack.Children.Add(block);
        _hint.Margin = new Thickness(0, 6, 0, 0);
        stack.Children.Add(Divider());
        stack.Children.Add(_actions);
        _viewport = new ScrollViewer { Content = stack, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Focusable = false };
        var border = new Border
        {
            CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(ShadowMargin), Child = _viewport,
            Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Direction = 270, Opacity = 0.18, Color = Colors.Black },
        };
        border.SetResourceReference(Border.BackgroundProperty, "ToolTipBackgroundBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "ToolTipBorderBrush");
        Content = border;
        SourceInitialized += (_, _) => WindowEffects.AddExtendedStyle(new WindowInteropHelper(this).Handle,
            WS_EX.TOOLWINDOW | WS_EX.NOACTIVATE | WS_EX.TRANSPARENT);
        SizeChanged += (_, _) => Reposition();
    }

    internal int ContentUpdates { get; private set; }

    public void ShowFor(RECT anchor, TaskbarDetails details, bool reduceMotion)
    {
        _anchor = anchor;
        var monitor = Monitors.FromRect(anchor);
        var scale = monitor?.Scale ?? 1;
        var work = monitor?.WorkArea;
        MaxWidth = work is { } w ? Math.Max(120, w.Width / scale) : 348;
        _viewport.MaxHeight = work is { } h ? Math.Max(60, h.Height / scale - 2 * ShadowMargin - 40) : double.PositiveInfinity;
        if (!IsVisible)
        {
            Opacity = 0;
            Show();
        }

        SetDetails(details);
        Reposition();
        BeginAnimation(OpacityProperty, new DoubleAnimation(1, reduceMotion ? TimeSpan.Zero : TimeSpan.FromMilliseconds(140)));
    }

    public bool SetDetails(TaskbarDetails details)
    {
        if (!IsVisible) return false;
        var changed = Set(_primary, details.Primary) | Set(_status, details.Status) | Set(_measurement, details.Measurement)
            | Set(_todayLabel, details.TodayLabel) | Set(_today, details.Today) | Set(_liveLabel, details.LiveLabel) | Set(_live, details.Live)
            | Set(_speedScope, details.SpeedScope) | Set(_costScope, details.CostScope) | Set(_subscriptions, details.Subscriptions)
            | Set(_notice, details.Notice) | Set(_hint, details.Hint) | Set(_actions, details.Actions) | Set(_traffic, details.Traffic);
        while (_sourceRows.Count > details.Sources.Count)
        {
            var row = _sourceRows[^1];
            _sources.Children.Remove(row.Label);
            _sources.Children.Remove(row.Value);
            _sources.RowDefinitions.RemoveAt(_sourceRows.Count - 1);
            _sourceRows.RemoveAt(_sourceRows.Count - 1);
            changed = true;
        }

        for (var i = 0; i < details.Sources.Count; i++)
        {
            if (i == _sourceRows.Count)
            {
                var row = (Text(12, primary: true), Text(12, primary: true));
                _sourceRows.Add(row);
                _sources.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                AddRow(_sources, row.Item1, row.Item2, i);
                changed = true;
            }

            changed |= Set(_sourceRows[i].Label, details.Sources[i].Label) | Set(_sourceRows[i].Value, details.Sources[i].Value);
        }

        _sources.Visibility = details.Sources.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (changed) ContentUpdates++;
        return changed;
    }

    public void HideTip()
    {
        if (!IsVisible) return;
        BeginAnimation(OpacityProperty, null);
        Opacity = 0;
        Hide();
    }

    private static TextBlock Text(double size, bool primary = false, bool bold = false)
    {
        var block = new TextBlock { FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 1) };
        block.SetResourceReference(TextBlock.ForegroundProperty, primary ? "ToolTipForegroundBrush" : "TextFillColorSecondaryBrush");
        block.SetResourceReference(TextBlock.FontFamilyProperty, AppFonts.UiFontKey);
        Typography.SetNumeralAlignment(block, FontNumeralAlignment.Tabular);
        return block;
    }

    private static bool Set(TextBlock block, string text)
    {
        var visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (block.Text == text && block.Visibility == visibility) return false;
        block.Text = text;
        block.Visibility = visibility;
        return true;
    }

    private static Border Divider()
    {
        var line = new Border { Height = 1, Margin = new Thickness(0, 8, 0, 8) };
        line.SetResourceReference(Border.BackgroundProperty, "ToolTipBorderBrush");
        return line;
    }

    private static void AddRow(Grid grid, TextBlock label, TextBlock value, int row)
    {
        Grid.SetRow(label, row);
        Grid.SetRow(value, row);
        Grid.SetColumn(value, 1);
        value.Margin = new Thickness(12, 1, 0, 1);
        value.HorizontalAlignment = HorizontalAlignment.Right;
        grid.Children.Add(label);
        grid.Children.Add(value);
    }

    private void Reposition()
    {
        if (!IsVisible || _anchor.IsEmpty) return;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == 0) return;
        var monitor = Monitors.FromRect(_anchor);
        if (monitor is null) return;
        var scale = monitor.Scale;
        var work = monitor.WorkArea;
        var edge = _anchor.Top >= work.Bottom - 2 ? TaskbarEdge.Bottom
            : _anchor.Bottom <= work.Top + 2 ? TaskbarEdge.Top
            : _anchor.Left >= work.Right - 2 ? TaskbarEdge.Right : TaskbarEdge.Left;
        var margin = ShadowMargin * scale;
        var point = PlacementMath.TaskbarTooltip(new RectD(_anchor.Left, _anchor.Top, _anchor.Width, _anchor.Height),
            new RectD(work.Left - margin, work.Top - margin, work.Width + margin * 2, work.Height + margin * 2), edge,
            ActualWidth * scale, ActualHeight * scale, 8 * scale - margin);
        User32.SetWindowPos(hwnd, HWND.TOPMOST, (int)Math.Round(point.X), (int)Math.Round(point.Y), 0, 0, SWP.NOSIZE | SWP.NOACTIVATE);
    }
}
