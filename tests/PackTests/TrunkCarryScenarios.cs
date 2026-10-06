using System.Collections;
using Atlas.XUnit;
using HarmonyLib;
using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.TrunkEntities;
using SeraphHorizons.Mod.TrunkEntities.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Xunit.Abstractions;

namespace SeraphHorizons.PackTests;

/// <summary>
/// mods-src/seraphhorizons/TrunkEntities/Game/TrunkCarry.cs: trunks through the pinned Carry On,
/// with the pack's default settings. The server's side only: Carry On's own client prediction of a
/// put-down goes through the same patched placement, which a server cannot run. Each scenario
/// works on a granite floor of its own high in the sky, with a player of its own.
/// </summary>
[AtlasWorld]
public class TrunkCarryScenarios(ITestOutputHelper output) : AtlasScenarioBase
{
    private IWorldAccessor W => World.Api.World;

    private TrunkEntitySystem Mod => TrunkEntitySystem.Of(World.Api);

    private Block BlockOf(string code) =>
        W.GetBlock(new AssetLocation(code)) is { Id: > 0 } block ? block : throw new Xunit.Sdk.XunitException($"no block {code}");

    private ItemStack Trunk(int logs, string size = "md", string wood = "oak")
    {
        var stack = new ItemStack(BlockOf($"loggingmod:treetrunk-{wood}-{size}-no-north"));
        var slots = new TreeAttribute();
        slots["0"] = new ItemstackAttribute(new ItemStack(BlockOf($"game:log-placed-{wood}-ud"), logs));
        stack.Attributes["slots"] = slots;
        return stack;
    }

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

    private async Task<IServerPlayer> Player(string name, BlockPos at)
    {
        var p = await World.JoinPlayer(name);
        await p.TeleportTo(at);
        await World.Ticks(5);
        var player = p.Player;
        player.InventoryManager.ActiveHotbarSlot.Itemstack = null;
        player.Entity.Controls.ShiftKey = player.Entity.Controls.Sneak = false;
        return player;
    }

    private List<Entity> Around(BlockPos pos, System.Func<Entity, bool> match, int range = 8) =>
        W.GetEntitiesAround(pos.ToVec3d().Add(0.5, 0.5, 0.5), range, range, e => e.Alive && match(e)).ToList();

    // Carry On's carry manager and a member of it, by name, as the mod finds them.
    private object CarryManager =>
        AccessTools.Property(AccessTools.TypeByName(TrunkCarry.CarrySystemName), "CarryManager")
            .GetValue(World.Api.ModLoader.GetModSystem(TrunkCarry.CarrySystemName))!;

    private object Invoke(string method, params object?[] args)
    {
        var m = AccessTools.TypeByName("CarryOn.API.Common.Interfaces.ICarryManager").GetMethod(method)
                ?? throw new Xunit.Sdk.XunitException($"no ICarryManager.{method}");
        var result = m.Invoke(CarryManager, args);
        for (int i = 0; i < args.Length; i++)
            if (args[i] is string s)
                output.WriteLine($"{method} arg {i}: {s}");
        return result!;
    }

    private static EntityBehaviorTrunkCarry Hold(Entity trunk) =>
        trunk.GetBehavior<EntityBehaviorTrunkCarry>() ?? throw new Xunit.Sdk.XunitException("trunk entity has no carry behaviour");

    private static object Hands => Enum.Parse(AccessTools.TypeByName("CarryOn.API.Common.Models.CarrySlot"), "Hands");

