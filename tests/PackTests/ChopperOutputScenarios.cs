using Atlas.Api;
using Atlas.XUnit;
using HarmonyLib;
using SeraphHorizons.Mod;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Xunit.Abstractions;

namespace SeraphHorizons.PackTests;

/// <summary>Immersive Woodworking's powered chopper as these scenarios drive it: a frame on a
/// granite floor in clear air, fed a log, its batch ejected through the chopper's own private
/// <c>EjectBatch</c> (what its chop-complete path calls), and the items that come of it. Shared
/// with <see cref="ChopperOutputOffScenarios"/>.</summary>
internal sealed class ChopperSite(IWorldSession world, BlockPos master, BlockFacing facing)
{
    public const string Log = "game:log-placed-oak-ud";

    public BlockPos Master { get; } = master;
    public BlockFacing Facing { get; } = facing;

    /// <summary>The cell right in front of the output side, at the machine's floor level.</summary>
    public BlockPos OutputCell => Master.AddCopy(Facing.Opposite);

    private IWorldAccessor W => world.Api.World;
    private BlockEntity? _chopper;

    public BlockEntity Chopper => _chopper ?? throw new InvalidOperationException("not built");

    /// <summary>Clears a 9 x 5 x 9 room of air around the master on a granite floor and places
    /// the frame (on its own: SetBlock places no part cells, which the cell in front is not one
    /// of anyway). With <paramref name="hopper"/>, a hopper is sunk into the floor under the cell
    /// in front, with a chest under it to take what it passes down.</summary>
    public async Task Build(bool hopper)
    {
        for (int dx = -4; dx <= 4; dx++)
        for (int dz = -4; dz <= 4; dz++)
        {
            world.SetBlock("game:rock-granite", Master.AddCopy(dx, -1, dz));
            world.SetBlock("game:rock-granite", Master.AddCopy(dx, -2, dz));
            for (int dy = 0; dy < 5; dy++)
                world.SetBlock("game:air", Master.AddCopy(dx, dy, dz));
        }
        if (hopper)
        {
            world.SetBlock("game:hopper", OutputCell.DownCopy());
            world.SetBlock("game:chest-east", OutputCell.DownCopy(2));
        }
        world.SetBlock($"immersivewoodworking:chopper-frame-{Facing.Code}", Master);
        await world.Ticks(2);
        _chopper = W.BlockAccessor.GetBlockEntity(Master)
                   ?? throw new Xunit.Sdk.XunitException("chopper frame placed without a block entity");
        Assert.Equal(Facing, (BlockFacing)AccessTools.Property(_chopper.GetType(), "Facing").GetValue(_chopper)!);
    }

    /// <summary>Loads a log, takes the chopper's own batch for it (product and drop count) and
    /// ejects it as the chop-complete path does. Returns the number of items ejected.</summary>
    public int ChopOneLog()
    {
        var input = (ItemSlot)AccessTools.Property(Chopper.GetType(), "InputSlot").GetValue(Chopper)!;
        input.Itemstack = new ItemStack(W.GetBlock(new AssetLocation(Log)));
        object batch = AccessTools.Method(Chopper.GetType(), "GetChopBatch").Invoke(Chopper, null)
                       ?? throw new Xunit.Sdk.XunitException($"the chopper makes nothing of {Log}");
        var (product, count) = ((ItemStack, int))batch;
        input.Itemstack = null;
        AccessTools.Method(Chopper.GetType(), "EjectBatch", [typeof(ItemStack), typeof(int)])
            .Invoke(Chopper, [product, count]);
        return product.StackSize;
    }

    /// <summary>The loose items in the room.</summary>
    public List<EntityItem> Loose() =>
        W.GetEntitiesAround(Master.ToVec3d().Add(0.5, 1, 0.5), 6, 4, e => e is EntityItem && e.Alive)
            .Cast<EntityItem>().ToList();

    /// <summary>Items in the sunk hopper and the chest under it.</summary>
    public int Caught() =>
        new[] { OutputCell.DownCopy(), OutputCell.DownCopy(2) }
            .Select(p => W.BlockAccessor.GetBlockEntity(p) as BlockEntityContainer)
            .Sum(c => c?.Inventory.Sum(s => s.StackSize) ?? 0);

    /// <summary>Where an item rests, relative to the master block's corner, along and across the
    /// output side (0.5 across is in line with the master's centre).</summary>
    public (double Along, double Across, double Up) Local(EntityItem item)
    {
        double rx = item.Pos.X - Master.X, rz = item.Pos.Z - Master.Z;
        Vec3i o = Facing.Opposite.Normali;
        double along = o.X != 0 ? (o.X > 0 ? rx : 1 - rx) : (o.Z > 0 ? rz : 1 - rz);
        double across = o.X != 0 ? rz : rx;
        return (along, across, item.Pos.Y - Master.Y);
    }

    /// <summary>Joins a player 16 blocks off to the side, so the area is simulated, and out of
    /// item pickup range.</summary>
    public static async Task<ITestPlayer> Watcher(IWorldSession world, BlockPos near, string name)
    {
        var player = await world.JoinPlayer(name);
        await player.TeleportTo(near.AddCopy(16, 3, 0));
        return player;
    }

    public static bool Patched(IWorldAccessor world) =>
        Harmony.GetPatchInfo(AccessTools.Method(AccessTools.TypeByName(ChopperOutput.ChopperType), "EjectBatch",
            [typeof(ItemStack), typeof(int)]))?.Prefixes.Any(p => p.owner == SeraphHorizonsSystem.HarmonyId) == true;
}

