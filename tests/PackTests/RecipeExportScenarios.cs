using System.Collections;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Atlas.XUnit;
using Json.Schema;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SeraphHorizons.RecipeExport;
using SeraphHorizons.RecipeExport.Recipes;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SeraphHorizons.PackTests;

/// <summary>
/// The recipe half of the export (RecipeSection). Expected values come from the vanilla and
/// mod asset files (read by hand and written down here), from the engine's own registries,
/// or from the schema; never from the exporter's own code.
/// </summary>
[AtlasWorld]
public class RecipeExportScenarios : AtlasScenarioBase
{
    private const int Timeout = 900_000;

    private JObject Doc => ExportUnderTest.Get(World.Api);

    private JObject Recipe(string id) =>
        Doc["recipes"]!.Cast<JObject>().SingleOrDefault(r => (string)r["id"]! == id)
        ?? throw new Xunit.Sdk.XunitException($"no recipe {id}");

    private static JArray Codes(JToken stacks) => new(stacks.Select(s => (string)s["code"]!));

    private static JObject VariantWith(JObject recipe, string name, string value) =>
        recipe["variants"]!.Cast<JObject>().Single(v => (string?)v["bindings"]?[name] == value);

    private static void Json(string expected, JToken actual) =>
        Assert.True(JToken.DeepEquals(JToken.Parse(expected), actual),
            $"expected {JToken.Parse(expected).ToString(Formatting.None)}\n  actual {actual.ToString(Formatting.None)}");

    // ------------------------------------------------------ known vanilla recipes

    // survival/recipes/grid/ladder.json, entry 1: "P_P PSP P_P", P = plank-* named wood,
    // skipping agedebony and rottenebony; output ladder-wood-{wood}-north x3.
    [AtlasScenario(TimeoutMs = Timeout)]
    public void Grid_wildcard_recipe_is_one_record_with_a_variant_per_wood()
    {
        var r = Recipe("grid|game:recipes/grid/ladder.json|1");
        Assert.Equal("grid", (string)r["type"]!);
        Assert.Equal("survival", (string)r["mod"]!);
        Assert.Equal("game:recipes/grid/ladder.json", (string)r["source"]!);
        Json("""{ "width": 3, "height": 3, "shapeless": false, "pattern": ["P_P", "PSP", "P_P"] }""", r["grid"]!);
        Json("""
            [
              { "key": "P", "code": "game:plank-*", "kind": "item", "quantity": 1, "wildcardName": "wood",
                "skipVariants": ["agedebony", "rottenebony"] },
              { "key": "S", "code": "game:stick", "kind": "item", "quantity": 1 }
            ]
            """, r["ingredients"]!);
        Json("""[{ "code": "game:ladder-wood-{wood}-north", "kind": "block", "quantity": 3 }]""", r["outputs"]!);

        Json("""
            {
              "bindings": { "wood": "oak" },
              "ingredients": [
                [{ "code": "game:plank-oak", "kind": "item", "quantity": 1 }],
                [{ "code": "game:stick", "kind": "item", "quantity": 1 }]
              ],
              "outputs": [{ "code": "game:ladder-wood-oak-north", "kind": "block", "quantity": 3 }]
            }
            """, VariantWith(r, "wood", "oak"));

        // Independent count: every registered plank not skipped, for which the ladder block exists.
        var world = World.Api.World;
        var expected = world.Items
            .Where(i => i?.Code != null && i.Code.Domain == "game" && i.Code.Path.StartsWith("plank-"))
            .Select(i => i.Code.Path["plank-".Length..])
            .Where(w => w is not "agedebony" and not "rottenebony")
            .Where(w => world.GetBlock(new AssetLocation($"game:ladder-wood-{w}-north")) != null)
            .OrderBy(w => w, StringComparer.Ordinal).ToList();
        var woods = r["variants"]!.Select(v => (string)v["bindings"]!["wood"]!).OrderBy(w => w, StringComparer.Ordinal).ToList();
        Assert.Equal(expected, woods);
        Assert.DoesNotContain("agedebony", woods);
    }

