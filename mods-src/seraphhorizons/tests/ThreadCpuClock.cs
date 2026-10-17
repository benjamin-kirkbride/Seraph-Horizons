using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SeraphHorizons.Mod.Tests;

/// <summary>The calling thread's CPU time, for tests that bound how long a piece of work takes. CI's
/// shared runners are oversubscribed (xunit runs test classes in parallel), so the wall clock also counts
/// the thread being descheduled, a 1 ms piece of work measuring 13 ms or 40 ms. Thread CPU time
/// (<c>CLOCK_THREAD_CPUTIME_ID</c>) leaves that out. Linux only (CI and the dev machines), the wall
/// clock elsewhere. Time the work as the fastest of a few runs so a GC pause on the thread doesn't
/// count either.</summary>
public static class ThreadCpuClock
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Timespec
    {
        public long Sec;
        public long Nsec;
    }

    [DllImport("libc", EntryPoint = "clock_gettime")]
    private static extern int ClockGetTime(int clock, out Timespec time);

    private const int ClockThreadCpuTime = 3; // CLOCK_THREAD_CPUTIME_ID

    private static readonly Stopwatch Wall = Stopwatch.StartNew();

    /// <summary>"thread CPU" or "wall": what <see cref="Ms"/> reads, for a test's output.</summary>
    public static string Kind => OperatingSystem.IsLinux() ? "thread CPU" : "wall";

    /// <summary>The calling thread's CPU time in ms on Linux, the wall clock elsewhere.</summary>
    public static double Ms()
    {
        if (OperatingSystem.IsLinux() && ClockGetTime(ClockThreadCpuTime, out var t) == 0)
            return t.Sec * 1000.0 + t.Nsec / 1e6;
        return Wall.Elapsed.TotalMilliseconds;
    }

    /// <summary>The fastest of <paramref name="runs"/> runs of <paramref name="work"/>, in ms of
    /// <see cref="Ms"/>.</summary>
    public static double Fastest(int runs, Action work)
    {
        double fastest = double.MaxValue;
        for (int i = 0; i < runs; i++)
        {
            double start = Ms();
            work();
            fastest = Math.Min(fastest, Ms() - start);
        }
        return fastest;
    }
}
