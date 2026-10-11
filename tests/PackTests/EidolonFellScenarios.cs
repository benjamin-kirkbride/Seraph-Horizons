using Atlas.XUnit;
using SeraphHorizons.Mod.Core;
using SeraphHorizons.Mod.Eidolon;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Xunit.Abstractions;

namespace SeraphHorizons.PackTests;

/// <summary>
/// mods-src/seraphhorizons/Eidolon (#677): felling a marked area with the pack's default settings,
/// ordered with the command tool as a player orders it. Given an axe, it fells every grown tree in the
/// area through Logging Expanded (trunks lie where they fell), its axe worn by the flat felling figure
/// per tree, oil spent per tree, and replants from what it carries; a player's log pillar and a sapling
/// stand. Without an axe the order is refused, and an axe that breaks stops it until it is given
/// another. Each scenario on a floor of its own in the sky, its trees grown by the game's generators
/// (<see cref="Felling"/>).
/// </summary>
[AtlasWorld]
public class EidolonFellScenarios(ITestOutputHelper output) : AtlasScenarioBase
{
    private IWorldAccessor W => World.Api.World;
    private EidolonSystem Mod => EidolonSystem.Of(World.Api)!;
    private ItemAxe IronAxe => (ItemAxe)W.GetItem(new AssetLocation("game:axe-felling-iron"))!;

    private void Log(string what)
    {
        W.Logger.Notification("[eidolon-fell-test] " + what);
        output.WriteLine(what);
    }

    /// <summary>What it carries, for these scenarios, until carrying (#676) gives it a container.</summary>
    private sealed class Carrier(Entity entity, IInventory inventory) : EntityBehavior(entity), IEidolonCarrier
    {
        public IInventory? CarriedInventory => inventory;
        public override string PropertyName() => "seraphhorizons-test-carrier";
    }

    private async Task<EntityLaborEidolon> Spawn(BlockPos at, IPlayer owner)
    {
        var e = Mod.Spawn(W, new Vec3d(at.X, at.Y, at.Z), 0, owner, activate: true);
        Assert.NotNull(e);
        await World.Until(() => e!.CanWork, 400);
        return e!;
    }

