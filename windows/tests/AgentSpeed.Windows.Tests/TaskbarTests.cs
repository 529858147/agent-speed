using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using AgentSpeed.Core;
using AgentSpeed.Interop;
using AgentSpeed.Presentation.Cost;
using AgentSpeed.Presentation.Formatting;
using AgentSpeed.Presentation.Heat;
using AgentSpeed.Presentation.Localization;
using AgentSpeed.Presentation.Orb;
using AgentSpeed.Presentation.Settings;
using AgentSpeed.Taskbar;

namespace AgentSpeed.Windows.Tests;

public sealed class TaskbarTests
{
    [Fact]
    public void NumericReadoutIsCompactAndTabularAtEveryScale() => OnSta(() =>
    {
        foreach (var language in LanguageCodes.All)
        foreach (var scale in new[] { 1, 1.25, 1.5, 2 })
        foreach (var dark in new[] { true, false })
        {
            var loc = new Localizer(Localizer.LoadEmbeddedTable(), language);
            var renderer = new TaskbarRenderer();
            foreach (var value in new[] { 0, 9, 10, 99, 100, 999, 1000, 1001 })
            {
                var state = State(loc, value, dark);
                Render(renderer, state, scale, vertical: false);
            }

            foreach (var group in new[] { new[] { 10, 11, 71, 99 }, new[] { 100, 101, 199, 999 }, new[] { 1000, 1234, 2345 } })
            {
                var widths = group.Select(value => renderer.MeasureLength(State(loc, value, dark), scale, vertical: false));
                Assert.Single(widths.Distinct());
            }

            var normal = State(loc, 71, dark);
            Assert.InRange(renderer.MeasureLength(normal, scale, vertical: false) / scale, 100, 190);
            Assert.True(renderer.PrimaryNumberWidth(normal, scale) < renderer.PrimaryNumberWidth(State(loc, 999900, dark), scale));
            var largeAmount = normal with { Secondary = normal.Secondary! with { Today = normal.Secondary.Today with { Number = "≈US$999.99" } } };
            Assert.True(renderer.SecondaryNumberWidth(normal, scale) < renderer.SecondaryNumberWidth(largeAmount, scale));
        }
    });

    [Fact]
    public void RenderSupportsVerticalTaskbarsGlyphOnlyAndHighContrast() => OnSta(() =>
    {
        var loc = new Localizer(Localizer.LoadEmbeddedTable(), LanguageCodes.ZhHans);
        foreach (var mode in Enum.GetValues<BarDisplayMode>())
        foreach (var scale in new[] { 1, 1.25, 1.5, 2 })
        {
            var state = State(loc, 71, false) with { Mode = mode, HighContrast = true, ReduceMotion = true };
            Render(new TaskbarRenderer(), state, scale, vertical: true);
            Render(new TaskbarRenderer(), state, scale, vertical: false);
        }
    });

    [Fact]
    public void HiddenTooltipIgnoresUpdatesAndEqualVisibleDetailsDoNotRewriteText() => OnSta(() =>
    {
        var loc = new Localizer(Localizer.LoadEmbeddedTable(), LanguageCodes.ZhHans);
        var snapshot = SpeedSnapshot.Empty with { State = MonitorState.Running, Live = new Rate(71, 71), GeneratingCount = 1 };
        var details = TaskbarDetails.From(snapshot, AppSettings.Default, new CostDisplay(loc));
        var tooltip = new HoverTooltip();
        try
        {
            Assert.False(tooltip.SetDetails(details));
            Assert.Equal(0, tooltip.ContentUpdates);
            tooltip.Show();
            Assert.True(tooltip.SetDetails(details));
            var updates = tooltip.ContentUpdates;
            Assert.False(tooltip.SetDetails(TaskbarDetails.From(snapshot, AppSettings.Default, new CostDisplay(loc))));
            Assert.Equal(updates, tooltip.ContentUpdates);
            Assert.True(tooltip.SetDetails(details with { Measurement = "近 3 秒 · 含估算" }));
            tooltip.HideTip();
            updates = tooltip.ContentUpdates;
            Assert.False(tooltip.SetDetails(details));
            Assert.Equal(updates, tooltip.ContentUpdates);
        }
        finally { tooltip.Close(); }
    });

    [Fact]
    public void AutoHideDetectionUsesTheTaskbarsMonitorAndDpi()
    {
        foreach (var dpi in new uint[] { 96, 120, 144, 192 })
        {
            var bounds = new RECT(-1920, -1080, 0, 0);
            var visible = new TaskbarGeometry(0, 0, new RECT(-1920, -48, 0, 0), default, dpi, false);
            Assert.False(TaskbarLocator.IsAutoHidden(visible, bounds));
            Assert.True(TaskbarLocator.IsAutoHidden(visible with { TaskbarRect = new RECT(-1920, -2, 0, 46) }, bounds));
            var vertical = visible with { Vertical = true, TaskbarRect = new RECT(-1920, -1080, -1872, 0) };
            Assert.False(TaskbarLocator.IsAutoHidden(vertical, bounds));
            Assert.True(TaskbarLocator.IsAutoHidden(vertical with { TaskbarRect = new RECT(-1966, -1080, -1918, 0) }, bounds));
        }
    }

    private static TaskbarState State(Localizer loc, double value, bool dark) => TaskbarState.Initial with
    {
        Enabled = true, Value = value, Tier = value > 0 ? SpeedTier.Cruise : SpeedTier.Idle,
        TaskbarDark = dark, UnitText = loc["unit.tokS"],
        Secondary = new BarSecondary(new BarMoneyLine("", "", "", true),
            new BarMoneyLine(loc["bar.money.today"], "≈US$28.06", "", false), "today") { Compact = true },
    };

    private static void Render(TaskbarRenderer renderer, TaskbarState state, double scale, bool vertical)
    {
        var length = renderer.MeasureLength(state, scale, vertical);
        var width = vertical ? (int)Math.Ceiling(64 * scale) : length;
        var height = vertical ? length : (int)Math.Ceiling(48 * scale);
        var scene = new BarScene { ReduceMotion = true };
        scene.SetTarget(state.Value, state.Tier);
        scene.Snap();
        var bytes = new byte[width * height * 4];
        var pinned = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try { renderer.Render(state, scene, width, height, scale, vertical, false, false, pinned.AddrOfPinnedObject(), bytes.Length); }
        finally { pinned.Free(); }
        // Secondary/idle text intentionally has lower opacity; distinguish it from the alpha-1 hit-test surface.
        var maximumAlpha = bytes.Where((_, i) => i % 4 == 3).Max();
        Assert.True(maximumAlpha > 32, $"No visible content at scale {scale}, vertical={vertical}, alpha={maximumAlpha}.");
    }

    private static void OnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception error) { failure = error; } }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(60)), "Taskbar test did not finish.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
