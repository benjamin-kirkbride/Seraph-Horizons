using System.Diagnostics;
using System.Reflection;
using System.Text;
using HarmonyLib;
using SeraphHorizons.Mod.NanMotion.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.Client.NoObf;
using Vintagestory.Common;
using Vintagestory.Server;

namespace SeraphHorizons.Mod.NanMotion;

/// <summary>
/// NaN motion diagnostics, other entities (#405). The first report named the player's own repulsion,
/// pushed NaN by a neighbour (<c>game:bowtorn-surface</c>) whose position already was NaN; 49 of 192
/// entities loaded on the client had NaN positions, of every kind (animals, butterflies, fish, carts,
/// wagons, the flying machine, eidolons, trunks: every one of them interpolated, the
/// <c>EntityBehaviorInterpolatePosition</c> that eases a remote entity toward the server's positions),
/// starting a second after an autosave, 22 minutes before the crash. The server's packets cannot
/// carry a NaN (positions are fixed point longs), so the client made it. This finds what did.
///
/// <para>Each traced call whose instance is an entity or an entity's behavior (every tick and server
/// position override, the physics steps, every remote physics step, the interpolation's
/// <c>PopQueue</c> and <c>OnReceivedServerPos</c>, and each renderer that is a behavior, the
/// interpolation's <c>OnRenderFrame</c> among them) checks its own entity's position and motion at its
/// start and its end (<see cref="EntityNanTracker"/>); the interpolation's traces check its snapshots,
/// accumulator and speed along with them, since a NaN there makes every later frame NaN. A sweep of
/// every loaded entity every <see cref="SweepIntervalMs"/> catches what is not traced and bounds when.
/// The context goes with the record: the frame's dt, the last client frames, the last position packet
/// from the server, and when the server last suspended and resumed (singleplayer's autosave) and the
/// game last paused.</para>
///
/// <para>The first trip is logged as a warning at once with the full record and written to its own
/// file (<c>Logs/seraphhorizons-nanentity-&lt;time&gt;.txt</c>), since the crash may come much later or
/// never; the crash report has it too. Nothing is changed: no NaN is repaired or dropped. Cheap while
/// nothing is wrong: on the client thread a checkpoint reads its entity's position and motion into a
/// value type, the time is the frame's (read once a frame), and after the first trip every one returns
/// at once.</para>
/// </summary>
public static partial class NanMotionDiagnostics
{
    /// <summary>How often every loaded entity is checked.</summary>
    public const int SweepIntervalMs = 500;

    /// <summary>How many client frames the entity record shows.</summary>
    public const int ClientFrameCount = 30;

    /// <summary>How many non-finite entities the entity report lists.</summary>
    public const int ListedEntities = 60;

    public const string SweepSite = "the sweep of loaded entities";

    private static int _entityThreadId = -1;
    private static long _nowMs;
    private static long _sweepListener = -1;
    private static OnGamePauseResume? _pauseHandler;
    private static PacketRecord _lastPacket;
    // Environment.TickCount64 of each, 0 for never: the server's ticking suspended (an autosave) and
    // resumed, and the game paused and unpaused.
    private static long _suspendedAt, _resumedAt, _pausedAt, _unpausedAt;

    public static EntityNanTracker EntityTracker { get; private set; } = new();
    public static FrameRing ClientFrames { get; private set; } = new(ClientFrameCount);

    /// <summary>The entity report file written, once there is one.</summary>
    public static string? EntityReportPath { get; private set; }

    /// <summary>The last entity position packet from the server, as the client decoded it.</summary>
    public readonly record struct PacketRecord(long EntityId, double X, double Y, double Z, double MotionX, double MotionY, double MotionZ,
        int Tick, bool Teleport, long AtMs, bool Received);

    private static void BindEntities(int threadId)
    {
        _entityThreadId = threadId;
        _nowMs = _api?.World?.ElapsedMilliseconds ?? 0;
        _lastPacket = default;
        _suspendedAt = _resumedAt = _pausedAt = _unpausedAt = 0;
        EntityTracker = new EntityNanTracker();
        ClientFrames = new FrameRing(ClientFrameCount);
        EntityReportPath = null;
    }