    /// <summary>The Carryable's Hands slot animation and transform templates on <paramref name="block"/>.</summary>
    private static (string? Animation, string[] Templates, float WalkSpeed)? HandsSettings(Block block)
    {
        var carryables = block.BlockBehaviors.Where(TrunkCarry.IsCarryable).ToList();
        if (carryables.Count == 0)
            return null;
        Assert.Single(carryables);
        var b = carryables[0];
        var templates = (string[])AccessTools.Property(b.GetType(), "TransformTemplates").GetValue(b)!;
        var storage = AccessTools.Property(b.GetType(), "Slots").GetValue(b)!;
        var dict = (IDictionary)AccessTools.Field(storage.GetType(), "SlotSettingsDict").GetValue(storage)!;
        foreach (DictionaryEntry e in dict)
            if (e.Key.ToString() == "Hands")
                return ((string?)AccessTools.Property(e.Value!.GetType(), "Animation").GetValue(e.Value),
                        templates,
                        (float)AccessTools.Property(e.Value.GetType(), "WalkSpeedModifier").GetValue(e.Value)!);
        throw new Xunit.Sdk.XunitException($"{block.Code}'s Carryable has no Hands slot");
    }

    [AtlasScenario]
    public void Carrying_runs()
    {
        Assert.True(Mod.Enabled);
        Assert.True(Mod.CarryOn);
        Assert.True(TrunkCarry.Available(World.Api));
        Assert.Contains(new AssetLocation("seraphhorizons", "patches/trunkentities-carryon.json"), TrunkEntitySystem.PatchAssets);
        Assert.Contains(W.GetEntityType(TrunkEntitySystem.ThinCode)!.Server.BehaviorsAsJsonObj, b => b["code"].AsString() == TrunkCarry.BehaviorCode);
        Assert.Contains(W.GetEntityType(TrunkEntitySystem.ThickCode)!.Server.BehaviorsAsJsonObj, b => b["code"].AsString() == TrunkCarry.BehaviorCode);
    }

    [AtlasScenario]
    public void A_carried_trunk_takes_logging_expandeds_animation_by_size()
    {
        foreach (var (size, animation) in new[] { ("xs", "trunkcarry"), ("sm", "trunkcarry"), ("md", "trunkcarry"), ("lg", "trunkcarry"),
                                                  ("xl", "trunkcarryheavy"), ("xxl", "trunkcarryheavy") })
        foreach (var branches in new[] { "yes", "no", "debarked" })
        {
            var block = BlockOf($"loggingmod:treetrunk-birch-{size}-{branches}-east");
            var settings = HandsSettings(block) ?? throw new Xunit.Sdk.XunitException($"{block.Code} has no Carryable");
            output.WriteLine($"{block.Code}: {settings.Animation}, [{string.Join(", ", settings.Templates)}], walk {settings.WalkSpeed}");
            Assert.Equal(animation, settings.Animation);
            // no template: Carry On's carry-trunk is the game's chest, carried across the front;
            // the pack's own hands transform in the patch puts the trunk on the shoulder
            Assert.Empty(settings.Templates);
            Assert.Equal(0f, settings.WalkSpeed);
        }
    }

    [AtlasScenario]
    public void Trunk_storage_racks_cannot_be_carried()
    {
        var racks = W.Blocks.Where(b => b?.Code is { Domain: "loggingmod" } c && c.Path.StartsWith("trunkstorage-")).ToList();
        Assert.NotEmpty(racks);
        foreach (var rack in racks)
        {
            Assert.DoesNotContain(rack.BlockBehaviors, TrunkCarry.IsCarryable);
            Assert.DoesNotContain(rack.CollectibleBehaviors, TrunkCarry.IsCarryable);
        }
        // the other Logging Expanded blocks keep theirs
        var resinRack = W.Blocks.First(b => b?.Code is { Domain: "loggingmod" } c && c.Path.StartsWith("resinrack"));
        Assert.Contains(resinRack.BlockBehaviors, TrunkCarry.IsCarryable);
    }