    private async Task<(IServerPlayer Player, ItemSlot Tool)> Commander(string name, BlockPos at)
    {
        var p = await World.JoinPlayer(name);
        var player = (IServerPlayer)p.Player;
        player.WorldData.CurrentGameMode = EnumGameMode.Creative;
        await p.TeleportTo(at);
        var slot = player.InventoryManager.ActiveHotbarSlot;
        slot.Itemstack = new ItemStack(W.GetItem(new AssetLocation(EidolonCommanderSystem.CommanderCode)));
        slot.MarkDirty();
        return (player, slot);
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

    /// <summary>Binds the tool to <paramref name="e"/>, picks "fell" and marks the area between the
    /// two floor blocks, which gives the order.</summary>
    private void OrderFell(IPlayer player, ItemSlot tool, EntityLaborEidolon e, BlockPos a, BlockPos b)
    {
        if (ItemEidolonCommander.Bound(tool.Itemstack!).Count == 0)
            RightClick(player, tool, entity: e);
        int index = EidolonCommandModes.IndexOf(FellOrder.OrderCode);
        Assert.True(index >= 0, "no fell mode");
        tool.Itemstack!.Collectible.SetToolMode(tool, player, null!, index);
        RightClick(player, tool, block: a);
        RightClick(player, tool, block: b);
    }

    /// <summary>Gives it an axe as a player does: a right-click on it with the axe.</summary>
    private void GiveAxe(IServerPlayer player, EntityLaborEidolon e, int? durability = null)
    {
        var stack = new ItemStack(IronAxe);
        if (durability is { } d)
            IronAxe.SetDurability(stack, d);
        var slot = new DummySlot(stack);
        e.OnInteract(player.Entity, slot, e.Pos.XYZ.AddCopy(0, 2, 0), EnumInteractMode.Interact);
        Assert.True(slot.Empty, "it did not take the axe");
        Assert.True(e.RightHandItemSlot?.Itemstack?.Collectible is ItemAxe);
    }

    private static string State(EntityLaborEidolon e) => EidolonCommands.Describe(e) + " / " + e.GetInfoText().Replace('\n', ' ');

    private int Wood(BlockPos log) => Felling.Tree(W, IronAxe, log).Wood;

    [AtlasScenario]
    public void The_fell_mode_is_on_the_wheel_after_follow_and_stay_and_marks_an_area()
    {
        var mode = EidolonCommandModes.Get(FellOrder.OrderCode);
        Assert.NotNull(mode);
        Assert.Equal(SeraphHorizons.Mod.Eidolon.Core.EidolonMarkKind.Area, mode!.Mark);
        Assert.True(EidolonCommandModes.IndexOf("fell") > EidolonCommandModes.IndexOf("stay"));
        Assert.True(EidolonOrders.Exists(FellOrder.OrderCode));
    }

    [AtlasScenario(TimeoutMs = 480_000)]
    public async Task Given_an_axe_it_fells_an_area_of_three_trees_and_replants_them()
    {
        var site = await Felling.Sky(World, World.Spawn.AddCopy(-420, 90, 1300), reach: 18);
        var spots = new[] { site.AddCopy(-9, 0, -2), site.AddCopy(0, 0, 7), site.AddCopy(9, 0, -2) };
        var logs = new List<BlockPos>();
        for (int i = 0; i < spots.Length; i++)
            logs.Add(await Felling.Grow(World, spots[i], Felling.Oak, 1.0f, 101 + i));
        var woods = logs.Select(Wood).ToList();
        Log($"trees: {string.Join(", ", logs.Zip(woods, (l, w) => $"{l} {w} logs"))}");
        // The generator grows a tree from the block above the one it is given, so the trunk stands a
        // block over the soil Felling.Grow lays: fill the gap, as a real tree stands on its ground.
        foreach (var l in logs.Where(l => W.BlockAccessor.GetBlock(l.DownCopy()).Id == 0))
            World.SetBlock("game:soil-medium-normal", l.DownCopy());
        Assert.All(woods, w => Assert.True(w >= Mod.Config.FellMinLogs, $"{w} logs"));
        // A player's log pillar and a planted sapling inside the area: neither is a grown tree.
        var pillar = site.AddCopy(5, 0, 9);
        int placedLog = W.GetBlock(new AssetLocation("game:log-placed-oak-ud"))!.Id;
        for (int y = 0; y < 6; y++)
            W.BlockAccessor.SetBlock(placedLog, pillar.AddCopy(0, y, 0));
        var young = site.AddCopy(-5, 0, 9);
        World.SetBlock("game:soil-medium-normal", young.DownCopy());
        World.SetBlock("game:sapling-oak-free", young);
        await World.Ticks(5);

        var (player, tool) = await Commander("eidolonforester", site.AddCopy(0, 0, -14));
        var e = await Spawn(site.AddCopy(0, 0, -11), player);
        double oilBefore = e.Oil?.Tank?.Points ?? 0;
        // It carries a sapling and a seed of oak: two of the three are replanted.
        var carried = new InventoryGeneric(2, "eidolontest-carried", "1", World.Api);
        carried[0].Itemstack = new ItemStack(W.GetBlock(new AssetLocation("game:sapling-oak-free")));
        carried[1].Itemstack = new ItemStack(W.GetItem(new AssetLocation("game:treeseed-oak")));
        e.SidedProperties.Behaviors.Add(new Carrier(e, carried));

        var a = site.AddCopy(-13, -1, -8);
        var b = site.AddCopy(13, -1, 12);
        // No axe: the order is refused.
        OrderFell(player, tool, e, a, b);
        Assert.Null(e.Orders!.OrderCode);

        GiveAxe(player, e);
        int full = IronAxe.GetRemainingDurability(e.RightHandItemSlot!.Itemstack!);
        OrderFell(player, tool, e, a, b);
        Assert.Equal(FellOrder.OrderCode, e.Orders.OrderCode);

        int lastLeft = 3, ticks = 0;
        await World.Until(() =>
        {
            int left = logs.Count(l => Wood(l) > 0);
            if (left != lastLeft || ++ticks % 300 == 0)
            {
                Log($"{left} standing, at {e.Pos.XYZ}: " + State(e));
                lastLeft = left;
            }
            return e.Orders.OrderCode == null;
        }, 9000);
        Log("after: " + State(e));
        Assert.Null(e.Orders.OrderCode);
        Assert.All(logs, l => Assert.Equal(0, Wood(l)));

        // The trunks lie where they fell (TrunkEntities), every log in them.
        var trunks = Felling.TrunksNear(World, site, 24);
        Assert.True(trunks.Count >= 3, $"{trunks.Count} trunks");
        Assert.Equal(woods.Sum(), trunks.Sum(t => SeraphHorizons.Mod.Machines.Trunks.StoredLogs(t, W)));
        // The axe wore as a player's: the flat figure per tree.
        Assert.Equal(full - 3 * FellingWearConfig.Defaults.ThinTree, IronAxe.GetRemainingDurability(e.RightHandItemSlot!.Itemstack!));
        // Oil per tree.
        if (e.Oil?.Tank is { } tank)
            Assert.Equal(oilBefore - 3 * Mod.Config.OilPerTreeFelled, tank.Points, 3);
        // Replanted at two stumps, from the sapling and the seed it carried.
        int saplings = logs.Count(l => W.BlockAccessor.GetBlock(l).Code?.Path == "sapling-oak-free");
        Assert.Equal(2, saplings);
        Assert.True(carried[0].Empty && carried[1].Empty);
        // The player's pillar and the young sapling stand.
        Assert.All(Enumerable.Range(0, 6), y => Assert.Equal(placedLog, W.BlockAccessor.GetBlock(pillar.AddCopy(0, y, 0)).Id));
        Assert.Equal("sapling-oak-free", W.BlockAccessor.GetBlock(young).Code?.Path);
        e.Die(EnumDespawnReason.Removed);
    }

    [AtlasScenario(TimeoutMs = 360_000)]
    public async Task An_axe_that_breaks_stops_it_until_it_is_given_another()
    {
        var site = await Felling.Sky(World, World.Spawn.AddCopy(-420, 90, 1400), reach: 14);
        var first = await Felling.Grow(World, site.AddCopy(-4, 0, 0), Felling.Oak, 1.0f, 101);
        var second = await Felling.Grow(World, site.AddCopy(5, 0, 0), Felling.Oak, 1.0f, 102);
        Log($"trees: {first} {Wood(first)} logs, {second} {Wood(second)} logs");
        Assert.True(Wood(first) >= Mod.Config.FellMinLogs && Wood(second) >= Mod.Config.FellMinLogs, $"{Wood(first)}, {Wood(second)}");

        var (player, tool) = await Commander("eidolonaxebreak", site.AddCopy(0, 0, -12));
        var e = await Spawn(site.AddCopy(-4, 0, -6), player);
        // Worn to the felling figure: it shatters after the first tree is down.
        GiveAxe(player, e, durability: FellingWearConfig.Defaults.ThinTree);
        OrderFell(player, tool, e, site.AddCopy(-10, -1, -4), site.AddCopy(10, -1, 4));
        Assert.Equal(FellOrder.OrderCode, e.Orders!.OrderCode);

        await World.Until(() => e.RightHandItemSlot?.Itemstack == null, 4000);
        await World.Ticks(60);
        Log("axe broken: " + State(e));
        Assert.Null(e.RightHandItemSlot?.Itemstack);
        Assert.Equal(1, new[] { first, second }.Count(l => Wood(l) == 0));
        Assert.Equal(FellOrder.OrderCode, e.Orders.OrderCode);
        Assert.Contains("no axe", e.GetInfoText());
        await World.Ticks(200);
        Assert.Equal(1, new[] { first, second }.Count(l => Wood(l) == 0));

        GiveAxe(player, e);
        await World.Until(() => e.Orders.OrderCode == null, 4000);
        Log("after: " + State(e));
        Assert.Equal(0, Wood(first));
        Assert.Equal(0, Wood(second));
        e.Die(EnumDespawnReason.Removed);
    }
}
