using System.Windows;
using System.Windows.Controls;
using AgentSpeed.Presentation.Localization;
using AgentSpeed.Presentation.Settings;
using AgentSpeed.Theme;

namespace AgentSpeed.Menus;

/// <summary>Actions the context menu can trigger.</summary>
internal interface IAppCommands
{
    void ToggleDashboardFromMenu();

    void UpdateSettings(Func<AppSettings, AppSettings> change);

    void OpenSettings(string? page = null);

    void Quit();
}

/// <summary>The shared Fluent context menu for the orb, the tray icon and the taskbar widget (DESIGN section 2).</summary>
internal static class AppMenu
{
    public static ContextMenu Build(IAppCommands commands, AppSettings settings, Localizer loc)
    {
        var menu = new ContextMenu();
        menu.SetResourceReference(Control.FontFamilyProperty, AppFonts.UiFontKey);
        menu.Language = AppFonts.XmlLanguageFor(loc.LanguageCode);

        var dashboard = Item(loc["menu.dashboard"], "", commands.ToggleDashboardFromMenu);
        dashboard.FontWeight = FontWeights.SemiBold;
        menu.Items.Add(dashboard);
        menu.Items.Add(new Separator());

        var size = new MenuItem { Header = loc["menu.size"] };
        var presets = AppSettings.OrbSizePresets;
        var nearest = presets.OrderBy(p => Math.Abs(p - settings.OrbSize)).First();
        string[] sizeKeys = ["menu.size.small", "menu.size.medium", "menu.size.large"];
        for (var i = 0; i < presets.Count; i++)
        {
            var preset = presets[i];
            size.Items.Add(Check(loc[sizeKeys[i]], Math.Abs(preset - nearest) < 0.5 && Math.Abs(preset - settings.OrbSize) < 8,
                () => commands.UpdateSettings(s => s with { OrbSize = preset })));
        }

        size.IsEnabled = settings.ShowOrb;
        menu.Items.Add(size);

        var effects = new MenuItem { Header = loc["menu.effects"] };
        (EffectsIntensity Level, string Key)[] levels =
        [
            (EffectsIntensity.Off, "effects.off"),
            (EffectsIntensity.Soft, "effects.soft"),
            (EffectsIntensity.Standard, "effects.standard"),
            (EffectsIntensity.Max, "effects.max"),
        ];
        foreach (var (level, key) in levels)
        {
            effects.Items.Add(Check(loc[key], settings.Effects == level, () => commands.UpdateSettings(s => s with { Effects = level })));
        }

        menu.Items.Add(effects);
        menu.Items.Add(Check(loc["menu.alwaysOnTop"], settings.OrbAlwaysOnTop,
            () => commands.UpdateSettings(s => s with { OrbAlwaysOnTop = !s.OrbAlwaysOnTop }), settings.ShowOrb));
        menu.Items.Add(Check(loc["menu.clickThrough"], settings.OrbClickThrough,
            () => commands.UpdateSettings(s => s with { OrbClickThrough = !s.OrbClickThrough }), settings.ShowOrb));
        menu.Items.Add(Item(loc[settings.ShowOrb ? "menu.hideOrb" : "menu.showOrb"], null,
            () => commands.UpdateSettings(s => s with { ShowOrb = !s.ShowOrb })));
        menu.Items.Add(Check(loc["settings.bar.show"], settings.ShowOnTaskbar,
            () => commands.UpdateSettings(s => s with { ShowOnTaskbar = !s.ShowOnTaskbar })));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item(loc["menu.settings"], "", () => commands.OpenSettings()));
        menu.Items.Add(Item(loc["menu.about"], "", () => commands.OpenSettings("about")));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item(loc["menu.quit"], "", commands.Quit));
        return menu;
    }

    private static MenuItem Item(string header, string? glyph, Action action)
    {
        var item = new MenuItem { Header = header };
        if (glyph is not null)
        {
            var icon = new TextBlock { Text = glyph, FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
            icon.SetResourceReference(TextBlock.FontFamilyProperty, AppFonts.SymbolFontKey);
            item.Icon = icon;
        }

        item.Click += (_, _) => action();
        return item;
    }

    private static MenuItem Check(string header, bool isChecked, Action action, bool enabled = true)
    {
        var item = new MenuItem { Header = header, IsCheckable = true, IsChecked = isChecked, IsEnabled = enabled };
        item.Click += (_, _) => action();
        return item;
    }
}