    [AtlasScenario]
    public async Task A_trunk_given_goes_into_empty_hands_and_back_out()
    {
        var pos = await Floor(0);
        var player = await Player("trunkbearer", pos.AddCopy(2, 0, 0));
        Assert.Null(TrunkCarry.Carried(player));
        Assert.Null(TrunkCarry.Take(player));

        Assert.True(TrunkCarry.TryGive(player, Trunk(10)));
        var carried = TrunkCarry.Carried(player);
        Assert.NotNull(carried);
        Assert.Equal("loggingmod:treetrunk-oak-md-no-north", carried.Collectible.Code.ToString());
        Assert.Equal(10, Trunks.StoredLogs(carried, W));

        // the hands are full: a second trunk is refused, and the first is still there
        Assert.False(TrunkCarry.TryGive(player, Trunk(4)));
        Assert.Equal(10, Trunks.StoredLogs(TrunkCarry.Carried(player)!, W));

        var taken = TrunkCarry.Take(player);
        Assert.NotNull(taken);
        Assert.Equal(10, Trunks.StoredLogs(taken, W));
        Assert.Null(TrunkCarry.Carried(player));
        // not a trunk
        Assert.False(TrunkCarry.TryGive(player, new ItemStack(BlockOf("game:log-placed-oak-ud"))));
        Assert.Null(TrunkCarry.Carried(player));
    }

    [AtlasScenario]
    public async Task A_trunk_is_refused_while_a_hand_holds_an_item()
    {
        var pos = await Floor(-20);
        var player = await Player("trunkjuggler", pos.AddCopy(2, 0, 0));
        var offhand = player.Entity.LeftHandItemSlot;
        Assert.NotNull(offhand);

        // something in the offhand: Carry On could then neither put the trunk down nor free the hand
        offhand.Itemstack = new ItemStack(W.GetItem(new AssetLocation("game:stick")));
        Assert.False(TrunkCarry.TryGive(player, Trunk(6)));
        Assert.Null(TrunkCarry.Carried(player));

        // sneak-clicking a trunk entity leaves it lying there
        var trunk = TrunkSpawns.Spawn(W, Trunk(6), pos.ToVec3d().Add(0.5, 0, 0.5), 0)!;
        await World.Ticks(2);
        player.Entity.Controls.ShiftKey = true;
        trunk.OnInteract(player.Entity, player.InventoryManager.ActiveHotbarSlot, new Vec3d(0, 0.5, 0), EnumInteractMode.Interact);
        player.Entity.Controls.ShiftKey = false;
        Assert.True(trunk.Alive);
        Assert.Null(TrunkCarry.Carried(player));

        // something in the active hand
        offhand.Itemstack = null;
        player.InventoryManager.ActiveHotbarSlot.Itemstack = new ItemStack(W.GetItem(new AssetLocation("game:stick")));
        Assert.False(TrunkCarry.TryGive(player, Trunk(6)));
        player.InventoryManager.ActiveHotbarSlot.Itemstack = null;

        // both empty: taken
        Assert.True(TrunkCarry.TryGive(player, Trunk(6)));
        Assert.NotNull(TrunkCarry.Take(player));
        trunk.Die(EnumDespawnReason.Removed);
    }

    [AtlasScenario]
    public async Task Carrying_a_trunk_slows_by_its_logs_and_stops_when_put_away()
    {
        var pos = await Floor(20);
        var player = await Player("trunkplodder", pos.AddCopy(2, 0, 0));
        var stats = player.Entity.Stats;
        float before = stats.GetBlended("walkspeed");
        Assert.False(stats["walkspeed"].ValuesByKey.ContainsKey(TrunkCarry.SpeedCode));

        Assert.True(TrunkCarry.TryGive(player, Trunk(4, "xs")));
        await World.Ticks(10);
        output.WriteLine($"walkspeed {before} -> {stats.GetBlended("walkspeed")} with 4 logs");
        Assert.Equal(TrunkWeight.CarrySpeed(4, Mod.Config), stats.GetBlended("walkspeed") - (before - 1f), 3);
        Assert.Equal(0.25f, TrunkWeight.CarrySpeed(4, Mod.Config), 3);
        TrunkCarry.Take(player);

        Assert.True(TrunkCarry.TryGive(player, Trunk(40, "xxl")));
        await World.Ticks(10);
        output.WriteLine($"walkspeed {stats.GetBlended("walkspeed")} with 40 logs");
        Assert.Equal(TrunkWeight.CarrySpeed(40, Mod.Config), stats.GetBlended("walkspeed") - (before - 1f), 3);

        TrunkCarry.Take(player);
        await World.Ticks(10);
        Assert.False(stats["walkspeed"].ValuesByKey.ContainsKey(TrunkCarry.SpeedCode));
        Assert.Equal(before, stats.GetBlended("walkspeed"), 3);
    }

