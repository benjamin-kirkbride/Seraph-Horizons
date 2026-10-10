using Atlas.XUnit;
using SeraphHorizons.Mod.Eidolon;
using SeraphHorizons.Mod.Eidolon.Core;
using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.Rosser.Core;
using SeraphHorizons.Mod.TrunkEntities;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SeraphHorizons.PackTests;

/// <summary>
/// mods-src/seraphhorizons/Eidolon (#679): the crew order. On the woodworking world, a floor in the
/// sky with a running rosser and two oaks grown by the game's generator beyond its infeed: an eidolon
/// holding an axe is told with the command tool to fell and haul (an area, then the rosser). It fells
/// one tree, hauls the trunk it threw to the rosser's infeed, fells the other and hauls that, the
/// rosser taking both, oil spent per tree and per trunk; then, the area clear, it says so and waits,
/// its order kept.
/// </summary>
public partial class WoodworkingEidolonScenarios
{
    [AtlasScenario(TimeoutMs = 600_000)]
    public async Task An_eidolon_crew_fells_two_trees_and_hauls_both_trunks_to_a_rosser()
    {
        var pos = await Felling.Sky(World, World.Spawn.AddCopy(-640, 50, 1700), reach: 34);
        var player = await Player("crewboss");
        await StandBy(player, pos);
        var rosser = await PlaceRosser(pos, "east");
        RosserReady(rosser, player);
        await PowerRosser(rosser);
        var cells = RosserRig.InfeedNeighbours().Select(rosser.CellPos).ToList();
        var outward = Footprint.ToWorld(RosserRig.InfeedSide, rosser.Side).Normal();
        var across = new Vec3d(-outward.Z, 0, outward.X);
        // Two oaks 14 beyond the infeed cells' middle, 10 apart across the line.
        double mx = cells.Average(c => c.X + 0.5), mz = cells.Average(c => c.Z + 0.5);
        var centre = new BlockPos((int)Math.Floor(mx + 14 * outward.X), pos.Y, (int)Math.Floor(mz + 14 * outward.Z));
        BlockPos Off(double a, double o) => centre.AddCopy((int)Math.Round(a * across.X + o * outward.X), 0, (int)Math.Round(a * across.Z + o * outward.Z));
        var axeItem = (ItemAxe)W.GetItem(new AssetLocation("game:axe-felling-iron"))!;
        var logs = new List<BlockPos>();
        foreach (var (spot, seed) in new[] { (Off(-5, 0), 101), (Off(5, 0), 102) })
            logs.Add(await Felling.Grow(World, spot, Felling.Oak, 1.0f, seed));
        foreach (var l in logs.Where(l => W.BlockAccessor.GetBlock(l.DownCopy()).Id == 0))
            World.SetBlock("game:soil-medium-normal", l.DownCopy());
        int Wood(BlockPos log) => Felling.Tree(W, axeItem, log).Wood;
        var woods = logs.Select(Wood).ToList();
        output.WriteLine($"trees: {string.Join(", ", logs.Zip(woods, (l, w) => $"{l} {w} logs"))}");
        var config = EidolonSystem.Of(World.Api)!.Config;
        Assert.All(woods, w => Assert.True(w >= config.FellMinLogs, $"{w} logs"));
        await World.Ticks(5);

        // An eidolon of the player's between the trees and the rosser, with an axe, bound to a command tool.
        var sp = (IServerPlayer)player;
        var back = sp.Entity.Pos.XYZ;
        sp.Entity.TeleportTo(Off(0, -6).ToVec3d().Add(0.5, 0, 0.5));
        var eidolon = EidolonSystem.Of(World.Api)!.Spawn(W, Off(0, -4).ToVec3d().Add(0.5, 0, 0.5), 0, player, activate: true);
        Assert.NotNull(eidolon);
        var e = eidolon!;
        await World.Until(() => e.CanWork, 600);
        var item = W.GetItem(new AssetLocation(EidolonCommanderSystem.CommanderCode));
        var commander = Assert.IsType<ItemEidolonCommander>(item);
        var slot = player.InventoryManager.ActiveHotbarSlot;
        slot.Itemstack = new ItemStack(item);
        var a = Off(-12, -8).DownCopy();
        var b = Off(12, 8).DownCopy();
        try
        {
            Assert.True(commander.Bind(sp, slot, e));
            ItemEidolonCommander.SetMode(slot.Itemstack, CrewOrder.OrderCode);
            Assert.Equal(EidolonMarkKind.AreaThenBlock, ItemEidolonCommander.ModeOf(slot.Itemstack)!.Mark);
            Assert.True(EidolonCommandModes.IndexOf(CrewOrder.OrderCode) > EidolonCommandModes.IndexOf(HaulOrder.OrderCode));

            // No axe: refused.
            Assert.Equal(0, commander.Use(sp, slot, a));
            Assert.Equal(0, commander.Use(sp, slot, b));
            Assert.Equal(0, commander.Use(sp, slot, rosser.GhostCells().First().Pos));
            Assert.Null(e.Orders!.OrderCode);

            var axe = new DummySlot(new ItemStack(axeItem));
            e.OnInteract(sp.Entity, axe, e.Pos.XYZ.AddCopy(0, 2, 0), EnumInteractMode.Interact);
            Assert.True(axe.Empty, "it did not take the axe");
            Assert.Equal(0, commander.Use(sp, slot, a));
            Assert.Equal(0, commander.Use(sp, slot, b));
            Assert.Equal(1, commander.Use(sp, slot, rosser.GhostCells().First().Pos));
        }
        finally
        {
            slot.Itemstack = null;
            sp.Entity.TeleportTo(back);
        }
        Assert.Equal(CrewOrder.OrderCode, e.Orders!.OrderCode);
        var carry = e.GetBehavior<EntityBehaviorEidolonTrunk>()!;
        double Oil() => e.Oil?.Tank?.Points ?? 0;
        double oil = Oil();
        string State() => $"{EidolonCommands.Describe(e)}; at {e.Pos.XYZ}; carrying {carry.Trunk?.Block?.Code}; rosser {rosser.State}; "
                           + $"standing {logs.Count(l => Wood(l) > 0)}; {e.GetInfoText().Replace('\n', ' ')}; trunks lying at "
                           + string.Join(" ", TrunksLying(pos, 34).Select(t => $"({t.Pos.X:0.0} {t.Pos.Y:0.0} {t.Pos.Z:0.0})"));

        // Both trunks through the rosser: each one it takes is counted, and a finished one taken off its bed.
        // Bounded well under the watchdog (which would fail the whole class): the wait ends early when the
        // crew says its area is clear having given a trunk up.
        var taken = new List<ItemStack>();
        bool had = false;
        int ticks = 0;
        var crew = Assert.IsType<CrewOrder>(e.Orders.Current);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        await World.Until(() =>
        {
            var on = rosser.Trunk;
            if (on != null && !had)
            {
                taken.Add(on.Clone());
                output.WriteLine($"rosser took trunk {taken.Count}: {on.Block?.Code} {Trunks.StoredLogs(on, W)} logs; " + State());
            }
            had = on != null;
            if (rosser.State == RosserState.Delivered)
                rosser.TakeFinished();
            if (++ticks % 600 == 0)
                output.WriteLine(State());
            return taken.Count >= 2 || (crew.Done && crew.LeftLying(e) > 0) || clock.Elapsed > CrewBudget;
        }, int.MaxValue);
        Assert.True(taken.Count >= 2, $"the rosser took {taken.Count} of 2 trunks in {clock.Elapsed.TotalSeconds:0} s "
                                      + $"(crew done {crew.Done}, {crew.LeftLying(e)} trunks given up); " + State());
        Assert.All(logs, l => Assert.Equal(0, Wood(l)));
        Assert.Equal(woods.Sum(), taken.Sum(t => Trunks.StoredLogs(t, W)));

        // The area clear and nothing left lying: it says so, keeps its order and waits.
        var order = Assert.IsType<CrewOrder>(e.Orders.Current);
        Assert.Same(crew, order);
        await World.Until(() => order.Done, 2000);
        await World.Until(() => e.GetInfoText().Contains("area is clear"), 400);
        output.WriteLine("done: " + State());
        Assert.Equal(CrewOrder.OrderCode, e.Orders.OrderCode);
        Assert.Equal(2, order.Felled);
        Assert.Equal(2, order.Delivered);
        Assert.False(carry.Carrying);
        Assert.Empty(TrunksLying(pos, 34));
        if (e.Oil?.Tank != null)
            Assert.Equal(oil - 2 * config.OilPerTreeFelled - 2 * config.OilPerTrunkDelivered, Oil(), 3);

        e.Die(EnumDespawnReason.Removed);
        W.BlockAccessor.SetBlock(0, RosserRotorPos(rosser));
        KillItemsNear(pos, 40);
    }

    /// <summary>The longest the crew scenario waits for both trunks to reach the rosser (about two
    /// minutes when it works), well under its watchdog.</summary>
    private static readonly TimeSpan CrewBudget = TimeSpan.FromMinutes(5);

    private List<EntityTrunk> TrunksLying(BlockPos around, int radius) =>
        World.EntitiesIn(new Cuboidi(around.X - radius, around.Y - 10, around.Z - radius, around.X + radius, around.Y + 20, around.Z + radius))
            .OfType<EntityTrunk>().Where(t => t.Alive).ToList();
}
