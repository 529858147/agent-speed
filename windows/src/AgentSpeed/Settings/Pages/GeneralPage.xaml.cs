using System.Windows;

namespace AgentSpeed.Settings.Pages;

internal sealed partial class GeneralPage : System.Windows.Controls.UserControl
{
    public GeneralPage()
    {
        InitializeComponent();
    }

    /// <summary>Hovering or focusing a speed title highlights its tick on the heat strip.</summary>
    private void OnLadderItemEnter(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TitleLadderRow row })
        {
            TitleStrip.HighlightIndex = row.Index;
        }
    }

    private void OnLadderItemLeave(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TitleLadderRow row } element && TitleStrip.HighlightIndex == row.Index
            && !element.IsMouseOver && !element.IsKeyboardFocused)
        {
            TitleStrip.HighlightIndex = -1;
        }
    }
}
