using Atlas.XUnit;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SeraphHorizons.PackTests;

/// <summary>
/// Hydrate or Diedrate's copies of water recipes (docs/recipe-browser/exporter.md, "Water
/// copies"). HoD 2.5.6 registers, for every recipe with `game:waterportion` in a slot or in a
/// container's `requiresContent`, one copy per kind of its clean water, named
/// `hydrateordiedrate:-HoD-&lt;Name path&gt;-&lt;water&gt;`. The copies are folded into the
/// recipes they copy. The kinds of water are the five in HoD's RecipeGenerator.ConversionMappings.
/// </summary>
public partial class RecipeExportScenarios
{
    private static readonly string[] HodWaters =
    {
        "hydrateordiedrate:waterportion-boiled-natural-clean",
        "hydrateordiedrate:waterportion-boiled-rain-clean",
        "hydrateordiedrate:waterportion-fresh-distilled-clean",
        "hydrateordiedrate:waterportion-fresh-rain-clean",
        "hydrateordiedrate:waterportion-fresh-well-clean",
    };

    private static bool HodCopy(AssetLocation? name) =>
        name is { Domain: "hydrateordiedrate" } && name.Path.StartsWith("-HoD-");

    private static bool Makes(JToken recipe, string code) =>
        recipe["variants"]!.Any(v => v["outputs"]!.Any(o => (string)o["code"]! == code));

    private static int Copies(JToken recipe) => (int?)recipe["extra"]?["waterCopies"]?["recipes"] ?? 0;

    // survival/recipes/grid/dough.json: three entries (bucket, bowl, jug) holding 1 L of
    // waterportion, with flour-* named type; spelt is one of the allowed flours.
    [AtlasScenario(TimeoutMs = Timeout)]
    public void Spelt_dough_is_one_record_per_recipe_with_the_water_copies_folded_in()
    {
        var api = (ICoreServerAPI)World.Api;
        var spelt = api.World.GridRecipes.Where(g => g.Output?.Code?.ToString() == "game:dough-spelt").ToList();
        var copies = spelt.Count(g => HodCopy(g.Name));
        var own = spelt.Count - copies;
        Assert.True(copies >= 3 * HodWaters.Length, $"HoD registered {copies} copies of the spelt dough grid recipes");

        var records = OfType("grid").Where(r => Makes(r, "game:dough-spelt")).ToList();
        Assert.All(records, r =>
        {
            Assert.NotNull(r["source"]);
            Assert.NotEqual("hydrateordiedrate", (string)r["mod"]!);
        });
        // Every registered recipe that is not a copy is one variant, and every copy is folded:
        // five per spelt variant of a recipe that takes water.
        int SpeltVariants(JToken r) => r["variants"]!.Count(v => v["outputs"]!.Any(o => (string)o["code"]! == "game:dough-spelt"));
        Assert.Equal(own, records.Sum(SpeltVariants));
        Assert.Equal(copies, HodWaters.Length * records.Where(r => Copies(r) > 0).Sum(SpeltVariants));

        for (int i = 0; i < 3; i++)
        {
            var r = Recipe($"grid|game:recipes/grid/dough.json|{i}");
            Assert.Equal("survival", (string)r["mod"]!);
            Assert.Single(r["variants"]!, v => (string?)v["bindings"]?["type"] == "spelt");
            var water = r["extra"]!["waterCopies"]!;
            Assert.Equal("hydrateordiedrate", (string)water["mod"]!);
            Assert.Equal(HodWaters, water["water"]!.Select(w => (string)w!));
            // Five copies of each registered variant.
            Assert.Equal(HodWaters.Length * r["variants"]!.Count(), (int)water["recipes"]!);
            // The slot is the container; what it must hold is unchanged.
            Assert.Equal("waterportion", (string)r["ingredients"]![0]!["extra"]!["recipeAttributes"]!["requiresContent"]!["code"]!);
        }
    }

