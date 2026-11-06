using System.Globalization;
using System.Text;
using AgentSpeed.Core.Hermes;
using AgentSpeed.Core.History;
using AgentSpeed.Core.Monitoring;

namespace AgentSpeed.Core.Tests;

/// <summary>Writes Hermes Agent log lines the way its logging handler does (local wall time, session tag).</summary>
internal static class HermesLines
{
    public static string Wall(long ms, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(ms), zone).ToString("yyyy-MM-dd HH:mm:ss,fff", CultureInfo.InvariantCulture);

    public static string Turn(long ms, TimeZoneInfo zone, string session, string model = "claude-opus-5-5", string platform = "cli") =>
        $"{Wall(ms, zone)} INFO [{session}] agent.turn_context: conversation turn: session={session} model={model} provider=anthropic " +
        $"platform={platform} history=0 msg='hello'";

    public static string Call(long ms, TimeZoneInfo zone, string session, int n, long input, long output, string latency = "2.0",
        string model = "claude-opus-5-5") =>
        $"{Wall(ms, zone)} INFO [{session}] agent.conversation_loop: API call #{n}: model={model} provider=anthropic in={input} " +
        $"out={output} total={input + output} latency={latency}s cache=0/{input} (0%)";

    public static string End(long ms, TimeZoneInfo zone, string session) =>
        $"{Wall(ms, zone)} INFO [{session}] agent.conversation_loop: Turn ended: reason=text_response(finish_reason=stop) session={session}";

    public static string Tool(long ms, TimeZoneInfo zone, string session) =>
        $"{Wall(ms, zone)} INFO [{session}] agent.tool_executor: tool read_file completed (0.10s, 12 chars)";

    public static void Append(string path, params string[] lines) => AppendText(path, string.Concat(lines.Select(l => l + "\n")));

    /// <summary>Appends with read, write and delete sharing, as a second writer would while the reader has it open.</summary>
    public static void AppendText(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        stream.Write(Encoding.UTF8.GetBytes(text));
    }
}

/// <summary>The Hermes source in the monitor while Hermes writes, rotates and reconfigures its logs (METRICS 4.15, 5, 8).</summary>
public sealed class HermesLiveTests : IDisposable
{
    private const string Main = "20261028_101500_a1b2c3";
    private const string Child = "20261028_101530_d4e5f6";

    private readonly TempDirectory _temp = new();
    private readonly long _now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000 * 1000;
    private readonly TimeZoneInfo _zone = TimeZoneInfo.Utc;

    public void Dispose() => _temp.Dispose();

    private string Home => _temp.Combine("home", ".hermes");

    private string Logs => Path.Combine(Home, "logs");

    private string Live => Path.Combine(Logs, HermesPaths.LiveLogName);

    private long TodayStart => _now - 3_600_000;

    [Fact]
    public void AGrowingLogIsReadIncrementallyAndFollowedAcrossRotation()
    {
        var t = _now - 300_000;
        HermesLines.Append(Live, HermesLines.Turn(t, _zone, Main), HermesLines.Call(t + 4000, _zone, Main, 1, 1000, 100));
        using var core = Core();
        var report = core.Seed(_now, TodayStart, CancellationToken.None);
        Assert.Equal((1, 1), (report.FilesRead, report.Responses));

        var agent = Assert.Single(Build(core).Agents);
        Assert.Equal((Logs + "#" + Main, SourceKind.Hermes, AgentPhase.Generating, string.Empty), (agent.Id, agent.Source, agent.Phase, agent.Project));
        var source = Source(core);
        Assert.Equal((100L, 1, true, Home), (source.Today.Output, source.SessionCount, source.Available, source.RootPath));

        HermesLines.Append(Live, HermesLines.Tool(t + 5000, _zone, Main), HermesLines.Call(t + 9000, _zone, Main, 2, 1200, 50, "3.5"));
        Assert.True(core.ProcessPaths([Live], _now, TodayStart));
        Assert.Equal((150L, 2L), (Source(core).Today.Output, Source(core).Today.Requests));

        // Rotation: the live log becomes agent.log.1 (still being written by a process that has not reopened yet) and a
        // new agent.log starts; nothing is read twice.
        File.Move(Live, Live + ".1");
        HermesLines.Append(Live, HermesLines.End(t + 9100, _zone, Main), HermesLines.Turn(t + 20_000, _zone, Main),
            HermesLines.Call(t + 22_000, _zone, Main, 1, 1300, 30));
        HermesLines.Append(Live + ".1", HermesLines.Turn(t + 9500, _zone, Child, platform: "subagent"));
        Assert.True(core.PollRecent(_now));
        var snapshot = Build(core);
        Assert.Equal((180L, 3L), (Source(core).Today.Output, Source(core).Today.Requests));
        var agents = snapshot.Agents.ToDictionary(a => a.Id);
        Assert.Equal(AgentPhase.Generating, agents[Logs + "#" + Main].Phase);
        Assert.True(agents[Logs + "#" + Child].IsSubagent);
        Assert.Equal(1, Source(core).SessionCount);

        // The oldest rotated file is deleted: its agents and counts stay.
        File.Delete(Live + ".1");
        core.Rediscover(_now, TodayStart, CancellationToken.None);
        Assert.Equal((180L, 2), (Source(core).Today.Output, Build(core).Agents.Count));
        Assert.False(core.PollRecent(_now));
    }