    // Entry 0 of the same file: sticks only, no wildcard.
    [AtlasScenario(TimeoutMs = Timeout)]
    public void Grid_plain_recipe_has_one_variant_without_bindings()
    {
        var r = Recipe("grid|game:recipes/grid/ladder.json|0");
        Json("""{ "width": 3, "height": 3, "shapeless": false, "pattern": ["S_S", "SSS", "S_S"] }""", r["grid"]!);
        Json("""[{ "key": "S", "code": "game:stick", "kind": "item", "quantity": 1 }]""", r["ingredients"]!);
        Json("""
            [{ "ingredients": [[{ "code": "game:stick", "kind": "item", "quantity": 1 }]],
               "outputs": [{ "code": "game:ladder-stick-north", "kind": "block", "quantity": 3 }] }]
            """, r["variants"]!);
    }

    // survival/recipes/smithing/chisel.json: ingot-* named metal, seven allowed metals,
    // one layer of three rows.
    [AtlasScenario(TimeoutMs = Timeout)]
    public void Smithing_recipe_has_voxels_and_one_variant_per_allowed_metal()
    {
        var r = Recipe("smithing|game:recipes/smithing/chisel.json|0");
        Json("""[["________###____", "_##########____", "________###____"]]""", r["voxels"]!);
        Json("""
            [{ "code": "game:ingot-*", "kind": "item", "quantity": 1, "wildcardName": "metal",
               "allowedVariants": ["copper", "tinbronze", "bismuthbronze", "blackbronze", "iron", "meteoriciron", "steel"] }]
            """, r["ingredients"]!);
        Json("""[{ "code": "game:chisel-{metal}", "kind": "item", "quantity": 1 }]""", r["outputs"]!);
        var metals = r["variants"]!.Select(v => (string)v["bindings"]!["metal"]!).OrderBy(m => m, StringComparer.Ordinal);
        Assert.Equal(new[] { "bismuthbronze", "blackbronze", "copper", "iron", "meteoriciron", "steel", "tinbronze" }, metals);
        Json("""
            { "bindings": { "metal": "copper" },
              "ingredients": [[{ "code": "game:ingot-copper", "kind": "item", "quantity": 1 }]],
              "outputs": [{ "code": "game:chisel-copper", "kind": "item", "quantity": 1 }] }
            """, VariantWith(r, "metal", "copper"));
    }

    // survival/recipes/knapping/flint-knife.json: flint, ten rows, knifeblade-flint.
    [AtlasScenario(TimeoutMs = Timeout)]
    public void Knapping_recipe_has_its_pattern()
    {
        var r = Recipe("knapping|game:recipes/knapping/flint-knife.json|0");
        Json("""
            [["_____#____", "_____#____", "____##____", "____##____", "____##____",
              "___###____", "___###____", "___###____", "___###____", "___###____"]]
            """, r["voxels"]!);
        Json("""[{ "code": "game:flint", "kind": "item", "quantity": 1 }]""", r["ingredients"]!);
        Json("""
            [{ "ingredients": [[{ "code": "game:flint", "kind": "item", "quantity": 1 }]],
               "outputs": [{ "code": "game:knifeblade-flint", "kind": "item", "quantity": 1 }] }]
            """, r["variants"]!);
    }

    // survival/recipes/clayforming/bowl.json: two layers, clay-* named color (blue, fire, red).
    [AtlasScenario(TimeoutMs = Timeout)]
    public void Clayforming_recipe_has_layers_bottom_to_top()
    {
        var r = Recipe("clayforming|game:recipes/clayforming/bowl.json|0");
        Json("""
            [["#####", "#####", "#####", "#####", "#####"],
             ["#####", "#___#", "#___#", "#___#", "#####"]]
            """, r["voxels"]!);
        Json("""[{ "code": "game:bowl-{color}-raw", "kind": "block", "quantity": 1 }]""", r["outputs"]!);
        var colors = r["variants"]!.Select(v => (string)v["bindings"]!["color"]!).OrderBy(c => c, StringComparer.Ordinal);
        Assert.Equal(new[] { "blue", "fire", "red" }, colors);
        Json("""
            { "bindings": { "color": "red" },
              "ingredients": [[{ "code": "game:clay-red", "kind": "item", "quantity": 1 }]],
              "outputs": [{ "code": "game:bowl-red-raw", "kind": "block", "quantity": 1 }] }
            """, VariantWith(r, "color", "red"));
    }

