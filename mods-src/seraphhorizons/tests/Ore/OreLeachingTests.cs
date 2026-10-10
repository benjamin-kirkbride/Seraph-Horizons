using System.Text.Json;
using System.Text.Json.Nodes;
using SeraphHorizons.Mod.Ore.Core;

namespace SeraphHorizons.Tests.Ore;

/// <summary>Leaching borax, saltpeter and alum (#742): the barrel and cooking recipe files against
/// <see cref="OreLeaching"/>, and the liquor's type file against its minerals.</summary>
public class OreLeachingTests
{
    private static readonly JsonDocumentOptions Lenient = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    private static JsonNode Read(params string[] path) =>
        JsonNode.Parse(File.ReadAllText(Path.Combine([AppContext.BaseDirectory, "oreprocessing", .. path])), documentOptions: Lenient)!;

    private static JsonObject[] Recipes(string file) => Read("recipes", file).AsArray().Select(r => r!.AsObject()).ToArray();

    [Fact]
    public void ThreeMineralsEachWithItsOwnLiquor()
    {
        Assert.Equal(["borax", "saltpeter", "alum"], OreLeaching.Minerals.Select(m => m.Mineral));
        Assert.Equal(3, OreLeaching.Minerals.Select(m => m.LiquorCode).Distinct().Count());
        // Not vanilla's diluted borax or Expanded Matter's diluted alum and saltpeter.
        Assert.All(OreLeaching.Minerals, m => Assert.StartsWith("seraphhorizons:crudeliquorportion-", m.LiquorCode));
        Assert.Equal(["game:ore-borax", "game:ore-alum"], OreLeaching.VanillaRaw);
        Assert.Equal("saltpeter", OreLeaching.OfRaw(OreLeaching.RawSaltpeter)?.Mineral);
    }

    [Fact]
    public void BarrelRecipesLeachEachRawMineral()
    {
        var recipes = Recipes("oreprocessing-leaching.json");
        Assert.Equal(OreLeaching.Minerals.Count, recipes.Length);
        foreach (var (m, r) in OreLeaching.Minerals.Zip(recipes))
        {
            var ingredients = r["ingredients"]!.AsArray().Select(i => i!.AsObject()).ToArray();
            var water = Assert.Single(ingredients, i => (string?)i["code"] == "game:waterportion");
            var raw = Assert.Single(ingredients, i => (string?)i["code"] == m.RawCode);
            Assert.Equal(OreLeaching.WaterLitresPerRaw, (int)water["litres"]!);
            Assert.Equal(1, (int)raw["quantity"]!);
            Assert.Equal(OreLeaching.SealHours, (double)r["sealHours"]!);
            Assert.Equal(m.LiquorCode, (string?)r["output"]!["code"]);
            Assert.Equal(OreLeaching.LiquorLitresPerRaw, (int)r["output"]!["litres"]!);
        }
    }

    [Fact]
    public void CookingRecipesBoilEachLiquorIntoItsCrystals()
    {
        var recipes = Recipes("oreprocessing-evaporating.json");
        Assert.Equal(OreLeaching.Minerals.Count, recipes.Length);
        foreach (var (m, r) in OreLeaching.Minerals.Zip(recipes))
        {
            var ingredient = Assert.Single(r["ingredients"]!.AsArray())!;
            Assert.Equal(m.LiquorCode, (string?)Assert.Single(ingredient["validStacks"]!.AsArray())!["code"]);
            Assert.Equal(1.0 / OreLeaching.CrystalsPerLitre, (double)ingredient["portionSizeLitres"]!);
            Assert.Equal(m.CrystalCode, (string?)r["cooksInto"]!["code"]);
            Assert.Equal(1, (int)r["cooksInto"]!["quantity"]!);
        }
    }

    [Fact]
    public void LiquorTypeHasAStatePerMineralAndRawSaltpeterIsRawStack()
    {
        var liquor = Read("crudeliquor.json");
        Assert.Equal(OreLeaching.Minerals.Select(m => m.Mineral),
            liquor["variantgroups"]![0]!["states"]!.AsArray().Select(s => (string)s!));
        Assert.Equal(OreProducts.RawStack, (int)Read("rawsaltpeter.json")["maxstacksize"]!);
    }
}
