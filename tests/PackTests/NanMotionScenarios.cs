using System.Reflection;
using Atlas.XUnit;
using HarmonyLib;
using SeraphHorizons.Mod.NanMotion;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SeraphHorizons.PackTests;

/// <summary>
/// mods-src/seraphhorizons, NanMotionDiagnostics (#405): the client traces every place that can write
/// the player's motion and writes a report when the NaN crash comes. Atlas runs no client, so this
/// runs the same code on the server's process: it finds the targets in the loaded pack (every one
/// the diagnostics name by hand must still exist, and none may fail to patch), patches them under a
/// Harmony id of its own, watches an entity of its own on this thread (never spawned, so the server's
/// own physics never sees its NaN), and feeds NaNs in: a neighbour whose repulsion position is NaN
/// (the game's repulsion takes it, and the culprit is named), then a behavior that makes the motion
/// NaN (the trip names it, inside its <c>OnGameTick</c>), then <c>ApplyTests</c> with that motion,
/// which throws the crash's exception, unchanged, after the report is written.
/// </summary>
public partial class SharedWorldScenarios
{
    private const string NanMotionTestHarmonyId = "seraphhorizons.nanmotion.atlas";

    /// <summary>A behavior that makes its entity's motion NaN, standing in for the unknown culprit.</summary>
    public class NanPushBehavior(Entity entity) : EntityBehavior(entity)
    {
        public override void OnGameTick(float deltaTime) => entity.Pos.Motion.X = double.NaN;

        public override string PropertyName() => "seraphhorizons-nanpush";
    }

    /// <summary>A behavior that makes its entity's position NaN: the unknown writer of another entity.</summary>
    public class NanPositionBehavior(Entity entity) : EntityBehavior(entity)
    {
        public override void OnGameTick(float deltaTime) => entity.Pos.X = double.NaN;

        public override string PropertyName() => "seraphhorizons-nanposition";
    }

    /// <summary>A behavior whose tick does nothing: a traced call that only sees what arrives.</summary>
    public class QuietBehavior(Entity entity) : EntityBehavior(entity)
    {
        public override void OnGameTick(float deltaTime)
        {
        }

        public override string PropertyName() => "seraphhorizons-quiet";
    }

    /// <summary>A behavior that is a renderer, as the interpolation is, and makes its entity's position NaN.</summary>
    public class NanRenderBehavior(Entity entity) : EntityBehavior(entity), IRenderer
    {
        public double RenderOrder => 0;
        public int RenderRange => 9999;
        public void OnRenderFrame(float deltaTime, EnumRenderStage stage) => entity.Pos.Z = double.NaN;

        public void Dispose()
        {
        }

        public override string PropertyName() => "seraphhorizons-nanrender";
    }

