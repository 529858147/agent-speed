using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using AgentSpeed.Settings.OrbsPane;

namespace AgentSpeed.Settings.Pages;

internal partial class OrbPage : UserControl
{
    private Point _dragStart;
    private OrbRowViewModel? _dragRow;

    public OrbPage()
    {
        InitializeComponent();
        IsVisibleChanged += (_, _) => Highlight(IsVisible ? Model?.SelectedRow : null);
        OrbList.SelectionChanged += (_, _) => Highlight(Model?.SelectedRow);
        OrbList.GotKeyboardFocus += (_, _) => Highlight(Model?.SelectedRow);
    }

    private OrbsPaneViewModel? Model => (DataContext as SettingsViewModel)?.Orbs;

    private System.Windows.Controls.Primitives.UniformGrid? _cardsPanel;

    private void OnCardsPanelLoaded(object sender, RoutedEventArgs e)
    {
        _cardsPanel = sender as System.Windows.Controls.Primitives.UniformGrid;
        UpdateCardColumns();
    }

    /// <summary>Five cards in one row; 3 + 2 below 560 pt of width (DESIGN 5).</summary>
    private void OnCardsSizeChanged(object sender, SizeChangedEventArgs e) => UpdateCardColumns();

    private void UpdateCardColumns()
    {
        if (_cardsPanel is { } panel)
        {
            var columns = StyleCards.ActualWidth >= 560 ? 5 : 3;
            if (panel.Columns != columns)
            {
                panel.Columns = columns;
            }
        }
    }

    private void Highlight(OrbRowViewModel? row) => Model?.Highlight(IsVisible ? row?.Id : null);

    private void OnRowEnter(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: OrbRowViewModel row })
        {
            Highlight(row);
        }
    }

    private void OnListMouseLeave(object sender, MouseEventArgs e) => Highlight(Model?.SelectedRow);

    private void OnEyeClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: OrbRowViewModel row })
        {
            Model?.ToggleVisibility(row);
        }

        e.Handled = true;
    }

    private void OnSourceClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: OrbSourceRowViewModel row })
        {
            Model?.ToggleSource(row);
        }
    }

    /// <summary>Space toggles visibility, Ctrl+D duplicates, Delete removes (the removal can be undone).</summary>
    private void OnListKeyDown(object sender, KeyEventArgs e)
    {
        if (Model is not { } model || model.SelectedRow is not { } row)
        {
            return;
        }

        if (e.Key == Key.Space)
        {
            model.ToggleVisibility(row);
            e.Handled = true;
        }
        else if (e.Key == Key.D && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (model.DuplicateCommand.CanExecute(null))
            {
                model.DuplicateCommand.Execute(null);
            }

            e.Handled = true;
        }
        else if (e.Key is Key.Delete or Key.Back)
        {
            model.RemoveSelected();
            e.Handled = true;
        }
    }

    private void OnListMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(OrbList);
        _dragRow = (e.OriginalSource as DependencyObject) is { } source && ItemsControl.ContainerFromElement(OrbList, source) is ListBoxItem item
            ? item.DataContext as OrbRowViewModel
            : null;
    }

    /// <summary>Rows reorder by drag (list order only).</summary>
    private void OnListMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragRow is null || e.OriginalSource is ButtonBase)
        {
            return;
        }

        var position = e.GetPosition(OrbList);
        if (Math.Abs(position.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var row = _dragRow;
        _dragRow = null;
        DragDrop.DoDragDrop(OrbList, new DataObject(typeof(OrbRowViewModel), row), DragDropEffects.Move);
    }

    private void OnListDrop(object sender, DragEventArgs e)
    {
        if (Model is not { } model || e.Data.GetData(typeof(OrbRowViewModel)) is not OrbRowViewModel row)
        {
            return;
        }

        var target = (e.OriginalSource as DependencyObject) is { } source && ItemsControl.ContainerFromElement(OrbList, source) is ListBoxItem item
            ? item.DataContext as OrbRowViewModel
            : null;
        var index = target is null ? model.Rows.Count - 1 : model.Rows.IndexOf(target);
        model.Move(row.Id, index);
        model.SelectedRow = model.Rows.FirstOrDefault(r => r.Id == row.Id);
        _ = VisualTreeHelper.GetParent(this);
    }
}
