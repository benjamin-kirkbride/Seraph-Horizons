using System.Runtime.CompilerServices;
using SeraphHorizons.Mod.Eidolon.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.Eidolon;

/// <summary>
/// The guard order (#680; README "Eidolon", guarding): the command tool's <c>guard</c> mode marks a
/// block and the eidolon holds the point on top of it (arguments <c>x</c>, <c>y</c>, <c>z</c>),
/// standing there in its guard stance (<c>guard-idle</c>). Once a second it looks about its point: a
/// hostile creature (<see cref="EidolonHostiles"/>) within <see cref="EidolonConfig.GuardRadius"/> of
/// it is handed to self-defence (<see cref="AiTaskEidolonDefend.Engage"/>, leashed to the point), which
/// cancels this order for the fight; the order starts again after and walks back to the point. Never
/// done; guarding drains no oil.
/// </summary>
public sealed class GuardOrder(Vec3d point) : IEidolonOrder
{
    public const string OrderCode = "guard";
    public const string IdleAnimation = "guard-idle";
    private const double RetrySeconds = 4;

    private EidolonNavigator? _nav;
    private double _retryAt;
    private double _scanAt;
    private bool _idle;

    public string Code => OrderCode;

    /// <summary>The point it guards.</summary>
    public Vec3d Point => point;

    /// <summary>Creatures it has handed to self-defence (for the scenarios).</summary>
    public int Engaged { get; private set; }

    public static ITreeAttribute Args(Vec3d point)
    {
        var tree = new TreeAttribute();
        tree.SetDouble("x", point.X);
        tree.SetDouble("y", point.Y);
        tree.SetDouble("z", point.Z);
        return tree;
    }

    /// <summary>The point on top of a marked block, in the middle of it.</summary>
    public static Vec3d PointOn(BlockPos block) => new(block.X + 0.5, block.Y + 1, block.Z + 0.5);

    public static GuardOrder From(ITreeAttribute args) =>
        new(new Vec3d(args.GetDouble("x"), args.GetDouble("y"), args.GetDouble("z")));

    public void Start(EntityLaborEidolon eidolon)
    {
        _nav ??= new EidolonNavigator(eidolon);
        _retryAt = 0;
        _scanAt = 0;
        eidolon.TaskAi?.PathTraverser?.Stop();
    }

    public bool Continue(EntityLaborEidolon eidolon, float dt)
    {
        double now = eidolon.World.ElapsedMilliseconds / 1000.0;
        if (now >= _scanAt)
        {
            _scanAt = now + EidolonGuard.ScanSeconds;
            if (Scan(eidolon))
            {
                Idle(eidolon, false);
                return true;
            }
        }
        if (_nav == null)
            return true;
        if (EidolonGuard.AtHome(eidolon.Pos.X - point.X, eidolon.Pos.Y - point.Y, eidolon.Pos.Z - point.Z))
        {
            if (_nav.Active)
                _nav.Stop();
            Idle(eidolon, true);
            eidolon.Orders?.SetStatus(null);
            return true;
        }
        Idle(eidolon, false);
        if (_nav.Active || now < _retryAt)
            return true;
        _retryAt = now + RetrySeconds;
        bool going = _nav.GoTo(point, false, () => { }, () => { });
        eidolon.Orders?.SetStatus(going ? null : "seraphhorizons:eidolon-status-nopath");
        return true;
    }

    public void Stop(EntityLaborEidolon eidolon, bool cancelled)
    {
        _nav?.Stop();
        Idle(eidolon, false);
    }

    /// <summary>Hands the nearest hostile creature within its radius of the point to self-defence;
    /// true when it did.</summary>
    private bool Scan(EntityLaborEidolon eidolon)
    {
        var config = EidolonSystem.Of(eidolon.Api)?.Config ?? EidolonConfig.Defaults;
        if (eidolon.TaskAi?.TaskManager.GetTask<AiTaskEidolonDefend>() is not { } defend)
            return false;
        double radius = config.GuardRadius;
        var hostility = EidolonHostiles.WorldHostility(eidolon.World);
        var found = eidolon.World.GetEntitiesAround(point, (float)radius + 1, (float)radius + 1,
            e => e is EntityAgent && e.Alive && e.Pos.Dimension == eidolon.Pos.Dimension
                 && EidolonGuard.Watches(e.Pos.DistanceTo(point), radius) && EidolonHostiles.IsHostile(e, hostility));
        int pick = EidolonGuard.Choose(found.Select(e => e.Pos.DistanceTo(eidolon.Pos.XYZ)).ToList());
        if (pick < 0 || !defend.Engage((EntityAgent)found[pick], point, radius + EidolonGuard.ChaseSlack))
            return false;
        Engaged++;
        return true;
    }

