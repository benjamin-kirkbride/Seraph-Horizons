using Atlas.XUnit;
using SeraphHorizons.Mod.BuckingSawmill.Core;
using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.Rosser;
using SeraphHorizons.Mod.TrunkEntities;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace SeraphHorizons.PackTests;

/// <summary>
/// mods-src/seraphhorizons/TrunkEntities/Game/TrunkStations.cs and the machines' side of trunk
/// entities: the rosser and the bucking mill take a trunk entity lying in their infeed cells, and
/// give a trunk back into Carry On's hands; Logging Expanded's sawhorse, Trunk Storage Rack and
/// heating rack load a trunk carried in Carry On's hands and unload into them. On the woodworking
/// world (<see cref="WoodworkingScenarios"/>), where trunk entities run with Carry On; each
/// scenario in its own patch of sky, west of the others.
/// </summary>
public partial class WoodworkingScenarios
{
    private static IServerPlayer Server(IPlayer player) => (IServerPlayer)player;

    /// <summary>Spawns a trunk entity with its underside's middle at <paramref name="at"/>, lying
    /// across the line whose outward direction is <paramref name="outward"/>.</summary>
    private EntityTrunk SpawnTrunk(ItemStack trunk, Vec3d at, Int3 outward)
    {
        float yaw = (float)(Math.Atan2(outward.X, outward.Z) + Math.PI / 2);
        return TrunkSpawns.Spawn(W, trunk, at, yaw) ?? throw new Xunit.Sdk.XunitException("no trunk entity spawned");
    }

    private static Vec3d Middle(BlockPos cell) => new(cell.X + 0.5, cell.Y, cell.Z + 0.5);

    [AtlasScenario(TimeoutMs = 180_000)]
    public async Task A_running_rosser_takes_a_trunk_entity_from_its_infeed_cells_only()
    {
        var pos = await RosserSky(-400, 0);
        var player = await Player("groundrosser");
        await StandBy(player, pos);
        var rosser = await PlaceRosser(pos, "east");
        RosserReady(rosser, player);
        var cells = RosserRig.InfeedNeighbours().Select(rosser.CellPos).ToList();
        var outward = Footprint.ToWorld(RosserRig.InfeedSide, rosser.Side).Normal();
        var middle = cells.OrderBy(c => c.X * outward.Z - c.Z * outward.X).ElementAt(cells.Count / 2);

        // Three cells beyond the infeed end: not the rosser's.
        var outside = SpawnTrunk(RosserTrunk("oak", 4, 3), Middle(middle).Add(3 * outward.X, 0, 3 * outward.Z), outward);
        // In the infeed cells but unpowered: it waits.
        var debarked = SpawnTrunk(Trunks.Debark(RosserTrunk("oak", 4, 0), W)!.Trunk, Middle(middle), outward);
        await PowerRosser(rosser);
        await World.Ticks(50);
        // a debarked trunk is refused, as on a rack
        Assert.Null(rosser.Trunk);
        Assert.True(debarked.Alive);
        debarked.Die(EnumDespawnReason.Removed);

        var inside = SpawnTrunk(RosserTrunk("oak", 4, 3), Middle(middle), outward);
        await World.Until(() => rosser.Trunk != null, 5000);
        Assert.False(inside.Alive);
        Assert.Equal(4, Trunks.StoredLogs(rosser.Trunk!, W));
        Assert.Equal(3, rosser.Trunk!.Attributes.GetInt(Trunks.BranchCountKey));
        Assert.True(outside.Alive);
        outside.Die(EnumDespawnReason.Removed);
        W.BlockAccessor.SetBlock(0, RosserRotorPos(rosser));
        KillItemsNear(pos, 20);
    }

    [AtlasScenario(TimeoutMs = 180_000)]
    public async Task A_running_mill_takes_a_debranched_trunk_entity_from_its_infeed_cells_only()
    {
        var pos = await RosserSky(-400, 60, reach: 10);
        var player = await Player("groundmill");
        await StandBy(player, pos);
        var mill = await PlaceMill(pos, "south");
        Assemble(mill, player);
        var cells = Rig.InfeedNeighbours().Select(mill.CellPos).ToList();
        var outward = Footprint.ToWorld(Rig.InfeedSide, mill.Side).Normal();
        var cell = cells[0];

        var outside = SpawnTrunk(Trunk("oak", 4), Middle(cell).Add(3 * outward.X, 0, 3 * outward.Z), outward);
        // branched, while Logging Expanded requires debranching: it waits there
        var branched = SpawnTrunk(Trunk("oak", 4, branched: true), Middle(cell), outward);
        await Power(mill, fast: true);
        // whole cycles: the saws pass the top each time
        await PastTheTop(mill);
        Assert.Null(mill.Trunk);
        Assert.True(branched.Alive);
        branched.Die(EnumDespawnReason.Removed);

        var clean = SpawnTrunk(Trunk("oak", 4), Middle(cell), outward);
        await World.Until(() => mill.Trunk != null, 15000);
        Assert.False(clean.Alive);
        Assert.Equal(4, Trunks.StoredLogs(mill.Trunk!, W));
        Assert.True(outside.Alive);
        outside.Die(EnumDespawnReason.Removed);
        await Unpower(mill);
        KillItemsNear(pos, 12);
    }

