using System.Reflection;
using Atlas.XUnit;
using HarmonyLib;
using SeraphHorizons.Mod.Pipes;
using SeraphHorizons.Mod.Pipes.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SeraphHorizons.PackTests;

// seraphhorizons, UnifiedPipes, the Hydrate or Diedrate side (Pipes/Game/HydratePipes.cs,
// Pipes/Game/HandPumpBridge.cs): Hydrate's pipes and valves are gone (no recipe, hidden, removed
// from the world as they load), and its hand pump finds a wellspring through ppex pipes, primes for
// their length, loses the spring when the run changes, a closed valve or steam in the run; a ppex
// fluid intake over a well drains its spring. The pipe scenarios build 40 above spawn at x 300 to
// 320, z -300 (clear of the other scenarios' sites), the well 30 above it at x -14, z 14; none needs
// a player.
public partial class SharedWorldScenarios
{
    private const string HodPump = "hydrateordiedrate:handpump-copper-east";

    private static Type HodType(string name) =>
        AccessTools.TypeByName(name) ?? throw new Xunit.Sdk.XunitException($"no {name}");

    private static object? CallPrivate(object target, string name, Type[] types, params object?[] args) =>
        (AccessTools.DeclaredMethod(target.GetType(), name, types)
         ?? throw new Xunit.Sdk.XunitException($"no {target.GetType().Name}.{name}")).Invoke(target, args);

    private static int HodNetworkVersion() =>
        (int)AccessTools.DeclaredProperty(HodType(HandPumpBridge.NetworkStateTypeName), "NetworkVersion").GetValue(null)!;

    private BlockEntity PumpAt(BlockPos pos) =>
        W.BlockAccessor.GetBlockEntity(pos) is { } be && be.GetType().FullName == HandPumpBridge.PumpTypeName
            ? be
            : throw new Xunit.Sdk.XunitException($"no hand pump at {pos}");

    private BlockEntity? SpringOf(BlockEntity pump) =>
        (BlockEntity?)CallPrivate(pump, "GetOrFindSpring", Type.EmptyTypes);

    private int PrimingOf(BlockEntity pump, BlockPos spring) =>
        (int)CallPrivate(pump, "ComputePrimingStrokes", [typeof(IWorldAccessor), typeof(BlockPos), typeof(BlockPos)], W, pump.Pos, spring)!;

    private static (bool Enabled, int PerStroke) HodPriming()
    {
        var config = AccessTools.DeclaredProperty(HodType(HandPumpBridge.HodConfigTypeName), "Instance").GetValue(null)!;
        var pump = AccessTools.Property(config.GetType(), "Pump").GetValue(config)!;
        return ((bool)AccessTools.Property(pump.GetType(), "HandPumpEnablePriming").GetValue(pump)!,
            (int)AccessTools.Property(pump.GetType(), "HandPumpPrimingBlocksPerStroke").GetValue(pump)!);
    }

    /// <summary>A ppex straight pipe of <paramref name="metal"/> on <paramref name="axis"/>, iron
    /// when that metal is not there.</summary>
    private string PpexPipe(string axis, string metal) =>
        W.GetBlock(new AssetLocation($"ppex:pipe-straight-{axis}-{metal}")) is { Id: > 0 }
            ? $"ppex:pipe-straight-{axis}-{metal}"
            : $"ppex:pipe-straight-{axis}-iron";

    /// <summary>The ppex pipe network beyond <paramref name="cell"/>'s <paramref name="face"/>, and
    /// its state's medium.</summary>
    private object? HodNetworkAcross(BlockPos cell, BlockFacing face)
    {
        var type = HodType(HandPumpBridge.ManagerNames[0]);
        var across = AccessTools.Method(type, "GetConnectedNetworkAcross", [typeof(IBlockAccessor), typeof(BlockPos), typeof(BlockFacing)]);
        return across.Invoke(World.Api.ModLoader.GetModSystem(type.FullName!), [W.BlockAccessor, cell, face]);
    }

    private static string? HodMedium(object network)
    {
        for (var t = network.GetType(); t != null; t = t.BaseType)
            if (t.GetProperty("State", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly) is { } p)
                return p.GetValue(network) is { } state ? (string?)state.GetType().GetProperty("MediumType")!.GetValue(state) : null;
        return null;
    }

    private async Task<BlockPos> PumpSite(int dx)
    {
        var origin = await CutterSite(300 + dx, -300, 3);
        for (int x = -3; x <= 3; x++)
            for (int z = -3; z <= 3; z++)
                for (int y = 5; y <= 12; y++)
                    W.BlockAccessor.SetBlock(0, origin.AddCopy(x, y, z));
        return origin;
    }

