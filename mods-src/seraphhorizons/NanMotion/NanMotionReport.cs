using System.Collections;
using System.Reflection;
using System.Text;
using HarmonyLib;
using SeraphHorizons.Mod.NanMotion.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.NanMotion;

/// <summary>
/// The NaN motion report (#405, <see cref="NanMotionDiagnostics"/>): everything about the watched
/// player and what is around it at the moment of the crash, each part in its own section that
/// cannot stop the others (<see cref="ReportWriter"/>). Every non-finite number is flagged.
/// </summary>
public static class NanMotionReport
{
    /// <summary>How far from the player entities are listed (every non-finite one is, wherever it is).</summary>
    public const double NearbyRange = 16;

    public static string Build(ICoreAPI api, Entity watched, string reason)
    {
        var r = new ReportWriter();
        r.Line("NaN motion report (seraphhorizons, #405). The crash that follows is left as it is; this is what led to it.");
        r.Line("Please attach this file to https://github.com/benjamin-kirkbride/Seraph-Horizons/issues/405");
        r.Line("");
        r.Section("Reason", sb =>
        {
            sb.Append("  ").Append(reason).Append('\n');
            sb.Append($"  local time {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}, game {GameVersion.LongGameVersion}, side {api.Side}\n");
            sb.Append($"  world elapsed {api.World.ElapsedMilliseconds} ms, calendar {api.World.Calendar?.TotalHours:0.000} h");
            if (api is ICoreClientAPI capi)
                sb.Append($", paused {capi.IsGamePaused}");
            sb.Append('\n');
        });
        r.Section("Where the motion first went non-finite", sb =>
        {
            var trip = NanMotionDiagnostics.Tracker.Trip;
            if (trip?.Detail is { } detail)
                Lines(sb, detail.Split('\n'));
            else
                Lines(sb, NanMotionDiagnostics.Tracker.Describe(NanMotionDiagnostics.Describe));
            if (trip == null)
                sb.Append("  Nothing traced saw it go non-finite: whatever made it is not among the traced methods (see the coverage section),\n"
                          + "  or it was non-finite before the player's first traced physics step.\n");
            sb.Append("  Repulsion: ").Append(NanMotionDiagnostics.RepulsionCulprit ?? "no neighbour made the player's push non-finite").Append('\n');
        });
        r.Section("The player", sb => Player(sb, watched));
        r.Section("The player's stats", sb => Stats(sb, watched));
        r.Section("The player's controls", sb => Controls(sb, watched));
        r.Section("The player's physics behavior and its modules", sb => Physics(sb, watched));
        r.Section("Blocks at and under the player", sb => Blocks(sb, api, watched));
        r.Section($"Entities within {NearbyRange:0} blocks, and every entity with a non-finite value", sb => Entities(sb, api, watched));
        r.Section($"The player's last {NanMotionDiagnostics.FrameCount} physics frames (start of each step, oldest first)", sb =>
            Lines(sb, NanMotionDiagnostics.Frames.Describe()));
        r.Section("Harmony patches on the physics path", Patches);
        r.Section("Diagnostics coverage", sb =>
        {
            sb.Append($"  {NanMotionDiagnostics.PatchedCount} patches went in; {NanMotionDiagnostics.PatchFailures.Count} failed\n");
            foreach (var f in NanMotionDiagnostics.PatchFailures)
                sb.Append("    failed: ").Append(f).Append('\n');
            sb.Append($"  {NanMotionDiagnostics.Tracker.Checkpoints} checkpoints looked\n");
        });
        if (r.Failures > 0)
            r.Line($"({r.Failures} sections failed; the rest are complete)");
        return r.ToString();
    }

    private static void Lines(StringBuilder sb, IEnumerable<string> lines)
    {
        foreach (var line in lines)
            sb.Append("  ").Append(line).Append('\n');
    }

