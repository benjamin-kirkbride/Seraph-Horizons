using Atlas.Api;
using Atlas.XUnit;
using Newtonsoft.Json;
using SeraphHorizons.Mod.Eidolon;
using SeraphHorizons.Mod.Eidolon.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Xunit.Abstractions;

namespace SeraphHorizons.PackTests;

/// <summary>
/// mods-src/seraphhorizons/Eidolon, three eidolons at the foot of a cliff on a player's mining area
/// (fixtures/eidolon-cliff, a WorldEdit export of it, bare rock, soil, gravel and ore only): the ground rises
/// from the foot at y 116 to about 127, then a sheer face of monzonite to a hilltop at 138 to 144 that runs on
/// past the area's far side, which no walk from the foot reaches. The window's side touches the hilltop, and the walk graph took
/// every place on the window's side as joined to every other through the world outside (README "Eidolon",
/// mining, *Stations*), so the plan dug the area from the hilltop down, and the game's pathfinder found no way
/// to any of its stands: each one found bad and the plan made again with another, every two seconds, nothing
/// dug, for good. A stand found bad that is joined to the window's side but not, in the window, to where the
/// one that set off for it stands now shuts those ways out by the side too (<see cref="MineWalk.SideWays"/>):
/// the hilltop is out of reach (it reaches 3 over its feet, so from the slope nothing over 134), and the crew
/// digs what it reaches from the foot, to the job's end (done means done: every block of the area left is one
/// the plan kept), finding one stand bad and starting within <see cref="FirstStrikeWithin"/>, on the virtual clock
/// (<see cref="VirtualClock"/>). In the sky (<see cref="Felling.Sky"/>), the export placed whole.
/// </summary>
[AtlasWorld]
public class EidolonCliffFootScenarios(ITestOutputHelper output) : AtlasScenarioBase
{
    private IWorldAccessor W => World.Api.World;
    private EidolonSystem Mod => EidolonSystem.Of(World.Api)!;

    // The export's own corner (its OriginalPos), the area and where the three stood in the player's world.
    private static readonly BlockPos Original = new(511940, 110, 512005);
    private static readonly BlockPos AreaFrom = new(511954, 116, 512009), AreaTo = new(511973, 143, 512033);
    private static readonly Vec3d[] Starts = [new(511949.77, 116, 512028.90), new(511949.8, 116, 512031.0), new(511949.8, 116, 512033.6)];

    /// <summary>The game time it may take, from the order, to finish (it took 527 s on this branch, SH_CLOCK_SEED 1).</summary>
    private const double Minutes = 14;

    /// <summary>The most game seconds from the first plan to the first block broken (20 s on this branch: the walk
    /// up the slope to the first station). On main none was broken, ever.</summary>
    private const double FirstStrikeWithin = 40;

    /// <summary>The most stands the game may find no way to: the first one on the hilltop a member set off for; none
    /// after it is taken. On main, 161 in 16 minutes of game time.</summary>
    private const int MostBadStands = 1;

    /// <summary>The least it digs: what the plan reaches from the foot (1512 blocks in the planner's tests,
    /// tests/Eidolon/MineCliffFootTests), less a few the game takes by itself.</summary>
    private const int Least = 1400;

    /// <summary>How long none may break a block while the job runs before the run stops as stuck (game time).</summary>
    private const double StuckSeconds = 90;

