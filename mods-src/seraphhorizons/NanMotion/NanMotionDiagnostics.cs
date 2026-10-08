using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using HarmonyLib;
using SeraphHorizons.Mod.NanMotion.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.NanMotion;

/// <summary>
/// NaN motion diagnostics (<c>NanMotionDiagnostics</c>, #405): the client crashes with
/// <c>Given pos contained NaN ... for entity game:player</c> from
/// <c>EntityBehaviorControlledPhysics.ApplyTests</c> when the local player's motion has gone NaN,
/// and nobody knows what made it NaN. This finds out, and changes nothing about the crash: no NaN is
/// repaired or dropped, and the exception is left alone.
///
/// <para>It traces every place on the client that can write the player's <c>Pos.Motion</c>, each
/// with a checkpoint at its start and its end (<see cref="NanTracker"/>), and records the first one
/// that sees the motion go from finite to non-finite: the method and its mod, its instance (which
/// entity's behavior, which tick listener, which renderer), the game time, the stack and the motion
/// before and after. Traced (README "NaN motion diagnostics" has the why of each):</para>
/// <list type="bullet">
/// <item>every <c>EntityBehavior.OnGameTick</c> and <c>OnReceivedServerPos</c> override, and every
/// <c>Entity.OnGameTick</c> and <c>OnReceivedServerPos</c>, in every loaded assembly (mods too):
/// another entity's behavior can push the player, so each checks the player's motion, not its own
/// entity's;</item>
/// <item>every <c>PModule.DoApply</c> override, the player physics' <c>OnRenderFrame</c>,
/// <c>SetState</c>, <c>MotionAndCollision</c>, <c>ApplyTests</c>, <c>AfterPhysicsTick</c>
/// and <c>CollisionTester.ApplyTerrainCollision</c>, and <c>SimPhysics</c> with a checkpoint
/// before every mod's prefix, one after them, and one after every postfix, so a NaN made inside the
/// physics step is told apart from one that arrives from outside, and from one a mod's patch on
/// <c>SimPhysics</c> makes;</item>
/// <item>the client's tick listeners (<c>GameTickListener.OnTriggered</c>), each renderer
/// (a transpiler wraps each <c>OnRenderFrame</c> call in <c>ClientEventManager.TriggerRenderStage</c>),
/// main thread tasks, server packets, mod network channels, and the server's entity positions
/// (<c>SystemNetworkProcess.HandleSinglePacket</c>, which writes motion from the packet);</item>
/// <item>the player's repulsion: each neighbour <c>EntityBehaviorRepulseAgents.WalkEntity</c> walks
/// is checked, so a neighbour that makes the push non-finite is named.</item>
/// </list>
///
/// <para>When the crash comes (<c>ApplyTests</c> is entered with a non-finite position or motion,
/// or an exception about a NaN is thrown on the client thread: <c>AppDomain.FirstChanceException</c>,
/// which sees it before anything catches it and leaves it as it is), it writes a full report
/// (<see cref="NanMotionReport"/>) to the client log and to its own file in the game's log folder,
/// before the exception propagates. No Harmony finalizer: Harmony rethrows a finalizer's exception,
/// which cuts its stack trace short in the crash log.</para>
///
/// <para>Cheap while nothing is wrong: each checkpoint reads the thread id, the player's motion and
/// three doubles, and allocates nothing (the probe is a value type); after the first trip every
/// checkpoint returns at once. Client side only, patched explicitly (never PatchAll) when the level is
/// final, under its own Harmony id.</para>
/// </summary>
public static class NanMotionDiagnostics
{
    public const string HarmonyId = "seraphhorizons.nanmotion";

    /// <summary>How many physics frames of the player's motion the report shows.</summary>
    public const int FrameCount = 20;

    public enum TargetKind
    {
        BehaviorTick,
        EntityTick,
        BehaviorServerPos,
        EntityServerPos,
        PhysicsModule,
        AfterPhysicsTick,
        Fixed,
    }

    /// <summary>A traced method, and the class whose prefix and postfix trace it.</summary>
    public sealed record Target(MethodBase Method, TargetKind Kind, Type Trace);

    private static ICoreAPI? _api;
    private static ICoreClientAPI? _capi;
    private static Entity? _watched;
    private static int _threadId = -1;
    private static bool _reported;
    [ThreadStatic] private static bool _inReport;
    private static Dictionary<Assembly, string>? _modIds;

    private static readonly AccessTools.FieldRef<EntityBehaviorRepulseAgents, Vec3d>? PushVector =
        AccessTools.Field(typeof(EntityBehaviorRepulseAgents), "pushVector") != null
            ? AccessTools.FieldRefAccess<EntityBehaviorRepulseAgents, Vec3d>("pushVector")
            : null;

    public static NanTracker Tracker { get; private set; } = new();
    public static MotionRing Frames { get; private set; } = new(FrameCount);

    /// <summary>The neighbour whose repulsion first made the player's push non-finite, if one did.</summary>
    public static string? RepulsionCulprit { get; private set; }

