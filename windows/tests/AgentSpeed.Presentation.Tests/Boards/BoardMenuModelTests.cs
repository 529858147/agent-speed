using AgentSpeed.Core;
using AgentSpeed.Presentation.Boards;
using AgentSpeed.Presentation.Localization;
using AgentSpeed.Presentation.Menus;
using AgentSpeed.Presentation.Settings;
using AgentSpeed.Presentation.Settings.Boards;
using AgentSpeed.Presentation.Settings.Orbs;

namespace AgentSpeed.Presentation.Tests.Boards;

public class BoardMenuModelTests
{
    private const string Separator = "—";

    private static readonly Localizer En = new(Localizer.LoadEmbeddedTable(), LanguageCodes.En);

    private static AppSettings With(params BoardDefinition[] boards) => AppSettings.Default with { Boards = new BoardsDocument(boards) };

    private static MenuLevel Menu(AppSettings settings, string id = "b0") => BoardMenuModel.Build(id, settings, SourceAvailability.All, En)!;

    private static MenuRow Row(MenuLevel level, string key) => level.Rows.Single(r => r.Label == En[key]);

    private static string[] Outline(MenuLevel level) => [.. level.Entries.Select(e => e is MenuRow row ? row.Label : Separator)];

    [Fact]
    public void TheBoardMenuFollowsTheDesignOrder()
    {
        var menu = Menu(With(new BoardDefinition("b0")));
        Assert.Equal(
            [
                En["boards.style.neon"] + " · " + En["orbs.sources.all"], Separator,
                En["boards.style"], En["orbs.sources"], En["boards.readouts"], En["menu.size"], En["menu.effects"], Separator,
                En["menu.alwaysOnTop"], En["menu.clickThrough"], Separator,
                En["boards.add"], En["boards.remove"], Separator,
                En["menu.dashboard"], En["boards.settings"],
            ],
            Outline(menu));
        Assert.True(menu.Rows.First().IsHeader);
    }

    [Fact]
    public void TheReadoutsSubmenuChecksEachSwitchAndTogglesIt()
    {
        var board = new BoardDefinition("b0") { Readouts = BoardReadouts.All with { Thinking = false } };
        var items = Row(Menu(With(board)), "boards.readouts").Submenu!.Rows.ToArray();
        Assert.Equal(
            [En["boards.readouts.intel"], En["boards.readouts.thinking"], En["boards.readouts.cost"]],
            items.Select(r => r.Label));
        Assert.Equal([true, false, true], items.Select(r => r.IsChecked));
        Assert.Equal(new MenuAction.SetBoardReadout("b0", BoardReadout.Intelligence, false), items[0].Action);
        Assert.Equal(new MenuAction.SetBoardReadout("b0", BoardReadout.Thinking, true), items[1].Action);
        Assert.Equal(new MenuAction.SetBoardReadout("b0", BoardReadout.Cost, false), items[2].Action);
    }

    [Fact]
    public void TheHeaderNamesTheStyleAndTheSources()
    {
        var board = new BoardDefinition("b0") { Style = BoardStyle.Timing, Sources = OrbSources.Of(SourceKind.Codex) };
        var header = Menu(With(board)).Rows.First();
        Assert.Equal(En["boards.style.timing"] + " · " + OrbNaming.SourceName(SourceKind.Codex, En), header.Label);
        Assert.IsType<MenuIcon.Dots>(header.Icon);
    }

