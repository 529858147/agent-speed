using System.Diagnostics;
using System.Globalization;
using System.IO;
using AgentSpeed.Boards.Gpu;
using AgentSpeed.Boards.Layers;
using AgentSpeed.Orb.Gpu;
using AgentSpeed.Presentation.Boards;
using AgentSpeed.Presentation.Boards.Demo;
using AgentSpeed.Presentation.Boards.Effects;
using AgentSpeed.Presentation.Localization;
using AgentSpeed.Presentation.Orb;
using AgentSpeed.Presentation.Settings;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace AgentSpeed.Boards.Harness;

/// <summary>
/// Developer harness <c>AGENT_SPEED_BOARD_BENCH=FILE</c>: the DESIGN 2c budget scenarios on the Direct3D renderer without a
/// window, like the orbs' bench. Boards run in real time on a simulated 144 Hz display clock, each rendering only on its
/// frame class's ticks (crowding included) into its own offscreen target, fed by the demo through the real ranking. Per
/// scenario: the process CPU (% of one core), frames per second and per frame class, the CPU of a frame's build, layers
/// rasterized per second, the GPU time per frame and the board's GPU memory (layer texture and a two-buffer swap chain).
/// Presenting is not included. <c>AGENT_SPEED_BOARD_BENCH_STYLES</c>, <c>_SCENARIOS</c> (names, comma-separated),
/// <c>_SCALE</c> (default 2, the budgets' 200 %), <c>_SIZE</c> (default M) and <c>_SECONDS</c> (default 10) adjust it.
/// </summary>
internal static class BoardBenchHarness
{
    private const double RefreshHz = 144;

    /// <summary>The story of 2c's benchmark: idle, a climb, redline, then overtakes and new leaders.</summary>
    private sealed record Scenario(string Name, int Count, BoardSimScenario Sim, Func<double, double?> Target, bool Quiet, EffectsIntensity Level);

    private static readonly Scenario[] Scenarios =
    [
        // No board at all: the process's floor (the runtime's and the graphics driver's own threads) on this machine.
        new("baseline", 0, BoardSimScenario.Calm, _ => 0, true, EffectsIntensity.Standard),
        new("idle", 1, BoardSimScenario.Calm, _ => 0, true, EffectsIntensity.Standard),
        new("cruise", 1, BoardSimScenario.Team, _ => 700, false, EffectsIntensity.Standard),
        new("redline", 1, BoardSimScenario.Team, _ => 3200, false, EffectsIntensity.Standard),
        new("redline max", 1, BoardSimScenario.Team, _ => 3200, false, EffectsIntensity.Max),
        new("story", 1, BoardSimScenario.Team, Story, false, EffectsIntensity.Standard),
        new("4 boards redline", 4, BoardSimScenario.Team, _ => 3200, false, EffectsIntensity.Standard),
    ];

    public static int Run(string path)
    {
        if (!GpuDevice.Initialize(composition: false) || GpuDevice.Shared is not { } device)
        {
            File.WriteAllText(path, "Direct3D 11 unavailable: " + GpuDevice.Failure);
            return 2;
        }

        var scale = Number("AGENT_SPEED_BOARD_BENCH_SCALE", 2);
        var seconds = Number("AGENT_SPEED_BOARD_BENCH_SECONDS", 10);
        var size = BoardSizes.Parse(Environment.GetEnvironmentVariable("AGENT_SPEED_BOARD_BENCH_SIZE") ?? "M");
        var only = Environment.GetEnvironmentVariable("AGENT_SPEED_BOARD_BENCH_SCENARIOS") is { Length: > 0 } names
            ? names.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.Ordinal)
            : null;
        var styles = (Environment.GetEnvironmentVariable("AGENT_SPEED_BOARD_BENCH_STYLES") ?? "glass,timing,neon")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(BoardStyles.Parse).Distinct().ToArray();
        var loc = new Localizer(Localizer.LoadEmbeddedTable(), LanguageCodes.ZhHant);
        ProcessCpu.RaiseToNormal();
        BoardGpuRenderer.Timer = new GpuTimer();
        var report = new System.Text.StringBuilder();
        report.AppendLine(CultureInfo.InvariantCulture,
            $"adapter: {device.AdapterName}, scale {scale}, size {BoardSizes.Key(size)}, {RefreshHz} Hz, {seconds} s per scenario");
        _ = ProcessCpu.BeginTimerPeriod();
        try
        {
            foreach (var style in styles)
            {
                // Untimed: tiered JIT (a method called once per tick needs 15 s to reach 30 calls), DirectWrite and the driver settle
                // before the first scenario.
                _ = Measure(device, style, size, new Scenario("warm-up", 2, BoardSimScenario.Team, Story, false, EffectsIntensity.Max), scale, 16, loc);
                foreach (var scenario in Scenarios)
                {
                    if (only is not null && !only.Contains(scenario.Name))
                    {
                        continue;
                    }

                    var line = Measure(device, style, size, scenario, scale, seconds, loc);
                    report.AppendLine(CultureInfo.InvariantCulture, $"{BoardStyles.Key(style)} {scenario.Name}: {line}");
                    File.WriteAllText(path, report.ToString());
                }
            }
        }
        finally
        {
            _ = ProcessCpu.EndTimerPeriod();
        }

