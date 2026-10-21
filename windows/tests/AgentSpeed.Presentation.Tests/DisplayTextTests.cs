using AgentSpeed.Core;
using AgentSpeed.Presentation.Formatting;
using AgentSpeed.Presentation.Heat;
using AgentSpeed.Presentation.Localization;

namespace AgentSpeed.Presentation.Tests;

public class DisplayTextTests
{
    private static readonly StringTable Embedded = Localizer.LoadEmbeddedTable();

    private static Localizer For(string language) => new(Embedded, language);

    private static AgentStatus Agent(int i) => new(
        $"/tmp/agent-{i}.jsonl", SourceKind.ClaudeCode, "proj", null, AgentPhase.Generating, false, Rate.Zero, null, DateTimeOffset.UnixEpoch);

    [Fact]
    public void SpeedLineInTraditionalChinese()
    {
        Assert.Equal("128 tok/s · 加速", DisplayText.SpeedLine(128, SpeedTier.Boost, For(LanguageCodes.ZhHant)));
        Assert.Equal("1.28k tok/s · 極限", DisplayText.SpeedLine(1280, SpeedTier.Redline, For(LanguageCodes.ZhHant)));
        Assert.Equal("0 tok/s · 待機", DisplayText.SpeedLine(0, SpeedTier.Idle, For(LanguageCodes.ZhHant)));
    }

    [Fact]
    public void SpeedLineInOtherLanguages()
    {
        Assert.Equal("4.2 tok/s · CRUISE", DisplayText.SpeedLine(4.2, SpeedTier.Cruise, For(LanguageCodes.En)));
        Assert.Equal("200 tok/s · 狂飙", DisplayText.SpeedLine(200, SpeedTier.Overdrive, For(LanguageCodes.ZhHans)));
        Assert.Equal("200 tok/s · 爆速", DisplayText.SpeedLine(200, SpeedTier.Overdrive, For(LanguageCodes.Ja)));
    }

    [Fact]
    public void AccessibilityLabel()
    {
        Assert.Equal("每秒 128 個 token，加速", DisplayText.Accessibility(128, SpeedTier.Boost, For(LanguageCodes.ZhHant)));
        Assert.Equal("每秒 128 个 token，加速", DisplayText.Accessibility(128, SpeedTier.Boost, For(LanguageCodes.ZhHans)));
        Assert.Equal("128 tokens per second, BOOST", DisplayText.Accessibility(128, SpeedTier.Boost, For(LanguageCodes.En)));
        Assert.Equal("毎秒 4.2 トークン、巡航", DisplayText.Accessibility(4.2, SpeedTier.Cruise, For(LanguageCodes.Ja)));
    }

    [Theory]
    [InlineData(SpeedTier.Idle, "IDLE", "待機")]
    [InlineData(SpeedTier.Cruise, "CRUISE", "巡航")]
    [InlineData(SpeedTier.Boost, "BOOST", "加速")]
    [InlineData(SpeedTier.Overdrive, "OVERDRIVE", "狂飆")]
    [InlineData(SpeedTier.Redline, "REDLINE", "極限")]
    public void TierLabelIsUppercaseInEnglishAndUnchangedInChinese(SpeedTier tier, string english, string traditional)
    {
        Assert.Equal(english, DisplayText.TierLabel(tier, For(LanguageCodes.En)));
        Assert.Equal(traditional, DisplayText.TierLabel(tier, For(LanguageCodes.ZhHant)));
        Assert.Equal(traditional, DisplayText.TierName(tier, For(LanguageCodes.ZhHant)));
    }

    [Fact]
    public void TierLabelUppercasesMixedCaseLatinText()
    {
        var table = StringTable.Parse("""{ "tier.boost": { "en": "Boost", "zh-Hant": "加速", "ja": "Boost" } }""");
        Assert.Equal("BOOST", DisplayText.TierLabel(SpeedTier.Boost, new Localizer(table, LanguageCodes.En)));
        Assert.Equal("Boost", DisplayText.TierName(SpeedTier.Boost, new Localizer(table, LanguageCodes.En)));
        Assert.Equal("Boost", DisplayText.TierLabel(SpeedTier.Boost, new Localizer(table, LanguageCodes.Ja)));
        Assert.Equal("加速", DisplayText.TierLabel(SpeedTier.Boost, new Localizer(table, LanguageCodes.ZhHant)));
    }

    [Theory]
    [InlineData(SpeedTier.Idle, "tier.idle")]
    [InlineData(SpeedTier.Cruise, "tier.cruise")]
    [InlineData(SpeedTier.Boost, "tier.boost")]
    [InlineData(SpeedTier.Overdrive, "tier.overdrive")]
    [InlineData(SpeedTier.Redline, "tier.redline")]
    [InlineData((SpeedTier)99, "tier.idle")]
    public void TierKeys(SpeedTier tier, string key)
    {
        Assert.Equal(key, DisplayText.TierKey(tier));
        Assert.True(Embedded.Contains(key));
    }

