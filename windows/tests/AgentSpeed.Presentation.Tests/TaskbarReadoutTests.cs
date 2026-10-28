using AgentSpeed.Core;
using AgentSpeed.Presentation.Cost;
using AgentSpeed.Presentation.Formatting;
using AgentSpeed.Presentation.Layout;
using AgentSpeed.Presentation.Localization;
using AgentSpeed.Presentation.Settings;

namespace AgentSpeed.Presentation.Tests;

public sealed class TaskbarReadoutTests
{
    private static readonly Localizer Loc = new(Localizer.LoadEmbeddedTable(), LanguageCodes.ZhHans);
    private static readonly AppSettings Settings = AppSettings.Default;
    private static CostDisplay Display(CostSettings? settings = null) => new(Loc) { Settings = settings ?? CostSettings.Default };
    private static SpeedSnapshot Running => SpeedSnapshot.Empty with { State = MonitorState.Running };
    private static SourceStatus Source(SourceKind source, double output, long amount = 28_060_000, int unpriced = 0) =>
        new(source, true, "", true, 1, new Rate(output, output * 2), new TokenTotals(Requests: 2, Output: 100), 1)
        { TodayCost = new CostTally(amount, unpriced), LiveCost = new LiveCost(9800, output) };

    [Theory]
    [InlineData(0, 0, "0", false, TaskbarActivity.Idle)]
    [InlineData(0, 1, "工具中", true, TaskbarActivity.Tools)]
    [InlineData(1, 0, "等待响应", true, TaskbarActivity.Waiting)]
    [InlineData(1, 1, "等待响应", true, TaskbarActivity.Waiting)]
    public void ZeroSpeedUsesKnownActivity(int generating, int tools, string value, bool hideUnit, TaskbarActivity activity)
    {
        var result = TaskbarReadout.From(Running with { GeneratingCount = generating, ToolsCount = tools }, SpeedMetric.Output, Loc);
        Assert.Equal(value, result.Value);
        Assert.Equal(hideUnit, result.HideUnit);
        Assert.Equal(activity, result.Activity);
        Assert.True(result.Muted);
    }

    [Fact]
    public void PositiveSpeedSurvivesToolActivityAndPartialReadErrors()
    {
        var snapshot = Running with { Live = new Rate(71, 142), ToolsCount = 1, Warnings = [new MonitorWarning.UnreadableFiles(1)] };
        var result = TaskbarReadout.From(snapshot, SpeedMetric.Output, Loc);
        Assert.Equal("71", result.Value);
        Assert.False(result.HideUnit);
        Assert.False(result.Muted);
        Assert.Contains("1", TaskbarDetails.From(snapshot, Settings, Display()).Notice);
    }

    [Fact]
    public void MissingSourcesAndReadFailuresDoNotMasqueradeAsZero()
    {
        Assert.Equal("–", TaskbarReadout.From(Running with { State = MonitorState.NoSources }, SpeedMetric.Output, Loc).Value);
        var failure = Running with { Warnings = [new MonitorWarning.UnreadableFiles(1)] };
        Assert.Equal("–", TaskbarReadout.From(failure, SpeedMetric.Output, Loc).Value);
        var details = TaskbarDetails.From(failure with { Sources = [Source(SourceKind.Codex, 0)] }, Settings, Display());
        Assert.NotEmpty(details.Notice);
        Assert.Equal("–", details.Live);
        Assert.Equal("US$28.06", details.Today);
        Assert.Equal(TaskbarActivity.Reading, TaskbarReadout.From(SpeedSnapshot.Empty, SpeedMetric.Output, Loc).Activity);
    }

    [Fact]
    public void EstimationNoticeAppearsAndDisappearsWithTheSnapshot()
    {
        var snapshot = Running with { Live = new Rate(71, 71), LiveEstimatedShare = 0.5 };
        Assert.Contains("含估算", TaskbarDetails.From(snapshot, Settings, Display()).Measurement);
        Assert.DoesNotContain("含估算", TaskbarDetails.From(snapshot with { LiveEstimatedShare = 0 }, Settings, Display()).Measurement);
    }

    [Fact]
    public void TotalMetricDetectsInputEstimatesEvenWhenOutputEstimateIsZero()
    {
        var source = Source(SourceKind.Codex, 0) with { LiveEstimated = new Rate(0, 15) };
        var snapshot = Running with { Live = new Rate(0, 15), Sources = [source] };
        var details = TaskbarDetails.From(snapshot, Settings with { Metric = SpeedMetric.Total }, Display());
        Assert.Contains("15", details.Primary);
        Assert.Contains("输入＋输出", details.Primary);
        Assert.Contains("含估算", details.Measurement);
    }

