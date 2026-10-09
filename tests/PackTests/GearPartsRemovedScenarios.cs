using Atlas.XUnit;
using HarmonyLib;
using SeraphHorizons.Mod;
using SeraphHorizons.Mod.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace SeraphHorizons.PackTests;

/// <summary>BetterLoot+'s gear part as these scenarios read it: the item, its grid recipes and the
/// harvestable drops BetterLoot+ wrote into the creatures. Shared with
/// <see cref="SwitchesOffScenarios"/>.</summary>
internal static class GearParts
{
    public static IEnumerable<GridRecipe> Recipes(IWorldAccessor world) =>
        world.GridRecipes.Where(r => r.Output?.Code?.ToString() == GearPartDropRules.GearPart
                                     || r.Ingredients?.Values.Any(i => i.Code?.ToString() == GearPartDropRules.GearPart) == true);

    /// <summary>An entity type's harvestable drops: (code, avg, var).</summary>
    public static List<(string Code, double Avg, double Var)> Drops(IWorldAccessor world, string entity)
    {
        var type = world.GetEntityType(new AssetLocation(entity)) ?? throw new Xunit.Sdk.XunitException($"no entity {entity}");
        return DropsOf(type.Server?.BehaviorsAsJsonObj);
    }

    /// <summary>Every harvestable drop of every entity type.</summary>
    public static IEnumerable<(string Entity, string Code)> AllDrops(IWorldAccessor world) =>
        world.EntityTypes.SelectMany(t => DropsOf(t.Server?.BehaviorsAsJsonObj).Select(d => (t.Code.ToString(), d.Code)));

    private static List<(string Code, double Avg, double Var)> DropsOf(JsonObject[]? behaviors)
    {
        var harvestable = behaviors?.FirstOrDefault(b => b?["code"].AsString() == "harvestable");
        if (harvestable == null || !harvestable["drops"].Exists)
            return [];
        return harvestable["drops"].AsArray()
            .Select(d => (new AssetLocation(d["code"].AsString() ?? "").ToString(),
                          d["quantity"]["avg"].AsDouble(), d["quantity"]["var"].AsDouble()))
            .ToList();
    }
}

/// <summary>
/// mods-src/seraphhorizons, GearPartsRemoved: BetterLoot+'s gear part is gone (item and both grid
/// recipes), and its loot drops are rusty gears at a quarter of the rate.
/// </summary>
public partial class SharedWorldScenarios
{
    [AtlasScenario]
    public void Gear_parts_are_gone_and_drop_as_rusty_gears()
    {
        Assert.True(Harmony.HasAnyPatches(GearPartsRemoved.HarmonyId));
        Assert.Null(W.GetItem(new AssetLocation(GearPartDropRules.GearPart)));
        Assert.Empty(GearParts.Recipes(W));
        Assert.DoesNotContain(GearParts.AllDrops(W), d => d.Code == GearPartDropRules.GearPart);

        // BetterLoot+ 1.0.0: a normal drifter dropped 0.25 parts, a nightmare one 3, beside the
        // rusty gears they already dropped (0.01 and 0.4, by the rustyGearDropRate stat).
        foreach (var (entity, parts, gears) in new[] { ("game:drifter-normal", 0.25, 0.01), ("game:drifter-nightmare", 3.0, 0.4) })
        {
            var rusty = GearParts.Drops(W, entity).Where(d => d.Code == GearPartDropRules.RustyGear).ToList();
            Assert.Equal(2, rusty.Count);
            Assert.Contains(rusty, d => Math.Abs(d.Avg - gears) < 1e-9);
            Assert.Contains(rusty, d => Math.Abs(d.Avg - parts / GearPartDropRules.PartsPerGear) < 1e-9);
        }
    }
}
