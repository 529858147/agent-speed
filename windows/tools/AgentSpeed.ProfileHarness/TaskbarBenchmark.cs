using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AgentSpeed.Core;
using AgentSpeed.Interop;
using AgentSpeed.Presentation.Cost;
using AgentSpeed.Presentation.Formatting;
using AgentSpeed.Presentation.Heat;
using AgentSpeed.Presentation.Localization;
using AgentSpeed.Presentation.Settings;
using AgentSpeed.Taskbar;
using AgentSpeed.Theme;

namespace AgentSpeed.ProfileHarness;

/// <summary>Uses an invisible surrogate taskbar; never changes Explorer, real settings, logs or network access.</summary>
internal static class TaskbarBenchmark
{
    public static async Task Run(string output, Localizer loc, ThemeService theme, bool skipCaptures)
    {
        var settings = AppSettings.Default;
        var cost = new CostDisplay(loc);
        var generating = SpeedSnapshot.Empty with
        {
            State = MonitorState.Running, Live = new Rate(71, 142), LiveEstimatedShare = 0.25, GeneratingCount = 2,
            Sources = [Source(SourceKind.Codex, 54, 24_000_000), Source(SourceKind.ClaudeCode, 17, 4_060_000)],
        };
        var idle = generating with { Live = Rate.Zero, LiveEstimatedShare = 0, GeneratingCount = 0,
            Sources = generating.Sources.Select(s => s with { Live = Rate.Zero, LiveEstimated = Rate.Zero, LiveCost = default, ActiveAgents = 0 }).ToArray() };
        if (!skipCaptures)
        {
            foreach (var dark in new[] { true, false })
            {
                theme.Mode = dark ? AppearanceMode.Dark : AppearanceMode.Light;
                foreach (var scale in new[] { 1, 1.25, 1.5, 2 })
                {
                    var variants = new[]
                    {
                        ("generating", generating), ("tools", idle with { ToolsCount = 1 }),
                        ("waiting", idle with { GeneratingCount = 1 }), ("idle", idle),
                        ("unreadable", idle with { Warnings = [new MonitorWarning.UnreadableFiles(1)] }),
                    };
                    foreach (var (name, snapshot) in variants)
                    {
                        var state = State(snapshot, settings, cost, dark);
                        var image = TaskbarCapture.Render(state, state.Value, state.Tier, state.Redline, scale);
                        Save(OnBackground(image, dark), Path.Combine(output, $"taskbar-{(dark ? "dark" : "light")}-{scale:0.##}-{name}.png"));
                    }

                    var card = new HoverTooltip();
                    try
                    {
                        card.Show();
                        card.SetDetails(TaskbarDetails.From(generating, settings, cost));
                        card.UpdateLayout();
                        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(card.ActualWidth * scale), (int)Math.Ceiling(card.ActualHeight * scale),
                            96 * scale, 96 * scale, PixelFormats.Pbgra32);
                        bitmap.Render(card);
                        Save(bitmap, Path.Combine(output, $"tooltip-{(dark ? "dark" : "light")}-{scale:0.##}.png"));
                    }
                    finally { card.Close(); }
                }
            }
        }

        var placementChecks = new List<object>();
        var displays = Monitors.All();
        var placementCard = new HoverTooltip();
        try
        {
            foreach (var display in displays)
            {
                var work = display.WorkArea;
                var anchor = new RECT(work.Right - 300, work.Bottom, work.Right - 100, work.Bottom + 48);
                placementCard.ShowFor(anchor, TaskbarDetails.From(generating, settings, cost), reduceMotion: true);
                // Keep the test window invisible while exercising its real HWND placement on each physical display.
                placementCard.BeginAnimation(Window.OpacityProperty, null);
                placementCard.Opacity = 0;
                placementCard.UpdateLayout();
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                if (!User32.GetWindowRect(new WindowInteropHelper(placementCard).Handle, out var placed))
                    throw new InvalidOperationException("Could not read the placed tooltip window.");
                var shadow = 14 * display.Scale + 1;
                if (placed.Left < work.Left - shadow || placed.Right > work.Right + shadow
                    || placed.Top < work.Top - shadow || placed.Bottom > work.Bottom + shadow)
                    throw new InvalidOperationException("Tooltip escaped its physical monitor's work area.");
                placementChecks.Add(new { display.DeviceName, display.Scale, placed.Left, placed.Top, placed.Right, placed.Bottom });
            }
        }
        finally { placementCard.Close(); }
        File.WriteAllText(Path.Combine(output, "monitor-placement.json"), JsonSerializer.Serialize(placementChecks,
            new JsonSerializerOptions { WriteIndented = true }));

