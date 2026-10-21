using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Atlas.Api;
using HarmonyLib;
using SeraphHorizons.Mod.Eidolon;
using Vintagestory.API.Server;
using Vintagestory.Common;
using Vintagestory.Server;
using Vintagestory.Server.Systems;
using Xunit.Sdk;

[assembly: SeraphHorizons.PackTests.VirtualClockGuard]

namespace SeraphHorizons.PackTests;

/// <summary>
/// Runs the embedded server on a virtual clock, faster than real time (#960; how to use it and when not to:
/// tests/PackTests/README.md). Opt in per scenario, after the world is set up:
/// <code>using var clock = VirtualClock.Start(World);</code>
/// From then on every engine pass (<c>ServerMain.Process</c>, which Atlas's pump calls back to back) moves the
/// server's master clock (<c>ServerMain.totalUnpausedTime</c>: <c>World.ElapsedMilliseconds</c>, every tick
/// listener, callback and server system's dt, and so physics' accumulator) on by exactly <see cref="StepMs"/>,
/// the engine's nominal tick, and the pass does not sleep; the calendar moves on by the same step. Dispose
/// goes back to real time, the clock going on from where the virtual one left it.
///
/// <para>How: the master clock is a public Stopwatch, kept stopped, its private <c>_elapsed</c> set before
/// each pass (a Harmony prefix on Process), which the engine's own reads then see; a transpiler on Process
/// skips its pacing <c>Thread.Sleep</c> while a clock is on. A pass while the server is suspended (autosave,
/// rollback) does not advance it. Before advancing, each pass waits for physics' off-thread sender
/// (<c>PhysicsManager</c>'s <c>physicsManagerHelper</c> thread) to be idle, which closes the engine's race
/// between it and the main thread rebuilding its client list (<c>Collection was modified</c> in
/// <c>SendAttributesViaTCP</c>, which passes 1 ms apart expose), and for every hold
/// (<see cref="HoldWhile"/>) to clear: by default one while any eidolon mine plan is being made on its
/// worker (<see cref="MinePlanHold"/>), so no game time passes during a plan. While it is on, the planner's
/// main-thread share of a tick is counted in blocks read, not wall time
/// (<see cref="EidolonMinePlans.BudgetReads"/>, <see cref="EidolonMinePlans.TypicalReads"/>), so a job's
/// window is read in the same ticks on any runner.</para>
///
/// <para>Each run plays alike (tests/PackTests/README.md, *Every run alike*): <see cref="Start"/> sets every phase the
/// engine keeps in game time from itself alone (the master clock on a whole millisecond, every system's and tick
/// listener's last call, physics' accumulators), physics on the game thread alone, the game thread's
/// <c>World.Rand</c> seeded (<see cref="RandSeed"/>) and the path traversers and unstable rock drawing from streams
/// of their own (<see cref="TraverserRand"/>, <see cref="BlockRand"/>), random block ticks picked on the game thread
/// in game time, the eidolon mine planner's workers inline (<see cref="RunInline"/>) and autosave off.</para>
///
/// <para>Every engine member reached by reflection, and the transpiler's pattern, is checked at
/// <see cref="Start"/>, which throws <see cref="NotSupportedException"/> naming what is missing (a game or
/// .NET update can rename them). A scenario that ends with its clock still on (no <c>using</c>) fails and
/// the clock is stopped (<see cref="VirtualClockGuardAttribute"/>), so it never leaks into the next one.</para>
///
/// <para>The patch does not outlive a server: MadMechanics, MadFoundation and MadHoarding call
/// <c>Harmony.UnpatchAll(null)</c> in their Dispose, which takes off every Harmony patch in the process, ours
/// too, when a class's server stops. So <see cref="Start"/> checks for it each time and patches again. Without
/// the prefix the stopped master clock would never move again and every tick wait would hang until the
/// scenario's watchdog (#960, CI run 38679326640). Should the patch go while a clock runs, a watch on a timer
/// sees passes running with no prefix, records a fault and goes back to real time, so the scenario fails
/// with the reason instead of hanging.</para>
/// </summary>
public sealed class VirtualClock : IDisposable
{
    /// <summary>The game time each pass moves on: the engine's nominal tick (<c>ServerConfig.TickTime</c>).
    /// <c>World.Until(..., ticks)</c> bounds are ticks × this of game time.</summary>
    public const double StepMs = 1000.0 / 30;

    /// <summary>How long a hold may keep the clock still, in wall time, before it is dropped as a fault.</summary>
    public static readonly TimeSpan DefaultHoldTimeout = TimeSpan.FromSeconds(60);

    // physics' sender is a few ms of work: this long busy means it is stuck
    private static readonly TimeSpan PhysicsTimeout = TimeSpan.FromSeconds(10);

    // passes running (the engine's per-pass watch restarted within this) with no prefix for PatchLostAfter:
    // the patch is gone
    private static readonly TimeSpan PassesRunning = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan PatchLostAfter = TimeSpan.FromSeconds(3);

    /// <summary>The Harmony id of its patch on Process.</summary>
    internal const string HarmonyId = "seraphhorizons.packtests.virtualclock";