    [Fact]
    public void ExcludingASubscriptionChangesMoneyAndScopeButKeepsItsSpeedContribution()
    {
        var snapshot = Running with { Live = new Rate(71, 142), Sources = [Source(SourceKind.Codex, 54, 4_000_000), Source(SourceKind.ClaudeCode, 17, 24_060_000)] };
        var cost = CostSettings.Default.WithSubscription(SourceKind.ClaudeCode, true);
        var excluded = TaskbarDetails.From(snapshot, Settings with { Cost = cost }, Display(cost));
        Assert.Equal("US$4.00", excluded.Today);
        Assert.Equal("US$0.0098/s", excluded.Live);
        Assert.Equal(2, excluded.Sources.Count);
        Assert.Contains("Codex", excluded.CostScope);
        Assert.DoesNotContain("Claude Code", excluded.CostScope);
        Assert.Contains("未计入", excluded.Subscriptions);
        Assert.Contains("Claude Code", excluded.Subscriptions);
        var included = cost with { IncludeSubscriptions = true };
        var all = TaskbarDetails.From(snapshot, Settings with { Cost = included }, Display(included));
        Assert.Equal("US$28.06", all.Today);
        Assert.Contains("Claude Code", all.CostScope);
        Assert.Contains("已计入", all.Subscriptions);
    }

    [Fact]
    public void SourceContributionsFollowTheSelectedMetricAndIgnoreDisabledSources()
    {
        var snapshot = Running with { Sources = [Source(SourceKind.Codex, 54), Source(SourceKind.ClaudeCode, 17) with { Enabled = false }] };
        var details = TaskbarDetails.From(snapshot, Settings with { Metric = SpeedMetric.Total }, Display());
        Assert.Single(details.Sources);
        Assert.Equal("108 tok/s", details.Sources[0].Value);
    }

    [Fact]
    public void CompactMoneyPreservesUnknownAndLowerBoundSemantics()
    {
        var display = Display();
        var priced = Running with { Sources = [Source(SourceKind.Codex, 71)] };
        var compact = BarSecondary.From(Settings, priced, display)!;
        Assert.True(compact.Compact);
        Assert.Equal("≈US$28.06", compact.Today.Number);
        Assert.Empty(compact.Live.Number);
        Assert.Equal("–", BarSecondary.From(Settings, priced with { Sources = [Source(SourceKind.Codex, 71, 0, 2)] }, display)!.Today.Number);
        Assert.Equal("≥US$28.06", BarSecondary.From(Settings, priced with { Sources = [Source(SourceKind.Codex, 71, 28_060_000, 1)] }, display)!.Today.Number);
        Assert.Null(BarSecondary.From(Settings with { BarMode = BarDisplayMode.GlyphOnly }, priced, display));
    }

    [Fact]
    public void CompactSettingDefaultsOnAndExplicitOptOutRoundTrips()
    {
        Assert.True(SettingsSerializer.Deserialize("{}").BarCompact);
        var settings = Settings with { BarCompact = false, BarSecondary = true };
        var restored = SettingsSerializer.Deserialize(SettingsSerializer.Serialize(settings));
        Assert.False(restored.BarCompact);
        Assert.True(restored.BarSecondary);
    }

    [Fact]
    public void HoverHoldsExpiredShrinkUntilLeaveWhileStillAllowingGrowthAndDpiChanges()
    {
        var width = new WidthHold(6000);
        Assert.Equal(200, width.Resolve(200, 0));
        Assert.Equal(200, width.Resolve(160, 1));
        Assert.Equal(200, width.Resolve(160, 7000, holdShrink: true));
        Assert.Equal(200, width.Peek(160, 7000, holdShrink: true));
        Assert.Equal(160, width.Resolve(160, 7001));
        Assert.Equal(220, width.Resolve(220, 7002, holdShrink: true));
        Assert.Equal(110, width.Resolve(110, 7003, force: true, holdShrink: true));
    }

    [Theory]
    [InlineData(0, 0, 1920, 1032, TaskbarEdge.Bottom)]
    [InlineData(-1920, 0, 1920, 1032, TaskbarEdge.Bottom)]
    [InlineData(1920, -1080, 2560, 1392, TaskbarEdge.Top)]
    [InlineData(-1080, -1920, 1032, 1920, TaskbarEdge.Left)]
    [InlineData(0, 0, 1032, 1920, TaskbarEdge.Right)]
    public void TooltipStaysInsideItsOwnMonitorWorkArea(double x, double y, double width, double height, TaskbarEdge edge)
    {
        var work = new RectD(x, y, width, height);
        var anchor = edge switch
        {
            TaskbarEdge.Top => new RectD(x + width - 200, y - 48, 200, 48),
            TaskbarEdge.Left => new RectD(x - 48, y + height - 200, 48, 200),
            TaskbarEdge.Right => new RectD(x + width, y + height - 200, 48, 200),
            _ => new RectD(x + width - 200, y + height, 200, 48),
        };
        var point = PlacementMath.TaskbarTooltip(anchor, work, edge, 348, 480, 8);
        Assert.InRange(point.X, work.X, work.Right - 348);
        Assert.InRange(point.Y, work.Y, work.Bottom - 480);
    }
}
