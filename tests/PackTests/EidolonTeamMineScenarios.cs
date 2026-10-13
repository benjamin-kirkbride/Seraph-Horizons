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
/// mods-src/seraphhorizons/Eidolon, several eidolons mining one area as one job (README "Eidolon",
/// working together), with the pack's default settings, ordered with the command tool as a player
/// orders them. Two given the same area never hold the same column, keep their floors within a climb,
/// report once with both shares and walk back each to its own place; one ordered later
/// joins the job running; and one whose sector is mined out steals half of another's. Waiting (for a
/// square, for a bystander, dry) is <see cref="EidolonTeamWaitScenarios"/>, sectors kept apart and paths
/// round each other <see cref="EidolonSectorScenarios"/>, tunnels <see cref="EidolonTunnelScenarios"/>.
/// On floors of their own in the sky (<see cref="Felling.Sky"/>).
/// </summary>
[AtlasWorld]
public class EidolonTeamMineScenarios(ITestOutputHelper output) : AtlasScenarioBase
{
    private IWorldAccessor W => World.Api.World;
    private EidolonSystem Mod => EidolonSystem.Of(World.Api)!;
    private EidolonJobSystem Jobs => EidolonJobSystem.Of(World.Api)!;

    private void Log(string what)
    {
        W.Logger.Notification("[eidolon-teammine-test] " + what);
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
        var p = await World.JoinPlayer(name);
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

    private static int Feet(Entity e) => (int)Math.Floor(e.Pos.Y + 0.01);

    /// <summary>Ground of granite three blocks thick round the site, a terrace a block lower to the north
    /// (z -6 to -4), so they can climb out of a pit two deep a block at a time.</summary>
    private void Ground(BlockPos site, int granite)
    {
        var blocks = W.BlockAccessor;
        for (int x = -12; x <= 12; x++)
        for (int z = -14; z <= 12; z++)
        {
            blocks.SetBlock(granite, site.AddCopy(x, -3, z));
            blocks.SetBlock(granite, site.AddCopy(x, -2, z));
            blocks.SetBlock(z is >= -6 and <= -4 ? 0 : granite, site.AddCopy(x, -1, z));
        }
    }

    [AtlasScenario(TimeoutMs = 300_000)]
    public async Task Two_eidolons_share_a_pit_a_layer_at_a_time_report_once_and_walk_back_each_to_its_place()
    {
        var site = await Felling.Sky(World, World.Spawn.AddCopy(-720, 90, 1600), reach: 15);
        var blocks = W.BlockAccessor;
        int granite = W.GetBlock(new AssetLocation("game:rock-granite"))!.Id;
        Ground(site, granite);
        // 8 × 4 and two deep, its north edge on the terrace: two sectors of 4 × 4, one each (a sector is
        // at least 4 wide, so a pit 6 wide is one sector and the second waits at the rim).
        var a = site.AddCopy(-4, -2, -3);
        var b = site.AddCopy(3, -1, 0);
        var area = Area(a, b);
        var toMine = Cells(area);

        var (test, player, tool) = await Commander("eidolonteam", site.AddCopy(0, 0, -12));
        var e1 = await Spawn(site.AddCopy(-3, 0, -10), player);
        var e2 = await Spawn(site.AddCopy(3, 0, -10), player);
        OrderMine(player, tool, [e1, e2], a, b);
        var o1 = Assert.IsType<MineOrder>(e1.Orders!.Current);
        var o2 = Assert.IsType<MineOrder>(e2.Orders!.Current);
        var home1 = e1.Pos.XYZ;
        var home2 = e2.Pos.XYZ;
        await World.Ticks(2);
        Assert.NotNull(o1.Job);
        Assert.Same(o1.Job, o2.Job);

        // Watch each tick: never a column held by both; nothing broken under either's box at or below
        // its feet; nothing broken with a block left two layers higher.
        var standing = toMine.ToHashSet();
        var faults = new List<string>();
        int ticks = 0, lastMined = -1;
        await World.Until(() =>
        {
            var held1 = Jobs.Mining.Held(e1.EntityId);
            var held2 = Jobs.Mining.Held(e2.EntityId);
            if (held1.Intersect(held2).Any())
                faults.Add($"both hold {string.Join(" ", held1.Intersect(held2))}");
            var broke = standing.Where(c => blocks.GetBlock(c).Id == 0).ToList();
            foreach (var c in broke)
            {
                foreach (var e in new[] { e1, e2 })
                    if (EidolonMining.Undermines(e.Pos.X, Feet(e), e.Pos.Z, new MarkPos(c.X, c.Y, c.Z)) && c.Y < e.Pos.Y)
                        faults.Add($"{c} broke under {e.EntityId} at {e.Pos.XYZ}");
                standing.Remove(c);
            }
            if (broke.Count > 0 && standing.Count > 0 && broke.Min(c => c.Y) < standing.Max(c => c.Y) - 1)
                faults.Add($"{string.Join(" ", broke)} broke with {standing.Max(c => c.Y)} left");
            if (o1.Mined + o2.Mined != lastMined || ++ticks % 300 == 0)
            {
                Log($"{o1.Mined} + {o2.Mined} mined, at {e1.Pos.XYZ} and {e2.Pos.XYZ}: {State(e1)} || {State(e2)}");
                lastMined = o1.Mined + o2.Mined;
            }
            return e1.Orders.OrderCode == null && e2.Orders.OrderCode == null;
        }, 6000);
        var lines = Reports(o1, o2);
        Log("reports: " + string.Join(" | ", lines));
        Log($"after: {o1.Mined} + {o2.Mined}; standing: "
            + string.Join(" ", toMine.Where(c => blocks.GetBlock(c).Id != 0).Select(c => $"({c.X - site.X},{c.Y - site.Y},{c.Z - site.Z})")));
        Assert.Empty(faults);
        Assert.All(toMine, c => Assert.Equal(0, blocks.GetBlock(c).Id));
        Assert.Equal(toMine.Count, o1.Mined + o2.Mined);
        Assert.True(o1.Mined > 0 && o2.Mined > 0, $"{o1.Mined} and {o2.Mined}");
        // One report, with both shares.
        var report = Assert.Single(lines);
        Assert.Contains("Your 2 eidolons", report);
        Assert.Contains($"{toMine.Count} blocks mined", report);
        Assert.Contains($"{o1.Mined} by eidolon {e1.EntityId}", report);
        Assert.Contains($"{o2.Mined} by eidolon {e2.EntityId}", report);
        // Each walked back to where it was ordered.
        Assert.True(e1.Pos.XYZ.HorizontalSquareDistanceTo(home1) <= 2.5 * 2.5, $"{e1.EntityId} at {e1.Pos.XYZ}, ordered at {home1}");
        Assert.True(e2.Pos.XYZ.HorizontalSquareDistanceTo(home2) <= 2.5 * 2.5, $"{e2.EntityId} at {e2.Pos.XYZ}, ordered at {home2}");
        Assert.Null(Jobs.Mining.JobOf(e1.EntityId));
        Assert.Null(Jobs.Mining.JobOf(e2.EntityId));
        e1.Die(EnumDespawnReason.Removed);
        e2.Die(EnumDespawnReason.Removed);
    }

    [AtlasScenario(TimeoutMs = 300_000)]
    public async Task One_ordered_later_joins_the_job_running()
    {
        var site = await Felling.Sky(World, World.Spawn.AddCopy(-720, 90, 1800), reach: 15);
        var blocks = W.BlockAccessor;
        int granite = W.GetBlock(new AssetLocation("game:rock-granite"))!.Id;
        Ground(site, granite);
        // 8 × 4 and two deep, its north edge on the terrace: two sectors of 4 × 4, one each (a sector is
        // at least 4 wide, so a pit 6 wide is one sector and the second waits at the rim).
        var a = site.AddCopy(-4, -2, -3);
        var b = site.AddCopy(3, -1, 0);
        var area = Area(a, b);
        var toMine = Cells(area);

        var (test, player, tool) = await Commander("eidolonlate", site.AddCopy(0, 0, -12));
        var e1 = await Spawn(site.AddCopy(-3, 0, -10), player);
        var e2 = await Spawn(site.AddCopy(3, 0, -10), player);
        e2.Orders!.ClearOrder();
        OrderMine(player, tool, [e1], a, b);
        var o1 = Assert.IsType<MineOrder>(e1.Orders!.Current);
        await World.Until(() => o1.Mined >= 6, 2000);
        var job = o1.Job;
        Assert.NotNull(job);
        Assert.False(job!.Closed);
        // The second is given the same area as the tool gives it, from where it stands.
        e2.Orders.SetOrder(MineOrder.OrderCode, MineOrder.Args(area, e2.Pos.XYZ));
        var o2 = Assert.IsType<MineOrder>(e2.Orders.Current);
        await World.Ticks(5);
        Assert.Same(job, o2.Job);
        Assert.Equal(2, job.Members.Count);
        await World.Until(() => e1.Orders.OrderCode == null && e2.Orders.OrderCode == null, 6000);
        var lines = Reports(o1, o2);
        Log($"after: {o1.Mined} + {o2.Mined}; reports: " + string.Join(" | ", lines));
        Assert.All(toMine, c => Assert.Equal(0, blocks.GetBlock(c).Id));
        Assert.Equal(toMine.Count, o1.Mined + o2.Mined);
        Assert.True(o2.Mined > 0, "the late one mined nothing");
        var report = Assert.Single(lines);
        Assert.Contains("Your 2 eidolons", report);
        e1.Die(EnumDespawnReason.Removed);
        e2.Die(EnumDespawnReason.Removed);
    }

    [AtlasScenario(TimeoutMs = 480_000)]
    public async Task One_whose_sector_is_mined_out_steals_the_far_half_of_anothers()
    {
        var site = await Felling.Sky(World, World.Spawn.AddCopy(-720, 90, 2200), reach: 15);
        var blocks = W.BlockAccessor;
        int granite = W.GetBlock(new AssetLocation("game:rock-granite"))!.Id;
        Ground(site, granite);
        // 16 × 2 × 8: two sectors of 8 × 8, the west one a layer only (its lower layer already dug)
        var a = site.AddCopy(-8, -2, -3);
        var b = site.AddCopy(7, -1, 4);
        var area = Area(a, b);
        for (int x = -8; x <= -1; x++)
        for (int z = -3; z <= 4; z++)
            blocks.SetBlock(0, site.AddCopy(x, -2, z));
        var toMine = Cells(area).Where(c => blocks.GetBlock(c).Id != 0).ToList();

        var (test, player, tool) = await Commander("eidolonsteal", site.AddCopy(0, 0, -12));
        var west = await Spawn(site.AddCopy(-5, 0, -10), player);
        var east = await Spawn(site.AddCopy(5, 0, -10), player);
        var eidolons = new[] { west, east };
        OrderMine(player, tool, eidolons, a, b);
        var orders = eidolons.Select(e => Assert.IsType<MineOrder>(e.Orders!.Current)).ToArray();
        await World.Ticks(5);
        var sectors = orders[0].Job!.Shared.Sectors;
        Log("sectors: " + string.Join(", ", sectors.All.Select(s => $"{s.Sector} by {s.Owner}")));
        Assert.Equal(new MineSector(a.X, a.Z, a.X + 7, b.Z), orders[0].Sector);

        var watch = new MineWatcher(W, toMine, eidolons, orders);
        MineSector? stolen = null, stealFar = null;
        int ticks = 0;
        await World.Until(() =>
        {
            watch.Tick();
            if (sectors.Steals > 0 && stolen == null)
            {
                stolen = sectors.Of(west.EntityId);
                // the east sector halved by where the east one stands now (a tick after the steal)
                stealFar = EidolonSectors.Halves(new MineSector(a.X + 8, a.Z, b.X, b.Z), east.Pos.X, east.Pos.Z)!.Value.Far;
                Log($"west stole {stolen}, east at {east.Pos.XYZ}; east keeps {sectors.Of(east.EntityId)}");
            }
            if (++ticks % 600 == 0)
                Log($"{orders[0].Mined} + {orders[1].Mined}: {State(west)} || {State(east)}");
            return eidolons.All(e => e.Orders!.OrderCode == null);
        }, 12000);
        var lines = Reports(orders);
        Log($"after: {orders[0].Mined} + {orders[1].Mined}; steals {sectors.Steals}; empty {orders.Sum(o => o.EmptyStrikes)}, cut {orders.Sum(o => o.CutStrikes)}; overlapping {watch.Overlaps} ticks; "
            + "reports: " + string.Join(" | ", lines));
        Log("faults: " + string.Join(" | ", watch.Faults.Take(30)) + "; overlapping: " + watch.Summary());
        Assert.Empty(watch.Faults);
        Assert.All(toMine, c => Assert.Equal(0, blocks.GetBlock(c).Id));
        Assert.True(sectors.Steals >= 1, "no steal");
        // the west one stole half the east sector, the half away from where the east one was
        Assert.NotNull(stolen);
        Assert.Equal(stealFar, stolen);
        Assert.Contains(watch.Broke[west.EntityId], c => c.X >= a.X + 8);
        Assert.All(watch.BySector.Values, v => Assert.Equal(0, v.Out));
        // the west one walked to its half and worked it apart from the east one, each standing in its own
        // (a shallow brush allowed, however long)
        Assert.Null(watch.Pushing());
        Assert.Equal(0, orders.Sum(o => o.EmptyStrikes));
        Assert.Single(lines);
        foreach (var e in eidolons)
            e.Die(EnumDespawnReason.Removed);
    }
}
