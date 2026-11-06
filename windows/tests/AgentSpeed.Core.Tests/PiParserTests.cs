using System.Globalization;
using AgentSpeed.Core.IO;
using AgentSpeed.Core.Monitoring;
using AgentSpeed.Core.Parsing;
using AgentSpeed.Core.Pi;

namespace AgentSpeed.Core.Tests;

/// <summary>Pi rules beyond the shared fixtures (METRICS 4.13, EXECUTION-MODES Pi) and Pi roots in the monitor.</summary>
public sealed class PiParserTests : IDisposable
{
    private const string Main = "/home/dev/.pi/agent/sessions/--w-halo--/2026-10-20T12-00-00-000Z_s.jsonl";

    private readonly TempDirectory _temp = new();
    private readonly long _now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    private readonly long _t0 = Ts.Ms("2026-10-20T12:00:00Z");

    public void Dispose() => _temp.Dispose();

    private string DefaultRoot => _temp.Combine("home", ".pi", "agent", "sessions");

    private string CustomRoot => _temp.Combine("home", "pi-sessions");

    [Fact]
    public void PendingAndDeferredResponsesCountInTotalsOnlyAndEndTheRun()
    {
        var (parser, sink) = Run(
            Header(_t0),
            User("u1", _t0 + 1000),
            Assistant("a1", _t0 + 3000, _t0 + 1005, 1500, "pending", output: 20),
            User("u2", _t0 + 4000),
            Assistant("a2", _t0 + 6000, _t0 + 4005, 1500, "deferred", output: 30));

        Assert.Equal([(null, _t0 + 2505), (null, _t0 + 5505)], sink.Latest.Select(r => (r.StartMs, r.EndMs)));
        Assert.Equal((AgentPhase.Idle, (long?)null), (parser.Phase, parser.InFlightStartMs));
    }

    [Fact]
    public void WithoutDurationMsTheResponseEndsAtItsEntryTimestamp()
    {
        var (_, sink) = Run(Header(_t0), Assistant("a1", _t0 + 3000, _t0 + 1005, null, "stop"),
            Assistant("a2", _t0 + 5000, _t0 + 4005, -1, "stop"));

        Assert.Equal([(_t0 + 1005, _t0 + 3000), (_t0 + 4005, _t0 + 5000)], sink.Latest.Select(r => ((long?)r.StartMs, r.EndMs)));
    }

    [Fact]
    public void UsageEntriesNeedAProviderAndAModelButNoApi()
    {
        var (parser, sink) = Run(
            Header(_t0),
            Entry("usage", "s1", _t0 + 1000, "\"kind\":\"cache_warm\",\"provider\":\"\",\"model\":\"m\",\"usage\":{\"cacheRead\":100,\"output\":1}"),
            Entry("usage", "s2", _t0 + 2000, "\"kind\":\"cache_warm\",\"model\":\"m\",\"usage\":{\"output\":1}"),
            Entry("usage", "s3", _t0 + 3000, "\"kind\":\"cache_warm\",\"provider\":\"x\",\"usage\":{\"output\":1}"),
            Entry("usage", "s4", _t0 + 4000, "\"kind\":\"cache_warm\",\"provider\":\"x\",\"model\":\"m\",\"usage\":{\"output\":0}"));

        var side = Assert.Single(sink.Latest);
        Assert.Equal(("pi:s1:" + Number(_t0 + 1000), (string?)"m", (string?)null, (long?)null, 101L),
            (side.Key, side.Model, side.Provider, side.StartMs, side.Tokens.Total));
        Assert.Null(parser.Model);
    }

    [Fact]
    public void SideUsageCarriesNoFactsAndRecordedLevelsReplaceSelectedOnes()
    {
        var (parser, sink) = Run(
            Header(_t0),
            Entry("thinking_level_change", "e1", _t0 + 10, "\"thinkingLevel\":\"none\""),
            Assistant("a1", _t0 + 3000, _t0 + 1005, 1500, "toolUse", call: "c1"),
            ToolResult("t1", _t0 + 3100, "c1", "\"usage\":{\"input\":50,\"output\":5}"),
            Entry("thinking_level_change", "e2", _t0 + 3200, "\"thinkingLevel\":\"not a level\""),
            Assistant("a2", _t0 + 5000, _t0 + 3205, 1500, "stop", thinking: "high"),
            Entry("compaction", "c", _t0 + 6000, "\"summary\":\"x\",\"usage\":{\"input\":10,\"output\":2}"));

        var modes = sink.Latest.ToDictionary(r => r.Key, r => EffortAndSpeed.Effort(r.Modes)?.Mode);
        Assert.Equal("off", modes["pi:a1:" + Number(_t0 + 1005)]);
        Assert.Null(modes["pi:t1:" + Number(_t0 + 3100)]);
        Assert.Equal("high", modes["pi:a2:" + Number(_t0 + 3205)]);
        Assert.Null(modes["pi:c:" + Number(_t0 + 6000)]);
        Assert.Equal(("high", "recorded"), (EffortAndSpeed.Effort(parser.Modes)!.Value.Mode, EffortAndSpeed.Effort(parser.Modes)!.Value.Evidence));
    }