    // Fails when Hydrate or Diedrate renames its pipe, valve or pump files, or ppex its straight
    // pipe: match patches/pipes-hydrateordiedrate.json to the new paths.
    [AtlasScenario]
    public void Hydrates_pipes_are_not_made_and_its_pump_takes_ppex_copper_or_lead_pipe()
    {
        foreach (var code in new[] { "hydrateordiedrate:pipe-copper", "hydrateordiedrate:pipe-lead",
                     "hydrateordiedrate:shutoffvalve-copper", "hydrateordiedrate:shutoffvalve-lead" })
        {
            var block = W.GetBlock(new AssetLocation(code));
            Assert.NotNull(block);
            Assert.True(block!.CreativeInventoryTabs == null || block.CreativeInventoryTabs.Length == 0, $"{code} is in the creative inventory");
            Assert.True(block.Attributes?["handbook"]?["exclude"]?.AsBool() == true, $"{code} has a handbook page");
            Assert.DoesNotContain(W.GridRecipes, r => r.Enabled && r.Output?.Code == block.Code);
        }
        foreach (var code in new[] { "hydrateordiedrate:pipesection-copper", "hydrateordiedrate:pipesection-lead" })
        {
            var item = W.GetItem(new AssetLocation(code));
            Assert.NotNull(item);
            Assert.True(item!.CreativeInventoryTabs == null || item.CreativeInventoryTabs.Length == 0, $"{code} is in the creative inventory");
            Assert.True(item.Attributes?["handbook"]?["exclude"]?.AsBool() == true, $"{code} has a handbook page");
            Assert.DoesNotContain(World.Api.GetSmithingRecipes(), r => r.Enabled && r.Output?.Code == item.Code);
        }
        // The pump: made from two ppex straight pipes of its metal.
        foreach (var metal in new[] { "copper", "lead" })
        {
            var pump = W.GetBlock(new AssetLocation($"hydrateordiedrate:handpump-{metal}-east"))!;
            var recipes = W.GridRecipes.Where(r => r.Enabled && r.Output?.Code == pump.Code).ToList();
            Assert.NotEmpty(recipes);
            Assert.All(recipes, r =>
            {
                var pipes = (r.ResolvedIngredients ?? []).Where(i => i?.Code?.Domain == "ppex").ToList();
                Assert.Equal(2, pipes.Count);
                Assert.All(pipes, i => Assert.Equal($"ppex:pipe-straight-ns-{metal}", i.ResolvedItemStack?.Collectible.Code.ToString()));
                Assert.DoesNotContain((r.ResolvedIngredients ?? []), i => i?.Code?.Path.StartsWith("pipe-") == true && i.Code.Domain == "hydrateordiedrate");
            });
            // Its handbook page says it works on ppex pipes.
            var sections = pump.Attributes?["handbook"]?["extraSections"]?.AsArray();
            Assert.Contains(sections ?? [], s => s["title"].AsString() == "seraphhorizons:handpump-handbook-title");
        }
        Assert.Contains("Pipes and Power Expanded", Lang.GetL("en", "seraphhorizons:handpump-handbook-text"));
    }

    [AtlasScenario]
    public async Task A_placed_Hydrate_pipe_or_valve_is_gone_with_nothing_left()
    {
        var origin = await PumpSite(0);
        var pipe = origin.AddCopy(0, 0, 0);
        var valve = origin.AddCopy(2, 0, 0);
        World.SetBlock("hydrateordiedrate:pipe-copper", pipe);
        World.SetBlock("hydrateordiedrate:shutoffvalve-lead", valve);
        await World.Ticks(5);
        Assert.Equal(0, W.BlockAccessor.GetBlock(pipe).Id);
        Assert.Equal(0, W.BlockAccessor.GetBlock(valve).Id);
        Assert.Null(W.BlockAccessor.GetBlockEntity(pipe));
        Assert.Empty(CutterItemsNear(origin));
        // Other blocks with block entities are untouched (the patch is on Hydrate's own Initialize).
        World.SetBlock(HodPump, pipe);
        World.SetBlock("game:chest-east", valve);
        await World.Ticks(5);
        Assert.Equal(HodPump, W.BlockAccessor.GetBlock(pipe).Code.ToString());
        Assert.Equal("game:chest-east", W.BlockAccessor.GetBlock(valve).Code.ToString());
        W.BlockAccessor.SetBlock(0, pipe);
        W.BlockAccessor.SetBlock(0, valve);
    }

