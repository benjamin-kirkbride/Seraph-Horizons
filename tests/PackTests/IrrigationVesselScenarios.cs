using Atlas.Api;
using Atlas.XUnit;
using SeraphHorizons.Mod;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace SeraphHorizons.PackTests;

/// <summary>The pack's two ollas as these scenarios read them: Primitive Survival's irrigation vessel
/// (<c>primitivesurvival:irrigationvessel-*</c>) and Olla's olla (<c>olla:olla-*</c>). Shared with
/// <see cref="SwitchesOffScenarios"/>.</summary>
internal static class Ollas
{
    public const string RuinLoot = "game:stackrandomizer-clayproducts";

    public static IEnumerable<Block> Vessels(IWorldAccessor world) =>
        world.Blocks.Where(b => b.Code?.ToString().StartsWith(IrrigationVessel.CodePrefix) == true);

    public static IEnumerable<GridRecipe> VesselRecipes(IWorldAccessor world) =>
        world.GridRecipes.Where(r => r.Output?.Code?.ToString().StartsWith(IrrigationVessel.CodePrefix) == true
                                     || r.Ingredients?.Values.Any(i => i.Code?.ToString().StartsWith(IrrigationVessel.CodePrefix) == true) == true);

    /// <summary>The codes BetterRuins' clay loot can roll.</summary>
    public static List<string> RuinLootCodes(IWorldAccessor world)
    {
        var item = world.GetItem(new AssetLocation(RuinLoot)) ?? throw new Xunit.Sdk.XunitException($"no item {RuinLoot}");
        return (item.Attributes?["stacks"].AsArray() ?? []).Select(s => s["code"].AsString() ?? "").ToList();
    }
}

/// <summary>
/// mods-src/seraphhorizons, IrrigationVessel: Primitive Survival's irrigation vessel can no longer be
/// made or found, and is out of the creative inventory and the handbook, but a placed one still works;
/// Olla's olla is clay formed and fired in a pit kiln.
/// </summary>
public partial class SharedWorldScenarios
{
    [AtlasScenario]
    public async Task Irrigation_vessel_is_retired_but_a_placed_one_still_works()
    {
        var vessels = Ollas.Vessels(W).ToList();
        Assert.Equal(10, vessels.Count);
        Assert.Empty(Ollas.VesselRecipes(W));
        Assert.All(vessels, v => Assert.False(Tuns.InCreative(v), v.Code.ToString()));
        Assert.All(vessels, v => Assert.True(Tuns.HandbookExcluded(v), v.Code.ToString()));
        Assert.DoesNotContain(Ollas.RuinLootCodes(W), c => c.StartsWith(IrrigationVessel.CodePrefix));
        // The rest of the clay loot is still there.
        Assert.Contains(Ollas.RuinLootCodes(W), c => c.StartsWith("primitivesurvival:"));

        // Its classes are still Primitive Survival's, so a vessel in an existing world loads and
        // holds its water as before (50 L).
        var pos = World.Spawn.AddCopy(40, 12, -40);
        var be = await Tuns.Place(World, IrrigationVessel.Block, pos);
        Assert.Equal("BEIrrigationVessel", be.GetType().Name);
        Assert.Equal(50f, Tuns.FillWithWater(W, pos));
    }

    [AtlasScenario]
    public void Olla_is_clay_formed_and_fired()
    {
        var raw = World.Api.GetClayformingRecipes().Where(r => r.Output.Code.ToString().StartsWith("olla:olla-raw-")).ToList();
        Assert.NotEmpty(raw);
        foreach (var color in (string[])["blue", "fire", "red"])
        {
            var block = Tuns.Block(W, $"olla:olla-raw-{color}");
            Assert.True(Tuns.InCreative(block));
            Assert.Equal(EnumSmeltType.Fire, block.CombustibleProps?.SmeltingType);
            var fired = block.CombustibleProps!.SmeltedStack!.ResolvedItemstack!.Collectible;
            Assert.Equal($"olla:olla-fired-{color}-normal", fired.Code.ToString());
            Assert.True(Tuns.InCreative((Block)fired));
            Assert.False(Tuns.HandbookExcluded((Block)fired));
        }
    }
}