    private void Log(string what)
    {
        W.Logger.Notification("[eidolon-cliff-test] {0}", what);
        output.WriteLine(what);
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

    [AtlasScenario(TimeoutMs = 240_000)]
    public async Task Three_at_a_cliffs_foot_dig_what_they_reach_from_it_and_never_make_for_the_hilltop()
    {
        string file = Path.Combine(Path.GetDirectoryName(typeof(EidolonCliffFootScenarios).Assembly.Location)!,
            "fixtures", "eidolon-cliff", "cliff.json");
        var schematic = JsonConvert.DeserializeObject<BlockSchematic>(File.ReadAllText(file))!;
        var centre = World.Spawn.AddCopy(720, 0, 1600);
        centre.Y = W.BlockAccessor.MapSizeY - 2 - 44;
        var site = await Felling.Sky(World, centre, reach: 40);
        // the export's corner, so that its 51 x 41 lie round the cleared site
        var corner = site.AddCopy(-25, 0, -20);
        BlockPos At(BlockPos p) => new(corner.X + p.X - Original.X, corner.Y + p.Y - Original.Y, corner.Z + p.Z - Original.Z);
        Vec3d AtV(Vec3d p) => new(corner.X + p.X - Original.X, corner.Y + p.Y - Original.Y, corner.Z + p.Z - Original.Z);
        schematic.Init(W.BlockAccessor);
        schematic.Place(W.BlockAccessor, W, corner, EnumReplaceMode.ReplaceAll, true);
        await World.Ticks(20);
        var from = At(AreaFrom);
        var to = At(AreaTo);
        var area = MarkArea.Between(new MarkPos(from.X, from.Y, from.Z), new MarkPos(to.X, to.Y, to.Z));
        var cells = RealMineRun.Cells(area);
        int blocks = cells.Count(c => W.BlockAccessor.GetBlock(c).Id != 0);

        var p = await World.JoinRetrying("eidolonclifffoot");
        var player = (IServerPlayer)p.Player;
        player.WorldData.CurrentGameMode = EnumGameMode.Creative;
        await p.TeleportTo(At(new BlockPos(511945, 125, 512030)));
        var tool = player.InventoryManager.ActiveHotbarSlot;
        tool.Itemstack = new ItemStack(W.GetItem(new AssetLocation(EidolonCommanderSystem.CommanderCode)));
        tool.MarkDirty();

        using var clock = VirtualClock.Start(World);
        var eidolons = new List<EntityLaborEidolon>();
        foreach (var at in Starts)
        {
            var e = Mod.Spawn(W, AtV(at), 0, player, activate: true);
            Assert.NotNull(e);
            await World.Until(() => e!.CanWork, 400);
            eidolons.Add(e!);
        }
        foreach (var e in eidolons)
            RightClick(player, tool, entity: e);
        tool.Itemstack!.Collectible.SetToolMode(tool, player, null!, EidolonCommandModes.IndexOf(MineOrder.OrderCode));
        RightClick(player, tool, block: from);
        RightClick(player, tool, block: to);
        await World.Ticks(2);
        var orders = eidolons.Select(e => Assert.IsType<MineOrder>(e.Orders!.Current)).ToList();
        var job = orders[0].Job;
        Assert.NotNull(job);

        double start = EidolonJobSystem.Now(W), end = start + Minutes * 60;
        double? planned = null, firstStrike = null;
        MinePlan? seen = null;
        int plans = 0, ticks = 0, lastMined = 0;
        double lastBreak = start;
        bool stuck = false;
        await World.Until(() =>
        {
            double now = EidolonJobSystem.Now(W);
            var shared = job!.Shared;
            if (shared.Plan is { } plan && plan != seen)
            {
                seen = plan;
                plans++;
                planned ??= now;
                Log($"{now - start:0} s: plan {plans}: {plan.Stations.Count} stations, {plan.Targets.Count} to dig, {plan.OutOfReach.Count} out of reach, "
                    + $"{plan.Held.Count} kept under those, {plan.Stair.Count} stair, {plan.Reruns} reruns; {shared.BadStandsFound} bad stands found so far");
            }
            if (orders.Sum(o => o.Mined) is var sum && sum != lastMined)
            {
                (lastMined, lastBreak) = (sum, now);
                firstStrike ??= now;
            }
            // none breaks a block for this long while the job runs: stuck (the loop: no strike, ever)
            if (planned != null && now - Math.Max(lastBreak, planned.Value) > StuckSeconds && eidolons.Any(e => e.Orders?.OrderCode != null))
            {
                stuck = true;
                return true;
            }
            if (++ticks % 900 == 0)
                Log($"{now - start:0} s: {string.Join(" + ", orders.Select(o => o.Mined))} mined, {shared.BadStandsFound} bad stands; "
                    + string.Join(" | ", orders.Select((o, i) => eidolons[i].Orders?.OrderCode == null ? "order ended" : o.DebugState)));
            return now >= end || eidolons.All(e => e.Orders?.OrderCode == null);
        }, 400_000);

        double seconds = EidolonJobSystem.Now(W) - start;
        bool ended = eidolons.All(e => e.Orders?.OrderCode == null);
        int total = orders.Sum(o => o.Mined);
        var shared = job!.Shared;
        int bad = shared.BadStandsFound;
        string? report = orders.Select(o => o.Reported).OfType<string>().FirstOrDefault();
        // what is left of the area: every block one the plan kept (out of reach, under those, the way out)
        var kept = orders[0].Kept;
        var left = cells.Where(c => W.BlockAccessor.GetBlock(c) is { Id: not 0 } b && !b.IsLiquid())
            .Select(c => new MarkPos(c.X, c.Y, c.Z)).Where(c => !kept.Contains(c)).ToList();
        Log($"{string.Join(" + ", orders.Select(o => o.Mined))} = {total} of {blocks} mined in {seconds:0} s of game time, {plans} plans, "
            + $"first strike {(firstStrike - planned) ?? -1:0} s after the first plan, {bad} bad stands, out of reach {orders[0].Unreachable}; "
            + $"{(ended ? "ended" : "still running")}; report: {report ?? "none"}; {left.Count} left the plan did not keep: {string.Join(" ", left.Take(20))}");
        foreach (var e in eidolons)
            e.Die(EnumDespawnReason.Removed);
        Assert.NotNull(planned);
        Assert.False(stuck, $"none broke a block for {StuckSeconds} s, {total} mined");
        Assert.True(firstStrike != null && firstStrike - planned <= FirstStrikeWithin,
            $"first block broken {(firstStrike - planned)?.ToString("0") ?? "never"} s after the first plan, over {FirstStrikeWithin} s");
        Assert.True(bad <= MostBadStands, $"{bad} stands found bad, over {MostBadStands}");
        Assert.True(total >= Least, $"{total} mined, under {Least}");
        Assert.True(ended, $"the job had not ended after {seconds:0} s");
        Assert.Empty(left);
    }
}