    [AtlasScenario]
    public async Task Sneak_clicking_a_trunk_entity_shoulders_it()
    {
        var pos = await Floor(40);
        var player = await Player("trunkshoulderer", pos.AddCopy(2, 0, 0));
        var trunk = TrunkSpawns.Spawn(W, Trunk(12), pos.ToVec3d().Add(0.5, 0, 0.5), 0)!;
        await World.Ticks(2);
        var slot = player.InventoryManager.ActiveHotbarSlot;

        // without sneaking it is a grab, not a carry
        trunk.OnInteract(player.Entity, slot, new Vec3d(0, 0.5, 0), EnumInteractMode.Interact);
        Assert.True(trunk.Alive);
        Assert.Null(TrunkCarry.Carried(player));
        Mod.Grabs!.Release(player.PlayerUID);

        // sneak + right click starts Carry On's pick-up hold; the trunk is shouldered only when it ends
        output.WriteLine($"pick-up hold {TrunkCarry.PickUpSeconds(World.Api, trunk.Trunk!.Block)} s");
        player.Entity.Controls.ShiftKey = true;
        player.Entity.ServerControls.RightMouseDown = true;
        trunk.OnInteract(player.Entity, slot, new Vec3d(0, 0.5, 0), EnumInteractMode.Interact);
        Assert.True(Hold(trunk).Holding);
        await World.Ticks(1);
        Assert.True(trunk.Alive);
        Assert.Null(TrunkCarry.Carried(player));
        await World.Until(() => !trunk.Alive, 10000);
        player.Entity.Controls.ShiftKey = false;
        player.Entity.ServerControls.RightMouseDown = false;
        Assert.False(Hold(trunk).Holding);
        var carried = TrunkCarry.Carried(player);
        Assert.NotNull(carried);
        Assert.Equal(12, Trunks.StoredLogs(carried, W));
        Assert.Empty(Around(pos, e => e is EntityTrunk));

        // hands full: a second trunk stays where it is
        var other = TrunkSpawns.Spawn(W, Trunk(5, "sm"), pos.ToVec3d().Add(0.5, 0, 3.5), 0)!;
        await World.Ticks(2);
        player.Entity.Controls.ShiftKey = true;
        player.Entity.ServerControls.RightMouseDown = true;
        other.OnInteract(player.Entity, slot, new Vec3d(0, 0.5, 0), EnumInteractMode.Interact);
        Assert.False(Hold(other).Holding);
        await World.Ticks(10);
        player.Entity.Controls.ShiftKey = false;
        player.Entity.ServerControls.RightMouseDown = false;
        Assert.True(other.Alive);
        Assert.Equal(12, Trunks.StoredLogs(TrunkCarry.Carried(player)!, W));
        other.Die(EnumDespawnReason.Removed);
        TrunkCarry.Take(player);
    }

