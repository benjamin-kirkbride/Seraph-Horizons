using System.Reflection;
using Atlas.XUnit;
using HarmonyLib;
using SeraphHorizons.Mod.Pipes;
using SeraphHorizons.Mod.Springs;
using SeraphHorizons.Mod.Springs.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace SeraphHorizons.PackTests;

// seraphhorizons, Springs (Springs/): the rivulet generator places springs in new chunks; a spring
// pours like the water source it replaced; a ppex pipe against it taps it, stops the pouring and
// fills its run with water at the spring's rate; untapped, it pours again; broken, it gives its
// rock's stones and its water drains; a rapids spring pours out of one side at a time. The
// hand-built springs sit 40 above spawn at z -360: the pouring one at x 360, the bucket's at x 372,
// the rapids spring at x 384 (clear of the pipe scenarios at x 300 to 320, z -300); the new chunks
// are generated at x -600 to -480, z 600 to 720.
public partial class SharedWorldScenarios
{
    private const string SpringCode = "seraphhorizons:waterspring-normal-granite";
    private const string RapidSpringCode = "seraphhorizons:waterspring-rapid-granite";

    private BlockEntityWaterSpring SpringAt(BlockPos pos) =>
        W.BlockAccessor.GetBlockEntity(pos) as BlockEntityWaterSpring
        ?? throw new Xunit.Sdk.XunitException($"no spring block entity at {pos}");

    private Block FluidAt(BlockPos pos) => W.BlockAccessor.GetBlock(pos, BlockLayersAccess.Fluid);

    private static float NetworkVolume(object network)
    {
        for (var t = network.GetType(); t != null; t = t.BaseType)
            if (t.GetProperty("State", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly) is { } p)
                return p.GetValue(network) is { } state ? (float)state.GetType().GetProperty("Volume")!.GetValue(state)! : 0f;
        return 0f;
    }

    [AtlasScenario]
    public void Springs_are_registered_and_the_rivulet_generator_is_hooked()
    {
        Assert.True(SpringsSystem.On);
        Assert.Equal(2, SpringsSystem.Hooked);
        // A spring for every rock the game's rock has, GeologyAdditions' included, of both kinds.
        foreach (var rock in new[] { "granite", "basalt", "limestone", "chalk", "dolostone", "gneiss" })
            foreach (var kind in SpringCodes.Kinds)
            {
                var code = new AssetLocation(SpringCodes.Domain, SpringCodes.BlockPath(kind, rock));
                var block = W.GetBlock(code);
                Assert.True(block is BlockWaterSpring { Id: > 0 }, $"{code} is not a spring block");
                Assert.Equal(kind, ((BlockWaterSpring)block!).Kind);
            }
        var spring = (BlockWaterSpring)W.GetBlock(new AssetLocation(SpringCode))!;
        Assert.Equal("Granite spring", spring.GetHeldItemName(new ItemStack(spring)));
        // Containers fill with Hydrate or Diedrate's fresh well water, and a player can drink it.
        Assert.Equal(SpringWater.WellWaterCode, spring.Attributes["waterTightContainerProps"]["whenFilled"]["stack"]["code"].AsString());
        Assert.True(SpringWater.CanDrink);
        // The guide's rates are the settings'.
        Assert.Contains($"{SpringsConfig.Format(SpringsSystem.Settings.NormalLitresPerSecond)} litres a second",
            Lang.GetL("en", SpringsSystem.GuideTextKey));
    }