    [Fact]
    public void SourceAndPhaseKeysExist()
    {
        foreach (var source in Enum.GetValues<SourceKind>())
        {
            Assert.True(Embedded.Contains(DisplayText.SourceKey(source)), DisplayText.SourceKey(source));
        }

        foreach (var phase in Enum.GetValues<AgentPhase>())
        {
            Assert.True(Embedded.Contains(DisplayText.PhaseKey(phase)), DisplayText.PhaseKey(phase));
        }

        Assert.Equal("source.claudeCode", DisplayText.SourceKey(SourceKind.ClaudeCode));
        Assert.Equal("phase.tools", DisplayText.PhaseKey(AgentPhase.Tools));
        Assert.Throws<ArgumentOutOfRangeException>(() => DisplayText.SourceKey((SourceKind)99));
    }

    [Theory]
    [InlineData(SourceKind.Codex, "source.codex", "Codex")]
    [InlineData(SourceKind.ClaudeCode, "source.claudeCode", "Claude Code")]
    [InlineData(SourceKind.ClaudeDesktop, "source.claudeDesktop", "Claude Desktop")]
    [InlineData(SourceKind.DeepSeek, "source.deepseek", "DeepSeek")]
    [InlineData(SourceKind.OpenCode, "source.opencode", "OpenCode")]
    [InlineData(SourceKind.Gemini, "source.gemini", "Gemini CLI")]
    public void EverySourceHasItsOwnName(SourceKind source, string key, string english)
    {
        Assert.Equal(key, DisplayText.SourceKey(source));
        Assert.Equal(english, For(LanguageCodes.En)[key]);
    }

    [Fact]
    public void DeepSeekStringsExistInEveryLanguage()
    {
        foreach (var language in LanguageCodes.All)
        {
            Assert.True(Embedded.TryGet("source.deepseek", language, out _), language);
            Assert.True(Embedded.TryGet("settings.sources.dshHint", language, out var hint), language);
            Assert.Contains("dsh", hint, StringComparison.Ordinal);
            Assert.True(Embedded.TryGet("state.noSources", language, out var noSources), language);
            Assert.False(string.IsNullOrWhiteSpace(noSources), language);
        }
    }

    [Theory]
    [InlineData(0, "剛剛", "just now")]
    [InlineData(4.9, "剛剛", "just now")]
    [InlineData(5, "5 秒前", "5s ago")]
    [InlineData(59.9, "59 秒前", "59s ago")]
    [InlineData(60, "1 分鐘前", "1m ago")]
    [InlineData(125, "2 分鐘前", "2m ago")]
    [InlineData(7200, "120 分鐘前", "120m ago")]
    public void TimeAgoThresholds(double seconds, string traditional, string english)
    {
        var elapsed = TimeSpan.FromSeconds(seconds);
        Assert.Equal(traditional, DisplayText.TimeAgo(elapsed, For(LanguageCodes.ZhHant)));
        Assert.Equal(english, DisplayText.TimeAgo(elapsed, For(LanguageCodes.En)));
    }

    [Fact]
    public void TodaySummaryAndAgentContext()
    {
        var snapshot = SpeedSnapshot.Empty with
        {
            Today = new TokenTotals(Output: 1_234_567),
            Agents = [Agent(1), Agent(2), Agent(3)],
            GeneratingCount = 3,
            ToolsCount = 1,
        };

        Assert.Equal("今日 1.23M · 3 個 agent", DisplayText.TodaySummary(snapshot, For(LanguageCodes.ZhHant)));
        Assert.Equal("Today 1.23M · 3 agents", DisplayText.TodaySummary(snapshot, For(LanguageCodes.En)));
        Assert.Equal("生成中 3 · 工具 1", DisplayText.AgentContext(snapshot, For(LanguageCodes.ZhHant)));
        Assert.Equal("3 generating · 1 in tools", DisplayText.AgentContext(snapshot, For(LanguageCodes.En)));
        Assert.Throws<ArgumentNullException>(() => DisplayText.TodaySummary(null!, For(LanguageCodes.En)));
        Assert.Throws<ArgumentNullException>(() => DisplayText.AgentContext(null!, For(LanguageCodes.En)));
    }

    [Fact]
    public void WarningsAreLocalized()
    {
        var zh = For(LanguageCodes.ZhHant);
        var partial = DisplayText.Warning(new MonitorWarning.PartialHistory(), zh);
        var unreadable = DisplayText.Warning(new MonitorWarning.UnreadableFiles(3), zh);
        var malformed = DisplayText.Warning(new MonitorWarning.MalformedLines(12), zh);

        Assert.Equal(zh["warning.partialHistory"], partial);
        Assert.NotEqual("warning.partialHistory", partial);
        Assert.Contains("3", unreadable, StringComparison.Ordinal);
        Assert.DoesNotContain("%d", unreadable, StringComparison.Ordinal);
        Assert.Contains("12", malformed, StringComparison.Ordinal);

        var unfinalized = DisplayText.Warning(new MonitorWarning.UnfinalizedUsage(4), zh);
        Assert.StartsWith("4 ", unfinalized, StringComparison.Ordinal);
        Assert.DoesNotContain("%d", unfinalized, StringComparison.Ordinal);
        Assert.NotEqual("warning.unfinalized", unfinalized);
    }
}
