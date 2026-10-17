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
/// mods-src/seraphhorizons/Eidolon, a crew mining by look-ahead routes (README "Eidolon", working together,
/// *Routes*), two and three eidolons on the cases the routes are measured on in xunit (MineRouteBenchmarkTests):
/// an open pit 16 × 3 × 12 with a terrace, a trench 16 long, 5 wide and 4 deep, a tunnel 8 high and 4 wide into
/// a stepped slope, and a pit 5 × 5 and 12 deep with its spiral stair. Each is dug out (but what the plan keeps
/// for good), with no fault (no block broken in another's columns, no claim struck under another, no box deep
/// in another), every eidolon home after, contacts held to the rule where routes ran (none over 2 s, at most
/// about one a member each 2 minutes; see <see cref="Crew"/> for a job left to today's dispatch), and the job's
/// makespan no later than today's dispatch's (<see cref="MineBaselines"/>, measured with
/// <c>SH_MINE_BASELINE=1</c>, which turns routes off) by more than <see cref="Tolerance"/>: a bound for the
/// spread runs still have on the virtual clock; the strict "no worse than today" is
/// MineRouteBenchmarkTests' in xunit, in simulated time. The log names each choice the route solver made in
/// turn (a variant, today's dispatch, or today's after its routes stalled). CI shard <c>eidolon-ahead</c>. On
/// floors of their own in the sky (<see cref="Felling.Sky"/>), each from its spawns on the virtual clock
/// (<see cref="VirtualClock"/>).
/// </summary>
[AtlasWorld]
public class EidolonLookaheadScenarios(ITestOutputHelper output) : AtlasScenarioBase
{
    /// <summary>How much later than today's baseline a crew by routes may finish here. On the virtual clock a
    /// case repeats closely but not exactly: today's own runs spread up to 5 % over their mean (the pit of
    /// three, 153 to 169 s), and the pit of three by routes is bimodal, most runs 133 to 156 s and a few 175 to
    /// 187 s (a member's walks stopped short and waiting behind another), so this catches a crew gone wrong,
    /// not that spread; xunit holds the routes to today exactly.</summary>
    private const double Tolerance = 0.20;

    /// <summary>How much later than its baseline a job left to today's dispatch may finish: that is today
    /// against today, its own spread (the tunnel of three, 105 to 142 s), only a crew gone badly wrong.</summary>
    private const double TodayTolerance = 0.35;

    /// <summary>How long (game time) every order left walking home may go with none of them moving
    /// <see cref="StallMove"/> blocks before the crew counts as stalled.</summary>
    private const long StallMs = 60_000;

    private const double StallMove = 0.5;

    private IWorldAccessor W => World.Api.World;
    private EidolonSystem Mod => EidolonSystem.Of(World.Api)!;
    private EidolonJobSystem Jobs => EidolonJobSystem.Of(World.Api)!;

