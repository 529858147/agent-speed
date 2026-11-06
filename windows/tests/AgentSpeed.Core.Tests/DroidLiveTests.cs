using System.Globalization;
using System.Text;
using AgentSpeed.Core.Droid;
using AgentSpeed.Core.History;
using AgentSpeed.Core.Monitoring;

namespace AgentSpeed.Core.Tests;

/// <summary>Writes Factory Droid transcript lines and settings files the way droid does (METRICS 4.16).</summary>
internal static class DroidLines
{
    public static string Start(string id, string cwd, string? parent = null, string? calling = null) =>
        "{\"type\":\"session_start\",\"id\":\"" + id + "\",\"title\":\"t\",\"version\":2,\"cwd\":\"" + cwd.Replace("\\", "\\\\") + "\""
        + (parent is null ? "" : ",\"parent\":\"" + parent + "\"")
        + (calling is null ? "" : ",\"callingSessionId\":\"" + calling + "\",\"callingToolUseId\":\"toolu_1\"") + "}";

    public static string User(string id, long ms, string text = "go", string? visibility = null) =>
        "{\"type\":\"message\",\"id\":\"" + id + "\",\"timestamp\":\"" + Ts.Iso(ms) + "\",\"message\":{\"role\":\"user\""
        + (visibility is null ? "" : ",\"visibility\":\"" + visibility + "\"")
        + ",\"content\":[{\"type\":\"text\",\"text\":\"" + text + "\"}]}}";

    public static string Assistant(string id, long ms, string? model = "claude-opus-5-5", string? effort = "high", bool tools = false) =>
        "{\"type\":\"message\",\"id\":\"" + id + "\",\"timestamp\":\"" + Ts.Iso(ms) + "\",\"message\":{\"role\":\"assistant\",\"content\":["
        + (tools ? "{\"type\":\"tool_use\",\"id\":\"toolu_" + id + "\",\"name\":\"Read\",\"input\":{}}" : "{\"type\":\"text\",\"text\":\"done\"}")
        + "]" + (model is null ? "" : ",\"modelId\":\"" + model + "\"") + (effort is null ? "" : ",\"reasoningEffort\":\"" + effort + "\"") + "}}";

    public static string Outcome(string turn) => "{\"type\":\"agent_turn_outcome\",\"turnId\":\"" + turn + "\",\"reason\":\"completed\"}";

    public static string Sidecar(long input, long cacheRead, long output, long thinking = 0, string model = "claude-opus-5-5") =>
        "{\n  \"model\": \"" + model + "\",\n  \"tokenUsage\": {\"inputTokens\": " + input.ToString(CultureInfo.InvariantCulture)
        + ", \"outputTokens\": " + output.ToString(CultureInfo.InvariantCulture)
        + ", \"cacheCreationTokens\": 0, \"cacheReadTokens\": " + cacheRead.ToString(CultureInfo.InvariantCulture)
        + ", \"thinkingTokens\": " + thinking.ToString(CultureInfo.InvariantCulture) + ", \"factoryCredits\": 0}\n}";

    public static void Append(string path, params string[] lines)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        stream.Write(Encoding.UTF8.GetBytes(string.Concat(lines.Select(l => l + "\n"))));
    }
}

/// <summary>The Factory Droid source in the monitor and the history while droid writes its two files (METRICS 4.16, 5, 8).</summary>
public sealed class DroidLiveTests : IDisposable
{
    private const string Main = "5a1d7c3e-0f4b-4e2a-8c6d-9b0e1f2a3b4c";
    private const string Worker = "6b2e8d4f-1a5c-4f3b-9d7e-0c1f2a3b4c5d";
    private const string Fork = "7c3f9e50-2b6d-4a4c-8e8f-1d2a3b4c5d6e";

    private const string CutOff = "{\"model\":\"claude-opus-5-5\",\"tokenUsage\":{\"inputTokens\":4";

    private readonly TempDirectory _temp = new();
    private readonly long _now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000 * 1000;
    private int _stamp;

    public void Dispose() => _temp.Dispose();

    private string Root => _temp.Combine("home", ".factory", "sessions");

    private string Folder => Path.Combine(Root, "-Users-dev-p");

    private long TodayStart => _now - 3_600_000;

    private string Transcript(string id) => Path.Combine(Folder, id + ".jsonl");