    [Fact]
    public void ToolCallsAndTheirResultsBecomeWorkActions()
    {
        var (parser, _) = Run(
            Header(_t0),
            Assistant("a1", _t0 + 3000, _t0 + 1005, 1500, "toolUse", call: "c1"),
            ToolResult("t1", _t0 + 3100, "c1", "\"content\":[]"),
            Assistant("a2", _t0 + 6000, _t0 + 3205, 1500, "toolUse", call: "c2"),
            Entry("message", "t2", _t0 + 6100, "\"message\":{\"role\":\"toolResult\",\"toolCallId\":\"c2\",\"isError\":true}"));

        Assert.Equal(new (string, bool?)[] { ("read", false), ("read", true) },
            parser.WorkActions.Select(a => (a.Tool, a.Failed)).ToArray());
    }

    [Fact]
    public void CopiedHistoryAddsNoWorkActions()
    {
        var (parser, _) = Run(Header(_t0), Assistant("a0", _t0 - 60_000, _t0 - 62_000, 1500, "toolUse", call: "c0"));
        Assert.Empty(parser.WorkActions);
    }

    [Fact]
    public void ToolResultsNeverEndARunAndCacheWrite1hIsASubsetOfCacheWrite()
    {
        var (parser, sink) = Run(
            Header(_t0),
            Assistant("a1", _t0 + 3000, _t0 + 1005, 1500, "toolUse", call: "c1", usage: "\"input\":1,\"output\":5,\"cacheWrite\":40,\"cacheWrite1h\":90"),
            ToolResult("t1", _t0 + 3100, "c1", "\"toolName\":\"yield\""));

        Assert.Equal((40L, 40L), (sink.Latest[0].Tokens.CacheWriteInput, sink.Latest[0].Tokens.CacheWrite1hInput));
        Assert.Equal((AgentPhase.Generating, (long?)(_t0 + 3100)), (parser.Phase, parser.InFlightStartMs));
    }

    [Fact]
    public void AKeyFirstSeenInAnotherFileIsIgnoredWithoutChangingThePhase()
    {
        var deduper = new Deduper();
        Run(deduper, Main, Header(_t0), Assistant("a1", _t0 + 3000, _t0 + 1005, 1500, "stop"),
            Entry("usage", "s1", _t0 + 4000, "\"provider\":\"x\",\"model\":\"m\",\"usage\":{\"output\":1}"));

        var (fork, sink) = Run(deduper, Main.Replace("_s.jsonl", "_fork.jsonl", StringComparison.Ordinal), Header(_t0 + 3500),
            User("u1", _t0 + 3600), Assistant("a1", _t0 + 3000, _t0 + 1005, 1500, "stop"),
            Entry("usage", "s1", _t0 + 4000, "\"provider\":\"x\",\"model\":\"m\",\"usage\":{\"output\":1}"));

        Assert.Empty(sink.Latest);
        Assert.Equal(AgentPhase.Generating, fork.Phase);
    }

    [Fact]
    public void ReadingFromAnOffsetTakesTheHeaderFirst()
    {
        var path = Write(DefaultRoot, "--w-halo--/2026-10-20T12-00-00-000Z_s.jsonl",
            Header(_t0, "/w/halo"), User("u1", _t0 + 1000), Assistant("a1", _t0 + 3000, _t0 + 1005, 1500, "stop"));
        var parser = PiParser.ForPath(path, new Deduper());
        var tailer = new FileTailer(path);
        var sink = new RecordingSink();
        var offset = File.ReadAllLines(path)[0].Length + 1;

        tailer.StartAt(offset, StartAlignment.SkipPartialLine);
        var batch = parser.NewBatch();
        parser.PrepareBatch(batch, tailer.Offset);
        tailer.ReadAppended(batch);
        batch.ApplyTo(sink);

        Assert.Equal(("halo", (long?)_t0), (parser.Project, parser.HeaderMs));
        Assert.Single(sink.Latest);
    }

