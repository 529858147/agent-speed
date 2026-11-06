using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AgentSpeed.Settings.OrbsPane;

namespace AgentSpeed.Settings.BoardsPane;

/// <summary>Settings → 懸浮窗, Boards: the boards' list and editor (<see cref="BoardsPaneViewModel"/>).</summary>
internal sealed partial class BoardsSection : UserControl
{
    public BoardsSection()
    {
        InitializeComponent();
    }

    private BoardsPaneViewModel? Model => DataContext as BoardsPaneViewModel;

    private void OnSourceClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: OrbSourceRowViewModel row })
        {
            Model?.ToggleSource(row);
        }
    }

    /// <summary>Space toggles the selected board's visibility, Delete removes it (asking first).</summary>
    private void OnListKeyDown(object sender, KeyEventArgs e)
    {
        if (Model is not { SelectedRow: { } row } model)
        {
            return;
        }

        if (e.Key == Key.Space)
        {
            model.ToggleVisibleCommand.Execute(row);
            e.Handled = true;
        }
        else if (e.Key == Key.Delete && model.RemoveCommand.CanExecute(null))
        {
            model.RemoveCommand.Execute(null);
            e.Handled = true;
        }
    }
}
