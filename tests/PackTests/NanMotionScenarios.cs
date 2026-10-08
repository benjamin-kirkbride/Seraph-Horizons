using System.Reflection;
using Atlas.XUnit;
using HarmonyLib;
using SeraphHorizons.Mod.NanMotion;
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
        // Mods' overrides too, not only the game's.
        Assert.Contains(targets, t => t.Method.DeclaringType!.Assembly.GetName().Name == "GondolaCableCar");
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
}
