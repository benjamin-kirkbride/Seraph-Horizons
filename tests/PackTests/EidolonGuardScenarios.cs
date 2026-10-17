using Atlas.XUnit;
using SeraphHorizons.Mod.Eidolon;
using SeraphHorizons.Mod.Eidolon.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Xunit.Abstractions;

namespace SeraphHorizons.PackTests;

/// <summary>
/// mods-src/seraphhorizons/Eidolon (#680): guarding a point with the pack's default settings. The
/// game's own creature types read as hostile or not (the archives' creatures and wolves are, grazers
/// are not until hurt, a bred or owned one never), and an eidolon given the guard order with the
/// command tool goes for a drifter 10 blocks from its point and kills it, leaves one 25 blocks off and
/// a bred wolf alone, and stands at its post about its point again after. Three eidolons guarding one
/// point each take a post of their own, apart. The creatures' own AI is taken away, so they stand
/// where they are put.
/// </summary>
[AtlasWorld]
public class EidolonGuardScenarios(ITestOutputHelper output) : AtlasScenarioBase
{
    private IWorldAccessor W => World.Api.World;
    private ICoreServerAPI Sapi => (ICoreServerAPI)World.Api;
    private EidolonSystem Mod => EidolonSystem.Of(World.Api)!;

    private void Log(string what)
    {
        W.Logger.Notification("[eidolon-guard-test] " + what);
        output.WriteLine(what);
    }

    /// <summary>A clear granite floor 40 above spawn at (<paramref name="dx"/>, <paramref name="dz"/>),
    /// <paramref name="reach"/> each way, with 8 blocks of air over it, its chunks loaded.</summary>
    private async Task<BlockPos> Floor(int dx, int dz, int reach)
    {
        var origin = World.Spawn.AddCopy(dx, 40, dz);
        int size = Vintagestory.API.Config.GlobalConstants.ChunkSize;
        for (int cx = (origin.X - reach) / size; cx <= (origin.X + reach) / size; cx++)
            for (int cz = (origin.Z - reach) / size; cz <= (origin.Z + reach) / size; cz++)
                Sapi.WorldManager.LoadChunkColumnPriority(cx, cz);
        await World.Until(() => W.BlockAccessor.GetChunkAtBlockPos(origin.AddCopy(-reach, 0, -reach)) != null
                                && W.BlockAccessor.GetChunkAtBlockPos(origin.AddCopy(reach, 0, reach)) != null, 1200);
        int granite = W.GetBlock(new AssetLocation("game:rock-granite"))!.Id;
        for (int x = -reach; x <= reach; x++)
        for (int z = -reach; z <= reach; z++)
        {
            W.BlockAccessor.SetBlock(granite, origin.AddCopy(x, -1, z));
            for (int y = 0; y <= 8; y++)
                W.BlockAccessor.SetBlock(0, origin.AddCopy(x, y, z));
        }
        await World.Ticks(5);
        return origin;
    }

    private EntityProperties Type(string code) =>
        W.GetEntityType(new AssetLocation(code)) ?? throw new InvalidOperationException("no entity type " + code);

    /// <summary>A creature of <paramref name="code"/> standing at <paramref name="at"/>, its AI taken away.
    /// Its <paramref name="generation"/> is set before it is in the world: a wolf set bred only after
    /// spawning was unbred, so hostile, for the ticks between, and a guard scanning then went for it and
    /// killed it (#812). Its tasks are stopped and its walk with them before the AI goes, and it is put
    /// back where it was placed: a task started in its first ticks (the wolf's, from the eidolon) left
    /// it walking with nothing to stop it, and a wolf placed 6 blocks from the point walked out of the
    /// guard's radius before it was set unbred (#894).</summary>
    private async Task<EntityAgent> Creature(string code, Vec3d at, int generation = 0)
    {
        var creature = (EntityAgent)W.ClassRegistry.CreateEntity(Type(code));
        creature.Pos.SetPos(at);
        if (generation != 0)
            creature.WatchedAttributes.SetInt("generation", generation);
        W.SpawnEntity(creature);
        await World.Ticks(2);
        if (creature.GetBehavior<EntityBehaviorTaskAI>() is { } ai)
        {
            ai.TaskManager.StopTasks();
            ai.PathTraverser?.Stop();
            creature.SidedProperties.Behaviors.Remove(ai);
        }
        creature.Controls.StopAllMovement();
        creature.Pos.Motion.Set(0, 0, 0);
        creature.TeleportTo(at);
        await World.Until(() => creature.Pos.XYZ.HorizontalSquareDistanceTo(at) < 0.1 * 0.1, 100);
        return creature;
    }

