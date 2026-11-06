using AgentSpeed.Core;
using AgentSpeed.Presentation.History;
using AgentSpeed.Presentation.Localization;

namespace AgentSpeed.Presentation.Tests;

public sealed class ExecutionModePresentationTests
{
    [Fact]
    public void ModeCsvIsIndependentQuotesUnsafeModelsAndUsesTheExistingWeightedAverage()
    {
        var modes = new[]
        {
            new ExecutionModeUsage("=cmd,model", SourceKind.ClaudeCode, ExecutionModeKind.ServiceTier, "fast", "served", "anthropic", 1, 10, 10, 1000),
            new ExecutionModeUsage("=cmd,model", SourceKind.ClaudeCode, ExecutionModeKind.ServiceTier, "fast", "served", "anthropic", 1, 30, 30, 1000),
            new ExecutionModeUsage("=cmd,model", SourceKind.ClaudeCode, ExecutionModeKind.ServiceTier, "unknown", "unknown", null, 1, 50, 0, 0),
        };
        var day = new DaySummary(new(2026, 10, 24), 3, 0, 0, 0, 90, 0, 40, 2000, 0, 1, null, [], []) { Modes = modes };
        var original = HistoryCsv.Build([day with { Modes = [] }], null);
        Assert.Equal(original, HistoryCsv.Build([day], null));
        var csv = ExecutionModeCsv.Build([day]);
        Assert.StartsWith("date,source,model,kind,mode,evidence,provider,requests,output_tokens,measured_output_tokens,measured_ms,avg_speed_tok_s\r\n", csv, StringComparison.Ordinal);
        Assert.Contains("2026-10-24,claudeCode,\"'=cmd,model\",serviceTier,fast,served,anthropic,2,40,40,2000,20.0\r\n", csv, StringComparison.Ordinal);
        Assert.Contains(",unknown,unknown,,1,50,0,0,\r\n", csv, StringComparison.Ordinal);
    }

    [Fact]
    public void ModeCsvBreaksDownEffortAndSpeedLikeEveryOtherKind()
    {
        var day = new DaySummary(new(2026, 10, 24), 2, 0, 0, 0, 30, 0, 30, 3000, 0, 1, null, [], [])
        {
            Modes =
            [
                new("gpt-5", SourceKind.Codex, ExecutionModeKind.Effort, "xhigh", "recorded", null, 2, 30, 30, 3000),
                new("gpt-5", SourceKind.Codex, ExecutionModeKind.Speed, "ultrafast", "selected", "openai", 1, 10, 10, 1000),
                new("gpt-5", SourceKind.Codex, ExecutionModeKind.Speed, "unknown", "unknown", null, 1, 20, 20, 2000),
            ],
        };
        var csv = ExecutionModeCsv.Build([day]);
        Assert.Contains("2026-10-24,codex,gpt-5,effort,xhigh,recorded,,2,30,30,3000,10.0\r\n", csv, StringComparison.Ordinal);
        Assert.Contains("2026-10-24,codex,gpt-5,speed,ultrafast,selected,openai,1,10,10,1000,10.0\r\n", csv, StringComparison.Ordinal);
        Assert.Contains("2026-10-24,codex,gpt-5,speed,unknown,unknown,,1,20,20,2000,10.0\r\n", csv, StringComparison.Ordinal);
    }

    [Fact]
    public void DailyAggregationKeepsSourceAndEvidenceInSeparateGroups()
    {
        var first = new DailyUsage("2026-10-24", SourceKind.ClaudeCode, new TokenTotals(1, 0, 0, 0, 10), 10, 1000, 0, 1, null, [])
        { Modes = [new("m", SourceKind.ClaudeCode, ExecutionModeKind.ServiceTier, "fast", "served", "anthropic", 1, 10, 10, 1000)] };
        var second = first with { Source = SourceKind.Omp, Modes = [new("m", SourceKind.Omp, ExecutionModeKind.ServiceTier, "fast", "recorded", "anthropic", 1, 10, 10, 1000)] };
        var day = Assert.Single(HistoryAggregator.Daily([first, second], null));
        Assert.Equal(2, day.Modes.Count);
        Assert.Equal(day.Requests, day.Modes.Sum(m => m.Requests));
        Assert.Equal(day.AverageSpeed, day.Modes.Sum(m => m.MeasuredOutput) / (day.Modes.Sum(m => m.MeasuredMs) / 1000.0));
    }

    [Fact]
    public void EveryLanguageHasTheEffortAndSpeedLabels()
    {
        foreach (var language in new[] { "en", "zh-Hant", "zh-Hans", "ja" })
        {
            var loc = new Localizer(Localizer.LoadEmbeddedTable(), language);
            foreach (var key in new[]
                     {
                         "mode.exportCsv", "effort.label", "effort.tooltip", "effort.ultra.detail", "effort.ultracode.detail",
                         "speed.label", "speed.tooltip", "speed.value.standard", "modes.unrecorded", "modes.history.title",
                         "modes.history.empty", "profile.title",
                     })
            {
                Assert.NotEqual(key, loc[key]);
            }
        }
    }
}