        return 0;
    }

    // Idle for 2 s, a climb to 2400 over 4 s, then redline with the team's overtakes and new leaders.
    private static double? Story(double t) => t < 2 ? 0 : t < 6 ? 2400 * (t - 2) / 4 : 3200;

    private static string Measure(GpuDevice device, BoardStyle style, BoardSize size, Scenario scenario, double scale, double seconds, Localizer loc)
    {
        var policy = new BoardEffectsPolicy(scenario.Level);
        var rigs = new BoardRig[scenario.Count];
        var targets = new Target[scenario.Count];
        for (var i = 0; i < rigs.Length; i++)
        {
            rigs[i] = new BoardRig(style, size, scenario.Sim, policy, loc, (uint)(7 + i * 13));
            rigs[i].Feed.Sim.Quiet = scenario.Quiet;
            rigs[i].Feed.Sim.TargetTotal = scenario.Target(0);
            targets[i] = new Target(device, (int)Math.Round(rigs[i].Scene.Canvas.Extent * scale));
        }

        var tsc = ProcessCpu.CycleRate();
        var period = 1 / RefreshHz;
        const double warm = 2.0;
        var clock = Stopwatch.StartNew();
        var measuring = false;
        var cyclesStart = 0UL;
        Dictionary<int, (ulong Cycles, string Group)>? threadsStart = null;
        var wallStart = 0.0;
        var frames = 0;
        var buildTicks = 0L;
        var feedTicks = 0L;
        var drawTicks = 0L;
        var updateStart = 0L;
        (long Describe, long Paint, long Flush) layerStart = default;
        var kinds = Enum.GetValues<BoardLayerKind>();
        var kindStart = new long[kinds.Length * 2];
        var rasterStart = 0L;
        var events = 0;
        var byClass = new long[Enum.GetValues<FrameClass>().Length];
        var next = new long[rigs.Length];
        var demand = new FrameClass[rigs.Length];
        var dirty = new bool[rigs.Length];
        var presented = new ulong[rigs.Length];
        var skipped = 0;
        Array.Fill(demand, FrameClass.Fps60);
        Array.Fill(dirty, true);
        while (true)
        {
            var elapsed = clock.Elapsed.TotalSeconds;
            var now = 1000 + elapsed;
            var tick = (long)(elapsed / period);
            if (!measuring && elapsed >= warm)
            {
                measuring = true;
                frames = 0;
                buildTicks = 0;
                feedTicks = 0;
                drawTicks = 0;
                updateStart = rigs.Sum(r => r.Frame.UpdateTicks);
                layerStart = (rigs.Sum(r => r.Frame.Layers.Ticks.Describe), rigs.Sum(r => r.Frame.Layers.Ticks.Paint),
                    rigs.Sum(r => r.Frame.Layers.Ticks.Flush));
                for (var k = 0; k < kinds.Length; k++)
                {
                    kindStart[2 * k] = rigs.Sum(r => r.Frame.Layers.PaintsByKind.Ticks[k]);
                    kindStart[2 * k + 1] = rigs.Sum(r => r.Frame.Layers.PaintsByKind.Count[k]);
                }
                events = 0;
                skipped = 0;
                Array.Clear(byClass);
                rasterStart = rigs.Sum(r => r.Frame.Layers.Atlas.Rasterized);
                BoardGpuRenderer.Timer?.Drain();
                threadsStart = ProcessThreads.Snapshot();
                cyclesStart = ProcessCpu.Cycles();
                wallStart = clock.Elapsed.TotalSeconds;
            }

            if (measuring && elapsed - wallStart >= seconds)
            {
                break;
            }

            var sixty = demand.Count(d => d == FrameClass.Fps60);
            var rendered = false;
            var due = long.MaxValue;
            for (var i = 0; i < rigs.Length; i++)
            {
                var rig = rigs[i];
                rig.Feed.Sim.TargetTotal = scenario.Target(measuring ? elapsed - wallStart : 0);
                var fed = Stopwatch.GetTimestamp();
                var advanced = rig.Advance(now);
                feedTicks += Stopwatch.GetTimestamp() - fed;
                if (advanced)
                {
                    // A tick is a change: the board draws at least once, then follows its class.
                    dirty[i] = true;
                    events += measuring ? rig.LastTick?.Events.Count ?? 0 : 0;
                }

                var frameClass = FramePolicy.Crowded(demand[i], sixty, i == 0);
                if (frameClass == FrameClass.None && dirty[i])
                {
                    frameClass = FrameClass.Fps60;
                }

                var divisor = FramePolicy.Divisor(frameClass, RefreshHz);
                if (divisor == 0)
                {
                    continue;
                }

                if (tick < next[i])
                {
                    due = Math.Min(due, next[i]);
                    continue;
                }

                next[i] = next[i] + divisor > tick ? next[i] + divisor : tick + divisor;
                due = Math.Min(due, next[i]);
                var start = Stopwatch.GetTimestamp();
                if (!rig.Frame.Build(device, now, scale))
                {
                    return "the device failed";
                }

                buildTicks += Stopwatch.GetTimestamp() - start;
                demand[i] = rig.Scene.Demand(now);
                dirty[i] = false;
                var fingerprint = rig.Frame.Fingerprint(targets[i].Pixels);
                if (fingerprint == presented[i])
                {
                    // As the window's surface: an identical frame is never drawn again.
                    skipped += measuring ? 1 : 0;
                    continue;
                }

                presented[i] = fingerprint;
                var drawn = Stopwatch.GetTimestamp();
                targets[i].Draw(device, rig.Frame);
                drawTicks += Stopwatch.GetTimestamp() - drawn;
                byClass[(int)frameClass]++;
                frames++;
                rendered = true;
            }

            if (rendered)
            {
                device.Current.Context.Flush();
            }

            // Wake for the next frame due or the next sim tick (every 0.5 s), whichever is first, like the app's clock: an idle
            // board costs its ticks only, not a polling harness.
            var wait = (due == long.MaxValue ? double.MaxValue : due * period) - clock.Elapsed.TotalSeconds;
            foreach (var rig in rigs)
            {
                wait = Math.Min(wait, rig.NextTick - (1000 + clock.Elapsed.TotalSeconds));
            }

            wait = Math.Min(wait, measuring ? wallStart + seconds - clock.Elapsed.TotalSeconds : warm - clock.Elapsed.TotalSeconds);
            Thread.Sleep(Math.Clamp((int)Math.Floor(wait * 1000), 1, 500));
        }

        var wall = clock.Elapsed.TotalSeconds - wallStart;
        var cpu = (ProcessCpu.Cycles() - cyclesStart) / tsc / wall * 100;
        var threads = threadsStart is null ? string.Empty : ProcessThreads.Top(threadsStart, ProcessThreads.Snapshot(), wall);
        var gpu = BoardGpuRenderer.Timer?.Drain() ?? (0.0, 0);
        var rasterized = rigs.Sum(r => r.Frame.Layers.Atlas.Rasterized) - rasterStart;
        var updateTicks = rigs.Sum(r => r.Frame.UpdateTicks) - updateStart;
        var describeTicks = rigs.Sum(r => r.Frame.Layers.Ticks.Describe) - layerStart.Describe;
        var paintTicks = rigs.Sum(r => r.Frame.Layers.Ticks.Paint) - layerStart.Paint;
        var flushTicks = rigs.Sum(r => r.Frame.Layers.Ticks.Flush) - layerStart.Flush;
        var byKind = new System.Text.StringBuilder();
        for (var k = 0; k < kinds.Length; k++)
        {
            var count = rigs.Sum(r => r.Frame.Layers.PaintsByKind.Count[k]) - kindStart[2 * k + 1];
            if (count > 0)
            {
                var ms = (rigs.Sum(r => r.Frame.Layers.PaintsByKind.Ticks[k]) - kindStart[2 * k]) * 1000.0 / Stopwatch.Frequency;
                byKind.Append(CultureInfo.InvariantCulture, $" {kinds[k]} {count / wall:0.0}/s × {ms / count:0.00} ms;");
            }
        }

        if (rigs.Length == 0)
        {
            return string.Create(CultureInfo.InvariantCulture, $"cpu {cpu:0.00}% of one core; threads: {threads}");
        }

        var atlas = rigs[0].Frame.Layers.Atlas;
        var swap = targets[0].Pixels * targets[0].Pixels * 4.0 * 2;
        var memory = (atlas.Width * (double)atlas.Height * 4 + swap) / (1024 * 1024);
        var instances = rigs[0].Frame.List.Count;
        var batches = rigs[0].Frame.List.BatchCount;
        foreach (var target in targets)
        {
            target.Dispose();
        }

        foreach (var rig in rigs)
        {
            rig.Dispose();
        }

        var perFrame = frames + skipped > 0 ? buildTicks * 1000.0 / Stopwatch.Frequency / (frames + skipped) : 0;
        string Fps(FrameClass c) => (byClass[(int)c] / wall).ToString("0.0", CultureInfo.InvariantCulture);
        string Share(long ticks) => (ticks * 100.0 / Stopwatch.Frequency / wall).ToString("0.00", CultureInfo.InvariantCulture);
        return string.Create(CultureInfo.InvariantCulture,
            $"cpu {cpu:0.00}% of one core, {frames / wall:0.0} frames/s (60: {Fps(FrameClass.Fps60)}, 30: {Fps(FrameClass.Fps30)}; {skipped / wall:0.0}/s unchanged), build {perFrame:0.000} ms/frame, {rasterized / wall:0.0} layers rasterized/s, {events} events, gpu {gpu.Milliseconds:0.000} ms/frame ({gpu.Frames} timed), {instances} instances in {batches} batches, gpu memory {memory:0.0} MB per board (layer texture {atlas.Width}×{atlas.Height}); cpu on this thread: feed {Share(feedTicks)}%, layers {Share(updateTicks)}% (describe {Share(describeTicks)}, paint {Share(paintTicks)}, flush {Share(flushTicks)}), frames {Share(buildTicks - updateTicks)}%, draws {Share(drawTicks)}%; painted:{byKind} threads: {threads}");
    }

    private static double Number(string name, double fallback) =>
        double.TryParse(Environment.GetEnvironmentVariable(name), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value > 0
            ? value
            : fallback;

    /// <summary>An offscreen render target without readback.</summary>
    private sealed class Target : IDisposable
    {
        private readonly ID3D11Texture2D _texture;
        private readonly ID3D11RenderTargetView _view;

        public Target(GpuDevice device, int pixels)
        {
            Pixels = pixels;
            var r = device.Current;
            _texture = r.Device.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm, (uint)pixels, (uint)pixels, 1, 1,
                BindFlags.RenderTarget, ResourceUsage.Default, CpuAccessFlags.None, 1, 0, ResourceOptionFlags.None));
            _view = r.Device.CreateRenderTargetView(_texture);
        }

        public int Pixels { get; }

        public void Draw(GpuDevice device, BoardFrame frame) => frame.Draw(device, _view, Pixels);

        public void Dispose()
        {
            _view.Dispose();
            _texture.Dispose();
        }
    }
}
