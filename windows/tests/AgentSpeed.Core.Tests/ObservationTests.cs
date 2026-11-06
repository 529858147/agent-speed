using System.Text;
using AgentSpeed.Core.Monitoring;

namespace AgentSpeed.Core.Tests;

/// <summary>Each snapshot carries when the earliest read that changed the engine since the previous one began (METRICS 11.9).</summary>
public sealed class ObservationTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly long _now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    public void Dispose() => _temp.Dispose();

    private string CodexRoot => _temp.Combine("codex", "sessions");

    private string ClaudeRoot => _temp.Combine("claude", "projects");

    [Fact]
    public void ASnapshotCarriesTheEarliestChangingReadSinceThePreviousOne()
    {
        Directory.CreateDirectory(CodexRoot);
        var today = _now - 3_600_000;
        var a = _temp.WriteFile("claude/projects/p/a.jsonl", ClaudeLines.UserText(_now - 9000, "go") + "\n");
        var b = _temp.WriteFile("claude/projects/p/b.jsonl", ClaudeLines.UserText(_now - 9000, "go") + "\n");
        var clock = 1_000L;
        using var core = new MonitorCore(MonitorConfiguration.Default, CodexRoot, ClaudeRoot, _temp.Combine("dsh"), today)
        {
            MonotonicClock = () => clock,
        };
        core.Seed(_now, today, CancellationToken.None);
        Assert.Null(Snapshot(core, today).ObservedAtMs);

        File.AppendAllText(a, ClaudeLines.Assistant(_now - 8000, "msg_a", 40) + "\n");
        Assert.True(core.ProcessPaths([a], _now, today));
        clock = 1_400;
        File.AppendAllText(b, ClaudeLines.Assistant(_now - 7000, "msg_b", 60) + "\n");
        Assert.True(core.ProcessPaths([b], _now, today));
        clock = 1_500;
        Assert.False(core.ProcessPaths([a], _now, today));
        Assert.False(core.PollRecent(_now));
        Assert.Equal(1_000, Snapshot(core, today).ObservedAtMs);
        Assert.Null(Snapshot(core, today).ObservedAtMs);

        clock = 2_000;
        File.AppendAllText(a, ClaudeLines.Assistant(_now - 6000, "msg_c", 10) + "\n");
        Assert.True(core.PollRecent(_now));
        Assert.Equal(2_000, Snapshot(core, today).ObservedAtMs);
    }

    [Fact]
    public void ABackgroundReadIsObservedWhenItBeganNotWhenItWasApplied()
    {
        Directory.CreateDirectory(CodexRoot);
        Directory.CreateDirectory(ClaudeRoot);
        var today = _now - 3_600_000;
        var loop = Environment.CurrentManagedThreadId;
        using var gate = new ManualResetEventSlim(false);
        using var core = new MonitorCore(MonitorConfiguration.Default,
            new SourceLocations(CodexRoot, ClaudeRoot, string.Empty, string.Empty, []), today)
        {
            ReadWholeFilesInBackground = true,
            BeforeBackgroundRead = _ => gate.Wait(TimeSpan.FromSeconds(20)),
            MonotonicClock = () => Environment.CurrentManagedThreadId == loop ? 9_000 : 6_000,
        };
        core.Seed(_now, today, CancellationToken.None);

        var zst = WriteZst("rollout-b.jsonl.zst", _now - 20_000);
        Assert.False(core.ProcessPaths([zst], _now, today));
        Assert.Null(Snapshot(core, today).ObservedAtMs);
        gate.Set();
        Assert.True(SpinWait.SpinUntil(core.ApplyCompletedReads, TimeSpan.FromSeconds(10)));
        Assert.Equal(6_000, Snapshot(core, today).ObservedAtMs);
    }

    private static SpeedSnapshot Snapshot(MonitorCore core, long today) =>
        core.BuildSnapshot(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), today, MonitorState.Running, observeLive: true).Snapshot;

    private string WriteZst(string name, long t)
    {
        var lines = string.Concat(new[]
        {
            CodexLines.SessionMeta(t, "thread-z", "/home/me/comet"),
            CodexLines.TurnContext(t + 10),
            CodexLines.TaskStarted(t + 1000),
            CodexLines.UsageRecord(t + 4000, "resp-z", CodexLines.Usage(600, 500, 90, 0), "thread-z"),
            CodexLines.TaskComplete(t + 4100),
        }.Select(l => l + "\n"));
        using var compressor = new ZstdSharp.Compressor();
        var path = Path.Combine(CodexRoot, "2026", "10", "21", name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, compressor.Wrap(Encoding.UTF8.GetBytes(lines)).ToArray());
        return path;
    }
}