    [AtlasScenario]
    public async Task Letting_go_before_the_pick_up_hold_ends_leaves_the_trunk()
    {
        var pos = await Floor(140);
        var player = await Player("trunkletgo", pos.AddCopy(2, 0, 0));
        var trunk = TrunkSpawns.Spawn(W, Trunk(7), pos.ToVec3d().Add(0.5, 0, 0.5), 0)!;
        await World.Ticks(2);
        var slot = player.InventoryManager.ActiveHotbarSlot;

        player.Entity.Controls.ShiftKey = true;
        player.Entity.ServerControls.RightMouseDown = true;
        trunk.OnInteract(player.Entity, slot, new Vec3d(0, 0.5, 0), EnumInteractMode.Interact);
        Assert.True(Hold(trunk).Holding);
        player.Entity.ServerControls.RightMouseDown = false;
        await World.Until(() => !Hold(trunk).Holding, 5000);
        player.Entity.Controls.ShiftKey = false;
        // well past the hold's length: nothing more happens
        long until = W.ElapsedMilliseconds + (long)(TrunkCarry.PickUpSeconds(World.Api, trunk.Trunk!.Block) * 1000) + 500;
        await World.Until(() => W.ElapsedMilliseconds > until, 10000);
        Assert.True(trunk.Alive);
        Assert.Null(TrunkCarry.Carried(player));

        // a trunk removed mid-hold ends the hold
        player.Entity.Controls.ShiftKey = true;
        player.Entity.ServerControls.RightMouseDown = true;
        trunk.OnInteract(player.Entity, slot, new Vec3d(0, 0.5, 0), EnumInteractMode.Interact);
        Assert.True(Hold(trunk).Holding);
        trunk.Die(EnumDespawnReason.Removed);
        await World.Ticks(5);
        player.Entity.Controls.ShiftKey = false;
        player.Entity.ServerControls.RightMouseDown = false;
        Assert.False(Hold(trunk).Holding);
        Assert.Null(TrunkCarry.Carried(player));
    }