    private static void UnbindEntities()
    {
        try
        {
            if (_capi != null)
            {
                if (_sweepListener >= 0)
                    _capi.Event.UnregisterGameTickListener(_sweepListener);
                if (_pauseHandler != null)
                    _capi.Event.PauseResume -= _pauseHandler;
            }
        }
        catch
        {
            // The world is going anyway.
        }
        _sweepListener = -1;
        _pauseHandler = null;
        _entityThreadId = -1;
    }

    // ---------------------------------------------------------------- patching

    public static MethodBase? MainRenderLoopMethod => Declared(typeof(ClientMain), nameof(ClientMain.MainRenderLoop), [typeof(float)]);

    public static MethodBase? ServerSuspendMethod => Declared(typeof(ServerMain), nameof(ServerMain.Suspend), [typeof(bool), typeof(int)]);

    public static MethodBase? HandleSinglePacketMethod =>
        Declared(typeof(SystemNetworkProcess), nameof(SystemNetworkProcess.HandleSinglePacket), [typeof(Packet_EntityPosition)]);

    /// <summary>The patches that only record context: each client frame, each entity position packet,
    /// and the server's suspensions (in singleplayer the server runs in this process).</summary>
    private static void PatchEntityContext(Harmony harmony, Action<string, Action> attempt)
    {
        void One(string label, MethodBase? method, HarmonyMethod? prefix, HarmonyMethod? postfix)
        {
            if (method == null)
                PatchFailures.Add($"{label}: not found");
            else
                attempt(label, () => harmony.Patch(method, prefix: prefix, postfix: postfix));
        }
        One("ClientMain.MainRenderLoop", MainRenderLoopMethod,
            new HarmonyMethod(typeof(ClientFrameEntry), nameof(ClientFrameEntry.Prefix)) { priority = Priority.First }, null);
        One("SystemNetworkProcess.HandleSinglePacket (the packet)", HandleSinglePacketMethod,
            new HarmonyMethod(typeof(PositionPacketEntry), nameof(PositionPacketEntry.Prefix)) { priority = Priority.First }, null);
        One("ServerMain.Suspend", ServerSuspendMethod, null,
            new HarmonyMethod(typeof(ServerSuspend), nameof(ServerSuspend.Postfix)) { priority = Priority.Last });
    }

    /// <summary>The client's sweep and pause listener, once patched.</summary>
    private static void StartSweeps(ICoreClientAPI api)
    {
        _sweepListener = api.Event.RegisterGameTickListener(SweepLoaded, SweepIntervalMs);
        _pauseHandler = OnPauseResume;
        api.Event.PauseResume += _pauseHandler;
    }

    private static void OnPauseResume(bool paused)
    {
        if (paused)
            _pausedAt = Environment.TickCount64;
        else
            _unpausedAt = Environment.TickCount64;
    }

    /// <summary>First thing in each client frame: the frame's time, and the frame for the record.</summary>
    public static class ClientFrameEntry
    {
        public static void Prefix(ClientMain __instance, float dt)
        {
            if (EntityTracker.Tripped || Environment.CurrentManagedThreadId != _entityThreadId)
                return;
            _nowMs = __instance.ElapsedMilliseconds;
            ClientFrames.Add(new ClientFrame(_nowMs, dt, __instance.IsPaused));
        }
    }

    /// <summary>Before the client applies an entity position from the server: the packet, decoded as
    /// the game decodes it.</summary>
    public static class PositionPacketEntry
    {
        public static void Prefix(Packet_EntityPosition packet)
        {
            if (packet == null || EntityTracker.Tripped || Environment.CurrentManagedThreadId != _entityThreadId)
                return;
            _lastPacket = new PacketRecord(packet.EntityId,
                CollectibleNet.DeserializeDoublePrecise(packet.X), CollectibleNet.DeserializeDoublePrecise(packet.Y),
                CollectibleNet.DeserializeDoublePrecise(packet.Z), CollectibleNet.DeserializeDoublePrecise(packet.MotionX),
                CollectibleNet.DeserializeDoublePrecise(packet.MotionY), CollectibleNet.DeserializeDoublePrecise(packet.MotionZ),
                packet.Tick, packet.Teleport, _nowMs, true);
        }
    }

