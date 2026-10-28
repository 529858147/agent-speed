using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AgentSpeed.Settings;

namespace AgentSpeed.ProfileHarness;

internal static class SettingsBenchmark
{
    public static async Task Run(string output, bool eager)
    {
        using var process = Process.GetCurrentProcess();
        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        var started = Stopwatch.GetTimestamp();
        var pages = new SettingsPageHost();
        if (eager)
        {
            foreach (var key in new[] { "general", "taskbar", "orb", "effects", "cost", "traffic", "report", "sources", "about" })
                pages.ShowPage(key);
        }

        pages.ShowPage("sources");
        var window = new Window
        {
            Width = 900, Height = 680, Left = -10000, Top = -10000,
            ShowInTaskbar = false, ShowActivated = false,
            Content = new ScrollViewer { Content = pages },
        };
        window.Show();
        window.UpdateLayout();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        var allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
        await Task.Delay(1000);
        process.Refresh();
        File.WriteAllText(Path.Combine(output, "settings-benchmark.json"), JsonSerializer.Serialize(new
        {
            Eager = eager, PagesCreated = pages.Children.Count, AllocatedBytes = allocated,
            ReadyMilliseconds = elapsed, WorkingSetBytes = process.WorkingSet64,
            PrivateBytes = process.PrivateMemorySize64, CpuSeconds = process.TotalProcessorTime.TotalSeconds,
        }));
        window.Close();
    }
}
