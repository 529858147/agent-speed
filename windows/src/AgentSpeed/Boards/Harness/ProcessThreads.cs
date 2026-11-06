using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace AgentSpeed.Boards.Harness;

/// <summary>
/// The harnesses' per-thread CPU breakdown: cycles of every thread of the process between two snapshots, grouped by the
/// thread's name (.NET names its workers), the caller's thread, or else the module its start address lies in (the graphics
/// driver's threads; the single-file app's own module holds the runtime's unnamed threads), so a bench tells the board's own
/// work from the driver's and the runtime's.
/// </summary>
internal static class ProcessThreads
{
    private const uint ThreadQueryInformation = 0x0040;
    private const int ThreadQuerySetWin32StartAddress = 9;

    /// <summary>Cycles per thread id, with the thread's group name.</summary>
    public static Dictionary<int, (ulong Cycles, string Group)> Snapshot()
    {
        var result = new Dictionary<int, (ulong, string)>();
        using var process = Process.GetCurrentProcess();
        var modules = new List<(long Start, long End, string Name)>();
        try
        {
            foreach (ProcessModule module in process.Modules)
            {
                var start = module.BaseAddress.ToInt64();
                modules.Add((start, start + module.ModuleMemorySize, module.ModuleName));
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }

        var main = NativeMethods.GetCurrentThreadId();
        foreach (ProcessThread thread in process.Threads)
        {
            var handle = NativeMethods.OpenThread(ThreadQueryInformation, false, (uint)thread.Id);
            if (handle == IntPtr.Zero)
            {
                continue;
            }

            try
            {
                if (NativeMethods.QueryThreadCycleTime(handle, out var cycles))
                {
                    result[thread.Id] = (cycles, thread.Id == main ? "harness thread" : Group(handle, modules));
                }
            }
            finally
            {
                NativeMethods.CloseHandle(handle);
            }
        }

        return result;
    }

    /// <summary>
    /// The groups that spent the most between <paramref name="from"/> and <paramref name="to"/> over <paramref name="seconds"/>,
    /// as "name x.xx%" (percent of one core), the busiest first.
    /// </summary>
    public static string Top(Dictionary<int, (ulong Cycles, string Group)> from, Dictionary<int, (ulong Cycles, string Group)> to, double seconds,
        int count = 6)
    {
        var groups = new Dictionary<string, ulong>(StringComparer.Ordinal);
        foreach (var (id, now) in to)
        {
            var spent = from.TryGetValue(id, out var before) ? now.Cycles - before.Cycles : now.Cycles;
            groups[now.Group] = groups.GetValueOrDefault(now.Group) + spent;
        }

        var text = new StringBuilder();
        foreach (var (group, cycles) in groups.OrderByDescending(g => g.Value).Take(count))
        {
            var percent = ProcessCpu.Percent(0, cycles, seconds);
            if (percent < 0.005)
            {
                break;
            }

            text.Append(CultureInfo.InvariantCulture, $"{(text.Length > 0 ? ", " : string.Empty)}{group} {percent:0.00}%");
        }

        return text.ToString();
    }

    private static string Group(IntPtr handle, List<(long Start, long End, string Name)> modules)
    {
        if (NativeMethods.GetThreadDescription(handle, out var description) >= 0 && description != IntPtr.Zero)
        {
            try
            {
                var name = Marshal.PtrToStringUni(description);
                if (!string.IsNullOrWhiteSpace(name))
                {
                    return name;
                }
            }
            finally
            {
                NativeMethods.LocalFree(description);
            }
        }

        if (NativeMethods.NtQueryInformationThread(handle, ThreadQuerySetWin32StartAddress, out var start, IntPtr.Size, IntPtr.Zero) == 0)
        {
            var address = start.ToInt64();
            foreach (var (from, end, name) in modules)
            {
                if (address >= from && address < end)
                {
                    return name;
                }
            }
        }

        return "?";
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenThread(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint threadId);

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool QueryThreadCycleTime(IntPtr thread, out ulong cycles);

        [DllImport("kernel32.dll")]
        public static extern int GetThreadDescription(IntPtr thread, out IntPtr description);

        [DllImport("kernel32.dll")]
        public static extern int GetCurrentThreadId();

        [DllImport("kernel32.dll")]
        public static extern IntPtr LocalFree(IntPtr memory);

        [DllImport("ntdll.dll")]
        public static extern int NtQueryInformationThread(IntPtr thread, int informationClass, out IntPtr information, int length, IntPtr returned);
    }
}
