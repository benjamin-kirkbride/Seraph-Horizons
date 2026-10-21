using Atlas.Api;
using Atlas.XUnit;
using SeraphHorizons.Mod.Eidolon;
using SeraphHorizons.Mod.Eidolon.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Xunit.Abstractions;

namespace SeraphHorizons.PackTests;

/// <summary>
/// mods-src/seraphhorizons/Eidolon, eidolons waiting in a shared job (README "Eidolon", working
/// together), with the pack's default settings, ordered with the command tool as a player orders them.
/// An eidolon standing in the area with no order is never mined out from under, waited for, and its
/// squares mined once it moves, no pillar left; one with no square (mining) or tree (felling) free waits
/// at the area's rim, holding nothing; one dry when its job is reported goes back once oiled with no
/// report of its own; and three finding no way to a stand another waits by give it up after a few tries
/// and end the job, never waiting on it for good. The shared pit and the late joiner are <see cref="EidolonTeamMineScenarios"/>, the
/// shared felling <see cref="EidolonTeamFellScenarios"/>. On floors of their own in the sky
/// (<see cref="Felling.Sky"/>), apart from those classes' sites, each from its spawns on the virtual clock
/// (<see cref="VirtualClock"/>).
/// </summary>
[AtlasWorld]
public class EidolonTeamWaitScenarios(ITestOutputHelper output) : AtlasScenarioBase
{
    private IWorldAccessor W => World.Api.World;
    private EidolonSystem Mod => EidolonSystem.Of(World.Api)!;
    private EidolonJobSystem Jobs => EidolonJobSystem.Of(World.Api)!;
    private ItemAxe IronAxe => (ItemAxe)W.GetItem(new AssetLocation("game:axe-felling-iron"))!;

