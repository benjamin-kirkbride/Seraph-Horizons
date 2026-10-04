using System.Reflection;
using Atlas.XUnit;
using HarmonyLib;
using SeraphHorizons.Mod;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace SeraphHorizons.PackTests;

/// <summary>Age of Flax (ageofflaxfork) as these scenarios read it: its loaded tool blocks' yields,
/// its balance file and its crop's drops. Shared with <see cref="SwitchesOffScenarios"/>.</summary>
internal static class AgeOfFlax
{
    public static readonly string[] Tiers = ["primitive", "simple", "advanced"];

    /// <summary>A private field of a loaded block, which Age of Flax sets from balance.json in
    /// <c>OnLoaded</c>: what the tool really uses.</summary>
    public static T Field<T>(IWorldAccessor world, string block, string field)
    {
        var b = world.GetBlock(new AssetLocation(block)) ?? throw new Xunit.Sdk.XunitException($"no block {block}");
        return (T)AccessTools.Field(b.GetType(), field).GetValue(b)!;
    }

    public static object Balance() =>
        AccessTools.Property(AccessTools.TypeByName("AgeOfFlax.SRC.Common.Config.AgeOfFlaxBalance"), "Current")
            .GetValue(null)!;

    public static object? Path(object root, params string[] properties) =>
        properties.Aggregate((object?)root, (o, p) => o == null ? null : AccessTools.Property(o.GetType(), p).GetValue(o));

    public static GridRecipe Recipe(IWorldAccessor world, string output) =>
        Assert.Single(world.GridRecipes, r => r.Output?.Code?.ToString() == output);

    public static string? Ingredient(GridRecipe recipe, string key) =>
        recipe.ResolvedIngredients!.First(i => i?.Id == key)!.Code?.ToString();

    /// <summary>The mean count of <paramref name="code"/> in <paramref name="n"/> rolls of the crop
    /// block's drops, the crop standing on farmland at <paramref name="pos"/>.</summary>
    public static double MeanDrop(IWorldAccessor world, Block crop, BlockPos pos, string code, int n) =>
        Enumerable.Range(0, n).Sum(_ => crop.GetDrops(world, pos, null, 1f)
            .Where(s => s.Collectible.Code.ToString() == code).Sum(s => s.StackSize)) / (double)n;
}

/// <summary>
/// mods-src/seraphhorizons, AgeOfFlaxRebalance: Age of Flax's ripple drops no seeds and the flax
/// plant drops vanilla's again, the ripple and hatchel yields are rebalanced against vanilla flax,
/// the advanced tools take steel, every break takes raw or rendered fat, and the text says so.
/// </summary>
[AtlasWorld]
public class AgeOfFlaxRebalanceScenarios : AtlasScenarioBase
{
    private IWorldAccessor W => World.Api.World;

    // Per bundle; a ripe plant drops 1.2 bundles. Vanilla ripe flax: grain avg 3, fibers avg 4.
    private static readonly Dictionary<string, (float Grain, float Fibers)> PerBundle = new()
    {
        ["primitive"] = (3f * 2 / 3 / 1.2f, 4f * 2 / 3 / 1.2f),
        ["simple"] = (3f / 1.2f, 4f / 1.2f),
        ["advanced"] = (3f * 4 / 3 / 1.2f, 4f * 4 / 3 / 1.2f),
    };

    [AtlasScenario]
    public void Ripple_and_hatchel_yields_are_rebalanced()
    {
        foreach (var tier in AgeOfFlax.Tiers)
        {
            var ripple = $"ageofflax:ripple-{tier}-east";
            Assert.Equal(0f, AgeOfFlax.Field<float>(W, ripple, "defaultFlaxSeedDropAvg"));
            Assert.Equal(PerBundle[tier].Grain, AgeOfFlax.Field<float>(W, ripple, "defaultFlaxGrainDropAvg"), 0.001f);
            float grainVar = AgeOfFlax.Field<float>(W, ripple, "defaultFlaxGrainDropVar");
            // Rolls below zero drop nothing, which would raise the mean: keep them out of reach.
            Assert.InRange(grainVar, 0.1f, PerBundle[tier].Grain / 5);

            var hatchel = $"ageofflax:hatchel-{tier}-east";
            Assert.Equal(PerBundle[tier].Fibers, AgeOfFlax.Field<float>(W, hatchel, "defaultFlaxDropAvg"), 0.001f);
            Assert.InRange(AgeOfFlax.Field<float>(W, hatchel, "defaultFlaxDropVar"), 0.1f, PerBundle[tier].Fibers / 5);
        }
    }