    // survival/recipes/barrel/dye/red.json: 5 L of water and cinnabar powder make 5 L of red dye.
    // The copies' waters become alternatives of the water slot.
    [AtlasScenario(TimeoutMs = Timeout)]
    public void Barrel_water_copies_are_alternatives_of_the_water_slot()
    {
        var r = Recipe("barrel|game:recipes/barrel/dye/red.json|0");
        Assert.Equal("survival", (string)r["mod"]!);
        Assert.Equal("game:recipes/barrel/dye/red.json", (string)r["source"]!);
        Assert.Equal("game:waterportion", (string)r["ingredients"]![0]!["code"]!);
        var variant = (JObject)Assert.Single(r["variants"]!);
        var water = (JArray)variant["ingredients"]![0]!;
        Assert.Equal(new[] { "game:waterportion" }.Concat(HodWaters), water.Select(s => (string)s["code"]!));
        Assert.All(water, s => Assert.Equal(5, (double)s["litres"]!));
        Json("""[[{ "code": "game:powder-cinnabar", "kind": "item", "quantity": 1 }]]""", new JArray(variant["ingredients"]![1]!));
        Assert.Equal(HodWaters.Length, Copies(r));

        var api = (ICoreServerAPI)World.Api;
        var cinnabar = api.GetBarrelRecipes().Where(b => b.Ingredients.Any(i => i.Code?.ToString() == "game:powder-cinnabar") &&
                                                         b.Output?.Code?.ToString() == "game:dye-red").ToList();
        Assert.Equal(1 + HodWaters.Length, cinnabar.Count);
        Assert.DoesNotContain(OfType("barrel"), b => (string)b["mod"]! == "hydrateordiedrate" && Makes(b, "game:dye-red"));
    }

    // Hydrate or Diedrate's own recipes are assets like any other and are left alone:
    // recipes/grid/keg.json (one recipe) and recipes/cooking/boiledwater.json, which takes
    // game:waterportion but is not a copy.
    [AtlasScenario(TimeoutMs = Timeout)]
    public void Hydrate_or_diedrate_recipes_of_its_own_are_untouched()
    {
        var keg = Recipe("grid|hydrateordiedrate:recipes/grid/keg.json|0");
        Assert.Equal("hydrateordiedrate", (string)keg["mod"]!);
        Assert.Single(keg["variants"]!);
        Assert.Null(keg["extra"]?["waterCopies"]);

        var boiled = Recipe("cooking|hydrateordiedrate:recipes/cooking/boiledwater.json|0");
        Assert.Equal("game:waterportion", (string)boiled["ingredients"]![0]!["code"]!);
        Assert.Null(boiled["extra"]?["waterCopies"]);
    }

    /// <summary>
    /// Every copy HoD registered is folded or is a record of its own, never lost; with the pack
    /// as it is, every one is folded.
    /// </summary>
    [AtlasScenario(TimeoutMs = Timeout)]
    public void Every_water_copy_is_folded_into_its_recipe()
    {
        var api = (ICoreServerAPI)World.Api;
        var engine = api.World.GridRecipes.Count(g => HodCopy(g.Name)) + api.GetBarrelRecipes().Count(b => HodCopy(b.Name));
        Assert.True(engine > 500, $"HoD registered {engine} grid and barrel copies");
        var records = OfType("grid").Concat(OfType("barrel")).ToList();
        var left = records.Where(r => ((string)r["id"]!).Contains("|hydrateordiedrate:-HoD-")).ToList();
        Assert.True(left.Count == 0, "copies not folded: " + string.Join(", ", left.Select(r => (string)r["id"]!)));
        Assert.Equal(engine, records.Sum(Copies));
        // No copy anywhere else (simmering) either.
        Assert.DoesNotContain(Doc["recipes"]!, r => ((string)r["id"]!).Contains("|hydrateordiedrate:-HoD-"));
    }
}