    [Fact]
    public void TheMonitorReadsEveryRootAttachesSubAgentsAndCountsMainSessionsOnly()
    {
        var t = _now - 200_000;
        var main = Write(DefaultRoot, "--w-halo--/2026-10-20T12-00-00-000Z_main.jsonl",
            Header(t, "/w/halo"), User("u1", t + 1000), Assistant("a1", t + 4000, t + 1010, 3000, "toolUse", call: "c1", output: 120));
        var sub = Write(DefaultRoot, "--w-halo--/2026-10-20T12-00-00-000Z_main/reviewer.jsonl",
            Header(t + 2000, "/w/halo"), User("s1", t + 2100), Assistant("s2", t + 3500, t + 2110, 1300, "stop", output: 60));
        var flat = Write(CustomRoot, "2026-10-20T12-05-00-000Z_flat.jsonl",
            Header(t, "/w/flat"), User("f1", t + 1000), Assistant("f2", t + 3000, t + 1005, 1900, "stop", output: 75));
        Write(DefaultRoot, ".trash/2026-10-20T12-00-00-000Z_main.jsonl", Header(t, "/w/halo"));
        using var core = Core(DefaultRoot, CustomRoot);

        var report = core.Seed(_now, _now - 3_600_000, CancellationToken.None);

        Assert.Equal(3, report.FilesDiscovered);
        var snapshot = Build(core);
        var agents = snapshot.Agents.ToDictionary(a => a.Id);
        Assert.Equal(main, agents[sub].ParentId);
        Assert.Equal((false, true, false), (agents[main].IsSubagent, agents[sub].IsSubagent, agents[flat].IsSubagent));
        Assert.Equal(("halo", "flat", AgentPhase.Tools), (agents[main].Project, agents[flat].Project, agents[main].Phase));
        var source = snapshot.Sources.Single(s => s.Kind == SourceKind.Pi);
        Assert.Equal((2, 255L, true, Path.GetFullPath(DefaultRoot)), (source.SessionCount, source.Today.Output, source.Available, source.RootPath));
    }

    [Fact]
    public void ACustomFolderSetInPisSettingsWhileRunningIsReadFromThenOn()
    {
        var t = _now - 200_000;
        IReadOnlyList<string> piRoots = [Path.GetFullPath(DefaultRoot)];
        var locations = new SourceLocations(string.Empty, string.Empty, string.Empty, string.Empty, [],
            PiRoots: piRoots, PiAutomatic: true);
        using var core = new MonitorCore(new MonitorConfiguration(new HashSet<SourceKind> { SourceKind.Pi }), locations, _now - 3_600_000)
        {
            AutomaticPiRoots = () => piRoots,
        };
        core.Seed(_now, _now - 3_600_000, CancellationToken.None);
        Write(CustomRoot, "2026-10-20T12-00-00-000Z_s.jsonl",
            Header(t, "/w/halo"), User("u1", t + 1000), Assistant("a1", t + 4000, t + 1010, 3000, "stop", output: 40));

        piRoots = [Path.GetFullPath(DefaultRoot), Path.GetFullPath(CustomRoot)];
        Assert.True(core.Rediscover(_now, _now - 3_600_000, CancellationToken.None));

        Assert.False(core.RestartRequested);
        Assert.Equal(40L, Build(core).Sources.Single(s => s.Kind == SourceKind.Pi).Today.Output);
    }