    [AtlasScenario]
    public void NaN_motion_diagnostics_find_their_targets_trace_the_NaN_and_report_the_crash()
    {
        // Every hand-named target exists in the pinned game.
        Assert.All(NanMotionDiagnostics.FixedTargets(), f => Assert.True(f.Method != null, $"{f.Label} not found"));
        Assert.NotNull(NanMotionDiagnostics.SimPhysicsMethod);
        Assert.NotNull(NanMotionDiagnostics.ApplyTestsMethod);
        Assert.NotNull(NanMotionDiagnostics.WalkEntityMethod);
        Assert.NotNull(NanMotionDiagnostics.TriggerRenderStageMethod);

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var targets = NanMotionDiagnostics.FindTargets(NanMotionDiagnostics.GameAssemblies());
        output.WriteLine($"{targets.Count} targets found in {watch.ElapsedMilliseconds} ms: "
                         + string.Join(", ", targets.GroupBy(t => t.Kind).Select(g => $"{g.Key} {g.Count()}")));
        output.WriteLine("by assembly: " + string.Join(", ", targets.GroupBy(t => t.Method.DeclaringType!.Assembly.GetName().Name)
            .OrderByDescending(g => g.Count()).Select(g => $"{g.Key} {g.Count()}")));
        var methods = targets.Select(t => t.Method).ToHashSet();
        Assert.Contains(AccessTools.DeclaredMethod(typeof(PModuleOnGround), nameof(PModule.DoApply)), methods);
        Assert.Contains(AccessTools.DeclaredMethod(typeof(PModuleInAir), nameof(PModule.DoApply)), methods);
        Assert.Contains(AccessTools.DeclaredMethod(typeof(EntityBehaviorRepulseAgents), nameof(EntityBehavior.OnGameTick)), methods);
        Assert.Contains(AccessTools.DeclaredMethod(typeof(Entity), nameof(Entity.OnGameTick)), methods);
        Assert.Contains(AccessTools.DeclaredMethod(typeof(EntityPlayer), nameof(Entity.OnGameTick)), methods);
        var nanPush = AccessTools.DeclaredMethod(typeof(NanPushBehavior), nameof(EntityBehavior.OnGameTick));
        Assert.Contains(nanPush, methods);
        // Mods' overrides too, not only the game's: a target in a code mod other than the game's own.
        var modAssemblies = World.Api.ModLoader.Mods
            .Where(m => m.Info.ModID is not ("game" or "creative" or "survival"))
            .SelectMany(m => m.Systems).Select(s => s.GetType().Assembly).ToHashSet();
        Assert.Contains(targets, t => modAssemblies.Contains(t.Method.DeclaringType!.Assembly));
        // EntityBehavior's own empty OnGameTick is left alone (every behavior without an override calls it).
        Assert.DoesNotContain(AccessTools.DeclaredMethod(typeof(EntityBehavior), nameof(EntityBehavior.OnGameTick)), methods);

        var type = W.GetEntityType(new AssetLocation("game:player"))
                   ?? throw new Xunit.Sdk.XunitException("no game:player entity type");
        Entity Make(double x, long id)
        {
            var e = W.ClassRegistry.CreateEntity(type);
            e.EntityId = id;
            e.World = W;
            e.Api = World.Api;
            var at = World.Spawn.AddCopy(-61, 3, 117);
            e.Pos.SetPos(at.X + x, at.Y, at.Z + 0.5);
            e.SelectionBox = new Cuboidf(-0.3f, 0, -0.3f, 0.3f, 1.8f, 0.3f);
            e.CollisionBox = e.SelectionBox.Clone();
            e.touchDistanceSq = 1;
            return e;
        }
        // Ids no spawned entity has.
        var watched = Make(0.5, -405);
        var neighbour = Make(1.0, -406);

        var harmony = new Harmony(NanMotionTestHarmonyId);
        try
        {
            watch.Restart();
            int ok = NanMotionDiagnostics.Patch(harmony, World.Api, targets);
            output.WriteLine($"{ok} patches in {watch.ElapsedMilliseconds} ms");
            Assert.True(NanMotionDiagnostics.PatchFailures.Count == 0, string.Join("\n", NanMotionDiagnostics.PatchFailures));
            // Both renderer calls in TriggerRenderStage go through the wrapper.
            var stageCode = PatchProcessor.GetCurrentInstructions(NanMotionDiagnostics.TriggerRenderStageMethod!);
            Assert.Equal(2, stageCode.Count(c => c.operand is MethodInfo m && m.Name == nameof(NanMotionDiagnostics.RenderFrame)));
            Assert.Contains(Harmony.GetPatchInfo(nanPush)!.Prefixes, p => p.owner == NanMotionTestHarmonyId);

            NanMotionDiagnostics.Bind(World.Api, watched, Environment.CurrentManagedThreadId);

            // 1. The game's repulsion takes a neighbour's NaN position as it is: the push goes NaN, and
            // the neighbour is named.
            var repulse = new EntityBehaviorRepulseAgents(watched);
            var theirs = new EntityBehaviorRepulseAgents(neighbour) { ownPosRepulseX = double.NaN };
            neighbour.BHRepulseAgents = theirs;
            repulse.ownPosRepulseX = watched.Pos.X;
            repulse.ownPosRepulseY = watched.Pos.Y;
            repulse.ownPosRepulseZ = watched.Pos.Z;
            theirs.ownPosRepulseY = neighbour.Pos.Y;
            theirs.ownPosRepulseZ = neighbour.Pos.Z;
            NanMotionDiagnostics.WalkEntityMethod!.Invoke(repulse, [neighbour]);
            Assert.NotNull(NanMotionDiagnostics.RepulsionCulprit);
            Assert.Contains($"#{neighbour.EntityId}", NanMotionDiagnostics.RepulsionCulprit);
            Assert.Contains("NaN", NanMotionDiagnostics.RepulsionCulprit);
            Assert.False(NanMotionDiagnostics.Tracker.Tripped);

            // 2. A behavior that makes the motion NaN is named, inside its OnGameTick.
            new NanPushBehavior(watched).OnGameTick(0.05f);
            var trip = NanMotionDiagnostics.Tracker.Trip;
            Assert.NotNull(trip);
            Assert.Equal(SeraphHorizons.Mod.NanMotion.Core.TripKind.Inside, trip.Kind);
            Assert.Equal(NanMotionDiagnostics.TraceBehaviorTick.Site, trip.Site);
            Assert.Contains("INSIDE", trip.Detail);
            Assert.Contains($"behavior {typeof(NanPushBehavior).FullName}", trip.Detail);
            // The exact method, from the stack.
            Assert.Contains($"\n  {typeof(NanPushBehavior).FullName}.OnGameTick [PackTests]", trip.Detail);
            Assert.Contains("stack:", trip.Detail);

            // 3. The crash: ApplyTests throws its own exception, unchanged, after the report is written.
            var physics = new EntityBehaviorControlledPhysics(watched);
            var thrown = Assert.Throws<ArgumentException>(() =>
                physics.ApplyTests(watched.Pos, new EntityControls(), 1 / 60f, remote: false));
            Assert.StartsWith("Given pos contained NaN", thrown.Message);
            Assert.EndsWith("for entity game:player", thrown.Message);
            Assert.Contains("ApplyTests", thrown.StackTrace);

            var path = NanMotionDiagnostics.ReportPath;
            Assert.NotNull(path);
            Assert.StartsWith(GamePaths.Logs, path);
            string report = File.ReadAllText(path);
            output.WriteLine(report);
            Assert.Contains("ApplyTests was entered with a non-finite position or motion", report);
            Assert.Contains($"Went non-finite INSIDE {NanMotionDiagnostics.TraceBehaviorTick.Site} of game:player #-405", report);
            Assert.Contains("Repulsion: pushVector became", report);
            foreach (var section in new[] { "== The player ==", "== The player's stats ==", "== The player's controls ==",
                         "== Blocks at and under the player ==", "== Harmony patches on the physics path ==", "== Diagnostics coverage ==" })
                Assert.Contains(section, report);
            Assert.Contains("Pos.Motion (NaN, 0.000000, 0.000000) <-- NON-FINITE", report);
            Assert.Contains(NanMotionTestHarmonyId, report);
            File.Delete(path);
        }
        finally
        {
            NanMotionDiagnostics.Unbind();
            harmony.UnpatchAll(NanMotionTestHarmonyId);
        }
    }