    /// <summary>A pump at (0, 8, 0), seven upright ppex pipes under it (copper, lead and iron mixed),
    /// and a wellspring beside the lowest (east of it), so Hydrate's own search (a spring right under
    /// the pump) finds nothing.</summary>
    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task The_hand_pump_finds_a_spring_through_ppex_pipes_and_primes_for_their_length()
    {
        var origin = await PumpSite(10);
        var pumpPos = origin.AddCopy(0, 8, 0);
        var springPos = origin.AddCopy(1, 1, 0);
        string[] metals = ["copper", "lead", "iron", "copper", "steel", "lead", "copper"];
        for (int i = 0; i < metals.Length; i++)
            World.SetBlock(PpexPipe("ud", metals[i]), origin.AddCopy(0, 7 - i, 0));
        World.SetBlock("hydrateordiedrate:wellspring", springPos);
        World.SetBlock(HodPump, pumpPos);
        await World.Ticks(5);
        var pump = PumpAt(pumpPos);
        var springBe = W.BlockAccessor.GetBlockEntity(springPos);
        Assert.Equal(HandPumpBridge.SpringTypeName, springBe?.GetType().FullName);

        // Found through the pipes, primed as Hydrate primes for seven of its own.
        Assert.Same(springBe, SpringOf(pump));
        var (enabled, perStroke) = HodPriming();
        int expected = HandPumpSearch.PrimingStrokes(7, enabled, perStroke);
        output.WriteLine($"priming: {PrimingOf(pump, springPos)} strokes for 7 pipes (enabled {enabled}, {perStroke} a stroke)");
        Assert.Equal(expected, PrimingOf(pump, springPos));
        Assert.Equal(expected, HandPumpSearch.PrimingStrokes(7, enabled, perStroke));
        // The pipe end under the pump and the one against the spring are sealed, not open.
        var node = AccessTools.DeclaredMethod(HodType(HandPumpBridge.NodeBlockTypeName), "IsValidNonNetworkConnection", [typeof(Block), typeof(BlockFacing)]);
        var top = W.BlockAccessor.GetBlock(pumpPos.DownCopy());
        Assert.True((bool)node.Invoke(top, [W.BlockAccessor.GetBlock(pumpPos), BlockFacing.UP])!);
        Assert.True((bool)node.Invoke(top, [W.BlockAccessor.GetBlock(springPos), BlockFacing.EAST])!);
        Assert.False((bool)node.Invoke(top, [W.GetBlock(new AssetLocation("game:rock-granite")), BlockFacing.UP])!);

        // A pipe taken out: the ppex network changes, Hydrate's cache is dropped, no spring.
        int version = HodNetworkVersion();
        var middle = origin.AddCopy(0, 4, 0);
        string middleCode = W.BlockAccessor.GetBlock(middle).Code.ToString();
        W.BlockAccessor.SetBlock(0, middle);
        W.BlockAccessor.TriggerNeighbourBlockUpdate(middle);
        await World.Until(() => HodNetworkVersion() != version, 5000);
        Assert.Null(SpringOf(pump));
        // Put back: found again.
        World.SetBlock(middleCode, middle);
        await World.Ticks(5);
        Assert.Same(springBe, SpringOf(pump));

        // A closed valve in the run cuts it, as Hydrate's shut-off valve did.
        var valveCode = W.Blocks.FirstOrDefault(b => b.Code is { Domain: "ppex" } c && c.Path.StartsWith("pipe-valve-ud-", StringComparison.Ordinal))?.Code
                        ?? throw new Xunit.Sdk.XunitException("no upright ppex valve");
        World.SetBlock(valveCode.ToString(), middle);
        await World.Ticks(5);
        var valve = W.BlockAccessor.GetBlockEntity(middle)!;
        bool IsOpen() => (bool)CallPrivate(valve, "IsOpen", Type.EmptyTypes)!;
        if (!IsOpen())
            CallPrivate(valve, "ToggleOpen", Type.EmptyTypes);
        await World.Ticks(5);
        Assert.True(IsOpen());
        Assert.Same(springBe, SpringOf(pump));
        CallPrivate(valve, "ToggleOpen", Type.EmptyTypes);
        await World.Ticks(5);
        Assert.False(IsOpen());
        Assert.Null(SpringOf(pump));
        CallPrivate(valve, "ToggleOpen", Type.EmptyTypes);
        await World.Ticks(5);
        Assert.Same(springBe, SpringOf(pump));

        // A spring right under a pump is still Hydrate's own find, with no priming.
        var direct = origin.AddCopy(-2, 1, 0);
        World.SetBlock("hydrateordiedrate:wellspring", direct);
        World.SetBlock(HodPump, direct.UpCopy());
        await World.Ticks(5);
        var directPump = PumpAt(direct.UpCopy());
        Assert.Same(W.BlockAccessor.GetBlockEntity(direct), SpringOf(directPump));
        Assert.Equal(0, PrimingOf(directPump, direct));

        // Steam in the run: no well.
        var network = HodNetworkAcross(pumpPos, BlockFacing.DOWN) ?? throw new Xunit.Sdk.XunitException("no pipe network under the pump");
        var gas = network.GetType().GetMethods().Single(m => m.Name == "TryProduceGas" && m.GetParameters().Length == 6);
        Assert.True((bool)gas.Invoke(network, [200f, 150f, "Steam", W.BlockAccessor, 1f, true])!);
        output.WriteLine($"run under the pump: {HodMedium(network)}");
        Assert.Equal("Steam", HodMedium(network));
        Assert.Null(HandPumpBridge.Search(World.Api, pumpPos));

        foreach (var pos in new[] { pumpPos, springPos, direct, direct.UpCopy() }.Concat(Enumerable.Range(1, 7).Select(y => origin.AddCopy(0, y, 0))))
            W.BlockAccessor.SetBlock(0, pos);
    }