    /// <summary>A bucket fills from a spring as from water (the game's own
    /// <c>TryFillFromBlock</c>), with Hydrate's fresh well water, endlessly; and a player drinks it,
    /// as Hydrate's drinking does.</summary>
    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task A_spring_fills_a_bucket_and_quenches_thirst_with_well_water()
    {
        var origin = await CutterSite(372, -360, 3);
        var spring = origin.AddCopy(0, 0, 0);
        World.SetBlock(SpringCode, spring);
        var bucket = (BlockLiquidContainerBase)W.GetBlock(new AssetLocation("game:woodbucket"))!;
        for (int i = 0; i < 2; i++)
        {
            var dropped = (EntityItem)W.SpawnItemEntity(new ItemStack(bucket), spring.UpCopy().ToVec3d().Add(0.5, 0.5, 0.5));
            bucket.TryFillFromBlock(dropped, spring);
            var content = bucket.GetContent(dropped.Itemstack);
            Assert.Equal(SpringWater.WellWaterCode, content?.Collectible.Code.ToString());
            Assert.Equal(bucket.CapacityLitres, bucket.GetCurrentLitres(dropped.Itemstack), 2);
            dropped.Die(EnumDespawnReason.Removed);
        }

        var player = await CutterPlayer();
        var thirst = player.Entity.GetBehavior("thirst") ?? throw new Xunit.Sdk.XunitException("no thirst behaviour");
        var current = AccessTools.Property(thirst.GetType(), "CurrentThirst");
        float max = (float)AccessTools.Property(thirst.GetType(), "MaxThirst").GetValue(thirst)!;
        current.SetValue(thirst, max / 2);
        Assert.True(SpringWater.MayDrink(player));
        Assert.True(SpringWater.Drink(W, player));
        float after = (float)current.GetValue(thirst)!;
        output.WriteLine($"thirst {max / 2} -> {after} of {max}");
        Assert.True(after > max / 2, $"drinking left thirst at {after}");
        // Nothing left in the player's hands or inventory from the drink.
        Assert.True(player.Entity.RightHandItemSlot.Empty && player.Entity.LeftHandItemSlot.Empty);
        W.BlockAccessor.SetBlock(0, spring);
    }