    // survival/recipes/barrel/leather.json: four entries with the same code, told apart by
    // hide size. Entry 1: 4 L strong tannin + a medium prepared hide, 108 h, 2 leather.
    [AtlasScenario(TimeoutMs = Timeout)]
    public void Barrel_entries_sharing_a_code_stay_separate()
    {
        var r = Recipe("barrel|game:recipes/barrel/leather.json|1");
        Json("""{ "sealHours": 108 }""", r["barrel"]!);
        var ingredients = (JArray)r["ingredients"]!;
        Assert.Equal(new[] { "game:strongtanninportion", "game:hide-prepared-medium" }, Codes(ingredients).Select(c => (string)c!));
        Assert.Equal(4, (double)ingredients[0]["litres"]!);
        Assert.Equal(4, (double)ingredients[0]["extra"]!["consumeLitres"]!);
        var variant = (JObject)Assert.Single(r["variants"]!);
        Assert.Equal("game:hide-prepared-medium", (string)variant["ingredients"]![1]![0]!["code"]!);
        Json("""[{ "code": "game:leather-normal-plain", "kind": "item", "quantity": 2 }]""", variant["outputs"]!);
        for (int i = 0; i < 4; i++)
            Assert.Single(Recipe($"barrel|game:recipes/barrel/leather.json|{i}")["variants"]!);
    }

    // survival/recipes/alloy/tinbronze.json: tin 8-12 %, copper 88-92 %.
    [AtlasScenario(TimeoutMs = Timeout)]
    public void Alloy_recipe_has_ratios()
    {
        var r = Recipe("alloy|game:recipes/alloy/tinbronze.json|0");
        Json("""
            [
              { "code": "game:ingot-tin", "kind": "item", "quantity": 1, "minRatio": 0.08, "maxRatio": 0.12 },
              { "code": "game:ingot-copper", "kind": "item", "quantity": 1, "minRatio": 0.88, "maxRatio": 0.92 }
            ]
            """, r["ingredients"]!);
        Json("{}", r["alloy"]!);
        Json("""
            [{ "ingredients": [[{ "code": "game:ingot-tin", "kind": "item", "quantity": 1 }],
                               [{ "code": "game:ingot-copper", "kind": "item", "quantity": 1 }]],
               "outputs": [{ "code": "game:ingot-tinbronze", "kind": "item", "quantity": 1 }] }]
            """, r["variants"]!);
    }

    // survival/recipes/cooking/jam.json: 2 honey portions + 2 of fruit-*.
    // survival/recipes/cooking/candle.json: 3 beeswax + 1 flax fibers, cooks into a candle.
    [AtlasScenario(TimeoutMs = Timeout)]
    public void Cooking_recipes_have_slots_with_quantities()
    {
        var jam = Recipe("cooking|game:recipes/cooking/jam.json|0");
        Assert.Equal("jam", (string)jam["cooking"]!["code"]!);
        var slots = (JArray)jam["ingredients"]!;
        Assert.Equal(new[] { "sweetener", "fruit" }, slots.Select(s => (string)s["role"]!));
        Assert.Equal(new[] { 2.0, 2.0 }, slots.Select(s => (double)s["minQuantity"]!));
        Assert.Equal(new[] { 2.0, 2.0 }, slots.Select(s => (double)s["maxQuantity"]!));
        Assert.Equal("game:fruit-*", (string)slots[1]["code"]!);
        var variant = (JObject)Assert.Single(jam["variants"]!);
        Json("""[{ "code": "game:honeyportion", "kind": "item", "quantity": 1 }]""", variant["ingredients"]![0]!);
        // Independent: every registered game:fruit-* item.
        var fruit = World.Api.World.Items
            .Where(i => i?.Code != null && i.Code.Domain == "game" && i.Code.Path.StartsWith("fruit-"))
            .Select(i => i.Code.ToString()).OrderBy(c => c, StringComparer.Ordinal);
        Assert.Equal(fruit, variant["ingredients"]![1]!.Select(s => (string)s["code"]!));
        Assert.Contains("game:fruit-blueberry", fruit);

        var candle = Recipe("cooking|game:recipes/cooking/candle.json|0");
        Json("""[{ "code": "game:candle", "kind": "item", "quantity": 1 }]""", candle["outputs"]!);
        Assert.Equal(new[] { "game:beeswax", "game:flaxfibers" }, Codes(candle["ingredients"]!).Select(c => (string)c!));
        Assert.Equal(new[] { 3.0, 1.0 }, candle["ingredients"]!.Select(s => (double)s["minQuantity"]!));
    }