    /// <summary>A well (a 5-level rock shaft, full) with a ppex fluid intake over its water: what the
    /// intake produces comes out of the spring, never more than it holds, and an empty spring gives
    /// none. The intake's own scan needs a wider pool than a one-block shaft, so the scenario marks
    /// it as having water, as its scan would, and calls ProduceWater as ppex's pumps do.</summary>
    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task A_fluid_intake_over_a_well_drains_its_spring()
    {
        // Close to spawn, as the well scenario's: a spring checks its shaft only where the chunks
        // around it are loaded (clear of that scenario's wells at x -9 to 5, z -9 to 5).
        var springPos = BuildWell(World.Spawn.AddCopy(-14, 30, 14), 5, (_, _) => Rock);
        for (int y = 6; y <= 8; y++)
            W.BlockAccessor.SetBlock(0, springPos.UpCopy(y));
        await World.Ticks(5);
        var (levels, capacity) = WellShaft(springPos);
        Assert.Equal(5, levels);
        var spring = W.BlockAccessor.GetBlockEntity(springPos)!;
        var springType = spring.GetType();
        float Litres() => (float)AccessTools.Property(springType, "TotalLiters").GetValue(spring)!;
        void Change(float litres) =>
            AccessTools.DeclaredMethod(springType, "TryChangeVolume", [typeof(float), typeof(bool)]).Invoke(spring, [litres, true]);
        Change(capacity);
        await World.Ticks(5);
        float full = Litres();
        output.WriteLine($"spring: {full:0.0} / {capacity:0.0} L; above it {string.Join(", ", Enumerable.Range(1, 5).Select(y => W.BlockAccessor.GetBlock(springPos.UpCopy(y), BlockLayersAccess.Fluid).Code))}");
        Assert.True(full > 100, $"the spring holds only {full} L");

        var intakePos = springPos.UpCopy(6);
        World.SetBlock("ppex:pipe-fluidintake-s", intakePos);
        await World.Ticks(5);
        var intake = W.BlockAccessor.GetBlockEntity(intakePos) ?? throw new Xunit.Sdk.XunitException("no fluid intake");
        Assert.Same(spring, HandPumpBridge.SpringUnderIntake(World.Api, intakePos, out bool wellWater));
        Assert.True(wellWater);

        float Produce(float litres)
        {
            AccessTools.DeclaredPropertySetter(intake.GetType(), "HasWater").Invoke(intake, [true]);
            AccessTools.DeclaredPropertySetter(intake.GetType(), "Crowded").Invoke(intake, [false]);
            return (float)CallPrivate(intake, "ProduceWater", [typeof(float), typeof(float), typeof(IBlockAccessor)], litres, 20f, W.BlockAccessor)!;
        }

        float produced = Produce(10f);
        output.WriteLine($"produced {produced:0.00} L; spring {full:0.0} -> {Litres():0.0} L");
        Assert.True(produced > 0, "the intake produced nothing");
        Assert.Equal(full - produced, Litres(), 2);

        // Down to 30 L (three levels of water): the intake gets no more than that.
        Change(30f - Litres());
        await World.Ticks(5);
        Assert.Equal(30f, Litres(), 2);
        float last = Produce(50f);
        output.WriteLine($"asked 50 L of a 30 L spring: produced {last:0.00} L, spring {Litres():0.0} L");
        Assert.InRange(last, 0.001f, 30.001f);
        Assert.Equal(30f - last, Litres(), 2);
        // Empty: an intake that still thinks it has water gets none of it.
        Change(-Litres());
        await World.Ticks(5);
        HandPumpBridge.SpringUnderIntake(World.Api, intakePos, out bool stillWater);
        if (stillWater)
            Assert.Equal(0f, Produce(10f));

        W.BlockAccessor.SetBlock(0, intakePos);
    }
}