    [Fact]
    public void AnIncompleteLineWaitsForItsNewlineAndAShrunkFileIsReadAgainWithoutCountingTwice()
    {
        var t = _now - 300_000;
        HermesLines.Append(Live, HermesLines.Turn(t, _zone, Main));
        using var core = Core();
        core.Seed(_now, TodayStart, CancellationToken.None);

        var call = HermesLines.Call(t + 3000, _zone, Main, 1, 500, 70);
        HermesLines.AppendText(Live, call[..40]);
        core.PollRecent(_now);
        Assert.Equal(0L, Source(core).Today.Output);
        HermesLines.AppendText(Live, call[40..] + "\n");
        core.PollRecent(_now);
        Assert.Equal(70L, Source(core).Today.Output);

        File.WriteAllText(Live, HermesLines.Turn(t, _zone, Main) + "\n" + call + "\n" + HermesLines.End(t + 3100, _zone, Main) + "\n");
        File.SetLastWriteTimeUtc(Live, DateTime.UtcNow.AddSeconds(5));
        core.ProcessPaths([Live], _now, TodayStart);
        Assert.Equal((70L, 1L, AgentPhase.Idle), (Source(core).Today.Output, Source(core).Today.Requests, Assert.Single(Build(core).Agents).Phase));
    }

    [Fact]
    public void FilesOlderThanTheBacklogStartAtTheirEnd()
    {
        var old = TodayStart - 7_200_000;
        HermesLines.Append(Live + ".1", HermesLines.Turn(old, _zone, Main), HermesLines.Call(old + 2000, _zone, Main, 1, 900, 999));
        File.SetLastWriteTimeUtc(Live + ".1", DateTime.UnixEpoch.AddMilliseconds(old + 2000));
        var t = _now - 60_000;
        HermesLines.Append(Live, HermesLines.Turn(t, _zone, Main), HermesLines.Call(t + 2000, _zone, Main, 1, 100, 10), HermesLines.End(t + 2100, _zone, Main));
        using var core = Core();
        core.Seed(_now, TodayStart, CancellationToken.None);
        Assert.Equal((10L, 1L), (Source(core).Today.Output, Source(core).Today.Requests));

        // A line appended to the old file later is read (it started at its end, after a complete line).
        HermesLines.Append(Live + ".1", HermesLines.Call(t + 5000, _zone, Main, 9, 100, 5));
        core.Rediscover(_now, TodayStart, CancellationToken.None);
        Assert.Equal(15L, Source(core).Today.Output);
    }

    [Fact]
    public void TheCodexRuntimeSwitchHidesGeneratingAndIsCheckedAtRediscovery()
    {
        var t = _now - 300_000;
        HermesLines.Append(Live, HermesLines.Turn(t, _zone, Main));
        using var core = Core();
        core.Seed(_now, TodayStart, CancellationToken.None);
        Assert.Equal(AgentPhase.Generating, Assert.Single(Build(core).Agents).Phase);

        var config = Path.Combine(Home, HermesPaths.ConfigName);
        File.WriteAllText(config, "model:\n  default: gpt-5.5\n  openai_runtime: codex_app_server\n");
        core.Rediscover(_now, TodayStart, CancellationToken.None);
        Assert.Equal(AgentPhase.Idle, Assert.Single(Build(core).Agents).Phase);
        Assert.True(core.Hermes!.Sets.Single().CodexRuntime);

        // While on, a turn start is idle; its responses still count.
        HermesLines.Append(Live, HermesLines.Turn(t + 10_000, _zone, Main), HermesLines.Call(t + 12_000, _zone, Main, 1, 100, 40));
        core.PollRecent(_now);
        Assert.Equal((AgentPhase.Idle, 40L), (Assert.Single(Build(core).Agents).Phase, Source(core).Today.Output));

        File.WriteAllText(config, "model:\n  openai_runtime: auto\n");
        File.SetLastWriteTimeUtc(config, DateTime.UtcNow.AddSeconds(3));
        core.Rediscover(_now, TodayStart, CancellationToken.None);
        HermesLines.Append(Live, HermesLines.Turn(t + 20_000, _zone, Main));
        core.PollRecent(_now);
        Assert.Equal(AgentPhase.Generating, Assert.Single(Build(core).Agents).Phase);
    }

