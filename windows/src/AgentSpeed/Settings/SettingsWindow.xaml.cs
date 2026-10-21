using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using AgentSpeed.App;

namespace AgentSpeed.Settings;

/// <summary>Fluent settings window (DESIGN section 5). Mica comes from the Fluent theme on Windows 11.</summary>
internal sealed partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;

    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        viewModel.PropertyChanged += OnViewModelChanged;
        ShowPage();
        LoadIcon();
    }

    public void ShowPage(string key)
    {
        _viewModel.Select(key);
        ShowPage();
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.PropertyChanged -= OnViewModelChanged;
        base.OnClosed(e);
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(SettingsViewModel.PageKey))
        {
            ShowPage();
        }
    }

    private string? _shownPage;

    private void ShowPage()
    {
        var key = _viewModel.PageKey;
        if (key == _shownPage)
        {
            return;
        }

        _shownPage = key;
        foreach (UIElement page in new UIElement[] { GeneralPage, TaskbarPage, OrbPage, EffectsPage, SourcesPage, AboutPage })
        {
            var name = ((FrameworkElement)page).Name;
            page.Visibility = string.Equals(name, key + "Page", StringComparison.OrdinalIgnoreCase)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        PageScroller.ScrollToTop();
    }

    private void LoadIcon()
    {
        try
        {
            var uri = new Uri("pack://application:,,,/AgentSpeed;component/Assets/AgentSpeed.ico", UriKind.Absolute);
            var decoder = BitmapDecoder.Create(uri, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames.OrderByDescending(f => f.PixelWidth).FirstOrDefault(f => f.PixelWidth <= 64) ?? decoder.Frames.FirstOrDefault();
            if (frame is null)
            {
                throw new IOException("Icon has no frames.");
            }

            AppIcon.Source = frame;
            Icon = decoder.Frames.FirstOrDefault(f => f.PixelWidth == 32) ?? frame;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UriFormatException or InvalidOperationException
                                       or System.Runtime.InteropServices.COMException or FileFormatException)
        {
            AppIcon.Visibility = Visibility.Collapsed;
            FallbackIcon.Visibility = Visibility.Visible;
            AppLog.Info("App icon resource not available; using the drawn gauge.");
        }
    }

    internal static string? PickFolder(Window? owner, string? initial)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Multiselect = false,
        };
        if (!string.IsNullOrEmpty(initial))
        {
            dialog.InitialDirectory = initial;
        }

        var result = owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
        return result == true ? dialog.FolderName : null;
    }
}