    // ------------------------------------------------------ mods and patches

    // survival/recipes/smithing/nails.json allows 13 metals. Two mods patch both entries:
    // BetterRuins (patches/others/universalpatches/makes_cupronickelnails_smithable.json)
    // adds "cupronickel", Sprinklers (patches/metalPatch.json) addmerges "brass" and "lead".
    [AtlasScenario(TimeoutMs = Timeout)]
    public void Patched_vanilla_recipe_carries_the_patches()
    {
        string[] vanilla = { "copper", "tinbronze", "bismuth", "bismuthbronze", "blackbronze", "brass", "silver",
                             "gold", "iron", "meteoriciron", "steel", "molybdochalkos", "electrum" };
        var world = World.Api.World;
        foreach (var (index, yield) in new[] { (0, 4), (1, 8) })
        {
            var r = Recipe($"smithing|game:recipes/smithing/nails.json|{index}");
            Assert.Equal("survival", (string)r["mod"]!);
            var allowed = r["ingredients"]![0]!["allowedVariants"]!.Select(v => (string)v!).ToList();
            Assert.Equal(vanilla.Concat(new[] { "cupronickel", "brass", "lead" }), allowed);

            // One variant per allowed metal whose ingot and nails both exist.
            var expected = allowed.Distinct()
                .Where(m => world.GetItem(new AssetLocation($"game:ingot-{m}")) != null &&
                            world.GetItem(new AssetLocation($"game:metalnailsandstrips-{m}")) != null)
                .OrderBy(m => m, StringComparer.Ordinal);
            Assert.Equal(expected, r["variants"]!.Select(v => (string)v["bindings"]!["metal"]!).OrderBy(m => m, StringComparer.Ordinal));
            Assert.Contains(r["variants"]!, v => (string?)v["bindings"]!["metal"] == "lead");

            var cupro = VariantWith(r, "metal", "cupronickel");
            Json($$"""
                { "bindings": { "metal": "cupronickel" },
                  "ingredients": [[{ "code": "game:ingot-cupronickel", "kind": "item", "quantity": 1 }]],
                  "outputs": [{ "code": "game:metalnailsandstrips-cupronickel", "kind": "item", "quantity": {{yield}} }] }
                """, cupro);
        }
    }

