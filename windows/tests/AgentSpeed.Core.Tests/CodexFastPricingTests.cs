using AgentSpeed.Core.Engine;
using AgentSpeed.Core.History;
using AgentSpeed.Core.IO.Sqlite;
using AgentSpeed.Core.Monitoring;
using AgentSpeed.Core.Parsing;
using AgentSpeed.Core.Pricing;
using static AgentSpeed.Core.Tests.CodexLines;

namespace AgentSpeed.Core.Tests;

public sealed class CodexFastPricingTests
{
    private const long DayStart = 1_792_454_400_000;

    [Fact]
    public void LiveAndHistoryCostsUseTheSelectedTierForEachResponse()
    {
        var parser = new CodexParser("/fixture/a.jsonl", new Deduper());
        var sink = new RecordingSink();
        Feed.Lines(parser, sink,
            SessionMeta(T0),
            TurnContext(T0 + 100, "gpt-6.1-terra"),
            Settings(T0 + 200, "priority"),
            TaskStarted(T0 + 300),
            UsageRecord(T0 + 1300, "fast", Usage(1000, 800, 200, 0)),
            Settings(T0 + 1400, "default"),
            TaskStarted(T0 + 1500),
            UsageRecord(T0 + 2500, "standard", Usage(1000, 800, 200, 0)));

        var responses = sink.Latest;
        Assert.Equal(2, responses.Count);
        Assert.All(responses, response => Assert.False(response.Fast));
        Assert.Equal(["fast", "standard"], responses.Select(r => r.Modes.Attribute(ExecutionModeKind.ServiceTier).Mode));
        Assert.Equal(2480, PriceBook.Bundled.Cost(responses[0]));
        Assert.Equal(1240, PriceBook.Bundled.Cost(responses[1]));

        var engine = new SpeedEngine(DayStart, new PriceBook(), TimeZoneInfo.Utc);
        foreach (var response in responses)
        {
            engine.Upsert(response, isCompletion: true, seeded: true);
        }

        var frame = engine.Compute(T0 + 3000, DayStart, observeLive: true);
        Assert.Equal(new CostTally(3720), frame.TodayCost);
        Assert.Equal(new CostTally(3720), frame.SourceTodayCost[(int)SourceKind.Codex]);
        var days = DayAggregate.Contributions(responses, SourceKind.Codex, new DayLabeler(TimeZoneInfo.Utc), new PriceBook());
        Assert.Equal(new CostTally(3720), Assert.Single(days).Value.Cost);
    }

    [Theory]
    [InlineData("priority", "openai", 2480)]
    [InlineData("fast", "openai", 2480)]
    [InlineData("priority", "azure", 2480)]
    [InlineData("default", "openai", 1240)]
    [InlineData("priority", "my-router", 1240)]
    [InlineData("ultrafast", "openai", 1240)]
    public void OnlyVerifiedFastSelectionsUseFastPrices(string tier, string provider, long expected)
    {
        var parser = new CodexParser("/fixture/a.jsonl", new Deduper());
        var sink = new RecordingSink();
        Feed.Lines(parser, sink, SessionMeta(T0), TurnContext(T0 + 100, "gpt-6.1-terra"),
            Settings(T0 + 200, tier, provider), UsageRecord(T0 + 1200, "r", Usage(1000, 800, 200, 0)));

        Assert.Equal(expected, PriceBook.Bundled.Cost(Assert.Single(sink.Latest)));
    }

    [Fact]
    public void MissingFastPriceIsNotReplacedWithTheStandardPrice()
    {
        var parser = new CodexParser("/fixture/a.jsonl", new Deduper());
        var sink = new RecordingSink();
        Feed.Lines(parser, sink, SessionMeta(T0), TurnContext(T0 + 100, "gpt-5.4-nano"),
            Settings(T0 + 200, "priority"), UsageRecord(T0 + 1200, "r", Usage(1000, 0, 200, 0)));

        var response = Assert.Single(sink.Latest);
        Assert.Null(PriceBook.Bundled.Cost(response));
        var engine = new SpeedEngine(DayStart, new PriceBook(), TimeZoneInfo.Utc);
        engine.Upsert(response, isCompletion: true, seeded: true);
        Assert.Equal(new CostTally(UnpricedRequests: 1), engine.Compute(T0 + 2000, DayStart, observeLive: true).TodayCost);
    }

    [Fact]
    public void ConfiguredFastWithoutASelectionDoesNotChangeTheEstimate()
    {
        var response = new ParsedResponse("r", SourceKind.Codex, "agent", "gpt-6.1-terra", null, T0,
            new TokenCounts(Input: 1000, CachedInput: 800, Output: 200))
        {
            Modes = ExecutionModeNative.OpenAiTier("priority", ExecutionModeEvidence.Configured, ExecutionModeScope.Configuration),
        };

        Assert.Equal(1240, PriceBook.Bundled.Cost(response));
    }

    [Fact]
    public void ExistingHistoryIsRepricedWhenTheAnalysisVersionChanges()
    {
        using var temp = new TempDirectory();
        temp.WriteFile("codex/sessions/a.jsonl", string.Join('\n', SessionMeta(T0),
            TurnContext(T0 + 100, "gpt-6.1-terra"), Settings(T0 + 200, "priority"),
            TaskStarted(T0 + 300), UsageRecord(T0 + 1300, "fast", Usage(1000, 800, 200, 0))) + "\n");
        var locations = new SourceLocations(temp.Combine("codex", "sessions"), "", "", "", []);
        var enabled = new HashSet<SourceKind> { SourceKind.Codex };
        using var store = HistoryStore.Open(temp.Combine("history.sqlite"));
        var old = new HistoryAnalyzer(store, new HistoryOptions { Version = 5 });
        Assert.Equal(1, old.Run(locations, enabled, TimeZoneInfo.Utc, CancellationToken.None).FilesAnalyzed);
        var originalTokens = Assert.Single(old.Days()).Totals;

        using (var connection = SqliteConnection.Open(store.Path, SqliteOpenMode.ReadWriteCreate))
        {
            connection.Execute("UPDATE contributions SET cost_micro_usd = 1240; UPDATE contribution_models SET cost_micro_usd = 1240;");
        }

        var before = new HistoryAnalyzer(store, new HistoryOptions { Version = 5 });
        Assert.Equal(new CostTally(1240), Assert.Single(before.Days()).Cost);
        var updated = new HistoryAnalyzer(store);
        Assert.Equal(1, updated.Run(locations, enabled, TimeZoneInfo.Utc, CancellationToken.None).FilesAnalyzed);
        var day = Assert.Single(updated.Days());
        Assert.Equal(originalTokens, day.Totals);
        Assert.Equal(new CostTally(2480), day.Cost);
        Assert.Equal(new CostTally(2480), Assert.Single(day.Models).Cost);
        Assert.Equal(new CostTally(2480), Assert.Single(new HistoryAnalyzer(store).Days()).Cost);
    }

    private static string Settings(long ms, string tier, string provider = "openai")
    {
        var settings = $$"""{"service_tier":"{{tier}}","model_provider_id":"{{provider}}"}""";
        return Envelope(ms, "event_msg", $$"""{"type":"thread_settings_applied","thread_id":"thread-1","thread_settings":{{settings}}}""");
    }
}
