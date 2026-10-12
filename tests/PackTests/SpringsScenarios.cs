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
// rock's stones and its water drains. The hand-built spring sits 40 above spawn at x 360, z -360
// (clear of the pipe scenarios at x 300 to 320, z -300); the new chunks are generated at x -600 to
// -480, z 600 to 720.
public partial class SharedWorldScenarios
{
    private const string SpringCode = "seraphhorizons:waterspring-normal-granite";

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
    /// taps it from above, its far end against granite (so the run is sealed both ends).</summary>
    [AtlasScenario(TimeoutMs = 180_000)]
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
}