    /// <summary>A spring in a granite wall, open to the east over a drop; a run of four ppex pipes
    /// taps it from above, its far end against granite (so the run is sealed both ends). On the virtual
    /// clock (tests/PackTests/README.md): the pouring, draining and the run's rate are all game time.</summary>
    [AtlasScenario(TimeoutMs = 60_000)]
    public async Task A_spring_pours_until_a_pipe_taps_it_which_it_fills_at_its_rate()
    {
        var origin = await CutterSite(360, -360, 4);
        string granite = "game:rock-granite";
        var spring = origin.AddCopy(0, 3, 0);
        for (int y = 0; y <= 4; y++)
            World.SetBlock(granite, origin.AddCopy(0, y, 0));
        foreach (var facing in new[] { BlockFacing.NORTH, BlockFacing.SOUTH, BlockFacing.WEST })
            World.SetBlock(granite, spring.AddCopy(facing));
        World.SetBlock(SpringCode, spring);
        W.BlockAccessor.TriggerNeighbourBlockUpdate(spring);
        using var clock = VirtualClock.Start(World);
        var east = spring.EastCopy();

        // It pours: its water runs out of the open side.
        await World.Until(() => FluidAt(east).Code?.Path.StartsWith("water-") == true, 10_000);
        Assert.StartsWith("water-", FluidAt(east).Code?.Path ?? "");
        Assert.False(SpringAt(spring).Tapped);
        await World.Ticks(40);
        Assert.True(FluidAt(east).LiquidLevel >= 6, $"{FluidAt(east).Code} beside the spring");

        // A pipe on top taps it: the pouring stops and the water drains.
        var top = spring.UpCopy();
        for (int i = 3; i >= 0; i--)
            World.SetBlock(PpexPipe("ud", "copper"), top.AddCopy(0, i, 0));
        World.SetBlock(granite, top.AddCopy(0, 4, 0));
        W.BlockAccessor.TriggerNeighbourBlockUpdate(top);
        await World.Until(() => SpringAt(spring).Tapped, 10_000);
        Assert.True(SpringAt(spring).Tapped);
        await World.Until(() => FluidAt(east).Id == 0, 20_000);
        Assert.Equal(0, FluidAt(east).Id);

        // Its end against the spring is sealed.
        var node = AccessTools.DeclaredMethod(HodType(HandPumpBridge.NodeBlockTypeName), "IsValidNonNetworkConnection", [typeof(Block), typeof(BlockFacing)]);
        Assert.True((bool)node.Invoke(W.BlockAccessor.GetBlock(top), [W.BlockAccessor.GetBlock(spring), BlockFacing.DOWN])!);

        // The run fills with water, no faster than the spring's rate. (The network is looked up each
        // time: exlib rebuilds a run's network object as its pipes settle.)
        float rate = SpringsSystem.Settings.NormalLitresPerSecond;
        float Volume() => HodNetworkAcross(spring, BlockFacing.UP) is { } n ? NetworkVolume(n) : 0f;
        await World.Until(() => Volume() > 0, 10_000);
        Assert.True(Volume() > 0, "the spring put no water into its run");
        Assert.Equal("Water", HodMedium(HodNetworkAcross(spring, BlockFacing.UP)!));
        long t0 = W.ElapsedMilliseconds;
        float v0 = Volume();
        await World.Until(() => W.ElapsedMilliseconds - t0 > 3000, 10_000);
        float v1 = Volume();
        float seconds = (W.ElapsedMilliseconds - t0) / 1000f;
        output.WriteLine($"run volume {v0:0.##} L -> {v1:0.##} L in {seconds:0.##} s at {rate} L/s (it holds {4 * 30} L)");
        Assert.True(v1 > v0, "the spring put no water into its run");
        Assert.True(v1 - v0 <= rate * (seconds + SpringFlow.MaxCatchUpSeconds) + 0.01f, $"{v1 - v0} L in {seconds} s is over {rate} L/s");
        Assert.True(v1 - v0 >= rate * (seconds - 2) - 0.01f, $"{v1 - v0} L in {seconds} s is well under {rate} L/s");

        // The pipe taken off: it pours again.
        World.SetBlock(granite, top);
        W.BlockAccessor.TriggerNeighbourBlockUpdate(top);
        await World.Until(() => !SpringAt(spring).Tapped, 10_000);
        Assert.False(SpringAt(spring).Tapped);
        await World.Until(() => FluidAt(east).Code?.Path.StartsWith("water-") == true, 10_000);
        Assert.StartsWith("water-", FluidAt(east).Code?.Path ?? "");

        // Broken: its rock's stones, and its water drains.
        CutterKillItems(spring);
        W.BlockAccessor.BreakBlock(spring, null);
        await World.Ticks(5);
        Assert.Equal(0, W.BlockAccessor.GetBlock(spring, BlockLayersAccess.Solid).Id);
        Assert.True(CutterItemsNear(spring).GetValueOrDefault("game:stone-granite") > 0, "no granite stones from the broken spring");
        Assert.DoesNotContain(CutterItemsNear(spring).Keys, k => k.Contains("waterspring"));
        await World.Until(() => FluidAt(east).Id == 0, 30_000);
        Assert.Equal(0, FluidAt(east).Id);
        CutterKillItems(spring);
        for (int i = 0; i <= 4; i++)
            W.BlockAccessor.SetBlock(0, top.AddCopy(0, i, 0));
    }