    [AtlasScenario]
    public void Drying_rack_text_matches_its_speed()
    {
        Assert.Equal(AgeOfFlaxRebalance.DryingRackSpeedMultiplier,
            (float)AgeOfFlax.Path(AgeOfFlax.Balance(), "Drying", "DryingRackSpeedMultiplier")!);
        Assert.Equal("Will dry retted flax 3x faster.", Lang.GetMatching("ageofflax:blockdesc-dryingrack-north"));
    }

    [AtlasScenario]
    public void Advanced_tools_take_steel()
    {
        foreach (var tool in new[] { "ripple", "hatchel", "break" })
        {
            Assert.Equal("game:metalnailsandstrips-steel",
                AgeOfFlax.Ingredient(AgeOfFlax.Recipe(W, $"ageofflax:{tool}-advanced-east"), "N"));
            Assert.Equal("game:metalnailsandstrips-copper",
                AgeOfFlax.Ingredient(AgeOfFlax.Recipe(W, $"ageofflax:{tool}-simple-east"), "N"));
        }
        Assert.Equal("game:rod-steel", AgeOfFlax.Ingredient(AgeOfFlax.Recipe(W, "ageofflax:break-advanced-east"), "B"));
        Assert.DoesNotContain(W.GridRecipes.Where(r => r.Output?.Code?.Domain == "ageofflax"),
            r => r.ResolvedIngredients!.Any(i => i?.Code?.Path.EndsWith("-iron") == true));
    }

    [AtlasScenario]
    public void Every_break_takes_raw_or_rendered_fat()
    {
        // game:fat* matches exactly these two in the pack.
        Assert.Equal(["game:fat", "game:fat-rendered"],
            W.Items.Where(i => i.Code?.Domain == "game" && WildcardUtil.Match("fat*", i.Code.Path))
                .Select(i => i.Code.ToString()).Order());
        foreach (var tier in AgeOfFlax.Tiers)
        {
            var fat = AgeOfFlax.Recipe(W, $"ageofflax:break-{tier}-east").ResolvedIngredients!.First(i => i?.Id == "V")!;
            Assert.True(fat.SatisfiesAsIngredient(new ItemStack(W.GetItem(new AssetLocation("game:fat")))));
            Assert.True(fat.SatisfiesAsIngredient(new ItemStack(W.GetItem(new AssetLocation("game:fat-rendered")))));
        }
    }

    [AtlasScenario]
    public void Flax_drops_vanilla_seeds_and_its_bundles()
    {
        var pos = World.Spawn.AddCopy(30, 2, -30);
        World.SetBlock("game:farmland-dry-medium", pos.DownCopy());
        foreach (var (stage, seeds, bundles) in new[] { (9, 1.2, 1.2), (8, 0.7, 0.5), (5, 0.7, 0.0) })
        {
            var crop = W.GetBlock(new AssetLocation($"game:crop-flax-{stage}"))!;
            Assert.InRange(AgeOfFlax.MeanDrop(W, crop, pos, "game:seeds-flax", 4000), seeds - 0.05, seeds + 0.05);
            Assert.InRange(AgeOfFlax.MeanDrop(W, crop, pos, "ageofflax:flaxbundle-unprocessed", 4000),
                bundles - 0.05, bundles + 0.05);
        }
        // The handbook lists the blocktype's drops.
        Assert.Contains(W.GetBlock(new AssetLocation("game:crop-flax-9"))!.Drops,
            d => d.Code?.ToString() == "game:seeds-flax" && Math.Abs(d.Quantity.avg - 1.2f) < 0.001f);
        Assert.Contains(W.GetBlock(new AssetLocation("game:crop-flax-8"))!.Drops,
            d => d.Code?.ToString() == "game:seeds-flax" && Math.Abs(d.Quantity.avg - 0.7f) < 0.001f);
    }

    // Fails when Age of Flax rewords its text: update AgeOfFlaxRebalance.LangEdits to match.
    [AtlasScenario]
    public void Text_matches_the_rebalance()
    {
        var entries = Lang.AvailableLanguages["en"].GetAllEntries();
        Assert.All(AgeOfFlaxRebalance.LangEdits, edit =>
        {
            var text = entries[edit.Key];
            Assert.Contains(edit.New, text);
            if (edit.Old.Length > 0 && !edit.New.Contains(edit.Old))
                Assert.DoesNotContain(edit.Old, text);
        });
        Assert.Equal("A tool used for stripping the grain from flax", Lang.GetMatching("ageofflax:blockdesc-ripple-advanced-west"));
        Assert.Equal("Break to prepare for the hatchel", Lang.GetMatching("ageofflax:itemdesc-flaxbundle-dried"));
        Assert.Equal("Hatchel to extract flax fibers", Lang.GetMatching("ageofflax:itemdesc-flaxbundle-broken"));
        var guide = Lang.Get("ageofflax:craftinginfo-flax-text");
        Assert.DoesNotContain("seeds and grain", guide);
        Assert.Contains("(steel)", guide);
    }
}