    /// <summary>After the server suspends or resumes its ticking (an autosave), on its own thread.</summary>
    public static class ServerSuspend
    {
        public static void Postfix(bool newSuspendState)
        {
            if (newSuspendState)
                _suspendedAt = Environment.TickCount64;
            else
                _resumedAt = Environment.TickCount64;
        }
    }

    // ---------------------------------------------------------------- checkpoints

    private static Entity? EntityOf(object? instance) => instance switch
    {
        EntityBehavior b => b.entity,
        Entity e => e,
        _ => null,
    };

    /// <summary>Whether a checkpoint on <paramref name="instance"/> would look at its entity.</summary>
    private static bool EntityWatching(object? instance) =>
        !EntityTracker.Tripped && Environment.CurrentManagedThreadId == _entityThreadId && EntityOf(instance) is { } e && !IsPlayer(e);

    /// <summary>The watched player (its own tracker has it), or before its first physics step the
    /// client's player.</summary>
    private static bool IsPlayer(Entity e) =>
        ReferenceEquals(e, _watched) || (_watched == null && e is EntityPlayer && ReferenceEquals(_capi?.World?.Player?.Entity, e));

    /// <summary>A checkpoint at the start of a traced call, on its instance's entity.
    /// <paramref name="stateFinite"/>: the call's own state checked along with it.</summary>
    private static EntityProbe EntityEnter(string site, object? instance, bool stateFinite, out bool tripped)
    {
        tripped = false;
        if (EntityTracker.Tripped || Environment.CurrentManagedThreadId != _entityThreadId || EntityOf(instance) is not { } e || IsPlayer(e))
            return default;
        var p = e.Pos;
        var m = p.Motion;
        return EntityTracker.Before(site, instance, e, e.EntityId, _nowMs, p.X, p.Y, p.Z, m.X, m.Y, m.Z, stateFinite, out tripped);
    }

    /// <summary>A checkpoint at the end of a traced call, on its instance's entity. Returns whether it
    /// tripped.</summary>
    private static bool EntityExit(string site, object? instance, in EntityProbe before, bool stateFinite)
    {
        if (!before.Watched || EntityTracker.Tripped || EntityOf(instance) is not { } e)
            return false;
        var p = e.Pos;
        var m = p.Motion;
        return EntityTracker.After(site, instance, e, e.EntityId, _nowMs, in before, p.X, p.Y, p.Z, m.X, m.Y, m.Z, stateFinite);
    }

    public static class TraceRemotePhysics
    {
        public const string Site = "IRemotePhysics.HandleRemotePhysics (a remote entity's physics step, which the interpolation runs)";
        public static void Prefix(object? __instance, out TraceState __state) => Enter(Site, __instance, out __state);
        public static void Postfix(object? __instance, TraceState __state) => Exit(Site, __instance, in __state);
    }

    /// <summary>The interpolation's state: what it eases between and how fast. A value type.</summary>
    public readonly struct InterpolationState
    {
        public InterpolationState(EntityBehaviorInterpolatePosition ip)
        {
            Captured = true;
            DtAccum = ip.dtAccum;
            TargetSpeed = ip.targetSpeed;
            QueueCount = ip.queueCount;
            Wait = ip.wait;
            L = ip.pL;
            N = ip.pN;
        }

        public static InterpolationState Of(EntityBehaviorInterpolatePosition ip) => new(ip);

        public bool Captured { get; }
        public float DtAccum { get; }
        public float TargetSpeed { get; }
        public int QueueCount { get; }
        public int Wait { get; }
        public PositionSnapshot L { get; }
        public PositionSnapshot N { get; }

        public bool Finite =>
            float.IsFinite(DtAccum) && float.IsFinite(TargetSpeed) && Snapshot(L) && Snapshot(N);

        private static bool Snapshot(in PositionSnapshot s) => NanTracker.IsFinite(s.x, s.y, s.z) && float.IsFinite(s.interval);

        private static string Describe(in PositionSnapshot s) =>
            $"{NanFormat.Vec(s.x, s.y, s.z)} interval {NanFormat.Value(s.interval)}{(s.isTeleport ? " teleport" : "")}";

        public string Describe() => !Captured
            ? "(not captured)"
            : $"dtAccum {NanFormat.Value(DtAccum)} targetSpeed {NanFormat.Value(TargetSpeed)} queueCount {QueueCount} wait {Wait}; "
              + $"pL {Describe(L)}; pN {Describe(N)}";
    }

