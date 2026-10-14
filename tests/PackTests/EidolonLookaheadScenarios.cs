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
/// in another), contacts brief and rare (none over 2 s, at most about one a member each 2 minutes), every
/// eidolon home after, and the job's makespan no later than today's dispatch's (<see cref="MineBaselines"/>,
/// measured with <c>SH_MINE_BASELINE=1</c>, which turns routes off) by more than <see cref="Tolerance"/>. The
/// log names which the route solver chose (a variant, or today's dispatch). CI shards
/// <c>eidolon-ahead-pit</c>, <c>eidolon-ahead-trench</c>, <c>eidolon-ahead-tunnel</c> and
/// <c>eidolon-ahead-spiral</c>. On floors of their own in the sky (<see cref="Felling.Sky"/>).
/// </summary>
[AtlasWorld]
public class EidolonLookaheadScenarios(ITestOutputHelper output) : AtlasScenarioBase
{
    /// <summary>How much later than today's baseline a crew by routes may finish (the server's timing varies
    /// from run to run by about this much).</summary>
    private const double Tolerance = 0.10;

    private IWorldAccessor W => World.Api.World;
    private EidolonSystem Mod => EidolonSystem.Of(World.Api)!;
    private EidolonJobSystem Jobs => EidolonJobSystem.Of(World.Api)!;

    private void Log(string what)
    {
        W.Logger.Notification("[eidolon-lookahead-test] " + what);
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
        await World.Until(() =>
        {
            watch.Tick();
            if (MineWatcher.ClaimFault(Jobs.Mining, crew, orders, EidolonJobSystem.Now(W)) is { } claimFault)
                faults.Add(claimFault);
            if (orders[0].Job?.Shared.Routes is { } routes
                && (routes.Today ? "today's dispatch" : $"variant {routes.VariantIndex}") is var choice
                && (choices.Count == 0 || choices[^1] != choice))
                choices.Add(choice);
            int mined = orders.Sum(o => o.Mined);
            if (mined != lastMined && mined % 40 == 0 || ++tick % 600 == 0)
            {
                Log($"{string.Join(" + ", orders.Select(o => o.Mined))} mined: {string.Join(" || ", crew.Select(State))}");
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
        Assert.Null(watch.Pushing());
        // all dug but what the plan keeps (the stair's way out), and every eidolon out of the pit and home
        Assert.All(left, c => Assert.True(kept.Contains(new MarkPos(c.X, c.Y, c.Z)), $"{c} left"));
        Assert.Equal(0, orders[0].Unreachable);
        for (int i = 0; i < crew.Length; i++)
        {
            Assert.True(crew[i].Pos.XYZ.HorizontalSquareDistanceTo(homes[i]) <= 2.5 * 2.5, $"{crew[i].EntityId} at {crew[i].Pos.XYZ}, ordered at {homes[i]}");
            Assert.Equal(homes[i].Y, crew[i].Pos.Y, 0.6);
        }
        // contacts brief and rare: none over 2 s, at most about one a member each 2 minutes of the job
        Assert.True(watch.LongestMs <= 2000, $"a contact of {watch.LongestMs} ms");
        Assert.True(watch.Contacts.Count <= crew.Length * (1 + seconds / 120), $"{watch.Contacts.Count} contacts in {seconds:0} s");
        if (EidolonMinePlans.Routing)
            Assert.True(seconds <= baseline * (1 + Tolerance), $"{seconds:0.0} s against today's {baseline:0.0} s");
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

    [AtlasScenario(TimeoutMs = 900_000)]
    public async Task Two_eidolons_dig_an_open_pit_no_later_than_today() => await Pit(2, 1000, MineBaselines.Pit2);

    [AtlasScenario(TimeoutMs = 900_000)]
    public async Task Three_eidolons_dig_an_open_pit_no_later_than_today() => await Pit(3, 1200, MineBaselines.Pit3);

    // ---- the trench: granite seven thick, a trench 16 long, 5 wide and 4 deep ----

    private async Task Trench(int crew, int z, double baseline)
    {
        var site = await Felling.Sky(World, World.Spawn.AddCopy(-1120, 90, z), reach: 20);
        Fill(site, Block("game:rock-granite"), -15, 19, -7, -1, -8, 12);
        BlockPos[] starts = [site.AddCopy(-6, 0, 0), site.AddCopy(-6, 0, 4), site.AddCopy(-9, 0, 2)];
        await Crew($"ahtrench{crew}", site, site.AddCopy(0, -4, 0), site.AddCopy(15, -1, 4), starts.Take(crew).ToArray(), baseline, 24000);
    }

    [AtlasScenario(TimeoutMs = 900_000)]
    public async Task Two_eidolons_dig_a_trench_no_later_than_today() => await Trench(2, 1400, MineBaselines.Trench2);

    [AtlasScenario(TimeoutMs = 900_000)]
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

    [AtlasScenario(TimeoutMs = 900_000)]
    public async Task Two_eidolons_finish_a_tall_tunnel_no_later_than_today() => await Tunnel(2, 1800, MineBaselines.Tunnel2);

    [AtlasScenario(TimeoutMs = 900_000)]
    public async Task Three_eidolons_finish_a_tall_tunnel_no_later_than_today() => await Tunnel(3, 2000, MineBaselines.Tunnel3);

    // ---- the spiral pit: cobblestone 14 thick, a pit 5 × 5 and 12 deep ----

    private async Task Spiral(int crew, int z, double baseline)
    {
        var site = await Felling.Sky(World, World.Spawn.AddCopy(-1120, 90, z), reach: 15);
        Fill(site, Block("game:cobblestone-granite"), -15, 15, -14, -1, -15, 15);
        BlockPos[] starts = [site.AddCopy(-4, 0, 0), site.AddCopy(-4, 0, 5), site.AddCopy(-7, 0, 2)];
        await Crew($"ahspiral{crew}", site, site.AddCopy(0, -12, 0), site.AddCopy(4, -1, 4), starts.Take(crew).ToArray(), baseline, 30000);
    }

    [AtlasScenario(TimeoutMs = 1_500_000)]
    public async Task Two_eidolons_dig_a_spiral_pit_no_later_than_today() => await Spiral(2, 2200, MineBaselines.Spiral2);

    [AtlasScenario(TimeoutMs = 1_500_000)]
    public async Task Three_eidolons_dig_a_spiral_pit_no_later_than_today() => await Spiral(3, 2400, MineBaselines.Spiral3);
}
