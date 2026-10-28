using System.Globalization;
using System.Text;
using AgentSpeed.Core.IO;
using AgentSpeed.Core.Parsing;
using AgentSpeed.Core.Pi;

namespace AgentSpeed.Core.Tests;

public sealed class PiParserTests
{
    private const string Guard = "\"api\":\"anthropic-messages\",\"provider\":\"anthropic\",\"model\":\"claude-opus-4-5\"";
    private const string MinimalUsage = "\"input\":1";
    private const string FileName = "2026-10-20T10-00-00-000Z_0199f1a2.jsonl";
    private static readonly long T0 = Ts.Ms("2026-10-20T10:00:00.000Z");

    private static string Root => Path.Combine(Path.GetTempPath(), "agent-speed-pi-tests", "sessions");

    private static string Main => Path.Combine(Root, "--home-me-proj--", FileName);

    private static string Subagent => Path.Combine(Root, "--home-me-proj--", "2026-10-20T10-00-00-000Z_0199f1a2",
        "0199f1a3-0000-7000-8000-000000000001", "run-0", "session.jsonl");

    public static TheoryData<string> MalformedLines() =>
    [
        "this line is not json",
        "[1,2,3]",
        "42",
        "null",
        "{\"type\":\"session\",\"timestamp\":\"2026-10-20T10:00:00Z\"",
        "{\"type\":\"session\"} trailing",
        "{\"type\":\"message\",}",
    ];

    public static TheoryData<string> UnguardedCalls() =>
    [
        "\"provider\":\"anthropic\",\"api\":\"a\",\"usage\":{\"input\":5}",
        "\"model\":5,\"provider\":\"anthropic\",\"api\":\"a\",\"usage\":{\"input\":5}",
        "\"model\":\"m\",\"api\":\"a\",\"usage\":{\"input\":5}",
        "\"model\":\"m\",\"provider\":7,\"api\":\"a\",\"usage\":{\"input\":5}",
        "\"model\":\"m\",\"provider\":\"anthropic\",\"usage\":{\"input\":5}",
        "\"model\":\"m\",\"provider\":\"anthropic\",\"api\":[\"a\"],\"usage\":{\"input\":5}",
        "\"model\":\"m\",\"provider\":\"anthropic\",\"api\":\"a\",\"usage\":null",
        "\"model\":\"m\",\"provider\":\"anthropic\",\"api\":\"a\",\"usage\":[5]",
    ];

    [Fact]
    public void AssistantMessageUsesItsOwnStartAndDuration()
    {
        var parser = Parser();
        var sink = new RecordingSink();

        Feed.Lines(parser, sink, Header(), User("u1", T0 + 1000),
            Assistant("a1", T0 + 9000, T0 + 1000, 8000, "stop", Usage()));

        var response = Assert.Single(sink.Latest);
        Assert.Equal("pi:a1:" + (T0 + 1000), response.Key);
        Assert.Equal(SourceKind.Pi, response.Source);
        Assert.Equal(T0 + 1000, response.StartMs);
        Assert.Equal(T0 + 9000, response.EndMs);
        Assert.Equal("claude-opus-4-5", response.Model);
        Assert.Equal("anthropic", response.Provider);
        Assert.Equal(1000 + 200 + 300, response.Tokens.Input);
        Assert.Equal(200, response.Tokens.CachedInput);
        Assert.Equal(300, response.Tokens.CacheWriteInput);
        Assert.Equal(50, response.Tokens.CacheWrite1hInput);
        Assert.Equal(400, response.Tokens.Output);
        Assert.Equal(120, response.Tokens.Reasoning);
        Assert.Equal(AgentPhase.Idle, parser.Phase);
    }

    [Fact]
    public void WithoutDurationMsTheEntryTimestampEndsTheResponse()
    {
        var parser = Parser();
        var sink = new RecordingSink();

        Feed.Lines(parser, sink, Header(), Assistant("a1", T0 + 5000, T0 + 1000, null, "stop", Usage()));

        var response = Assert.Single(sink.Latest);
        Assert.Equal(T0 + 1000, response.StartMs);
        Assert.Equal(T0 + 5000, response.EndMs);
    }