    // VintageEngineering's assets/vinteng/recipes/vemetalpress/metal-gears.json, entry 0:
    // metalplate-* named metal (copper, brass, tinbronze, gold) makes metalgear-{metal} and
    // 4 metalbit-{metal}, 250 power per craft. A mod registry with no engine base class.
    [AtlasScenario(TimeoutMs = Timeout)]
    public void Mod_machine_recipe_matches_the_mod_asset()
    {
        var r = Recipe("vintageengineering:vemetalpress|vinteng:recipes/vemetalpress/metal-gears.json|0");
        Assert.Equal("vintageengineering", (string)r["mod"]!);
        Assert.Equal("generic", (string)Doc["recipeTypes"]!["vintageengineering:vemetalpress"]!["shape"]!);
        Json("""
            [{ "code": "game:metalplate-*", "kind": "item", "quantity": 1, "wildcardName": "metal",
               "allowedVariants": ["copper", "brass", "tinbronze", "gold"] }]
            """, r["ingredients"]!);
        Assert.Equal(new[] { "vinteng:metalgear-{metal}", "game:metalbit-{metal}" }, Codes(r["outputs"]!).Select(c => (string)c!));
        Assert.Equal(new[] { 1.0, 4.0 }, r["outputs"]!.Select(o => (double)o["quantity"]!));
        Assert.Equal(250, (int)r["extra"]!["powerPerCraft"]!);
        Json("""
            { "bindings": { "metal": "copper" },
              "ingredients": [[{ "code": "game:metalplate-copper", "kind": "item", "quantity": 1 }]],
              "outputs": [{ "code": "vinteng:metalgear-copper", "kind": "item", "quantity": 1 },
                          { "code": "game:metalbit-copper", "kind": "item", "quantity": 4 }] }
            """, VariantWith(r, "metal", "copper"));
        var world = World.Api.World;
        var expected = new[] { "copper", "brass", "tinbronze", "gold" }
            .Where(m => world.GetItem(new AssetLocation($"game:metalplate-{m}")) != null &&
                        world.GetItem(new AssetLocation($"vinteng:metalgear-{m}")) != null)
            .OrderBy(m => m, StringComparer.Ordinal);
        Assert.Equal(expected, r["variants"]!.Select(v => (string)v["bindings"]!["metal"]!).OrderBy(m => m, StringComparer.Ordinal));
    }

    // ------------------------------------------------------ counts

    /// <summary>
    /// Records per base-game type equal the definitions in the asset files, counted here
    /// with the engine's own asset loader (GetMany&lt;JToken&gt;, as RecipeLoader does):
    /// one per object, one per array entry. Disabled and unresolvable definitions are
    /// records too (without variants), so the numbers are equal, not just close.
    /// </summary>
    [AtlasScenario(TimeoutMs = Timeout)]
    public void Records_per_type_equal_definitions_in_the_assets()
    {
        var api = (ICoreServerAPI)World.Api;
        var counts = Doc["recipes"]!.GroupBy(r => (string)r["type"]!).ToDictionary(g => g.Key, g => g.Count());
        foreach (var type in new[] { "grid", "smithing", "knapping", "clayforming", "barrel", "alloy", "cooking" })
        {
            var definitions = api.Assets.GetMany<JToken>(api.Logger, $"recipes/{type}/")
                .Sum(kv => kv.Value switch { JObject => 1, JArray a => a.Count, _ => 0 });
            Assert.True(definitions == counts.GetValueOrDefault(type),
                $"{type}: {definitions} definitions in the assets, {counts.GetValueOrDefault(type)} records");
            Assert.Equal(counts.GetValueOrDefault(type), (int)Doc["recipeTypes"]![type]!["count"]!);
        }
    }

    /// <summary>
    /// Every recipe the engine registered is exactly one variant: the sum of variants per
    /// type equals the size of the engine's registry, read through the public API.
    /// </summary>
    [AtlasScenario(TimeoutMs = Timeout)]
    public void Variants_per_type_equal_the_engine_registries()
    {
        var api = (ICoreServerAPI)World.Api;
        var engine = new Dictionary<string, int>
        {
            ["grid"] = api.World.GridRecipes.Count,
            ["smithing"] = api.GetSmithingRecipes().Count,
            ["knapping"] = api.GetKnappingRecipes().Count,
            ["clayforming"] = api.GetClayformingRecipes().Count,
            ["barrel"] = api.GetBarrelRecipes().Count,
            ["alloy"] = api.GetMetalAlloys().Count,
            ["cooking"] = api.GetCookingRecipes().Count,
        };
        foreach (var (type, count) in engine)
        {
            var variants = Doc["recipes"]!.Where(r => (string)r["type"]! == type).Sum(r => r["variants"]!.Count());
            Assert.True(count == variants, $"{type}: engine has {count} recipes, export has {variants} variants");
        }

        // All three ladder records together hold exactly the recipes registered under that asset.
        var ladders = api.World.GridRecipes.Count(g => g.Name?.ToString() == "game:recipes/grid/ladder.json");
        var exported = Enumerable.Range(0, 3).Sum(i => Recipe($"grid|game:recipes/grid/ladder.json|{i}")["variants"]!.Count());
        Assert.Equal(ladders, exported);
    }

