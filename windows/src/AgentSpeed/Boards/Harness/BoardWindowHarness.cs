using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AgentSpeed.Boards.Gpu;
using AgentSpeed.Boards.Layers;
using AgentSpeed.Interop;
using AgentSpeed.Orb.Gpu;
using AgentSpeed.Orb.Render;
using AgentSpeed.Presentation.Boards;
using AgentSpeed.Presentation.Boards.Demo;
using AgentSpeed.Presentation.Boards.Effects;
using AgentSpeed.Presentation.Layout;
using AgentSpeed.Presentation.Localization;
using AgentSpeed.Presentation.Orb;
using AgentSpeed.Presentation.Settings;

namespace AgentSpeed.Boards.Harness;

/// <summary>
/// Developer harness <c>AGENT_SPEED_BOARD_WINDOW=FILE</c>: one real board window (click-through, bottom-right of the primary
/// work area) on the orbs' clock and the compositor ticker, presenting through its composition swap chain while the demo
/// runs: the process's baseline with no board shown and no data, a warm-up, then redline (with the team's overtakes and new
/// leaders), cruise and idle, then hidden with and without data, then closed. Per phase it reports the process CPU, the demo
/// feed's share of it (the simulation is the harness's cost, not the board's), JIT and GC activity, what was presented per
/// second, the frames built per class, frames skipped as unchanged or refused by a busy compositor; hidden must build
/// nothing. <c>FILE.png</c> is a capture at the end of redline. Runs next to the installed app.
/// <c>AGENT_SPEED_BOARD_WINDOW_STYLE</c>, <c>_SIZE</c> (default S) and <c>_SECONDS</c> (per measured phase, default 5) adjust it.
/// </summary>
internal static class BoardWindowHarness
{
    private sealed record Phase(string Name, double Seconds, double? Target, bool Quiet = false, bool Measured = true, bool Hidden = false,
        bool Ticks = true);

    public static int Run(string path)
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        if (!GpuDevice.Initialize() || GpuDevice.Shared is not { } device)
        {
            File.WriteAllText(path, "Direct3D 11 unavailable: " + GpuDevice.Failure);
            return 2;
        }

        var style = BoardStyles.Parse(Environment.GetEnvironmentVariable("AGENT_SPEED_BOARD_WINDOW_STYLE") ?? "neon");
        var size = BoardSizes.Parse(Environment.GetEnvironmentVariable("AGENT_SPEED_BOARD_WINDOW_SIZE") ?? "S");
        var seconds = double.TryParse(Environment.GetEnvironmentVariable("AGENT_SPEED_BOARD_WINDOW_SECONDS"), NumberStyles.Float,
            CultureInfo.InvariantCulture, out var s) && s > 0 ? Math.Min(s, 30) : 5;
        Phase[] phases =
        [
            new("baseline", 3, 0, Quiet: true, Hidden: true, Ticks: false),
            // Tiered JIT promotes a method after 30 calls: once-per-tick code needs 15 s of ticks (DESIGN 2c measures after warm-up).
            new("warm-up", 16, 3200, Measured: false),
            new("redline", seconds, 3200),
            new("settle", 1, 700, Measured: false),
            new("cruise", seconds, 700),
            new("settle", 2, 0, Quiet: true, Measured: false),
            new("idle", seconds, 0, Quiet: true),
            new("hidden", 5, 3200, Hidden: true),
            new("hidden", 5, 3200, Hidden: true),
            new("hidden, no data", 5, 0, Quiet: true, Hidden: true, Ticks: false),
        ];
        var loc = new Localizer(Localizer.LoadEmbeddedTable(), LanguageCodes.ZhHant);
        ProcessCpu.RaiseToNormal();
        var watch = Stopwatch.StartNew();
        var clock = new OrbClock(dispatcher, () => 1000 + watch.Elapsed.TotalSeconds);
        clock.UseCompositorTicker();
        var monitor = Monitors.Primary();
        var work = monitor?.WorkRect ?? new RectD(0, 0, 1920, 1080);
        var scale = monitor?.Scale ?? 1;
        var half = BoardPlacement.HalfSide(size, scale);
        var inset = (OrbGeometry.DefaultInset + BoardCanvas.Margin) * scale;
        var window = new GpuBoardWindow("harness", device, clock, style, size, BoardPainters.Create(style, loc));
        window.Configure(style, size, new BoardEffectsPolicy(EffectsIntensity.Standard), new BoardWindowOptions(AlwaysOnTop: true, ClickThrough: true));
        window.MoveTo(new PointD(work.Right - inset - half, work.Bottom - inset - half));