    /// <summary>One line about an entity: code, id, class, position, motion, yaw, each non-finite value
    /// flagged.</summary>
    public static string EntityLine(Entity? e, Entity? watched)
    {
        if (e == null)
            return "(no entity)";
        var p = e.Pos;
        var m = p.Motion;
        bool bad = !NanTracker.IsFinite(p.X, p.Y, p.Z) || !NanTracker.IsFinite(m.X, m.Y, m.Z) || !float.IsFinite(p.Yaw);
        return $"{e.Code} #{e.EntityId} ({e.GetType().FullName}){(ReferenceEquals(e, watched) ? " [the player]" : "")} "
               + $"pos ({NanFormat.Num(p.X)}, {NanFormat.Num(p.Y)}, {NanFormat.Num(p.Z)}) dim {p.Dimension} "
               + $"motion ({NanFormat.Num(m.X)}, {NanFormat.Num(m.Y)}, {NanFormat.Num(m.Z)}) yaw {NanFormat.Num(p.Yaw)}"
               + (bad ? NanFormat.Flag : "");
    }

    private static string Pos(EntityPos? p) =>
        p == null
            ? "null"
            : $"xyz {NanFormat.Vec(p.X, p.Y, p.Z)} yaw {NanFormat.Value(p.Yaw)} pitch {NanFormat.Value(p.Pitch)} "
              + $"roll {NanFormat.Value(p.Roll)} headYaw {NanFormat.Value(p.HeadYaw)} headPitch {NanFormat.Value(p.HeadPitch)} dim {p.Dimension}";

    /// <summary>One line, written by <paramref name="line"/>, or why it could not be: a line that
    /// throws loses only itself.</summary>
    private static void Line(StringBuilder sb, Func<string> line)
    {
        string text;
        try
        {
            text = line();
        }
        catch (Exception e)
        {
            text = $"(this line failed: {e.GetType().Name}: {e.Message})";
        }
        sb.Append("  ").Append(text).Append('\n');
    }

    private static void Player(StringBuilder sb, Entity e)
    {
        Line(sb, () => EntityLine(e, e));
        Line(sb, () => "Pos " + Pos(e.Pos));
        Line(sb, () => "Pos.Motion " + NanFormat.Vec(e.Pos.Motion.X, e.Pos.Motion.Y, e.Pos.Motion.Z));
        Line(sb, () => "PreviousServerPos " + Pos(e.PreviousServerPos));
        Line(sb, () => $"PositionBeforeFalling {NanFormat.Vec(e.PositionBeforeFalling.X, e.PositionBeforeFalling.Y, e.PositionBeforeFalling.Z)}");
        Line(sb, () => $"Alive {e.Alive} State {e.State} OnGround {e.OnGround} Swimming {e.Swimming} FeetInLiquid {e.FeetInLiquid} InLava {e.InLava}");
        Line(sb, () => $"Collided {e.Collided} CollidedVertically {e.CollidedVertically} CollidedHorizontally {e.CollidedHorizontally} "
                       + $"ClimbingOnFace {e.ClimbingOnFace?.Code ?? "none"} ClimbingIntoFace {e.ClimbingIntoFace?.Code ?? "none"}");
        Line(sb, () => $"CollisionBox {e.CollisionBox} OriginCollisionBox {e.OriginCollisionBox} SelectionBox {e.SelectionBox}");
        Line(sb, () => $"touchDistance {NanFormat.Value(e.touchDistance)} touchDistanceSq {NanFormat.Value(e.touchDistanceSq)}");
        Line(sb, () => $"ApplyGravity {e.ApplyGravity}");
        Line(sb, () => $"attributes: dmgkb {e.Attributes.GetInt("dmgkb")}, kbdir "
                       + NanFormat.Vec(e.WatchedAttributes.GetDouble("kbdirX"), e.WatchedAttributes.GetDouble("kbdirY"), e.WatchedAttributes.GetDouble("kbdirZ")));
        if (e is EntityAgent agent)
            Line(sb, () => agent.MountedOn is not { } seat
                ? "mounted on: nothing"
                : $"mounted on: seat {seat.GetType().FullName} of {EntityLine(seat.Entity, e)}; supplier {seat.MountSupplier?.GetType().FullName}; "
                  + $"seat position {Pos(seat.SeatPosition)}");
        if (e is EntityPlayer player)
        {
            Line(sb, () => $"walkSpeed {NanFormat.Value(player.walkSpeed)} WalkYaw {NanFormat.Value(player.WalkYaw)} WalkPitch {NanFormat.Value(player.WalkPitch)}");
            Line(sb, () => $"GetWalkSpeedMultiplier(0.3) {NanFormat.Value(player.GetWalkSpeedMultiplier(0.3))}");
            Line(sb, () => player.Player?.WorldData is not { } data
                ? "no player world data"
                : $"game mode {data.CurrentGameMode} FreeMove {data.FreeMove} NoClip {data.NoClip} MoveSpeedMultiplier {NanFormat.Value(data.MoveSpeedMultiplier)}");
        }
    }