    /// <summary>What a traced interpolation call's start saw: the trace's and the interpolation's state.</summary>
    public readonly struct InterpolationTrace(TraceState trace, InterpolationState state)
    {
        public TraceState Trace { get; } = trace;
        public InterpolationState State { get; } = state;
    }

    private static void InterpolationEnter(string site, EntityBehaviorInterpolatePosition ip, out InterpolationTrace state)
    {
        var player = default(MotionProbe);
        if (Watching() is { } m)
        {
            player = Tracker.Before(site, ip, m.X, m.Y, m.Z, out bool tripped);
            if (tripped)
                OnTrip();
        }
        var before = EntityWatching(ip) ? InterpolationState.Of(ip) : default;
        var entity = EntityEnter(site, ip, !before.Captured || before.Finite, out bool arrived);
        if (arrived)
            OnEntityTrip("the interpolation's state at the start: " + before.Describe());
        state = new InterpolationTrace(new TraceState(player, entity), before);
    }

    private static void InterpolationExit(string site, EntityBehaviorInterpolatePosition ip, in InterpolationTrace state)
    {
        if (state.Trace.Player.Watched && Watching() is { } m && Tracker.After(site, ip, state.Trace.Player, m.X, m.Y, m.Z))
            OnTrip();
        if (!state.Trace.Entity.Watched)
            return;
        var after = InterpolationState.Of(ip);
        if (EntityExit(site, ip, state.Trace.Entity, after.Finite))
            OnEntityTrip($"the interpolation's state at the start: {state.State.Describe()}\nthe interpolation's state at the end:   {after.Describe()}");
    }

    public static class TraceInterpolationPop
    {
        public const string Site = "EntityBehaviorInterpolatePosition.PopQueue (moves the entity to the next snapshot and runs its remote physics)";
        public static void Prefix(EntityBehaviorInterpolatePosition __instance, out InterpolationTrace __state) => InterpolationEnter(Site, __instance, out __state);
        public static void Postfix(EntityBehaviorInterpolatePosition __instance, InterpolationTrace __state) => InterpolationExit(Site, __instance, in __state);
    }

    public static class TraceInterpolationServerPos
    {
        public const string Site = "EntityBehaviorInterpolatePosition.OnReceivedServerPos (queues the server's position)";
        public static void Prefix(EntityBehaviorInterpolatePosition __instance, out InterpolationTrace __state) => InterpolationEnter(Site, __instance, out __state);
        public static void Postfix(EntityBehaviorInterpolatePosition __instance, InterpolationTrace __state) => InterpolationExit(Site, __instance, in __state);
    }

    /// <summary>A renderer's frame, for the record: its dt and stage, and the interpolation's state
    /// at its start and end where the renderer is one.</summary>
    private static string FrameContext(float dt, EnumRenderStage stage, EntityBehaviorInterpolatePosition? ip,
        InterpolationState before, InterpolationState? after)
    {
        var sb = new StringBuilder($"the frame: dt {NanFormat.Value(dt)} stage {stage}, game paused {_capi?.IsGamePaused}");
        if (ip != null)
        {
            sb.Append("\nthe interpolation's state at the start: ").Append(before.Describe());
            if (after is { } a)
                sb.Append("\nthe interpolation's state at the end:   ").Append(a.Describe());
            sb.Append($"\nmountable {ip.mountableSupplier?.GetType().FullName ?? "none"}, remote physics {ip.physics?.GetType().FullName ?? "none"}");
        }
        return sb.ToString();
    }

    // ---------------------------------------------------------------- the sweep

