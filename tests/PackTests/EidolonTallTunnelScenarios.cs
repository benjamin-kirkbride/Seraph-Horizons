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
/// mods-src/seraphhorizons/Eidolon, tunnels taller than it reaches and a pit deeper than it climbs
/// (README "Eidolon", mining, *Stations*), with the pack's default settings, ordered with the command tool
/// as a player orders them: a tunnel 6 high into a slope from a step against its face, and one 8 high into
/// a stepped slope, each finished, its top band before the layers under it, out by the mouth; one 6 and
/// one 8 high into a flat face, its top out of reach told and kept with the floor under it, the rest dug;
/// two eidolons on a tunnel 8 (and 6) high and 4 wide into a stepped slope, each a real share, neither in
/// the other's columns, their contacts held as their job was worked (<see cref="MineWatcher.ContactFault"/>:
/// the solver may leave the 8 high to today's dispatch); a pit five deep with no shelf, dug with a stair it leaves as its way out; and two on
/// a pit 5 × 5 and 12 deep (the least that needs one), too deep for a straight stair, both climbing out by
/// the spiral one round its walls. The tunnels are 4 long, the least that shows each case, to keep the
/// class within CI's shards (ci.yml: `eidolon-tall`, `eidolon-tall-team`, `eidolon-tall-team-6`, `eidolon-spiral`). The
/// geometry is that of the planner's tests (MinePlannerTests), shortened. On floors of their own in the sky
/// (<see cref="Felling.Sky"/>). Each order runs on the virtual clock (<see cref="VirtualClock"/>), from the
/// order to its end, its mine plans holding game time still; the site, the commander and the spawn before it
/// in real time. Tick bounds and the seconds logged are game time.
/// </summary>
[AtlasWorld]
public class EidolonTallTunnelScenarios(ITestOutputHelper output) : AtlasScenarioBase
{
    private IWorldAccessor W => World.Api.World;
    private EidolonSystem Mod => EidolonSystem.Of(World.Api)!;
    private EidolonJobSystem Jobs => EidolonJobSystem.Of(World.Api)!;