    /// <summary>A tapped spring on the east edge of its chunk column, open below, its pipe run in the
    /// next column east: both saved and unloaded, and the spring's column loaded alone first, as a
    /// world loading does a column at a time. With its pipe's column not there yet, the spring can
    /// not tell whether a pipe still taps it: it stays tapped and pours nothing out of its open
    /// bottom; the pipe's column loaded, it is still tapped and fills the run again. In real time
    /// (the unloading and loading run on the chunk threads).</summary>
    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task A_tapped_spring_stays_tapped_while_its_pipes_column_loads_after_its_own()
    {
        int size = GlobalConstants.ChunkSize;
        int Mod(int v) => ((v % size) + size) % size;
        // the spring on the last x of its column, at x about 396
        int dx = 396 + (size - 1) - Mod(World.Spawn.X + 396);
        var origin = await CutterSite(dx, -360, 5);
        string granite = "game:rock-granite";
        var spring = origin.AddCopy(0, 2, 0);
        Assert.Equal(size - 1, Mod(spring.X));
        foreach (var facing in new[] { BlockFacing.NORTH, BlockFacing.SOUTH, BlockFacing.WEST, BlockFacing.UP })
            World.SetBlock(granite, spring.AddCopy(facing));
        // open under it, down to the floor: untapped, it pours there
        var below = spring.DownCopy();
        for (int i = 1; i <= 3; i++)
            World.SetBlock(PpexPipe("we", "copper"), spring.AddCopy(i, 0, 0));
        World.SetBlock(granite, spring.AddCopy(4, 0, 0));
        World.SetBlock(SpringCode, spring);
        W.BlockAccessor.TriggerNeighbourBlockUpdate(spring);
        W.BlockAccessor.TriggerNeighbourBlockUpdate(spring.EastCopy());
        await World.Until(() => SpringAt(spring).Tapped, 300);
        Assert.True(SpringAt(spring).Tapped);
        await World.Ticks(30);
        Assert.Equal(0, FluidAt(below).Id);

        var sapi = (ICoreServerAPI)World.Api;
        int ax = spring.X / size, bx = ax + 1, cz = spring.Z / size;
        var pipe = spring.EastCopy();
        Assert.Equal(bx, pipe.X / size);
        var saved = await World.ExecuteCommand("/autosavenow");
        output.WriteLine($"/autosavenow: {saved.Ok}");
        await World.Ticks(5);
        var old = SpringAt(spring);
        sapi.WorldManager.UnloadChunkColumn(bx, cz);
        sapi.WorldManager.UnloadChunkColumn(ax, cz);
        for (int i = 0; i < 300 && (W.BlockAccessor.GetChunkAtBlockPos(spring) != null || W.BlockAccessor.GetChunkAtBlockPos(pipe) != null); i++)
            await World.Ticks(1);
        Assert.Null(W.BlockAccessor.GetChunkAtBlockPos(spring));
        Assert.Null(W.BlockAccessor.GetChunkAtBlockPos(pipe));

        // the spring's column alone
        for (int i = 0; i < 60 && !(W.BlockAccessor.GetBlockEntity(spring) is BlockEntityWaterSpring again && !ReferenceEquals(again, old)); i++)
        {
            sapi.WorldManager.LoadChunkColumnPriority(ax, cz);
            await World.Ticks(10);
        }
        var reloaded = SpringAt(spring);
        Assert.NotSame(old, reloaded);
        // well past its first look (half a second)
        await World.Ticks(90);
        output.WriteLine($"spring column alone: pipe column loaded {W.BlockAccessor.GetChunkAtBlockPos(pipe) != null}, tapped {reloaded.Tapped}, under it {FluidAt(below).Code}");
        Assert.Null(W.BlockAccessor.GetChunkAtBlockPos(pipe));
        Assert.True(reloaded.Tapped, "the spring took its pipe's unloaded column for no pipe");
        Assert.Equal(0, FluidAt(below).Id);

        // the pipe's column too: still tapped, and it fills the run
        for (int i = 0; i < 60 && W.BlockAccessor.GetChunkAtBlockPos(pipe) == null; i++)
        {
            sapi.WorldManager.LoadChunkColumnPriority(bx, cz);
            await World.Ticks(10);
        }
        Assert.NotNull(W.BlockAccessor.GetChunkAtBlockPos(pipe));
        float Volume() => HodNetworkAcross(spring, BlockFacing.EAST) is { } n ? NetworkVolume(n) : 0f;
        await World.Until(() => Volume() > 0, 600);
        output.WriteLine($"both columns: tapped {reloaded.Tapped}, under it {FluidAt(below).Code}, run {Volume():0.##} L");
        Assert.True(reloaded.Tapped);
        Assert.Equal(0, FluidAt(below).Id);
        Assert.True(Volume() > 0, "the spring put no water into its run");

        for (int i = 0; i <= 4; i++)
            W.BlockAccessor.SetBlock(0, spring.AddCopy(i, 0, 0));
        foreach (var facing in new[] { BlockFacing.NORTH, BlockFacing.SOUTH, BlockFacing.WEST, BlockFacing.UP })
            W.BlockAccessor.SetBlock(0, spring.AddCopy(facing));
    }