        var primary = displays.First(m => m.IsPrimary).WorkArea;
        var surrogate = new Window { Width = 1200, Height = 48, Left = primary.Left + 16, Top = primary.Top + 16, Opacity = 0,
            WindowStyle = WindowStyle.None, ShowActivated = false, ShowInTaskbar = false };
        surrogate.Show();
        var handle = new WindowInteropHelper(surrogate).Handle;
        WindowEffects.AddExtendedStyle(handle, WS_EX.NOACTIVATE | WS_EX.TOOLWINDOW | WS_EX.TRANSPARENT);
        TaskbarGeometry? Geometry()
        {
            if (!User32.GetWindowRect(handle, out var bounds)) return null;
            return new TaskbarGeometry(handle, 0, bounds, new RECT(bounds.Right - 180, bounds.Top, bounds.Right, bounds.Bottom), 96, false);
        }

        using var host = new TaskbarHost(_ => { }, Geometry);
        var stateIdle = State(idle, settings, cost, true);
        var tooltip = new HoverTooltip();
        try
        {
            host.Apply(stateIdle);
            await Task.Delay(2000);
            using var process = Process.GetCurrentProcess();
            process.Refresh();
            var beforeCpu = process.TotalProcessorTime;
            var beforeWorking = process.WorkingSet64;
            var beforePrivate = process.PrivateMemorySize64;
            var beforeAllocation = GC.GetTotalAllocatedBytes(precise: true);
            var beforePresent = host.PresentCount;
            var started = Stopwatch.GetTimestamp();
            var details = TaskbarDetails.From(idle, settings, cost);
            for (var i = 0; i < 40; i++)
            {
                host.Apply(stateIdle);
                if (tooltip.SetDetails(details)) throw new InvalidOperationException("Hidden tooltip updated its controls.");
                await Task.Delay(250);
            }

            process.Refresh();
            var duration = Stopwatch.GetElapsedTime(started).TotalSeconds;
            var cpu = (process.TotalProcessorTime - beforeCpu).TotalSeconds;
            var frames = host.PresentCount - beforePresent;
            File.WriteAllText(Path.Combine(output, "taskbar-benchmark.json"), JsonSerializer.Serialize(new
            {
                Scope = "Isolated WPF taskbar host in an invisible surrogate, no log monitor or real account; not whole-app memory",
                DurationSeconds = duration, CpuSeconds = cpu, CpuPercentOfOneCore = cpu / duration * 100,
                WorkingSetBefore = beforeWorking, WorkingSetAfter = process.WorkingSet64,
                PrivateBytesBefore = beforePrivate, PrivateBytesAfter = process.PrivateMemorySize64,
                AllocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - beforeAllocation,
                IdlePresents = frames, AnimationRunning = host.IsAnimating, HiddenTooltipUpdates = tooltip.ContentUpdates,
                Monitors = Monitors.All().Select(m => new { m.Bounds.Left, m.Bounds.Top, m.Bounds.Right, m.Bounds.Bottom, m.Scale }),
            }, new JsonSerializerOptions { WriteIndented = true }));
            if (frames != 0 || host.IsAnimating || tooltip.ContentUpdates != 0)
                throw new InvalidOperationException("Idle taskbar or hidden tooltip kept rendering.");
        }
        finally { tooltip.Close(); surrogate.Close(); }
    }

    private static SourceStatus Source(SourceKind kind, double speed, long amount) =>
        new(kind, true, "", true, 1, new Rate(speed, speed * 2), new TokenTotals(Requests: 2, Output: 100), 1)
        { LiveCost = new LiveCost(4900, speed), TodayCost = new CostTally(amount) };

    private static TaskbarState State(SpeedSnapshot snapshot, AppSettings settings, CostDisplay cost, bool dark)
    {
        var readout = TaskbarReadout.From(snapshot, settings.Metric, cost.Localizer);
        return TaskbarState.Initial with
        {
            Enabled = true, Value = snapshot.Live.Output, Tier = snapshot.Live.Output > 0 ? SpeedTier.Cruise : SpeedTier.Idle,
            TaskbarDark = dark, ReduceMotion = true, UnitText = cost.Localizer["unit.tokS"],
            ValueText = readout.Value, ValueMuted = readout.Muted, HideUnit = readout.HideUnit,
            ReservationText = cost.Localizer["win.taskbar.reserve"], Secondary = BarSecondary.From(settings, snapshot, cost),
        };
    }

    private static BitmapSource OnBackground(BitmapSource source, bool dark)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(dark ? Color.FromRgb(32, 32, 32) : Color.FromRgb(243, 243, 243)), null, new Rect(0, 0, source.Width, source.Height));
            dc.DrawImage(source, new Rect(0, 0, source.Width, source.Height));
        }

        var bitmap = new RenderTargetBitmap(source.PixelWidth, source.PixelHeight, source.DpiX, source.DpiY, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        return bitmap;
    }

    private static void Save(BitmapSource image, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var file = File.Create(path);
        encoder.Save(file);
    }
}
