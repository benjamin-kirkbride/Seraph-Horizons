using Atlas.Api;
using Atlas.XUnit;
using SeraphHorizons.Mod.Eidolon;
using SeraphHorizons.Mod.Eidolon.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Xunit.Abstractions;

namespace SeraphHorizons.PackTests;

/// <summary>
/// mods-src/seraphhorizons/Eidolon, several eidolons kept apart (README "Eidolon", working together,
/// *Dispatch* and *Claims*, and the paths round other eidolons), with the pack's default settings, ordered
/// with the command tool as a player orders them: three on one area 16 × 3 × 12 each work stations of their
/// own, each strike in its station's claimed columns, apart, never pushing into each other nor digging in
/// another's columns, the floors within a climb (two by look-ahead routes, whose spare work goes a band
/// deeper), with no slam at nothing, one report; and two ordered across each other's way walk round each
/// other and both arrive. One taking stations in another's part once its own is done is
/// <see cref="EidolonTeamMineScenarios"/>'s. On floors of their own in the sky (<see cref="Felling.Sky"/>), each
/// from its spawns on the virtual clock (<see cref="VirtualClock"/>).
/// </summary>
[AtlasWorld]
public class EidolonSectorScenarios(ITestOutputHelper output) : AtlasScenarioBase
{
    private IWorldAccessor W => World.Api.World;
    private EidolonSystem Mod => EidolonSystem.Of(World.Api)!;

    private void Log(string what)
    {
        W.Logger.Notification("[eidolon-sector-test] " + what);
        output.WriteLine(what);
    }

    private async Task<EntityLaborEidolon> Spawn(BlockPos at, IPlayer owner)
    {
        var e = Mod.Spawn(W, new Vec3d(at.X, at.Y, at.Z), 0, owner, activate: true);
        Assert.NotNull(e);
        await World.Until(() => e!.CanWork, 400);
        return e!;
    }

