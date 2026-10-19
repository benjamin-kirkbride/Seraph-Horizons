using Atlas.XUnit;
using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.Ore.Core;
using SeraphHorizons.Mod.Ore.Processing;
using SeraphHorizons.Mod.Sluice;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SeraphHorizons.PackTests;

// seraphhorizons, Sluice (mods-src/seraphhorizons/Sluice/; #734, #701): the sluice in the pack's world, built
// by its stages, fed by hand, watered by code (FillWater, as a ppex pipe would), washing on the virtual
// clock: concentrate down into a chest under its third block, tailings into a chest beyond its tail or onto
// a gravel heap there; rich gravel and bog iron on the line. Built 40 above spawn at x 600 to 640, z 600.
public partial class MillWorldScenarios
{
    /// <summary>
    /// A floor of granite at <paramref name="dx"/>, <paramref name="dz"/> from spawn, 40 up, cleared five high, its
    /// chunk columns kept loaded: on the virtual clock minutes of game time pass, which would otherwise unload them.
    /// </summary>
    private async Task<BlockPos> SluiceSite(int dx, int dz, int reach = 6)
    {
        var origin = World.Spawn.AddCopy(dx, 40, dz);
        int size = GlobalConstants.ChunkSize;
        var columns = new List<BlockPos>();
        for (int cx = (origin.X - reach) / size; cx <= (origin.X + reach) / size; cx++)
            for (int cz = (origin.Z - reach) / size; cz <= (origin.Z + reach) / size; cz++)
                columns.Add(new BlockPos(cx * size, origin.Y, cz * size));
        if (World.Api is ICoreServerAPI sapi)
            foreach (var c in columns)
                sapi.WorldManager.LoadChunkColumnPriority(c.X / size, c.Z / size, new ChunkLoadOptions { KeepLoaded = true });
        await World.Until(() => columns.All(c => W.BlockAccessor.GetChunkAtBlockPos(c) != null), 30000);
        int floor = W.GetBlock(new AssetLocation("game:rock-granite"))!.Id;
        for (int x = -reach; x <= reach; x++)
        for (int z = -reach; z <= reach; z++)
        {
            W.BlockAccessor.SetBlock(floor, origin.AddCopy(x, -1, z));
            for (int y = 0; y <= 4; y++)
                W.BlockAccessor.SetBlock(0, origin.AddCopy(x, y, z));
        }
        return origin;
    }

    /// <summary>A sluice facing south (running +z) a block up at <paramref name="origin"/>, a chest under its
    /// third block for the concentrate, and a granite block under the cell beyond its tail.</summary>
    private async Task<(BESluice Sluice, BlockPos Head, BlockPos Concentrate, BlockPos Tail)> PlaceSluice(BlockPos origin)
    {
        var head = origin.UpCopy();
        World.SetBlock("seraphhorizons:sluice-south", head);
        await World.Ticks(5);
        var be = W.BlockAccessor.GetBlockEntity(head) as BESluice ?? throw new Xunit.Sdk.XunitException($"no sluice at {head}");
        be.PlaceCells();
        var conc = origin.AddCopy(0, 0, 2);
        World.SetBlock("game:chest-east", conc);
        var tail = head.AddCopy(0, 0, 4);
        World.SetBlock("game:rock-granite", tail.DownCopy());
        await World.Ticks(2);
        return (be, head, conc, tail);
    }

    private static void BuildSluice(BESluice be)
    {
        while (be.Assembly!.FillNext()) { }
        Assert.True(be.Built);
        be.MarkDirty(true);
    }

    private int ChestCount(BlockPos at, string code) =>
        (W.BlockAccessor.GetBlockEntity(at) as BlockEntityContainer)?.Inventory
            .Where(s => !s.Empty && s.Itemstack.Collectible.Code.ToString() == code).Sum(s => s.StackSize) ?? 0;

    private static ItemStack SluiceStack(IWorldAccessor w, string code, int n)
    {
        var loc = new AssetLocation(code);
        CollectibleObject c = (CollectibleObject?)w.GetItem(loc) ?? w.GetBlock(loc) ?? throw new Xunit.Sdk.XunitException($"no {code}");
        return new ItemStack(c, n);
    }

