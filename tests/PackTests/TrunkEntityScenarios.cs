using Atlas.XUnit;
using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.TrunkEntities;
using SeraphHorizons.Mod.TrunkEntities.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Xunit.Abstractions;

namespace SeraphHorizons.PackTests;

/// <summary>
/// mods-src/seraphhorizons/TrunkEntities: Logging Expanded's tree trunks as entities, against the
/// pinned Logging Expanded, with the pack's default settings (<c>TrunkEntities</c> on; the
/// woodworking scenarios' world has it off until they move onto trunk entities). Logging Expanded's
/// felling spawns the trunk as an item entity (<c>FellingListener.SpawnTrunk</c>), so the scenarios
/// spawn one the same way. Each scenario works on a granite floor of its own high in the sky.
/// </summary>
[AtlasWorld]
public class TrunkEntityScenarios(ITestOutputHelper output) : AtlasScenarioBase
{
    private IWorldAccessor W => World.Api.World;

    private TrunkEntitySystem Mod => TrunkEntitySystem.Of(World.Api);

    private Block BlockOf(string code) =>
        W.GetBlock(new AssetLocation(code)) is { Id: > 0 } block ? block : throw new Xunit.Sdk.XunitException($"no block {code}");

    /// <summary>A trunk of <paramref name="wood"/> holding <paramref name="logs"/> logs, with
    /// <paramref name="branches"/> branches counted (and the <c>yes</c> state) when above 0.</summary>
    private ItemStack Trunk(int logs, string size = "md", int branches = 0, string wood = "oak")
    {
        var stack = new ItemStack(BlockOf($"loggingmod:treetrunk-{wood}-{size}-{(branches > 0 ? "yes" : "no")}-north"));
        var slots = new TreeAttribute();
        slots["0"] = new ItemstackAttribute(new ItemStack(BlockOf($"game:log-placed-{wood}-ud"), logs));
        stack.Attributes["slots"] = slots;
        if (branches > 0)
            stack.Attributes.SetInt(Trunks.BranchCountKey, branches);
        return stack;
    }

    /// <summary>A spot 40 above spawn at <paramref name="dx"/>, its chunk loaded, cleared around,
    /// on a granite floor one below it.</summary>
    private async Task<BlockPos> Floor(int dx, int reach = 6)
    {
        var origin = World.Spawn.AddCopy(dx, 40, 60);
        await World.Until(() => W.BlockAccessor.GetChunkAtBlockPos(origin) != null, 30000);
        int granite = BlockOf("game:rock-granite").Id;
        for (int x = -reach; x <= reach; x++)
        for (int z = -reach; z <= reach; z++)
        {
            W.BlockAccessor.SetBlock(granite, origin.AddCopy(x, -1, z));
            for (int y = 0; y <= 5; y++)
                W.BlockAccessor.SetBlock(0, origin.AddCopy(x, y, z));
        }
        return origin;
    }

    private List<Entity> Around(BlockPos pos, System.Func<Entity, bool> match) =>
        W.GetEntitiesAround(pos.ToVec3d().Add(0.5, 0.5, 0.5), 8, 8, e => e.Alive && match(e)).ToList();

    [AtlasScenario]
    public void Trunk_entities_run_and_trunks_fit_no_inventory()
    {
        Assert.True(Mod.Enabled);
        Assert.NotNull(Mod.Logging);
        Assert.True(W.Config.GetBool(TrunkEntitySystem.RunningKey));
        var trunk = BlockOf("loggingmod:treetrunk-oak-lg-no-north");
        Assert.Equal(TrunkEntitySystem.NoStorage, trunk.StorageFlags);
        Assert.NotNull(W.GetEntityType(TrunkEntitySystem.ThinCode));
        Assert.NotNull(W.GetEntityType(TrunkEntitySystem.ThickCode));
    }

