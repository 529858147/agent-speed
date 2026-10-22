using AgentSpeed.Core;
using AgentSpeed.Presentation.Localization;
using AgentSpeed.Presentation.Orb;
using AgentSpeed.Presentation.Settings;
using AgentSpeed.Presentation.Settings.Orbs;
using static AgentSpeed.Presentation.Menus.MenuParts;

namespace AgentSpeed.Presentation.Menus;

/// <summary>
/// The context menu of one floating orb (DESIGN 2b, Context menu, items 1–9). Item 3 is followed by
/// <c>orbs.showName</c> (the orb's name plate).
/// </summary>
public static class OrbMenuModel
{
    /// <summary>A size within this distance of its nearest preset checks that preset.</summary>
    public const double PresetTolerance = 8;

    /// <summary>The menu of <paramref name="orbId"/>; null when no such orb exists.</summary>
    /// <param name="sources">Source availability (split, the sources' secondary text and faded dots).</param>
    public static MenuLevel? Build(string orbId, AppSettings settings, SourceAvailability sources, Localizer localizer)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(localizer);
        var document = settings.Orbs ?? OrbsDocument.Fresh();
        if (document.Find(orbId) is not { } orb)
        {
            return null;
        }

        var id = orb.Id;
        var limit = localizer.Format("orbs.limit", OrbsDocument.MaxOrbs);
        var canAdd = document.CanAdd;
        var canSplit = OrbEditing.CanSplit(document, id, sources.Available);
        var splitTip = canSplit ? null : !canAdd ? limit : orb.Sources.TracksAll ? localizer["orbs.split.unavailable"] : null;
        var lastVisible = document.Orbs.Count(o => o.Visible) <= 1;
        var hide = lastVisible
            ? new MenuRow(localizer["menu.hideOrb"]) { Action = new MenuAction.SetShowOrbs(false) }
            : new MenuRow(localizer["menu.orb.hide"]) { Action = new MenuAction.SetOrbVisible(id, false) };

        return new MenuLevel(
        [
            new MenuRow(OrbNaming.DisplayName(orb, localizer)) { Icon = DotsOf(orb, sources), IsHeader = true, IsEnabled = false },
            MenuSeparator.Instance,
            StyleMenu(orb, localizer),
            SourcesMenu(orb, sources, localizer),
            ShowNameToggle(orb, settings, localizer),
            SizeMenu(orb, localizer),
            EffectsMenu(settings.Effects, localizer),
            MenuSeparator.Instance,
            Toggle(localizer["menu.alwaysOnTop"], orb.AlwaysOnTop, new MenuAction.SetOrbAlwaysOnTop(id, !orb.AlwaysOnTop)),
            Toggle(localizer["menu.clickThrough"], orb.ClickThrough, new MenuAction.SetOrbClickThrough(id, !orb.ClickThrough)),
            MenuSeparator.Instance,
            new MenuRow(localizer["orbs.add"]) { Action = new MenuAction.AddOrb(id), IsEnabled = canAdd, ToolTip = canAdd ? null : limit },
            new MenuRow(localizer["menu.orb.duplicate"]) { Action = new MenuAction.DuplicateOrb(id), IsEnabled = canAdd, ToolTip = canAdd ? null : limit },
            new MenuRow(localizer["menu.orb.split"]) { Action = new MenuAction.SplitOrb(id), IsEnabled = canSplit, ToolTip = splitTip },
            new MenuRow(localizer["menu.orb.mergeAll"]) { Action = new MenuAction.MergeOrbs(id), IsEnabled = document.CanMerge },
            MenuSeparator.Instance,
            hide,
            new MenuRow(localizer["menu.orb.remove"]) { Action = new MenuAction.RemoveOrb(id), IsEnabled = document.CanRemove },
            MenuSeparator.Instance,
            Command(localizer["menu.orb.settings"], new MenuAction.OpenOrbSettings(id), MenuGlyph.Settings),
            Command(localizer["menu.dashboard"], new MenuAction.OpenDashboard(), MenuGlyph.Dashboard),
            Command(localizer["menu.quit"], new MenuAction.Quit(), MenuGlyph.Quit),
        ]);
    }

    /// <summary>The size preset a size checks (0 small, 1 medium, 2 large): the nearest one within 8; else −1.</summary>
    public static int PresetIndex(double size)
    {
        var presets = AppSettings.OrbSizePresets;
        var best = -1;
        for (var i = 0; i < presets.Count; i++)
        {
            if (best < 0 || Math.Abs(presets[i] - size) < Math.Abs(presets[best] - size))
            {
                best = i;
            }
        }

        return best >= 0 && Math.Abs(presets[best] - size) < PresetTolerance ? best : -1;
    }

    private static MenuRow StyleMenu(OrbDefinition orb, Localizer localizer) =>
        Submenu(localizer["orbs.style"], OrbStyles.All.Select(style =>
            (MenuEntry)Toggle(localizer["style." + OrbStyles.Key(style)], orb.Style == style, new MenuAction.SetOrbStyle(orb.Id, style))));

    /// <summary>
    /// <c>orbs.sources.all</c>, a separator, then every source in source order with its dot and a check when tracked.
    /// Unavailable sources say why and stay selectable; the last tracked source cannot be unchecked.
    /// </summary>
    private static MenuRow SourcesMenu(OrbDefinition orb, SourceAvailability sources, Localizer localizer)
    {
        var rows = new List<MenuEntry>
        {
            Toggle(localizer["orbs.sources.all"], orb.Sources.TracksAll, new MenuAction.SetOrbAllSources(orb.Id)),
            MenuSeparator.Instance,
        };
        foreach (var kind in Enum.GetValues<SourceKind>())
        {
            var tracked = !orb.Sources.TracksAll && orb.Sources.Known.Contains(kind);
            var locked = tracked && !OrbEditing.CanUncheck(orb, kind);
            var state = sources.StateOf(kind);
            rows.Add(Toggle(OrbNaming.SourceName(kind, localizer), tracked, new MenuAction.ToggleOrbSource(orb.Id, kind), !locked) with
            {
                Icon = new MenuIcon.Dots([new SourceDot(kind, state == SourceState.Available)]),
                Detail = state switch
                {
                    SourceState.Disabled => localizer["source.disabled"],
                    SourceState.NotFound => localizer["source.notFound"],
                    _ => null,
                },
                ToolTip = locked ? localizer["orbs.sources.lastOne"] : null,
            });
        }

        return Submenu(localizer["orbs.sources"], rows);
    }
}