    /// <summary>The client's sweep: every loaded entity (no allocation: the dictionary's own enumerator).</summary>
    private static void SweepLoaded(float dt)
    {
        if (EntityTracker.Tripped || _capi?.World is not { } world)
            return;
        long now = world.ElapsedMilliseconds;
        foreach (var e in world.LoadedEntities.Values)
            if (SweepOne(e, now))
                return;
        EntityTracker.SweepDone(now);
    }

    /// <summary>A sweep of <paramref name="entities"/>. Returns whether it tripped.</summary>
    public static bool Sweep(IEnumerable<Entity> entities)
    {
        if (EntityTracker.Tripped)
            return false;
        long now = _api?.World?.ElapsedMilliseconds ?? 0;
        foreach (var e in entities)
            if (SweepOne(e, now))
                return true;
        EntityTracker.SweepDone(now);
        return false;
    }

    private static bool SweepOne(Entity e, long now)
    {
        if (e == null || IsPlayer(e) || e.Pos is not { } p)
            return false;
        var m = p.Motion;
        if (!EntityTracker.Sweep(SweepSite, e, e.EntityId, now, p.X, p.Y, p.Z, m.X, m.Y, m.Z))
            return false;
        OnEntityTrip(null);
        return true;
    }

    // ---------------------------------------------------------------- the trip and its report

    /// <summary>The first entity trip: its record, logged as a warning and written to its own file.
    /// Never throws.</summary>
    private static void OnEntityTrip(string? context)
    {
        var trip = EntityTracker.Trip;
        if (trip == null)
            return;
        bool wasIn = _inReport;
        _inReport = true;
        try
        {
            var lines = new List<string>
            {
                $"local time {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}, world elapsed {_api?.World?.ElapsedMilliseconds} ms, "
                + $"calendar {_api?.World?.Calendar?.TotalHours:0.000} h, game paused {_capi?.IsGamePaused}",
                ClockEvents(),
            };
            lines.AddRange(EntityTracker.Describe(Describe));
            if (trip.Entity is Entity e)
                lines.AddRange(EntityDetail(e));
            if (context != null)
                lines.AddRange(context.Split('\n'));
            lines.Add(PacketLine(trip.EntityId));
            lines.Add("client frames before it, oldest first:");
            lines.AddRange(ClientFrames.Describe().Select(l => "  " + l));
            var stack = new StackTrace(1, false);
            lines.Add("patched methods on the stack, innermost first (for a trip INSIDE a call, the first is the one that made it):");
            lines.AddRange(PatchedOnStack(stack).Select(m => "  " + Label(m)));
            lines.Add("stack:");
            lines.Add(stack.ToString());
            trip.Detail = string.Join("\n", lines);
        }
        catch (Exception ex)
        {
            trip.Detail = $"(describing the entity trip failed: {ex})";
        }
        try
        {
            _api?.Logger.Warning("[seraphhorizons] NaN motion diagnostics: an entity's position or motion went non-finite on the client (#405). "
                                 + "Nothing is changed; the record follows and is written to its own file.\n{0}", trip.Detail);
        }
        catch
        {
            // The file has it.
        }
        WriteEntityReport(trip);
        _inReport = wasIn;
    }

    /// <summary>When the server last suspended and resumed its ticking, and the game last paused and
    /// unpaused, before now.</summary>
    private static string ClockEvents()
    {
        long now = Environment.TickCount64;
        string Ago(long at) => at == 0 ? "never seen" : $"{now - at} ms ago";
        return $"server ticking last suspended (an autosave) {Ago(_suspendedAt)}, resumed {Ago(_resumedAt)}; "
               + $"game last paused {Ago(_pausedAt)}, unpaused {Ago(_unpausedAt)}";
    }

    private static string PacketLine(long entityId)
    {
        var p = _lastPacket;
        if (!p.Received)
            return "last entity position packet from the server: none recorded";
        return $"last entity position packet from the server: for {(p.EntityId == entityId ? "THIS entity" : $"entity #{p.EntityId}")}, "
               + $"at {p.AtMs} ms: pos {NanFormat.Vec(p.X, p.Y, p.Z)} motion {NanFormat.Vec(p.MotionX, p.MotionY, p.MotionZ)} "
               + $"tick {p.Tick} teleport {p.Teleport}";
    }

