using AgentSpeed.Core.Parsing;

namespace AgentSpeed.Core.Tests;

/// <summary>Reasoning effort and speed per native rules (EXECUTION-MODES, Native rules).</summary>
public sealed class EffortParserTests
{
    private const long T0 = ClaudeLines.T0;

    [Fact]
    public void ClaudePerTurnEffortWinsAndANullFallsBackToTheTopLevelEffort()
    {
        var parser = new ClaudeParser("/fixture/a.jsonl", new Deduper());
        var sink = new RecordingSink();
        Feed.Lines(parser, sink,
            ClaudeLines.UserText(T0, "go"),
            ClaudeLines.Assistant(T0 + 1000, "a", 10, extra: ""","effort":"high","perTurnEffort":"max" """),
            ClaudeLines.UserText(T0 + 2000, "again"),
            ClaudeLines.Assistant(T0 + 3000, "b", 10, extra: ""","effort":"LOW","perTurnEffort":null"""),
            ClaudeLines.Assistant(T0 + 4000, "c", 10, extra: ""","effort":"auto" """));
        Assert.Equal(["max", "low", "unknown"], sink.Latest.Select(r => Effort(r.Modes)));
        Assert.Equal("low", Effort(parser.Modes));
    }

    [Fact]
    public void ClaudeSessionUltracodeLastsUntilItsExitAndThenTheLatestLineEffortReturns()
    {
        var parser = new ClaudeParser("/fixture/a.jsonl", new Deduper());
        var sink = new RecordingSink();
        Feed.Lines(parser, sink,
            ClaudeLines.UserText(T0, "go"),
            ClaudeLines.Assistant(T0 + 1000, "a", 10, extra: ""","effort":"high" """).Replace("\"usage\":{", "\"usage\":{\"speed\":\"fast\",", StringComparison.Ordinal),
            Attachment(T0 + 1500, "ultra_effort_enter"));
        Assert.Equal("ultracode", Effort(parser.Modes));
        Assert.Equal("high", Effort(sink.Latest[0].Modes));
        Assert.Equal("fast", parser.Modes.Attribute(ExecutionModeKind.Speed).Mode);

        Feed.Lines(parser, sink,
            ClaudeLines.UserText(T0 + 2000, "next"),
            ClaudeLines.Assistant(T0 + 3000, "b", 10, extra: ""","effort":"medium" """),
            Attachment(T0 + 3500, "ultra_effort_enter"));
        Assert.Equal("ultracode", Effort(sink.Latest[1].Modes));
        Assert.Equal("ultracode", Effort(parser.Modes));

        Feed.Lines(parser, sink, Attachment(T0 + 4000, "ultra_effort_exit"));
        Assert.Equal("medium", Effort(parser.Modes));
        Assert.Equal("unknown", parser.Modes.Attribute(ExecutionModeKind.Speed).Mode);
    }

    [Fact]
    public void ClaudeWorkflowKeywordLastsForItsTurnAndOnlyARealPromptEndsIt()
    {
        var parser = new ClaudeParser("/fixture/a.jsonl", new Deduper());
        var sink = new RecordingSink();
        Feed.Lines(parser, sink,
            ClaudeLines.UserText(T0, "go"),
            Attachment(T0 + 100, "workflow_keyword_request"),
            ClaudeLines.User(T0 + 200, "[{\"type\":\"text\",\"text\":\"meta\"}]", extra: ",\"isMeta\":true"),
            ClaudeLines.Assistant(T0 + 1000, "a", 10, "\"tool_use\"", extra: ""","effort":"xhigh" """),
            ClaudeLines.User(T0 + 1500, "[{\"type\":\"tool_result\",\"tool_use_id\":\"t\",\"content\":\"ok\"}]"),
            ClaudeLines.Assistant(T0 + 2000, "b", 10, extra: ""","effort":"xhigh" """));
        Assert.Equal(["ultracode", "ultracode"], sink.Latest.Select(r => Effort(r.Modes)));
        Assert.Equal("ultracode", Effort(parser.Modes));

        Feed.Lines(parser, sink, ClaudeLines.User(T0 + 3000, "[{\"type\":\"text\",\"text\":\"next\"}]"));
        Assert.Equal("xhigh", Effort(parser.Modes));
        Feed.Lines(parser, sink, ClaudeLines.Assistant(T0 + 4000, "c", 10, extra: ""","effort":"xhigh" """));
        Assert.Equal("xhigh", Effort(sink.Latest[2].Modes));
    }

