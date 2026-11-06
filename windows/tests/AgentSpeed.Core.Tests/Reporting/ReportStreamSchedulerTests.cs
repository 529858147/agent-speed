using System.Text.Json;
using AgentSpeed.Core.Reporting;

namespace AgentSpeed.Core.Tests.Reporting;

/// <summary>METRICS 11.9 against report-vectors.json <c>websocket.scheduler</c>: the simulator's timelines with instant writes.</summary>
public sealed class ReportStreamSchedulerTests
{
    private sealed record Simulation(List<string> Log, ReportStreamScheduler Scheduler);

    /// <summary>Runs one case like <c>simulate_stream</c>: events in time order, wake-ups as ticks without new data.</summary>
    private static Simulation Simulate(JsonElement item)
    {
        var config = item.TryGetProperty("config", out var c) ? c : default;
        int Config(string name, int fallback) =>
            config.ValueKind == JsonValueKind.Object && config.TryGetProperty(name, out var value) ? value.GetInt32() : fallback;
        var available = config.ValueKind != JsonValueKind.Object || !config.TryGetProperty("available", out var flag) || flag.GetBoolean();
        var settings = WithOverrides(ReportStreamSettings.Default, config);
        var scheduler = new ReportStreamScheduler(available ? settings : null, Config("heartbeatSeconds", 60), jitter: () => 1);
        var log = new List<string>();
        var events = Expand(item.GetProperty("events"));
        var tickTimes = events.Where(e => e.Text("type") == "tick").Select(e => e.GetProperty("at").GetInt64()).ToHashSet();
        var lastActive = false;

        void Record(long t, IEnumerable<ReportStreamAction> actions) =>
            log.AddRange(actions.Select(a => a.Text).OfType<string>().Select(text => $"{t} {text}"));

        void WakeUntil(long limit)
        {
            while (scheduler.WakeAt is { } at && at < limit)
            {
                scheduler.ClearWake();
                if (!tickTimes.Contains(at))
                {
                    Record(at, scheduler.Tick(at, lastActive));
                }
            }
        }

        foreach (var e in events)
        {
            var t = e.GetProperty("at").GetInt64();
            var kind = e.Text("type");
            WakeUntil(t);
            if (scheduler.WakeAt == t && kind == "tick")
            {
                scheduler.ClearWake();
            }

            var actions = kind switch
            {
                "tick" => Tick(e, t),
                "open" => scheduler.Opened(t),
                "ready" => scheduler.Ready(t, WithOverrides(scheduler.Settings, e.TryGetProperty("limits", out var limits) ? limits : default)),
                "ack" => scheduler.Ack(t, e.GetProperty("seq").GetInt32(), e.OptionalText("status") == "duplicate" ? ReportAckStatus.Duplicate : ReportAckStatus.Accepted,
                    (e.OptionalText("sha") ?? "match") == "match"),
                "error" => scheduler.Error(t, OptionalInt(e, "seq"), e.Text("code"), e.GetProperty("httpStatus").GetInt32(), OptionalInt(e, "retryAfter"),
                    e.TryGetProperty("fatal", out var fatal) && fatal.GetBoolean()),
                "close" => scheduler.Closed(t, e.GetProperty("code").GetInt32()),
                "reject" => scheduler.Rejected(t, e.GetProperty("status").GetInt32(), e.OptionalText("code"), OptionalInt(e, "retryAfter")),
                "settings" => scheduler.SettingsChanged(t, e.TryGetProperty("settings", out var given) && given.ValueKind == JsonValueKind.Object
                    ? WithOverrides(ReportStreamSettings.Default, given)
                    : null),
                "retry" => scheduler.Retry(t),
                _ => throw new InvalidOperationException("unknown event " + kind),
            };
            Record(t, actions);
        }

        WakeUntil(long.MaxValue);
        return new Simulation(log, scheduler);

        List<ReportStreamAction> Tick(JsonElement e, long t)
        {
            lastActive = e.TryGetProperty("active", out var active) && active.GetBoolean();
            var changed = e.TryGetProperty("changed", out var flag) && flag.GetBoolean();
            long? observed = e.TryGetProperty("observedAgo", out var ago) ? t - ago.GetInt64() : null;
            var userEvents = e.TryGetProperty("events", out var list)
                ? list.EnumerateArray().Select(v => v.GetString() switch
                {
                    "pause" => ReportEvent.Pause,
                    "resume" => ReportEvent.Resume,
                    "networkOff" => ReportEvent.NetworkOff,
                    "networkOn" => ReportEvent.NetworkOn,
                    _ => ReportEvent.Stop,
                }).ToList()
                : [];
            return scheduler.Tick(t, lastActive, changed, observed, events: userEvents);
        }
    }

    private static List<JsonElement> Expand(JsonElement events)
    {
        var expanded = new List<JsonElement>();
        foreach (var e in events.EnumerateArray())
        {
            if (e.Text("type") == "tick" && e.TryGetProperty("until", out var until))
            {
                var step = e.TryGetProperty("step", out var s) ? s.GetInt64() : 500;
                for (var at = e.GetProperty("at").GetInt64(); at <= until.GetInt64(); at += step)
                {
                    var copy = e.EnumerateObject().Where(p => p.Name is not ("until" or "step" or "at"))
                        .ToDictionary(p => p.Name, p => (object)p.Value);
                    copy["at"] = at;
                    expanded.Add(JsonSerializer.SerializeToElement(copy));
                }
            }
            else
            {
                expanded.Add(e);
            }
        }

        return [.. expanded.OrderBy(e => e.GetProperty("at").GetInt64())];
    }