    [Fact]
    public void ACallTakesItsSidecarRewriteWhicheverFileDroidWritesFirst()
    {
        var t = _now - 300_000;
        var main = Transcript(Main);
        DroidLines.Append(main, DroidLines.Start(Main, "/Users/dev/p"), DroidLines.User("u1", t), DroidLines.Assistant("m1", t + 3000, tools: true));
        WriteSidecar(Main, DroidLines.Sidecar(1000, 0, 100));
        using var core = Core();
        var report = core.Seed(_now, TodayStart, CancellationToken.None);
        Assert.Equal((1, 1), (report.FilesRead, report.Responses));
        Assert.Equal((1L, 1000L, 100L), Today(core));
        var agent = Assert.Single(Build(core).Agents);
        Assert.Equal((main, "p", "claude-opus-5-5", AgentPhase.Tools, false), (agent.Id, agent.Project, agent.Model, agent.Phase, agent.IsSubagent));

        // The transcript first: the call is open until the sidecar's rewrite is read.
        DroidLines.Append(main, DroidLines.User("r1", t + 3200), DroidLines.Assistant("m2", t + 5000));
        Assert.True(core.ProcessPaths([main], _now, TodayStart));
        Assert.Equal((1L, 1000L, 100L), Today(core));
        Assert.Equal(AgentPhase.Idle, Assert.Single(Build(core).Agents).Phase);
        WriteSidecar(Main, DroidLines.Sidecar(1300, 4000, 160));
        Assert.True(core.ProcessPaths([DroidPaths.SidecarOf(main)], _now, TodayStart));
        Assert.Equal((2L, 5300L, 160L), Today(core));

        // The sidecar first: its growth waits for the call's line.
        WriteSidecar(Main, DroidLines.Sidecar(1500, 9000, 200));
        core.ProcessPaths([DroidPaths.SidecarOf(main)], _now, TodayStart);
        Assert.Equal((2L, 5300L, 160L), Today(core));
        DroidLines.Append(main, DroidLines.User("u3", t + 60_000), DroidLines.Assistant("m3", t + 62_000, "custom:GPT-5.5-[OpenAI-BYOK]-0", "none"));
        Assert.True(core.ProcessPaths([main], _now, TodayStart));
        Assert.Equal((3L, 10_500L, 200L), Today(core));
        Assert.Equal("custom:GPT-5.5-[OpenAI-BYOK]-0", Assert.Single(Build(core).Agents).Model);
    }

    [Fact]
    public void ALowerSidecarAddsNothingAndAReplacedTranscriptStartsOverWithoutCountingTwice()
    {
        var t = _now - 300_000;
        var main = Transcript(Main);
        DroidLines.Append(main, DroidLines.Start(Main, "/Users/dev/p"), DroidLines.User("u1", t), DroidLines.Assistant("m1", t + 3000),
            DroidLines.Outcome("u1"));
        WriteSidecar(Main, DroidLines.Sidecar(1000, 0, 100));
        var observer = new Observer();
        using var core = Core(observer);
        core.Seed(_now, TodayStart, CancellationToken.None);

        // A restored .bak: lower counters add nothing, later growth counts only above the mark.
        WriteSidecar(Main, DroidLines.Sidecar(400, 0, 40));
        core.ProcessPaths([DroidPaths.SidecarOf(main)], _now, TodayStart);
        DroidLines.Append(main, DroidLines.User("u2", t + 10_000), DroidLines.Assistant("m2", t + 12_000));
        WriteSidecar(Main, DroidLines.Sidecar(1100, 0, 130));
        core.ProcessPaths([main], _now, TodayStart);
        Assert.Equal((2L, 1100L, 130L), Today(core));

        // The transcript replaced by a copy (a new file): its counters are allocated again over the same keys.
        var copy = main + ".new";
        File.WriteAllBytes(copy, File.ReadAllBytes(main));
        File.Move(copy, main, overwrite: true);
        observer.Items.Clear();
        core.ProcessPaths([main], _now, TodayStart);
        Assert.Equal((2L, 1100L, 130L), Today(core));
        Assert.Equal([("droid:m1", false), ("droid:m2", false)], observer.Items);
    }