    // ------------------------------------------------------ whole-document checks

    [AtlasScenario(TimeoutMs = Timeout)]
    public void Document_satisfies_the_structural_invariants()
    {
        var recipes = Doc["recipes"]!.Cast<JObject>().ToList();
        var types = (JObject)Doc["recipeTypes"]!;
        var problems = new List<string>();
        var code = new Regex("^[a-z0-9_-]+:[^\\s:]+$");

        var ids = recipes.Select(r => (string)r["id"]!).ToList();
        if (ids.Distinct().Count() != ids.Count) problems.Add("ids are not unique");
        var sorted = ids.OrderBy(i => i, StringComparer.Ordinal).ToList();
        if (!ids.SequenceEqual(sorted)) problems.Add("recipes are not sorted by id (ordinal)");

        foreach (var (type, entry) in types)
        {
            var n = recipes.Count(r => (string)r["type"]! == type);
            if ((int)entry!["count"]! != n) problems.Add($"recipeTypes[{type}].count is {entry["count"]}, {n} records");
        }
        foreach (var r in recipes)
        {
            var id = (string)r["id"]!;
            var type = (string)r["type"]!;
            if (types[type] == null) problems.Add($"{id}: type {type} is not in recipeTypes");
            if (!id.StartsWith(type + "|")) problems.Add($"{id}: id does not start with its type");
            var ingredients = (JArray)r["ingredients"]!;
            foreach (var v in r["variants"]!)
            {
                if (v["ingredients"]!.Count() != ingredients.Count)
                    problems.Add($"{id}: a variant has {v["ingredients"]!.Count()} slots, the recipe {ingredients.Count}");
                foreach (var s in v["ingredients"]!.SelectMany(slot => slot).Concat(v["outputs"]!))
                    if (!code.IsMatch((string?)s["code"] ?? "")) problems.Add($"{id}: bad code {s["code"]}");
            }
            if (r["grid"] is JObject g)
            {
                int w = (int)g["width"]!, h = (int)g["height"]!;
                var rows = g["pattern"]!.Select(x => (string)x!).ToList();
                var keys = ingredients.Select(i => (string?)i["key"]).ToHashSet();
                if (rows.Count != h || rows.Any(row => row.Length != w))
                    problems.Add($"{id}: pattern is not {h} rows of {w}");
                foreach (var c in string.Concat(rows))
                    if (c != '_' && !keys.Contains(c.ToString())) problems.Add($"{id}: pattern key {c} has no ingredient");
            }
            if (type == "grid" && r["grid"] == null) problems.Add($"{id}: grid recipe without grid");
        }
        Assert.True(problems.Count == 0, $"{problems.Count} problem(s):\n" + string.Join("\n", problems.Take(50)));
    }