    [Fact]
    public void CodexThreadSettingsAreSessionSelectionsAndTurnContextsReplaceTurnFacts()
    {
        var parser = new CodexParser("/fixture/rollout-a.jsonl", new Deduper());
        var sink = new RecordingSink();
        Feed.Lines(parser, sink,
            CodexLines.SessionMeta(T0),
            Settings(T0 + 100, """{"service_tier":"priority","reasoning_effort":"xhigh"}"""));
        Assert.Equal(("xhigh", "selected"), Pair(parser.Modes, ExecutionModeKind.Effort));
        Assert.Equal(("fast", "selected"), Pair(parser.Modes, ExecutionModeKind.Speed));

        Feed.Lines(parser, sink,
            CodexLines.Envelope(T0 + 200, "turn_context", """{"model":"gpt","effort":"high"}"""),
            CodexLines.UserMessage(T0 + 300),
            UsageRecordWithTier(T0 + 1000, "r1", "default"));
        var first = sink.Latest[0].Modes;
        Assert.Equal(("high", "recorded"), Pair(first, ExecutionModeKind.Effort));
        Assert.Equal(("standard", "recorded"), Pair(first, ExecutionModeKind.Speed));
        Assert.Equal(("fast", "selected"), Pair(parser.Modes, ExecutionModeKind.Speed));
        Assert.Equal(("high", "recorded"), Pair(parser.Modes, ExecutionModeKind.Effort));

        Feed.Lines(parser, sink,
            CodexLines.Envelope(T0 + 2000, "turn_context", """{"model":"gpt","collaboration_mode":{"settings":{"reasoning_effort":"none"}}}"""),
            CodexLines.UsageRecord(T0 + 3000, "r2", CodexLines.Usage(1, 0, 10, 0)));
        Assert.Equal(("off", "recorded"), Pair(sink.Latest[1].Modes, ExecutionModeKind.Effort));
        Assert.Equal(("fast", "selected"), Pair(sink.Latest[1].Modes, ExecutionModeKind.Speed));

        Feed.Lines(parser, sink,
            Settings(T0 + 4000, """{"service_tier":"ultrafast"}"""),
            CodexLines.Envelope(T0 + 4100, "turn_context", """{"model":"gpt"}"""));
        Assert.Equal(("xhigh", "selected"), Pair(parser.Modes, ExecutionModeKind.Effort));
        Assert.Equal(("ultrafast", "selected"), Pair(parser.Modes, ExecutionModeKind.Speed));

        Feed.Lines(parser, sink, Settings(T0 + 5000, """{"service_tier":null,"reasoning_effort":null}"""));
        Assert.True(parser.Modes.IsEmpty);
        Assert.Equal(("high", "recorded"), Pair(sink.Latest[0].Modes, ExecutionModeKind.Effort));
    }

    [Fact]
    public void CodexLegacyTokenCountsTakeTheContextAndSubAgentsStartWithoutSettings()
    {
        var parser = new CodexParser("/fixture/rollout-b.jsonl", new Deduper());
        var sink = new RecordingSink();
        Feed.Lines(parser, sink,
            CodexLines.SessionMeta(T0),
            Settings(T0 + 100, """{"reasoning_effort":"ultra"}"""),
            CodexLines.TokenCount(T0 + 1000, CodexLines.Usage(10, 0, 20, 0), CodexLines.Usage(10, 0, 20, 0)));
        Assert.Equal(("ultra", "selected"), Pair(Assert.Single(sink.Latest).Modes, ExecutionModeKind.Effort));
        Assert.Equal("unknown", parser.Modes.Attribute(ExecutionModeKind.Speed).Mode);
        Assert.True(new CodexParser("/fixture/rollout-c.jsonl", new Deduper()).Modes.IsEmpty);
    }

    [Fact]
    public void DshRequestHeadersReplaceOrClearTheEffortAndResponsesKeepTheirs()
    {
        var parser = new DshParser("/fixture/session.v4.jsonl.zstd", new Deduper());
        var sink = new RecordingSink();
        Feed.Lines(parser, sink,
            DshLines.Session(T0),
            DshHeader(1, T0 + 10, ""","reasoningEffort":"max" """),
            DshLines.StepStart(2, T0 + 20),
            DshLines.Message(3, T0 + 1000, "m-1", DshLines.Usage(10, 20)));
        Assert.Equal(("max", "recorded"), Pair(Assert.Single(sink.Latest).Modes, ExecutionModeKind.Effort));
        Assert.Equal(("max", "recorded"), Pair(parser.Modes, ExecutionModeKind.Effort));

        Feed.Lines(parser, sink, DshHeader(4, T0 + 2000, ""","reasoningEffort":"HIGH" """));
        Assert.Equal(("high", "recorded"), Pair(parser.Modes, ExecutionModeKind.Effort));

        Feed.Lines(parser, sink, DshHeader(5, T0 + 3000, string.Empty), DshLines.Message(6, T0 + 4000, "m-2", DshLines.Usage(10, 20)));
        Assert.True(parser.Modes.IsEmpty);
        Assert.True(sink.Latest[1].Modes.IsEmpty);
        Assert.Equal(("max", "recorded"), Pair(sink.Latest[0].Modes, ExecutionModeKind.Effort));
    }

    private static string DshHeader(long seq, long ms, string extra) =>
        DshLines.Event("request/header", seq, ms, $$"""{"header":{"config":{"provider":"deepseek-account","model":"deepseek-flash"{{extra}}} } }""");

    private static string Attachment(long ms, string type) =>
        $$"""{"type":"attachment","timestamp":"{{Ts.Iso(ms)}}","attachment":{"type":"{{type}}"} }""";

    private static string Settings(long ms, string settingsJson) =>
        CodexLines.Envelope(ms, "event_msg", $$"""{"type":"thread_settings_applied","thread_settings":{{settingsJson}} }""");

    private static string UsageRecordWithTier(long ms, string id, string tier) =>
        CodexLines.Envelope(ms, "token_usage_record",
            $$"""{"thread_id":"thread-1","response_id":"{{id}}","service_tier":"{{tier}}","usage":{{CodexLines.Usage(1, 0, 10, 0)}} }""");

    private static string Effort(ExecutionModes modes) => modes.Attribute(ExecutionModeKind.Effort).Mode;

    private static (string, string) Pair(ExecutionModes modes, ExecutionModeKind kind) =>
        modes.Attribute(kind) is var value ? (value.Mode, value.Evidence) : default;
}