    /// <summary>A rapids spring in a granite pillar, walled north and south, open east and west
    /// over a drop: it pours out of one side only (east, the first open one), and that water stays.
    /// That side walled up, it turns to the other, and the water that fell from the first drains;
    /// opened again, it keeps to the side it has. On the virtual clock (tests/PackTests/README.md).</summary>
    [AtlasScenario(TimeoutMs = 60_000)]
    public async Task A_rapids_spring_pours_out_of_one_side_at_a_time()
    {
        var origin = await CutterSite(384, -360, 4);
        string granite = "game:rock-granite";
        var spring = origin.AddCopy(0, 3, 0);
        for (int y = 0; y <= 4; y++)
            World.SetBlock(granite, origin.AddCopy(0, y, 0));
        foreach (var facing in new[] { BlockFacing.NORTH, BlockFacing.SOUTH })
            World.SetBlock(granite, spring.AddCopy(facing));
        World.SetBlock(RapidSpringCode, spring);
        W.BlockAccessor.TriggerNeighbourBlockUpdate(spring);
        using var clock = VirtualClock.Start(World);
        var east = spring.EastCopy();
        var west = spring.WestCopy();
        bool Running(BlockPos at) => FluidAt(at).Code?.Path.StartsWith("rapidwater-") == true
                                     && FluidAt(at).LiquidLevel >= SpringCodes.PouredLevel;
        string Sides() => $"east {FluidAt(east).Code}, west {FluidAt(west).Code}, face {SpringAt(spring).PourFace}";

        // It pours east only, and the water there stays.
        await World.Until(() => Running(east), 10_000);
        await World.Ticks(60);
        output.WriteLine(Sides());
        Assert.Equal(SpringPour.East, SpringAt(spring).PourFace);
        Assert.True(Running(east), Sides());
        Assert.False(Running(west), Sides());

        // East walled up: it turns west, and what fell from the east side drains.
        var fell = east.DownCopy();
        Assert.StartsWith("rapidwater-", FluidAt(fell).Code?.Path ?? "");
        W.BlockAccessor.SetBlock(0, east, BlockLayersAccess.Fluid);
        World.SetBlock(granite, east);
        W.BlockAccessor.TriggerNeighbourBlockUpdate(east);
        await World.Until(() => Running(west), 10_000);
        Assert.Equal(SpringPour.West, SpringAt(spring).PourFace);
        await World.Until(() => FluidAt(fell).Id == 0, 20_000);
        output.WriteLine($"{Sides()}, under east {FluidAt(fell).Code}");
        Assert.Equal(0, FluidAt(fell).Id);

        // East open again: it keeps pouring west.
        W.BlockAccessor.SetBlock(0, east);
        W.BlockAccessor.TriggerNeighbourBlockUpdate(east);
        await World.Ticks(60);
        output.WriteLine(Sides());
        Assert.Equal(SpringPour.West, SpringAt(spring).PourFace);
        Assert.True(Running(west), Sides());
        Assert.False(Running(east), Sides());

        // Taken away: its water drains.
        W.BlockAccessor.SetBlock(0, spring);
        W.BlockAccessor.TriggerNeighbourBlockUpdate(spring);
        await World.Until(() => FluidAt(west).Id == 0, 30_000);
        Assert.Equal(0, FluidAt(west).Id);
        for (int y = 0; y <= 4; y++)
            W.BlockAccessor.SetBlock(0, origin.AddCopy(0, y, 0));
        foreach (var facing in new[] { BlockFacing.NORTH, BlockFacing.SOUTH })
            W.BlockAccessor.SetBlock(0, spring.AddCopy(facing));
    }
}