    [AtlasScenario(TimeoutMs = Timeout)]
    public void Document_validates_against_the_schema()
    {
        var schema = JsonSchema.FromText(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "recipe-export.schema.json")));
        var instance = JsonNode.Parse(Doc.ToString(Formatting.None));
        var result = schema.Evaluate(instance, new EvaluationOptions { OutputFormat = OutputFormat.List });
        var errors = (result.Details ?? new List<EvaluationResults>())
            .Where(d => d.HasErrors && d.Errors != null)
            .SelectMany(d => d.Errors!.Select(e => $"{d.InstanceLocation}: {e.Value}"))
            .Take(50).ToList();
        Assert.True(result.IsValid, "schema errors:\n" + string.Join("\n", errors));
    }

    [AtlasScenario(TimeoutMs = Timeout)]
    public void Two_exports_are_identical()
    {
        var api = (ICoreServerAPI)World.Api;
        var a = new JObject();
        var b = new JObject();
        var refsA = RecipeSection.Fill(api, a);
        var refsB = RecipeSection.Fill(api, b);
        Assert.Equal(a.ToString(Formatting.None), b.ToString(Formatting.None));
        Assert.Equal(refsA.OrderBy(c => c, StringComparer.Ordinal), refsB.OrderBy(c => c, StringComparer.Ordinal));
    }

    /// <summary>The referenced set holds exactly the codes of every variant stack.</summary>
    [AtlasScenario(TimeoutMs = Timeout)]
    public void Referenced_codes_cover_every_variant_stack()
    {
        var referenced = RecipeSection.Fill((ICoreServerAPI)World.Api, new JObject());
        var inVariants = Doc["recipes"]!.SelectMany(r => r["variants"]!)
            .SelectMany(v => v["ingredients"]!.SelectMany(s => s).Concat(v["outputs"]!))
            .Select(s => (string)s["code"]!).ToHashSet();
        Assert.Empty(inVariants.Except(referenced));
        // And each is a registered collectible.
        var world = World.Api.World;
        var unknown = inVariants.Where(c => world.GetItem(new AssetLocation(c)) == null && world.GetBlock(new AssetLocation(c)) == null).ToList();
        Assert.Empty(unknown);
    }

    // ------------------------------------------------------ failure path

    private sealed class OpaqueRegistry : RecipeRegistryBase
    {
        public override void ToBytes(IWorldAccessor resolver, out byte[] data, out int quantity) { data = Array.Empty<byte>(); quantity = 0; }
        public override void FromBytes(IWorldAccessor resolver, int quantity, byte[] data) { }
    }

    public sealed class ShapelessThing : IByteSerializable
    {
        public string Colour = "red";
        public void ToBytes(BinaryWriter writer) { }
        public void FromBytes(BinaryReader reader, IWorldAccessor resolver) { }
    }

    [AtlasScenario(TimeoutMs = Timeout)]
    public void Registry_that_cannot_be_serialised_makes_the_export_throw()
    {
        var api = (ICoreServerAPI)World.Api;
        var noList = new RegistryInfo { Code = "opaquerecipes", Registry = new OpaqueRegistry(), Mod = "seraphtest" };
        var e1 = Assert.Throws<RecipeExportException>(() => RecipeSection.Fill(api, new JObject(), new[] { noList }));
        Assert.Contains("'opaquerecipes'", e1.Message);

        var unreadable = new RecipeRegistryGeneric<ShapelessThing>(new List<ShapelessThing> { new() });
        var noShape = new RegistryInfo { Code = "shapelessrecipes", Registry = unreadable, Mod = "seraphtest" };
        var e2 = Assert.Throws<RecipeExportException>(() => RecipeSection.Fill(api, new JObject(), new[] { noShape }));
        Assert.Contains("'shapelessrecipes'", e2.Message);

        // The same registry alongside readable ones still fails the whole export.
        var all = Registries.Find(api).Append(noShape).ToList();
        Assert.Throws<RecipeExportException>(() => RecipeSection.Fill(api, new JObject(), all));
    }

    /// <summary>Registries are found by reflection, not listed: every base-game one and the pack's mod ones.</summary>
    [AtlasScenario(TimeoutMs = Timeout)]
    public void Every_registry_is_found_and_exported()
    {
        var api = (ICoreServerAPI)World.Api;
        var codes = Registries.Find(api).Select(r => r.Code).ToHashSet();
        foreach (var expected in new[] { "gridrecipes", "smithingrecipes", "knappingrecipes", "clayformingrecipes",
                                         "barrelrecipes", "alloyrecipes", "cookingrecipes" })
            Assert.Contains(expected, codes);
        // Each one the engine can look up by code (GameMain.GetRecipeRegistry) is in recipeTypes.
        var registries = Doc["recipeTypes"]!.Values().Select(t => (string)t["registry"]!).ToHashSet();
        Assert.Equal(codes.OrderBy(c => c, StringComparer.Ordinal), registries.OrderBy(c => c, StringComparer.Ordinal));
    }
}