    /// <summary>Waters the sluice every server tick, as a pipe would, until <paramref name="done"/>.</summary>
    private async Task SluiceUntil(BESluice be, Func<bool> done, int ticks)
    {
        long listener = W.RegisterGameTickListener(_ => be.FillWater(100), 20);
        var trace = new System.Text.StringBuilder();
        try
        {
            for (int t = 0; t < ticks && !done(); t += 100)
            {
                await World.Ticks(100);
                trace.Append($"[{W.ElapsedMilliseconds} {be.State} {be.InfeedSlot.StackSize}] ");
            }
            if (!done())
                throw new TimeoutException(trace.ToString());
        }
        catch (Exception e) when (e is not Xunit.Sdk.XunitException)
        {
            var sb = new System.Text.StringBuilder();
            be.GetBlockInfo(null!, sb);
            throw new Xunit.Sdk.XunitException($"the sluice stalled ({e.Message}): state {be.State}, infeed {be.InfeedSlot.StackSize}\n{sb}");
        }
        finally
        {
            W.UnregisterGameTickListener(listener);
        }
    }

    [AtlasScenario, ReadsBootLog]
    public void Sluice_loads_with_its_build_and_bog_iron_on_the_line()
    {
        Assert.True(SluiceSystem.Applies(World.Api));
        foreach (var side in new[] { "north", "east", "south", "west" })
            Assert.IsType<BlockMachine>(W.GetBlock(new AssetLocation($"seraphhorizons:sluice-{side}")));
        Assert.Contains(W.GridRecipes, r => r.Output?.Code?.ToString() == "seraphhorizons:sluice-north");
        var build = MachinesSystem.Of(World.Api).Build("sluice");
        Assert.NotNull(build);
        Assert.Equal(["riffles", "services"], build!.Stages.Select(s => s.Id));
        var rig = OreMachinesSystem.Of(World.Api).Rig("sluice", ["concentrate"]);
        Assert.NotNull(rig);
        Assert.Null(rig!.Anchor(OreMachineRig.Power));
        Assert.NotNull(rig.Anchor("concentrate"));
        Assert.NotNull(rig.Anchor("tailings"));
        // bog iron (#701): one stone a block, and the stone does not bloom raw
        var bog = W.GetItem(new AssetLocation(PlacerFeed.BogIronCode));
        Assert.NotNull(bog);
        Assert.Null(bog!.CombustibleProps);
        var block = W.GetBlock(SluiceSystem.BogIronBlock);
        var drop = Assert.Single(block!.Drops!, d => d.Code.ToString() == PlacerFeed.BogIronCode);
        Assert.Equal(1f, drop.Quantity.avg);
        Assert.Equal(0f, drop.Quantity.var);
        // it takes crushed ore, rich gravel and bog iron; not raw ore or concentrate
        Assert.True(SluiceSystem.TakesFeed(SluiceStack(W, "seraphhorizons:crushedore-hematite-rich", 1)));
        Assert.True(SluiceSystem.TakesFeed(SluiceStack(W, "game:richgravel-granite", 1)));
        Assert.True(SluiceSystem.TakesFeed(new ItemStack(bog)));
        Assert.False(SluiceSystem.TakesFeed(SluiceStack(W, "seraphhorizons:concentrate-hematite", 1)));
        Assert.False(SluiceSystem.TakesFeed(SluiceStack(W, "game:ore-rich-hematite-granite", 1)));
        // the pan's table for rich gravel is read, with nuggets in it
        Assert.Contains(SeraphHorizons.Mod.Ore.PlacerTable.For(World.Api, "game:richgravel-granite"), d => d.Metal);
        var logged = World.BootDiagnostics
            .Where(e => e.Level is EnumLogType.Warning or EnumLogType.Error)
            .Where(e => e.Message.Contains("sluice", StringComparison.OrdinalIgnoreCase))
            .Select(e => $"[{e.Level}] {e.Message}").ToList();
        Assert.True(logged.Count == 0, "Logged:\n" + string.Join("\n", logged));
    }

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Sluice_washes_crushed_ore_into_its_concentrate_and_tailings_chutes()
    {
        var origin = await SluiceSite(600, 600, 6);
        var (be, head, conc, tail) = await PlaceSluice(origin);
        World.SetBlock("game:chest-east", tail);
        await World.Ticks(2);

        // unbuilt it takes nothing in and does not run
        Assert.Equal(MachineState.NotBuilt, be.State);
        BuildSluice(be);
        await World.Ticks(3);
        Assert.Equal(MachineState.Dry, be.State);

        const string feed = "seraphhorizons:crushedore-hematite-rich";
        be.InfeedSlot.Itemstack = SluiceStack(W, feed, 6);
        be.InfeedSlot.MarkDirty();
        using (var clock = VirtualClock.Start(World))
            await SluiceUntil(be, () => be.InfeedSlot.Empty, 6 * 400);

        // the riffles' catch: 6 rocks × units × 0.85 (unclassified) × 0.55, in whole concentrates of 5
        var recovery = OreProcessingSystem.RecoveryFor(World.Api);
        double units = OreProducts.RawUnits("hematite", "rich")!.Value;
        int expected = (int)Math.Floor(6 * units * 0.85 * recovery.Figure(OreMachine.Sluice) / recovery.ConcentrateUnits + 1e-9);
        await World.Ticks(5);
        Assert.Equal(expected, ChestCount(conc, "seraphhorizons:concentrate-hematite"));
        // the tailings, the same rock carrying the rest, worked by the sluice
        Assert.Equal(6, ChestCount(tail, feed));
        var tailings = (W.BlockAccessor.GetBlockEntity(tail) as BlockEntityContainer)!.Inventory.First(s => !s.Empty).Itemstack;
        var stream = OreStreamAttributes.Read(tailings)!;
        Assert.Equal(WorkedBy.Sluice, stream.Worked);
        Assert.True(stream.Units < units);

        // with nothing beyond the tail the tailings go on a heap of gravel, a layer a rock
        W.BlockAccessor.SetBlock(0, tail);
        await World.Ticks(2);
        be.InfeedSlot.Itemstack = SluiceStack(W, feed, 2);
        be.InfeedSlot.MarkDirty();
        using (var clock = VirtualClock.Start(World))
            await SluiceUntil(be, () => be.InfeedSlot.Empty, 2 * 400);
        await World.Ticks(3);
        Assert.Equal("game:gravel-granite-land-2", W.BlockAccessor.GetBlock(tail).Code.ToString());

        W.BlockAccessor.SetBlock(0, head);
        foreach (var p in new[] { conc, tail, tail.DownCopy() })
            W.BlockAccessor.SetBlock(0, p);
    }

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Sluice_washes_rich_gravel_to_the_pans_finds_and_its_tailings_go_on()
    {
        var origin = await SluiceSite(620, 600, 6);
        var (be, head, conc, tail) = await PlaceSluice(origin);
        World.SetBlock("game:chest-east", tail);
        await World.Ticks(2);
        BuildSluice(be);

        be.InfeedSlot.Itemstack = SluiceStack(W, "game:richgravel-granite", 8);
        be.InfeedSlot.MarkDirty();
        using (var clock = VirtualClock.Start(World))
            await SluiceUntil(be, () => be.InfeedSlot.Empty, 8 * 400);
        await World.Ticks(5);

        // the pan's table at the sluice's figure: 8 blocks give whole finds (stones at least) into the chest
        int found = (W.BlockAccessor.GetBlockEntity(conc) as BlockEntityContainer)!.Inventory.Where(s => !s.Empty).Sum(s => s.StackSize);
        Assert.True(found > 0, "nothing came down the concentrate chute");
        // the gravel goes on, carrying 45 % of its content, worked by the sluice
        Assert.Equal(8, ChestCount(tail, "game:richgravel-granite"));
        var tailings = (W.BlockAccessor.GetBlockEntity(tail) as BlockEntityContainer)!.Inventory.First(s => !s.Empty).Itemstack;
        var stream = SluiceSystem.StreamOf(tailings)!;
        Assert.True(PlacerLine.IsPlacer(stream));
        Assert.Equal(0.45, stream.Units, 6);
        Assert.Equal(WorkedBy.Sluice, stream.Worked);
        // and the sluice catches nothing more from its own tailings
        Assert.Empty(ConcentratorWash.Wash(OreProcessingSystem.RecoveryFor(World.Api), OreMachine.Sluice, stream).Products);

        W.BlockAccessor.SetBlock(0, head);
        foreach (var p in new[] { conc, tail, tail.DownCopy() })
            W.BlockAccessor.SetBlock(0, p);
    }

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Sluice_washes_bog_iron_to_limonite_concentrate()
    {
        var origin = await SluiceSite(640, 600, 6);
        var (be, head, conc, tail) = await PlaceSluice(origin);
        BuildSluice(be);
        be.InfeedSlot.Itemstack = SluiceStack(W, PlacerFeed.BogIronCode, 4);
        be.InfeedSlot.MarkDirty();
        using (var clock = VirtualClock.Start(World))
            await SluiceUntil(be, () => be.InfeedSlot.Empty, 4 * 400);
        await World.Ticks(5);
        // 4 × 14 × 0.55 = 30.8 units: 6 concentrates of 5
        Assert.Equal((int)Math.Floor(4 * PlacerLine.BogIronUnits * 0.55 / 5 + 1e-9), ChestCount(conc, "seraphhorizons:concentrate-limonite"));

        W.BlockAccessor.SetBlock(0, head);
        foreach (var p in new[] { conc, tail, tail.DownCopy() })
            W.BlockAccessor.SetBlock(0, p);
    }
}