    private static float Health(Entity e) => e.GetBehavior<EntityBehaviorHealth>()!.Health;

    /// <summary>Every blow each watched creature took, as it took it (#812).</summary>
    private readonly Dictionary<long, List<string>> _hurts = [];

    /// <summary>Records every blow <paramref name="e"/> takes from now on, for <see cref="State"/>.</summary>
    private void Watch(Entity e)
    {
        var hurts = _hurts[e.EntityId] = [];
        e.GetBehavior<EntityBehaviorHealth>()!.onDamaged += (damage, source) =>
        {
            hurts.Add($"{damage:0.##} at {W.ElapsedMilliseconds} ms, {Describe(source)}, standing at {e.Pos.XYZ}");
            return damage;
        };
    }

    private static string Describe(DamageSource? source) => source == null
        ? "no damage source"
        : $"{source.Type} from {source.Source} (by {source.SourceEntity?.Code}, cause {source.GetCauseEntity()?.Code}"
          + $" {source.GetCauseEntity()?.EntityId})";

    /// <summary>Whatever says why <paramref name="e"/> is dead or hurt: alive, health, where it stands
    /// and on what, how it left the world, and every blow it took since <see cref="Watch"/>.</summary>
    private string State(Entity e, string name)
    {
        var health = e.GetBehavior<EntityBehaviorHealth>();
        var feet = e.Pos.AsBlockPos;
        return $"{name} ({e.Code}, id {e.EntityId}): alive {e.Alive}, state {e.State}, health {health?.Health}/{health?.MaxHealth}, "
               + $"at {e.Pos.XYZ} on {W.BlockAccessor.GetBlock(feet.DownCopy()).Code} in {W.BlockAccessor.GetBlock(feet).Code}, "
               + $"loaded {W.GetEntityById(e.EntityId) != null}, despawn {e.DespawnReason?.Reason.ToString() ?? "none"}"
               + (e.DespawnReason is { } gone ? $" by {Describe(gone.DamageSourceForDeath)}" : "")
               + $"; blows: [{string.Join("; ", _hurts.GetValueOrDefault(e.EntityId) ?? [])}]";
    }

    private static bool Hostile(EntityProperties type, params string[] states) =>
        EidolonHostility.IsHostile(EidolonHostiles.TasksOf(type), states.Contains, 0, false, false, CreatureHostility.Aggressive);

    /// <summary>The first of the game's entity types whose code starts <paramref name="prefix"/> (an adult
    /// where there are ages).</summary>
    private EntityProperties Some(string prefix) =>
        W.EntityTypes.Where(t => t.Code.Domain == "game" && t.Code.Path.StartsWith(prefix, StringComparison.Ordinal))
            .OrderBy(t => t.Code.Path.Contains("baby") ? 1 : 0).ThenBy(t => t.Code.Path, StringComparer.Ordinal)
            .FirstOrDefault() ?? throw new InvalidOperationException("no entity type " + prefix + "*");

    [AtlasScenario]
    public void The_game_creatures_read_hostile_or_not_from_their_types()
    {
        foreach (var prefix in new[] { "drifter-normal", "drifter-deep", "bowtorn-", "shiver-", "locust-bronze", "wolf-eurasian-adult", "bear-", "hyena-" })
        {
            var type = Some(prefix);
            Assert.True(Hostile(type), type.Code + " should be hostile");
        }
        foreach (var prefix in new[] { "sheep-", "pig-eurasian-adult", "chicken-", "fox-", "deer-", "mechhelper", "locust-bronze-hacked", "tameddeer-" })
        {
            var type = Some(prefix);
            Assert.False(Hostile(type), type.Code + " should not be hostile");
        }
        // A trader fights back when hit (aggressiveondamage), but a person is never the guard's.
        var trader = Some("trader");
        Assert.True(Hostile(trader, "aggressiveondamage"));
        Assert.False(EidolonHostiles.IsHostile(W.ClassRegistry.CreateEntity(trader), CreatureHostility.Aggressive));
        var sheep = Some("sheep-");
        Assert.True(Hostile(sheep, "aggressiveondamage"), sheep.Code + " is hostile when hurt");
        Assert.Equal(["follow", "stay"], EidolonCommandModes.All.Take(2).Select(m => m.Code));
        Assert.Equal(EidolonMarkKind.Block, EidolonCommandModes.Get(GuardOrder.OrderCode)!.Mark);
        Assert.True(EidolonOrders.Exists(GuardOrder.OrderCode));
    }