    /// <summary>The tripped entity: its line, behaviors, the ticks the interpolation's intervals come
    /// from, its interpolation now and its mount.</summary>
    private static IEnumerable<string> EntityDetail(Entity e)
    {
        string Safe(Func<string> line)
        {
            try
            {
                return line();
            }
            catch (Exception ex)
            {
                return $"  (this line failed: {ex.GetType().Name}: {ex.Message})";
            }
        }
        yield return Safe(() => "entity: " + NanMotionReport.EntityLine(e, _watched));
        yield return Safe(() => $"  alive {e.Alive} state {e.State} onGround {e.OnGround} "
                                + $"behaviors {string.Join(",", e.SidedProperties?.Behaviors.Select(b => b.GetType().Name) ?? [])}");
        yield return Safe(() => $"  attributes: tick {e.Attributes.GetInt("tick")} tickDiff {e.Attributes.GetInt("tickDiff", 1)}");
        yield return Safe(() => e.SidedProperties != null && e.GetBehavior<EntityBehaviorInterpolatePosition>() is { } ip
            ? "  its interpolation now: " + InterpolationState.Of(ip).Describe()
            : "  no interpolation");
        yield return Safe(() => e is EntityAgent { MountedOn: { } seat }
            ? $"  mounted on {seat.Entity?.Code} #{seat.Entity?.EntityId}"
            : "  not mounted");
    }

    /// <summary>Writes the entity report once: the record and every non-finite entity now. Never throws.</summary>
    private static void WriteEntityReport(EntityNanTrip trip)
    {
        if (EntityReportPath != null || _api is not { } api)
            return;
        try
        {
            var r = new ReportWriter();
            r.Line("NaN entity report (seraphhorizons, #405): an entity other than the player went non-finite on the client.");
            r.Line("Nothing is changed. Please attach this file to https://github.com/benjamin-kirkbride/Seraph-Horizons/issues/405");
            r.Line($"game {GameVersion.LongGameVersion}, side {api.Side}");
            r.Line("");
            r.Section("Where it first went non-finite", sb =>
            {
                foreach (var line in (trip.Detail ?? "").Split('\n'))
                    sb.Append("  ").Append(line).Append('\n');
            });
            r.Section("Every loaded entity with a non-finite position or motion now", sb => NonFinite(sb, api));
            r.Section("Diagnostics coverage", sb =>
            {
                sb.Append($"  {PatchedCount} patches went in; {PatchFailures.Count} failed\n");
                foreach (var f in PatchFailures)
                    sb.Append("    failed: ").Append(f).Append('\n');
                sb.Append($"  {EntityTracker.Checkpoints} entity checkpoints looked, {EntityTracker.CleanSweeps} clean sweeps\n");
            });
            string dir = GamePaths.Logs;
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, $"seraphhorizons-nanentity-{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.txt");
            File.WriteAllText(path, r.ToString());
            EntityReportPath = path;
            api.Logger.Warning("[seraphhorizons] NaN motion diagnostics: the entity report is in {0}", path);
        }
        catch (Exception e)
        {
            try
            {
                api.Logger.Warning("[seraphhorizons] NaN motion diagnostics: could not write the entity report: {0}", e.Message);
            }
            catch
            {
                // Nothing more to do.
            }
        }
    }

    private static void NonFinite(StringBuilder sb, ICoreAPI api)
    {
        int found = 0, total = 0;
        foreach (var e in NanMotionReport.Loaded(api).ToList())
        {
            total++;
            var p = e.Pos;
            if (NanTracker.IsFinite(p.X, p.Y, p.Z) && NanTracker.IsFinite(p.Motion.X, p.Motion.Y, p.Motion.Z))
                continue;
            if (++found <= ListedEntities)
                sb.Append("  ").Append(NanMotionReport.EntityLine(e, _watched)).Append('\n');
        }
        if (found > ListedEntities)
            sb.Append($"  ... and {found - ListedEntities} more\n");
        sb.Append($"  {found} of {total} loaded\n");
    }
}
