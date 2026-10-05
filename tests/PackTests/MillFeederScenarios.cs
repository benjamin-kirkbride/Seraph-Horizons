using Atlas.XUnit;
using SeraphHorizons.Mod.BuckingSawmill;
using SeraphHorizons.Mod.BuckingSawmill.Core;
using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Machines.Core;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace SeraphHorizons.PackTests;

/// <summary>A feeder for the mill's scenarios, standing in for the rosser: a block entity of the
/// test assembly only, registered at run time and spawned on a plain block.</summary>
public sealed class StubTrunkFeeder : BlockEntity, ITrunkFeeder
{
    public Side Side { get; set; }
    public HashSet<BlockPos> OutfeedCells { get; } = [];
    public bool Busy { get; set; }
    public ItemStack? Finished { get; set; }
    public int Takes { get; private set; }

    public bool HasOutfeedCell(BlockPos world) => OutfeedCells.Contains(world);

    public ItemStack? PeekFinished() => Finished;

    public ItemStack? TakeFinished()
    {
        Takes++;
        var trunk = Finished;
        Finished = null;
        return trunk;
    }
}

/// <summary>A ghost cell for <see cref="StubTrunkFeeder"/>.</summary>
public sealed class StubMachineGhost : BlockEntity, IMachineGhost
{
    public BlockPos? Principal { get; set; }
}

/// <summary>
/// mods-src/seraphhorizons/BuckingSawmill: the mill's second feeder kind (<see cref="ITrunkFeeder"/>,
/// the rosser's hand-off), with a stub feeder in the cells where it looks for a rack. The real
/// rosser's hand-off is RosserScenarios'.
/// </summary>
public partial class WoodworkingScenarios
{
    private const string StubFeederClass = "seraphhorizonspacktests.StubTrunkFeeder";
    private const string StubGhostClass = "seraphhorizonspacktests.StubMachineGhost";

    private void RegisterStubs()
    {
        foreach (var (name, type) in new[] { (StubFeederClass, typeof(StubTrunkFeeder)), (StubGhostClass, typeof(StubMachineGhost)) })
            if (RegisteredType(name) != type)
                World.Api.RegisterBlockEntityClass(name, type);
    }

    private Type? RegisteredType(string name)
    {
        try
        {
            return World.Api.ClassRegistry.GetBlockEntity(name);
        }
        catch (KeyNotFoundException)
        {
            return null;
        }
    }

    /// <summary>Puts a stub block entity of <paramref name="className"/> on a plain block at <paramref name="pos"/>.</summary>
    private T SpawnStub<T>(string className, BlockPos pos) where T : BlockEntity
    {
        RegisterStubs();
        World.SetBlock("game:rock-granite", pos);
        W.BlockAccessor.SpawnBlockEntity(className, pos);
        return W.BlockAccessor.GetBlockEntity(pos) as T ?? throw new Xunit.Sdk.XunitException($"no {typeof(T).Name} at {pos}");
    }

    private void RemoveStub(BlockPos pos)
    {
        W.BlockAccessor.RemoveBlockEntity(pos);
        W.BlockAccessor.SetBlock(0, pos);
    }

    /// <summary>A spot in the sky as <see cref="Sky"/> gives, its chunk column loaded first (the
    /// first scenario of a boot can run before the server has loaded it).</summary>
    private async Task<BlockPos> LoadedSky(int dx, int dz)
    {
        var at = World.Spawn.AddCopy(dx, 30, dz);
        int size = Vintagestory.API.Config.GlobalConstants.ChunkSize;
        if (World.Api is ICoreServerAPI sapi)
            sapi.WorldManager.LoadChunkColumnPriority(at.X / size, at.Z / size);
        await World.Until(() => W.BlockAccessor.GetChunkAtBlockPos(at) != null, 30000);
        return Sky(dx, dz);
    }

    private static Side Turned(Side side, int quarters) => Sides.All[(Array.IndexOf(Sides.All, side) + quarters) % 4];

