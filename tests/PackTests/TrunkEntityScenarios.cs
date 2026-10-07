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
using Vintagestory.API.Server;
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
    private async Task<BlockPos> Floor(int dx, int reach = 6, int dz = 60)
    {
        var origin = World.Spawn.AddCopy(dx, 40, dz);
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

    // A thin trunk of `logs` at `pos`'s middle, lying along z (yaw 0).
    private EntityTrunk SpawnThin(BlockPos pos, int logs)
    {
        var trunk = (EntityTrunk)W.ClassRegistry.CreateEntity(W.GetEntityType(TrunkEntitySystem.ThinCode)!);
        trunk.WatchedAttributes.SetItemstack(EntityTrunk.TrunkKey, Trunk(logs));
        trunk.Pos.SetPos(pos.ToVec3d().Add(0.5, 0, 0.5));
        W.SpawnEntity(trunk);
        return trunk;
    }

    // A fresh player at `at` with an empty hand, not sneaking.
    private async Task<IServerPlayer> Walker(string name, BlockPos at)
    {
        var p = await World.JoinPlayer(name);
        await p.TeleportTo(at);
        await World.Ticks(5);
        var player = p.Player;
        player.InventoryManager.ActiveHotbarSlot.Itemstack = null;
        player.Entity.Controls.ShiftKey = player.Entity.Controls.Sneak = false;
        return player;
    }

    private static (double X, double Z) EndOf(EntityTrunk trunk, int end) =>
        TrunkPull.EndPos(trunk.Pos.X, trunk.Pos.Z, trunk.Pos.Yaw, TrunkBox.Size(trunk.TypeClass).Length, end);

    // An empty-hand click on the trunk at end `end`.
    private static void Click(EntityTrunk trunk, IServerPlayer player, int end)
    {
        var (x, z) = EndOf(trunk, end);
        trunk.OnInteract(player.Entity, player.InventoryManager.ActiveHotbarSlot, new Vec3d(x, trunk.Pos.Y + 0.5, z), EnumInteractMode.Interact);
    }

    // The driver's keys: the game feeds a mounted player's movement keys into their seat's controls.
    private static EntityControls Keys(IServerPlayer player) => player.Entity.MountedOn!.Controls;

    // Progress in the server log, live (the test's own output only shows at the end).
    private void Log(string what) => W.Logger.Notification("[trunk-drive-test] " + what);

    private static double Flat(Vec3d a, double x, double z) => Math.Sqrt((a.X - x) * (a.X - x) + (a.Z - z) * (a.Z - z));

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task A_drive_saved_with_a_trunk_is_cleared_when_it_loads()
    {
        var pos = await Floor(-40);
        // a trunk as a world saves it mid-drive (the driver's id), and from when the grab was a
        // game rope (the rope's id, and the rope in the ropetieable's list; the session that made
        // the rope is gone, so the game has no such cloth)
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
        Assert.False(trunk.Driven);
        Assert.False(trunk.WatchedAttributes.HasAttribute(EntityTrunk.GrabClothKey));
        Assert.Empty(trunk.GetBehavior<EntityBehaviorRopeTieable>()!.ClothIds?.value ?? []);

        // and a player can take it: by the end at local -z (yaw 0: world +z), clicked there
        var player = await Walker("trunkregrabber", pos.AddCopy(2, 0, 0));
        Click(trunk, player, -1);
        Assert.True(trunk.Grabbed);
        Assert.Equal(player.Entity.EntityId, trunk.GrabbedBy);
        Assert.Equal(-1, trunk.DriveEnd);
        var seat = Assert.IsType<TrunkDriveSeat>(player.Entity.MountedOn);
        Assert.Same(trunk, seat.Entity);
        Assert.Same(player.Entity, trunk.Driver);
        // no rope of any kind
        Assert.Empty(trunk.GetBehavior<EntityBehaviorRopeTieable>()!.ClothIds?.value ?? []);
        await World.Ticks(2);
        // the driver stands just beyond that end, along the axis, on the trunk's ground
        var (ex, ez) = EndOf(trunk, -1);
        var me = player.Entity.Pos.XYZ;
        output.WriteLine($"trunk {trunk.Pos.XYZ}, end ({ex:F2}, {ez:F2}), driver {me}");
        Assert.InRange(Flat(me, ex, ez), TrunkDrive.StandOff - 0.15, TrunkDrive.StandOff + 0.15);
        Assert.True(Flat(me, trunk.Pos.X, trunk.Pos.Z) > Flat(new Vec3d(ex, 0, ez), trunk.Pos.X, trunk.Pos.Z));
        Assert.InRange(me.Y, trunk.Pos.Y - 0.1, trunk.Pos.Y + 0.1);
        // the driver's own clicks again change nothing
        for (int i = 0; i < 3; i++)
        {
            Click(trunk, player, 1);
            await World.Ticks(1);
            Assert.Same(seat, player.Entity.MountedOn);
            Assert.Equal(-1, trunk.DriveEnd);
        }
        // sneak lets go (the game's dismount on the seat's sneak), and the mark goes with it
        Keys(player).UpdateFromPacket(true, (int)EnumEntityAction.Sneak);
        await World.Ticks(2);
        Assert.Null(player.Entity.MountedOn);
        Assert.False(trunk.Grabbed);
        Assert.False(trunk.Driven);
        trunk.Die(EnumDespawnReason.Removed);
    }

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task A_driven_trunk_moves_and_turns_by_the_drivers_keys()
    {
        var pos = await Floor(0, 10, 100);
        var trunk = SpawnThin(pos, 6);
        await World.Ticks(20);
        var player = await Walker("trunkdriver", pos.AddCopy(0, 0, 3));
        // the end at world +z (local -z)
        Click(trunk, player, -1);
        Assert.True(trunk.Driven);
        Log("driver attached");
        var keys = Keys(player);

        // W: the trunk moves along its axis with the taken end leading, the driver backing up with it
        double z0 = trunk.Pos.Z, x0 = trunk.Pos.X;
        keys.Forward = true;
        await World.Ticks(20);
        keys.Forward = false;
        await World.Ticks(10);
        output.WriteLine($"W: trunk z {z0:F2} -> {trunk.Pos.Z:F2}, x {trunk.Pos.X:F2}, driver {player.Entity.Pos.XYZ}");
        Assert.True(trunk.Pos.Z > z0 + 1, $"W did not move the trunk towards the taken end: {z0:F2} -> {trunk.Pos.Z:F2}");
        Assert.InRange(trunk.Pos.X, x0 - 0.1, x0 + 0.1);
        var (ex, ez) = EndOf(trunk, -1);
        Assert.InRange(Flat(player.Entity.Pos.XYZ, ex, ez), TrunkDrive.StandOff - 0.15, TrunkDrive.StandOff + 0.15);
        Assert.True(player.Entity.Pos.Z > ez, "the driver is not beyond the taken end");

        // S: the other way, the driver pushing it
        double z1 = trunk.Pos.Z;
        keys.Backward = true;
        await World.Ticks(20);
        keys.Backward = false;
        await World.Ticks(10);
        output.WriteLine($"S: trunk z {z1:F2} -> {trunk.Pos.Z:F2}");
        Assert.True(trunk.Pos.Z < z1 - 1, $"S did not push the trunk: {z1:F2} -> {trunk.Pos.Z:F2}");

        // A and D turn it about its middle: both ends swing, the middle stays
        var middle = trunk.Pos.XYZ;
        float yaw0 = trunk.Pos.Yaw;
        keys.Left = true;
        await World.Ticks(20);
        keys.Left = false;
        await World.Ticks(5);
        double turnedA = TrunkPull.Wrap(trunk.Pos.Yaw - yaw0);
        output.WriteLine($"A: yaw {yaw0:F2} -> {trunk.Pos.Yaw:F2} ({turnedA:F2}), middle {middle} -> {trunk.Pos.XYZ}");
        Assert.True(turnedA < -0.2, $"A did not turn the trunk: {turnedA:F2}");
        Assert.True(Flat(trunk.Pos.XYZ, middle.X, middle.Z) < 0.2, "the middle moved while turning");
        // the driver swung with their end (which way is TrunkDriveTests'): still just beyond it
        (ex, ez) = EndOf(trunk, -1);
        Assert.InRange(Flat(player.Entity.Pos.XYZ, ex, ez), TrunkDrive.StandOff - 0.15, TrunkDrive.StandOff + 0.15);
        float yaw1 = trunk.Pos.Yaw;
        keys.Right = true;
        await World.Ticks(20);
        keys.Right = false;
        await World.Ticks(5);
        double turnedD = TrunkPull.Wrap(trunk.Pos.Yaw - yaw1);
        output.WriteLine($"D: yaw {yaw1:F2} -> {trunk.Pos.Yaw:F2} ({turnedD:F2})");
        Assert.True(turnedD > 0.2, $"D did not turn the trunk back: {turnedD:F2}");
        Assert.True(Flat(trunk.Pos.XYZ, middle.X, middle.Z) < 0.2, "the middle moved while turning");
        player.Entity.TryUnmount();
        await World.Ticks(2);
        Assert.False(trunk.Grabbed);
        trunk.Die(EnumDespawnReason.Removed);
    }

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task No_one_else_may_touch_a_driven_trunk()
    {
        var pos = await Floor(-30, 8, 100);
        var trunk = SpawnThin(pos.AddCopy(0, 0, -3), 6);
        await World.Ticks(20);
        var player = await Walker("trunkkeeper", pos.AddCopy(0, 0, 0));
        Click(trunk, player, -1);
        Assert.True(trunk.Driven);
        Log("keeper attached");

        // another player can neither take it (an empty hand at the other end), nor shoulder it
        // (sneak), nor reach the seat; the first stays the driver
        var other = await Walker("trunkintruder", pos.AddCopy(3, 0, -6));
        Click(trunk, other, 1);
        Assert.Null(other.Entity.MountedOn);
        Assert.Equal(player.Entity.EntityId, trunk.GrabbedBy);
        other.Entity.Controls.ShiftKey = true;
        other.Entity.ServerControls.RightMouseDown = true;
        Click(trunk, other, 1);
        await World.Ticks(40);
        other.Entity.Controls.ShiftKey = false;
        other.Entity.ServerControls.RightMouseDown = false;
        Assert.True(trunk.Alive, "another player shouldered a driven trunk");
        Assert.Same(player.Entity, trunk.Driver);
        Assert.False(other.Entity.TryMount(trunk.DriveSeat!), "a second player mounted the driver's place");

        // letting go leaves the trunk free for the other player
        player.Entity.TryUnmount();
        await World.Ticks(2);
        Assert.False(trunk.Grabbed);
        Click(trunk, other, 1);
        Assert.Same(other.Entity, trunk.Driver);
        Assert.Equal(1, trunk.DriveEnd);
        other.Entity.TryUnmount();
        await World.Ticks(2);
        Assert.False(trunk.Grabbed);
        trunk.Die(EnumDespawnReason.Removed);
    }

    // Spawns a thin trunk of `logs` at `pos` (yaw 0, along z), has a fresh player take its end at
    // local -z (world +z) and hold W for `ticks`, and returns the trunk, let go, and how far along
    // z it went.
    private async Task<(EntityTrunk Trunk, double Moved)> DriveFrom(BlockPos pos, int logs, string name, int ticks)
    {
        var trunk = SpawnThin(pos, logs);
        await World.Ticks(20);
        var player = await Walker(name, pos.AddCopy(0, 0, 3));
        Click(trunk, player, -1);
        Assert.True(trunk.Driven);
        Log($"{name} attached");
        double z0 = trunk.Pos.Z;
        Keys(player).Forward = true;
        await World.Ticks(ticks);
        double moved = trunk.Pos.Z - z0;
        output.WriteLine($"{name}: {logs} logs moved {moved:F2} to {trunk.Pos.XYZ}, afloat {trunk.Afloat}");
        Assert.True(trunk.Driven);
        Keys(player).Forward = false;
        player.Entity.TryUnmount();
        await World.Ticks(2);
        Assert.False(trunk.Grabbed);
        return (trunk, moved);
    }

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task A_one_log_trunk_drives_about_twice_as_fast_as_a_48_log_one()
    {
        var light = await DriveFrom(await Floor(30, 10, 100), 1, "trunklightdriver", 30);
        var heavy = await DriveFrom(await Floor(60, 10, 100), 48, "trunkheavydriver", 30);
        double ratio = light.Moved / heavy.Moved;
        output.WriteLine($"light {light.Moved:F2}, heavy {heavy.Moved:F2}, ratio {ratio:F2}");
        Assert.True(heavy.Moved > 0.5, $"the heavy trunk hardly moved: {heavy.Moved:F2}");
        Assert.InRange(ratio, 1.6, 2.4);
        light.Trunk.Die(EnumDespawnReason.Removed);
        heavy.Trunk.Die(EnumDespawnReason.Removed);
    }

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task A_driven_trunk_climbs_a_one_block_step()
    {
        var pos = await Floor(-60, 8);
        // a step one block high across the way, from z +3 (the trunk's +z end is at +2.5)
        int granite = BlockOf("game:rock-granite").Id;
        for (int x = -8; x <= 8; x++)
            for (int z = 3; z <= 8; z++)
                W.BlockAccessor.SetBlock(granite, pos.AddCopy(x, 0, z));
        var (trunk, moved) = await DriveFrom(pos, 6, "trunkstepper", 40);
        Assert.True(trunk.Pos.Y > pos.Y + 0.9, $"the trunk did not climb the step: {trunk.Pos.XYZ}");
        Assert.True(moved > 1.0, $"the trunk did not move onto the step: {trunk.Pos.XYZ}");
        trunk.Die(EnumDespawnReason.Removed);
    }

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task A_heavy_trunk_drives_faster_afloat_than_on_land()
    {
        var land = await Floor(-80, 10);
        var pool = await Floor(-100, 10);
        int water = BlockOf("game:water-still-7").Id;
        int granite = BlockOf("game:rock-granite").Id;
        for (int x = -10; x <= 10; x++)
            for (int z = -10; z <= 10; z++)
            {
                // a pool two deep, sunk into the floor (still water, which a still block keeps)
                var below = pool.AddCopy(x, -1, z);
                W.BlockAccessor.SetBlock(granite, below.AddCopy(0, -2, 0));
                W.BlockAccessor.SetBlock(0, below.AddCopy(0, -1, 0));
                W.BlockAccessor.SetBlock(0, below);
                W.BlockAccessor.SetBlock(water, below.AddCopy(0, -1, 0), BlockLayersAccess.Fluid);
                W.BlockAccessor.SetBlock(water, below, BlockLayersAccess.Fluid);
            }
        var onLand = await DriveFrom(land, 48, "trunklanddriver", 40);
        var afloat = await DriveFrom(pool.AddCopy(0, -2, 0), 48, "trunkwaterdriver", 40);
        Assert.True(afloat.Trunk.Afloat || afloat.Trunk.Swimming, "the trunk in the pool is not afloat");
        output.WriteLine($"moved on land {onLand.Moved:F2}, afloat {afloat.Moved:F2}");
        Assert.True(afloat.Moved > onLand.Moved * 1.2, $"afloat {afloat.Moved:F2} is not faster than on land {onLand.Moved:F2}");
        onLand.Trunk.Die(EnumDespawnReason.Removed);
        afloat.Trunk.Die(EnumDespawnReason.Removed);
    }

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task A_player_inside_a_trunk_is_pushed_out_of_every_box()
    {
        var pos = await Floor(-60, 8, 100);
        var trunk = SpawnThin(pos, 6);
        await World.Ticks(20);
        var player = await Walker("trunkinsider", pos.AddCopy(4, 0, 0));
        var startTrunk = trunk.Pos.XYZ;
        // into the middle of the trunk, a little off its axis
        player.Entity.TeleportToDouble(trunk.Pos.X + 0.1, trunk.Pos.Y, trunk.Pos.Z + 0.4);
        bool Inside()
        {
            var cb = player.Entity.CollisionBox;
            var p = player.Entity.Pos;
            var box = new Box((float)(p.X - trunk.Pos.X + cb.X1), (float)(p.Y - trunk.Pos.Y + cb.Y1), (float)(p.Z - trunk.Pos.Z + cb.Z1),
                              (float)(p.X - trunk.Pos.X + cb.X2), (float)(p.Y - trunk.Pos.Y + cb.Y2), (float)(p.Z - trunk.Pos.Z + cb.Z2));
            return TrunkBoxes.Turned(trunk.TypeClass, trunk.Pos.Yaw).Any(b => TrunkPush.Overlaps(b, box));
        }
        await World.Ticks(2);
        Log($"insider at {player.Entity.Pos.XYZ}, inside {Inside()}");
        await World.Until(() => !Inside(), 20);
        output.WriteLine($"player out at {player.Entity.Pos.XYZ}; trunk {startTrunk} -> {trunk.Pos.XYZ}");
        Assert.False(Inside());
        // out across the trunk (+x, the near side), and the trunk itself stayed put
        Assert.True(player.Entity.Pos.X > trunk.Pos.X + 0.5);
        Assert.True(Flat(trunk.Pos.XYZ, startTrunk.X, startTrunk.Z) < 0.15, "the trunk was moved");
        trunk.Die(EnumDespawnReason.Removed);
    }
}
