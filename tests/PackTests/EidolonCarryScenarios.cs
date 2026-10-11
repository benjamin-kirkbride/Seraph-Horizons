using Atlas.XUnit;
using SeraphHorizons.Mod.Eidolon;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Xunit.Abstractions;

namespace SeraphHorizons.PackTests;

/// <summary>
/// mods-src/seraphhorizons/Eidolon (#676): the carry job with the pack's default settings and Carry On,
/// driven through the command tool as a player uses it. A chest of flint is lifted, carried after its
/// commander 20 blocks, opened while carried (what goes in stays in), kept through the entity's own
/// save, and set down with everything in it, costing a load's oil; a block Carry On does not carry
/// is refused; and an eidolon removed while carrying sets its load down where it stood.
/// </summary>
[AtlasWorld]
public class EidolonCarryScenarios(ITestOutputHelper output) : AtlasScenarioBase
{
    private IWorldAccessor W => World.Api.World;
    private ICoreServerAPI Sapi => (ICoreServerAPI)World.Api;
    private EidolonSystem Mod => EidolonSystem.Of(World.Api)!;

    private void Log(string what)
    {
        W.Logger.Notification("[eidolon-carry-test] " + what);
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
        int granite = Granite.Id;
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

    private Block Granite => W.GetBlock(new AssetLocation("game:rock-granite"))!;

    private async Task<EntityLaborEidolon> Spawn(BlockPos at, IPlayer? owner)
    {
        var e = Mod.Spawn(W, new Vec3d(at.X, at.Y, at.Z), 0, owner, activate: true);
        Assert.NotNull(e);
        await World.Until(() => e!.CanWork, 400);
        return e!;
    }

    private ItemSlot Tool(IPlayer player)
    {
        var slot = player.InventoryManager.ActiveHotbarSlot;
        slot.Itemstack = new ItemStack(W.GetItem(new AssetLocation(EidolonCommanderSystem.CommanderCode))!);
        slot.MarkDirty();
        return slot;
    }

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
    }

    private static string State(EntityLaborEidolon e) => EidolonCommands.Describe(e);

    /// <summary>A chest at <paramref name="pos"/> holding 12 flint in its first slot.</summary>
    private BlockEntityGenericTypedContainer Chest(BlockPos pos)
    {
        var block = W.GetBlock(new AssetLocation("game:chest-east"))!;
        var stack = new ItemStack(block);
        stack.Attributes.SetString("type", "normal-generic");
        W.BlockAccessor.SetBlock(block.Id, pos, stack);
        var chest = Assert.IsAssignableFrom<BlockEntityGenericTypedContainer>(W.BlockAccessor.GetBlockEntity(pos));
        chest.Inventory[0].Itemstack = new ItemStack(W.GetItem(new AssetLocation("game:flint"))!, 12);
        chest.Inventory[0].MarkDirty();
        chest.MarkDirty();
        return chest;
    }

    private static int Count(IInventory inv, string code) =>
        inv.Where(s => !s.Empty && s.Itemstack.Collectible.Code.ToString() == code).Sum(s => s.StackSize);

    [AtlasScenario]
    public void The_wheel_has_carry_and_set_down_after_follow_and_stay_and_Carry_On_is_found()
    {
        Assert.Equal(["follow", "stay", "carry", "setdown"], EidolonCommandModes.All.Take(4).Select(m => m.Code));
        Assert.True(EidolonOrders.Exists(CarryOrder.OrderCode));
        Assert.True(EidolonOrders.Exists(SetDownOrder.OrderCode));
        Assert.True(EidolonCarryOn.Available(World.Api));
        Assert.True(EidolonCarryOn.IsCarryable(World.Api, W.GetBlock(new AssetLocation("game:chest-east"))!));
        Assert.False(EidolonCarryOn.IsCarryable(World.Api, Granite));
    }

