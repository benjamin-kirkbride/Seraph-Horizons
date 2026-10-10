using Atlas.XUnit;
using SeraphHorizons.Mod.Eidolon;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Xunit.Abstractions;

namespace SeraphHorizons.PackTests;

/// <summary>
/// mods-src/seraphhorizons/Eidolon (#675): the command tool with the pack's default settings, used as
/// a player uses it (<c>OnHeldInteractStart</c> on the server: right-click an eidolon to bind, the
/// mode wheel's index, right-click elsewhere to order). Its owner binds it and it follows them over
/// rough ground, steps and a pillar, keeping a few blocks behind; a stranger can neither bind it nor
/// order it with a tool bound to it; and a creature that hurts it is struck until dead while its stay
/// order holds, and a player who hurts it is not; a creature walking steadily away from it is caught and
/// struck on the move until dead. Each scenario builds on a granite floor of its own high over the spawn.
/// </summary>
[AtlasWorld]
public class EidolonCommanderScenarios(ITestOutputHelper output) : AtlasScenarioBase
{
    private IWorldAccessor W => World.Api.World;
    private ICoreServerAPI Sapi => (ICoreServerAPI)World.Api;
    private EidolonSystem Mod => EidolonSystem.Of(World.Api)!;

    private void Log(string what)
    {
        W.Logger.Notification("[eidolon-commander-test] " + what);
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
        int granite = Granite;
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

    private int Granite => W.GetBlock(new AssetLocation("game:rock-granite"))!.Id;

    private async Task<EntityLaborEidolon> Spawn(BlockPos at, IPlayer? owner)
    {
        var e = Mod.Spawn(W, new Vec3d(at.X, at.Y, at.Z), 0, owner, activate: true);
        Assert.NotNull(e);
        await World.Until(() => e!.CanWork, 400);
        return e!;
    }

    /// <summary>The player's active slot holding a fresh command tool.</summary>
    private ItemSlot Tool(IPlayer player)
    {
        var item = W.GetItem(new AssetLocation(EidolonCommanderSystem.CommanderCode));
        Assert.IsType<ItemEidolonCommander>(item);
        var slot = player.InventoryManager.ActiveHotbarSlot;
        slot.Itemstack = new ItemStack(item);
        slot.MarkDirty();
        return slot;
    }

    /// <summary>A right-click with the held tool: on <paramref name="entity"/>, or on
    /// <paramref name="block"/>, or in the air.</summary>
    private static void RightClick(IPlayer player, ItemSlot slot, Entity? entity = null, BlockPos? block = null)
    {
        var handling = EnumHandHandling.NotHandled;
        slot.Itemstack!.Collectible.OnHeldInteractStart(slot, player.Entity,
            block == null ? null : new BlockSelection { Position = block, Face = BlockFacing.UP, HitPosition = new Vec3d(0.5, 1, 0.5) },
            entity == null ? null : new EntitySelection { Entity = entity, Position = entity.Pos.XYZ.AddCopy(0, 2, 0) },
            true, ref handling);
        Assert.Equal(EnumHandHandling.PreventDefault, handling);
    }

    private static void ChooseMode(IPlayer player, ItemSlot slot, string code)
    {
        int index = EidolonCommandModes.IndexOf(code);
        Assert.True(index >= 0, $"no mode {code}");
        slot.Itemstack!.Collectible.SetToolMode(slot, player, null!, index);
        Assert.Equal(code, ItemEidolonCommander.ModeOf(slot.Itemstack!)!.Code);
    }

    private static string State(EntityLaborEidolon e) => EidolonCommands.Describe(e);

    [AtlasScenario]
    public void The_tool_has_its_class_and_the_wheel_follow_then_stay()
    {
        Assert.IsType<ItemEidolonCommander>(W.GetItem(new AssetLocation(EidolonCommanderSystem.CommanderCode)));
        Assert.Equal(["follow", "stay"], EidolonCommandModes.All.Take(2).Select(m => m.Code));
        Assert.True(EidolonOrders.Exists(FollowOrder.OrderCode));
        var type = W.GetEntityType(EidolonSystem.EntityCode)!;
        var tasks = type.Server.BehaviorsAsJsonObj.First(b => b["code"].AsString() == "taskai")["aitasks"].AsArray()!;
        float Priority(string code) => tasks.First(t => t["code"].AsString() == code)["priority"].AsFloat();
        Assert.True(Priority(AiTaskEidolonDefend.Code) > Priority(AiTaskEidolonOrder.Code));
    }

    [AtlasScenario(TimeoutMs = 300_000)]
    public async Task Bound_by_its_owner_it_follows_them_over_rough_ground()
    {
        var at = await Floor(0, 0, reach: 20);
        // Rough ground east of the start: patches a block up, a shelf one higher beyond x + 10 with
        // its own patches (so steps of one and two blocks' climb in all), and a 2 × 2 pillar four
        // high in the way at x + 7.
        int granite = Granite;
        for (int x = 3; x <= 20; x++)
        for (int z = -20; z <= 20; z++)
        {
            int h = (x >= 10 ? 1 : 0) + (((x / 3) + (z + 30) / 3) % 3 == 0 ? 1 : 0);
            for (int y = 0; y < h; y++)
                W.BlockAccessor.SetBlock(granite, at.AddCopy(x, y, z));
        }
        for (int x = 7; x <= 8; x++)
        for (int z = 0; z <= 1; z++)
        for (int y = 0; y < 4; y++)
            W.BlockAccessor.SetBlock(granite, at.AddCopy(x, y, z));
        await World.Ticks(5);

        var p = await World.JoinPlayer("eidolonleader");
        var player = (IServerPlayer)p.Player;
        player.WorldData.CurrentGameMode = EnumGameMode.Creative;
        await p.TeleportTo(at.AddCopy(-3, 0, 1));
        var e = await Spawn(at.AddCopy(-6, 0, 1), player);

        var slot = Tool(player);
        RightClick(player, slot, entity: e);
        Assert.Equal([e.EntityId], ItemEidolonCommander.Bound(slot.Itemstack!));
        ChooseMode(player, slot, "follow");
        RightClick(player, slot);
        Assert.Equal(FollowOrder.OrderCode, e.Orders!.OrderCode);

        var config = Mod.Config;
        double Distance() => e.Pos.DistanceTo(player.Entity.Pos.XYZ);
        // Each leg: the player moves on over the rough ground; it catches up and stands a few blocks off.
        foreach (var (dx, dz) in new[] { (14, 1), (18, -12), (5, -14), (16, 14) })
        {
            var to = at.AddCopy(dx, 0, dz);
            while (W.BlockAccessor.GetBlock(to).Id != 0)
                to.Up();
            await p.TeleportTo(to);
            await World.Until(() => Distance() <= config.FollowDistance + 2.5 && e.TaskAi!.PathTraverser?.Active != true, 2400);
            Log($"leg to {dx},{dz}: distance {Distance():0.0}; " + State(e));
            Assert.Equal(FollowOrder.OrderCode, e.Orders.OrderCode);
            Assert.True(e.Pos.Y >= at.Y - 0.1, State(e));
        }
        Assert.DoesNotContain("no way", e.GetInfoText());

        // Then stay: it holds where it stands while they walk off.
        ChooseMode(player, slot, "stay");
        RightClick(player, slot);
        Assert.Equal(StayOrder.OrderCode, e.Orders.OrderCode);
        var held = e.Pos.XYZ;
        await p.TeleportTo(at.AddCopy(-15, 0, -15));
        await World.Ticks(100);
        Assert.True(e.Pos.XYZ.HorizontalSquareDistanceTo(held) < 1.5 * 1.5, State(e));
        e.Die(EnumDespawnReason.Removed);
    }

    [AtlasScenario(TimeoutMs = 180_000)]
    public async Task Following_it_waits_at_a_gap_it_cannot_cross_and_says_so()
    {
        var at = await Floor(60, 0, reach: 14);
        // A wall across the floor at x + 4 with a doorway one block wide: the player is beyond it.
        int granite = Granite;
        for (int z = -14; z <= 14; z++)
        for (int y = 0; y <= 8; y++)
            if (!(z == 0 && y < 4))
                W.BlockAccessor.SetBlock(granite, at.AddCopy(4, y, z));
        await World.Ticks(5);
        var p = await World.JoinPlayer("eidolonwalker");
        var player = (IServerPlayer)p.Player;
        player.WorldData.CurrentGameMode = EnumGameMode.Creative;
        await p.TeleportTo(at.AddCopy(1, 0, 0));
        var e = await Spawn(at.AddCopy(-4, 0, 0), player);
        var slot = Tool(player);
        RightClick(player, slot, entity: e);
        ChooseMode(player, slot, "follow");
        RightClick(player, slot);
        await p.TeleportTo(at.AddCopy(12, 0, 0));
        await World.Until(() => e.GetInfoText().Contains("no way"), 600);
        Log("at the narrow door: " + State(e) + " / " + e.GetInfoText());
        Assert.True(e.Pos.X < at.X + 4, State(e));
        Assert.Equal(FollowOrder.OrderCode, e.Orders!.OrderCode);
        e.Die(EnumDespawnReason.Removed);
    }

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task A_stranger_can_neither_bind_it_nor_order_it()
    {
        var at = await Floor(120, 0, reach: 8);
        var ownerP = await World.JoinPlayer("eidolonmaster");
        var strangerP = await World.JoinPlayer("eidolonthief");
        await ownerP.TeleportTo(at.AddCopy(-5, 0, -5));
        await strangerP.TeleportTo(at.AddCopy(-5, 0, 5));
        var owner = (IServerPlayer)ownerP.Player;
        var stranger = (IServerPlayer)strangerP.Player;
        var e = await Spawn(at, owner);
        e.Orders!.SetOrder(StayOrder.OrderCode, StayOrder.Args(e.Pos.XYZ));

        // Their own tool: right-clicking the eidolon binds nothing.
        var theirs = Tool(stranger);
        RightClick(stranger, theirs, entity: e);
        Assert.Empty(ItemEidolonCommander.Bound(theirs.Itemstack!));
        ChooseMode(stranger, theirs, "follow");
        RightClick(stranger, theirs);
        Assert.Equal(StayOrder.OrderCode, e.Orders.OrderCode);

        // The owner's tool, bound, in the stranger's hands: the eidolon refuses its order.
        var ownerSlot = Tool(owner);
        RightClick(owner, ownerSlot, entity: e);
        Assert.Single(ItemEidolonCommander.Bound(ownerSlot.Itemstack!));
        theirs.Itemstack = ownerSlot.Itemstack!.Clone();
        ChooseMode(stranger, theirs, "follow");
        var tool = (ItemEidolonCommander)theirs.Itemstack.Collectible;
        Assert.Single(ItemEidolonCommander.BoundNear(W, theirs.Itemstack, stranger.Entity.Pos.XYZ, Mod.Config.CommandRange));
        Assert.Equal(0, tool.Use(stranger, theirs, null));
        Assert.Equal(StayOrder.OrderCode, e.Orders.OrderCode);

        // The owner's own use of it works.
        ChooseMode(owner, ownerSlot, "follow");
        Assert.Equal(1, ((ItemEidolonCommander)ownerSlot.Itemstack!.Collectible).Use(owner, ownerSlot, null));
        Assert.Equal(FollowOrder.OrderCode, e.Orders.OrderCode);
        e.Die(EnumDespawnReason.Removed);
    }

    [AtlasScenario(TimeoutMs = 180_000)]
    public async Task It_strikes_back_at_a_creature_that_hurts_it_never_a_player_and_holds_its_order()
    {
        var at = await Floor(180, 0, reach: 12);
        var p = await World.JoinPlayer("eidolonbystander");
        var player = (IServerPlayer)p.Player;
        player.WorldData.CurrentGameMode = EnumGameMode.Creative;
        await p.TeleportTo(at.AddCopy(-10, 0, -10));
        var e = await Spawn(at, player);
        e.Orders!.SetOrder(StayOrder.OrderCode, StayOrder.Args(e.Pos.XYZ));
        var post = e.Pos.XYZ;
        var defend = e.TaskAi!.TaskManager.GetTask<AiTaskEidolonDefend>();
        Assert.NotNull(defend);

        // A player hurts it: it does not turn on them.
        float playerHealth = player.Entity.GetBehavior<EntityBehaviorHealth>()!.Health;
        e.ReceiveDamage(new DamageSource { Source = EnumDamageSource.Player, SourceEntity = player.Entity, Type = EnumDamageType.BluntAttack }, 2);
        await World.Ticks(60);
        Assert.Null(defend!.Attacker);
        Assert.Equal(0, defend.BlowsLanded);
        Assert.Equal(playerHealth, player.Entity.GetBehavior<EntityBehaviorHealth>()!.Health);

        // A wolf (its own AI taken away, so it stands) hurts it from 5 blocks off: it goes and strikes
        // it until it is dead, then walks back to its post.
        var wolfType = W.GetEntityType(new AssetLocation("game:wolf-eurasian-adult-male"))!;
        var wolf = (EntityAgent)W.ClassRegistry.CreateEntity(wolfType);
        wolf.Pos.SetPos(new Vec3d(at.X + 5.5, at.Y, at.Z + 0.5));
        W.SpawnEntity(wolf);
        await World.Ticks(2);
        if (wolf.GetBehavior<EntityBehaviorTaskAI>() is { } wolfAi)
            wolf.SidedProperties.Behaviors.Remove(wolfAi);
        e.ReceiveDamage(new DamageSource { Source = EnumDamageSource.Entity, SourceEntity = wolf, Type = EnumDamageType.PiercingAttack }, 3);
        await World.Until(() => defend.BlowsLanded > 0, 600);
        Log($"first blow: wolf health {wolf.GetBehavior<EntityBehaviorHealth>()?.Health}; " + State(e));
        await World.Until(() => !wolf.Alive, 1200);
        Log($"wolf dead after {defend.BlowsLanded} blows; " + State(e));
        Assert.True(e.CanWork);
        Assert.Equal(StayOrder.OrderCode, e.Orders.OrderCode);
        await World.Until(() => e.Pos.XYZ.HorizontalSquareDistanceTo(post) < 1.5 * 1.5, 1200);
        Assert.Equal(playerHealth, player.Entity.GetBehavior<EntityBehaviorHealth>()!.Health);
        e.Die(EnumDespawnReason.Removed);
    }

    [AtlasScenario(TimeoutMs = 180_000)]
    public async Task It_runs_down_and_strikes_on_the_move_a_creature_walking_away()
    {
        var at = await Floor(240, 0, reach: 30);
        var p = await World.JoinPlayer("eidolonwitness");
        var player = (IServerPlayer)p.Player;
        player.WorldData.CurrentGameMode = EnumGameMode.Creative;
        await p.TeleportTo(at.AddCopy(0, 0, -12));
        var e = await Spawn(at.AddCopy(-20, 0, 0), player);
        e.Orders!.SetOrder(StayOrder.OrderCode, StayOrder.Args(e.Pos.XYZ));
        var defend = e.TaskAi!.TaskManager.GetTask<AiTaskEidolonDefend>()!;

        // A drifter (tall: jabs) and then a wolf (low: hammers), each hurting it from 3 blocks off and
        // then walking steadily away at 1.5 blocks a second (moved each tick, its own AI taken away),
        // knockback and all, until it is dead. Standing to strike, it would never catch one.
        foreach (var code in new[] { "game:drifter-normal", "game:wolf-eurasian-adult-male" })
        {
            var creature = (EntityAgent)W.ClassRegistry.CreateEntity(W.GetEntityType(new AssetLocation(code))!);
            creature.Pos.SetPos(new Vec3d(e.Pos.X + 3, e.Pos.Y, e.Pos.Z));
            W.SpawnEntity(creature);
            await World.Ticks(2);
            if (creature.GetBehavior<EntityBehaviorTaskAI>() is { } ai)
                creature.SidedProperties.Behaviors.Remove(ai);
            int landed = defend.BlowsLanded, moving = defend.MovingBlowsLanded, missed = defend.BlowsMissed;
            double startX = creature.Pos.X;
            long walk = Sapi.Event.RegisterGameTickListener(dt =>
            {
                if (creature.Alive)
                    creature.Pos.X += 1.5 * dt;
            }, 20);
            try
            {
                e.ReceiveDamage(new DamageSource { Source = EnumDamageSource.Entity, SourceEntity = creature, Type = EnumDamageType.PiercingAttack }, 1);
                await World.Until(() => defend.BlowsLanded > landed, 900);
                Log($"{code}: first blow {creature.Pos.X - startX:0.0} blocks on; " + State(e));
                await World.Until(() => !creature.Alive, 1500);
            }
            finally
            {
                Sapi.Event.UnregisterGameTickListener(walk);
            }
            Log($"{code}: dead {creature.Pos.X - startX:0.0} blocks on, {defend.BlowsLanded - landed} blows " +
                $"({defend.MovingBlowsLanded - moving} on the move), {defend.BlowsMissed - missed} missed; " + State(e));
            Assert.True(defend.MovingBlowsLanded > moving, code + ": struck on the move");
            Assert.True(e.CanWork);
            await World.Until(() => defend.Attacker == null, 300);
        }
        Assert.Equal(StayOrder.OrderCode, e.Orders.OrderCode);
        e.Die(EnumDespawnReason.Removed);
    }
}