    [Fact]
    public void TheSubmenusCheckTheBoardsChoices()
    {
        var board = new BoardDefinition("b0") { Style = BoardStyle.Glass, Size = BoardSize.L, Sources = OrbSources.Of(SourceKind.Codex) };
        var menu = Menu(With(board) with { Effects = EffectsIntensity.Max });

        var styles = Row(menu, "boards.style").Submenu!.Rows.ToArray();
        Assert.Equal([En["boards.style.glass"], En["boards.style.timing"], En["boards.style.neon"]], styles.Select(r => r.Label));
        Assert.Equal([true, false, false], styles.Select(r => r.IsChecked));
        Assert.Equal(new MenuAction.SetBoardStyle("b0", BoardStyle.Neon), styles[2].Action);

        var sizes = Row(menu, "menu.size").Submenu!.Rows.ToArray();
        Assert.Equal([false, false, true], sizes.Select(r => r.IsChecked));
        Assert.Equal(new MenuAction.SetBoardSize("b0", BoardSize.S), sizes[0].Action);

        var sources = Row(menu, "orbs.sources").Submenu!.Rows.ToArray();
        Assert.Equal(new MenuAction.SetBoardAllSources("b0"), sources[0].Action);
        var codex = sources.Single(r => r.Label == OrbNaming.SourceName(SourceKind.Codex, En));
        Assert.True(codex.IsChecked);
        Assert.False(codex.IsEnabled);
        Assert.Equal(En["orbs.sources.lastOne"], codex.ToolTip);
        Assert.Equal(new MenuAction.ToggleBoardSource("b0", SourceKind.ClaudeCode),
            sources.Single(r => r.Label == OrbNaming.SourceName(SourceKind.ClaudeCode, En)).Action);

        Assert.True(Row(menu, "menu.effects").Submenu!.Rows.Single(r => r.Label == En["effects.max"]).IsChecked);
    }

    [Fact]
    public void TogglesAndCommandsActOnTheBoard()
    {
        var menu = Menu(With(new BoardDefinition("b0") { ClickThrough = true }));
        Assert.Equal(new MenuAction.SetBoardAlwaysOnTop("b0", false), Row(menu, "menu.alwaysOnTop").Action);
        Assert.Equal(new MenuAction.SetBoardClickThrough("b0", false), Row(menu, "menu.clickThrough").Action);
        Assert.True(Row(menu, "menu.clickThrough").IsChecked);
        Assert.Equal(new MenuAction.AddBoard("b0"), Row(menu, "boards.add").Action);
        Assert.Equal(new MenuAction.RemoveBoard("b0"), Row(menu, "boards.remove").Action);
        Assert.Equal(new MenuAction.OpenBoardSettings("b0"), Row(menu, "boards.settings").Action);
        Assert.Equal(new MenuAction.OpenDashboard(), Row(menu, "menu.dashboard").Action);
    }

    [Fact]
    public void AddIsDisabledAtFourBoards()
    {
        var full = With([.. Enumerable.Range(0, 4).Select(i => new BoardDefinition("b" + i))]);
        var add = Row(Menu(full), "boards.add");
        Assert.False(add.IsEnabled);
        Assert.Equal(En.Format("boards.limit", 4), add.ToolTip);
    }

    [Fact]
    public void AnUnknownBoardHasNoMenu() => Assert.Null(BoardMenuModel.Build("zzz", With(), SourceAvailability.All, En));

    [Fact]
    public void TheQuickMenuShowsOrHidesEveryBoardAndAdds()
    {
        var none = QuickMenuModel.BoardRows(BoardsDocument.Empty, En).ToArray();
        Assert.Equal([En["boards.add"]], none.Select(r => r.Label));
        Assert.Equal(new MenuAction.AddBoard(null), none[0].Action);

        var shown = QuickMenuModel.BoardRows(new BoardsDocument([new BoardDefinition("a") { Visible = false }, new BoardDefinition("b")]), En).ToArray();
        Assert.Equal([En["boards.hide"], En["boards.add"]], shown.Select(r => r.Label));
        Assert.Equal(new MenuAction.SetShowBoards(false), shown[0].Action);

        var hidden = QuickMenuModel.BoardRows(new BoardsDocument([new BoardDefinition("a") { Visible = false }]), En).ToArray();
        Assert.Equal(new MenuAction.SetShowBoards(true), hidden[0].Action);
        Assert.Equal(En["boards.show"], hidden[0].Label);

        var quick = QuickMenuModel.Build(With(new BoardDefinition("a")), SourceAvailability.All, En);
        Assert.Contains(quick.Rows, r => r.Label == En["boards.hide"]);
    }

    [Fact]
    public void TheOrbMenuAddsABoardNextToTheOrb()
    {
        var settings = AppSettings.Default;
        var orbId = settings.Orbs.Orbs[0].Id;
        var menu = OrbMenuModel.Build(orbId, settings, SourceAvailability.All, En)!;
        Assert.Equal(new MenuAction.AddBoard(orbId), Row(menu, "boards.add").Action);
    }
}