    [AtlasScenario(TimeoutMs = 300_000)]
    public async Task It_carries_a_filled_chest_20_blocks_after_its_commander_and_sets_it_down_with_its_contents()
    {
        var at = await Floor(0, 0, reach: 28);
        var p = await World.JoinPlayer("eidolonmule");
        var player = (IServerPlayer)p.Player;
        player.WorldData.CurrentGameMode = EnumGameMode.Creative;
        await p.TeleportTo(at.AddCopy(-4, 0, 4));
        var chestAt = at.AddCopy(-8, 0, 0);
        Chest(chestAt);
        var e = await Spawn(at.AddCopy(-14, 0, 0), player);
        var carry = e.GetBehavior<EntityBehaviorEidolonCarry>()!;
        var config = Mod.Config;
        double oilBefore = e.Oil!.Tank!.Value.Points;

        var slot = Tool(player);
        RightClick(player, slot, entity: e);

        // A block Carry On does not carry is refused, and no order goes out.
        ChooseMode(player, slot, "carry");
        RightClick(player, slot, block: at.AddCopy(-8, -1, 3));
        Assert.Null(e.Orders!.OrderCode);

        // Carry the chest: it walks up, lifts it on the grab frame, and the chest leaves the world.
        RightClick(player, slot, block: chestAt);
        Assert.Equal(CarryOrder.OrderCode, e.Orders.OrderCode);
        await World.Until(() => carry.Carrying, 1200);
        Log("lifted: " + State(e) + " at " + e.Pos.XYZ);
        Assert.Equal(0, W.BlockAccessor.GetBlock(chestAt).Id);
        Assert.Equal("chest", carry.LoadStack!.Block!.FirstCodePart());
        Assert.Contains("Carrying:", e.GetInfoText());
        Assert.True(carry.CarriesContainer());

        // Opened while carried, by its commander: the chest's own contents, and what goes in stays in.
        e.OnInteract(player.Entity, new DummySlot(), e.Pos.XYZ.AddCopy(0, 2, 0), EnumInteractMode.Interact);
        var inv = player.InventoryManager.GetInventory("eidolonload-" + e.EntityId);
        Assert.NotNull(inv);
        Assert.Equal(12, Count(inv, "game:flint"));
        var empty = inv.First(s => s.Empty);
        empty.Itemstack = new ItemStack(W.GetItem(new AssetLocation("game:stick"))!, 5);
        empty.MarkDirty();
        player.InventoryManager.CloseInventory(inv);

        // It saves with the entity: the load and its contents are in what is written.
        using (var ms = new MemoryStream())
        {
            using (var writer = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
                e.ToBytes(writer, false);
            ms.Position = 0;
            var copy = W.ClassRegistry.CreateEntity(e.Properties);
            using var reader = new BinaryReader(ms);
            copy.FromBytes(reader, false, new Dictionary<string, string>());
            var load = copy.WatchedAttributes.GetTreeAttribute(EntityBehaviorEidolonCarry.LoadKey);
            Assert.NotNull(load);
            var saved = (load["Data"] as ITreeAttribute)!.GetTreeAttribute("inventory")!;
            var savedInv = new InventoryGeneric(saved.GetInt("qslots"), "check-0", World.Api);
            savedInv.FromTreeAttributes(saved);
            savedInv.ResolveBlocksOrItems();
            Assert.Equal(12, Count(savedInv, "game:flint"));
            Assert.Equal(5, Count(savedInv, "game:stick"));
        }

        // The player walks 20 blocks on; it follows, carrying.
        await World.Until(() => carry.Carrying && e.AnimManager.IsAnimationActive(EntityBehaviorEidolonCarry.IdleAnimation), 200);
        await p.TeleportTo(at.AddCopy(16, 0, 4));
        await World.Until(() => e.Pos.X > at.X + 8 && e.TaskAi!.PathTraverser?.Active != true, 2400);
        Log("followed: " + State(e) + " at " + e.Pos.XYZ);
        Assert.True(carry.Carrying);

        // Set it down 20 blocks from where it stood: on the granite (the place above it).
        var ground = at.AddCopy(12, -1, 0);
        var place = ground.UpCopy();
        ChooseMode(player, slot, "setdown");
        RightClick(player, slot, block: ground);
        Assert.Equal(SetDownOrder.OrderCode, e.Orders.OrderCode);
        await World.Until(() => e.Orders.OrderCode == null, 1200);
        Log("set down: " + State(e) + " at " + e.Pos.XYZ);
        Assert.False(carry.Carrying);
        Assert.True(place.X - chestAt.X >= 20);
        var placed = Assert.IsAssignableFrom<BlockEntityGenericTypedContainer>(W.BlockAccessor.GetBlockEntity(place));
        Assert.Equal(12, Count(placed.Inventory, "game:flint"));
        Assert.Equal(5, Count(placed.Inventory, "game:stick"));
        Assert.Equal(oilBefore - config.OilPerLoadCarried, e.Oil.Tank!.Value.Points, 3);
        Assert.False(EidolonCarrying_Overlaps(e, place));
        e.Die(EnumDespawnReason.Removed);
    }

    private static bool EidolonCarrying_Overlaps(EntityLaborEidolon e, BlockPos cell) =>
        SeraphHorizons.Mod.Eidolon.Core.EidolonCarrying.Overlaps(e.Pos.X, e.Pos.Z, cell.X, cell.Z);

    [AtlasScenario(TimeoutMs = 180_000)]
    public async Task Removed_while_carrying_it_sets_its_load_down_where_it_stood()
    {
        var at = await Floor(80, 0, reach: 12);
        var p = await World.JoinPlayer("eidolonporter");
        var player = (IServerPlayer)p.Player;
        player.WorldData.CurrentGameMode = EnumGameMode.Creative;
        await p.TeleportTo(at.AddCopy(-4, 0, 4));
        var chestAt = at.AddCopy(2, 0, 0);
        Assert.Equal(12, Count(Chest(chestAt).Inventory, "game:flint"));
        var e = await Spawn(at.AddCopy(-3, 0, 0), player);
        var carry = e.GetBehavior<EntityBehaviorEidolonCarry>()!;
        var slot = Tool(player);
        RightClick(player, slot, entity: e);
        ChooseMode(player, slot, "carry");
        RightClick(player, slot, block: chestAt);
        await World.Until(() => carry.Carrying, 1200);
        var stood = e.Pos.AsBlockPos;
        e.Die(EnumDespawnReason.Removed);
        await World.Ticks(5);

        BlockEntityGenericTypedContainer? found = null;
        for (int dx = -4; dx <= 4 && found == null; dx++)
        for (int dz = -4; dz <= 4 && found == null; dz++)
        for (int dy = -1; dy <= 2 && found == null; dy++)
            found = W.BlockAccessor.GetBlockEntity(stood.AddCopy(dx, dy, dz)) as BlockEntityGenericTypedContainer;
        Assert.NotNull(found);
        Log("released at " + found!.Pos);
        Assert.Equal(12, Count(found.Inventory, "game:flint"));
    }
}