    [AtlasTheory(TimeoutMs = 120_000), MemberData(nameof(Facings))]
    public async Task A_feeder_in_line_at_the_infeed_end_is_found_in_every_cell_and_facing(string side, int index)
    {
        var pos = await LoadedSky(-60 + 30 * index, -120);
        var player = await Player("feeder" + index);
        var mill = await PlaceMill(pos, side);
        var cells = Rig.InfeedNeighbours().Select(mill.CellPos).ToList();
        Assert.Equal(3, cells.Count);
        Assert.Equal(RackState.None, mill.CheckRack(out _));

        foreach (var cell in cells)
        {
            var feeder = SpawnStub<StubTrunkFeeder>(StubFeederClass, cell);
            try
            {
                feeder.Side = mill.Side;
                feeder.OutfeedCells.Add(cell.Copy());
                Assert.Equal(RackState.FeederEmpty, mill.CheckRack(out var ready));
                Assert.Null(ready);
                feeder.Busy = true;
                Assert.Equal(RackState.FeederBusy, mill.CheckRack(out _));

                // Its finished trunk is offered by the rack's rules, and only peeked at.
                feeder.Finished = DebarkedTrunk("oak", 4);
                Assert.Equal(RackState.FeederReady, mill.CheckRack(out ready));
                Assert.Same(feeder, ready);
                feeder.Finished = Trunk("oak", 4, branched: true);
                Assert.Equal(RackState.Branched, mill.CheckRack(out _));
                feeder.Finished = Trunk("oak", 0);
                Assert.Equal(RackState.NoLogs, mill.CheckRack(out _));
                feeder.Finished = DebarkedTrunk("oak", 4);

                // Facing any other way, or not its outfeed cell: not a feeder of this mill.
                foreach (int quarters in new[] { 1, 2, 3 })
                {
                    feeder.Side = Turned(mill.Side, quarters);
                    Assert.Equal(RackState.None, mill.CheckRack(out _));
                }
                feeder.Side = mill.Side;
                feeder.OutfeedCells.Clear();
                Assert.Equal(RackState.None, mill.CheckRack(out _));
                feeder.OutfeedCells.Add(cell.Copy());

                // An unassembled mill does not pull: the trunk stays with the feeder.
                Assert.False(mill.PullFromRack());
                Assert.Equal(0, feeder.Takes);
                Assert.NotNull(feeder.Finished);
            }
            finally
            {
                RemoveStub(cell);
            }

            // Through a ghost cell, the feeder's controller standing elsewhere.
            var controllerPos = cell.UpCopy(5);
            var controller = SpawnStub<StubTrunkFeeder>(StubFeederClass, controllerPos);
            var ghost = SpawnStub<StubMachineGhost>(StubGhostClass, cell);
            try
            {
                controller.Side = mill.Side;
                controller.Finished = DebarkedTrunk("oak", 4);
                ghost.Principal = controllerPos.Copy();
                // The controller's cell is not the outfeed cell: the ghost's is.
                controller.OutfeedCells.Add(controllerPos.Copy());
                Assert.Equal(RackState.None, mill.CheckRack(out _));
                controller.OutfeedCells.Add(cell.Copy());
                Assert.Equal(RackState.FeederReady, mill.CheckRack(out var ready));
                Assert.Same(controller, ready);
                ghost.Principal = null;
                Assert.Equal(RackState.None, mill.CheckRack(out _));
            }
            finally
            {
                RemoveStub(cell);
                RemoveStub(controllerPos);
            }
        }

        // The block info says what the feeder offers.
        var busy = SpawnStub<StubTrunkFeeder>(StubFeederClass, cells[1]);
        try
        {
            busy.Side = mill.Side;
            busy.OutfeedCells.Add(cells[1].Copy());
            busy.Busy = true;
            Assemble(mill, player);
            await World.Until(() => mill.RackState == RackState.FeederBusy, 3000);
            Assert.Contains("Rosser: a trunk is on its way through", Info(mill, player));
            busy.Busy = false;
            await World.Until(() => mill.RackState == RackState.FeederEmpty, 3000);
            Assert.Contains("Rosser: empty", Info(mill, player));
            busy.Finished = DebarkedTrunk("oak", 4);
            await World.Until(() => mill.RackState == RackState.FeederReady, 3000);
            Assert.Contains("Rosser: its debarked trunk goes on when the saws come to the top", Info(mill, player));
            // Unpowered, the mill still does not take it.
            Assert.Null(mill.Trunk);
            Assert.Equal(0, busy.Takes);
        }
        finally
        {
            RemoveStub(cells[1]);
        }
    }

    [AtlasScenario(TimeoutMs = 180_000)]
    public async Task A_running_mill_takes_the_feeders_finished_trunk_at_the_top_and_cuts_it()
    {
        var pos = await LoadedSky(90, -120);
        var player = await Player("fedmill");
        var mill = await PlaceMill(pos, "south");
        Assemble(mill, player);
        KillItemsNear(pos);
        // The cell in line with the bed, where the rosser's trunk line leaves it.
        var cell = mill.CellPos(Rig.InfeedNeighbours().Single(c => c.Z == 0));
        var feeder = SpawnStub<StubTrunkFeeder>(StubFeederClass, cell);
        try
        {
            feeder.Side = mill.Side;
            feeder.OutfeedCells.Add(cell.Copy());
            feeder.Busy = true;
            await Power(mill);

            // A trunk on its way: the mill cycles empty and waits.
            var until = DateTime.UtcNow.AddSeconds(3);
            while (DateTime.UtcNow < until)
                await World.Ticks(5);
            Assert.Null(mill.Trunk);
            Assert.Equal(RackState.FeederBusy, mill.RackState);
            Assert.Equal(0, feeder.Takes);

            // Finished, it is taken at the next top and cut into debarked logs.
            var trunk = DebarkedTrunk("oak", 4);
            feeder.Busy = false;
            feeder.Finished = trunk;
            await World.Until(() => mill.Trunk != null, 8000);
            Assert.Equal(1, feeder.Takes);
            Assert.Null(feeder.Finished);
            Assert.True(Trunks.IsDebarked(mill.Trunk));
            Assert.Equal(4, Trunks.StoredLogs(mill.Trunk!, W));
            await World.Until(() => mill.Trunk == null, 6000);
            Assert.Equal(Cutting.LogYield(4, Mod.Config.LogsPerStoredLog), ItemsNear(pos).GetValueOrDefault("game:debarkedlog-oak-ud"));

            // The next finished trunk waits, still the feeder's, while the saws rise from the bed.
            feeder.Finished = DebarkedTrunk("oak", 4);
            Assert.Equal(MillPhase.Raising, mill.Phase);
            Assert.True(mill.Depth > 0.75f, $"depth {mill.Depth} right after the cut");
            Assert.False(mill.PullFromRack());
            Assert.Equal(1, feeder.Takes);
            Assert.NotNull(feeder.Finished);
            float lowest = mill.Depth;
            until = DateTime.UtcNow.AddSeconds(10);
            while (mill.Trunk == null && DateTime.UtcNow < until)
            {
                lowest = Math.Min(lowest, mill.Depth);
                await World.Ticks(1);
            }
            Assert.NotNull(mill.Trunk);
            Assert.True(lowest <= 0.2f, $"the next trunk came with the saws still at {lowest}");
            Assert.Equal(2, feeder.Takes);
            Assert.Null(feeder.Finished);
        }
        finally
        {
            RemoveStub(cell);
        }
    }
}