/// <summary>
/// mods-src/seraphhorizons, ChopperOutput: Immersive Woodworking's powered chopper drops its batch
/// in the cell right in front of its output side, so a hopper sunk into the floor there catches all
/// of it. The scenarios eject real batches through the chopper's own EjectBatch.
/// </summary>
[AtlasWorld]
public class ChopperOutputScenarios(ITestOutputHelper output) : AtlasScenarioBase
{
    private const int Logs = 10;

    private IWorldAccessor W => World.Api.World;

    // Fails when Immersive Woodworking grows its footprint into the cell in front: the drop point
    // would be inside the machine.
    [AtlasScenario]
    public void The_cell_in_front_is_clear_of_the_machine()
    {
        Assert.True(ChopperSite.Patched(W));
        var getCells = AccessTools.Method(AccessTools.TypeByName(ChopperOutput.ChopperType), "GetCells");
        foreach (var facing in BlockFacing.HORIZONTALS)
        {
            var master = new BlockPos(1000, 100, 1000);
            var cells = (BlockPos[])getCells.Invoke(null, [master, facing])!;
            Assert.Contains(master, cells);
            Assert.DoesNotContain(master.AddCopy(facing.Opposite), cells);
            Assert.DoesNotContain(master.AddCopy(facing.Opposite).Up(), cells);
        }
    }

    [AtlasScenario(TimeoutMs = 180_000)]
    public async Task Every_facing_drops_its_batches_in_the_cell_in_front()
    {
        var origin = World.Spawn.AddCopy(60, 12, -60);
        await ChopperSite.Watcher(World, origin, "chopwatcher");
        int i = 0;
        foreach (var facing in new[] { BlockFacing.NORTH, BlockFacing.EAST, BlockFacing.SOUTH, BlockFacing.WEST })
        {
            var site = new ChopperSite(World, origin.AddCopy(0, 0, 12 * i++), facing);
            await site.Build(hopper: false);
            int ejected = 0;
            for (int n = 0; n < Logs; n++)
                ejected += site.ChopOneLog();
            await World.Ticks(150);

            var loose = site.Loose();
            output.WriteLine($"{facing.Code}: wind {W.BlockAccessor.GetWindSpeedAt(site.OutputCell)}");
            Assert.Equal(ejected, loose.Sum(e => e.Itemstack.StackSize));
            foreach (var item in loose)
            {
                var (along, across, up) = site.Local(item);
                output.WriteLine($"{facing.Code}: {item.Itemstack.StackSize}x {item.Itemstack.Collectible.Code} at "
                                 + $"along {along:F3} across {across:F3} up {up:F3} onGround {item.OnGround}");
                // In the cell in front (along 1..2 from the master's corner), away from its edges.
                // Dropped at least 0.3 from them; the rest is room for the wind (ChopperEject).
                Assert.InRange(along, 1.15, 1.85);
                Assert.InRange(across, 0.15, 0.85);
                Assert.InRange(up, -0.01, 0.2);
                Assert.True(item.OnGround);
            }
        }
    }

    [AtlasScenario(TimeoutMs = 240_000)]
    public async Task A_hopper_sunk_in_front_catches_every_batch()
    {
        var origin = World.Spawn.AddCopy(-60, 12, 60);
        await ChopperSite.Watcher(World, origin, "hopperwatcher");
        int i = 0;
        foreach (var facing in new[] { BlockFacing.NORTH, BlockFacing.EAST })
        {
            var site = new ChopperSite(World, origin.AddCopy(0, 0, 12 * i++), facing);
            await site.Build(hopper: true);
            int ejected = 0;
            for (int n = 0; n < Logs; n++)
                ejected += site.ChopOneLog();
            await World.Until(() => site.Loose().Count == 0, 1500);
            output.WriteLine($"{facing.Code}: {site.Caught()} of {ejected} caught");
            Assert.Equal(ejected, site.Caught());
        }
    }
}

/// <summary>
/// The same with the switch off (<c>"ChopperDropsInFront": false</c>, fixtures/chopperoutput-off):
/// the chopper is not patched and throws its batch past the cell in front, as Immersive
/// Woodworking ships it. Its own server.
/// </summary>
[AtlasWorld]
[AtlasDataFiles("fixtures/chopperoutput-off", TargetPath = "ModConfig")]
public class ChopperOutputOffScenarios(ITestOutputHelper output) : AtlasScenarioBase
{
    [AtlasScenario(TimeoutMs = 180_000)]
    public async Task Switched_off_the_chopper_throws_its_batch_as_it_ships()
    {
        Assert.False(World.Api.LoadModConfig("seraphhorizons.json")["ChopperDropsInFront"].AsBool(true));
        Assert.False(ChopperSite.Patched(World.Api.World));

        var origin = World.Spawn.AddCopy(60, 12, -60);
        await ChopperSite.Watcher(World, origin, "chopwatcher");
        var site = new ChopperSite(World, origin, BlockFacing.NORTH);
        await site.Build(hopper: false);
        int ejected = 0;
        for (int n = 0; n < 10; n++)
            ejected += site.ChopOneLog();
        await World.Ticks(150);

        var loose = site.Loose();
        Assert.Equal(ejected, loose.Sum(e => e.Itemstack.StackSize));
        var spots = loose.Select(site.Local).ToList();
        foreach (var (along, across, up) in spots)
            output.WriteLine($"along {along:F3} across {across:F3} up {up:F3}");
        // Immersive Woodworking's throw carries the batch past the cell in front.
        Assert.Contains(spots, s => s.Along > 2);
    }
}