    private void Idle(EntityLaborEidolon eidolon, bool on)
    {
        if (_idle == on)
            return;
        _idle = on;
        if (on)
            eidolon.AnimManager.StartAnimation(IdleAnimation);
        else
            eidolon.AnimManager.StopAnimation(IdleAnimation);
    }
}

/// <summary>
/// The game side of <see cref="EidolonHostility"/>: reads a creature's attack tasks from its entity
/// type's JSON (the <c>taskai</c> behaviour's <c>aitasks</c>, its variant's values; cached per type),
/// its emotion states, its generation and owner from its attributes, and the world's
/// <c>creatureHostility</c>. Reading the type rather than the running tasks keeps the answer the
/// type's however its AI stands now.
/// </summary>
public static class EidolonHostiles
{
    private static readonly ConditionalWeakTable<EntityProperties, CreatureTask[]> Tasks = new();

    public static CreatureHostility WorldHostility(IWorldAccessor world) =>
        world.Config.GetString("creatureHostility") switch
        {
            "passive" => CreatureHostility.Passive,
            "off" => CreatureHostility.Off,
            _ => CreatureHostility.Aggressive,
        };

    public static bool IsHostile(Entity entity, CreatureHostility hostility)
    {
        // Never a player, an eidolon or a person (traders and villagers: angry, by their tasks, once
        // a player has hit one, but not the guard's to kill).
        if (entity is not EntityAgent || entity is EntityPlayer or EntityLaborEidolon or EntityDressedHumanoid || !entity.Alive)
            return false;
        var attributes = entity.WatchedAttributes;
        bool owned = attributes.HasAttribute("ownedby") || attributes.HasAttribute("guardedPlayerUid")
                     || attributes.GetLong("guardedEntityId") != 0;
        bool tamedType = entity.Properties.Attributes?.IsTrue("tamed") == true;
        var emotions = entity.GetBehavior<EntityBehaviorEmotionStates>();
        return EidolonHostility.IsHostile(TasksOf(entity.Properties), s => emotions?.IsInEmotionState(s) == true,
            attributes.GetInt("generation"), owned, tamedType, hostility);
    }

    /// <summary>The AI tasks of an entity type, as its JSON gives them.</summary>
    public static CreatureTask[] TasksOf(EntityProperties type) => Tasks.GetValue(type, Read);

    private static CreatureTask[] Read(EntityProperties type)
    {
        var taskAi = type.Server?.BehaviorsAsJsonObj?.FirstOrDefault(b => b["code"].AsString() == "taskai");
        if (taskAi?["aitasks"].AsArray() is not { } tasks)
            return [];
        return tasks.Where(t => t["code"].AsString() != null).Select(t => new CreatureTask(
            t["code"].AsString()!,
            t["entityCodes"].Exists ? Strings(t["entityCodes"]) : ["player"],
            States(t, "whenInEmotionState"),
            States(t, "whenNotInEmotionState"),
            t["minGeneration"].Exists ? t["minGeneration"].AsInt() : null,
            t["maxGeneration"].Exists ? t["maxGeneration"].AsInt() : null)).ToArray();
    }

    /// <summary>A task's emotion states, as the game reads them: the singular key split at <c>|</c>,
    /// or the plural key's array.</summary>
    private static string[]? States(JsonObject task, string key)
    {
        if (task[key + "s"].Exists)
            return Strings(task[key + "s"]);
        return task[key].AsString() is { Length: > 0 } one ? one.Split('|') : null;
    }

    private static string[] Strings(JsonObject array) => (array.AsArray<string>([]) ?? []).OfType<string>().ToArray();
}