    private static readonly object Gate = new();
    private static Engine? _engine;
    private static VirtualClock? _current;

    private readonly Engine _e;
    private readonly ServerMain _server;
    private object? _physics;
    private readonly long _step;
    private long _elapsed;
    private readonly List<Hold> _holds = new();
    private readonly List<string> _faults = new();
    private bool _disposed;
    private System.Threading.Timer? _watch;
    private long _lastPrefix = Environment.TickCount64;
    private int? _readsBefore;
    private object? _autosaveBefore;

    /// <summary>The clock running now, if any.</summary>
    public static VirtualClock? Current
    {
        get { lock (Gate) return _current; }
    }

    /// <summary>The server's api.</summary>
    public ICoreServerAPI Api { get; }

    /// <summary>Where it was started (file:line), for the messages.</summary>
    public string StartedAt { get; }

    /// <summary>Passes advanced since <see cref="Start"/>.</summary>
    public long Passes { get; private set; }

    private VirtualClock(Engine e, ServerMain server, ICoreServerAPI api, object? physics, string startedAt)
    {
        _e = e;
        _server = server;
        _physics = physics;
        Api = api;
        StartedAt = startedAt;
        _step = (long)Math.Round(StepMs * Stopwatch.Frequency / 1000.0);
    }

    /// <summary>Starts the virtual clock on <paramref name="world"/>'s server (call it on the game thread, in
    /// the scenario body, after the world is set up). With <paramref name="holdMinePlans"/> (the default) no
    /// game time passes while an eidolon mine plan is made off the main thread (<see cref="MinePlanHold"/>);
    /// nothing is held when the Eidolon switch is off.</summary>
    /// <exception cref="InvalidOperationException">A clock is already on.</exception>
    /// <exception cref="NotSupportedException">An engine member it needs is missing (a game or .NET update).</exception>
    public static VirtualClock Start(IWorldSession world, bool holdMinePlans = true,
        [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        var api = world.Api;
        var server = api.World as ServerMain
            ?? throw new NotSupportedException($"the virtual clock needs the engine's ServerMain; the world is {api.World.GetType().FullName}");
        string at = $"{Path.GetFileName(file)}:{line}";
        lock (Gate)
        {
            if (_current != null)
                throw new InvalidOperationException($"a virtual clock is already on (started at {_current.StartedAt}); clocks do not nest");
            var e = _engine ??= Engine.Resolve();
            // a mod's UnpatchAll(null) at the last server's stop took it off
            e.EnsurePatched();
            var clock = new VirtualClock(e, server, api, e.Physics(server), at);
            if (holdMinePlans && MinePlanHold.For(api) is { } planning)
                clock._holds.Add(new Hold(clock, planning, "an eidolon mine plan made off the main thread", DefaultHoldTimeout));
            var watch = server.totalUnpausedTime;
            watch.Stop();
            clock._elapsed = (long)e.Elapsed.GetValue(watch)!;
            clock.Align(watch);
            _current = clock;
            clock._readsBefore = EidolonMinePlans.BudgetReads;
            // no autosave while it runs: one falls at a game time set by how long the server ran before Start, and
            // while it saves the engine runs the master clock on wall time (two runs parted after one at 459 s)
            clock._autosaveBefore = e.Autosave.GetValue(null);
            e.Autosave.SetValue(null, Convert.ChangeType(0, e.Autosave.FieldType));
            EidolonMinePlans.BudgetReads = EidolonMinePlans.TypicalReads;
            clock._watch = new System.Threading.Timer(_ => clock.WatchPatch(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
            return clock;
        }
    }

    private static readonly ConditionalWeakTable<Vintagestory.API.Common.Entities.Entity, Random> TraverserRands = new();

    /// <summary>The random numbers a path traverser draws for <paramref name="entity"/> (a Harmony transpiler puts
    /// this in place of its <c>entity.World.Rand</c>): while a clock runs, its own, seeded from <see cref="RandSeed"/>
    /// and the order its first draw came in since the clock started (not its id, which depends on what else
    /// spawned before), so its walks turn alike every run; else the world's, as the game has it.</summary>
    public static Random TraverserRand(Vintagestory.API.Common.Entities.Entity entity) =>
        _current is not { } clock ? entity.World.Rand
            : TraverserRands.GetValue(entity, _ => new Random(RandSeed * 7919 + ++clock._traversers));

    private int _traversers;

    private Random _blockRand = new(RandSeed);

    /// <summary>The eidolon mine planner's <c>Task.Run(func)</c> (a Harmony transpiler puts this in its place): while a
    /// clock runs, <paramref name="func"/> run there and then on the game thread, so a plan or a route solve is always
    /// done before anything in its pass could cancel it; else on the thread pool, as the game has it.</summary>
    public static Task<T> RunInline<T>(Func<T> func) => _current == null ? Task.Run(func) : Task.FromResult(func());

    /// <summary>The random numbers the game's unstable rock and falling ground draw (a Harmony transpiler puts this in
    /// place of their <c>world.Rand</c>): while a clock runs, its own stream, seeded at <see cref="Start"/>, so a
    /// cave-in comes alike every run; else the world's.</summary>
    public static Random BlockRand(Vintagestory.API.Common.IWorldAccessor world) => _current?._blockRand ?? world.Rand;

    /// <summary>The seed the game thread's <c>World.Rand</c> and the clock's own streams are given at
    /// <see cref="Start"/>: 1, or <c>SH_CLOCK_SEED</c> from the environment (to measure a scenario over several runs
    /// that differ only in it: each seed plays the same every run).</summary>
    public static int RandSeed => int.TryParse(Environment.GetEnvironmentVariable("SH_CLOCK_SEED"), out int seed) ? seed : 1;

    // Every phase the engine keeps in game time made the same at Start, so what happens from here depends on game
    // time since Start alone, not on how much wall time came before (setting the world up, the runner's load): the
    // master clock on a whole millisecond (each pass then 33, 33, 34 ms the same way), every server system's last
    // tick, every tick listener's last call and physics' accumulators set to it, and the game thread's World.Rand
    // seeded. Each run of a scenario then steps alike, its eidolons' positions the same to the last digit
    // (two hillside runs once parted at the fourth decimal on their first step).
    private void Align(Stopwatch watch)
    {
        long perMs = Stopwatch.Frequency / 1000;
        _elapsed = (_elapsed / perMs + 1) * perMs;
        _e.Elapsed.SetValue(watch, _elapsed);
        long now = _elapsed / perMs;
        foreach (var system in (ServerSystem[])_e.Systems.GetValue(_server)!)
            system.millisecondsSinceStart = now;
        foreach (var field in new[] { _e.ListenersEntity, _e.ListenersBlock })
            if (field.GetValue(_server.EventManager) is IEnumerable listeners)
                foreach (var listener in listeners)
                    if (listener is GameTickListenerBase l)
                        l.LastUpdateMilliseconds = now;
        if (((ServerSystem[])_e.Systems.GetValue(_server)!).OfType<ServerUdpNetwork>().FirstOrDefault()?.physicsManager is { } physics)
        {
            _e.PhysicsAccum.SetValue(physics, 0f);
            _e.PhysicsAttrAccum.SetValue(physics, 0f);
            // over 800 entities physics is split across its worker threads, which race (dropped items pile up in a
            // class's world, scenario after scenario): one thread, the game's, while the clock runs
            _physicsManager = physics;
            _physicsThreadsBefore = (int)_e.PhysicsThreads.GetValue(physics)!;
            _e.PhysicsThreads.SetValue(physics, 1);
        }
        _seeded = false;
        _blockSim = ((ServerSystem[])_e.Systems.GetValue(_server)!).OfType<ServerSystemBlockSimulation>().FirstOrDefault();
        if (_blockSim != null)
        {
            _e.BlockTickRand.SetValue(_blockSim, new Random(RandSeed));
            _blockTicksAt = now;
        }
    }

    private PhysicsManager? _physicsManager;
    private int _physicsThreadsBefore;

    // World.Rand is the game thread's own (a ThreadLocal): seeded on the first pass, which runs there
    private bool _seeded = true;

    // The random block ticks' pick (which blocks get a tick: grass grows on soil, and so on) runs on the server's
    // separate thread every GetUpdateInterval of wall time, with a Random of its own: which blocks, and when, came out
    // differently each run (tall grass grew on a dug floor in one run, and the plan was made again round it). While a
    // clock runs it is made on the game thread instead, every GetUpdateInterval of game time, its Random seeded.
    private ServerSystemBlockSimulation? _blockSim;
    private long _blockTicksAt;
    [ThreadStatic] private static bool _lockstepBlockTicks;

    // the separate thread's call: skipped while a clock runs on its server (the clock's own call goes through)
    private static bool BeforeBlockTicks(ServerSystemBlockSimulation __instance) =>
        _lockstepBlockTicks || _current is not { } clock || clock._blockSim != __instance;

    // the chunks the block ticks' pick ticks while a clock runs: those it found round the players at its first pick,
    // in order (chunks loaded later, on their own threads in wall time, would come in at a different tick each run)
    private List<long>? _blockChunks;

    // on the game thread, each pass: the block ticks' pick every its interval of game time
    private void BlockTicksInLockstep()
    {
        if (_blockSim == null)
            return;
        long now = _elapsed / (Stopwatch.Frequency / 1000);
        if (now - _blockTicksAt <= _blockSim.GetUpdateInterval())
            return;
        _blockTicksAt = now;
        if (_blockChunks == null)
        {
            _lockstepBlockTicks = true;
            try
            {
                _blockSim.OnSeparateThreadTick();
            }
            finally
            {
                _lockstepBlockTicks = false;
            }
            _blockChunks = ((IDictionary)_e.BlockTickChunks.GetValue(_blockSim)!).Keys.Cast<long>().Order().ToList();
            return;
        }
        foreach (long index in _blockChunks)
            if (_server.WorldMap.GetServerChunk(index) is { } chunk)
            {
                chunk.MarkFresh();
                _e.TickChunk.Invoke(_blockSim, [index, chunk]);
            }
    }

    /// <summary>
    /// Holds game time still while <paramref name="holds"/> is true: before each pass the game thread waits
    /// (yielding) until it is false, then advances as usual. Use it for work off the main thread that runs on
    /// wall time, so that it takes no game time and every run steps the same way. The predicate runs on the
    /// game thread between passes, so it may read main-thread state; it must become false without the game
    /// thread's help (a worker finishing), or it holds until <paramref name="timeout"/> (wall time;
    /// <see cref="DefaultHoldTimeout"/>), when it is dropped and the clock's <see cref="Dispose"/> throws, as it
    /// does if the predicate throws. Dispose the result to remove it; the clock's Dispose removes all.
    /// </summary>
    public IDisposable HoldWhile(Func<bool> holds, string what = "a hold", TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(holds);
        lock (Gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var hold = new Hold(this, holds, what, timeout ?? DefaultHoldTimeout);
            _holds.Add(hold);
            return hold;
        }
    }

    /// <summary>Back to real time, the clock going on from the virtual one's time. Throws (after restoring)
    /// if a hold timed out or threw, or physics' sender stuck, while it ran.</summary>
    public void Dispose()
    {
        string[] faults;
        lock (Gate)
        {
            if (_disposed)
                return;
            Stop();
            faults = _faults.ToArray();
        }
        if (faults.Length > 0)
            throw new InvalidOperationException($"the virtual clock started at {StartedAt} faulted:\n" + string.Join("\n", faults));
    }

    // Under Gate: real time again, exactly where the virtual clock is.
    private void Stop()
    {
        _disposed = true;
        _holds.Clear();
        RealTimeAgain();
    }

    // Under Gate: no longer the clock on, the master clock running on from the virtual time.
    private void RealTimeAgain()
    {
        _watch?.Dispose();
        _watch = null;
        if (_current != this)
            return;
        _current = null;
        EidolonMinePlans.BudgetReads = _readsBefore;
        if (_autosaveBefore != null)
            _e.Autosave.SetValue(null, _autosaveBefore);
        if (_physicsManager != null)
            _e.PhysicsThreads.SetValue(_physicsManager, _physicsThreadsBefore);
        var watch = _server.totalUnpausedTime;
        // a resume (Suspend(false)) since the last pass started it: set it back to the virtual time
        watch.Stop();
        _e.Elapsed.SetValue(watch, _elapsed);
        if (!_server.Suspended)
            watch.Start();
    }

    /// <summary>The leak guard (<see cref="VirtualClockGuardAttribute"/>): a clock still on after
    /// <paramref name="scenario"/> is stopped, and the scenario fails.</summary>
    internal static void StopLeaked(string scenario)
    {
        string at;
        lock (Gate)
        {
            if (_current is not { } clock)
                return;
            at = clock.StartedAt;
            clock.Stop();
        }
        throw new InvalidOperationException($"{scenario} ended with the virtual clock started at {at} still on: "
            + "start it with `using var clock = VirtualClock.Start(World);`. It has been stopped.");
    }

    private void Fault(string what)
    {
        _faults.Add(what);
        Api.Logger.Warning("[virtual-clock] {0}", what);
    }

    private static void BeforePass(ServerMain __instance)
    {
        lock (Gate)
        {
            if (_current is not { } clock || clock._server != __instance)
                return;
            clock._lastPrefix = Environment.TickCount64;
            if (__instance.Suspended)
                return;
            clock.Advance();
            clock._lastPrefix = Environment.TickCount64;
        }
    }

    // On a timer thread, every second: passes running (the engine restarts its per-pass watch each one)
    // while the prefix has not run for PatchLostAfter means the patch on Process is gone (a mod unpatched
    // everything). The master clock would never move again, so: a fault, and real time again.
    private void WatchPatch()
    {
        try
        {
            lock (Gate)
            {
                if (_current != this || _server.Suspended
                    || Environment.TickCount64 - _lastPrefix < PatchLostAfter.TotalMilliseconds
                    || _e.FrameWatch.GetValue(_server) is not Stopwatch { Elapsed: var sincePass } || sincePass > PassesRunning)
                    return;
                Fault($"passes ran for {PatchLostAfter.TotalSeconds:0} s without the virtual clock's patch on ServerMain.Process "
                    + $"({_e.PatchState()}): something unpatched it (a mod's Harmony.UnpatchAll(null)?); back on real time");
                RealTimeAgain();
            }
        }
        catch (Exception ex)
        {
            // never let a timer callback take the test host down
            Api.Logger.Warning("[virtual-clock] the patch watch failed: {0}", ex);
        }
    }

    private void Advance()
    {
        if (!_seeded)
        {
            _server.rand.Value = new Random(RandSeed);
            _seeded = true;
        }
        WaitForPhysics();
        WaitForHolds();
        var watch = _server.totalUnpausedTime;
        // the engine's Suspend(false) starts it again: keep it stopped, on the virtual count
        watch.Stop();
        _elapsed += _step;
        _e.Elapsed.SetValue(watch, _elapsed);
        AdvanceCalendar();
        BlockTicksInLockstep();
        Passes++;
    }

    // The engine waits for this itself at the top of PhysicsManager.ServerTick, but on its Busy(), which
    // reads false between the sender dequeuing a task and marking itself busy, and only for 1 s. Idle here
    // is the sender parked in its Monitor.Wait with nothing queued: it is running nothing.
    private void WaitForPhysics()
    {
        if (_physics == null)
            return;
        var wall = Stopwatch.StartNew();
        var spin = new SpinWait();
        while (!_e.PhysicsIdle(_physics))
        {
            if (wall.Elapsed > PhysicsTimeout)
            {
                Fault($"physics' off-thread sender stayed busy over {PhysicsTimeout.TotalSeconds:0} s; passes no longer wait for it");
                _physics = null;
                return;
            }
            spin.SpinOnce(sleep1Threshold: -1);
        }
    }

    private void WaitForHolds()
    {
        if (_holds.Count == 0)
            return;
        var wall = Stopwatch.StartNew();
        var spin = new SpinWait();
        while (true)
        {
            bool held = false;
            foreach (var hold in _holds.ToArray())
            {
                bool on;
                try
                {
                    on = hold.Holds();
                }
                catch (Exception ex)
                {
                    Fault($"the hold for {hold.What} threw, and was dropped: {ex}");
                    _holds.Remove(hold);
                    continue;
                }
                if (!on)
                    continue;
                if (wall.Elapsed > hold.Timeout)
                {
                    Fault($"the hold for {hold.What} held the clock over {hold.Timeout.TotalSeconds:0.#} s of wall time, and was dropped");
                    _holds.Remove(hold);
                    continue;
                }
                held = true;
            }
            if (!held)
                return;
            spin.SpinOnce();
        }
    }

    // The calendar adds its own watch's real time at each Tick (every 200 ms of server time) and restarts it.
    // A Harmony prefix on GameCalendar.Tick was bypassed in practice (most likely the JIT had inlined the
    // small virtual method into ServerSystemCalendar.OnServerTick by guarded devirtualisation). Instead each
    // pass adds the step to in-game time itself and restarts the watch, which leaves Tick well under 1 ms
    // of real time to add (ElapsedMilliseconds floors it to 0).
    private void AdvanceCalendar()
    {
        if (_e.ServerCalendar.GetValue(_server) is not GameCalendar calendar)
            return;
        if (_e.CalendarWatch.GetValue(calendar) is not Stopwatch { IsRunning: true } watch)
            return;
        double seconds = _step / (double)Stopwatch.Frequency;
        var span = (TimeSpan)_e.CalendarSpan.GetValue(calendar)!;
        _e.CalendarSpan.SetValue(calendar, span + TimeSpan.FromSeconds(seconds * calendar.SpeedOfTime * calendar.CalendarSpeedMul));
        watch.Restart();
    }

    private static long _pacingSleeps;

    /// <summary>Pacing sleeps the transpiled Process has slept (in real time, or for a server not on the
    /// clock) since the process began: none in a window on the virtual clock.</summary>
    public static long PacingSleeps => Interlocked.Read(ref _pacingSleeps);

    // The transpiled Process's pacing sleep: none for a server on the virtual clock.
    private static void PacingSleep(int ms, ServerMain server)
    {
        if (Volatile.Read(ref _current)?._server != server)
        {
            Interlocked.Increment(ref _pacingSleeps);
            Thread.Sleep(ms);
        }
    }

    private sealed class Hold(VirtualClock clock, Func<bool> holds, string what, TimeSpan timeout) : IDisposable
    {
        public readonly Func<bool> Holds = holds;
        public readonly string What = what;
        public readonly TimeSpan Timeout = timeout;

        public void Dispose()
        {
            lock (Gate)
                clock._holds.Remove(this);
        }
    }

    /// <summary>The engine members the clock reaches by reflection, found (and Process patched) once; any
    /// missing is named in one <see cref="NotSupportedException"/>.</summary>
    private sealed class Engine
    {
        public required FieldInfo Elapsed { get; init; }
        public required FieldInfo CalendarWatch { get; init; }
        public required FieldInfo CalendarSpan { get; init; }
        public required FieldInfo ServerCalendar { get; init; }
        public required FieldInfo Systems { get; init; }
        public required FieldInfo Offthread { get; init; }
        public required FieldInfo OffthreadIdle { get; init; }
        public required FieldInfo OffthreadQueue { get; init; }
        public required FieldInfo FrameWatch { get; init; }
        public required FieldInfo PhysicsAccum { get; init; }
        public required FieldInfo PhysicsAttrAccum { get; init; }
        public required FieldInfo ListenersEntity { get; init; }
        public required FieldInfo ListenersBlock { get; init; }
        public required FieldInfo Autosave { get; init; }
        public required FieldInfo BlockTickRand { get; init; }
        public required FieldInfo PhysicsThreads { get; init; }
        public required FieldInfo BlockTickChunks { get; init; }
        public required MethodInfo TickChunk { get; init; }

        private static readonly MethodInfo Process = AccessTools.Method(typeof(ServerMain), nameof(ServerMain.Process));

        /// <summary>The random block ticks' pick, on the server's separate thread (<see cref="BeforeBlockTicks"/>).</summary>
        public static readonly MethodInfo BlockTicks = AccessTools.Method(typeof(ServerSystemBlockSimulation), nameof(ServerSystemBlockSimulation.OnSeparateThreadTick));

        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        public static Engine Resolve()
        {
            var missing = new List<string>();
            FieldInfo Field(Type? type, string name, Type fieldType)
            {
                var f = type?.GetField(name, Any);
                if (f == null || !fieldType.IsAssignableFrom(f.FieldType))
                    missing.Add($"{type?.FullName ?? "?"}.{name} ({fieldType.Name})");
                return f!;
            }
            MethodInfo Method(Type type, string name)
            {
                var m = type.GetMethod(name, Any);
                if (m == null)
                    missing.Add($"{type.FullName}.{name}()");
                return m!;
            }
            FieldInfo Static(Type? type, string name)
            {
                var f = type?.GetField(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (f == null || !f.FieldType.IsPrimitive)
                    missing.Add($"{type?.FullName ?? "Vintagestory.Server.MagicNum"}.{name} (a static number)");
                return f!;
            }
            var offthread = Field(typeof(PhysicsManager), "offthreadProcess", typeof(object));
            var tasks = offthread?.FieldType;
            var e = new Engine
            {
                Elapsed = Field(typeof(Stopwatch), "_elapsed", typeof(long)),
                CalendarWatch = Field(typeof(GameCalendar), "watchIngameTime", typeof(Stopwatch)),
                CalendarSpan = Field(typeof(GameCalendar), "timespan", typeof(TimeSpan)),
                ServerCalendar = Field(typeof(ServerMain), "GameWorldCalendar", typeof(GameCalendar)),
                Systems = Field(typeof(ServerMain), "Systems", typeof(ServerSystem[])),
                Offthread = offthread!,
                OffthreadIdle = Field(tasks, "idle", typeof(bool)),
                OffthreadQueue = Field(tasks, "queue", typeof(ICollection)),
                FrameWatch = Field(typeof(ServerMain), "lastFramePassedTime", typeof(Stopwatch)),
                PhysicsAccum = Field(typeof(PhysicsManager), "physicsTickAccum", typeof(float)),
                PhysicsAttrAccum = Field(typeof(PhysicsManager), "attrUpdateAccum", typeof(float)),
                ListenersEntity = Field(typeof(Vintagestory.Common.EventManager), "GameTickListenersEntity", typeof(IEnumerable)),
                ListenersBlock = Field(typeof(Vintagestory.Common.EventManager), "GameTickListenersBlock", typeof(IEnumerable)),
                Autosave = Static(typeof(PhysicsManager).Assembly.GetType("Vintagestory.Server.MagicNum"), "ServerAutoSave"),
                BlockTickRand = Field(typeof(ServerSystemBlockSimulation), "rand", typeof(Random)),
                PhysicsThreads = Field(typeof(PhysicsManager), "maxPhysicsThreads", typeof(int)),
                BlockTickChunks = Field(typeof(ServerSystemBlockSimulation), "chunksToBeTicked", typeof(IDictionary)),
                TickChunk = Method(typeof(ServerSystemBlockSimulation), "tickChunk"),
            };
            if (missing.Count > 0)
                throw new NotSupportedException("the virtual clock cannot run on this engine: missing " + string.Join(", ", missing)
                    + ". A game or .NET update renamed them: find them again by decompiling (tests/PackTests/README.md).");
            e.EnsurePatched();
            return e;
        }

        /// <summary>Process carries this clock's prefix and transpiler: patched (again) if not, as after a
        /// mod's <c>UnpatchAll(null)</c>.</summary>
        public void EnsurePatched()
        {
            if (Patched())
                return;
            Patch();
            if (!Patched())
                throw new NotSupportedException("the virtual clock patched ServerMain.Process but its patch is not on it: " + PatchState());
        }

        private static bool Patched()
        {
            var info = Harmony.GetPatchInfo(Process);
            return info != null && info.Prefixes.Any(p => p.owner == HarmonyId) && info.Transpilers.Any(p => p.owner == HarmonyId)
                && TraverserMethods().Concat(UnstableMethods()).All(m => Harmony.GetPatchInfo(m)?.Transpilers.Any(p => p.owner == HarmonyId) == true)
                && Harmony.GetPatchInfo(BlockTicks)?.Prefixes.Any(p => p.owner == HarmonyId) == true
                && WorkerMethods().All(m => Harmony.GetPatchInfo(m)?.Transpilers.Any(p => p.owner == HarmonyId) == true);
        }

        // The eidolon mine planner's workers (a plan, a route solve: EidolonMinePlans' Task.Runs). Game time stands still
        // while one runs (MinePlanHold), but the main thread may cancel one in the pass it started (a new plan come
        // in): whether it had finished by then, its result taken, or not, its result dropped, was the threads' race.
        private static IEnumerable<MethodBase> WorkerMethods() =>
            new[]
                {
                    AccessTools.Method(typeof(EidolonMinePlans), "RouteStep"),
                    AccessTools.Inner(typeof(EidolonMinePlans), "Work") is { } work
                        ? AccessTools.Method(typeof(EidolonMinePlans), "Start", [work, typeof(Mod.Eidolon.Core.MineSite), typeof(double)]) : null,
                }
                .Select(m => m ?? throw new NotSupportedException("the virtual clock found no EidolonMinePlans.RouteStep or Start"));

        // Task.Run(func) in the planner: RunInline(func) instead
        private static IEnumerable<CodeInstruction> Inline(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            var inline = AccessTools.Method(typeof(VirtualClock), nameof(RunInline));
            int replaced = 0;
            for (int i = 0; i < code.Count; i++)
                if (code[i].operand is MethodInfo { Name: "Run", IsGenericMethod: true } run && run.DeclaringType == typeof(Task)
                    && run.GetParameters() is [{ ParameterType.IsGenericType: true } p] && p.ParameterType.GetGenericTypeDefinition() == typeof(Func<>))
                {
                    code[i] = new CodeInstruction(OpCodes.Call, inline.MakeGenericMethod(run.GetGenericArguments())).MoveLabelsFrom(code[i]);
                    replaced++;
                }
            if (replaced == 0)
                throw new NotSupportedException("the virtual clock found no Task.Run in the eidolon mine planner to run inline");
            return code;
        }

        // The game's unstable rock and falling ground draw from World.Rand too (whether rock left hanging caves in,
        // and how): the same shared stream, so a cave-in came in one run and not the next.
        private static IEnumerable<MethodBase> UnstableMethods()
        {
            var rock = AccessTools.TypeByName("Vintagestory.GameContent.BlockBehaviorUnstableRock");
            var falling = AccessTools.TypeByName("Vintagestory.GameContent.BlockBehaviorUnstableFalling");
            if (rock == null || falling == null)
                throw new NotSupportedException("the virtual clock found no Vintagestory.GameContent.BlockBehaviorUnstableRock or BlockBehaviorUnstableFalling");
            return new[]
                {
                    AccessTools.Method(rock, "getNearestUnstableBlocks"), AccessTools.Method(rock, "collapseLayer"),
                    AccessTools.Method(rock, "checkCollapsibleNeighbours"), AccessTools.Method(rock, "CheckCollapsible"),
                    AccessTools.Method(falling, "TryFalling"),
                }
                .Select(m => m ?? throw new NotSupportedException("the virtual clock found no unstable rock or falling method it draws for"));
        }

        // world.Rand in an unstable block's behaviour: BlockRand(world) instead
        private static IEnumerable<CodeInstruction> OwnBlockRand(IEnumerable<CodeInstruction> instructions)
        {
            var rand = AccessTools.PropertyGetter(typeof(Vintagestory.API.Common.IWorldAccessor), "Rand");
            var own = AccessTools.Method(typeof(VirtualClock), nameof(BlockRand));
            var code = instructions.ToList();
            int replaced = 0;
            for (int i = 0; i < code.Count; i++)
                if (code[i].Calls(rand))
                {
                    code[i] = new CodeInstruction(OpCodes.Call, own).MoveLabelsFrom(code[i]);
                    replaced++;
                }
            if (replaced == 0)
                throw new NotSupportedException("the virtual clock found no world.Rand in an unstable block's behaviour to replace");
            return code;
        }

        // The game's path traversers' draws from World.Rand (a turn rate as each walk begins, a stuck check): the game
        // thread's one stream, which block ticks and the like draw from too, as many times as the chunks loaded by
        // their own threads (wall time) give them, so where an eidolon's walk lands in it differs run to run.
        private static IEnumerable<MethodBase> TraverserMethods()
        {
            var waypoints = AccessTools.TypeByName("Vintagestory.Essentials.WaypointsTraverser");
            var straight = AccessTools.TypeByName("Vintagestory.Essentials.StraightLineTraverser");
            if (waypoints == null || straight == null)
                throw new NotSupportedException("the virtual clock found no Vintagestory.Essentials.WaypointsTraverser or StraightLineTraverser");
            return new[] { AccessTools.Method(waypoints, "BeginGo"), AccessTools.Method(waypoints, "OnGameTick"), AccessTools.Method(straight, "BeginGo") }
                .Select(m => m ?? throw new NotSupportedException("the virtual clock found no path traverser BeginGo or OnGameTick"));
        }

        // entity.World.Rand in a traverser: TraverserRand(entity) instead
        private static IEnumerable<CodeInstruction> OwnRand(IEnumerable<CodeInstruction> instructions)
        {
            var world = AccessTools.Field(typeof(Vintagestory.API.Common.Entities.Entity), "World");
            var rand = AccessTools.PropertyGetter(typeof(Vintagestory.API.Common.IWorldAccessor), "Rand");
            var own = AccessTools.Method(typeof(VirtualClock), nameof(TraverserRand));
            var code = instructions.ToList();
            int replaced = 0;
            for (int i = 0; i + 1 < code.Count; i++)
                if (code[i].LoadsField(world) && code[i + 1].Calls(rand))
                {
                    code[i] = new CodeInstruction(OpCodes.Call, own).MoveLabelsFrom(code[i]);
                    code[i + 1] = new CodeInstruction(OpCodes.Nop).MoveLabelsFrom(code[i + 1]);
                    replaced++;
                }
            if (replaced == 0)
                throw new NotSupportedException("the virtual clock found no entity.World.Rand in a path traverser to replace");
            return code;
        }

        /// <summary>Who has patched Process, for the messages.</summary>
        public string PatchState()
        {
            var info = Harmony.GetPatchInfo(Process);
            return info == null ? "Process carries no Harmony patch" : "Process patched by " + string.Join(", ", info.Owners);
        }

        /// <summary>The off-thread sender of <paramref name="server"/>'s physics manager, or null when it has
        /// none (reduced server threads run its tasks inline).</summary>
        public object? Physics(ServerMain server)
        {
            if (server.ReducedServerThreads)
                return null;
            var physics = ((ServerSystem[])Systems.GetValue(server)!).OfType<ServerUdpNetwork>().FirstOrDefault()?.physicsManager
                ?? throw new NotSupportedException("the virtual clock found no PhysicsManager on the server's ServerUdpNetwork system");
            return Offthread.GetValue(physics)
                ?? throw new NotSupportedException("the server's PhysicsManager has no offthreadProcess");
        }

        public bool PhysicsIdle(object tasks) =>
            (bool)OffthreadIdle.GetValue(tasks)! && ((ICollection)OffthreadQueue.GetValue(tasks)!).Count == 0;

        private static void Patch()
        {
            try
            {
                var harmony = new Harmony(HarmonyId);
                harmony.Patch(Process,
                    prefix: new HarmonyMethod(typeof(VirtualClock), nameof(BeforePass)),
                    transpiler: new HarmonyMethod(typeof(Engine), nameof(SkipPacingSleep)));
                foreach (var m in TraverserMethods())
                    harmony.Patch(m, transpiler: new HarmonyMethod(typeof(Engine), nameof(OwnRand)));
                foreach (var m in UnstableMethods())
                    harmony.Patch(m, transpiler: new HarmonyMethod(typeof(Engine), nameof(OwnBlockRand)));
                harmony.Patch(BlockTicks, prefix: new HarmonyMethod(typeof(VirtualClock), nameof(BeforeBlockTicks)));
                foreach (var m in WorkerMethods())
                    harmony.Patch(m, transpiler: new HarmonyMethod(typeof(Engine), nameof(Inline)));
            }
            catch (Exception ex)
            {
                throw new NotSupportedException("the virtual clock could not patch ServerMain.Process: " + ex.Message, ex);
            }
        }

        // Process ends its pass with
        //     int ms = (int)Math.Max(0f, Config.TickTime - (float)passMs);
        //     if (ms > 0) { Thread.Sleep(ms); ... }
        // That one Sleep (not the suspended or standby passes' short ones) becomes PacingSleep(ms, this).
        private static IEnumerable<CodeInstruction> SkipPacingSleep(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            var tickTime = AccessTools.PropertyGetter(typeof(ServerConfig), nameof(ServerConfig.TickTime));
            var max = AccessTools.Method(typeof(Math), nameof(Math.Max), [typeof(float), typeof(float)]);
            var sleep = AccessTools.Method(typeof(Thread), nameof(Thread.Sleep), [typeof(int)]);
            var paced = AccessTools.Method(typeof(VirtualClock), nameof(PacingSleep));
            int found = 0;
            for (int i = 4; i + 2 < code.Count; i++)
            {
                if (!code[i].Calls(max) || code[i + 1].opcode != OpCodes.Conv_I4 || !code[i + 2].IsStloc()
                    || !code.Skip(i - 4).Take(4).Any(c => c.Calls(tickTime)))
                    continue;
                int local = LocalIndex(code[i + 2]);
                for (int j = i + 3; j + 1 < code.Count; j++)
                {
                    if (!code[j + 1].Calls(sleep) || !code[j].IsLdloc() || LocalIndex(code[j]) != local)
                        continue;
                    var call = code[j + 1];
                    code[j + 1] = new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(call).MoveBlocksFrom(call);
                    code.Insert(j + 2, new CodeInstruction(OpCodes.Call, paced));
                    found++;
                    break;
                }
            }
            if (found != 1)
                throw new InvalidOperationException($"expected one pacing Thread.Sleep(int) after Math.Max(0, Config.TickTime - ms) in Process, found {found}");
            return code;
        }

        private static int LocalIndex(CodeInstruction c)
        {
            if (c.opcode == OpCodes.Stloc_0 || c.opcode == OpCodes.Ldloc_0) return 0;
            if (c.opcode == OpCodes.Stloc_1 || c.opcode == OpCodes.Ldloc_1) return 1;
            if (c.opcode == OpCodes.Stloc_2 || c.opcode == OpCodes.Ldloc_2) return 2;
            if (c.opcode == OpCodes.Stloc_3 || c.opcode == OpCodes.Ldloc_3) return 3;
            return c.operand switch
            {
                LocalBuilder b => b.LocalIndex,
                int n => n,
                byte n => n,
                short n => n,
                _ => -1,
            };
        }
    }
}

/// <summary>Stops a virtual clock a scenario left on, and fails that scenario
/// (<see cref="VirtualClock.StopLeaked"/>): after each scenario, on xUnit's thread.</summary>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class VirtualClockGuardAttribute : BeforeAfterTestAttribute
{
    public override void After(MethodInfo methodUnderTest) =>
        VirtualClock.StopLeaked($"{methodUnderTest.DeclaringType?.Name}.{methodUnderTest.Name}");
}