    [Fact]
    public void ASessionOlderThanTheStartupWindowCountsOnlyLaterGrowth()
    {
        var t = TodayStart - 7_200_000;
        var main = Transcript(Main);
        DroidLines.Append(main, DroidLines.Start(Main, "/Users/dev/p"), DroidLines.User("u1", t), DroidLines.Assistant("m1", t + 3000),
            DroidLines.Outcome("u1"));
        WriteSidecar(Main, DroidLines.Sidecar(50_000, 200_000, 9000));
        var old = DateTime.UnixEpoch.AddMilliseconds(t + 4000);
        File.SetLastWriteTimeUtc(main, old);
        File.SetLastWriteTimeUtc(DroidPaths.SidecarOf(main), old);
        using var core = Core();
        var report = core.Seed(_now, TodayStart, CancellationToken.None);
        Assert.Equal((0, 0, 1), (report.FilesRead, report.Responses, report.FilesDiscovered));
        Assert.Equal((0L, 0L, 0L), Today(core));

        DroidLines.Append(main, DroidLines.User("u2", _now - 10_000), DroidLines.Assistant("m2", _now - 8000, "gpt-5.5", "low"));
        WriteSidecar(Main, DroidLines.Sidecar(50_400, 210_000, 9080));
        Assert.True(core.ProcessPaths([main], _now, TodayStart));
        Assert.Equal((1L, 10_400L, 80L), Today(core));
        Assert.Equal(("gpt-5.5", AgentPhase.Idle), (Assert.Single(Build(core).Agents).Model, Assert.Single(Build(core).Agents).Phase));
    }

    [Fact]
    public void PollingAndRediscoveryNoticeASidecarRewrite()
    {
        var t = _now - 300_000;
        var main = Transcript(Main);
        DroidLines.Append(main, DroidLines.Start(Main, "/Users/dev/p"), DroidLines.User("u1", t), DroidLines.Assistant("m1", t + 3000));
        using var core = Core();
        core.Seed(_now, TodayStart, CancellationToken.None);
        Assert.Equal((0L, 0L, 0L), Today(core));

        WriteSidecar(Main, DroidLines.Sidecar(700, 0, 70));
        Assert.True(core.PollRecent(_now));
        Assert.Equal((1L, 700L, 70L), Today(core));
        Assert.False(core.PollRecent(_now));

        DroidLines.Append(main, DroidLines.User("u2", t + 10_000), DroidLines.Assistant("m2", t + 12_000));
        Assert.True(core.Rediscover(_now, TodayStart, CancellationToken.None));
        WriteSidecar(Main, DroidLines.Sidecar(900, 0, 90));
        Assert.True(core.Rediscover(_now, TodayStart, CancellationToken.None));
        Assert.Equal((2L, 900L, 90L), Today(core));
        Assert.False(core.Rediscover(_now, TodayStart, CancellationToken.None));
    }

    [Fact]
    public void AWorkerIsItsCallingSessionsSubagentAndAForkSubtractsItsParentsCounters()
    {
        var t = _now - 300_000;
        var main = Transcript(Main);
        var worker = Transcript(Worker);
        var fork = Transcript(Fork);
        DroidLines.Append(main, DroidLines.Start(Main, "/Users/dev/p"), DroidLines.User("u1", t), DroidLines.Assistant("m1", t + 3000, tools: true));
        WriteSidecar(Main, DroidLines.Sidecar(1000, 0, 100));
        DroidLines.Append(worker, DroidLines.Start(Worker, "/Users/dev/p", calling: Main), DroidLines.User("w1", t + 4000),
            DroidLines.Assistant("w-m1", t + 6000, "gpt-5.5", "medium"));
        WriteSidecar(Worker, DroidLines.Sidecar(300, 0, 30, model: "gpt-5.5"));
        DroidLines.Append(fork, DroidLines.Start(Fork, "/Users/dev/p", parent: Main), DroidLines.User("u1", t),
            DroidLines.Assistant("m1", t + 3000, tools: true), DroidLines.User("f1", t + 20_000), DroidLines.Assistant("f-m1", t + 22_000));
        WriteSidecar(Fork, DroidLines.Sidecar(1250, 0, 120));
        using var core = Core();
        core.Seed(_now, TodayStart, CancellationToken.None);

        var agents = Build(core).Agents.ToDictionary(a => a.Id);
        Assert.Equal((main, true), (agents[worker].ParentId, agents[worker].IsSubagent));
        Assert.Equal((null, false), (agents[fork].ParentId, agents[fork].IsSubagent));
        Assert.Equal(2, Source(core).SessionCount);

        // main 1000/100, worker 300/30, the fork its own 250/20 (its copy of m1 belongs to main).
        Assert.Equal((3L, 1550L, 150L), Today(core));
    }

