using AgentSpeed.Core.History;
using AgentSpeed.Core.IO.Sqlite;
using AgentSpeed.Core.Monitoring;
using AgentSpeed.Core.Parsing;
using static AgentSpeed.Core.Tests.CodexLines;

namespace AgentSpeed.Core.Tests;

public sealed class CodexExecutionModeTests
{
    [Theory]
    [InlineData("priority", "fast", "openai")]
    [InlineData("default", "standard", "openai")]
    [InlineData("ultrafast", "ultrafast", "openai")]
    [InlineData("warp-2", "warp-2", "openai")]
    [InlineData("priority", "fast", "azure")]
    public void AppliedThreadSettingsPublishSelectedSessionEvidence(string tier, string expected, string provider)
    {
        var parser = new CodexParser("/fixture/a.jsonl", new Deduper());
        var sink = new RecordingSink();
        Feed.Lines(parser, sink, SessionMeta(T0),
            Settings(T0 + 100, $$"""{"service_tier":"{{tier}}","model_provider_id":"{{provider}}"}"""));

        var fact = Assert.Single(parser.Modes.Facts);
        Assert.Equal((expected, tier, "openai", ExecutionModeEvidence.Selected, ExecutionModeScope.Session),
            (fact.Value, fact.RawValue, fact.Provider, fact.Evidence, fact.Scope));
        Assert.Equal(AgentPhase.Idle, parser.Phase);
        Assert.Empty(sink.Emissions);

        Feed.Lines(parser, sink, TaskStarted(T0 + 1000), UsageRecord(T0 + 3000, "r", Usage(3, 0, 40, 0)));
        var response = Assert.Single(sink.Latest);
        Assert.Equal(parser.Modes, response.Modes);
        Assert.Equal((T0 + 1000, T0 + 3000, new TokenCounts(Input: 3, Output: 40)),
            (response.StartMs, response.EndMs, response.Tokens));
        Assert.False(response.Fast);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("true")]
    [InlineData("42")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("\"bad mode\"")]
    public void ExplicitNullOrInvalidTierClearsTheSelection(string tier)
    {
        var parser = new CodexParser("/fixture/a.jsonl", new Deduper());
        var sink = new RecordingSink();
        Feed.Lines(parser, sink, Settings(T0, """{"model_provider_id":"openai","service_tier":"priority"}"""),
            Settings(T0 + 1, $$"""{"model_provider_id":"openai","service_tier":{{tier}}}"""));
        Assert.Empty(parser.Modes.Facts);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"model_provider_id\":\"openai\"}")]
    [InlineData("null")]
    public void AbsentTierLeavesTheSessionSelectionUnchanged(string settings)
    {
        var parser = new CodexParser("/fixture/a.jsonl", new Deduper());
        var sink = new RecordingSink();
        Feed.Lines(parser, sink, Settings(T0, """{"model_provider_id":"openai","service_tier":"priority"}"""),
            Settings(T0 + 1, settings),
            Envelope(T0 + 2, "event_msg", """{"type":"unrelated","thread_settings":{"model_provider_id":"openai","service_tier":"default"}}"""));
        Assert.Equal("fast", parser.Modes.Attribute(ExecutionModeKind.ServiceTier).Mode);
    }

    [Theory]
    [InlineData("{\"service_tier\":\"priority\"}")]
    [InlineData("{\"service_tier\":\"priority\",\"model_provider_id\":null}")]
    [InlineData("{\"service_tier\":\"priority\",\"model_provider_id\":\"my-router\"}")]
    public void UnverifiedProviderNeverBecomesOpenAiFast(string settings)
    {
        var parser = new CodexParser("/fixture/a.jsonl", new Deduper());
        var sink = new RecordingSink();
        Feed.Lines(parser, sink, Settings(T0, settings));
        Assert.Empty(parser.Modes.Facts);
    }

    [Fact]
    public void ChangesApplyToSubsequentResponsesWithoutRetimingOrRewritingEarlierOnes()
    {
        var parser = new CodexParser("/fixture/a.jsonl", new Deduper());
        var sink = new RecordingSink();
        Feed.Lines(parser, sink, Settings(T0, """{"model_provider_id":"openai","service_tier":"priority"}"""),
            TaskStarted(T0 + 1000), UsageRecord(T0 + 3000, "r1", Usage(3, 0, 40, 0)),
            TaskStarted(T0 + 4000), Settings(T0 + 4500, """{"model_provider_id":"openai","service_tier":"default"}"""),
            UsageRecord(T0 + 6000, "r2", Usage(3, 0, 40, 0)),
            Settings(T0 + 6500, """{"model_provider_id":"openai","service_tier":null}"""),
            TaskStarted(T0 + 7000), UsageRecord(T0 + 9000, "r3", Usage(3, 0, 40, 0)));

        Assert.Equal(["fast", "standard", "unknown"], sink.Latest.Select(r => r.Modes.Attribute(ExecutionModeKind.ServiceTier).Mode));
        Assert.Equal([T0 + 1000, T0 + 4000, T0 + 7000], sink.Latest.Select(r => r.StartMs));
        Assert.All(sink.Latest, r => Assert.Equal(new TokenCounts(Input: 3, Output: 40), r.Tokens));
        Assert.Equal(3, sink.Emissions.Count);
    }

    [Fact]
    public void CopiedForeignAndParallelSessionSettingsDoNotLeakAndResetClearsThem()
    {
        var deduper = new Deduper();
        var parser = new CodexParser("/fixture/a.jsonl", deduper);
        var other = new CodexParser("/fixture/b.jsonl", deduper);
        var sink = new RecordingSink();
        Feed.Lines(parser, sink, SessionMeta(T0),
            Settings(T0 - 2001, """{"model_provider_id":"openai","service_tier":"priority"}"""),
            Settings(T0 + 1, """{"model_provider_id":"openai","service_tier":"priority"}""", "parent"));
        Assert.Empty(parser.Modes.Facts);
        Assert.Equal(T0, parser.LastLineMs);
        Feed.Lines(parser, sink, Settings(T0 + 2, """{"model_provider_id":"openai","service_tier":"priority"}"""));
        Assert.Equal("fast", parser.Modes.Attribute(ExecutionModeKind.ServiceTier).Mode);
        Assert.Empty(other.Modes.Facts);
        parser.Reset();
        Assert.Empty(parser.Modes.Facts);
        Feed.Lines(parser, sink, SessionMeta(T0), Settings(T0 + 2, """{"model_provider_id":"openai","service_tier":"priority"}"""));
        Assert.Equal("fast", parser.Modes.Attribute(ExecutionModeKind.ServiceTier).Mode);
    }

    [Fact]
    public void OlderHistoryIsReanalysedWithoutChangingUsageTotals()
    {
        using var temp = new TempDirectory();
        temp.WriteFile("codex/sessions/a.jsonl", string.Join('\n', SessionMeta(T0),
            TurnContext(T0 + 50), Settings(T0 + 100, """{"model_provider_id":"openai","service_tier":"priority"}"""),
            TaskStarted(T0 + 1000), UsageRecord(T0 + 3000, "r", Usage(3, 0, 40, 0))) + "\n");
        var locations = new SourceLocations(temp.Combine("codex", "sessions"), "", "", "", []);
        var enabled = new HashSet<SourceKind> { SourceKind.Codex };
        using var store = HistoryStore.Open(temp.Combine("history.sqlite"));
        var old = new HistoryAnalyzer(store, new HistoryOptions { Version = 4 });
        old.Run(locations, enabled, TimeZoneInfo.Utc, CancellationToken.None);
        var totals = Assert.Single(old.Days()).Totals;
        using (var connection = SqliteConnection.Open(store.Path, SqliteOpenMode.ReadWriteCreate))
        {
            connection.Execute("DELETE FROM contribution_modes");
        }

        var current = new HistoryAnalyzer(store);
        var report = current.Run(locations, enabled, TimeZoneInfo.Utc, CancellationToken.None);
        Assert.Equal(1, report.FilesAnalyzed);
        var day = Assert.Single(current.Days());
        Assert.Equal(totals, day.Totals);
        var mode = Assert.Single(day.Modes, m => m.Kind == ExecutionModeKind.ServiceTier);
        Assert.Equal(("fast", "selected", 1L, 40L), (mode.Mode, mode.Evidence, mode.Requests, mode.Output));
    }

    private static string Settings(long ms, string settings, string thread = "thread-1") =>
        Envelope(ms, "event_msg", $$"""{"type":"thread_settings_applied","thread_id":"{{thread}}","thread_settings":{{settings}}}""");
}