    [AtlasScenario]
    public async Task A_spawned_trunk_item_becomes_a_trunk_entity()
    {
        var pos = await Floor(0);
        var stack = Trunk(12, "md", branches: 20);
        W.SpawnItemEntity(stack, pos.ToVec3d().Add(0.5, 0.2, 0.5));
        await World.Ticks(5);

        Assert.Empty(Around(pos, e => e is EntityItem item && Trunks.IsTrunk(item.Itemstack)));
        var trunk = Assert.IsType<EntityTrunk>(Assert.Single(Around(pos, e => e is EntityTrunk)));
        Assert.Equal(TrunkEntitySystem.ThinCode, trunk.Code);
        Assert.Equal(TrunkClass.Thin, trunk.Class);
        Assert.Equal(12, trunk.Logs);
        Assert.Equal("loggingmod:treetrunk-oak-md-yes-north", trunk.Trunk!.Collectible.Code.ToString());
        Assert.Equal(20, trunk.Trunk.Attributes.GetInt(Trunks.BranchCountKey));
        Assert.False(trunk.CanCollect(trunk));
        Assert.Contains("12", trunk.GetInfoText());
        trunk.Die(EnumDespawnReason.Removed);
    }

    [AtlasScenario]
    public async Task A_thick_trunk_item_becomes_a_thick_trunk_entity()
    {
        var pos = await Floor(20);
        W.SpawnItemEntity(Trunk(30, "xl"), pos.ToVec3d().Add(0.5, 0.2, 0.5));
        await World.Ticks(5);
        var trunk = Assert.IsType<EntityTrunk>(Assert.Single(Around(pos, e => e is EntityTrunk)));
        Assert.Equal(TrunkEntitySystem.ThickCode, trunk.Code);
        Assert.Equal(TrunkClass.Thick, trunk.Class);
        Assert.Equal(30, trunk.Logs);
        trunk.Die(EnumDespawnReason.Removed);
    }

    [AtlasScenario]
    public async Task A_trunk_cannot_be_given_to_a_player()
    {
        var p = await World.JoinPlayer("trunkgiver");
        await p.TeleportTo((await Floor(40)).AddCopy(2, 0, 0));
        var player = p.Player;
        Assert.False(player.InventoryManager.TryGiveItemstack(Trunk(6), true));
        Assert.False(player.Entity.TryGiveItemStack(Trunk(6)));
        foreach (var name in new[] { GlobalConstants.hotBarInvClassName, GlobalConstants.backpackInvClassName })
            Assert.DoesNotContain(player.InventoryManager.GetOwnInventory(name) ?? Enumerable.Empty<ItemSlot>(), s => Trunks.IsTrunk(s.Itemstack));
    }

    [AtlasScenario]
    public async Task A_trunk_entity_weighs_by_its_logs()
    {
        var pos = await Floor(60);
        var trunk = TrunkSpawns.Spawn(W, Trunk(10), pos.ToVec3d().Add(0.5, 0, 0.5), 0);
        Assert.NotNull(trunk);
        await World.Ticks(2);
        Assert.Equal(TrunkWeight.Weight(10, Mod.Config), trunk.Properties.Weight);
        Assert.Equal(90f, trunk.Properties.Weight);

        trunk.SetTrunk(Trunk(4));
        Assert.Equal(4, trunk.Logs);
        Assert.Equal(42f, trunk.Properties.Weight);
        Assert.Equal(42f, trunk.WatchedAttributes.GetFloat(EntityTrunk.WeightKey));
        // the type's own properties are shared, and untouched
        Assert.Equal(50f, W.GetEntityType(TrunkEntitySystem.ThinCode)!.Weight);

        var other = TrunkSpawns.Spawn(W, Trunk(48, "lg"), pos.ToVec3d().Add(0.5, 0, 3.5), 0)!;
        await World.Ticks(2);
        Assert.Equal(394f, other.Properties.Weight);
        Assert.Equal(42f, trunk.Properties.Weight);

        trunk.SetTrunk(Trunk(0));
        Assert.False(trunk.Alive);
        other.SetTrunk(null);
        Assert.False(other.Alive);
        Assert.Null(TrunkSpawns.Spawn(W, Trunk(0), pos.ToVec3d(), 0));
    }

    [AtlasScenario]
    public async Task A_dropped_trunk_entity_falls_and_rests_on_the_ground()
    {
        var pos = await Floor(80);
        // entities only simulate near a player
        var p = await World.JoinPlayer("trunkwatcher");
        await p.TeleportTo(pos.AddCopy(4, 0, 0));
        var trunk = TrunkSpawns.Spawn(W, Trunk(8, "sm"), pos.ToVec3d().Add(0.5, 2.5, 0.5), 0.6f)!;
        await World.Ticks(120);
        Assert.True(trunk.Alive);
        output.WriteLine($"trunk at {trunk.Pos.XYZ}, on ground {trunk.OnGround}, motion {trunk.Pos.Motion}");
        Assert.True(trunk.OnGround);
        Assert.InRange(trunk.Pos.Y, pos.Y - 0.05, pos.Y + 0.2);
        Assert.True(trunk.Pos.Motion.Length() < 0.01);
        trunk.Die(EnumDespawnReason.Removed);
    }

