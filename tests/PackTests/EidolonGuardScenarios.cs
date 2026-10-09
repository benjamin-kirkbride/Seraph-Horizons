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
/// a bred wolf alone, and stands at its point again after. The creatures' own AI is taken away, so
/// they stand where they are put.
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

    /// <summary>A creature of <paramref name="code"/> standing at <paramref name="at"/>, its AI taken away.</summary>
    private async Task<EntityAgent> Creature(string code, Vec3d at)
    {
        var creature = (EntityAgent)W.ClassRegistry.CreateEntity(Type(code));
        creature.Pos.SetPos(at);
        W.SpawnEntity(creature);
        await World.Ticks(2);
        if (creature.GetBehavior<EntityBehaviorTaskAI>() is { } ai)
            creature.SidedProperties.Behaviors.Remove(ai);
        return creature;
    }

    private static float Health(Entity e) => e.GetBehavior<EntityBehaviorHealth>()!.Health;

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

    [AtlasScenario(TimeoutMs = 300_000)]
    public async Task Guarding_it_kills_a_drifter_near_its_point_and_leaves_one_far_and_a_bred_wolf()
    {
        var at = await Floor(0, 0, reach: 28);
        var p = await World.JoinPlayer("eidolonwarden");
        var player = (IServerPlayer)p.Player;
        player.WorldData.CurrentGameMode = EnumGameMode.Creative;
        await p.TeleportTo(at.AddCopy(4, 0, -6));
        var e = (await SpawnEidolon(at.AddCopy(-3, 0, 0), player));

        // The owner binds the tool, chooses guard and marks the floor block under the point.
        var item = W.GetItem(new AssetLocation(EidolonCommanderSystem.CommanderCode))!;
        var slot = player.InventoryManager.ActiveHotbarSlot;
        slot.Itemstack = new ItemStack(item);
        Click(player, slot, entity: e);
        slot.Itemstack.Collectible.SetToolMode(slot, player, null!, EidolonCommandModes.IndexOf(GuardOrder.OrderCode));
        Click(player, slot, block: at.DownCopy());
        Assert.Equal(GuardOrder.OrderCode, e.Orders!.OrderCode);
        var point = new Vec3d(at.X + 0.5, at.Y, at.Z + 0.5);
        await World.Until(() => EidolonGuard.AtHome(e.Pos.X - point.X, e.Pos.Y - point.Y, e.Pos.Z - point.Z), 1200);
        await World.Until(() => e.AnimManager.IsAnimationActive(GuardOrder.IdleAnimation), 100);

        // A drifter 25 blocks off, a bred wolf 6 and a drifter 10, its AI gone, so they stand.
        var far = await Creature("game:drifter-normal", point.AddCopy(25, 0, 0));
        var wolf = await Creature("game:wolf-eurasian-adult-male", point.AddCopy(0, 0, 6));
        wolf.WatchedAttributes.SetInt("generation", 1);
        var near = await Creature("game:drifter-normal", point.AddCopy(-10, 0, 0));
        float farHealth = Health(far), wolfHealth = Health(wolf);
        var defend = e.TaskAi!.TaskManager.GetTask<AiTaskEidolonDefend>()!;

        await World.Until(() => defend.Attacker == near, 200);
        await World.Until(() => !near.Alive, 1600);
        Log($"near drifter dead after {defend.BlowsLanded} blows; " + EidolonCommands.Describe(e));
        Assert.Equal(GuardOrder.OrderCode, e.Orders.OrderCode);

        // Back to its point; the far drifter and the wolf untouched and never gone for.
        await World.Until(() => EidolonGuard.AtHome(e.Pos.X - point.X, e.Pos.Y - point.Y, e.Pos.Z - point.Z), 1600);
        await World.Ticks(100);
        Assert.True(far.Alive);
        Assert.True(wolf.Alive);
        Assert.Equal(farHealth, Health(far));
        Assert.Equal(wolfHealth, Health(wolf));
        Assert.Null(defend.Attacker);
        Assert.Equal(1, (e.Orders.Current as GuardOrder)!.Engaged);
        Assert.True(e.CanWork);
        Assert.True(EidolonGuard.AtHome(e.Pos.X - point.X, e.Pos.Y - point.Y, e.Pos.Z - point.Z), EidolonCommands.Describe(e));

        // Unbred, the same wolf is hostile and it goes for it.
        wolf.WatchedAttributes.SetInt("generation", 0);
        await World.Until(() => defend.Attacker == wolf, 200);
        foreach (var c in new Entity[] { far, wolf, e })
            c.Die(EnumDespawnReason.Removed);
    }

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