    /// <summary>The report file written, once there is one.</summary>
    public static string? ReportPath { get; private set; }

    /// <summary>The methods patched, and those that failed to patch (with why).</summary>
    public static int PatchedCount { get; private set; }
    public static List<string> PatchFailures { get; } = [];

    /// <summary>The entity whose motion is watched: the local player once its physics has run.</summary>
    public static Entity? Watched => _watched;

    // ---------------------------------------------------------------- targets

    /// <summary>The assemblies searched for overrides: the game's API and everything that references
    /// it (the game's own mods, every code mod, this one).</summary>
    public static IEnumerable<Assembly> GameAssemblies()
    {
        var api = typeof(Entity).Assembly;
        string apiName = api.GetName().Name!;
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (asm.IsDynamic)
                continue;
            bool refs;
            try
            {
                refs = asm == api || asm.GetReferencedAssemblies().Any(n => n.Name == apiName);
            }
            catch
            {
                continue;
            }
            if (refs)
                yield return asm;
        }
    }

    private static IEnumerable<Type> TypesOf(Assembly asm)
    {
        Type?[] types;
        try
        {
            types = asm.GetTypes();
        }
        catch (ReflectionTypeLoadException e)
        {
            types = e.Types;
        }
        catch
        {
            yield break;
        }
        foreach (var t in types)
            if (t != null)
                yield return t;
    }

    /// <summary>Every concrete override (in <paramref name="baseType"/> or a type deriving from it) of
    /// <paramref name="baseType"/>'s virtual method, with a body. <paramref name="includeBase"/>: the
    /// base's own method too (it does work of its own).</summary>
    public static List<MethodInfo> Overrides(IEnumerable<Type> types, Type baseType, string name, Type[] parameters, bool includeBase)
    {
        var found = new List<MethodInfo>();
        foreach (var t in types)
        {
            if (!baseType.IsAssignableFrom(t) || t.ContainsGenericParameters || (t == baseType && !includeBase))
                continue;
            MethodInfo? m;
            try
            {
                m = t.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                    null, parameters, null);
                if (m == null || m.IsAbstract || !m.IsVirtual || m.GetBaseDefinition().DeclaringType != baseType
                    || m.GetMethodBody() == null)
                    continue;
            }
            catch
            {
                continue;
            }
            found.Add(m);
        }
        return found;
    }

    private static MethodInfo? Declared(Type? type, string name, Type[]? parameters = null) =>
        type == null ? null : parameters == null ? AccessTools.DeclaredMethod(type, name) : AccessTools.DeclaredMethod(type, name, parameters);

    /// <summary>The methods that are not found by searching for overrides, by name (null where the
    /// game no longer has it), each with its trace class. Client types are looked up by name: they are
    /// internal or may move.</summary>
    public static List<(string Label, MethodBase? Method, Type Trace)> FixedTargets()
    {
        var cp = typeof(EntityBehaviorControlledPhysics);
        var pp = typeof(EntityBehaviorPlayerPhysics);
        return
        [
            ("EntityBehaviorPlayerPhysics.OnRenderFrame", Declared(pp, "OnRenderFrame", [typeof(float), typeof(EnumRenderStage)]), typeof(TracePhysicsFrame)),
            ("EntityBehaviorControlledPhysics.SetState", Declared(cp, "SetState", [typeof(EntityPos), typeof(float)]), typeof(TraceSetState)),
            ("EntityBehaviorControlledPhysics.MotionAndCollision",
                Declared(cp, "MotionAndCollision", [typeof(EntityPos), typeof(EntityControls), typeof(float)]), typeof(TraceMotionAndCollision)),
            ("EntityBehaviorControlledPhysics.ApplyTests", ApplyTestsMethod, typeof(TraceApplyTests)),
            ("CollisionTester.ApplyTerrainCollision", Declared(typeof(CollisionTester), "ApplyTerrainCollision"), typeof(TraceTerrainCollision)),
            ("GameTickListener.OnTriggered", Declared(AccessTools.TypeByName("Vintagestory.Common.GameTickListener"), "OnTriggered"), typeof(TraceTickListener)),
            ("ClientMain.ExecuteMainThreadTasks",
                Declared(AccessTools.TypeByName("Vintagestory.Client.NoObf.ClientMain"), "ExecuteMainThreadTasks"), typeof(TraceMainThreadTasks)),
            ("ProcessPacketTask.ProcessPacket",
                Declared(AccessTools.TypeByName("Vintagestory.Client.NoObf.ProcessPacketTask"), "ProcessPacket"), typeof(TraceServerPacket)),
            ("NetworkChannel.OnPacket", Declared(AccessTools.TypeByName("Vintagestory.Client.NoObf.NetworkChannel"), "OnPacket"), typeof(TraceChannelPacket)),
            ("UdpNetworkChannel.OnPacket", Declared(AccessTools.TypeByName("Vintagestory.Client.NoObf.UdpNetworkChannel"), "OnPacket"), typeof(TraceChannelPacket)),
            ("SystemNetworkProcess.HandleSinglePacket",
                Declared(AccessTools.TypeByName("Vintagestory.Client.NoObf.SystemNetworkProcess"), "HandleSinglePacket"), typeof(TraceEntityPositionPacket)),
        ];
    }

    /// <summary>The trace class of the overrides of each kind.</summary>
    public static Type TraceFor(TargetKind kind) => kind switch
    {
        TargetKind.BehaviorTick => typeof(TraceBehaviorTick),
        TargetKind.EntityTick => typeof(TraceEntityTick),
        TargetKind.BehaviorServerPos => typeof(TraceBehaviorServerPos),
        TargetKind.EntityServerPos => typeof(TraceEntityServerPos),
        TargetKind.PhysicsModule => typeof(TracePhysicsModule),
        TargetKind.AfterPhysicsTick => typeof(TraceAfterPhysicsTick),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "a fixed target names its own trace"),
    };

    public static MethodInfo? SimPhysicsMethod =>
        Declared(typeof(EntityBehaviorPlayerPhysics), "SimPhysics", [typeof(float), typeof(EntityPos)]);

    public static MethodInfo? ApplyTestsMethod =>
        Declared(typeof(EntityBehaviorControlledPhysics), "ApplyTests", [typeof(EntityPos), typeof(EntityControls), typeof(float), typeof(bool)]);

    public static MethodInfo? WalkEntityMethod => Declared(typeof(EntityBehaviorRepulseAgents), "WalkEntity", [typeof(Entity)]);

    public static MethodInfo? TriggerRenderStageMethod =>
        Declared(AccessTools.TypeByName("Vintagestory.Client.NoObf.ClientEventManager"), "TriggerRenderStage");

    /// <summary>Every traced method: the overrides found in <paramref name="assemblies"/> and the
    /// fixed ones that exist, each once.</summary>
    public static List<Target> FindTargets(IEnumerable<Assembly> assemblies)
    {
        var types = assemblies.SelectMany(TypesOf).ToList();
        var targets = new List<Target>();
        var seen = new HashSet<MethodBase>();
        void Add(IEnumerable<MethodBase> methods, TargetKind kind)
        {
            foreach (var m in methods)
                if (seen.Add(m))
                    targets.Add(new Target(m, kind, TraceFor(kind)));
        }
        Add(Overrides(types, typeof(EntityBehavior), nameof(EntityBehavior.OnGameTick), [typeof(float)], includeBase: false), TargetKind.BehaviorTick);
        Add(Overrides(types, typeof(Entity), nameof(Entity.OnGameTick), [typeof(float)], includeBase: true), TargetKind.EntityTick);
        Add(Overrides(types, typeof(EntityBehavior), nameof(EntityBehavior.OnReceivedServerPos),
            [typeof(bool), typeof(EnumHandling).MakeByRefType()], includeBase: false), TargetKind.BehaviorServerPos);
        Add(Overrides(types, typeof(Entity), nameof(Entity.OnReceivedServerPos), [typeof(bool)], includeBase: true), TargetKind.EntityServerPos);
        Add(Overrides(types, typeof(PModule), nameof(PModule.DoApply),
            [typeof(float), typeof(Entity), typeof(EntityPos), typeof(EntityControls)], includeBase: false), TargetKind.PhysicsModule);
        Add(Overrides(types, typeof(EntityBehaviorControlledPhysics), nameof(EntityBehaviorControlledPhysics.AfterPhysicsTick),
            [typeof(float)], includeBase: true), TargetKind.AfterPhysicsTick);
        foreach (var (_, method, trace) in FixedTargets())
            if (method != null && seen.Add(method))
                targets.Add(new Target(method, TargetKind.Fixed, trace));
        return targets;
    }

    // ---------------------------------------------------------------- binding and patching

    /// <summary>Starts watching: <paramref name="watched"/> on <paramref name="threadId"/>. On the
    /// client neither is known before the player's first physics step, which sets both (see
    /// <see cref="SimPhysicsEntry"/>); the Atlas scenarios bind an entity of their own.</summary>
    public static void Bind(ICoreAPI api, Entity? watched, int threadId)
    {
        _api = api;
        _capi = api as ICoreClientAPI;
        _watched = watched;
        _threadId = threadId;
        _reported = false;
        _modIds = null;
        Tracker = new NanTracker();
        Frames = new MotionRing(FrameCount);
        RepulsionCulprit = null;
        ReportPath = null;
        AppDomain.CurrentDomain.FirstChanceException -= OnFirstChance;
        AppDomain.CurrentDomain.FirstChanceException += OnFirstChance;
    }

    /// <summary>Stops watching; patches left over do nothing.</summary>
    public static void Unbind()
    {
        AppDomain.CurrentDomain.FirstChanceException -= OnFirstChance;
        _watched = null;
        _threadId = -1;
        _api = null;
        _capi = null;
        _modIds = null;
    }

    /// <summary>Patches every target. Each patch is its own try: one that fails is logged and the rest
    /// go in. Returns how many went in.</summary>
    public static int Patch(Harmony harmony, ICoreAPI api, IEnumerable<Target> targets)
    {
        PatchFailures.Clear();
        int ok = 0;
        // The trace's prefix runs before every other mod's prefix, its postfix after every postfix.
        HarmonyMethod Prefix(Type trace) => new(trace, "Prefix") { priority = Priority.First };
        HarmonyMethod Postfix(Type trace) => new(trace, "Postfix") { priority = Priority.Last };
        void Try(string label, Action patch)
        {
            try
            {
                patch();
                ok++;
            }
            catch (Exception e)
            {
                PatchFailures.Add($"{label}: {e.GetType().Name}: {e.Message}");
            }
        }

        // SimPhysics: the frame recorder and binding first, then the trace, then a checkpoint after
        // every other mod's prefix (gondolacablecar's moves the player before the body runs).
        if (SimPhysicsMethod is { } sim)
        {
            Try("SimPhysics entry", () => harmony.Patch(sim,
                prefix: new HarmonyMethod(typeof(SimPhysicsEntry), nameof(SimPhysicsEntry.Prefix)) { priority = Priority.First }));
            Try("SimPhysics trace", () => harmony.Patch(sim, prefix: Prefix(typeof(TraceSimPhysics)), postfix: Postfix(typeof(TraceSimPhysics))));
            Try("SimPhysics body", () => harmony.Patch(sim,
                prefix: new HarmonyMethod(typeof(SimPhysicsBody), nameof(SimPhysicsBody.Prefix)) { priority = Priority.Last }));
        }
        else
            PatchFailures.Add("EntityBehaviorPlayerPhysics.SimPhysics(float, EntityPos): not found");

        if (ApplyTestsMethod is { } applyTests)
            Try("ApplyTests entry", () => harmony.Patch(applyTests,
                prefix: new HarmonyMethod(typeof(ApplyTestsEntry), nameof(ApplyTestsEntry.Prefix)) { priority = Priority.Last }));
        else
            PatchFailures.Add("EntityBehaviorControlledPhysics.ApplyTests: not found; the report is written from the first-chance exception only");

        if (WalkEntityMethod is { } walk)
            Try("RepulseAgents.WalkEntity", () => harmony.Patch(walk,
                postfix: new HarmonyMethod(typeof(RepulseWalk), nameof(RepulseWalk.Postfix)) { priority = Priority.Last }));
        else
            PatchFailures.Add("EntityBehaviorRepulseAgents.WalkEntity(Entity): not found; a repulsion culprit cannot be named");

        if (TriggerRenderStageMethod is { } stage)
            Try("ClientEventManager.TriggerRenderStage", () => harmony.Patch(stage,
                transpiler: new HarmonyMethod(typeof(RenderStage), nameof(RenderStage.Transpiler))));

        foreach (var target in targets)
            Try(Label(target.Method), () => harmony.Patch(target.Method, prefix: Prefix(target.Trace), postfix: Postfix(target.Trace)));
        PatchedCount = ok;
        return ok;
    }

    /// <summary>The client's setup, when the level is final: binds and patches.</summary>
    public static void PatchClient(Harmony harmony, ICoreClientAPI api)
    {
        var watch = Stopwatch.StartNew();
        Bind(api, null, -1);
        var targets = FindTargets(GameAssemblies());
        int ok = Patch(harmony, api, targets);
        api.Logger.Notification(
            "[seraphhorizons] NaN motion diagnostics: the player's motion is traced through {0} patches on {1} methods ({2} failed) in {3} ms; "
            + "a report is written to the log folder if it goes NaN", ok, targets.Count, PatchFailures.Count, watch.ElapsedMilliseconds);
        foreach (var failure in PatchFailures)
            api.Logger.Warning("[seraphhorizons] NaN motion diagnostics: could not patch {0}", failure);
    }

    // ---------------------------------------------------------------- checkpoints

    /// <summary>The watched motion, when this call is on the watched thread and the tracker still
    /// looks. Otherwise null.</summary>
    private static Vec3d? Watching()
    {
        var w = _watched;
        if (w == null || Tracker.Tripped || Environment.CurrentManagedThreadId != _threadId)
            return null;
        return w.Pos.Motion;
    }

    /// <summary>A checkpoint at the start of a traced call.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Enter(string site, object? instance, out MotionProbe state)
    {
        if (Watching() is not { } m)
        {
            state = default;
            return;
        }
        state = Tracker.Before(site, instance, m.X, m.Y, m.Z, out bool tripped);
        if (tripped)
            OnTrip();
    }

    /// <summary>A checkpoint at the end of a traced call.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Exit(string site, object? instance, in MotionProbe state)
    {
        if (!state.Watched || Watching() is not { } m)
            return;
        if (Tracker.After(site, instance, in state, m.X, m.Y, m.Z))
            OnTrip();
    }

    // The traces: a prefix (before every other mod's) and a postfix (after every other mod's) on each
    // target, one class per site, so the site is a constant. Not __originalMethod: Harmony computes it
    // with MethodBase.GetMethodFromHandle on every call, on every thread, watched or not; the exact
    // method is read from the stack when the trip happens. A class each also keeps their __state apart
    // (Harmony shares it between the patches of one class).

    public static class TraceBehaviorTick
    {
        public const string Site = "EntityBehavior.OnGameTick";
        public static void Prefix(object? __instance, out MotionProbe __state) => Enter(Site, __instance, out __state);
        public static void Postfix(object? __instance, MotionProbe __state) => Exit(Site, __instance, in __state);
    }

    public static class TraceEntityTick
    {
        public const string Site = "Entity.OnGameTick";
        public static void Prefix(object? __instance, out MotionProbe __state) => Enter(Site, __instance, out __state);
        public static void Postfix(object? __instance, MotionProbe __state) => Exit(Site, __instance, in __state);
    }

    public static class TraceBehaviorServerPos
    {
        public const string Site = "EntityBehavior.OnReceivedServerPos";
        public static void Prefix(object? __instance, out MotionProbe __state) => Enter(Site, __instance, out __state);
        public static void Postfix(object? __instance, MotionProbe __state) => Exit(Site, __instance, in __state);
    }

    public static class TraceEntityServerPos
    {
        public const string Site = "Entity.OnReceivedServerPos";
        public static void Prefix(object? __instance, out MotionProbe __state) => Enter(Site, __instance, out __state);
        public static void Postfix(object? __instance, MotionProbe __state) => Exit(Site, __instance, in __state);
    }

    public static class TracePhysicsModule
    {
        public const string Site = "PModule.DoApply (a physics module)";
        public static void Prefix(object? __instance, out MotionProbe __state) => Enter(Site, __instance, out __state);
        public static void Postfix(object? __instance, MotionProbe __state) => Exit(Site, __instance, in __state);
    }

    public static class TraceAfterPhysicsTick
    {
        public const string Site = "EntityBehaviorControlledPhysics.AfterPhysicsTick (the traversed blocks' OnEntityInside, the AfterPhysicsTick delegate)";
        public static void Prefix(object? __instance, out MotionProbe __state) => Enter(Site, __instance, out __state);
        public static void Postfix(object? __instance, MotionProbe __state) => Exit(Site, __instance, in __state);
    }

    public static class TracePhysicsFrame
    {
        public const string Site = "EntityBehaviorPlayerPhysics.OnRenderFrame (the player's physics frame)";
        public static void Prefix(object? __instance, out MotionProbe __state) => Enter(Site, __instance, out __state);
        public static void Postfix(object? __instance, MotionProbe __state) => Exit(Site, __instance, in __state);
    }

    public static class TraceSimPhysics
    {
        public const string Site = "EntityBehaviorPlayerPhysics.SimPhysics (with every mod's patch on it)";
        public static void Prefix(object? __instance, out MotionProbe __state) => Enter(Site, __instance, out __state);
        public static void Postfix(object? __instance, MotionProbe __state) => Exit(Site, __instance, in __state);
    }

    public static class TraceSetState
    {
        public const string Site = "EntityBehaviorControlledPhysics.SetState";
        public static void Prefix(object? __instance, out MotionProbe __state) => Enter(Site, __instance, out __state);
        public static void Postfix(object? __instance, MotionProbe __state) => Exit(Site, __instance, in __state);
    }

    public static class TraceMotionAndCollision
    {
        public const string Site = "EntityBehaviorControlledPhysics.MotionAndCollision (the physics modules)";
        public static void Prefix(object? __instance, out MotionProbe __state) => Enter(Site, __instance, out __state);
        public static void Postfix(object? __instance, MotionProbe __state) => Exit(Site, __instance, in __state);
    }

    public static class TraceApplyTests
    {
        public const string Site = "EntityBehaviorControlledPhysics.ApplyTests (collision, stepping, sneaking, with every mod's patch on it)";
        public static void Prefix(object? __instance, out MotionProbe __state) => Enter(Site, __instance, out __state);
        public static void Postfix(object? __instance, MotionProbe __state) => Exit(Site, __instance, in __state);
    }

    public static class TraceTerrainCollision
    {
        public const string Site = "CollisionTester.ApplyTerrainCollision (with every mod's patch on it)";
        public static void Prefix(object? __instance, out MotionProbe __state) => Enter(Site, __instance, out __state);
        public static void Postfix(object? __instance, MotionProbe __state) => Exit(Site, __instance, in __state);
    }

    public static class TraceTickListener
    {
        public const string Site = "GameTickListener.OnTriggered (a tick listener)";
        public static void Prefix(object? __instance, out MotionProbe __state) => Enter(Site, __instance, out __state);
        public static void Postfix(object? __instance, MotionProbe __state) => Exit(Site, __instance, in __state);
    }

    public static class TraceMainThreadTasks
    {
        public const string Site = "ClientMain.ExecuteMainThreadTasks (queued main thread tasks)";
        public static void Prefix(object? __instance, out MotionProbe __state) => Enter(Site, __instance, out __state);
        public static void Postfix(object? __instance, MotionProbe __state) => Exit(Site, __instance, in __state);
    }

    public static class TraceServerPacket
    {
        public const string Site = "ProcessPacketTask.ProcessPacket (a packet from the server)";
        public static void Prefix(object? __instance, out MotionProbe __state) => Enter(Site, __instance, out __state);
        public static void Postfix(object? __instance, MotionProbe __state) => Exit(Site, __instance, in __state);
    }

    public static class TraceChannelPacket
    {
        public const string Site = "NetworkChannel.OnPacket (a mod's network message)";
        public static void Prefix(object? __instance, out MotionProbe __state) => Enter(Site, __instance, out __state);
        public static void Postfix(object? __instance, MotionProbe __state) => Exit(Site, __instance, in __state);
    }

    public static class TraceEntityPositionPacket
    {
        public const string Site = "SystemNetworkProcess.HandleSinglePacket (an entity position from the server, motion included)";
        public static void Prefix(object? __instance, out MotionProbe __state) => Enter(Site, __instance, out __state);
        public static void Postfix(object? __instance, MotionProbe __state) => Exit(Site, __instance, in __state);
    }

    public const string SimPhysicsBodySite = "EntityBehaviorPlayerPhysics.SimPhysics, its body (after every mod's prefix on it)";

    /// <summary>First of all prefixes on <c>SimPhysics</c>: learns the player and the client thread
    /// (only the local player's renderer calls it), and records the frame.</summary>
    public static class SimPhysicsEntry
    {
        public static void Prefix(EntityBehaviorPlayerPhysics __instance, EntityPos pos)
        {
            var entity = __instance.entity;
            if (!ReferenceEquals(entity, _watched))
            {
                if (_capi == null || !ReferenceEquals(_capi.World?.Player?.Entity, entity))
                    return;
                _watched = entity;
                _threadId = Environment.CurrentManagedThreadId;
            }
            if (Environment.CurrentManagedThreadId != _threadId)
                return;
            var m = pos.Motion;
            var agent = entity as EntityAgent;
            Frames.Add(new MotionFrame(entity.World.ElapsedMilliseconds, m.X, m.Y, m.Z, pos.X, pos.Y, pos.Z, pos.Yaw,
                entity.OnGround, agent?.Controls.TriesToMove ?? false, (entity as EntityPlayer)?.walkSpeed ?? float.NaN));
        }
    }

    /// <summary>Last of all prefixes on <c>SimPhysics</c>: what every mod's prefix left.</summary>
    public static class SimPhysicsBody
    {
        public static void Prefix(EntityBehaviorPlayerPhysics __instance)
        {
            if (!ReferenceEquals(__instance.entity, _watched) || Watching() is not { } m)
                return;
            Tracker.Check(SimPhysicsBodySite, __instance, CheckPhase.Before, m.X, m.Y, m.Z, out bool tripped);
            if (tripped)
                OnTrip();
        }
    }

    /// <summary>Last prefix on <c>ApplyTests</c>, just before its NaN check: with the watched entity's
    /// position or motion non-finite, the check is about to throw, so the report is written now.</summary>
    public static class ApplyTestsEntry
    {
        public static void Prefix(EntityBehaviorControlledPhysics __instance, EntityPos pos)
        {
            if (_reported || !ReferenceEquals(__instance.entity, _watched) || Environment.CurrentManagedThreadId != _threadId)
                return;
            var m = pos.Motion;
            if (NanTracker.IsFinite(m.X, m.Y, m.Z) && NanTracker.IsFinite(pos.X, pos.Y, pos.Z))
                return;
            WriteReport("EntityBehaviorControlledPhysics.ApplyTests was entered with a non-finite position or motion: "
                        + $"pos {NanFormat.Vec(pos.X, pos.Y, pos.Z)} motion {NanFormat.Vec(m.X, m.Y, m.Z)}; "
                        + "its NaN check is about to throw (\"Given pos contained NaN\")");
        }
    }

    /// <summary>After each neighbour the player's repulsion walks: the first one that leaves the push
    /// non-finite is named. The push is reset at the start of every repulsion tick, so that one made it.</summary>
    public static class RepulseWalk
    {
        public static void Postfix(EntityBehaviorRepulseAgents __instance, Entity e)
        {
            if (RepulsionCulprit != null || !ReferenceEquals(__instance.entity, _watched) || PushVector == null
                || Watching() == null)
                return;
            var push = PushVector(__instance);
            if (push == null || NanTracker.IsFinite(push.X, push.Y, push.Z))
                return;
            try
            {
                RepulsionCulprit = $"pushVector became {NanFormat.Vec(push.X, push.Y, push.Z)} after walking "
                                   + NanMotionReport.EntityLine(e, _watched)
                                   + $"; its repulse behavior {e.BHRepulseAgents?.GetType().FullName ?? "none"}"
                                   + (e.BHRepulseAgents is EntityBehaviorRepulseAgents r
                                       ? $", ownPosRepulse {NanFormat.Vec(r.ownPosRepulseX, r.ownPosRepulseY, r.ownPosRepulseZ)}"
                                       : "")
                                   + $"; the player's ownPosRepulse {NanFormat.Vec(__instance.ownPosRepulseX, __instance.ownPosRepulseY, __instance.ownPosRepulseZ)}, "
                                   + $"mySize {NanFormat.Value(__instance.mySize)}, touchDistanceSq {NanFormat.Value(__instance.entity.touchDistanceSq)}"
                                   + $" + {NanFormat.Value(e.touchDistanceSq)}";
                _api?.Logger.Warning("[seraphhorizons] NaN motion diagnostics: the player's repulsion went non-finite: {0}", RepulsionCulprit);
            }
            catch (Exception ex)
            {
                RepulsionCulprit = $"(a neighbour made the push non-finite, but describing it failed: {ex.Message})";
            }
        }
    }

    /// <summary>Each renderer's <c>OnRenderFrame</c> in <c>TriggerRenderStage</c> called through
    /// <see cref="RenderFrame"/>, which traces it.</summary>
    public static class RenderStage
    {
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var target = AccessTools.Method(typeof(IRenderer), nameof(IRenderer.OnRenderFrame));
            var wrapper = AccessTools.Method(typeof(NanMotionDiagnostics), nameof(RenderFrame));
            foreach (var code in instructions)
            {
                if (code.opcode == OpCodes.Callvirt && code.operand is MethodInfo mi && mi == target)
                    yield return new CodeInstruction(OpCodes.Call, wrapper).MoveLabelsFrom(code).MoveBlocksFrom(code);
                else
                    yield return code;
            }
        }
    }

    public const string RendererSite = "a renderer's OnRenderFrame";

    /// <summary>A renderer's frame, traced.</summary>
    public static void RenderFrame(IRenderer renderer, float dt, EnumRenderStage stage)
    {
        if (Watching() is not { } m)
        {
            renderer.OnRenderFrame(dt, stage);
            return;
        }
        var probe = Tracker.Before(RendererSite, renderer, m.X, m.Y, m.Z, out bool tripped);
        if (tripped)
            OnTrip();
        renderer.OnRenderFrame(dt, stage);
        if (Watching() is { } after && Tracker.After(RendererSite, renderer, in probe, after.X, after.Y, after.Z))
            OnTrip();
    }

    // ---------------------------------------------------------------- the trip and the report

    private static void OnTrip()
    {
        var trip = Tracker.Trip;
        if (trip == null)
            return;
        try
        {
            var lines = new List<string>
            {
                $"local time {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}, world elapsed {_watched?.World?.ElapsedMilliseconds} ms, "
                + $"calendar {_watched?.World?.Calendar?.TotalHours:0.000} h",
            };
            lines.AddRange(Tracker.Describe(Describe));
            var stack = new StackTrace(2, false);
            lines.Add("patched methods on the stack, innermost first (for a trip INSIDE a call, the first is the one that made it):");
            lines.AddRange(PatchedOnStack(stack).Select(m => "  " + Label(m)));
            lines.Add("stack:");
            lines.Add(stack.ToString());
            trip.Detail = string.Join("\n", lines);
            _api?.Logger.Warning("[seraphhorizons] NaN motion diagnostics: the player's motion went non-finite (#405). "
                                 + "Nothing is changed; if the client crashes, the full report follows.\n{0}", trip.Detail);
        }
        catch (Exception e)
        {
            trip.Detail = $"(describing the trip failed: {e})";
        }
    }

    private static void OnFirstChance(object? sender, FirstChanceExceptionEventArgs e)
    {
        if (_reported || _inReport || _watched == null || Environment.CurrentManagedThreadId != _threadId)
            return;
        var ex = e.Exception;
        if (ex is not ArgumentException && ex.GetType() != typeof(Exception))
            return;
        try
        {
            _inReport = true;
            string message = ex.Message;
            if (!message.Contains("NaN", StringComparison.Ordinal))
                return;
            var w = _watched;
            var m = w.Pos.Motion;
            bool watchedNonFinite = !NanTracker.IsFinite(m.X, m.Y, m.Z) || !NanTracker.IsFinite(w.Pos.X, w.Pos.Y, w.Pos.Z);
            if (!watchedNonFinite && !message.Contains(w.Code?.ToString() ?? "game:player", StringComparison.Ordinal))
                return;
            WriteReport($"{ex.GetType().FullName}: {message}");
        }
        catch
        {
            // Never in the way of the exception.
        }
        finally
        {
            _inReport = false;
        }
    }

    /// <summary>Writes the report once: to the log as an error and to its own file. Never throws.</summary>
    public static void WriteReport(string reason)
    {
        if (_reported)
            return;
        _reported = true;
        bool wasIn = _inReport;
        _inReport = true;
        try
        {
            var api = _api;
            var watched = _watched;
            if (api == null || watched == null)
                return;
            string report;
            try
            {
                report = NanMotionReport.Build(api, watched, reason);
            }
            catch (Exception e)
            {
                report = $"NaN motion report (#405): building it failed: {e}\nreason: {reason}";
            }
            try
            {
                string dir = Vintagestory.API.Config.GamePaths.Logs;
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, $"seraphhorizons-nanmotion-{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.txt");
                File.WriteAllText(path, report);
                ReportPath = path;
            }
            catch (Exception e)
            {
                report += $"\n(could not write the report file: {e.Message})";
            }
            try
            {
                api.Logger.Error("[seraphhorizons] NaN motion diagnostics (#405){0}:\n{1}",
                    ReportPath != null ? $", also in {ReportPath}" : "", report);
            }
            catch
            {
                // The file has it.
            }
        }
        catch
        {
            // Never in the way of the crash.
        }
        finally
        {
            _inReport = wasIn;
        }
    }

    // ---------------------------------------------------------------- describing

    /// <summary>The original methods of the Harmony-patched frames on the stack, innermost first, at
    /// most 12.</summary>
    public static List<MethodBase> PatchedOnStack(StackTrace stack)
    {
        var found = new List<MethodBase>();
        foreach (var frame in stack.GetFrames())
        {
            MethodBase? original;
            try
            {
                original = Harmony.GetOriginalMethodFromStackframe(frame);
            }
            catch
            {
                continue;
            }
            if (original == null || original.DeclaringType == typeof(NanMotionDiagnostics) || found.Contains(original))
                continue;
            if (Harmony.GetPatchInfo(original) == null)
                continue;
            found.Add(original);
            if (found.Count == 12)
                break;
        }
        return found;
    }

    /// <summary>A method as <c>Type.Method [assembly, mod]</c>.</summary>
    public static string Label(MethodBase m) =>
        $"{m.DeclaringType?.FullName}.{m.Name} [{ModOf(m.DeclaringType?.Assembly)}]";

    /// <summary>An assembly as its name and the mod that has it, where one does.</summary>
    public static string ModOf(Assembly? asm)
    {
        if (asm == null)
            return "?";
        string name = asm.GetName().Name ?? "?";
        try
        {
            if (_modIds == null && _api != null)
            {
                var ids = new Dictionary<Assembly, string>();
                foreach (var mod in _api.ModLoader.Mods)
                foreach (var system in mod.Systems)
                    ids.TryAdd(system.GetType().Assembly, mod.Info?.ModID ?? "?");
                _modIds = ids;
            }
            if (_modIds != null && _modIds.TryGetValue(asm, out var id))
                return $"{name}, mod {id}";
        }
        catch
        {
            // The name alone.
        }
        return name;
    }

    /// <summary>A checkpoint's site and instance, for the report.</summary>
    public static string Describe(object site, object? instance)
    {
        string where = site is MethodBase m ? Label(m) : site.ToString() ?? "?";
        string what = "";
        try
        {
            what = instance switch
            {
                null => "",
                EntityBehavior b => $" of {NanMotionReport.EntityLine(b.entity, _watched)} (behavior {b.GetType().FullName} [{ModOf(b.GetType().Assembly)}])",
                Entity e => $" of {NanMotionReport.EntityLine(e, _watched)}",
                PModule p => $" ({p.GetType().FullName} [{ModOf(p.GetType().Assembly)}])",
                IRenderer r => $" ({r.GetType().FullName} [{ModOf(r.GetType().Assembly)}])",
                _ => DescribeOther(instance),
            };
        }
        catch (Exception e)
        {
            what = $" (describing its instance failed: {e.Message})";
        }
        return where + what;
    }

    private static string DescribeOther(object instance)
    {
        var type = instance.GetType();
        // GameTickListener: which handler.
        if (AccessTools.Field(type, "Handler")?.GetValue(instance) is Delegate handler)
            return $" (handler {handler.Method.DeclaringType?.FullName}.{handler.Method.Name} [{ModOf(handler.Method.DeclaringType?.Assembly)}]"
                   + $" on {handler.Target?.GetType().FullName ?? "static"})";
        if (instance is INetworkChannel channel)
            return $" (channel {channel.ChannelName})";
        // ProcessPacketTask: which packet.
        if (AccessTools.Field(type, "packet")?.GetValue(instance) is { } packet)
            return $" (packet id {AccessTools.Property(packet.GetType(), "Id")?.GetValue(packet) ?? AccessTools.Field(packet.GetType(), "Id")?.GetValue(packet)})";
        return $" ({type.FullName})";
    }
}