    [Fact]
    public void AProfileCreatedWhileRunningJoinsWithItsWatchFolder()
    {
        Directory.CreateDirectory(Logs);
        using var core = Core();
        core.Seed(_now, TodayStart, CancellationToken.None);
        Assert.True(Source(core).Available);
        Assert.Equal(0, Source(core).SessionCount);

        var t = _now - 60_000;
        var profileLog = Path.Combine(Home, "profiles", "work", "logs", HermesPaths.LiveLogName);
        HermesLines.Append(profileLog, HermesLines.Turn(t, _zone, "cron_1", platform: "cron"), HermesLines.Call(t + 3000, _zone, "cron_1", 1, 300, 20));
        Assert.True(core.Rediscover(_now, TodayStart, CancellationToken.None));
        Assert.Equal((20L, 1), (Source(core).Today.Output, Source(core).SessionCount));
        Assert.Contains(core.WatchRoots, r => r.Family == RootFamily.Hermes && r.Path == Path.GetDirectoryName(profileLog));
        Assert.Equal(Path.GetDirectoryName(profileLog) + "#cron_1", Assert.Single(Build(core).Agents).Id);
    }

    [Fact]
    public void WallTimesAreReadInTheMonitorsZone()
    {
        var zone = HermesClock.FindZone("America/Los_Angeles");
        var t = _now - 300_000;
        HermesLines.Append(Live, HermesLines.Turn(t, zone, Main), HermesLines.Call(t + 4000, zone, Main, 1, 1000, 100, "4.0"));
        using var core = new MonitorCore(
            new MonitorConfiguration(new HashSet<SourceKind> { SourceKind.Hermes }),
            new SourceLocations(string.Empty, string.Empty, string.Empty, string.Empty, [], HermesRoot: Home),
            TodayStart,
            zone: zone);
        core.Seed(_now, TodayStart, CancellationToken.None);
        var agent = Assert.Single(Build(core).Agents);
        Assert.Equal(AgentPhase.Generating, agent.Phase);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(t + 4000), agent.LastActivity);
    }

    [Fact]
    public void HistoryFollowsLogsByIdentityAndCountsASessionOnceAcrossFiles()
    {
        var t = Ts.Ms("2026-10-20T10:00:00Z");
        HermesLines.Append(Live, HermesLines.Turn(t, _zone, Main), HermesLines.Call(t + 4000, _zone, Main, 1, 1000, 100));
        var locations = new SourceLocations(string.Empty, string.Empty, string.Empty, string.Empty, [], HermesRoot: Home);
        var enabled = new HashSet<SourceKind> { SourceKind.Hermes };
        using var store = HistoryStore.Open(_temp.Combine("store", "history.sqlite"));
        var analyzer = new HistoryAnalyzer(store);

        Assert.Equal(1, analyzer.Run(locations, enabled, TimeZoneInfo.Utc, CancellationToken.None).FilesAnalyzed);
        Assert.Equal((100L, 1), Day(analyzer));
        Assert.Equal(0, analyzer.Run(locations, enabled, TimeZoneInfo.Utc, CancellationToken.None).FilesAnalyzed);

        // Rotation renames the file without changing it: only the new agent.log is analysed, and the session spanning
        // both files is one agent.
        File.Move(Live, Live + ".1");
        HermesLines.Append(Live, HermesLines.Call(t + 9000, _zone, Main, 2, 1100, 40), HermesLines.Turn(t + 20_000, _zone, Child),
            HermesLines.Call(t + 22_000, _zone, Child, 1, 200, 5));
        var report = analyzer.Run(locations, enabled, TimeZoneInfo.Utc, CancellationToken.None);
        Assert.Equal((2, 1, 1), (report.FilesDiscovered, report.FilesPlanned, report.FilesAnalyzed));
        Assert.Equal((145L, 2), Day(analyzer));

        // The oldest log is deleted: its day keeps its contributions.
        File.Delete(Live + ".1");
        Assert.Equal(0, analyzer.Run(locations, enabled, TimeZoneInfo.Utc, CancellationToken.None).FilesAnalyzed);
        Assert.Equal((145L, 2), Day(analyzer));

        // A copied line (a tool quoting the log) is not counted again.
        HermesLines.Append(Live, HermesLines.Call(t + 4000, _zone, Main, 1, 1000, 100));
        Assert.Equal(1, analyzer.Run(locations, enabled, TimeZoneInfo.Utc, CancellationToken.None).FilesAnalyzed);
        Assert.Equal((145L, 2), Day(analyzer));
    }

    private static (long Output, int Agents) Day(HistoryAnalyzer analyzer)
    {
        var day = Assert.Single(analyzer.Days());
        Assert.Equal(SourceKind.Hermes, day.Source);
        return (day.Totals.Output, day.Agents);
    }

    private MonitorCore Core() => new(
        new MonitorConfiguration(new HashSet<SourceKind> { SourceKind.Hermes }),
        new SourceLocations(string.Empty, string.Empty, string.Empty, string.Empty, [], HermesRoot: Home),
        TodayStart,
        zone: _zone);

    private SourceStatus Source(MonitorCore core) => Build(core).Sources.Single(s => s.Kind == SourceKind.Hermes);

    private SpeedSnapshot Build(MonitorCore core) =>
        core.BuildSnapshot(_now, TodayStart, MonitorState.Running, observeLive: false).Snapshot;
}