    /// <summary>
    /// The same diagnostics for every entity other than the player (#405: the player's motion went NaN
    /// because a neighbour's position already was, and dozens of entities were NaN on the client). The
    /// interpolation that writes every remote entity's position, the remote physics and the context
    /// patches must all be found and patched; then a behavior that makes another entity's position NaN
    /// is named inside its tick and the entity report is written at once, a renderer that does it is
    /// named with its frame, the interpolation's own <c>PopQueue</c> taking a NaN snapshot is named with
    /// its state, an entity already NaN is blamed on untraced code, and the sweep finds one nothing
    /// traced saw.
    /// </summary>
    [AtlasScenario]
    public void NaN_motion_diagnostics_name_what_makes_another_entity_NaN()
    {
        Assert.NotNull(NanMotionDiagnostics.MainRenderLoopMethod);
        Assert.NotNull(NanMotionDiagnostics.ServerSuspendMethod);
        Assert.NotNull(NanMotionDiagnostics.HandleSinglePacketMethod);
        var targets = NanMotionDiagnostics.FindTargets(NanMotionDiagnostics.GameAssemblies());
        var remote = targets.Where(t => t.Kind == NanMotionDiagnostics.TargetKind.RemotePhysics).Select(t => t.Method).ToList();
        output.WriteLine("remote physics traced: " + string.Join(", ", remote.Select(m => m.DeclaringType!.FullName)));
        Assert.Contains(AccessTools.DeclaredMethod(typeof(EntityBehaviorControlledPhysics), nameof(EntityBehaviorControlledPhysics.HandleRemotePhysics)), remote);
        Assert.Contains(AccessTools.DeclaredMethod(typeof(EntityBehaviorPassivePhysics), nameof(EntityBehaviorPassivePhysics.HandleRemotePhysics)), remote);
        Assert.Contains(remote, m => m.DeclaringType!.FullName == "SeraphHorizons.Mod.TrunkEntities.EntityBehaviorTrunkPhysics");
        // The interpolation's server position has its own trace (it records the interpolation's state), once.
        var interpServerPos = AccessTools.DeclaredMethod(typeof(EntityBehaviorInterpolatePosition), nameof(EntityBehavior.OnReceivedServerPos));
        Assert.Single(targets, t => t.Method == interpServerPos);
        Assert.Equal(typeof(NanMotionDiagnostics.TraceInterpolationServerPos), targets.Single(t => t.Method == interpServerPos).Trace);

        var type = W.GetEntityType(new AssetLocation("game:player"))
                   ?? throw new Xunit.Sdk.XunitException("no game:player entity type");
        Entity Make(double x, long id)
        {
            var e = W.ClassRegistry.CreateEntity(type);
            e.EntityId = id;
            e.World = W;
            e.Api = World.Api;
            var at = World.Spawn.AddCopy(-61, 3, 117);
            e.Pos.SetPos(at.X + x, at.Y, at.Z + 0.5);
            return e;
        }
        // Ids no spawned entity has.
        var watched = Make(0.5, -405);
        int thread = Environment.CurrentManagedThreadId;
        var reports = new List<string>();
        void KeepReport()
        {
            if (NanMotionDiagnostics.EntityReportPath is { } p)
                reports.Add(p);
        }

        var harmony = new Harmony(NanMotionTestHarmonyId);
        try
        {
            NanMotionDiagnostics.Patch(harmony, World.Api, targets);
            Assert.True(NanMotionDiagnostics.PatchFailures.Count == 0, string.Join("\n", NanMotionDiagnostics.PatchFailures));
            Assert.Contains(Harmony.GetPatchInfo(interpServerPos)!.Prefixes,
                p => p.owner == NanMotionTestHarmonyId && p.PatchMethod.DeclaringType == typeof(NanMotionDiagnostics.TraceInterpolationServerPos));

            // 1. A behavior that makes another entity's position NaN is named inside its tick, and the
            // entity report is written at once (no crash needed).
            NanMotionDiagnostics.Bind(World.Api, watched, thread);
            var cow = Make(3, -407);
            new NanPositionBehavior(cow).OnGameTick(0.05f);
            var trip = NanMotionDiagnostics.EntityTracker.Trip;
            Assert.NotNull(trip);
            Assert.Equal(SeraphHorizons.Mod.NanMotion.Core.EntityTripKind.Inside, trip.Kind);
            Assert.Equal(NanMotionDiagnostics.TraceBehaviorTick.Site, trip.Site);
            Assert.Equal(-407, trip.EntityId);
            Assert.True(trip.Before.Finite);
            Assert.False(trip.After.PositionFinite);
            Assert.Contains($"behavior {typeof(NanPositionBehavior).FullName}", trip.Detail);
            Assert.Contains($"\n  {typeof(NanPositionBehavior).FullName}.OnGameTick [PackTests]", trip.Detail);
            Assert.Contains("client frames before it", trip.Detail);
            Assert.Contains("server ticking last suspended", trip.Detail);
            // The player's own tracker is not touched by another entity.
            Assert.False(NanMotionDiagnostics.Tracker.Tripped);
            KeepReport();
            var path = NanMotionDiagnostics.EntityReportPath;
            Assert.NotNull(path);
            Assert.StartsWith(GamePaths.Logs, path);
            Assert.Contains("seraphhorizons-nanentity-", path);
            string entityReport = File.ReadAllText(path);
            output.WriteLine(entityReport);
            Assert.Contains("== Where it first went non-finite ==", entityReport);
            Assert.Contains("== Every loaded entity with a non-finite position or motion now ==", entityReport);
            Assert.Contains("Went non-finite INSIDE", entityReport);
            // The crash report carries it too.
            string crash = NanMotionReport.Build(World.Api, watched, "test");
            Assert.Contains("== Other entities: where a position or motion first went non-finite ==", crash);
            Assert.Contains($"Also written, when it happened, to {path}", crash);
            // Only the first: a later NaN is not looked at.
            new NanPositionBehavior(Make(4, -408)).OnGameTick(0.05f);
            Assert.Same(trip, NanMotionDiagnostics.EntityTracker.Trip);

            // 2. A renderer that is an entity's behavior, as the interpolation is, is named with its frame.
            NanMotionDiagnostics.Bind(World.Api, watched, thread);
            NanMotionDiagnostics.RenderFrame(new NanRenderBehavior(Make(5, -409)), 1 / 60f, EnumRenderStage.Before);
            trip = NanMotionDiagnostics.EntityTracker.Trip;
            Assert.NotNull(trip);
            Assert.Equal(NanMotionDiagnostics.RendererSite, trip.Site);
            Assert.Contains("the frame: dt 0.016667 stage Before", trip.Detail);
            KeepReport();

            // 3. The interpolation taking a NaN snapshot off its queue: named inside PopQueue, with its
            // state. (Its constructor refuses the server side, so it is made bare.)
            NanMotionDiagnostics.Bind(World.Api, watched, thread);
            var cart = Make(6, -410);
            var ip = (EntityBehaviorInterpolatePosition)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(
                typeof(EntityBehaviorInterpolatePosition));
            ip.entity = cart;
            ip.positionQueue = new Queue<PositionSnapshot>();
            ip.pN = new PositionSnapshot(cart.Pos, 1 / 15f, false);
            ip.targetSpeed = 0.6f;
            ip.PushQueue(new PositionSnapshot(new Vec3d(double.NaN, 1, 1), 1 / 15f, false));
            ip.PopQueue(false);
            trip = NanMotionDiagnostics.EntityTracker.Trip;
            Assert.NotNull(trip);
            Assert.Equal(NanMotionDiagnostics.TraceInterpolationPop.Site, trip.Site);
            Assert.Equal(-410, trip.EntityId);
            Assert.Contains("the interpolation's state at the start: dtAccum", trip.Detail);
            Assert.Contains("the interpolation's state at the end:   dtAccum", trip.Detail);
            Assert.Contains("pN (NaN, 1.000000, 1.000000)", trip.Detail);
            KeepReport();

            // 4. An entity already NaN when a traced call begins: made by code that is not traced,
            // since the last clean sweep.
            NanMotionDiagnostics.Bind(World.Api, watched, thread);
            var hen = Make(7, -411);
            Assert.False(NanMotionDiagnostics.Sweep([hen]));
            hen.Pos.Y = double.NaN;
            new QuietBehavior(hen).OnGameTick(0.05f);
            trip = NanMotionDiagnostics.EntityTracker.Trip;
            Assert.NotNull(trip);
            Assert.Equal(SeraphHorizons.Mod.NanMotion.Core.EntityTripKind.Arrived, trip.Kind);
            Assert.True(trip.LastCleanSweepMs >= 0);
            KeepReport();

            // 5. The sweep finds one that nothing traced saw; the watched player's own NaN is its
            // tracker's, never the sweep's.
            NanMotionDiagnostics.Bind(World.Api, watched, thread);
            var fish = Make(8, -412);
            Assert.False(NanMotionDiagnostics.Sweep([fish, watched]));
            fish.Pos.Motion.X = double.NaN;
            watched.Pos.Motion.Y = double.NaN;
            Assert.True(NanMotionDiagnostics.Sweep([watched, fish]));
            trip = NanMotionDiagnostics.EntityTracker.Trip;
            Assert.NotNull(trip);
            Assert.Equal(SeraphHorizons.Mod.NanMotion.Core.EntityTripKind.Sweep, trip.Kind);
            Assert.Equal(-412, trip.EntityId);
            Assert.Contains("Found by the sweep of loaded entities", trip.Detail);
            KeepReport();
        }
        finally
        {
            NanMotionDiagnostics.Unbind();
            harmony.UnpatchAll(NanMotionTestHarmonyId);
            foreach (var p in reports.Distinct())
                File.Delete(p);
        }
    }
}