        // Every live metric on, as a new board has them (DESIGN 2c, Live metrics: Frame policy).
        var feed = new BoardDemoFeed(BoardSimScenario.Team) { Localizer = loc, Readouts = BoardReadouts.All };
        var report = new System.Text.StringBuilder();
        report.AppendLine(CultureInfo.InvariantCulture,
            $"adapter: {device.AdapterName}, {BoardStyles.Key(style)} {BoardSizes.Key(size)}, scale {scale}, clock {clock.Source} at {clock.RefreshHz:0} Hz");
        var events = 0;
        var feedTicks = 0L;
        var ticker = new DispatcherTimer(TimeSpan.FromSeconds(BoardSim.TickSeconds), DispatcherPriority.Normal, (_, _) =>
        {
            // The demo's simulation and ranking are the harness's cost, not the board's: timed apart.
            var start = Stopwatch.GetTimestamp();
            var tick = feed.Step(style, size);
            feedTicks += Stopwatch.GetTimestamp() - start;
            events += tick.Events.Count;
            window.Apply(tick);
        }, dispatcher);

        _ = ProcessCpu.CycleRate();
        RunPhase(0);
        Dispatcher.Run();
        return 0;

        void RunPhase(int index)
        {
            if (index == phases.Length)
            {
                ticker.Stop();
                window.Dispose();
                File.WriteAllText(path, report.ToString());
                dispatcher.InvokeShutdown();
                return;
            }

            var phase = phases[index];
            feed.Sim.Quiet = phase.Quiet;
            feed.Sim.TargetTotal = phase.Target;
            if (phase.Hidden)
            {
                window.Hide();
            }
            else if (!window.IsVisible)
            {
                window.Show();
            }

            ticker.IsEnabled = phase.Ticks;

            var threads = ProcessThreads.Snapshot();
            var cycles = ProcessCpu.Cycles();
            var presented = BoardSurface.FramesPresented;
            var skipped = BoardSurface.FramesSkipped;
            var busy = BoardSurface.FramesBusy;
            var byClass = (long[])BoardSurface.FramesByClass.Clone();
            var rasterized = window.Surface.Frame.Layers.Atlas.Rasterized;
            var eventsBefore = events;
            var feedBefore = feedTicks;
            var jitBefore = System.Runtime.JitInfo.GetCompilationTime();
            var gcBefore = GC.CollectionCount(0) + GC.CollectionCount(1) + GC.CollectionCount(2);
            var started = watch.Elapsed.TotalSeconds;
            After(dispatcher, phase.Seconds, () =>
            {
                var wall = watch.Elapsed.TotalSeconds - started;
                var cpu = ProcessCpu.Percent(cycles, ProcessCpu.Cycles(), wall);
                var top = ProcessThreads.Top(threads, ProcessThreads.Snapshot(), wall);
                string Fps(FrameClass c) => ((BoardSurface.FramesByClass[(int)c] - byClass[(int)c]) / wall).ToString("0.0", CultureInfo.InvariantCulture);
                var built = BoardSurface.FramesPresented + BoardSurface.FramesSkipped + BoardSurface.FramesBusy - presented - skipped - busy;
                var feedShare = (feedTicks - feedBefore) * 100.0 / Stopwatch.Frequency / wall;
                var runtime = string.Create(CultureInfo.InvariantCulture,
                    $"jit {(System.Runtime.JitInfo.GetCompilationTime() - jitBefore).TotalMilliseconds:0} ms, {GC.CollectionCount(0) + GC.CollectionCount(1) + GC.CollectionCount(2) - gcBefore} gc");
                if (phase.Hidden)
                {
                    report.AppendLine(CultureInfo.InvariantCulture,
                        $"{phase.Name} (hidden) {wall:0.0} s: {built} frames built, cpu {cpu:0.00}% of one core (demo feed {feedShare:0.00}%; {runtime}); threads: {top}");
                }
                else if (phase.Measured)
                {
                    report.AppendLine(CultureInfo.InvariantCulture,
                        $"{phase.Name} {wall:0.0} s: cpu {cpu:0.00}% of one core (demo feed {feedShare:0.00}%; {runtime}), {(BoardSurface.FramesPresented - presented) / wall:0.0} presented/s, built per class 60: {Fps(FrameClass.Fps60)}/s, 30: {Fps(FrameClass.Fps30)}/s, none: {Fps(FrameClass.None)}/s; {BoardSurface.FramesSkipped - skipped} unchanged, {BoardSurface.FramesBusy - busy} busy, {events - eventsBefore} events, {window.Surface.Frame.Layers.Atlas.Rasterized - rasterized} layers rasterized; threads: {top}");
                }

                if (phase.Name == "redline")
                {
                    Save(window, clock, path + ".png", report);
                }

                File.WriteAllText(path, report.ToString());
                RunPhase(index + 1);
            });
        }
    }

    private static void After(Dispatcher dispatcher, double seconds, Action action)
    {
        var timer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher) { Interval = TimeSpan.FromSeconds(seconds) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            action();
        };
        timer.Start();
    }

    private static void Save(GpuBoardWindow window, OrbClock clock, string path, System.Text.StringBuilder report)
    {
        using var target = new OffscreenTarget();
        if (window.Surface.Capture(clock.Now, target) is not { } bitmap)
        {
            report.AppendLine("capture failed");
            return;
        }

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