    [Fact]
    public void AForkWaitsWhileItsParentsSidecarIsCaughtMidWrite()
    {
        var t = _now - 300_000;
        var main = Transcript(Main);
        var fork = Transcript(Fork);
        DroidLines.Append(main, DroidLines.Start(Main, "/Users/dev/p"), DroidLines.User("u1", t), DroidLines.Assistant("m1", t + 3000));
        WriteSidecar(Main, CutOff);
        DroidLines.Append(fork, DroidLines.Start(Fork, "/Users/dev/p", parent: Main), DroidLines.User("f1", t + 20_000),
            DroidLines.Assistant("f-m1", t + 22_000));
        WriteSidecar(Fork, DroidLines.Sidecar(1250, 0, 120));
        using var core = Core();
        core.Seed(_now, TodayStart, CancellationToken.None);
        Assert.Equal((0L, 0L, 0L), Today(core));

        WriteSidecar(Main, DroidLines.Sidecar(1000, 0, 100));
        core.ProcessPaths([DroidPaths.SidecarOf(main)], _now, TodayStart);
        Assert.Equal((1L, 1000L, 100L), Today(core));
        Assert.True(core.ProcessPaths([fork], _now, TodayStart));
        Assert.Equal((2L, 1250L, 120L), Today(core));
    }

    [Fact]
    public void AnOldForkTakesItsMarkOnceItsParentsSidecarReads()
    {
        var t = TodayStart - 7_200_000;
        var main = Transcript(Main);
        var fork = Transcript(Fork);
        DroidLines.Append(main, DroidLines.Start(Main, "/Users/dev/p"), DroidLines.User("u1", t), DroidLines.Assistant("m1", t + 3000));
        WriteSidecar(Main, CutOff);
        DroidLines.Append(fork, DroidLines.Start(Fork, "/Users/dev/p", parent: Main), DroidLines.User("f1", t + 20_000),
            DroidLines.Assistant("f-m1", t + 22_000), DroidLines.Outcome("f1"));
        WriteSidecar(Fork, DroidLines.Sidecar(1250, 0, 120));
        foreach (var path in new[] { main, DroidPaths.SidecarOf(main), fork, DroidPaths.SidecarOf(fork) })
        {
            File.SetLastWriteTimeUtc(path, DateTime.UnixEpoch.AddMilliseconds(t + 30_000));
        }

        using var core = Core();
        core.Seed(_now, TodayStart, CancellationToken.None);
        WriteSidecar(Main, DroidLines.Sidecar(1000, 0, 100));
        core.ProcessPaths([DroidPaths.SidecarOf(main), fork], _now, TodayStart);
        Assert.Equal((0L, 0L, 0L), Today(core));

        DroidLines.Append(fork, DroidLines.User("f2", _now - 10_000), DroidLines.Assistant("f-m2", _now - 8000));
        WriteSidecar(Fork, DroidLines.Sidecar(1400, 0, 150));
        Assert.True(core.ProcessPaths([fork], _now, TodayStart));
        Assert.Equal((1L, 150L, 30L), Today(core));
    }

    [Fact]
    public void HistorySkipsAForkWhoseParentSidecarIsUnreadableUntilItReadsOrStaysTheSame()
    {
        var t = Ts.Ms("2026-10-20T12:00:00Z");
        DroidLines.Append(Transcript(Main), DroidLines.Start(Main, "/Users/dev/p"), DroidLines.User("u1", t), DroidLines.Assistant("m1", t + 3000));
        WriteSidecar(Main, CutOff);
        DroidLines.Append(Transcript(Fork), DroidLines.Start(Fork, "/Users/dev/p", parent: Main), DroidLines.User("f1", t + 20_000),
            DroidLines.Assistant("f-m1", t + 22_000));
        WriteSidecar(Fork, DroidLines.Sidecar(1250, 0, 120));
        using var store = HistoryStore.Open(_temp.Combine("store", "history.sqlite"));
        var analyzer = new HistoryAnalyzer(store);
        var locations = new SourceLocations(string.Empty, string.Empty, string.Empty, string.Empty, [], DroidRoot: Root);
        var enabled = new HashSet<SourceKind> { SourceKind.Droid };

        // The fork is left out and read again by the next analysis, whose parent is unreadable with the same signature: 0.
        Assert.Equal(1, analyzer.Run(locations, enabled, TimeZoneInfo.Utc, CancellationToken.None).FilesAnalyzed);
        Assert.DoesNotContain(Transcript(Fork), analyzer.Entries.Keys);
        Assert.Equal(1, analyzer.Run(locations, enabled, TimeZoneInfo.Utc, CancellationToken.None).FilesAnalyzed);
        Assert.Equal((1L, 1250L, 120L), Day(analyzer));

        // A new analyzer (a relaunch) starts without the memory: a readable parent is subtracted again.
        WriteSidecar(Main, DroidLines.Sidecar(1000, 0, 100));
        var fresh = new HistoryAnalyzer(store);
        Assert.Equal(2, fresh.Run(locations, enabled, TimeZoneInfo.Utc, CancellationToken.None).FilesAnalyzed);
        Assert.Equal((2L, 1250L, 120L), Day(fresh));
    }