    private static ReportStreamSettings WithOverrides(ReportStreamSettings basis, JsonElement overrides)
    {
        var values = basis.Values.Select(v => (long)v).ToArray();
        for (var index = 0; index < values.Length; index++)
        {
            if (overrides.ValueKind == JsonValueKind.Object && overrides.TryGetProperty(ReportStreamSettings.Fields[index].Name, out var value))
            {
                values[index] = value.GetInt64();
            }
        }

        return ReportStreamSettings.FromValues(values);
    }

    private static int? OptionalInt(JsonElement e, string name) =>
        e.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : null;

    [Fact]
    public void VectorTimelines()
    {
        var cases = ReportSpec.Vector("websocket").GetProperty("scheduler").EnumerateArray().ToList();
        Assert.Equal(22, cases.Count);
        foreach (var item in cases)
        {
            var name = item.Text("name");
            var expect = item.GetProperty("expect");
            var got = Simulate(item);
            var log = expect.GetProperty("log").EnumerateArray().Select(v => v.GetString()!).ToList();
            Assert.True(log.SequenceEqual(got.Log), $"{name}\nexpected: {string.Join(" | ", log)}\ngot:      {string.Join(" | ", got.Log)}");
            var scheduler = got.Scheduler;
            if (expect.TryGetProperty("counters", out var counters))
            {
                var expected = counters.EnumerateObject().Select(p => $"{p.Name}={p.Value.GetInt32()}");
                var actual = scheduler.Counters.All.Where(c => c.Value != 0).Select(c => $"{c.Name}={c.Value}");
                Assert.Equal(expected.Order(), actual.Order());
            }

            if (expect.TryGetProperty("latency", out var latency))
            {
                Assert.Equal(latency.EnumerateArray().Select(v => v.GetInt64()), scheduler.Latency.Samples);
            }

            if (expect.TryGetProperty("state", out var state))
            {
                Assert.True(ReportStreamAction.Name(scheduler.Connection) == state.Text("connection"), $"{name}: connection {scheduler.Connection}");
                Assert.Equal(state.GetProperty("seq").GetInt32(), scheduler.Seq);
                Assert.Equal(state.GetProperty("failures").GetInt32(), scheduler.Failures);
                Assert.Equal(state.OptionalText("stop"), scheduler.Stop is { } stop ? ReportStreamAction.Name(stop) : null);
                long? lastDelivered = state.GetProperty("lastDelivered").ValueKind == JsonValueKind.Null ? null : state.GetProperty("lastDelivered").GetInt64();
                Assert.Equal(lastDelivered, scheduler.LastDelivered);
            }
        }
    }

    [Fact]
    public void BackoffVector()
    {
        var expected = ReportSpec.Vector("websocket").GetProperty("backoff").EnumerateArray().Select(v => v.GetInt64());
        var delays = Enumerable.Range(1, 9).Select(failures => ReportStreamScheduler.BackoffMs[Math.Min(failures, ReportStreamScheduler.BackoffMs.Count) - 1]);
        Assert.Equal(expected, delays);
    }

    /// <summary>Nothing connects or is built while blocked (not confirmed, not registered, an error that needs the user).</summary>
    [Fact]
    public void BlockedConnectsNothing()
    {
        var scheduler = new ReportStreamScheduler(ReportStreamSettings.Default, 60, jitter: () => 1);
        var actions = new List<ReportStreamAction>();
        for (long t = 0; t <= 30_000; t += 500)
        {
            actions.AddRange(scheduler.Tick(t, active: true, changed: true, observed: t, blocked: true));
        }

        Assert.All(actions, a => Assert.IsType<ReportStreamAction.Discard>(a));
        Assert.Equal(ReportStreamConnection.Closed, scheduler.Connection);
    }

    /// <summary>A message too large for the connection is dropped as stale; a closing connection then closes.</summary>
    [Fact]
    public void UnsendableIsStale()
    {
        var scheduler = new ReportStreamScheduler(ReportStreamSettings.Default, 60, jitter: () => 1);
        scheduler.Tick(0, active: true, changed: true);
        scheduler.Opened(10);
        scheduler.Ready(20, ReportStreamSettings.Default);
        var write = Assert.Single(scheduler.Tick(100, active: true, changed: true).OfType<ReportStreamAction.Write>());
        Assert.Equal([new ReportStreamAction.Dropped(write.Seq, ReportStreamDrop.Stale)], scheduler.Unsendable(write.Seq));
        Assert.Equal(1, scheduler.Counters.Stale);
        Assert.Equal(0, scheduler.AwaitingCount);
        Assert.Empty(scheduler.Unsendable(write.Seq));
    }

    /// <summary>A pending wake-up is consumed by any tick at or after it, so a timer that fires late adds nothing.</summary>
    [Fact]
    public void WakeIsConsumedByLaterTick()
    {
        var scheduler = new ReportStreamScheduler(ReportStreamSettings.Default, 60, jitter: () => 1);
        scheduler.Tick(0, active: true, changed: true);
        scheduler.Opened(10);
        scheduler.Ready(20, ReportStreamSettings.Default);
        scheduler.Tick(100, active: true, changed: true);
        var wake = Assert.Single(scheduler.Tick(110, active: true, changed: true).OfType<ReportStreamAction.Wake>());
        Assert.Equal(150, wake.At);
        Assert.Contains(scheduler.Tick(160, active: true), a => a is ReportStreamAction.Build);
        Assert.Null(scheduler.WakeAt);
    }
}