    [Fact]
    public void ErrorAndAbortedKeepTheirTotalsWithoutAnInterval()
    {
        var parser = Parser();
        var sink = new RecordingSink();

        Feed.Lines(parser, sink, Header(),
            Assistant("a1", T0 + 2000, T0 + 1000, 1000, "error", Usage()),
            Assistant("a2", T0 + 4000, T0 + 3000, 1000, "aborted", Usage()),
            Assistant("a3", T0 + 6000, T0 + 5000, 1000, null, Usage()));

        var responses = sink.Latest;
        Assert.Equal(3, responses.Count);
        Assert.All(responses, r => Assert.Null(r.StartMs));
        Assert.All(responses, r => Assert.Equal(400, r.Tokens.Output));
        Assert.Equal(AgentPhase.Idle, parser.Phase);
    }

    [Fact]
    public void ZeroUsageProducesNoResponse()
    {
        var parser = Parser();
        var sink = new RecordingSink();

        Feed.Lines(parser, sink, Header(), Assistant("a1", T0 + 2000, T0 + 1000, 1000, "stop",
            "\"input\":0,\"output\":0,\"cacheRead\":0,\"cacheWrite\":0"));

        Assert.Empty(sink.Latest);
        Assert.Equal(AgentPhase.Idle, parser.Phase);
    }

    [Theory]
    [MemberData(nameof(UnguardedCalls))]
    public void UnguardedCallsProduceNoResponse(string call)
    {
        var parser = Parser();
        var sink = new RecordingSink();

        Feed.Lines(parser, sink, Header(),
            $"{{\"type\":\"message\",\"id\":\"a1\",\"timestamp\":\"{Ts.Iso(T0 + 2000)}\",\"message\":{{\"role\":\"assistant\",\"content\":[],\"stopReason\":\"stop\",\"timestamp\":{T0 + 1000},{call}}}}}");

        Assert.Empty(sink.Latest);
    }

    [Fact]
    public void SideUsagesCountInTotalsOnlyAndKeepTheSessionModel()
    {
        var parser = Parser();
        var sink = new RecordingSink();
        const string SideUsage = "\"input\":10,\"output\":20,\"cacheRead\":0,\"cacheWrite\":0";

        Feed.Lines(parser, sink, Header(), Assistant("a1", T0 + 2000, T0 + 1000, 1000, "stop", Usage()),
            $"{{\"type\":\"usage\",\"id\":\"s1\",\"timestamp\":\"{Ts.Iso(T0 + 3000)}\",\"kind\":\"cache_warm\",\"provider\":\"anthropic\",\"model\":\"claude-haiku-4-5\",\"usage\":{{{SideUsage}}}}}",
            $"{{\"type\":\"compaction\",\"id\":\"s2\",\"timestamp\":\"{Ts.Iso(T0 + 4000)}\",\"summary\":\"…\",\"firstKeptEntryId\":\"a1\",\"tokensBefore\":100,\"usage\":{{{SideUsage}}}}}",
            ToolResult("t1", T0 + 5000, "\"call-1\"", "\"usage\":{" + SideUsage + "}"));

        var responses = sink.Latest;
        Assert.Equal(4, responses.Count);
        Assert.Equal("claude-opus-4-5", responses[0].Model);
        Assert.Equal("claude-haiku-4-5", responses[1].Model);
        Assert.Equal("pi:s1:" + (T0 + 3000), responses[1].Key);
        Assert.Null(responses[1].StartMs);
        Assert.Equal(T0 + 3000, responses[1].EndMs);
        // Compaction and tool results name no model of their own; they belong to the session's current one.
        Assert.Equal("claude-opus-4-5", responses[2].Model);
        Assert.Equal(10, responses[2].Tokens.Input);
        Assert.Equal("claude-opus-4-5", responses[3].Model);
        Assert.Equal(20, responses[3].Tokens.Output);
        // A side call never becomes the agent's model.
        Assert.Equal("claude-opus-4-5", parser.Model);
    }

    [Fact]
    public void PhasesFollowUsersToolCallsAndResults()
    {
        var parser = Parser();
        var sink = new RecordingSink();
        const string ToolCall = "[{\"type\":\"toolCall\",\"id\":\"call-1\",\"name\":\"read\",\"arguments\":{}}]";

        Feed.Lines(parser, sink, Header(), User("u1", T0 + 1000));
        Assert.Equal(AgentPhase.Generating, parser.Phase);
        Assert.Equal(T0 + 1000, parser.InFlightStartMs);

        Feed.Lines(parser, sink, Assistant("a1", T0 + 3000, T0 + 1000, 2000, "toolUse", Usage(), ToolCall));
        Assert.Equal(AgentPhase.Tools, parser.Phase);
        Assert.Null(parser.InFlightStartMs);

        Feed.Lines(parser, sink, ToolResult("t1", T0 + 4000, "\"call-1\"", "\"isError\":false"));
        Assert.Equal(AgentPhase.Generating, parser.Phase);
        Assert.Equal(T0 + 4000, parser.InFlightStartMs);

        Feed.Lines(parser, sink, Assistant("a2", T0 + 6000, T0 + 4000, 2000, "stop", Usage()));
        Assert.Equal(AgentPhase.Idle, parser.Phase);
        Assert.Null(parser.InFlightStartMs);

        Feed.Lines(parser, sink, Assistant("a3", T0 + 8000, T0 + 7000, 1000, "error", Usage()));
        Assert.Equal(AgentPhase.Idle, parser.Phase);
    }