    [AtlasScenario(TimeoutMs = 150_000)]
    public async Task Guarding_it_kills_a_drifter_near_its_point_and_leaves_one_far_and_a_bred_wolf()
    {
        var at = await Floor(0, 0, reach: 28);
        var p = await World.JoinRetrying("eidolonwarden");
        var player = (IServerPlayer)p.Player;
        player.WorldData.CurrentGameMode = EnumGameMode.Creative;
        await p.TeleportTo(at.AddCopy(4, 0, -6));
        var e = (await SpawnEidolon(at.AddCopy(-3, 0, 0), player));
        using var clock = VirtualClock.Start(World);

        // The owner binds the tool, chooses guard and marks the floor block under the point.
        var item = W.GetItem(new AssetLocation(EidolonCommanderSystem.CommanderCode))!;
        var slot = player.InventoryManager.ActiveHotbarSlot;
        slot.Itemstack = new ItemStack(item);
        Click(player, slot, entity: e);
        slot.Itemstack.Collectible.SetToolMode(slot, player, null!, EidolonCommandModes.IndexOf(GuardOrder.OrderCode));
        Click(player, slot, block: at.DownCopy());
        Assert.Equal(GuardOrder.OrderCode, e.Orders!.OrderCode);
        var point = new Vec3d(at.X + 0.5, at.Y, at.Z + 0.5);
        await World.Until(() => AtItsPost(e), 1200);
        await World.Until(() => e.AnimManager.IsAnimationActive(GuardOrder.IdleAnimation), 100);

        // A drifter 25 blocks off, a bred wolf 6 and a drifter 10, its AI gone, so they stand.
        var far = await Creature("game:drifter-normal", point.AddCopy(25, 0, 0));
        Watch(far);
        var wolf = await Creature("game:wolf-eurasian-adult-male", point.AddCopy(0, 0, 6), generation: 1);
        Assert.Equal(1, wolf.WatchedAttributes.GetInt("generation"));
        Watch(wolf);
        var near = await Creature("game:drifter-normal", point.AddCopy(-10, 0, 0));
        float farHealth = Health(far), wolfHealth = Health(wolf);
        var defend = e.TaskAi!.TaskManager.GetTask<AiTaskEidolonDefend>()!;

        await World.Until(() => defend.Attacker == near, 200);
        await World.Until(() => !near.Alive, 1600);
        Log($"near drifter dead after {defend.BlowsLanded} blows; " + EidolonCommands.Describe(e));
        Assert.Equal(GuardOrder.OrderCode, e.Orders.OrderCode);

        // Back to its post; the far drifter and the wolf untouched and never gone for.
        await World.Until(() => AtItsPost(e), 1600);
        await World.Ticks(100);
        string farState = State(far, "far drifter"), wolfState = State(wolf, "bred wolf");
        Log(farState);
        Log(wolfState);
        Assert.True(far.Alive, farState);
        Assert.True(wolf.Alive, wolfState);
        Assert.True(Health(far) == farHealth, $"far drifter was at {farHealth}; " + farState);
        Assert.True(Health(wolf) == wolfHealth, $"bred wolf was at {wolfHealth}; " + wolfState);
        Assert.True(defend.Attacker == null, $"still after {defend.Attacker?.Code} {defend.Attacker?.EntityId}; " + EidolonCommands.Describe(e));
        Assert.True((e.Orders.Current as GuardOrder)?.Engaged == 1,
            $"engaged {(e.Orders.Current as GuardOrder)?.Engaged} creatures (only the near drifter expected); " + EidolonCommands.Describe(e));
        Assert.True(e.CanWork, "can't work: " + EidolonCommands.Describe(e));
        Assert.True(AboutItsPoint(e, point), EidolonCommands.Describe(e));
        // Still where it was put, inside the guard's radius, so the next step tests the guard alone.
        Assert.True(Across(wolf.Pos.X - point.X, wolf.Pos.Z - point.Z) < 6.5, wolfState);

        // Unbred, the same wolf is hostile and it goes for it.
        wolf.WatchedAttributes.SetInt("generation", 0);
        await World.Until(() => defend.Attacker == wolf, 200);
        foreach (var c in new Entity[] { far, wolf, e })
            c.Die(EnumDespawnReason.Removed);
    }