    [AtlasScenario]
    public async Task Ctrl_taking_from_the_mill_puts_the_trunk_in_hands_and_full_hands_refuse()
    {
        var pos = await RosserSky(-400, 120, reach: 10);
        var player = await Player("handsmill");
        await StandBy(player, pos);
        var mill = await PlaceMill(pos, "north");
        Assemble(mill, player);

        // Loaded from Carry On's hands (unpowered, the saws at the top).
        var trunk = Trunk("oak", 4);
        Assert.Null(Click(player, pos, trunk.Clone()));
        Assert.NotNull(mill.Trunk);
        Assert.Null(TrunkCarry.Carried(player));

        // Ctrl takes it into the hands.
        Click(player, pos, null, ctrl: true);
        Assert.Null(mill.Trunk);
        var back = TrunkCarry.Carried(player);
        Assert.NotNull(back);
        Assert.True(back.Equals(W, trunk, Vintagestory.API.Config.GlobalConstants.IgnoredStackAttributes), "the trunk came back changed");
        Assert.True(mill.HasBladeKit);

        // Hands full: the trunk stays on the bed, what is carried stays carried, the blade kit too.
        TurnToTop(mill);
        TrunkCarry.Take(Server(player));
        Assert.Null(Click(player, pos, Trunk("birch", 6)));
        Assert.NotNull(mill.Trunk);
        Assert.True(TrunkCarry.TryGive(Server(player), trunk.Clone()));
        Click(player, pos, null, ctrl: true, keepCarried: true);
        Assert.Equal("birch", Trunks.Wood(mill.Trunk!, W));
        Assert.Equal("oak", Trunks.Wood(TrunkCarry.Carried(player)!, W));
        Assert.True(mill.HasBladeKit);
        TrunkCarry.Take(Server(player));
    }

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task A_sawhorse_loads_from_hands_and_unloads_into_hands()
    {
        var shop = await Woodshop.Open(World, World.Spawn.AddCopy(-400, 3, 200));
        var sp = Server(shop.P);
        TrunkCarry.Take(sp);
        var pos = shop.Cell(0);
        World.SetBlock("loggingmod:sawhorse-north", pos);
        await World.Ticks(2);
        // Carry On lets a click through to each station while carrying (patches/trunkentities-stations.json)
        foreach (var code in new[] { "loggingmod:sawhorse-north", "loggingmod:sawhorseadvanced-oak-north", "loggingmod:trunkstorage-oak-empty-north",
                                     "loggingmod:resinrack-tan-north", "seraphhorizons:rosser-frame-north", "seraphhorizons:rosser-ghost",
                                     "seraphhorizons:buckingmill-frame-north" })
            Assert.Contains(BlockOf(code).BlockBehaviors, b => b.GetType().Name == "BlockBehaviorCarryableInteract");

        // a branched trunk is refused while Logging Expanded requires debranching
        Assert.True(TrunkCarry.TryGive(sp, Trunk("oak", 8, branched: true)));
        Assert.True(shop.Click(pos));
        Assert.Equal(0, shop.LogsOn(pos));
        Assert.NotNull(TrunkCarry.Take(sp));

        Assert.True(TrunkCarry.TryGive(sp, Trunk("oak", 8)));
        Assert.True(shop.Click(pos));
        Assert.Equal(8, shop.LogsOn(pos));
        Assert.Null(TrunkCarry.Carried(shop.P));

        // Carrying, a loaded sawhorse takes nothing and gives nothing.
        Assert.True(TrunkCarry.TryGive(sp, Trunk("birch", 4)));
        Assert.True(shop.Click(pos));
        Assert.Equal(8, shop.LogsOn(pos));
        Assert.Equal("birch", Trunks.Wood(TrunkCarry.Take(sp)!, W));

        // An axe takes a log, then the empty hand unloads what is left into the hands.
        shop.Holding(Woodshop.Axe);
        await shop.Work(pos);
        int left = shop.LogsOn(pos);
        Assert.True(left < 8);
        await shop.Collect();
        shop.Holding((ItemStack?)null);
        Assert.True(shop.Click(pos));
        var back = TrunkCarry.Take(sp);
        Assert.NotNull(back);
        Assert.Equal(left, Trunks.StoredLogs(back, W));
        Assert.Equal(0, shop.LogsOn(pos));
        // nothing on the ground
        Assert.DoesNotContain(await shop.Collect(), kv => kv.Key.StartsWith("loggingmod:treetrunk"));
    }

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task A_trunk_storage_rack_takes_carried_trunks_and_pops_into_hands()
    {
        var shop = await Woodshop.Open(World, World.Spawn.AddCopy(-400, 3, 230));
        var sp = Server(shop.P);
        TrunkCarry.Take(sp);
        var pos = shop.Cell(0);
        World.SetBlock("loggingmod:trunkstorage-oak-empty-north", pos);
        await World.Ticks(2);
        var logging = Mod.Logging!;
        BlockEntity Rack() => logging.FindRack(W.BlockAccessor, pos) ?? throw new Xunit.Sdk.XunitException("no rack");

        for (int i = 1; i <= 4; i++)
        {
            Assert.True(TrunkCarry.TryGive(sp, Trunk("oak", i, branched: i == 2)));
            Assert.True(shop.Click(pos));
            Assert.Null(TrunkCarry.Carried(shop.P));
            Assert.Equal(i, logging.TrunkCount(Rack()));
        }
        // full: the carried trunk stays carried, and the rack gives nothing
        Assert.True(TrunkCarry.TryGive(sp, Trunk("birch", 9)));
        Assert.True(shop.Click(pos));
        Assert.Equal(4, logging.TrunkCount(Rack()));
        Assert.Equal("birch", Trunks.Wood(TrunkCarry.Take(sp)!, W));

        // an empty hand pops the top one into the hands; carrying it, a click puts it back
        Assert.True(shop.Click(pos));
        Assert.Equal(4, Trunks.StoredLogs(TrunkCarry.Carried(shop.P)!, W));
        Assert.Equal(3, logging.TrunkCount(Rack()));
        Assert.True(shop.Click(pos));
        Assert.Equal(4, logging.TrunkCount(Rack()));
        Assert.Null(TrunkCarry.Carried(shop.P));
        Assert.True(shop.Click(pos));
        Assert.Equal(4, Trunks.StoredLogs(TrunkCarry.Take(sp)!, W));
        Assert.True(shop.Click(pos));
        Assert.Equal(3, Trunks.StoredLogs(TrunkCarry.Take(sp)!, W));
        Assert.Equal(2, logging.TrunkCount(Rack()));
        Assert.DoesNotContain(await shop.Collect(), kv => kv.Key.StartsWith("loggingmod:treetrunk"));
    }

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task A_heating_rack_loads_from_hands_and_unloads_into_hands()
    {
        var shop = await Woodshop.Open(World, World.Spawn.AddCopy(-400, 3, 260));
        var sp = Server(shop.P);
        TrunkCarry.Take(sp);
        var pos = shop.Cell(0);
        World.SetBlock("loggingmod:resinrack-tan-north", pos);
        await World.Ticks(2);
        var rack = shop.Entity(pos);
        bool Empty() => (bool)HarmonyLib.AccessTools.Property(rack.GetType(), "IsEmpty").GetValue(rack)!;
        Assert.True(Empty());

        // branched is refused, as its own empty-hand load refuses it
        Assert.True(TrunkCarry.TryGive(sp, Trunk("pine", 6, branched: true)));
        Assert.True(shop.Click(pos));
        Assert.True(Empty());
        Assert.NotNull(TrunkCarry.Take(sp));

        var trunk = Trunk("pine", 6);
        Assert.True(TrunkCarry.TryGive(sp, trunk.Clone()));
        Assert.True(shop.Click(pos));
        Assert.False(Empty());
        Assert.Null(TrunkCarry.Carried(shop.P));

        // full hands: it stays on the rack
        Assert.True(TrunkCarry.TryGive(sp, Trunk("oak", 2)));
        Assert.True(shop.Click(pos));
        Assert.False(Empty());
        Assert.Equal("oak", Trunks.Wood(TrunkCarry.Take(sp)!, W));

        // empty hands: into them
        Assert.True(shop.Click(pos));
        Assert.True(Empty());
        var back = TrunkCarry.Take(sp);
        Assert.NotNull(back);
        Assert.Equal("pine", Trunks.Wood(back, W));
        Assert.Equal(6, Trunks.StoredLogs(back, W));
        Assert.DoesNotContain(await shop.Collect(), kv => kv.Key.StartsWith("loggingmod:treetrunk"));
    }
}