    private void Log(string what)
    {
        // as an argument: a contact's description holds braces (PathCell { X = ... })
        W.Logger.Notification("[eidolon-lookahead-test] {0}", what);
        output.WriteLine(what);
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
        var p = await World.JoinRetrying(name);
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

    private static void OrderMine(IPlayer player, ItemSlot tool, IEnumerable<EntityLaborEidolon> eidolons, BlockPos a, BlockPos b)
    {
        foreach (var e in eidolons)
            RightClick(player, tool, entity: e);
        tool.Itemstack!.Collectible.SetToolMode(tool, player, null!, EidolonCommandModes.IndexOf(MineOrder.OrderCode));
        RightClick(player, tool, block: a);
        RightClick(player, tool, block: b);
    }

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

    private int Block(string code) => W.GetBlock(new AssetLocation(code))!.Id;

    private static string State(EntityLaborEidolon e) => EidolonCommands.Describe(e) + " / " + e.GetInfoText().Replace('\n', ' ');

    /// <summary>
    /// A crew of <paramref name="starts"/>.Length mines <paramref name="a"/> to <paramref name="b"/>: watched each
    /// tick until every order ends; then all dug but what the plan keeps, no fault, contacts within the rule,
    /// every eidolon home, and its makespan held to <paramref name="baseline"/> (logged for the baseline when
    /// routes are off).
    /// </summary>
    private async Task Crew(string name, BlockPos site, BlockPos a, BlockPos b, BlockPos[] starts, double baseline, int ticks)
    {
        var toMine = Cells(site, a.X - site.X, b.X - site.X, a.Y - site.Y, b.Y - site.Y, a.Z - site.Z, b.Z - site.Z)
            .Where(c => W.BlockAccessor.GetBlock(c).Id != 0).ToList();
        var (player, tool) = await Commander(name, starts[0].AddCopy(-3, 0, -1));
        // from the spawns on, game time on the virtual clock: makespans and contacts the same on every runner,
        // a route solve (like a plan) costing none (MinePlanHold)
        using var clock = VirtualClock.Start(World);
        var eidolons = new List<EntityLaborEidolon>();
        foreach (var from in starts)
            eidolons.Add(await Spawn(from, player));
        OrderMine(player, tool, eidolons, a, b);
        var orders = eidolons.Select(e => Assert.IsType<MineOrder>(e.Orders!.Current)).ToArray();
        var homes = eidolons.Select(e => e.Pos.XYZ).ToArray();
        var crew = eidolons.ToArray();
        var watch = new MineWatcher(W, toMine, crew, orders, c => orders[0].Kept.Contains(c) || orders[0].Plan?.Stair.Contains(c) == true, floors: false);
        var faults = new List<string>();
        // Each distinct choice in turn (a job's first solve, then any new station plan's).
        var choices = new List<string>();
        int tick = 0, lastMined = -1;
        long start = W.ElapsedMilliseconds;
        // where each still under its order last moved from, and when: every one left walking home and none
        // getting anywhere for StallMs is a stall (#971, two parting for ever on the way out of the spiral pit),
        // failed then, with where they stand, rather than at the tick limit
        var moved = new Dictionary<long, (Vec3d At, long Since)>();
        await World.Until(() =>
        {
            long now = W.ElapsedMilliseconds;
            var going = crew.Select((e, i) => (e, o: orders[i])).Where(m => m.e.Orders!.Current == m.o).ToList();
            if (going.Count > 0 && going.All(m => m.o.Returning))
            {
                foreach (var (e, _) in going)
                    if (!moved.TryGetValue(e.EntityId, out var last) || last.At.DistanceTo(e.Pos.XYZ) > StallMove)
                        moved[e.EntityId] = (e.Pos.XYZ, now);
                if (going.All(m => now - moved[m.e.EntityId].Since > StallMs))
                    Assert.Fail($"every order left walking home and none moved {StallMove} blocks in {StallMs / 1000} s: "
                        + string.Join(" | ", going.Select(m => $"{m.e.EntityId} at {m.e.Pos.XYZ}, ordered at {homes[Array.IndexOf(crew, m.e)]}: {m.o.DebugState}")));
            }
            else
                moved.Clear();
            watch.Tick();
            if (MineWatcher.ClaimFault(Jobs.Mining, crew, orders, EidolonJobSystem.Now(W)) is { } claimFault)
                faults.Add(claimFault);
            if (orders[0].Job?.Shared is { Routes: { } routes } cells
                && (routes.Today ? "today's dispatch"
                    : cells.RoutesStalledPlan == cells.PlanVersion ? "today's dispatch (stalled)"
                    : $"variant {routes.VariantIndex}") is var choice
                && (choices.Count == 0 || choices[^1] != choice))
                choices.Add(choice);
            int mined = orders.Sum(o => o.Mined);
            if (mined != lastMined && mined % 40 == 0 || ++tick % 600 == 0)
            {
                Log($"{string.Join(" + ", orders.Select(o => o.Mined))} mined; {string.Join(" | ", orders.Select((o, i) =>
                        $"{crew[i].EntityId}: {(crew[i].Orders!.Current == o ? o.DebugState : "order over")}"))}: "
                    + string.Join(" || ", crew.Select(State)));
                lastMined = mined;
            }
            return crew.All(e => e.Orders!.OrderCode == null);
        }, ticks);
        double seconds = (W.ElapsedMilliseconds - start) / 1000.0;
        var left = toMine.Where(c => W.BlockAccessor.GetBlock(c).Id != 0).ToList();
        var kept = orders[0].Kept;
        Log($"{name}: {(EidolonMinePlans.Routing ? "routes" : "BASELINE (today's dispatch)")}, chosen {(choices.Count == 0 ? "none" : string.Join(" then ", choices))}; makespan {seconds:0.0} s "
            + $"(baseline {baseline:0.0} s); mined {string.Join(" + ", orders.Select(o => o.Mined))} of {toMine.Count}, left {left.Count}; "
            + $"contacts {watch.Contacts.Count}, longest {watch.LongestMs} ms; empty {orders.Sum(o => o.EmptyStrikes)}, cut {orders.Sum(o => o.CutStrikes)}");
        Log("faults: " + string.Join(" | ", faults.Concat(watch.Faults).Take(30)) + "; contacts: " + watch.Summary());
        Assert.Empty(faults);
        Assert.Empty(watch.Faults);
        // all dug but what the plan keeps (the stair's way out), and every eidolon out of the pit and home
        Assert.All(left, c => Assert.True(kept.Contains(new MarkPos(c.X, c.Y, c.Z)), $"{c} left"));
        Assert.Equal(0, orders[0].Unreachable);
        for (int i = 0; i < crew.Length; i++)
        {
            Assert.True(crew[i].Pos.XYZ.HorizontalSquareDistanceTo(homes[i]) <= 2.5 * 2.5, $"{crew[i].EntityId} at {crew[i].Pos.XYZ}, ordered at {homes[i]}");
            Assert.Equal(homes[i].Y, crew[i].Pos.Y, 0.6);
        }
        // Contacts: by routes the whole job, brief and rare, none over 2 s, at most about one a member each 2
        // minutes, and none deep (MineWatcher.Pushing). Where any of the job ran on today's dispatch (the
        // solver's choice, its routes stalled, or routes off) its contacts are today's own, as they were before
        // routes (its trench of three brushed for 5 s once, 7.1 s in another run, and once came 0.87 apart as all
        // three climbed out together to go home), so only a loose bound holds: none over 10 s, and no faults
        // (above) either way.
        bool routed = EidolonMinePlans.Routing && choices.Count > 0 && choices.All(c => c.StartsWith("variant"));
        if (routed)
        {
            Assert.Null(watch.Pushing());
            Assert.True(watch.LongestMs <= 2000, $"a contact of {watch.LongestMs} ms");
            Assert.True(watch.Contacts.Count <= crew.Length * (1 + seconds / 120), $"{watch.Contacts.Count} contacts in {seconds:0} s");
        }
        else
            Assert.True(watch.LongestMs <= 10000, $"a contact of {watch.LongestMs} ms on today's dispatch");
        // Makespan: by routes the whole job, within Tolerance of today's; left to today's dispatch (by the
        // solver or a stall) it is today's own time, held only to TodayTolerance (today's tunnel of three ran
        // 105 to 142 s on the virtual clock)
        if (EidolonMinePlans.Routing)
            Assert.True(seconds <= baseline * (1 + (routed ? Tolerance : TodayTolerance)),
                $"{seconds:0.0} s against today's {baseline:0.0} s{(routed ? "" : " on today's dispatch")}");
        foreach (var e in crew)
            e.Die(EnumDespawnReason.Removed);
    }

    // ---- the open pit: granite four thick, a terrace a block lower north of it, the pit 16 × 3 × 12 ----

    private async Task Pit(int crew, int z, double baseline)
    {
        var site = await Felling.Sky(World, World.Spawn.AddCopy(-1120, 90, z), reach: 15);
        int granite = Block("game:rock-granite");
        for (int x = -14; x <= 14; x++)
        for (int zz = -14; zz <= 14; zz++)
        for (int y = -4; y <= -1; y++)
            W.BlockAccessor.SetBlock(y == -1 && zz is >= -10 and <= -7 ? 0 : granite, site.AddCopy(x, y, zz));
        BlockPos[] starts = [site.AddCopy(-6, 0, -12), site.AddCopy(6, 0, -12), site.AddCopy(0, 0, -12)];
        await Crew($"ahpit{crew}", site, site.AddCopy(-8, -3, -6), site.AddCopy(7, -1, 5), starts.Take(crew).ToArray(), baseline, 24000);
    }

    [AtlasScenario(TimeoutMs = 240_000)]
    public async Task Two_eidolons_dig_an_open_pit_no_later_than_today() => await Pit(2, 1000, MineBaselines.Pit2);

    [AtlasScenario(TimeoutMs = 240_000)]
    public async Task Three_eidolons_dig_an_open_pit_no_later_than_today() => await Pit(3, 1200, MineBaselines.Pit3);

    // ---- the trench: granite seven thick, a trench 16 long, 5 wide and 4 deep ----

    private async Task Trench(int crew, int z, double baseline)
    {
        var site = await Felling.Sky(World, World.Spawn.AddCopy(-1120, 90, z), reach: 20);
        Fill(site, Block("game:rock-granite"), -15, 19, -7, -1, -8, 12);
        BlockPos[] starts = [site.AddCopy(-6, 0, 0), site.AddCopy(-6, 0, 4), site.AddCopy(-9, 0, 2)];
        await Crew($"ahtrench{crew}", site, site.AddCopy(0, -4, 0), site.AddCopy(15, -1, 4), starts.Take(crew).ToArray(), baseline, 24000);
    }

    [AtlasScenario(TimeoutMs = 240_000)]
    public async Task Two_eidolons_dig_a_trench_no_later_than_today() => await Trench(2, 1400, MineBaselines.Trench2);

    [AtlasScenario(TimeoutMs = 240_000)]
    public async Task Three_eidolons_dig_a_trench_no_later_than_today() => await Trench(3, 1600, MineBaselines.Trench3);

    // ---- the tall tunnel: 8 high and 4 wide into a stepped slope ----

    private async Task Tunnel(int crew, int z, double baseline)
    {
        var site = await Felling.Sky(World, World.Spawn.AddCopy(-1120, 90, z), reach: 15);
        int granite = Block("game:rock-granite");
        Fill(site, granite, 0, 12, 0, 11, -3, 6);
        Fill(site, granite, -2, -1, 0, 3, -3, 6);
        Fill(site, granite, -4, -3, 0, 1, -3, 6);
        BlockPos[] starts = [site.AddCopy(-6, 0, 0), site.AddCopy(-6, 0, 4), site.AddCopy(-9, 0, 2)];
        await Crew($"ahtunnel{crew}", site, site.AddCopy(-2, 0, 0), site.AddCopy(3, 7, 3), starts.Take(crew).ToArray(), baseline, 24000);
    }

    [AtlasScenario(TimeoutMs = 240_000)]
    public async Task Two_eidolons_finish_a_tall_tunnel_no_later_than_today() => await Tunnel(2, 1800, MineBaselines.Tunnel2);

    [AtlasScenario(TimeoutMs = 240_000)]
    public async Task Three_eidolons_finish_a_tall_tunnel_no_later_than_today() => await Tunnel(3, 2000, MineBaselines.Tunnel3);

    // ---- the spiral pit: cobblestone 14 thick, a pit 5 × 5 and 12 deep ----

    private async Task Spiral(int crew, int z, double baseline)
    {
        var site = await Felling.Sky(World, World.Spawn.AddCopy(-1120, 90, z), reach: 15);
        Fill(site, Block("game:cobblestone-granite"), -15, 15, -14, -1, -15, 15);
        BlockPos[] starts = [site.AddCopy(-4, 0, 0), site.AddCopy(-4, 0, 5), site.AddCopy(-7, 0, 2)];
        await Crew($"ahspiral{crew}", site, site.AddCopy(0, -12, 0), site.AddCopy(4, -1, 4), starts.Take(crew).ToArray(), baseline, 30000);
    }

    [AtlasScenario(TimeoutMs = 300_000)]
    public async Task Two_eidolons_dig_a_spiral_pit_no_later_than_today() => await Spiral(2, 2200, MineBaselines.Spiral2);

    [AtlasScenario(TimeoutMs = 300_000)]
    public async Task Three_eidolons_dig_a_spiral_pit_no_later_than_today() => await Spiral(3, 2400, MineBaselines.Spiral3);
}