    /// <summary>Moving the files over would leave today's omp responses counted, so the monitor starts over (METRICS 4.7).</summary>
    [Fact]
    public void ACustomFolderThatCoversAnOmpRootRestartsTheMonitorSoItsFilesCountOnceAsPi()
    {
        var t = _now - 200_000;
        var omp = Path.GetFullPath(_temp.Combine("home", ".omp", "agent", "sessions"));
        Write(CustomRoot, "--w-halo--/2026-10-20T12-00-00-000Z_s.jsonl",
            Header(t, "/w/halo"), User("u1", t + 1000), Assistant("a1", t + 4000, t + 1010, 3000, "stop", output: 40));
        var enabled = new HashSet<SourceKind> { SourceKind.Omp, SourceKind.Pi };
        IReadOnlyList<string> piRoots = [Path.GetFullPath(DefaultRoot)];
        var before = new SourceLocations(string.Empty, string.Empty, string.Empty, string.Empty, [],
            OmpRoots: [omp, Path.GetFullPath(CustomRoot)], PiRoots: piRoots, PiAutomatic: true);
        using (var core = new MonitorCore(new MonitorConfiguration(enabled), before, _now - 3_600_000) { AutomaticPiRoots = () => piRoots })
        {
            core.Seed(_now, _now - 3_600_000, CancellationToken.None);
            Assert.Equal(SourceKind.Omp, Assert.Single(Build(core).Agents).Source);

            piRoots = [Path.GetFullPath(DefaultRoot), Path.GetFullPath(CustomRoot)];
            core.Rediscover(_now, _now - 3_600_000, CancellationToken.None);
            Assert.True(core.RestartRequested);
        }

        using var restarted = new MonitorCore(new MonitorConfiguration(enabled), before with { PiRoots = piRoots }, _now - 3_600_000)
        {
            AutomaticPiRoots = () => piRoots,
        };
        restarted.Seed(_now, _now - 3_600_000, CancellationToken.None);
        var snapshot = Build(restarted);
        Assert.Equal(SourceKind.Pi, Assert.Single(snapshot.Agents).Source);
        Assert.Equal((0L, 40L), (snapshot.Sources.Single(s => s.Kind == SourceKind.Omp).Today.Output,
            snapshot.Sources.Single(s => s.Kind == SourceKind.Pi).Today.Output));
        restarted.Rediscover(_now, _now - 3_600_000, CancellationToken.None);
        Assert.False(restarted.RestartRequested);
    }

    private MonitorCore Core(params string[] roots) => new(
        new MonitorConfiguration(new HashSet<SourceKind> { SourceKind.Pi }),
        new SourceLocations(string.Empty, string.Empty, string.Empty, string.Empty, [], PiRoots: [.. roots.Select(Path.GetFullPath)]),
        _now - 3_600_000);

    private SpeedSnapshot Build(MonitorCore core) =>
        core.BuildSnapshot(_now, _now - 3_600_000, MonitorState.Running, observeLive: false).Snapshot;

    private static (PiParser Parser, RecordingSink Sink) Run(params string[] lines) => Run(new Deduper(), Main, lines);

    private static (PiParser Parser, RecordingSink Sink) Run(IDeduper deduper, string path, params string[] lines)
    {
        var parser = new PiParser(path, deduper, null);
        var sink = new RecordingSink();
        Feed.Lines(parser, sink, lines);
        return (parser, sink);
    }

    private static string Write(string root, string relative, params string[] lines)
    {
        var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, string.Concat(lines.Select(l => l + "\n")));
        return Path.GetFullPath(path);
    }

    private static string Header(long ms, string cwd = "/w/halo") =>
        "{\"type\":\"session\",\"version\":3,\"id\":\"01a1\",\"timestamp\":\"" + Ts.Iso(ms) + "\",\"cwd\":\"" + cwd + "\"}";

    private static string Entry(string type, string id, long ms, string fields) =>
        "{\"type\":\"" + type + "\",\"id\":\"" + id + "\",\"timestamp\":\"" + Ts.Iso(ms) + "\"," + fields + "}";

    private static string User(string id, long ms) =>
        Entry("message", id, ms, "\"message\":{\"role\":\"user\",\"content\":[{\"type\":\"text\",\"text\":\"go\"}],\"timestamp\":" + Number(ms) + "}");

    private static string ToolResult(string id, long ms, string call, string fields) =>
        Entry("message", id, ms, "\"message\":{\"role\":\"toolResult\",\"toolCallId\":\"" + call + "\",\"isError\":false," + fields + "}");

    private static string Assistant(string id, long entryMs, long startedMs, double? duration, string stop, string? call = null,
        long output = 10, string? thinking = null, string? usage = null)
    {
        var content = call is null
            ? "[{\"type\":\"text\",\"text\":\"done\"}]"
            : "[{\"type\":\"toolCall\",\"id\":\"" + call + "\",\"name\":\"read\",\"arguments\":{}}]";
        return Entry("message", id, entryMs, "\"message\":{\"role\":\"assistant\",\"content\":" + content
            + ",\"api\":\"anthropic-messages\",\"provider\":\"anthropic\",\"model\":\"claude-opus-5-5\""
            + (thinking is null ? string.Empty : ",\"thinkingLevel\":\"" + thinking + "\"")
            + ",\"usage\":{" + (usage ?? "\"input\":10,\"output\":" + Number(output) + ",\"cacheRead\":100,\"cacheWrite\":0")
            + "},\"stopReason\":\"" + stop + "\",\"timestamp\":" + Number(startedMs)
            + (duration is { } d ? ",\"durationMs\":" + d.ToString("0.###", CultureInfo.InvariantCulture) : string.Empty) + "}");
    }

    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
}
