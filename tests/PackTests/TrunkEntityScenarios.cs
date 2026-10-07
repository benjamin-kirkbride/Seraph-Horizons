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
    public void Trunk_entities_run_and_trunks_are_given_to_no_survival_player()
    {
        Assert.True(Mod.Enabled);
        Assert.NotNull(Mod.Logging);
        Assert.True(W.Config.GetBool(TrunkEntitySystem.RunningKey));
        var trunk = BlockOf("loggingmod:treetrunk-oak-lg-no-north");
        // a trunk item is an ordinary block stack in a slot (Logging Expanded's own flag), but no
        // give reaches a player who is not in creative mode
        Assert.Equal(EnumItemStorageFlags.Backpack, trunk.StorageFlags);
        Assert.False(TrunkPockets.MayGive(null, new ItemStack(trunk)));
        Assert.True(TrunkPockets.MayGive(null, new ItemStack(BlockOf("game:rock-granite"))));
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
    public async Task A_trunk_cannot_be_given_to_a_survival_player_but_can_to_a_creative_one()
    {
        var p = await World.JoinPlayer("trunkgiver");
        await p.TeleportTo((await Floor(40)).AddCopy(2, 0, 0));
        var player = p.Player;
        player.WorldData.CurrentGameMode = EnumGameMode.Survival;
        Assert.False(TrunkPockets.MayGive(player, Trunk(6)));
        Assert.False(player.InventoryManager.TryGiveItemstack(Trunk(6), true));
        Assert.False(player.Entity.TryGiveItemStack(Trunk(6)));
        Assert.Null(OwnTrunkSlot(player));
        // anything else is given as before
        Assert.True(player.InventoryManager.TryGiveItemstack(new ItemStack(BlockOf("game:rock-granite")), true));

        player.WorldData.CurrentGameMode = EnumGameMode.Creative;
        Assert.True(TrunkPockets.MayGive(player, Trunk(6)));
        Assert.True(player.InventoryManager.TryGiveItemstack(Trunk(6), true));
        var given = OwnTrunkSlot(player);
        Assert.NotNull(given);
        given.Itemstack = null;
        given.MarkDirty();
        player.WorldData.CurrentGameMode = EnumGameMode.Survival;
    }

    private static ItemSlot? OwnTrunkSlot(IPlayer player) =>
        new[] { GlobalConstants.hotBarInvClassName, GlobalConstants.backpackInvClassName }
            .SelectMany(name => player.InventoryManager.GetOwnInventory(name) ?? Enumerable.Empty<ItemSlot>())
            .FirstOrDefault(s => Trunks.IsTrunk(s.Itemstack));

    [AtlasScenario]
    public async Task A_trunk_left_in_a_slot_moves_and_is_thrown_out_as_a_trunk_entity()
    {
        var pos = await Floor(140);
        var p = await World.JoinPlayer("trunkmover");
        await p.TeleportTo(pos.AddCopy(0, 0, 3));
        await World.Ticks(5);
        var player = p.Player;
        player.WorldData.CurrentGameMode = EnumGameMode.Survival;
        // from before: a trunk sitting in a hotbar slot, which no give put there
        var slot = player.InventoryManager.GetHotbarInventory()[2];
        slot.Itemstack = Trunk(7, "md");
        slot.MarkDirty();

        // it moves into a slot that takes Logging Expanded's own flag (a bag slot, with
        // TreeTrunkBackpackOnly on; any slot without it), as a player's click moves it
        var backpack = player.InventoryManager.GetOwnInventory(GlobalConstants.backpackInvClassName)!;
        var hotbar = player.InventoryManager.GetHotbarInventory();
        var target = backpack.Concat(hotbar).FirstOrDefault(s => s != slot && s.Empty && s.CanHold(slot));
        Assert.True(target != null, "no own slot takes a trunk");
        var op = new ItemStackMoveOperation(W, EnumMouseButton.Left, 0, EnumMergePriority.AutoMerge, 1);
        Assert.Equal(1, slot.TryPutInto(target, ref op));
        Assert.True(slot.Empty);
        Assert.True(Trunks.IsTrunk(target.Itemstack));

        // thrown out, it lies on the ground as a trunk entity, and stays there
        Assert.True(player.InventoryManager.DropItem(target, true));
        await World.Ticks(10);
        Assert.True(target.Empty);
        Assert.Empty(Around(pos, e => e is EntityItem item && Trunks.IsTrunk(item.Itemstack)));
        var trunk = Assert.IsType<EntityTrunk>(Assert.Single(Around(pos, e => e is EntityTrunk)));
        Assert.Equal(7, trunk.Logs);
        Assert.Null(OwnTrunkSlot(player));
        trunk.Die(EnumDespawnReason.Removed);
    }

    [AtlasScenario]
    public async Task A_felled_trunk_at_a_survival_players_feet_is_not_collected()
    {
        var pos = await Floor(160);
        var p = await World.JoinPlayer("trunkstander");
        await p.TeleportTo(pos);
        await World.Ticks(5);
        var player = p.Player;
        player.WorldData.CurrentGameMode = EnumGameMode.Survival;
        // as felling drops it: an item entity, right where the player stands
        W.SpawnItemEntity(Trunk(5, "sm"), pos.ToVec3d().Add(0.5, 0.2, 0.5));
        await World.Ticks(40);

        var trunk = Assert.IsType<EntityTrunk>(Assert.Single(Around(pos, e => e is EntityTrunk)));
        Assert.Equal(5, trunk.Logs);
        Assert.False(trunk.CanCollect(player.Entity));
        Assert.Empty(Around(pos, e => e is EntityItem item && Trunks.IsTrunk(item.Itemstack)));
        Assert.Null(OwnTrunkSlot(player));
        trunk.Die(EnumDespawnReason.Removed);
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
    public async Task A_trunk_left_in_a_hotbar_is_laid_down_as_an_entity_and_not_placed()
    {
        var pos = await Floor(-20);
        var p = await World.JoinPlayer("trunkplacer");
        await p.TeleportTo(pos.AddCopy(0, 0, 3));
        await World.Ticks(5);
        var player = p.Player;
        player.WorldData.CurrentGameMode = EnumGameMode.Survival;
        player.Entity.Pos.Yaw = 0;
        // from before trunk entities ran, or given in creative: a trunk sitting in a hotbar slot
        var slot = player.InventoryManager.ActiveHotbarSlot;
        slot.Itemstack = Trunk(9, "md");
        int blockId = slot.Itemstack.Block.Id;

        // the game's own placement from the hotbar, as its server runs it for a client's click
        var server = World.Api.World;
        var systems = (System.Collections.IEnumerable)HarmonyLib.AccessTools.Field(server.GetType(), "Systems").GetValue(server)!;
        var simulation = systems.Cast<object>().Single(s => s.GetType().Name == "ServerSystemBlockSimulation");
        var packetType = HarmonyLib.AccessTools.TypeByName("Packet_ClientBlockPlaceOrBreak");
        var packet = Activator.CreateInstance(packetType)!;
        void Set(string field, object value) => HarmonyLib.AccessTools.Field(packetType, field).SetValue(packet, value);
        Set("X", pos.X);
        Set("Y", pos.Y);
        Set("Z", pos.Z);
        Set("Mode", 1);
        Set("BlockType", blockId);
        Set("OnBlockFace", BlockFacing.UP.Index);
        Set("DidOffset", 1);
        bool placed = (bool)HarmonyLib.AccessTools.Method(simulation.GetType(), "TryModifyBlockInWorld").Invoke(simulation, [player, packet])!;
        Assert.True(placed);
        await World.Ticks(5);

        // the hotbar gave the trunk up, no block went down, and the trunk lies there as an entity
        Assert.True(slot.Empty);
        for (int x = -2; x <= 2; x++)
        for (int y = 0; y <= 2; y++)
        for (int z = -4; z <= 4; z++)
            Assert.Equal(0, W.BlockAccessor.GetBlock(pos.AddCopy(x, y, z)).Id);
        var trunk = Assert.IsType<EntityTrunk>(Assert.Single(Around(pos, e => e is EntityTrunk)));
        Assert.Equal(9, trunk.Logs);
        Assert.InRange(trunk.Pos.X, pos.X + 0.3, pos.X + 0.7);
        Assert.InRange(trunk.Pos.Z, pos.Z + 0.3, pos.Z + 0.7);
        Assert.Empty(Around(pos, e => e is EntityItem item && Trunks.IsTrunk(item.Itemstack)));
        trunk.Die(EnumDespawnReason.Removed);
    }

    [AtlasScenario]
    public async Task A_grab_saved_with_a_trunk_is_cleared_when_it_loads()
    {
        var pos = await Floor(-40);
        // a trunk as a world saves it mid-grab: the grabber's id, the grab's rope id, and the rope
        // in the ropetieable's list; the session that made the rope is gone, so the game has no such cloth
        var trunk = (EntityTrunk)W.ClassRegistry.CreateEntity(W.GetEntityType(TrunkEntitySystem.ThinCode)!);
        trunk.WatchedAttributes.SetItemstack(EntityTrunk.TrunkKey, Trunk(6));
        trunk.WatchedAttributes.SetLong(EntityTrunk.GrabbedByKey, 987654321);
        trunk.WatchedAttributes.SetInt(EntityTrunk.GrabClothKey, 424242);
        trunk.WatchedAttributes["clothIds"] = new IntArrayAttribute([424242]);
        trunk.Pos.SetPos(pos.ToVec3d().Add(0.5, 0, 0.5));
        Assert.True(trunk.Grabbed);
        Assert.Null(W.Api.ModLoader.GetModSystem<ClothManager>().GetClothSystem(424242));
        W.SpawnEntity(trunk);
        await World.Ticks(3);

        Assert.True(trunk.Alive);
        Assert.False(trunk.Grabbed);
        Assert.False(trunk.WatchedAttributes.HasAttribute(EntityTrunk.GrabClothKey));
        Assert.Empty(trunk.GetBehavior<EntityBehaviorRopeTieable>()!.ClothIds?.value ?? []);

        // and a player can grab it
        var p = await World.JoinPlayer("trunkregrabber");
        await p.TeleportTo(pos.AddCopy(2, 0, 0));
        await World.Ticks(5);
        var player = p.Player;
        var slot = player.InventoryManager.ActiveHotbarSlot;
        slot.Itemstack = null;
        // grabbed by the end at local -z (yaw 0: world +z), clicked there
        var length = TrunkBox.Size(trunk.TypeClass).Length;
        var (cx, cz) = TrunkPull.EndPos(trunk.Pos.X, trunk.Pos.Z, trunk.Pos.Yaw, length, -1);
        var click = new Vec3d(cx, trunk.Pos.Y + 0.5, cz);
        player.Entity.ServerControls.RightMouseDown = true;
        trunk.OnInteract(player.Entity, slot, click, EnumInteractMode.Interact);
        Assert.True(trunk.Grabbed);
        Assert.Equal(player.Entity.EntityId, trunk.GrabbedBy);
        Assert.Same(trunk, Mod.Grabs!.HeldBy(player));
        // the player is held to the trunk's pace: 6 logs (weight 48) walk at its factor, 50/48
        // capped at the ceiling
        var walk = player.Entity.Stats["walkspeed"].ValuesByKey;
        Assert.True(walk.ContainsKey(TrunkGrab.DragCode), "no drag walk speed");
        Assert.Equal(TrunkPull.DragSpeed(trunk.LandWeight) - 1, walk[TrunkGrab.DragCode].Value, 3);
        Assert.Equal(TrunkPull.DragCeiling - 1, walk[TrunkGrab.DragCode].Value, 3);
        // no rope of any kind: the grab is the mod's own pull
        Assert.Empty(trunk.GetBehavior<EntityBehaviorRopeTieable>()!.ClothIds?.value ?? []);
        Assert.False(trunk.WatchedAttributes.HasAttribute(EntityTrunk.GrabClothKey));

        // the held button repeats the interact: nothing restarts or lets go
        for (int i = 0; i < 5; i++)
        {
            trunk.OnInteract(player.Entity, slot, new Vec3d(0, 0.5, 0), EnumInteractMode.Interact);
            await World.Ticks(1);
            Assert.True(trunk.Grabbed);
            Assert.Same(trunk, Mod.Grabs.HeldBy(player));
        }

        // the player steps away, still within reach: the trunk follows and turns its grabbed
        // end (world +z at the start) towards the player (world +x)
        var start = trunk.Pos.XYZ;
        float startYaw = trunk.Pos.Yaw;
        await p.TeleportTo(pos.AddCopy(3, 0, 0));
        await World.Ticks(40);
        output.WriteLine($"trunk from {start} yaw {startYaw} to {trunk.Pos.XYZ} yaw {trunk.Pos.Yaw}, grabbed {trunk.Grabbed}");
        Assert.True(trunk.Grabbed);
        Assert.True(trunk.Pos.X > start.X + 0.1, $"the trunk did not follow: {start} → {trunk.Pos.XYZ}");
        Assert.True(Math.Abs(TrunkPull.Wrap(trunk.Pos.Yaw - startYaw)) > 0.2, $"the trunk did not turn: yaw {trunk.Pos.Yaw}");
        var (gx, gz) = TrunkPull.EndPos(trunk.Pos.X, trunk.Pos.Z, trunk.Pos.Yaw, length, -1);
        var (ox, oz) = TrunkPull.EndPos(trunk.Pos.X, trunk.Pos.Z, trunk.Pos.Yaw, length, 1);
        var me = player.Entity.Pos;
        Assert.True(Math.Pow(me.X - gx, 2) + Math.Pow(me.Z - gz, 2) < Math.Pow(me.X - ox, 2) + Math.Pow(me.Z - oz, 2),
            "the grabbed end does not lead");

        player.Entity.ServerControls.RightMouseDown = false;
        await World.Until(() => !trunk.Grabbed, 5000);
        Assert.Null(Mod.Grabs.HeldBy(player));
        Assert.False(walk.ContainsKey(TrunkGrab.DragCode), "the drag walk speed outlived the grab");
        Assert.Empty(trunk.GetBehavior<EntityBehaviorRopeTieable>()!.ClothIds?.value ?? []);
        // no rope item ever comes of a grab
        Assert.Empty(W.GetEntitiesAround(trunk.Pos.XYZ, 16, 16,
            e => e is EntityItem { Itemstack.Collectible.Code.Path: "rope" }));

        // too far from the trunk: no grab
        await p.TeleportTo(pos.AddCopy(6, 0, 0));
        await World.Ticks(2);
        player.Entity.ServerControls.RightMouseDown = true;
        trunk.OnInteract(player.Entity, slot, new Vec3d(0, 0.5, 0), EnumInteractMode.Interact);
        Assert.False(trunk.Grabbed);
        player.Entity.ServerControls.RightMouseDown = false;
        trunk.Die(EnumDespawnReason.Removed);
    }

    // Spawns a thin trunk of `logs` at `pos` (yaw 0, along z), has a fresh player grab its end at
    // local -z (world +z) and walk to `to`, and returns the trunk after `ticks`, still grabbed.
    // `to` must stay within GrabRange (3) of that end, which lies at z + 2.5, or the grab lets go.
    private async Task<EntityTrunk> PullFrom(BlockPos pos, int logs, string name, BlockPos to, int ticks)
    {
        var trunk = (EntityTrunk)W.ClassRegistry.CreateEntity(W.GetEntityType(TrunkEntitySystem.ThinCode)!);
        trunk.WatchedAttributes.SetItemstack(EntityTrunk.TrunkKey, Trunk(logs));
        trunk.Pos.SetPos(pos.ToVec3d().Add(0.5, 0, 0.5));
        W.SpawnEntity(trunk);
        await World.Ticks(20);
        var p = await World.JoinPlayer(name);
        await p.TeleportTo(pos.AddCopy(0, 0, 3));
        await World.Ticks(5);
        var player = p.Player;
        var slot = player.InventoryManager.ActiveHotbarSlot;
        slot.Itemstack = null;
        var (cx, cz) = TrunkPull.EndPos(trunk.Pos.X, trunk.Pos.Z, trunk.Pos.Yaw, TrunkBox.Size(trunk.TypeClass).Length, -1);
        player.Entity.ServerControls.RightMouseDown = true;
        trunk.OnInteract(player.Entity, slot, new Vec3d(cx, trunk.Pos.Y + 0.5, cz), EnumInteractMode.Interact);
        Assert.True(trunk.Grabbed);
        await p.TeleportTo(to);
        await World.Ticks(ticks);
        output.WriteLine($"{name}: trunk at {trunk.Pos.XYZ}, afloat {trunk.Afloat}, grabbed {trunk.Grabbed}");
        Assert.True(trunk.Grabbed);
        player.Entity.ServerControls.RightMouseDown = false;
        await World.Until(() => !trunk.Grabbed, 5000);
        return trunk;
    }

    [AtlasScenario]
    public async Task A_grabbed_trunk_is_pulled_up_a_one_block_step()
    {
        var pos = await Floor(-60, 8);
        // a step one block high across the way, from z +3 (the trunk's +z end is at +2.5)
        int granite = BlockOf("game:rock-granite").Id;
        for (int x = -8; x <= 8; x++)
            for (int z = 3; z <= 8; z++)
                W.BlockAccessor.SetBlock(granite, pos.AddCopy(x, 0, z));
        // the player stands on the step, 2.5 blocks beyond the trunk's end: the trunk comes up
        // to within the grab's slack of them, so its middle ends past z +1.5
        var trunk = await PullFrom(pos, 6, "trunkstepper", pos.AddCopy(0, 1, 5), 120);
        Assert.True(trunk.Pos.Y > pos.Y + 0.9, $"the trunk did not climb the step: {trunk.Pos.XYZ}");
        Assert.True(trunk.Pos.Z > pos.Z + 1.5, $"the trunk did not move onto the step: {trunk.Pos.XYZ}");
        trunk.Die(EnumDespawnReason.Removed);
    }

    [AtlasScenario]
    public async Task A_heavy_trunk_is_pulled_faster_afloat_than_on_land()
    {
        var land = await Floor(-80, 8);
        var pool = await Floor(-100, 8);
        int water = BlockOf("game:water-still-7").Id;
        int granite = BlockOf("game:rock-granite").Id;
        for (int x = -8; x <= 8; x++)
            for (int z = -8; z <= 8; z++)
            {
                // a pool two deep, sunk into the floor (still water, which a still block keeps)
                var below = pool.AddCopy(x, -1, z);
                W.BlockAccessor.SetBlock(granite, below.AddCopy(0, -2, 0));
                W.BlockAccessor.SetBlock(0, below.AddCopy(0, -1, 0));
                W.BlockAccessor.SetBlock(0, below);
                W.BlockAccessor.SetBlock(water, below.AddCopy(0, -1, 0), BlockLayersAccess.Fluid);
                W.BlockAccessor.SetBlock(water, below, BlockLayersAccess.Fluid);
            }
        var onLand = await PullFrom(land, 48, "trunklandpuller", land.AddCopy(0, 0, 5), 60);
        var afloat = await PullFrom(pool.AddCopy(0, -2, 0), 48, "trunkwaterpuller", pool.AddCopy(0, -1, 5), 60);
        Assert.True(afloat.Afloat || afloat.Swimming, "the trunk in the pool is not afloat");
        double landMoved = onLand.Pos.Z - (land.Z + 0.5), waterMoved = afloat.Pos.Z - (pool.Z + 0.5);
        output.WriteLine($"moved on land {landMoved:F2}, afloat {waterMoved:F2}");
        Assert.True(waterMoved > landMoved + 0.5, $"afloat {waterMoved:F2} is not faster than on land {landMoved:F2}");
        onLand.Die(EnumDespawnReason.Removed);
        afloat.Die(EnumDespawnReason.Removed);
    }
}