    [Fact]
    public void HistoryReadsASessionAgainWhenItsSidecarOrItsParentsSidecarChanges()
    {
        var t = Ts.Ms("2026-10-20T12:00:00Z");
        DroidLines.Append(Transcript(Main), DroidLines.Start(Main, "/Users/dev/p"), DroidLines.User("u1", t), DroidLines.Assistant("m1", t + 3000));
        WriteSidecar(Main, DroidLines.Sidecar(1000, 0, 100));
        DroidLines.Append(Transcript(Fork), DroidLines.Start(Fork, "/Users/dev/p", parent: Main), DroidLines.User("f1", t + 20_000),
            DroidLines.Assistant("f-m1", t + 22_000));
        WriteSidecar(Fork, DroidLines.Sidecar(1250, 0, 120));
        DroidLines.Append(Transcript(Worker), DroidLines.Start(Worker, "/Users/dev/p"), DroidLines.User("w1", t + 30_000), DroidLines.Outcome("w1"));
        WriteSidecar(Worker, DroidLines.Sidecar(500, 0, 0));
        using var store = HistoryStore.Open(_temp.Combine("store", "history.sqlite"));
        var analyzer = new HistoryAnalyzer(store);
        var locations = new SourceLocations(string.Empty, string.Empty, string.Empty, string.Empty, [], DroidRoot: Root);
        var enabled = new HashSet<SourceKind> { SourceKind.Droid };

        Assert.Equal(3, analyzer.Run(locations, enabled, TimeZoneInfo.Utc, CancellationToken.None).FilesAnalyzed);
        Assert.Equal((3L, 1750L, 120L), Day(analyzer));
        Assert.Equal(0, analyzer.Run(locations, enabled, TimeZoneInfo.Utc, CancellationToken.None).FilesAnalyzed);

        // The fork's parent keeps working: the fork's own counters shrink, so both are read again.
        WriteSidecar(Main, DroidLines.Sidecar(1100, 0, 110));
        Assert.Equal(2, analyzer.Run(locations, enabled, TimeZoneInfo.Utc, CancellationToken.None).FilesAnalyzed);
        Assert.Equal((3L, 1750L, 120L), Day(analyzer));

        WriteSidecar(Worker, DroidLines.Sidecar(800, 0, 0));
        Assert.Equal(1, analyzer.Run(locations, enabled, TimeZoneInfo.Utc, CancellationToken.None).FilesAnalyzed);
        Assert.Equal((3L, 2050L, 120L), Day(analyzer));
    }

    private static (long Requests, long Input, long Output) Day(HistoryAnalyzer analyzer)
    {
        var day = Assert.Single(analyzer.Days());
        Assert.Equal(SourceKind.Droid, day.Source);
        return (day.Totals.Requests, day.Totals.Input, day.Totals.Output);
    }

    /// <summary>Rewrites a sidecar with a modification time of its own, as droid's rewrites have.</summary>
    private void WriteSidecar(string id, string text)
    {
        var path = DroidPaths.SidecarOf(Transcript(id));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(++_stamp));
    }

    private (long Requests, long Input, long Output) Today(MonitorCore core)
    {
        var today = Source(core).Today;
        return (today.Requests, today.Input, today.Output);
    }

    private MonitorCore Core(Observer? observer = null) => new(
        new MonitorConfiguration(new HashSet<SourceKind> { SourceKind.Droid }),
        new SourceLocations(string.Empty, string.Empty, string.Empty, string.Empty, [], DroidRoot: Root),
        TodayStart,
        observer);

    private SourceStatus Source(MonitorCore core) => Build(core).Sources.Single(s => s.Kind == SourceKind.Droid);

    private SpeedSnapshot Build(MonitorCore core) =>
        core.BuildSnapshot(_now, TodayStart, MonitorState.Running, observeLive: false).Snapshot;

    private sealed class Observer : IResponseObserver
    {
        public List<(string Key, bool IsCompletion)> Items { get; } = [];

        public void OnResponse(Parsing.ParsedResponse response, bool isCompletion, bool seeded) => Items.Add((response.Key, isCompletion));
    }
}
