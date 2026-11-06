using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using AgentSpeed.Localization;
using AgentSpeed.Presentation.History;

namespace AgentSpeed.History;

/// <summary>
/// The unknown-prices popover's content (DESIGN 9): rows by source and the Copy Diagnostics button, which copies the
/// path-free text of METRICS 9.10 and shows <c>history.unpricedModels.copied</c> (or <c>.copyFailed</c>) for 1.6 s.
/// </summary>
internal sealed partial class UnpricedPanel : UserControl
{
    private static readonly TimeSpan Fade = TimeSpan.FromSeconds(0.15);
    private static readonly TimeSpan Hold = TimeSpan.FromSeconds(1.6);
    private static readonly TimeSpan Retry = TimeSpan.FromMilliseconds(100);

    private readonly DispatcherTimer _restore;
    private bool _copying;

    public UnpricedPanel()
    {
        InitializeComponent();
        _restore = new DispatcherTimer(Hold, DispatcherPriority.Normal, (_, _) => ShowFace(IdleFace), Dispatcher);
        _restore.Stop();
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Copy, OnCopyRow, OnCanCopyRow));
    }

    /// <summary>Set by the window from the system's animation preference.</summary>
    public bool ReduceMotion { get; set; }

    /// <summary>Moves keyboard focus to the first row, or to the copy button when there is none.</summary>
    public void FocusFirst()
    {
        if (FirstRow() is { } row)
        {
            row.Focus();
        }
        else
        {
            CopyButton.Focus();
        }
    }

    /// <summary>Back to the plain button when the popover closes.</summary>
    public void Reset()
    {
        _restore.Stop();
        foreach (var face in new[] { IdleFace, CopiedFace, FailedFace })
        {
            face.BeginAnimation(OpacityProperty, null);
            face.Opacity = ReferenceEquals(face, IdleFace) ? 1 : 0;
        }
    }

    private async void OnCopy(object sender, RoutedEventArgs e)
    {
        if (_copying || DataContext is not HistoryViewModel model)
        {
            return;
        }

        _copying = true;
        try
        {
            var copied = await TryCopy(model.UnpricedDiagnostics());
            ShowFace(copied ? CopiedFace : FailedFace);
            _restore.Stop();
            _restore.Start();
            Announce(LocalizationSource.Instance[copied ? "history.unpricedModels.copied" : "history.unpricedModels.copyFailed"]);
        }
        finally
        {
            _copying = false;
        }
    }

    /// <summary>Plain text to the clipboard; another program can hold it for a moment, so a failure is retried once.</summary>
    private static async Task<bool> TryCopy(string text)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                Clipboard.SetDataObject(new DataObject(DataFormats.UnicodeText, text), copy: true);
                return true;
            }
            catch (ExternalException)
            {
                if (attempt == 0)
                {
                    await Task.Delay(Retry);
                }
            }
        }

        return false;
    }

    private void ShowFace(FrameworkElement shown)
    {
        foreach (var face in new FrameworkElement[] { IdleFace, CopiedFace, FailedFace })
        {
            var target = ReferenceEquals(face, shown) ? 1.0 : 0.0;
            if (ReduceMotion)
            {
                face.BeginAnimation(OpacityProperty, null);
                face.Opacity = target;
            }
            else
            {
                face.BeginAnimation(OpacityProperty, new DoubleAnimation(target, Fade) { EasingFunction = new QuadraticEase() });
            }
        }
    }

    /// <summary>Speaks the result once without moving focus (UI Automation notification).</summary>
    private void Announce(string text)
    {
        var peer = UIElementAutomationPeer.FromElement(CopyButton) ?? UIElementAutomationPeer.CreatePeerForElement(CopyButton);
        peer?.RaiseNotificationEvent(AutomationNotificationKind.ActionCompleted, AutomationNotificationProcessing.MostRecent, text,
            "AgentSpeed.UnpricedCopy");
    }

    private static UnpricedRowView? RowOf(object? source) =>
        (source as FrameworkElement)?.DataContext as UnpricedRowView;

    private void OnCanCopyRow(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = RowOf(e.OriginalSource) is { IsUnnamed: false };
        e.Handled = true;
    }

    /// <summary>Ctrl+C on a focused row copies its model id as logged.</summary>
    private void OnCopyRow(object sender, ExecutedRoutedEventArgs e)
    {
        if (RowOf(e.OriginalSource) is { IsUnnamed: false } row)
        {
            _ = TryCopy(row.Model);
            e.Handled = true;
        }
    }

    private ListBoxItem? FirstRow()
    {
        if (Groups.ItemContainerGenerator.ContainerFromIndex(0) is not { } group)
        {
            return null;
        }

        return FindChild<ListBox>(group) is { } list && list.ItemContainerGenerator.ContainerFromIndex(0) is ListBoxItem item ? item : null;
    }

    private static T? FindChild<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T found)
            {
                return found;
            }

            if (FindChild<T>(child) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }
}