    private void Log(string what)
    {
        // as an argument: a mining order's state holds braces (PathCell { X = ... })
        W.Logger.Notification("[eidolon-teamwait-test] {0}", what);
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

    /// <summary>Binds the tool to each of <paramref name="eidolons"/>, picks <paramref name="mode"/> and
    /// marks the area between the two blocks, which gives every bound eidolon the order.</summary>
    private static void Order(IPlayer player, ItemSlot tool, string mode, IEnumerable<EntityLaborEidolon> eidolons, BlockPos a, BlockPos b)
    {
        foreach (var e in eidolons)
            RightClick(player, tool, entity: e);
        tool.Itemstack!.Collectible.SetToolMode(tool, player, null!, EidolonCommandModes.IndexOf(mode));
        RightClick(player, tool, block: a);
        RightClick(player, tool, block: b);
    }

    private static void OrderMine(IPlayer player, ItemSlot tool, IEnumerable<EntityLaborEidolon> eidolons, BlockPos a, BlockPos b) =>
        Order(player, tool, MineOrder.OrderCode, eidolons, a, b);

    private static void OrderFell(IPlayer player, ItemSlot tool, IEnumerable<EntityLaborEidolon> eidolons, BlockPos a, BlockPos b) =>
        Order(player, tool, FellOrder.OrderCode, eidolons, a, b);

    /// <summary>Gives it an axe as a player does: a right-click on it with the axe.</summary>
    private void GiveAxe(IServerPlayer player, EntityLaborEidolon e)
    {
        var slot = new DummySlot(new ItemStack(IronAxe));
        e.OnInteract(player.Entity, slot, e.Pos.XYZ.AddCopy(0, 2, 0), EnumInteractMode.Interact);
        Assert.True(slot.Empty, "it did not take the axe");
    }

    /// <summary>The reports the orders made (what the owner was told; Atlas's chat capture fails on the
    /// strikes' and the felling's particles).</summary>
    private static List<string> Reports(params MineOrder[] orders) => orders.Select(o => o.Reported).OfType<string>().ToList();

    private static List<string> FellReports(params FellOrder[] orders) => orders.Select(o => o.Reported).OfType<string>().ToList();

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

    private int Wood(BlockPos log) => Felling.Tree(W, IronAxe, log).Wood;

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

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task An_eidolon_standing_in_the_area_with_no_order_is_waited_for_and_its_squares_mined_once_it_moves()
    {
        var site = await Felling.Sky(World, World.Spawn.AddCopy(-720, 90, 1700), reach: 15);
        var blocks = W.BlockAccessor;
        int granite = W.GetBlock(new AssetLocation("game:rock-granite"))!.Id;
        Ground(site, granite);
        // 8 × 6 and two deep, its north edge on the terrace, the idle one over its middle: room for the
        // miner to work round it without crowding it off (a narrower area has it step aside).
        var a = site.AddCopy(-4, -2, -3);
        var b = site.AddCopy(3, -1, 2);
        var area = Area(a, b);
        var toMine = Cells(area);

        var (test, player, tool) = await Commander("eidolonbystander", site.AddCopy(0, 0, -12));
        using var clock = VirtualClock.Start(World);
        var miner = await Spawn(site.AddCopy(0, 0, -10), player);
        // Standing on the area's top, its box over columns -1 and 0 each way, with no order.
        var idle = await Spawn(site.AddCopy(0, 0, 0), player);
        idle.Orders!.ClearOrder();
        await World.Ticks(20);
        double idleY = idle.Pos.Y;
        OrderMine(player, tool, [miner], a, b);
        var order = Assert.IsType<MineOrder>(miner.Orders!.Current);
        Assert.Null(idle.Orders.OrderCode);

        // It mines all it may and waits: never a block under the idle one at or below its feet.
        var standing = toMine.ToHashSet();
        var faults = new List<string>();
        int ticks = 0;
        void Watch()
        {
            foreach (var c in standing.Where(c => blocks.GetBlock(c).Id == 0).ToList())
            {
                if (EidolonMining.Undermines(idle.Pos.X, Feet(idle), idle.Pos.Z, new MarkPos(c.X, c.Y, c.Z)))
                    faults.Add($"{c} broke under the idle one at {idle.Pos.XYZ}");
                standing.Remove(c);
            }
        }
        await World.Until(() =>
        {
            Watch();
            if (++ticks % 300 == 0)
                Log($"{order.Mined} mined: {State(miner)}");
            // every block left is under the idle one, and the miner waits for it
            return faults.Count > 0 || miner.Orders.OrderCode == null
                || (Status(miner).EndsWith("mine-waiting") && standing.All(c => EidolonMining.UnderFoot(idle.Pos.X, idle.Pos.Z, new MarkPos(c.X, c.Y, c.Z))));
        }, 8000);
        Log($"waiting: {order.Mined} mined, {standing.Count} left; idle at {idle.Pos.XYZ}; " + State(miner));
        Assert.Empty(faults);
        Assert.Equal(MineOrder.OrderCode, miner.Orders.OrderCode);
        Assert.NotEmpty(standing);
        Assert.Equal(idleY, idle.Pos.Y, 1);
        Assert.Equal(0, order.Unreachable);

        // The idle one walks off: the miner takes what was under it, and leaves no pillar.
        idle.TeleportTo(site.AddCopy(8, 0, -11).ToVec3d().Add(0.5, 0, 0.5));
        await World.Until(() => miner.Orders.OrderCode == null, 3000);
        Log($"after: {order.Mined} mined, {order.Unreachable} out of reach; {order.Reported}");
        Assert.All(toMine, c => Assert.Equal(0, blocks.GetBlock(c).Id));
        Assert.InRange((toMine.Count) - (order.Mined), -(order.Job?.Shared.Gone ?? 0), order.Job?.Shared.Gone ?? 0);
        Assert.Equal(0, order.Unreachable);
        Assert.Contains("0 out of its reach", order.Reported);
        miner.Die(EnumDespawnReason.Removed);
        idle.Die(EnumDespawnReason.Removed);
    }

    [AtlasScenario(TimeoutMs = 90_000)]
    public async Task Mining_one_with_no_square_free_waits_at_the_rim()
    {
        var site = await Felling.Sky(World, World.Spawn.AddCopy(-720, 90, 1900), reach: 12);
        var blocks = W.BlockAccessor;
        int granite = W.GetBlock(new AssetLocation("game:rock-granite"))!.Id;
        // A granite pillar 2 × 2 and 4 high on the floor: one square, so one works it at a time.
        var a = site.AddCopy(0, 0, 0);
        var b = site.AddCopy(1, 3, 1);
        var area = Area(a, b);
        var toMine = Cells(area);
        foreach (var c in toMine)
            blocks.SetBlock(granite, c);

        var (test, player, tool) = await Commander("eidolonspare", site.AddCopy(0, 0, -10));
        using var clock = VirtualClock.Start(World);
        var e1 = await Spawn(site.AddCopy(-2, 0, 1), player);
        var e2 = await Spawn(site.AddCopy(5, 0, 5), player);
        OrderMine(player, tool, [e1, e2], a, b);
        var o1 = Assert.IsType<MineOrder>(e1.Orders!.Current);
        var o2 = Assert.IsType<MineOrder>(e2.Orders!.Current);

        // Who waited, and whether it stood clear of the area once there.
        var waited = new HashSet<long>();
        var atRim = new HashSet<long>();
        var faults = new List<string>();
        int ticks = 0;
        await World.Until(() =>
        {
            foreach (var e in new[] { e1, e2 })
            {
                if (!Status(e).EndsWith("mine-waiting"))
                    continue;
                waited.Add(e.EntityId);
                if (e.Pos.Motion.HorLength() < 0.001 && !OverArea(area, e))
                    atRim.Add(e.EntityId);
                if (Jobs.Mining.Held(e.EntityId).Count > 0)
                    faults.Add($"{e.EntityId} waits holding {string.Join(" ", Jobs.Mining.Held(e.EntityId))}");
            }
            if (++ticks % 120 == 0)
                Log($"{o1.Mined} + {o2.Mined}: {State(e1)} || {State(e2)}");
            return e1.Orders.OrderCode == null && e2.Orders.OrderCode == null;
        }, 4000);
        var lines = Reports(o1, o2);
        Log($"after: {o1.Mined} + {o2.Mined}, waited {string.Join(",", waited)}, at the rim {string.Join(",", atRim)}; reports: " + string.Join(" | ", lines));
        Assert.Empty(faults);
        Assert.All(toMine, c => Assert.Equal(0, blocks.GetBlock(c).Id));
        Assert.NotEmpty(waited);
        Assert.NotEmpty(atRim);
        Assert.Single(lines);
        e1.Die(EnumDespawnReason.Removed);
        e2.Die(EnumDespawnReason.Removed);
    }

    [AtlasScenario(TimeoutMs = 90_000)]
    public async Task Felling_one_with_no_tree_free_waits_at_the_rim()
    {
        var site = await Felling.Sky(World, World.Spawn.AddCopy(-820, 90, 1700), reach: 14);
        // One oak (EidolonFellScenarios' first seed), on its ground: one tree for two.
        var logs = new List<BlockPos> { await Felling.Grow(World, site.AddCopy(0, 0, 0), Felling.Oak, 1.0f, 101) };
        if (W.BlockAccessor.GetBlock(logs[0].DownCopy()).Id == 0)
            World.SetBlock("game:soil-medium-normal", logs[0].DownCopy());
        await World.Ticks(5);
        Assert.True(Wood(logs[0]) >= Mod.Config.FellMinLogs, $"{Wood(logs[0])} logs");
        var a = site.AddCopy(-3, -1, -3);
        var b = site.AddCopy(3, -1, 3);
        var area = MarkArea.Between(new MarkPos(a.X, a.Y, a.Z), new MarkPos(b.X, b.Y, b.Z));

        var (_, player, tool) = await Commander("eidolonfellspare", site.AddCopy(0, 0, -12));
        using var clock = VirtualClock.Start(World);
        var e1 = await Spawn(site.AddCopy(-3, 0, -6), player);
        var e2 = await Spawn(site.AddCopy(8, 0, 8), player);
        GiveAxe(player, e1);
        GiveAxe(player, e2);
        OrderFell(player, tool, [e1, e2], a, b);
        var o1 = Assert.IsType<FellOrder>(e1.Orders!.Current);
        var o2 = Assert.IsType<FellOrder>(e2.Orders!.Current);

        // Who waited, whether it stood clear of the area once there, and never waiting holding a tree.
        var waited = new HashSet<long>();
        var atRim = new HashSet<long>();
        var faults = new List<string>();
        int ticks = 0;
        await World.Until(() =>
        {
            foreach (var e in new[] { e1, e2 })
            {
                if (!Status(e).EndsWith("fell-waiting"))
                    continue;
                waited.Add(e.EntityId);
                var f = EidolonMining.Footprint(e.Pos.X, e.Pos.Z);
                bool over = f.MaxX >= area.Min.X && f.MinX <= area.Max.X && f.MaxZ >= area.Min.Z && f.MinZ <= area.Max.Z;
                if (e.Pos.Motion.HorLength() < 0.001 && !over)
                    atRim.Add(e.EntityId);
                if (Jobs.Felling.Held(e.EntityId).Count > 0)
                    faults.Add($"{e.EntityId} waits holding {string.Join(" ", Jobs.Felling.Held(e.EntityId))}");
            }
            if (++ticks % 120 == 0)
                Log($"{o1.Felled} + {o2.Felled}: {State(e1)} || {State(e2)}");
            return e1.Orders.OrderCode == null && e2.Orders.OrderCode == null;
        }, 4000);
        var lines = FellReports(o1, o2);
        Log($"after: {o1.Felled} + {o2.Felled}, waited {string.Join(",", waited)}, at the rim {string.Join(",", atRim)}; reports: " + string.Join(" | ", lines));
        Assert.Empty(faults);
        Assert.Equal(0, Wood(logs[0]));
        Assert.Equal(1, o1.Felled + o2.Felled);
        Assert.NotEmpty(waited);
        Assert.NotEmpty(atRim);
        var report = Assert.Single(lines);
        Assert.Contains("Your 2 eidolons", report);
        Assert.Contains("1 felled", report);
        e1.Die(EnumDespawnReason.Removed);
        e2.Die(EnumDespawnReason.Removed);
    }

    /// <summary>
    /// No way to a stand with another eidolon waiting by it: the stand bad after a few tries all the same, and
    /// the job ends. A pillar 1 × 1 and 4 high stands on dry stone fences (whole blocks to the plan, as any
    /// block reaching half a block up; the game's pathfinder finds no way onto them from the granite round
    /// them), and the three are ordered from that granite; another eidolon, with no order, stands 2.8 blocks
    /// off the first stand planned (2 west and 2 north of the pillar), and stays. Each walks to the fences'
    /// edge and arrives short: once, with the other by the stand, the three took it, failed and took it again
    /// for good, with nothing in the log and the plan never made again. Now, with every stand round it bad,
    /// the job reports the pillar out of reach and ends.
    /// </summary>
    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Three_finding_no_way_to_a_stand_another_waits_by_give_it_up_and_end_the_job() => await NoWayByAnother(2200, routing: true);

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Three_finding_no_way_to_a_stand_another_waits_by_give_it_up_and_end_the_job_by_todays_dispatch() => await NoWayByAnother(2400, routing: false);

    private async Task NoWayByAnother(int z, bool routing)
    {
        bool was = EidolonMinePlans.Routing;
        EidolonMinePlans.Routing = routing;
        try
        {
            var site = await Felling.Sky(World, World.Spawn.AddCopy(-720, 90, z), reach: 15);
            var blocks = W.BlockAccessor;
            int granite = W.GetBlock(new AssetLocation("game:rock-granite"))!.Id;
            var fence = W.GetBlock(new AssetLocation("game:drystonefence-granite-nesw-free"));
            Assert.NotNull(fence);
            for (int x = -7; x <= 8; x++)
            for (int zz = -7; zz <= 8; zz++)
                blocks.SetBlock(x == 0 && zz == 0 ? granite : fence!.Id, site.AddCopy(x, -1, zz));
            var a = site.AddCopy(0, 0, 0);
            var b = site.AddCopy(0, 3, 0);
            var area = Area(a, b);
            var pillar = Cells(area);
            foreach (var c in pillar)
                blocks.SetBlock(granite, c);

            var (_, player, tool) = await Commander(routing ? "nowayroutes" : "nowaytoday", site.AddCopy(-14, 0, -1));
            using var clock = VirtualClock.Start(World);
            var other = await Spawn(site.AddCopy(-4, 1, 0), player);
            other.Orders!.ClearOrder();
            var crew = new[] { await Spawn(site.AddCopy(-11, 0, -3), player), await Spawn(site.AddCopy(-11, 0, 0), player), await Spawn(site.AddCopy(-11, 0, 3), player) };
            await World.Ticks(20);
            var otherAt = other.Pos.XYZ;
            OrderMine(player, tool, crew, a, b);
            var orders = crew.Select(e => Assert.IsType<MineOrder>(e.Orders!.Current)).ToArray();
            int ticks = 0;
            // five minutes of game time: unbounded, it never ended
            await World.Until(() =>
            {
                if (++ticks % 600 == 0)
                    Log($"plan {orders[0].Job?.Shared.PlanVersion}: {string.Join(" || ", crew.Select(e => e.Orders!.Current is MineOrder o ? $"{e.EntityId} {o.DebugState}" : $"{e.EntityId} done"))}");
                return crew.All(e => e.Orders!.OrderCode == null);
            }, 9000);
            var lines = Reports(orders);
            Log($"after: plan {orders[0].Job?.Shared.PlanVersion}, the other at {other.Pos.XYZ}; reports: {string.Join(" | ", lines)}");
            Assert.All(crew, e => Assert.True(e.Orders!.OrderCode == null, State(e)));
            Assert.True(other.Pos.XYZ.HorizontalSquareDistanceTo(otherAt) < 0.5 * 0.5, $"the other moved from {otherAt} to {other.Pos.XYZ}");
            Assert.All(pillar, c => Assert.NotEqual(0, blocks.GetBlock(c).Id));
            var report = Assert.Single(lines);
            Assert.Contains($"{pillar.Count} out of their reach", report);
            foreach (var e in crew)
                e.Die(EnumDespawnReason.Removed);
            other.Die(EnumDespawnReason.Removed);
        }
        finally
        {
            EidolonMinePlans.Routing = was;
        }
    }

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task One_dry_when_its_job_is_reported_goes_back_once_oiled_with_no_report_of_its_own()
    {
        var site = await Felling.Sky(World, World.Spawn.AddCopy(-720, 90, 2000), reach: 15);
        var blocks = W.BlockAccessor;
        int granite = W.GetBlock(new AssetLocation("game:rock-granite"))!.Id;
        Ground(site, granite);
        var a = site.AddCopy(-2, -2, -2);
        var b = site.AddCopy(1, -1, 1);
        var area = Area(a, b);

        var (test, player, tool) = await Commander("eidolondrymate", site.AddCopy(0, 0, -12));
        using var clock = VirtualClock.Start(World);
        var e1 = await Spawn(site.AddCopy(-3, 0, -10), player);
        var e2 = await Spawn(site.AddCopy(3, 0, -10), player);
        if (e2.Oil?.Tank == null)
            return; // MachineOil off: never dry.
        OrderMine(player, tool, [e1, e2], a, b);
        var o1 = Assert.IsType<MineOrder>(e1.Orders!.Current);
        var o2 = Assert.IsType<MineOrder>(e2.Orders!.Current);
        await World.Ticks(5);
        Assert.Same(o1.Job, o2.Job);
        var home2 = e2.Pos.XYZ;
        // The second runs dry before it reaches the area: away from the job, which the first finishes.
        e2.Oil.SetPoints(0);
        await World.Until(() => !e2.CanWork, 400);
        await World.Until(() => e1.Orders.OrderCode == null, 8000);
        Log($"first done: {o1.Mined} + {o2.Mined}; {o1.Reported}; second: {State(e2)}");
        Assert.All(Cells(area), c => Assert.Equal(0, blocks.GetBlock(c).Id));
        Assert.NotNull(o1.Reported);
        Assert.True(o1.Job!.Closed);
        // the job's report saved done on the second's order too
        Assert.True(e2.Orders.OrderArgs!.GetBool("done"));
        // Oiled, it goes back to its place and makes no report of its own.
        e2.Oil.SetPoints(Mod.Config.OilTank / 2);
        await World.Until(() => e2.Orders.OrderCode == null, 4000);
        Log($"second after: {o2.Mined}, {o2.Reported ?? "no report"}; at {e2.Pos.XYZ}, ordered at {home2}");
        Assert.Null(o2.Reported);
        Assert.Equal(0, o2.Mined);
        Assert.True(e2.Pos.XYZ.HorizontalSquareDistanceTo(home2) <= 2.5 * 2.5, $"at {e2.Pos.XYZ}, ordered at {home2}");
        e1.Die(EnumDespawnReason.Removed);
        e2.Die(EnumDespawnReason.Removed);
    }
}