    private async Task<(ITestPlayer Test, IServerPlayer Player, ItemSlot Tool)> Commander(string name, BlockPos at)
    {
        var p = await World.JoinRetrying(name);
        var player = (IServerPlayer)p.Player;
        player.WorldData.CurrentGameMode = EnumGameMode.Creative;
        await p.TeleportTo(at);
        var slot = player.InventoryManager.ActiveHotbarSlot;
        slot.Itemstack = new ItemStack(W.GetItem(new AssetLocation(EidolonCommanderSystem.CommanderCode)));
        slot.MarkDirty();
        return (p, player, slot);
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

    /// <summary>Binds the tool to each of <paramref name="eidolons"/>, picks "mine" and marks the area
    /// between the two blocks, which gives every bound eidolon the order.</summary>
    private static void OrderMine(IPlayer player, ItemSlot tool, IEnumerable<EntityLaborEidolon> eidolons, BlockPos a, BlockPos b)
    {
        foreach (var e in eidolons)
            RightClick(player, tool, entity: e);
        int index = EidolonCommandModes.IndexOf(MineOrder.OrderCode);
        tool.Itemstack!.Collectible.SetToolMode(tool, player, null!, index);
        RightClick(player, tool, block: a);
        RightClick(player, tool, block: b);
    }

    /// <summary>The reports the orders made (<see cref="MineOrder.Reported"/>: what the owner was told;
    /// Atlas's chat capture fails on the strikes' broken-block particles).</summary>
    private static List<string> Reports(params MineOrder[] orders) => orders.Select(o => o.Reported).OfType<string>().ToList();

    private static string State(EntityLaborEidolon e) => EidolonCommands.Describe(e) + " / " + e.GetInfoText().Replace('\n', ' ');

    private static MarkArea Area(BlockPos a, BlockPos b) => MarkArea.Between(new MarkPos(a.X, a.Y, a.Z), new MarkPos(b.X, b.Y, b.Z));

    private static List<BlockPos> Cells(MarkArea area)
    {
        var cells = new List<BlockPos>();
        for (int x = area.Min.X; x <= area.Max.X; x++)
        for (int y = area.Min.Y; y <= area.Max.Y; y++)
        for (int z = area.Min.Z; z <= area.Max.Z; z++)
            cells.Add(new BlockPos(x, y, z));
        return cells;
    }

    [AtlasScenario(TimeoutMs = 240_000)]
    public async Task Three_eidolons_work_one_area_apart_the_floors_within_a_climb_and_no_slam_at_nothing()
    {
        var site = await Felling.Sky(World, World.Spawn.AddCopy(-720, 90, 2100), reach: 15);
        var blocks = W.BlockAccessor;
        int granite = W.GetBlock(new AssetLocation("game:rock-granite"))!.Id;
        // Granite four thick; a terrace a block lower north of the pit (z -10 to -7), so they climb out
        // of a pit three deep two at a time.
        for (int x = -14; x <= 14; x++)
        for (int z = -14; z <= 14; z++)
        for (int y = -4; y <= -1; y++)
            blocks.SetBlock(y == -1 && z is >= -10 and <= -7 ? 0 : granite, site.AddCopy(x, y, z));
        // 16 × 3 × 12
        var a = site.AddCopy(-8, -3, -6);
        var b = site.AddCopy(7, -1, 5);
        var area = Area(a, b);
        var toMine = Cells(area);

        var (test, player, tool) = await Commander("eidolonsectors", site.AddCopy(0, 0, -13));
        using var clock = VirtualClock.Start(World);
        var eidolons = new[]
        {
            await Spawn(site.AddCopy(-6, 0, -12), player),
            await Spawn(site.AddCopy(0, 0, -12), player),
            await Spawn(site.AddCopy(6, 0, -12), player),
        };
        OrderMine(player, tool, eidolons, a, b);
        var orders = eidolons.Select(e => Assert.IsType<MineOrder>(e.Orders!.Current)).ToArray();
        var homes = eidolons.Select(e => e.Pos.XYZ).ToArray();
        await World.Ticks(5);
        Assert.All(orders, o => Assert.Same(orders[0].Job, o.Job));

        // By look-ahead routes a member may take spare work a band deeper (the owner's rule): floors two
        // climbs apart, not one.
        var watch = new MineWatcher(W, toMine, eidolons, orders, floorSlack: EidolonMinePlans.Routing ? 2 : 1);
        int ticks = 0, lastMined = -1;
        await World.Until(() =>
        {
            watch.Tick();
            int mined = orders.Sum(o => o.Mined);
            if (mined != lastMined && mined % 24 == 0 || ++ticks % 600 == 0)
            {
                Log($"{string.Join(" + ", orders.Select(o => o.Mined))} mined, stations {string.Join(" ", orders.Select(o => o.Sector?.ToString() ?? "none"))}, "
                    + $"mean apart {watch.MeanDistance:0.0}, overlaps {watch.Overlaps}: {string.Join(" || ", eidolons.Select(State))}");
                lastMined = mined;
            }
            return eidolons.All(e => e.Orders!.OrderCode == null);
        }, 24000);
        var lines = Reports(orders);
        Log($"after: {string.Join(" + ", orders.Select(o => o.Mined))}; in/out of station "
            + string.Join(" ", watch.BySector.Select(kv => $"{kv.Key}: {kv.Value.In}/{kv.Value.Out}"))
            + $"; empty strikes {orders.Sum(o => o.EmptyStrikes)}, cut {orders.Sum(o => o.CutStrikes)}; mean apart {watch.MeanDistance:0.00}; overlaps {watch.Overlaps}");
        Log("reports: " + string.Join(" | ", lines));
        Log("faults: " + string.Join(" | ", watch.Faults.Take(30)) + "; overlapping: " + watch.Summary());
        Assert.Empty(watch.Faults);
        Assert.All(toMine, c => Assert.Equal(0, blocks.GetBlock(c).Id));
        Assert.Equal(toMine.Count, orders.Sum(o => o.Mined));
        Assert.All(orders, o => Assert.True(o.Mined > 0, $"one mined nothing: {string.Join(" + ", orders.Select(x => x.Mined))}"));
        // Each broke in its own station's columns, and they kept apart: well over two boxes' width on average,
        // never one pushing through or standing in another (a shallow brush allowed, however long).
        Assert.All(watch.BySector.Values, v => Assert.Equal(0, v.Out));
        Assert.True(watch.MeanDistance >= 5, $"mean distance apart {watch.MeanDistance:0.00}");
        Assert.Null(watch.Pushing());
        // Every slam broke something.
        Assert.Equal(0, orders.Sum(o => o.EmptyStrikes));
        var report = Assert.Single(lines);
        Assert.Contains("Your 3 eidolons", report);
        Assert.Contains($"{toMine.Count} blocks mined", report);
        for (int i = 0; i < 3; i++)
            Assert.True(eidolons[i].Pos.XYZ.HorizontalSquareDistanceTo(homes[i]) <= 2.5 * 2.5, $"{eidolons[i].EntityId} at {eidolons[i].Pos.XYZ}, ordered at {homes[i]}");
        foreach (var e in eidolons)
            e.Die(EnumDespawnReason.Removed);
    }

    [AtlasScenario(TimeoutMs = 60_000)]
    public async Task Two_ordered_across_each_others_way_walk_round_each_other_and_both_arrive()
    {
        var site = await Felling.Sky(World, World.Spawn.AddCopy(-720, 90, 2300), reach: 15);
        // A player near: a goto is no work, so an eidolon with none within its simulation range is
        // inactive (README "Chunk loading") and never sets off.
        await Commander("eidoloncrossing", site.AddCopy(0, 0, -10));
        using var clock = VirtualClock.Start(World);
        var west = await Spawn(site.AddCopy(-9, 0, 0), null!);
        var east = await Spawn(site.AddCopy(9, 0, 0), null!);
        // each to where the other stands, at once: head on
        var toEast = site.AddCopy(9, 0, 0).ToVec3d();
        var toWest = site.AddCopy(-9, 0, 0).ToVec3d();
        west.Orders!.SetOrder(GoToOrder.OrderCode, GoToOrder.Args(toEast, run: false));
        east.Orders!.SetOrder(GoToOrder.OrderCode, GoToOrder.Args(toWest, run: false));
        int overlaps = 0, ticks = 0;
        double nearest = double.MaxValue;
        await World.Until(() =>
        {
            if (MineWatcher.Overlap(west, east))
                overlaps++;
            nearest = Math.Min(nearest, Math.Sqrt(west.Pos.SquareHorDistanceTo(east.Pos.XYZ)));
            if (++ticks % 60 == 0)
                Log($"west {west.Pos.XYZ} east {east.Pos.XYZ}: {State(west)} || {State(east)}");
            return west.Orders.OrderCode == null && east.Orders.OrderCode == null;
        }, 3000);
        Log($"arrived: west {west.Pos.XYZ}, east {east.Pos.XYZ}; nearest {nearest:0.00}, overlapping {overlaps} ticks");
        Assert.Null(west.Orders.OrderCode);
        Assert.Null(east.Orders.OrderCode);
        Assert.True(west.Pos.XYZ.HorizontalSquareDistanceTo(toEast) <= 1, $"west at {west.Pos.XYZ}");
        Assert.True(east.Pos.XYZ.HorizontalSquareDistanceTo(toWest) <= 1, $"east at {east.Pos.XYZ}");
        Assert.Equal(0, overlaps);
        west.Die(EnumDespawnReason.Removed);
        east.Die(EnumDespawnReason.Removed);
    }
}
