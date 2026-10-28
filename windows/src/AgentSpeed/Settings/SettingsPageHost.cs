using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using AgentSpeed.Settings.Pages;

namespace AgentSpeed.Settings;

/// <summary>Creates each settings page on its first visit and retains its controls until the window closes.</summary>
internal sealed class SettingsPageHost : Grid
{
    private readonly Func<string, FrameworkElement> _create;
    private readonly Dictionary<string, FrameworkElement> _pages = new(StringComparer.Ordinal);

    public SettingsPageHost() : this(CreatePage) { }

    internal SettingsPageHost(Func<string, FrameworkElement> create) => _create = create;

    public FrameworkElement ShowPage(string key)
    {
        if (!_pages.TryGetValue(key, out var selected))
        {
            selected = _create(key);
            selected.Visibility = Visibility.Collapsed;
            _pages.Add(key, selected);
            Children.Add(selected);
        }

        foreach (UIElement page in Children)
        {
            page.Visibility = ReferenceEquals(page, selected) ? Visibility.Visible : Visibility.Collapsed;
        }

        return selected;
    }

    private static FrameworkElement CreatePage(string key)
    {
        FrameworkElement page = key switch
        {
            "general" => new GeneralPage(),
            "taskbar" => new TaskbarPage(),
            "orb" => new OrbPage(),
            "effects" => new EffectsPage(),
            "cost" => new CostPage(),
            "traffic" => new TrafficPage(),
            "report" => new ReportPage(),
            "sources" => new SourcesPage(),
            "about" => new AboutPage(),
            _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown settings page."),
        };
        if (key is "cost" or "traffic" or "report")
        {
            page.SetBinding(DataContextProperty, new Binding(key switch
            {
                "cost" => nameof(SettingsViewModel.Cost),
                "traffic" => nameof(SettingsViewModel.Traffic),
                _ => nameof(SettingsViewModel.Report),
            }));
        }

        return page;
    }
}
