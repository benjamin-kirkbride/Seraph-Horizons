using System.Diagnostics;
using System.Reflection;
using HarmonyLib;
using Vintagestory.Common;
using Vintagestory.Server;

namespace SeraphHorizons.PackTests;

/// <summary>
/// PROTOTYPE: runs the embedded server on a virtual clock, faster than real time. Every engine pass
/// (<c>ServerMain.Process</c>, which Atlas's pump calls back to back) advances the server's master clock
/// (<c>ServerMain.totalUnpausedTime</c>, behind <c>World.ElapsedMilliseconds</c>, every tick listener,
/// callback and server system's dt, and so physics' accumulator) by a fixed step instead of by the wall
/// clock, and the pass's pacing sleep is cut to at most 1 ms (<c>ServerConfig.TickTime</c>). The calendar's
/// own stopwatch is driven from the same clock. Off-thread work (chunk threads, Task.Run) still runs on
/// wall time: it looks slower in virtual time.
///
/// <para>How: the master clock is a public Stopwatch; it is kept stopped and its private <c>_elapsed</c>
/// raised a step before each pass (a Harmony prefix on Process), which the engine's own reads then see.
/// A pass while the server is suspended (autosave, rollback) does not advance it.</para>
/// </summary>
internal static class VirtualClock
{
    private static readonly FieldInfo StopwatchElapsed = typeof(Stopwatch).GetField("_elapsed", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new MissingFieldException(nameof(Stopwatch), "_elapsed");
    private static readonly AccessTools.FieldRef<GameCalendar, Stopwatch> CalendarWatch = AccessTools.FieldRefAccess<GameCalendar, Stopwatch>("watchIngameTime");
    private static readonly AccessTools.FieldRef<GameCalendar, TimeSpan> CalendarSpan = AccessTools.FieldRefAccess<GameCalendar, TimeSpan>("timespan");
    private static readonly AccessTools.FieldRef<ServerMain, GameCalendar> ServerCalendar = AccessTools.FieldRefAccess<ServerMain, GameCalendar>("GameWorldCalendar");

    private static Harmony? _harmony;
    private static ServerMain? _server;
    private static long _stepTicks;
    private static float _tickTime;

    /// <summary>Passes advanced on the virtual clock since <see cref="Enable"/>.</summary>
    public static long Passes { get; private set; }

    public static bool On => _server != null;

    /// <summary>Starts the virtual clock on <paramref name="server"/>: each pass from now on advances
    /// <paramref name="stepMs"/> of server time (the engine's nominal 33.3 ms by default).</summary>
    public static void Enable(ServerMain server, double stepMs = 1000.0 / 30)
    {
        if (_harmony == null)
        {
            _harmony = new Harmony("seraphhorizons.packtests.virtualclock");
            _harmony.Patch(AccessTools.Method(typeof(ServerMain), nameof(ServerMain.Process)),
                prefix: new HarmonyMethod(typeof(VirtualClock), nameof(BeforePass)));
        }
        _stepTicks = (long)Math.Round(stepMs * Stopwatch.Frequency / 1000.0);
        server.totalUnpausedTime.Stop();
        _tickTime = server.Config.TickTime;
        server.Config.TickTime = 1;
        Passes = 0;
        _server = server;
    }

    /// <summary>Back to real time, the clock going on from where the virtual one left it.</summary>
    public static void Disable()
    {
        if (_server is not { } server)
            return;
        _server = null;
        server.Config.TickTime = _tickTime;
        if (!server.Suspended)
            server.totalUnpausedTime.Start();
    }

    private static long Raw(Stopwatch w) => (long)StopwatchElapsed.GetValue(w)!;

    private static void BeforePass(ServerMain __instance)
    {
        if (__instance != _server || __instance.Suspended)
            return;
        var clock = __instance.totalUnpausedTime;
        // the engine's Suspend(false) starts it again: keep it stopped, on the virtual count
        clock.Stop();
        StopwatchElapsed.SetValue(clock, Raw(clock) + _stepTicks);
        AdvanceCalendar(__instance);
        Passes++;
    }

    // The calendar adds its own watch's real time at each Tick (every 200 ms of server time) and restarts it.
    // A Harmony prefix on GameCalendar.Tick was bypassed in practice (most likely the JIT had inlined the
    // small virtual method into ServerSystemCalendar.OnServerTick by guarded devirtualisation). Instead each
    // pass adds the step to in-game time itself and restarts the watch, which leaves Tick well under 1 ms
    // of real time to add (ElapsedMilliseconds floors it to 0).
    private static void AdvanceCalendar(ServerMain server)
    {
        var calendar = ServerCalendar(server);
        if (calendar == null)
            return;
        var watch = CalendarWatch(calendar);
        if (!watch.IsRunning)
            return;
        double seconds = _stepTicks / (double)Stopwatch.Frequency;
        CalendarSpan(calendar) += TimeSpan.FromSeconds(seconds * calendar.SpeedOfTime * calendar.CalendarSpeedMul);
        watch.Restart();
    }
}