    [AtlasScenario]
    public async Task A_placed_trunk_multiblock_is_removed_when_it_loads()
    {
        var pos = await Floor(100);
        var block = BlockOf("loggingmod:treetrunk-oak-lg-no-north");
        W.BlockAccessor.SetBlock(block.Id, pos, Trunk(10, "lg"));
        await World.Ticks(5);
        Assert.Equal(0, W.BlockAccessor.GetBlock(pos).Id);
        for (int z = -3; z <= 3; z++)
            Assert.IsNotType<BlockMultiblock>(W.BlockAccessor.GetBlock(pos.AddCopy(0, 0, z)));
        Assert.Empty(Around(pos, e => e is EntityItem item && Trunks.IsTrunk(item.Itemstack)));
        Assert.Empty(Around(pos, e => e is EntityTrunk));
    }

    [AtlasScenario]
    public async Task An_empty_hand_drags_a_trunk_while_held()
    {
        var pos = await Floor(120);
        var trunk = TrunkSpawns.Spawn(W, Trunk(10), pos.ToVec3d().Add(0.5, 0, 0.5), 0)!;
        var p = await World.JoinPlayer("trunkdragger");
        await p.TeleportTo(pos.AddCopy(2, 0, 0));
        await World.Ticks(5);
        var player = p.Player;
        var slot = player.InventoryManager.ActiveHotbarSlot;
        slot.Itemstack = null;
        var cloth = W.Api.ModLoader.GetModSystem<ClothManager>();

        // sneaking is Carry On's: no grab
        player.Entity.Controls.ShiftKey = true;
        trunk.OnInteract(player.Entity, slot, new Vec3d(0, 0.5, 0), EnumInteractMode.Interact);
        player.Entity.Controls.ShiftKey = false;
        Assert.False(trunk.Grabbed);

        player.Entity.ServerControls.RightMouseDown = true;
        trunk.OnInteract(player.Entity, slot, new Vec3d(0, 0.5, 0), EnumInteractMode.Interact);
        Assert.True(trunk.Grabbed);
        Assert.Equal(player.Entity.EntityId, trunk.GrabbedBy);
        Assert.Same(trunk, Mod.Grabs!.HeldBy(player));
        int id = trunk.WatchedAttributes.GetInt(EntityTrunk.GrabClothKey);
        var rope = cloth.GetClothSystem(id);
        Assert.NotNull(rope);
        Assert.Contains(id, trunk.GetBehavior<EntityBehaviorRopeTieable>()!.ClothIds!.value);
        Assert.Equal(trunk.EntityId, rope.LastPoint.PinnedToEntity?.EntityId);
        Assert.Equal(player.Entity.EntityId, rope.FirstPoint.PinnedToEntity?.EntityId);

        // the player steps away, still within reach: the stretched rope pulls the trunk after
        var start = trunk.Pos.XYZ;
        await p.TeleportTo(pos.AddCopy(3, 0, 0));
        await World.Ticks(40);
        output.WriteLine($"trunk from {start} to {trunk.Pos.XYZ}, grabbed {trunk.Grabbed}");
        Assert.True(trunk.Grabbed);
        Assert.True(trunk.Pos.X > start.X + 0.1, $"the trunk did not follow: {start} → {trunk.Pos.XYZ}");

        player.Entity.ServerControls.RightMouseDown = false;
        await World.Until(() => !trunk.Grabbed, 5000);
        Assert.Null(cloth.GetClothSystem(id));
        Assert.Null(Mod.Grabs.HeldBy(player));
        Assert.Empty(trunk.GetBehavior<EntityBehaviorRopeTieable>()!.ClothIds?.value ?? []);

        // too far from the trunk: no grab
        await p.TeleportTo(pos.AddCopy(6, 0, 0));
        await World.Ticks(2);
        player.Entity.ServerControls.RightMouseDown = true;
        trunk.OnInteract(player.Entity, slot, new Vec3d(0, 0.5, 0), EnumInteractMode.Interact);
        Assert.False(trunk.Grabbed);
        player.Entity.ServerControls.RightMouseDown = false;
        trunk.Die(EnumDespawnReason.Removed);
    }
}