    [Fact]
    public void CopiedHistoryIsSkippedAndDeduplicated()
    {
        var deduper = new Deduper();
        var first = Parser(deduper);
        var sink = new RecordingSink();
        Feed.Lines(first, sink, Header(), Assistant("a1", T0 + 2000, T0 + 1000, 1000, "stop", Usage()));
        Assert.Single(sink.Latest);

        // A clone's header is fresh, its copied entries keep their original timestamps: before the header minus 2 s.
        var copy = new PiParser(Path.Combine(Root, "--home-me-proj--", "copy.jsonl"), deduper, Root);
        var copySink = new RecordingSink();
        Feed.Lines(copy, copySink, Header(T0 + 60_000),
            Assistant("a1", T0 + 2000, T0 + 1000, 1000, "stop", Usage()),
            Assistant("a2", T0 + 61_000, T0 + 61_000, null, "stop", Usage()));
        Assert.Equal("pi:a2:" + (T0 + 61_000), Assert.Single(copySink.Latest).Key);
    }

    [Fact]
    public void AnEmptyKeyPartStandsInForAMissingId()
    {
        var parser = Parser();
        var sink = new RecordingSink();

        Feed.Lines(parser, sink, Header(),
            $"{{\"type\":\"message\",\"timestamp\":\"{Ts.Iso(T0 + 2000)}\",\"message\":{{\"role\":\"assistant\",\"content\":[],\"stopReason\":\"stop\",\"timestamp\":{T0 + 1000},\"usage\":{{{MinimalUsage}}},{Guard}}}}}");

        Assert.Equal("pi::" + (T0 + 1000), Assert.Single(sink.Latest).Key);
    }

    [Theory]
    [MemberData(nameof(MalformedLines))]
    public void MalformedLinesAreCounted(string line)
    {
        var parser = Parser();
        Feed.Lines(parser, new RecordingSink(), Header(), line, User("u1", T0 + 1000));
        Assert.Equal(1, parser.MalformedLines);
        Assert.Equal(AgentPhase.Generating, parser.Phase);
    }

    [Fact]
    public void SubagentRunsFollowThePath()
    {
        var main = Parser();
        Assert.False(main.PathIsSubagent);
        Assert.Null(main.ParentPath);
        Assert.Equal(SourceKind.Pi, main.Source);

        var subagent = new PiParser(Subagent, new Deduper(), Root);
        Assert.True(subagent.PathIsSubagent);
        Assert.Equal(Main, subagent.ParentPath);
        Assert.True(subagent.IsSubagent);
    }

    [Fact]
    public void OffsetReadsTakeTheHeaderFirst()
    {
        using var temp = new TempDirectory();
        var path = temp.WriteFile("agent/sessions/--home-me-proj--/" + FileName,
            Header() + "\n" + User("u1", T0 + 1000) + "\n" + Assistant("a1", T0 + 3000, T0 + 1000, 2000, "stop", Usage()) + "\n");
        var parser = new PiParser(path, new Deduper(), temp.Combine("agent", "sessions"));
        var sink = new RecordingSink();
        var batch = parser.NewBatch();

        parser.PrepareBatch(batch, new FileInfo(path).Length);
        batch.OnLine(Encoding.UTF8.GetBytes(Assistant("a2", T0 + 6000, T0 + 5000, 1000, "stop", Usage())));
        batch.ApplyTo(sink);

        Assert.True(parser.HeaderSeen);
        Assert.Equal(T0, parser.HeaderMs);
        Assert.Equal("proj", parser.Project);
        Assert.Equal("pi:a2:" + (T0 + 5000), Assert.Single(sink.Latest).Key);
    }