    private static void Stats(StringBuilder sb, Entity e)
    {
        if (e.Stats == null)
        {
            sb.Append("  (no stats)\n");
            return;
        }
        foreach (var (category, stats) in e.Stats)
        {
            float blended = stats.GetBlended();
            sb.Append($"  {category} = {NanFormat.Value(blended)} ({stats.BlendType}):");
            foreach (var (code, stat) in stats.ValuesByKey)
                sb.Append($" {code}={NanFormat.Num(stat.Value)}x{NanFormat.Num(stat.Weight)}{(float.IsFinite(stat.Value) && float.IsFinite(stat.Weight) ? "" : "!")}");
            sb.Append('\n');
        }
    }

    private static void Controls(StringBuilder sb, Entity e)
    {
        if (e is not EntityAgent agent)
        {
            sb.Append("  (not an agent)\n");
            return;
        }
        void One(string name, EntityControls? c)
        {
            if (c == null)
            {
                sb.Append($"  {name}: null\n");
                return;
            }
            sb.Append($"  {name}: WalkVector {NanFormat.Vec(c.WalkVector.X, c.WalkVector.Y, c.WalkVector.Z)} "
                      + $"FlyVector {NanFormat.Vec(c.FlyVector.X, c.FlyVector.Y, c.FlyVector.Z)} "
                      + $"MovespeedMultiplier {NanFormat.Value(c.MovespeedMultiplier)} GlideSpeed {NanFormat.Value(c.GlideSpeed)}\n");
            var on = c.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(p => p.PropertyType == typeof(bool) && p.GetIndexParameters().Length == 0)
                .Select(p =>
                {
                    try
                    {
                        return (bool)p.GetValue(c)! ? p.Name : null;
                    }
                    catch
                    {
                        return null;
                    }
                })
                .Where(n => n != null);
            sb.Append($"    set: {string.Join(", ", on)}\n");
        }
        One("Controls", agent.Controls);
        One("ServerControls", agent.ServerControls);
    }

    private static void Physics(StringBuilder sb, Entity e)
    {
        var behaviors = e.SidedProperties?.Behaviors;
        if (behaviors == null)
        {
            sb.Append("  (no behaviors)\n");
            return;
        }
        sb.Append($"  behaviors: {string.Join(", ", behaviors.Select(b => b.GetType().Name))}\n");
        foreach (var physics in behaviors.OfType<PhysicsBehaviorBase>())
        {
            sb.Append($"  {physics.GetType().FullName}:\n");
            Fields(sb, physics, typeof(EntityBehavior), "    ");
            if (physics is not EntityBehaviorControlledPhysics)
                continue;
            foreach (var listName in new[] { "physicsModules", "customModules" })
            {
                if (AccessTools.Field(typeof(EntityBehaviorControlledPhysics), listName)?.GetValue(physics) is not IEnumerable modules)
                    continue;
                foreach (var module in modules)
                {
                    if (module == null)
                        continue;
                    sb.Append($"    {listName}: {module.GetType().FullName} [{NanMotionDiagnostics.ModOf(module.GetType().Assembly)}]\n");
                    Fields(sb, module, typeof(object), "      ");
                }
            }
            if (AccessTools.Field(typeof(EntityBehaviorControlledPhysics), "traversedBlocks")?.GetValue(physics) is IEnumerable<Block> traversed)
                sb.Append($"    traversed blocks (their OnEntityInside runs in AfterPhysicsTick): {string.Join(", ", traversed.Select(b => b?.Code?.ToString() ?? "null"))}\n");
        }
    }