    private void Log(string what)
    {
        W.Logger.Notification("[eidolon-talltunnel-test] " + what);
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

    private static List<string> Reports(params MineOrder[] orders) => orders.Select(o => o.Reported).OfType<string>().ToList();

    private static string State(EntityLaborEidolon e) => EidolonCommands.Describe(e) + " / " + e.GetInfoText().Replace('\n', ' ');

    private static List<BlockPos> Cells(BlockPos site, int x0, int x1, int y0, int y1, int z0, int z1)
    {
        var cells = new List<BlockPos>();
        for (int x = x0; x <= x1; x++)
        for (int y = y0; y <= y1; y++)
        for (int z = z0; z <= z1; z++)
            cells.Add(site.AddCopy(x, y, z));
        return cells;
    }

    private void Fill(BlockPos site, int block, int x0, int x1, int y0, int y1, int z0, int z1)
    {
        foreach (var c in Cells(site, x0, x1, y0, y1, z0, z1))
            W.BlockAccessor.SetBlock(block, c);
    }

    private int Granite => W.GetBlock(new AssetLocation("game:rock-granite"))!.Id;

    private static string Rel(BlockPos site, BlockPos c) => $"({c.X - site.X},{c.Y - site.Y},{c.Z - site.Z})";

    /// <summary>Its plan as the log has it: stations, what is out of reach, kept, the way out, and how many
    /// times it was made again.</summary>
    private static string PlanOf(MineOrder o) => o.Plan is { } p
        ? $"{p.Stations.Count} stations, {p.OutOfReach.Count} out of reach, {p.Held.Count} kept under those, {p.Stair.Count} stair ({p.WayOut.Count} way out), {p.Reruns} reruns"
        : "no plan";

    /// <summary>
    /// One eidolon mines <paramref name="toMine"/> (<paramref name="a"/> to <paramref name="b"/>), ordered from
    /// <paramref name="home"/>: watched each tick (nothing broken under its box at any height, and
    /// <paramref name="fault"/> for each block as it breaks, with what still stands), until its order ends.
    /// </summary>
    private async Task<(EntityLaborEidolon E, MineOrder Order, List<string> Faults, Vec3d Ordered, double Seconds)> MineAlone(
        string name, BlockPos site, BlockPos home, BlockPos a, BlockPos b, List<BlockPos> toMine, Action<BlockPos, HashSet<BlockPos>, List<string>>? fault = null,
        int ticks = 12000)
    {
        var (_, player, tool) = await Commander(name, site.AddCopy(-10, 0, 1));
        var e = await Spawn(home, player);
        using var clock = VirtualClock.Start(World);
        OrderMine(player, tool, [e], a, b);
        Assert.Equal(MineOrder.OrderCode, e.Orders!.OrderCode);
        var order = Assert.IsType<MineOrder>(e.Orders.Current);
        var ordered = e.Pos.XYZ;
        var standing = toMine.ToHashSet();
        var faults = new List<string>();
        int tick = 0, lastMined = -1;
        long start = W.ElapsedMilliseconds;
        await World.Until(() =>
        {
            foreach (var c in standing.Where(c => W.BlockAccessor.GetBlock(c).Id == 0).ToList())
            {
                standing.Remove(c);
                // a block gone with no strike of its own: the game's rock caving in, say
                if (order.Group?.Cells.Contains(new MarkPos(c.X, c.Y, c.Z)) != true)
                    Log($"{Rel(site, c)} gone, not in its group, it at {e.Pos.XYZ}");
                if (EidolonMining.UnderFoot(e.Pos.X, e.Pos.Z, new MarkPos(c.X, c.Y, c.Z)))
                    faults.Add($"{Rel(site, c)} broke under it at {e.Pos.XYZ}");
                fault?.Invoke(c, standing, faults);
            }
            if (order.Mined != lastMined || ++tick % 300 == 0)
            {
                Log($"{order.Mined} mined, {order.Strikes} strikes, at {e.Pos.XYZ}: " + State(e));
                lastMined = order.Mined;
            }
            return e.Orders.OrderCode == null;
        }, ticks);
        double seconds = (W.ElapsedMilliseconds - start) / 1000.0;
        Log($"after {seconds:0} s: {order.Mined} mined, {order.Strikes} strikes, {order.Unreachable} out of reach, {order.EmptyStrikes} empty, "
            + $"{order.CutStrikes} cut; plan: {PlanOf(order)}; told: {order.ToldOutOfReach}; reported: {order.Reported}; " + State(e));
        Log("standing: " + string.Join(" ", toMine.Where(c => W.BlockAccessor.GetBlock(c).Id != 0).Select(c => Rel(site, c))));
        return (e, order, faults, ordered, seconds);
    }

    private static void AssertHome(EntityLaborEidolon e, Vec3d ordered)
    {
        Assert.True(e.Pos.XYZ.HorizontalSquareDistanceTo(ordered) <= 2.5 * 2.5, $"{e.EntityId} at {e.Pos.XYZ}, ordered at {ordered}");
        Assert.Equal(ordered.Y, e.Pos.Y, 0.6);
    }

    [AtlasScenario(TimeoutMs = 240_000)]
    public async Task It_finishes_a_tunnel_six_high_into_a_slope_from_a_step_against_the_face()
    {
        var site = await Felling.Sky(World, World.Spawn.AddCopy(-920, 90, 1600), reach: 15);
        // A hill of granite (x 0 to 12, ten high, z -3 to 4), a step two high against its face (x -2, -1),
        // outside the area; the tunnel 2 wide, 6 high and 4 long.
        Fill(site, Granite, 0, 12, 0, 9, -3, 4);
        Fill(site, Granite, -2, -1, 0, 1, -3, 4);
        var a = site.AddCopy(0, 0, 0);
        var b = site.AddCopy(3, 5, 1);
        var toMine = Cells(site, 0, 3, 0, 5, 0, 1);
        var (e, order, faults, ordered, _) = await MineAlone("eidolontall6", site, site.AddCopy(-6, 0, 0), a, b, toMine, (c, standing, f) =>
        {
            // the top band (layers 2 to 5) before any of the layers under it
            if (c.Y - site.Y <= 1 && standing.Any(o => o.Y - site.Y >= 2))
                f.Add($"{Rel(site, c)} broke with the top band standing");
        });
        Assert.Empty(faults);
        Assert.Null(e.Orders!.OrderCode);
        Assert.All(toMine, c => Assert.Equal(0, W.BlockAccessor.GetBlock(c).Id));
        Assert.Equal(toMine.Count, order.Mined + (order.Job?.Shared.Gone ?? 0));
        Assert.Equal(0, order.Unreachable);
        Assert.Equal(0, order.EmptyStrikes);
        Assert.Null(order.ToldOutOfReach);
        Assert.Contains("0 out of its reach", order.Reported);
        // the hill round the tunnel stands
        Assert.Equal(Granite, W.BlockAccessor.GetBlock(site.AddCopy(3, 6, 0)).Id);
        Assert.Equal(Granite, W.BlockAccessor.GetBlock(site.AddCopy(3, 2, 2)).Id);
        AssertHome(e, ordered);
        e.Die(EnumDespawnReason.Removed);
    }

    [AtlasScenario(TimeoutMs = 240_000)]
    public async Task It_finishes_a_tunnel_eight_high_into_a_stepped_slope()
    {
        var site = await Felling.Sky(World, World.Spawn.AddCopy(-920, 90, 1800), reach: 15);
        // A hill (x 0 to 12, twelve high, z -3 to 4), stepped: a step four high (x -2, -1) in the area, one
        // two high (x -4, -3) in front of it; the tunnel 8 high from the step's foot.
        Fill(site, Granite, 0, 12, 0, 11, -3, 4);
        Fill(site, Granite, -2, -1, 0, 3, -3, 4);
        Fill(site, Granite, -4, -3, 0, 1, -3, 4);
        var a = site.AddCopy(-2, 0, 0);
        var b = site.AddCopy(3, 7, 1);
        // the area's blocks (over the step, x -2 and -1 above layer 3, is air)
        var toMine = Cells(site, -2, 3, 0, 7, 0, 1).Where(c => W.BlockAccessor.GetBlock(c).Id != 0).ToList();
        var (e, order, faults, ordered, _) = await MineAlone("eidolontall8", site, site.AddCopy(-6, 0, 0), a, b, toMine, (c, standing, f) =>
        {
            // band 0 (the hill's layers 4 to 7) all before any of the step is dug: it stands on the step for it
            if (c.X - site.X < 0 && standing.Any(o => o.X - site.X >= 0 && o.Y - site.Y >= 4))
                f.Add($"{Rel(site, c)} of the step broke with band 0 standing");
        });
        Assert.Empty(faults);
        Assert.Null(e.Orders!.OrderCode);
        Assert.All(toMine, c => Assert.Equal(0, W.BlockAccessor.GetBlock(c).Id));
        Assert.Equal(toMine.Count, order.Mined + (order.Job?.Shared.Gone ?? 0));
        Assert.Equal(0, order.Unreachable);
        Assert.Equal(0, order.EmptyStrikes);
        Assert.Contains("0 out of its reach", order.Reported);
        AssertHome(e, ordered);
        e.Die(EnumDespawnReason.Removed);
    }

    [AtlasScenario(TimeoutMs = 240_000)]
    public async Task It_reports_the_top_of_a_tunnel_six_high_into_a_flat_face_out_of_reach_and_keeps_the_floor_under_it() =>
        await FlatFace(6, 2000);

    [AtlasScenario(TimeoutMs = 240_000)]
    public async Task It_reports_the_top_of_a_tunnel_eight_high_into_a_flat_face_out_of_reach_and_keeps_the_floor_under_it() =>
        await FlatFace(8, 2200);

    /// <summary>Granite ground four thick; a hill x 0 to 10, z -3 to 4, on it; the area from a pit two deep in
    /// front of the face (x -4 to -1) into the hill (x 0 to 7), <paramref name="high"/> high over the ground.</summary>
    private async Task FlatFace(int high, int z)
    {
        var site = await Felling.Sky(World, World.Spawn.AddCopy(-920, 90, z), reach: 15);
        Fill(site, Granite, -15, 15, -4, -1, -15, 15);
        Fill(site, Granite, 0, 10, 0, 11, -3, 4);
        var a = site.AddCopy(-4, -2, 0);
        var b = site.AddCopy(7, high - 1, 1);
        var toMine = Cells(site, -4, -1, -2, -1, 0, 1);
        var covered = Cells(site, 0, 7, -2, high - 1, 0, 1);
        var outOfReach = Cells(site, 0, 7, 4, high - 1, 0, 1).Select(c => new MarkPos(c.X, c.Y, c.Z)).ToHashSet();
        var (e, order, faults, ordered, _) = await MineAlone($"eidolonflat{high}", site, site.AddCopy(-6, 0, 0), a, b, toMine.Concat(covered).ToList());
        Assert.Empty(faults);
        Assert.Null(e.Orders!.OrderCode);
        // the pit in front mined, every covered block still there
        Assert.All(toMine, c => Assert.Equal(0, W.BlockAccessor.GetBlock(c).Id));
        Assert.All(covered, c => Assert.Equal(Granite, W.BlockAccessor.GetBlock(c).Id));
        Assert.Equal(toMine.Count, order.Mined + (order.Job?.Shared.Gone ?? 0));
        Assert.Equal(outOfReach, order.Plan!.OutOfReach.ToHashSet());
        Assert.Equal(high == 6 ? 32 : 64, order.Unreachable);
        Assert.Equal(96, order.Plan.Held.Count);
        // told once, with how high a step would bring the top in reach, and reported
        Assert.NotNull(order.ToldOutOfReach);
        Assert.Contains($"{outOfReach.Count} blocks", order.ToldOutOfReach);
        Assert.Contains("96 blocks under them", order.ToldOutOfReach);
        Assert.Contains($"A step {high - 4} high", order.ToldOutOfReach);
        Assert.Contains($"{outOfReach.Count} out of its reach", order.Reported);
        Assert.Contains("96 kept under those as their floor", order.Reported);
        AssertHome(e, ordered);
        e.Die(EnumDespawnReason.Removed);
    }

    [AtlasScenario(TimeoutMs = 360_000)]
    public async Task Two_eidolons_finish_a_tunnel_eight_high_four_wide_into_a_stepped_slope() => await TwoOnAStepped(8, 2400);

    [AtlasScenario(TimeoutMs = 360_000)]
    public async Task Two_eidolons_finish_a_tunnel_six_high_four_wide_into_a_stepped_slope() => await TwoOnAStepped(6, 2600);

    /// <summary>The stepped slope 4 wide (the hill and its steps z -3 to 6, the tunnel z 0 to 3), two eidolons
    /// ordered from (-6, 0, 0) and (-6, 0, 4).</summary>
    private async Task TwoOnAStepped(int high, int z)
    {
        var site = await Felling.Sky(World, World.Spawn.AddCopy(-920, 90, z), reach: 15);
        Fill(site, Granite, 0, 12, 0, high + 3, -3, 6);
        Fill(site, Granite, -2, -1, 0, 3, -3, 6);
        Fill(site, Granite, -4, -3, 0, 1, -3, 6);
        var a = site.AddCopy(-2, 0, 0);
        var b = site.AddCopy(3, high - 1, 3);
        var toMine = Cells(site, -2, 3, 0, high - 1, 0, 3).Where(c => W.BlockAccessor.GetBlock(c).Id != 0).ToList();
        var (_, player, tool) = await Commander($"eidolontwotall{high}", site.AddCopy(-10, 0, 2));
        var e1 = await Spawn(site.AddCopy(-6, 0, 0), player);
        var e2 = await Spawn(site.AddCopy(-6, 0, 4), player);
        var eidolons = new[] { e1, e2 };
        using var clock = VirtualClock.Start(World);
        OrderMine(player, tool, eidolons, a, b);
        var orders = eidolons.Select(e => Assert.IsType<MineOrder>(e.Orders!.Current)).ToArray();
        var homes = eidolons.Select(e => e.Pos.XYZ).ToArray();
        var watch = new MineWatcher(W, toMine, eidolons, orders, floors: false);
        var faults = new List<string>();
        int ticks = 0, lastMined = -1;
        long start = W.ElapsedMilliseconds;
        await World.Until(() =>
        {
            watch.Tick();
            if (MineWatcher.ClaimFault(Jobs.Mining, eidolons, orders, EidolonJobSystem.Now(W)) is { } claimFault)
                faults.Add(claimFault);
            int mined = orders.Sum(o => o.Mined);
            if (mined != lastMined || ++ticks % 300 == 0)
            {
                Log($"{orders[0].Mined} + {orders[1].Mined} mined, at {e1.Pos.XYZ} and {e2.Pos.XYZ}: {State(e1)} || {State(e2)}");
                lastMined = mined;
            }
            return eidolons.All(e => e.Orders!.OrderCode == null);
        }, 24000);
        double seconds = (W.ElapsedMilliseconds - start) / 1000.0;
        var lines = Reports(orders);
        Log($"after {seconds:0} s: {orders[0].Mined} + {orders[1].Mined} of {toMine.Count}; plan: {PlanOf(orders[0])}; "
            + $"empty {orders.Sum(o => o.EmptyStrikes)}, cut {orders.Sum(o => o.CutStrikes)}; reports: " + string.Join(" | ", lines));
        Log("faults: " + string.Join(" | ", faults.Concat(watch.Faults).Take(30)) + "; worked by " + string.Join(" then ", watch.Choices) + "; overlapping: " + watch.Summary());
        Log("standing: " + string.Join(" ", toMine.Where(c => W.BlockAccessor.GetBlock(c).Id != 0).Select(c => Rel(site, c))));
        Assert.Empty(faults);
        Assert.Empty(watch.Faults);
        Assert.All(toMine, c => Assert.Equal(0, W.BlockAccessor.GetBlock(c).Id));
        Assert.Equal(toMine.Count, orders.Sum(o => o.Mined) + (orders[0].Job?.Shared.Gone ?? 0));
        Assert.Equal(0, orders[0].Unreachable);
        // each a real share: none idled the whole job
        Assert.All(orders, o => Assert.True(o.Mined >= toMine.Count / 4, $"{orders[0].Mined} and {orders[1].Mined} of {toMine.Count}"));
        Assert.Null(watch.ContactFault());
        var report = Assert.Single(lines);
        Assert.Contains("Your 2 eidolons", report);
        for (int i = 0; i < 2; i++)
            AssertHome(eidolons[i], homes[i]);
        foreach (var e in eidolons)
            e.Die(EnumDespawnReason.Removed);
    }

    [AtlasScenario(TimeoutMs = 240_000)]
    public async Task It_digs_a_stair_out_of_a_pit_five_deep_and_leaves_it()
    {
        var site = await Felling.Sky(World, World.Spawn.AddCopy(-920, 90, 2800), reach: 15);
        // Granite seven thick; a pit 6 × 6 and five deep with no shelf, ordered from the west.
        Fill(site, Granite, -15, 15, -7, -1, -15, 15);
        var a = site.AddCopy(0, -5, 0);
        var b = site.AddCopy(5, -1, 5);
        var toMine = Cells(site, 0, 5, -5, -1, 0, 5);
        var (e, order, faults, ordered, _) = await MineAlone("eidolonstair", site, site.AddCopy(-6, 0, 2), a, b, toMine);
        var plan = order.Plan!;
        var left = toMine.Where(c => W.BlockAccessor.GetBlock(c).Id != 0).Select(c => new MarkPos(c.X, c.Y, c.Z)).ToHashSet();
        Assert.Empty(faults);
        Assert.Null(e.Orders!.OrderCode);
        Assert.NotEmpty(plan.Stair);
        Assert.NotEmpty(plan.WayOut);
        // all dug but some of its stair, its way out, which it left and climbed out by
        Assert.NotEmpty(left);
        Assert.Subset(order.Stair.ToHashSet(), left);
        Assert.Equal(toMine.Count - left.Count, order.Mined + (order.Job?.Shared.Gone ?? 0));
        Assert.Equal(0, order.Unreachable);
        Assert.Contains($"{left.Count} left as its way out", order.Reported);
        AssertHome(e, ordered);
        e.Die(EnumDespawnReason.Removed);
    }

    [AtlasScenario(TimeoutMs = 480_000)]
    public async Task Two_eidolons_dig_a_pit_too_deep_for_a_straight_stair_and_climb_out_by_its_spiral()
    {
        var site = await Felling.Sky(World, World.Spawn.AddCopy(-920, 90, 3000), reach: 15);
        // Cobblestone 14 thick (half granite's resistance, so the scenario fits its CI shard; granite's caving
        // in under a lone tread is the five-deep pit's); a pit 5 × 5 and 12 deep, the least too deep for a
        // straight stair across it: a spiral one
        // round its walls, which both climb out by.
        Fill(site, W.GetBlock(new AssetLocation("game:cobblestone-granite"))!.Id, -15, 15, -14, -1, -15, 15);
        var a = site.AddCopy(0, -12, 0);
        var b = site.AddCopy(4, -1, 4);
        var toMine = Cells(site, 0, 4, -12, -1, 0, 4);
        var (_, player, tool) = await Commander("eidolonspiral2", site.AddCopy(-10, 0, 4));
        var e1 = await Spawn(site.AddCopy(-4, 0, 0), player);
        var e2 = await Spawn(site.AddCopy(-4, 0, 5), player);
        var eidolons = new[] { e1, e2 };
        using var clock = VirtualClock.Start(World);
        OrderMine(player, tool, eidolons, a, b);
        var orders = eidolons.Select(e => Assert.IsType<MineOrder>(e.Orders!.Current)).ToArray();
        var homes = eidolons.Select(e => e.Pos.XYZ).ToArray();
        var watch = new MineWatcher(W, toMine, eidolons, orders, c => orders[0].Kept.Contains(c) || orders[0].Plan?.Stair.Contains(c) == true);
        var faults = new List<string>();
        int ticks = 0, lastMined = -1;
        long start = W.ElapsedMilliseconds;
        await World.Until(() =>
        {
            watch.Tick();
            if (MineWatcher.ClaimFault(Jobs.Mining, eidolons, orders, EidolonJobSystem.Now(W)) is { } claimFault)
                faults.Add(claimFault);
            int mined = orders.Sum(o => o.Mined);
            if (mined != lastMined && mined % 40 == 0 || ++ticks % 600 == 0)
            {
                Log($"{orders[0].Mined} + {orders[1].Mined} mined, at {e1.Pos.XYZ} and {e2.Pos.XYZ}: {State(e1)} || {State(e2)}");
                lastMined = mined;
            }
            return eidolons.All(e => e.Orders!.OrderCode == null);
        }, 30000);
        double seconds = (W.ElapsedMilliseconds - start) / 1000.0;
        var plan = orders[0].Plan!;
        var lines = Reports(orders);
        var left = toMine.Where(c => W.BlockAccessor.GetBlock(c).Id != 0).Select(c => new MarkPos(c.X, c.Y, c.Z)).ToHashSet();
        Log($"after {seconds:0} s: {orders[0].Mined} + {orders[1].Mined} of {toMine.Count}; plan: {PlanOf(orders[0])}; "
            + $"empty {orders.Sum(o => o.EmptyStrikes)}, cut {orders.Sum(o => o.CutStrikes)}; reports: " + string.Join(" | ", lines));
        Log("faults: " + string.Join(" | ", faults.Concat(watch.Faults).Take(30)) + "; worked by " + string.Join(" then ", watch.Choices) + "; overlapping: " + watch.Summary());
        Log("left: " + string.Join(" ", left.Select(c => $"({c.X - site.X},{c.Y - site.Y},{c.Z - site.Z})")));
        Assert.Empty(faults);
        Assert.Empty(watch.Faults);
        Assert.NotEmpty(orders[0].Stair);
        Assert.Equal(0, orders[0].Unreachable);
        // only the stair's way out left, both worked, both out by it and home
        Assert.NotEmpty(left);
        Assert.Subset(orders[0].Stair.ToHashSet(), left);
        Assert.All(orders, o => Assert.True(o.Mined > 0, $"{orders[0].Mined} and {orders[1].Mined}"));
        Assert.Equal(toMine.Count - left.Count, orders.Sum(o => o.Mined) + (orders[0].Job?.Shared.Gone ?? 0));
        var report = Assert.Single(lines);
        Assert.Contains($"{left.Count} left as their way out", report);
        Assert.Null(watch.ContactFault());
        for (int i = 0; i < 2; i++)
            AssertHome(eidolons[i], homes[i]);
        foreach (var e in eidolons)
            e.Die(EnumDespawnReason.Removed);
    }
}