    [Fact]
    public void RootsFollowTheEnvironment()
    {
        var home = Path.Combine(Path.GetTempPath(), "home");
        Assert.Equal(Path.Combine(home, ".pi", "agent", "sessions"), PiPaths.Root(_ => null, home));
        Assert.Equal(Path.Combine("D:\\pi", "sessions"), PiPaths.Root(name => name == "PI_CODING_AGENT_DIR" ? "D:\\pi" : null, home));
        Assert.Equal("D:\\store", PiPaths.Root(
            name => name == "PI_CODING_AGENT_SESSION_DIR" ? "D:\\store" : "D:\\pi", home));
    }

    [Fact]
    public void DiscoveryTakesMainSessionsAndRunSubsessionsOnly()
    {
        using var temp = new TempDirectory();
        var root = temp.Combine("agent", "sessions");
        temp.WriteFile("agent/sessions/--home-me-proj--/" + FileName, Header() + "\n");
        temp.WriteFile("agent/sessions/--home-me-proj--/2026-10-20T10-00-00-000Z_0199f1a2/0199f1a3-0000-7000-8000-000000000001/run-0/session.jsonl", Header() + "\n");
        temp.WriteFile("agent/sessions/--home-me-proj--/ollama.json", "{}\n");
        temp.WriteFile("agent/sessions/stray.jsonl", Header() + "\n");
        temp.WriteFile("agent/sessions/--home-me-proj--/notes/draft.jsonl", Header() + "\n");
        temp.WriteFile("agent/sessions/.hidden/session.jsonl", Header() + "\n");

        var found = PiDiscovery.Enumerate(root, int.MaxValue)
            .Select(f => Path.GetRelativePath(root, f.Path).Replace(Path.DirectorySeparatorChar, '/'))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(
            ["--home-me-proj--/2026-10-20T10-00-00-000Z_0199f1a2.jsonl",
             "--home-me-proj--/2026-10-20T10-00-00-000Z_0199f1a2/0199f1a3-0000-7000-8000-000000000001/run-0/session.jsonl"],
            found);

        Assert.True(PiDiscovery.IsSessionPath(root, temp.Combine("agent", "sessions", "--home-me-proj--", FileName)));
        Assert.True(PiDiscovery.IsSessionPath(root, temp.Combine("agent", "sessions", "--home-me-proj--",
            "2026-10-20T10-00-00-000Z_0199f1a2", "0199f1a3-0000-7000-8000-000000000001", "run-0", "session.jsonl")));
        Assert.False(PiDiscovery.IsSessionPath(root, Path.Combine(root, "stray.jsonl")));
        Assert.False(PiDiscovery.IsSessionPath(root, Path.Combine(root, "--home-me-proj--", "notes", "draft.jsonl")));
        Assert.False(PiDiscovery.IsSessionPath(root, Path.Combine(root, "--home-me-proj--", "ollama.json")));
    }

    private static PiParser Parser(IDeduper? deduper = null) => new(Main, deduper ?? new Deduper(), Root);

    private static string Header(long? ms = null) =>
        $"{{\"type\":\"session\",\"version\":3,\"id\":\"0199f1a2-0000-7000-8000-000000000000\",\"timestamp\":\"{Ts.Iso(ms ?? T0)}\",\"cwd\":\"/home/me/proj\"}}";

    private static string User(string id, long ms) => Message(id, ms,
        $"{{\"role\":\"user\",\"content\":\"go\",\"timestamp\":{ms}}}");

    private static string Assistant(string id, long entryMs, long? startedMs, long? durationMs, string? stop, string usage, string content = "[]")
    {
        var message = $"{{\"role\":\"assistant\",\"content\":{content},\"timestamp\":{startedMs ?? entryMs},\"usage\":{{{usage}}},{Guard}";
        message += $",\"stopReason\":{(stop is null ? "7" : "\"" + stop + "\"")}";
        if (durationMs is { } duration)
        {
            message += ",\"durationMs\":" + duration.ToString(CultureInfo.InvariantCulture);
        }

        return Message(id, entryMs, message + "}");
    }

    private static string ToolResult(string id, long entryMs, string callId, string extra) => Message(id, entryMs,
        $"{{\"role\":\"toolResult\",\"toolCallId\":{callId},\"content\":[],\"timestamp\":{entryMs},{extra}}}");

    private static string Message(string id, long entryMs, string message) =>
        $"{{\"type\":\"message\",\"id\":\"{id}\",\"parentId\":null,\"timestamp\":\"{Ts.Iso(entryMs)}\",\"message\":{message}}}";

    private static string Usage() =>
        "\"input\":1000,\"output\":400,\"cacheRead\":200,\"cacheWrite\":300,\"cacheWrite1h\":50,\"reasoning\":120";
}