    /// <summary>The instance fields of <paramref name="o"/> up to (not including)
    /// <paramref name="stop"/>, those of simple types (numbers, flags, vectors, positions).</summary>
    private static void Fields(StringBuilder sb, object o, Type stop, string indent)
    {
        var parts = new List<string>();
        for (var t = o.GetType(); t != null && t != stop && t != typeof(object); t = t.BaseType)
        {
            foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                object? v;
                try
                {
                    v = f.GetValue(o);
                }
                catch
                {
                    continue;
                }
                string? text = v switch
                {
                    double d => NanFormat.Value(d),
                    float fl => NanFormat.Value(fl),
                    int or long or bool or string or Enum => v.ToString(),
                    Vec3d vd => NanFormat.Vec(vd.X, vd.Y, vd.Z),
                    Vec3f vf => NanFormat.Vec(vf.X, vf.Y, vf.Z),
                    FastVec3f fv => NanFormat.Vec(fv.X, fv.Y, fv.Z),
                    EntityPos ep => Pos(ep) + " motion " + NanFormat.Vec(ep.Motion.X, ep.Motion.Y, ep.Motion.Z),
                    _ => null,
                };
                if (text != null)
                    parts.Add($"{f.Name}={text}");
            }
        }
        sb.Append(indent).Append(parts.Count == 0 ? "(no simple fields)" : string.Join("; ", parts)).Append('\n');
    }

    private static void Blocks(StringBuilder sb, ICoreAPI api, Entity e)
    {
        var p = e.Pos;
        if (!NanTracker.IsFinite(p.X, p.Y, p.Z))
        {
            sb.Append("  (position not finite)\n");
            return;
        }
        var ba = api.World.BlockAccessor;
        int x = (int)Math.Floor(p.X), z = (int)Math.Floor(p.Z);
        int feet = (int)Math.Floor(p.InternalY);
        foreach (var (label, dy) in new[] { ("head", 1), ("feet", 0), ("under", -1), ("under-feet test (y - 0.05)", int.MinValue) })
        {
            int y = dy == int.MinValue ? (int)(p.InternalY - 0.05) : feet + dy;
            var pos = new BlockPos(x, y, z, 0);
            sb.Append($"  {label} ({x}, {y}, {z}):");
            foreach (var (layerName, layer) in new[] { ("solid", BlockLayersAccess.Solid), ("fluid", BlockLayersAccess.Fluid) })
            {
                var b = ba.GetBlockRaw(x, y, z, layer);
                sb.Append($" {layerName} {b?.Code} #{b?.Id} ({b?.GetType().Name})");
                if (b != null)
                    sb.Append($" walkSpeedMul {NanFormat.Value(b.WalkSpeedMultiplier)} dragMul {NanFormat.Value(b.DragMultiplier)} liquidLevel {b.LiquidLevel};");
            }
            if (ba.GetBlockEntity(pos) is { } be)
                sb.Append($" block entity {be.GetType().FullName}");
            sb.Append('\n');
        }
    }

    private static IEnumerable<Entity> Loaded(ICoreAPI api) => api.World switch
    {
        IClientWorldAccessor c => c.LoadedEntities.Values,
        IServerWorldAccessor s => s.LoadedEntities.Values,
        _ => [],
    };

    private static void Entities(StringBuilder sb, ICoreAPI api, Entity watched)
    {
        var p = watched.Pos;
        int listed = 0, total = 0;
        foreach (var e in Loaded(api).ToList())
        {
            total++;
            if (ReferenceEquals(e, watched))
                continue;
            bool nonFinite = !NanTracker.IsFinite(e.Pos.X, e.Pos.Y, e.Pos.Z) || !NanTracker.IsFinite(e.Pos.Motion.X, e.Pos.Motion.Y, e.Pos.Motion.Z)
                             || !float.IsFinite(e.Pos.Yaw);
            double dist = e.Pos.Dimension == p.Dimension ? e.Pos.DistanceTo(p) : double.PositiveInfinity;
            if (!nonFinite && !(dist <= NearbyRange))
                continue;
            listed++;
            try
            {
                sb.Append("  ").Append(EntityLine(e, watched)).Append($" dist {NanFormat.Num(dist)}\n");
                sb.Append($"    alive {e.Alive} state {e.State} onGround {e.OnGround} touchDistance {NanFormat.Value(e.touchDistance)} "
                          + $"selectionBox {e.SelectionBox} repulse {e.BHRepulseAgents?.GetType().Name ?? "none"}");
                if (e.BHRepulseAgents is EntityBehaviorRepulseAgents r)
                    sb.Append($" ownPosRepulse {NanFormat.Vec(r.ownPosRepulseX, r.ownPosRepulseY, r.ownPosRepulseZ)} mySize {NanFormat.Value(r.mySize)}");
                if (e is EntityAgent { MountedOn: { } seat })
                    sb.Append($" mounted on {seat.Entity?.Code} #{seat.Entity?.EntityId}");
                sb.Append($" behaviors {string.Join(",", e.SidedProperties?.Behaviors.Select(b => b.GetType().Name) ?? [])}\n");
            }
            catch (Exception ex)
            {
                sb.Append($"\n    (describing it failed: {ex.Message})\n");
            }
        }
        sb.Append($"  {listed} listed of {total} loaded\n");
    }

    /// <summary>The methods on the physics path whose patches matter, and who patches them.</summary>
    public static IEnumerable<(string Label, MethodBase? Method)> PatchedMethods()
    {
        yield return ("EntityBehaviorPlayerPhysics.SimPhysics", NanMotionDiagnostics.SimPhysicsMethod);
        foreach (var f in NanMotionDiagnostics.FixedTargets().Take(5))
            yield return (f.Label, f.Method);
        yield return ("EntityBehaviorRepulseAgents.OnGameTick", AccessTools.DeclaredMethod(typeof(EntityBehaviorRepulseAgents), nameof(EntityBehavior.OnGameTick)));
        yield return ("EntityBehaviorRepulseAgents.WalkEntity", NanMotionDiagnostics.WalkEntityMethod);
        yield return ("EntityBehaviorEllipsoidalRepulseAgents.Repulse", AccessTools.DeclaredMethod(typeof(EntityBehaviorEllipsoidalRepulseAgents), "Repulse"));
        yield return ("EntityAgent.GetWalkSpeedMultiplier", AccessTools.DeclaredMethod(typeof(EntityAgent), nameof(EntityAgent.GetWalkSpeedMultiplier)));
        yield return ("EntityPlayer.GetWalkSpeedMultiplier", AccessTools.DeclaredMethod(typeof(EntityPlayer), nameof(EntityPlayer.GetWalkSpeedMultiplier)));
        yield return ("EntityControls.CalcMovementVectors", AccessTools.DeclaredMethod(typeof(EntityControls), nameof(EntityControls.CalcMovementVectors)));
        yield return ("PModuleInLiquid.HandleSwimming", AccessTools.DeclaredMethod(typeof(PModuleInLiquid), nameof(PModuleInLiquid.HandleSwimming)));
        yield return ("PModulePlayerInLiquid.HandleSwimming", AccessTools.DeclaredMethod(typeof(PModulePlayerInLiquid), nameof(PModulePlayerInLiquid.HandleSwimming)));
        foreach (var module in new[] { typeof(PModuleWind), typeof(PModuleOnGround), typeof(PModuleInLiquid), typeof(PModuleInAir),
                     typeof(PModuleGravity), typeof(PModuleMotionDrag), typeof(PModuleKnockback) })
            yield return ($"{module.Name}.DoApply", AccessTools.DeclaredMethod(module, nameof(PModule.DoApply)));
    }

    private static void Patches(StringBuilder sb)
    {
        foreach (var (label, method) in PatchedMethods())
        {
            if (method == null)
            {
                sb.Append($"  {label}: not found\n");
                continue;
            }
            var info = Harmony.GetPatchInfo(method);
            if (info == null)
            {
                sb.Append($"  {label}: not patched\n");
                continue;
            }
            string Owners(IEnumerable<HarmonyLib.Patch> patches) =>
                string.Join(", ", patches.Select(p => $"{p.owner}({p.PatchMethod.DeclaringType?.Name}.{p.PatchMethod.Name}, priority {p.priority})"));
            sb.Append($"  {label}:\n");
            if (info.Prefixes.Count > 0) sb.Append($"    prefixes: {Owners(info.Prefixes)}\n");
            if (info.Postfixes.Count > 0) sb.Append($"    postfixes: {Owners(info.Postfixes)}\n");
            if (info.Transpilers.Count > 0) sb.Append($"    transpilers: {Owners(info.Transpilers)}\n");
            if (info.Finalizers.Count > 0) sb.Append($"    finalizers: {Owners(info.Finalizers)}\n");
        }
    }
}