    [AtlasScenario]
    public void No_trunk_has_a_back_slot()
    {
        var trunks = W.Blocks.Where(b => b?.Code is { Domain: "loggingmod" } c && c.Path.StartsWith("treetrunk-", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(trunks);
        foreach (var block in trunks)
            Assert.False(TrunkCarry.HasBackSlot(World.Api, block), $"{block.Code} can go on a back");
    }

    [AtlasScenario]
    public async Task A_trunk_put_on_a_back_is_laid_down_beside_the_player()
    {
        var pos = await Floor(160);
        var player = await Player("trunkbacker", pos);
        var set = AccessTools.TypeByName("CarryOn.API.Common.Interfaces.ICarryManager").GetMethods()
            .Single(m => m.Name == "SetCarried" && m.GetParameters().Length == 4);
        var carriedType = set.GetParameters()[1].ParameterType;
        var slotType = AccessTools.TypeByName("CarryOn.API.Common.Models.CarrySlot");
        var stack = Trunk(11);
        var data = new TreeAttribute();
        data.SetString("blockCode", stack.Collectible.Code.ToShortString());
        data.SetString("type", "");
        var carried = AccessTools.Constructor(carriedType, [slotType, typeof(ItemStack), typeof(ITreeAttribute)])
            .Invoke([Enum.Parse(slotType, "Back"), stack, data]);

        set.Invoke(CarryManager, [player.Entity, carried, null, true]);
        await World.Ticks(5);

        Assert.Null(TrunkCarry.OnBack(player.Entity));
        Assert.Null(TrunkCarry.Carried(player));
        var laid = Assert.IsType<EntityTrunk>(Assert.Single(Around(pos, e => e is EntityTrunk)));
        Assert.Equal(11, laid.Logs);
        laid.Die(EnumDespawnReason.Removed);
    }

    [AtlasScenario]
    public async Task Putting_a_carried_trunk_down_lays_a_trunk_entity_and_no_block()
    {
        var pos = await Floor(60);
        var player = await Player("trunklayer", pos.AddCopy(0, 0, 3));
        player.Entity.Pos.Yaw = 0; // looking towards -z
        Assert.True(TrunkCarry.TryGive(player, Trunk(9)));

        // Carry On's own put-down, as its server handler runs it for a client's place-down
        var selection = new BlockSelection { Position = pos.DownCopy(), Face = BlockFacing.UP, HitPosition = new Vec3d(0.5, 1, 0.5) };
        object?[] args = [player, Hands, selection, null, "__ignore__"];
        bool placed = (bool)Invoke("TryPlaceDownAt", args);
        output.WriteLine($"placed {placed}, failure {args[4]}, at {args[3]}");
        Assert.True(placed);
        await World.Ticks(5);

        Assert.Null(TrunkCarry.Carried(player));
        for (int x = -2; x <= 2; x++)
        for (int y = 0; y <= 2; y++)
        for (int z = -4; z <= 4; z++)
        {
            var b = W.BlockAccessor.GetBlock(pos.AddCopy(x, y, z));
            Assert.True(b.Id == 0, $"{b.Code} at {pos.AddCopy(x, y, z)}");
        }
        var trunk = Assert.IsType<EntityTrunk>(Assert.Single(Around(pos, e => e is EntityTrunk)));
        Assert.Equal(9, trunk.Logs);
        output.WriteLine($"trunk at {trunk.Pos.XYZ}, yaw {trunk.Pos.Yaw}");
        // along the player's view (-z), reaching away from the selected cell
        Assert.InRange(trunk.Pos.X, pos.X + 0.3, pos.X + 0.7);
        Assert.InRange(trunk.Pos.Z, pos.Z - 1.2, pos.Z - 0.8);
        Assert.Empty(Around(pos, e => e is EntityItem item && Trunks.IsTrunk(item.Itemstack)));
        Assert.False(player.Entity.Stats["walkspeed"].ValuesByKey.ContainsKey(TrunkCarry.SpeedCode));
        trunk.Die(EnumDespawnReason.Removed);
    }

    [AtlasScenario]
    public async Task A_dropped_carried_trunk_lands_as_a_trunk_entity()
    {
        var pos = await Floor(80);
        var player = await Player("trunkdropper", pos.AddCopy(0, 0, 0));
        Assert.True(TrunkCarry.TryGive(player, Trunk(7, "sm")));

        // what death, damage and the quick drop key do
        var slots = Array.CreateInstance(Hands.GetType(), 1);
        slots.SetValue(Hands, 0);
        Invoke("DropCarried", player.Entity, slots, 4);
        await World.Ticks(5);

        Assert.Null(TrunkCarry.Carried(player));
        var trunk = Assert.IsType<EntityTrunk>(Assert.Single(Around(pos, e => e is EntityTrunk)));
        Assert.Equal(7, trunk.Logs);
        Assert.Empty(Around(pos, e => e is EntityItem item && Trunks.IsTrunk(item.Itemstack)));
        Assert.Empty(Around(pos, e => e.Code?.Domain == "carryon"));
        for (int x = -4; x <= 4; x++)
        for (int y = 0; y <= 3; y++)
        for (int z = -4; z <= 4; z++)
            Assert.Equal(0, W.BlockAccessor.GetBlock(pos.AddCopy(x, y, z)).Id);
        trunk.Die(EnumDespawnReason.Removed);
    }

    private const string BasicCart = "cartwrightscaravan:cart-basiccart-classic-oak";

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task A_cart_takes_a_carried_trunk_and_gives_it_back_to_the_hands()
    {
        var pos = await Floor(100);
        foreach (var code in new[] { BasicCart })
        {
            var type = W.GetEntityType(new AssetLocation(code)) ?? throw new Xunit.Sdk.XunitException($"{code} is not an entity type");
            Assert.Contains(type.Server.BehaviorsAsJsonObj, b => b["code"].AsString() == "carryon:attachablecarryable");
            Assert.Contains(type.Client.BehaviorsAsJsonObj, b => b["code"].AsString() == "carryon:attachablecarryable");
        }
        var sled = W.EntityTypes.FirstOrDefault(t => t.Code.Domain == "cartwrightscaravan" && t.Code.Path.StartsWith("sled"));
        Assert.NotNull(sled);
        Assert.Contains(sled.Server.BehaviorsAsJsonObj, b => b["code"].AsString() == "carryon:attachablecarryable");

        var cartType = W.GetEntityType(new AssetLocation(BasicCart))!;
        var cart = W.ClassRegistry.CreateEntity(cartType);
        cart.Pos.SetPos(pos.X + 0.5, pos.Y, pos.Z + 0.5);
        W.SpawnEntity(cart);
        var player = await Player("trunkcarter", pos.AddCopy(2, 0, 0));
        await World.Ticks(20);
        var attachable = cart.GetBehavior<EntityBehaviorAttachable>() ?? throw new Xunit.Sdk.XunitException("the cart has no attachable behaviour");
        var boxes = cart.GetBehavior<EntityBehaviorSelectionBoxes>()!;
        AccessTools.Method(typeof(EntityBehaviorSelectionBoxes), "loadSelectionBoxes").Invoke(boxes, null);
        int box = Array.FindIndex(boxes.selectionBoxes, b => b.AttachPoint.Code == "RightStorage3AP");
        Assert.True(box >= 0, $"no RightStorage3AP among {string.Join(", ", boxes.selectionBoxes.Select(b => b.AttachPoint.Code))}");
        var slot = attachable.GetSlotFromSelectionBoxIndex(box);
        Assert.NotNull(slot);
        Assert.True(slot.Empty);

        // attach, as Carry On does on its key over the slot
        Assert.True(TrunkCarry.TryGive(player, Trunk(6, "sm")));
        object?[] attach = [player, cart.EntityId, box, "__ignore__", true];
        bool attached = (bool)Invoke("TryAttach", attach);
        output.WriteLine($"attached {attached}, failure {attach[3]}");
        Assert.True(attached);
        Assert.Null(TrunkCarry.Carried(player));
        Assert.True(Trunks.IsTrunk(slot.Itemstack));
        Assert.Equal(6, Trunks.StoredLogs(slot.Itemstack!, W));

        // detach by Carry On's key: into the hands
        object?[] detach = [player, cart.EntityId, box, "__ignore__", true];
        bool detached = (bool)Invoke("TryDetach", detach);
        output.WriteLine($"detached {detached}, failure {detach[3]}");
        Assert.True(detached);
        Assert.True(slot.Empty);
        var back = TrunkCarry.Carried(player);
        Assert.NotNull(back);
        Assert.Equal(6, Trunks.StoredLogs(back, W));

        // the game's own empty-hand take (ctrl + right click) also puts it in the hands, not an inventory
        Assert.True((bool)Invoke("TryAttach", [player, cart.EntityId, box, "__ignore__", true]));
        Assert.True(Trunks.IsTrunk(slot.Itemstack));
        bool taken = (bool)AccessTools.Method(typeof(EntityBehaviorAttachable), "TryRemoveAttachment").Invoke(attachable, [player.Entity, box])!;
        Assert.True(taken);
        Assert.True(slot.Empty);
        var again = TrunkCarry.Carried(player);
        Assert.NotNull(again);
        Assert.Equal(6, Trunks.StoredLogs(again, W));
        Assert.False(again.Attributes.HasAttribute("carryonbackup"));
        Assert.False(again.Attributes.HasAttribute("backpack"));

        // and with full hands it stays on the cart
        Assert.True((bool)Invoke("TryAttach", [player, cart.EntityId, box, "__ignore__", true]));
        Assert.True(TrunkCarry.TryGive(player, Trunk(3, "xs")));
        Assert.False((bool)AccessTools.Method(typeof(EntityBehaviorAttachable), "TryRemoveAttachment").Invoke(attachable, [player.Entity, box])!);
        Assert.True(Trunks.IsTrunk(slot.Itemstack));
        TrunkCarry.Take(player);
        cart.Die(EnumDespawnReason.Removed);
    }
}
