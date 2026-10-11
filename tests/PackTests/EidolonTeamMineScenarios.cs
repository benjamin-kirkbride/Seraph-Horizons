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
/// orders them. Two given the same area never hold the same column, take the pit down a layer at a
/// time together, report once with both shares and walk back each to its own place; one ordered later
/// joins the job running; three on one area each work a sector of their own, apart, never overlapping, the
/// floors within a climb, with no slam at nothing; one whose sector is mined out steals half of another's;
/// two ordered across each other's way walk round each other; and two drive a tunnel 4 wide a heading
/// each. Waiting (for a square, for a bystander, dry) is <see cref="EidolonTeamWaitScenarios"/>.
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

    private static bool OverArea(MarkArea area, EntityLaborEidolon e)
    {
        var f = EidolonMining.Footprint(e.Pos.X, e.Pos.Z);
        return f.MaxX >= area.Min.X && f.MinX <= area.Max.X && f.MaxZ >= area.Min.Z && f.MinZ <= area.Max.Z;
    }

    private static string Status(EntityLaborEidolon e) =>
        e.WatchedAttributes.GetTreeAttribute(EntityBehaviorEidolonOrders.StatusKey)?.GetString("key") ?? "";

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

    /// <summary>Whether two eidolons' boxes (1.7 wide) overlap.</summary>
    private static bool Overlap(Entity a, Entity b) => Math.Abs(a.Pos.X - b.Pos.X) < 1.7 && Math.Abs(a.Pos.Z - b.Pos.Z) < 1.7
        && Math.Abs(a.Pos.Y - b.Pos.Y) < 3.75;

    /// <summary>Watches each tick who broke what: a block gone this tick is the one whose strike's group
    /// held it; counts those in its sector and out, overlapping boxes, and a block broken more than a layer
    /// under the highest left (the floors more than a climb apart).</summary>
    private sealed class Watcher(IWorldAccessor w, List<BlockPos> toMine, EntityLaborEidolon[] eidolons, MineOrder[] orders)
    {
        private readonly HashSet<BlockPos> _standing = toMine.ToHashSet();
        /// <summary>Floors more than a climb apart.</summary>
        public readonly List<string> Faults = new();

        /// <summary>Every 30th tick of two overlapping, so it says when.</summary>
        public readonly List<string> Contacts = new();
        public readonly Dictionary<long, (int In, int Out)> BySector = eidolons.ToDictionary(e => e.EntityId, _ => (0, 0));
        public readonly Dictionary<long, List<BlockPos>> Broke = eidolons.ToDictionary(e => e.EntityId, _ => new List<BlockPos>());
        public int Overlaps;
        public double DistanceSum;
        public int DistanceTicks;

        public void Tick()
        {
            var broke = _standing.Where(c => w.BlockAccessor.GetBlock(c).Id == 0).ToList();
            foreach (var c in broke)
            {
                _standing.Remove(c);
                var cell = new MarkPos(c.X, c.Y, c.Z);
                for (int i = 0; i < eidolons.Length; i++)
                {
                    if (orders[i].Group?.Cells.Contains(cell) != true)
                        continue;
                    var (inside, outside) = BySector[eidolons[i].EntityId];
                    BySector[eidolons[i].EntityId] = orders[i].Sector is { } s && s.Contains(c.X, c.Z) ? (inside + 1, outside) : (inside, outside + 1);
                    Broke[eidolons[i].EntityId].Add(c);
                }
            }
            if (broke.Count > 0 && _standing.Count > 0 && broke.Min(c => c.Y) < _standing.Max(c => c.Y) - 1)
                Faults.Add($"{string.Join(" ", broke)} broke with {_standing.Max(c => c.Y)} left");
            for (int i = 0; i < eidolons.Length; i++)
            for (int j = i + 1; j < eidolons.Length; j++)
            {
                if (Overlap(eidolons[i], eidolons[j]) && Overlaps++ % 30 == 0)
                    Contacts.Add($"{Describe(i)} and {Describe(j)}");
                // apart while all are at work (none reported, none on its way back)
                if (orders.All(o => o.Reported == null) && eidolons.All(e => e.Orders?.OrderCode == MineOrder.OrderCode))
                {
                    DistanceSum += Math.Sqrt(eidolons[i].Pos.SquareHorDistanceTo(eidolons[j].Pos.XYZ));
                    DistanceTicks++;
                }
            }
        }

        private string Describe(int i) =>
            $"{eidolons[i].EntityId} at {eidolons[i].Pos.XYZ} ({eidolons[i].Orders?.OrderCode}, {Status(eidolons[i])})";

        public double MeanDistance => DistanceTicks == 0 ? 0 : DistanceSum / DistanceTicks;
    }

    [AtlasScenario(TimeoutMs = 600_000)]
    public async Task Three_eidolons_work_a_sector_each_apart_the_floors_within_a_climb_and_no_slam_at_nothing()
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
        var sectors = orders[0].Job!.Shared.Sectors;
        Log("sectors: " + string.Join(", ", sectors.All.Select(s => $"{s.Sector} by {s.Owner}")));
        Assert.Equal(3, sectors.All.Count(s => s.Owner != null));

        var watch = new Watcher(W, toMine, eidolons, orders);
        int ticks = 0, lastMined = -1;
        await World.Until(() =>
        {
            watch.Tick();
            int mined = orders.Sum(o => o.Mined);
            if (mined != lastMined && mined % 24 == 0 || ++ticks % 600 == 0)
            {
                Log($"{string.Join(" + ", orders.Select(o => o.Mined))} mined, sectors {string.Join(" ", orders.Select(o => o.Sector?.ToString() ?? "none"))}, "
                    + $"mean apart {watch.MeanDistance:0.0}, overlaps {watch.Overlaps}: {string.Join(" || ", eidolons.Select(State))}");
                lastMined = mined;
            }
            return eidolons.All(e => e.Orders!.OrderCode == null);
        }, 24000);
        var lines = Reports(orders);
        Log($"after: {string.Join(" + ", orders.Select(o => o.Mined))}; steals {sectors.Steals}; in/out of sector "
            + string.Join(" ", watch.BySector.Select(kv => $"{kv.Key}: {kv.Value.In}/{kv.Value.Out}"))
            + $"; empty strikes {orders.Sum(o => o.EmptyStrikes)}, cut {orders.Sum(o => o.CutStrikes)}; mean apart {watch.MeanDistance:0.00}; overlaps {watch.Overlaps}");
        Log("reports: " + string.Join(" | ", lines));
        Log("faults: " + string.Join(" | ", watch.Faults.Take(30)) + "; overlapping: " + string.Join(" | ", watch.Contacts.Take(10)));
        Assert.Empty(watch.Faults);
        Assert.All(toMine, c => Assert.Equal(0, blocks.GetBlock(c).Id));
        Assert.Equal(toMine.Count, orders.Sum(o => o.Mined));
        Assert.All(orders, o => Assert.True(o.Mined > 0, $"one mined nothing: {string.Join(" + ", orders.Select(x => x.Mined))}"));
        // Each broke in its own sector, and they kept apart: well over two boxes' width on average,
        // never one inside another.
        Assert.All(watch.BySector.Values, v => Assert.Equal(0, v.Out));
        Assert.True(watch.MeanDistance >= 5, $"mean distance apart {watch.MeanDistance:0.00}");
        Assert.Equal(0, watch.Overlaps);
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

        var watch = new Watcher(W, toMine, eidolons, orders);
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
        Log("faults: " + string.Join(" | ", watch.Faults.Take(30)) + "; overlapping: " + string.Join(" | ", watch.Contacts.Take(10)));
        Assert.Empty(watch.Faults);
        Assert.All(toMine, c => Assert.Equal(0, blocks.GetBlock(c).Id));
        Assert.True(sectors.Steals >= 1, "no steal");
        // the west one stole half the east sector, the half away from where the east one was
        Assert.NotNull(stolen);
        Assert.Equal(stealFar, stolen);
        Assert.Contains(watch.Broke[west.EntityId], c => c.X >= a.X + 8);
        Assert.All(watch.BySector.Values, v => Assert.Equal(0, v.Out));
        Assert.Equal(0, orders.Sum(o => o.EmptyStrikes));
        Assert.Single(lines);
        foreach (var e in eidolons)
            e.Die(EnumDespawnReason.Removed);
    }

    [AtlasScenario(TimeoutMs = 300_000)]
    public async Task Two_ordered_across_each_others_way_walk_round_each_other_and_both_arrive()
    {
        var site = await Felling.Sky(World, World.Spawn.AddCopy(-720, 90, 2300), reach: 15);
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
            if (Overlap(west, east))
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

    [AtlasScenario(TimeoutMs = 300_000)]
    public async Task Two_eidolons_drive_a_tunnel_4_wide_a_2_wide_heading_each_side_by_side()
    {
        var site = await Felling.Sky(World, World.Spawn.AddCopy(-720, 90, 2400), reach: 14);
        var blocks = W.BlockAccessor;
        int granite = W.GetBlock(new AssetLocation("game:rock-granite"))!.Id;
        // A hill of granite (x 0 to 10, seven high, z -3 to 6), a cliff face to the west onto the floor.
        for (int x = 0; x <= 10; x++)
        for (int y = 0; y <= 6; y++)
        for (int z = -3; z <= 6; z++)
            blocks.SetBlock(granite, site.AddCopy(x, y, z));
        // A tunnel 4 wide, 4 high and 8 long into it: two headings, z 0 to 1 and 2 to 3.
        var a = site.AddCopy(0, 0, 0);
        var b = site.AddCopy(7, 3, 3);
        var area = Area(a, b);
        var toMine = Cells(area);

        var (_, player, tool) = await Commander("eidolontunnels2", site.AddCopy(-10, 0, 2));
        var e1 = await Spawn(site.AddCopy(-6, 0, 0), player);
        var e2 = await Spawn(site.AddCopy(-6, 0, 4), player);
        OrderMine(player, tool, [e1, e2], a, b);
        var o1 = Assert.IsType<MineOrder>(e1.Orders!.Current);
        var o2 = Assert.IsType<MineOrder>(e2.Orders!.Current);
        var home1 = e1.Pos.XYZ;
        var home2 = e2.Pos.XYZ;

        // Watch each tick: each strikes only in its own heading, never a column held by both, nothing
        // broken under either's box; and how near they come.
        var standing = toMine.ToHashSet();
        var faults = new List<string>();
        var headings = new HashSet<string>();
        double nearest = double.MaxValue;
        int ticks = 0, lastMined = -1;
        await World.Until(() =>
        {
            if (Jobs.Mining.Held(e1.EntityId).Intersect(Jobs.Mining.Held(e2.EntityId)).Any())
                faults.Add("both hold a column");
            foreach (var (o, e) in new[] { (o1, e1), (o2, e2) })
            {
                if (o.Driving && o.Sector is { } sector)
                    headings.Add($"{e.EntityId} {sector}");
                if (o.Driving && o.Group is { } g && !g.Cells.All(c => o.Sector is { } s && s.Contains(c.X, c.Z)))
                    faults.Add($"{e.EntityId} strikes {string.Join(" ", g.Cells)} out of its heading {o.Sector}");
            }
            var broke = standing.Where(c => blocks.GetBlock(c).Id == 0).ToList();
            foreach (var c in broke)
            {
                foreach (var e in new[] { e1, e2 })
                    if (EidolonMining.Undermines(e.Pos.X, Feet(e), e.Pos.Z, new MarkPos(c.X, c.Y, c.Z)) && c.Y < e.Pos.Y)
                        faults.Add($"{c} broke under {e.EntityId} at {e.Pos.XYZ}");
                standing.Remove(c);
            }
            if (OverArea(area, e1) && OverArea(area, e2))
                nearest = Math.Min(nearest, Math.Sqrt(e1.Pos.XYZ.HorizontalSquareDistanceTo(e2.Pos.XYZ)));
            if (o1.Mined + o2.Mined != lastMined || ++ticks % 300 == 0)
            {
                Log($"{o1.Mined} + {o2.Mined} mined, at {e1.Pos.XYZ} and {e2.Pos.XYZ}: {State(e1)} || {State(e2)}");
                lastMined = o1.Mined + o2.Mined;
            }
            return e1.Orders.OrderCode == null && e2.Orders.OrderCode == null;
        }, 9000);
        var lines = Reports(o1, o2);
        Log("reports: " + string.Join(" | ", lines));
        Log($"headings: {string.Join(", ", headings)}; nearest in the tunnel {nearest:0.00}");
        Log($"after: {o1.Mined} + {o2.Mined}, {o1.Unreachable} + {o2.Unreachable} out of reach, {o1.EmptyStrikes} + {o2.EmptyStrikes} empty; standing: "
            + string.Join(" ", toMine.Where(c => blocks.GetBlock(c).Id != 0).Select(c => $"({c.X - site.X},{c.Y - site.Y},{c.Z - site.Z})")));
        Assert.Empty(faults);
        Assert.Equal((1, 0), (o1.Tunnel!.DriveX, o1.Tunnel.DriveZ));
        Assert.All(toMine, c => Assert.Equal(0, blocks.GetBlock(c).Id));
        Assert.Equal(toMine.Count, o1.Mined + o2.Mined);
        Assert.Equal(0, o1.Unreachable + o2.Unreachable);
        Assert.Equal(0, o1.EmptyStrikes + o2.EmptyStrikes);
        // A heading each, the nearer: e1 the north one (z 0 to 1), e2 the south; both worked theirs.
        Assert.Contains($"{e1.EntityId} ({a.X},{a.Z})-({b.X},{a.Z + 1})", headings);
        Assert.Contains($"{e2.EntityId} ({a.X},{a.Z + 2})-({b.X},{b.Z})", headings);
        Assert.True(o1.Mined > 0 && o2.Mined > 0, $"{o1.Mined} and {o2.Mined}");
        // Side by side, never in each other's way: their boxes (1.7 wide) never overlapped.
        Assert.True(nearest >= 1.6, $"nearest {nearest}");
        var report = Assert.Single(lines);
        Assert.Contains("Your 2 eidolons", report);
        Assert.True(e1.Pos.XYZ.HorizontalSquareDistanceTo(home1) <= 2.5 * 2.5, $"{e1.EntityId} at {e1.Pos.XYZ}, ordered at {home1}");
        Assert.True(e2.Pos.XYZ.HorizontalSquareDistanceTo(home2) <= 2.5 * 2.5, $"{e2.EntityId} at {e2.Pos.XYZ}, ordered at {home2}");
        e1.Die(EnumDespawnReason.Removed);
        e2.Die(EnumDespawnReason.Removed);
    }
}