    [AtlasScenario(TimeoutMs = 150_000)]
    public async Task Guards_of_one_point_each_stand_at_a_post_of_their_own()
    {
        var at = await Floor(80, 0, reach: 14);
        var p = await World.JoinRetrying("eidolonposts");
        var player = (IServerPlayer)p.Player;
        await p.TeleportTo(at.AddCopy(10, 0, 10));
        var guards = new List<EntityLaborEidolon>();
        foreach (var (dx, dz) in new[] { (-6, -6), (6, -6), (-6, 6) })
            guards.Add(await SpawnEidolon(at.AddCopy(dx, 0, dz), player));
        using var clock = VirtualClock.Start(World);
        var point = GuardOrder.PointOn(at.DownCopy());
        foreach (var g in guards)
            g.Orders!.SetOrder(GuardOrder.OrderCode, GuardOrder.Args(point));

        await World.Until(() => guards.All(g => AtItsPost(g) && g.AnimManager.IsAnimationActive(GuardOrder.IdleAnimation)), 1600);
        var posts = guards.Select(g => ((GuardOrder)g.Orders!.Current!).Post!.Value).ToList();
        Log("posts: " + string.Join("; ", posts) + " | at: " + string.Join("; ", guards.Select(g => g.Pos.XYZ)));
        for (int i = 0; i < guards.Count; i++)
        {
            Assert.True(AboutItsPoint(guards[i], point), EidolonCommands.Describe(guards[i]));
            for (int j = i + 1; j < guards.Count; j++)
            {
                Assert.True(Across(posts[i].X - posts[j].X, posts[i].Z - posts[j].Z) >= EidolonGuard.PostSpacing,
                    $"posts {posts[i]} and {posts[j]}");
                // Standing, never in each other (the box is 1.7 wide).
                Assert.True(Across(guards[i].Pos.X - guards[j].Pos.X, guards[i].Pos.Z - guards[j].Pos.Z) >= 1.7,
                    $"guards at {guards[i].Pos.XYZ} and {guards[j].Pos.XYZ}");
            }
        }
        foreach (var g in guards)
            g.Die(EnumDespawnReason.Removed);
    }

    private static double Across(double dx, double dz) => Math.Sqrt(dx * dx + dz * dz);

    /// <summary>Standing at the post its guard order holds.</summary>
    private static bool AtItsPost(EntityLaborEidolon e) =>
        e.Orders?.Current is GuardOrder { Post: { } post } && EidolonGuard.AtHome(e.Pos.X - post.X, e.Pos.Y - post.Y, e.Pos.Z - post.Z);

    /// <summary>Within a post's reach of its point (it may be off to a new post by now).</summary>
    private static bool AboutItsPoint(EntityLaborEidolon e, Vec3d point) =>
        Across(e.Pos.X - point.X, e.Pos.Z - point.Z) < EidolonGuard.PostRange + EidolonGuard.HomeTolerance
        && Math.Abs(e.Pos.Y - point.Y) < EidolonGuard.HomeTolerance + 2;

    private async Task<EntityLaborEidolon> SpawnEidolon(BlockPos at, IPlayer owner)
    {
        var e = Mod.Spawn(W, new Vec3d(at.X, at.Y, at.Z), 0, owner, activate: true);
        Assert.NotNull(e);
        await World.Until(() => e!.CanWork, 400);
        return e!;
    }

    private static void Click(IPlayer player, ItemSlot slot, Entity? entity = null, BlockPos? block = null)
    {
        var handling = EnumHandHandling.NotHandled;
        slot.Itemstack!.Collectible.OnHeldInteractStart(slot, player.Entity,
            block == null ? null : new BlockSelection { Position = block, Face = BlockFacing.UP, HitPosition = new Vec3d(0.5, 1, 0.5) },
            entity == null ? null : new EntitySelection { Entity = entity, Position = entity.Pos.XYZ.AddCopy(0, 2, 0) },
            true, ref handling);
        Assert.Equal(EnumHandHandling.PreventDefault, handling);
    }
}
