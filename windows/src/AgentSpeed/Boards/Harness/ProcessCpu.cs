using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AgentSpeed.Boards.Harness;

/// <summary>The board harnesses' CPU measure: cycles of every thread of the process, exact unlike the tick-sampled time.</summary>
internal static class ProcessCpu
{
    private static double s_rate;

    public static ulong Cycles() => NativeMethods.QueryProcessCycleTime(NativeMethods.GetCurrentProcess(), out var cycles) ? cycles : 0;

    /// <summary>
    /// The cycle counter's rate, measured once by a thread spinning: the best of ten 40 ms spins, since a spin the scheduler
    /// interrupts (other programs busy on the machine) counts fewer cycles and would inflate every percentage.
    /// </summary>
    public static double CycleRate()
    {
        if (s_rate > 0)
        {
            return s_rate;
        }

        var thread = NativeMethods.GetCurrentThread();
        for (var i = 0; i < 10; i++)
        {
            NativeMethods.QueryThreadCycleTime(thread, out var c0);
            var watch = Stopwatch.StartNew();
            while (watch.Elapsed.TotalMilliseconds < 40)
            {
            }

            NativeMethods.QueryThreadCycleTime(thread, out var c1);
            s_rate = Math.Max(s_rate, (c1 - c0) / watch.Elapsed.TotalSeconds);
        }

        return s_rate;
    }

    /// <summary>Percent of one core between two <see cref="Cycles"/> readings <paramref name="seconds"/> apart.</summary>
    public static double Percent(ulong from, ulong to, double seconds) => seconds > 0 ? (to - from) / CycleRate() / seconds * 100 : 0;

    /// <summary>
    /// Runs the harness at normal priority: Task Scheduler starts tasks below normal, where any busy program starves the
    /// harness and its numbers measure the scheduler instead of the board.
    /// </summary>
    public static void RaiseToNormal()
    {
        using var process = Process.GetCurrentProcess();
        if (process.PriorityClass is ProcessPriorityClass.BelowNormal or ProcessPriorityClass.Idle)
        {
            process.PriorityClass = ProcessPriorityClass.Normal;
        }
    }

    /// <summary>1 ms timer resolution while a harness sleeps between frames.</summary>
    public static uint BeginTimerPeriod() => NativeMethods.timeBeginPeriod(1);

    public static uint EndTimerPeriod() => NativeMethods.timeEndPeriod(1);

    private static class NativeMethods
    {
        [DllImport("winmm.dll")]
        public static extern uint timeBeginPeriod(uint period);

        [DllImport("winmm.dll")]
        public static extern uint timeEndPeriod(uint period);

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool QueryProcessCycleTime(IntPtr process, out ulong cycles);

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool QueryThreadCycleTime(IntPtr thread, out ulong cycles);

        [DllImport("kernel32.dll")]
        public static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll")]
        public static extern IntPtr GetCurrentThread();
    }
}
