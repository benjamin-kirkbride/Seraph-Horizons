using Atlas.XUnit;
using Newtonsoft.Json.Linq;
using SeraphHorizons.RecipeExport;
using Vintagestory.API.Common;

namespace SeraphHorizons.PackTests;

/// <summary>
/// The gear chain's shapes (#483): the pickling tub's rules (PicklingTubSettings' defaults in
/// mods-src/seraphhorizons/PicklingTub/Core/TubConfig.cs), the oiled gear's lottery
/// (GearReclamationSettings), the gear cutter's process (config/gearcutter-rig.json and the
/// cutter's agreed defaults) and casting in tool molds (the molds' blocktypes). Values are written
/// down from those files.
/// </summary>
public partial class RecipeExportScenarios
{
    private IEnumerable<JObject> OfType(string type) =>
        Doc["recipes"]!.Cast<JObject>().Where(r => (string)r["type"]! == type);

    private bool Registered(string code) =>
        World.Api.World.GetItem(new AssetLocation(code)) is { IsMissing: false, Code: not null };

    [AtlasScenario(TimeoutMs = Timeout)]
    public void Pickling_tub_rules_are_tub_records()
    {
        Json("""{ "name": "Pickling tub", "shape": "tub", "registry": "PicklingTubSettings", "mod": "seraphhorizons" }""",
            new JObject(((JObject)Doc["recipeTypes"]![RecipeSection.TubType]!).Properties().Where(p => p.Name != "count")));

        // TubConfig.AcidRules: degreased gears in vinegar, 24 hours, eaten a gear every 3 after 12.
        var vinegar = Recipe("picklingtub|seraphhorizons:gear-degreased|game:vinegarportion");
        Assert.Equal("seraphhorizons", (string)vinegar["mod"]!);
        Json("""
            [
              { "code": "seraphhorizons:gear-degreased", "kind": "item", "quantity": 1, "role": "batch" },
              { "code": "game:vinegarportion", "kind": "item", "quantity": 1, "litres": 1, "role": "liquid" },
              { "code": "seraphhorizons:picklingtub", "kind": "block", "quantity": 1, "role": "station" }
            ]
            """, vinegar["ingredients"]!);
        Json("""
            [
              { "code": "seraphhorizons:gear-pickled", "kind": "item", "quantity": 1 },
              { "code": "game:metalbit-steel", "kind": "item", "quantity": 1 }
            ]
            """, vinegar["outputs"]!);
        Json("""{ "kind": "pickle", "hours": 24, "batchSize": 8, "litresPerBatch": 1, "graceHours": 12, "lossEveryHours": 3, "failure": 1 }""",
            vinegar["tub"]!);
        Assert.Equal(new[] { "seraphhorizons:gear-pickled", "game:metalbit-steel" }, Codes(vinegar["variants"]![0]!["outputs"]!).Select(c => (string)c!));

        // Sulfuric is 8 hours; the dip that takes a steel gear bare, 2.
        Assert.Equal(8, (double)Recipe("picklingtub|seraphhorizons:gear-degreased|game:acid-full-sulfuric")["tub"]!["hours"]!);
        Assert.Equal(2, (double)Recipe("picklingtub|seraphhorizons:gear-steel|game:acid-full-sulfuric")["tub"]!["hours"]!);
        // Hydrochloric acid is Expanded Matter's; its rule is exported when the acid is registered.
        var hcl = OfType(RecipeSection.TubType).SingleOrDefault(r => (string)r["id"]! == "picklingtub|seraphhorizons:gear-degreased|game:acid-full-hydrochloric");
        Assert.Equal(Registered("game:acid-full-hydrochloric"), hcl != null);
        if (hcl != null) Assert.Equal(2, (double)hcl["tub"]!["hours"]!);

        // The brine bath: 48 hours, 4 for bare gears, each gear 10% to bits, no loss by time.
        Json("""{ "kind": "rust", "hours": 48, "batchSize": 8, "litresPerBatch": 1, "lossChance": 0.1, "failure": 1 }""",
            Recipe("picklingtub|seraphhorizons:gear-steel|game:brineportion")["tub"]!);
        Json("""{ "kind": "rust", "hours": 4, "batchSize": 8, "litresPerBatch": 1, "lossChance": 0.1, "failure": 1 }""",
            Recipe("picklingtub|seraphhorizons:gear-steel-bare|game:brineportion")["tub"]!);
        Assert.Equal(new[] { "game:gear-rusty", "game:metalbit-steel" }, Codes(Recipe("picklingtub|seraphhorizons:gear-steel|game:brineportion")["outputs"]!).Select(c => (string)c!));
    }

    [AtlasScenario(TimeoutMs = Timeout)]
    public void Oiled_gear_is_a_lottery_of_one_in_ten()
    {
        Json("""{ "name": "Decided on pickup", "count": 1, "shape": "lottery", "registry": "GearReclamationSettings", "mod": "seraphhorizons" }""",
            Doc["recipeTypes"]![RecipeSection.LotteryType]!);
        var r = Recipe("lottery|seraphhorizons:gear-oiled|0");
        Json("""[{ "code": "seraphhorizons:gear-oiled", "kind": "item", "quantity": 1 }]""", r["ingredients"]!);
        Json("""
            [
              { "code": "seraphhorizons:gear-steel", "kind": "item", "quantity": 1 },
              { "code": "game:metalbit-steel", "kind": "item", "quantity": 1 }
            ]
            """, r["outputs"]!);
        Json("""{ "trigger": "inventory", "outcomes": [{ "chance": 0.1, "outputs": [0] }, { "chance": 0.9, "outputs": [1] }] }""", r["lottery"]!);
    }

    [AtlasScenario(TimeoutMs = Timeout)]
    public void Gear_cutter_process_is_a_machine_record_per_blank_size()
    {
        var records = OfType(RecipeSection.CutterType).ToList();
        Assert.Equal(new[] { "gearcutter|seraphhorizons:gearblank-steel|0", "gearcutter|seraphhorizons:largegearblank-steel|0" },
            records.Select(r => (string)r["id"]!));
        Assert.Equal("machine", (string)Doc["recipeTypes"]![RecipeSection.CutterType]!["shape"]!);

        // The rig: 12 axle turns per tooth, 12 teeth on the temporal gear master, 20 on the large one.
        // The cutter's defaults: kit wear 10 a small gear (17 a large: 10 x 20 / 12, rounded up),
        // oil 10 points a small gear, double a large, from a 1000-point tank.
        foreach (var (r, blank, master, gear, teeth, wear, oil) in new[]
                 {
                     (records[0], "seraphhorizons:gearblank-steel", "game:gear-temporal", "seraphhorizons:gear-steel", 12, 10, 10),
                     (records[1], "seraphhorizons:largegearblank-steel", "game:largegear-temporal", "seraphhorizons:largegear-steel", 20, 17, 20),
                 })
        {
            var ingredients = (JArray)r["ingredients"]!;
            Assert.Equal(new[] { blank, master, "seraphhorizons:gearcutterkit-steel" }, ingredients.Take(3).Select(i => (string)i["code"]!));
            Assert.Equal("kept", (string)ingredients[1]["role"]!);
            Assert.True((bool)ingredients[2]["isTool"]!);
            Assert.Equal(wear, (int)ingredients[2]["toolDurabilityCost"]!);
            Assert.Equal("oil", (string)ingredients[3]["role"]!);
            Assert.Equal(oil / 100.0, (double)ingredients[3]["litres"]!, 6);
            Json($$"""{ "code": "seraphhorizons:gearcutter-frame-north", "kind": "block", "quantity": 1, "role": "station" }""", ingredients[4]);
            Json($$"""[{ "code": "{{gear}}", "kind": "item", "quantity": 1 }]""", r["outputs"]!);
            Json($$"""
                {
                  "power": "mechanical", "turns": {{teeth * 12}},
                  "work": { "amount": {{teeth}}, "unit": "teeth", "turnsPerUnit": 12 },
                  "kept": [1],
                  "wear": { "ingredient": 2, "rule": "dividedByOilFill" },
                  "oil": { "ingredient": 3, "points": {{oil}}, "tank": 1000 }
                }
                """, r["machine"]!);

            var variant = r["variants"]![0]!;
            Assert.Equal(new[] { blank }, Codes(variant["ingredients"]![0]!).Select(c => (string)c!));
            Assert.Equal(new[] { master }, Codes(variant["ingredients"]![1]!).Select(c => (string)c!));
            // The kit and the frame are the cutter's gameplay's (#480, #481): in the variant once registered.
            Assert.Equal(Registered("seraphhorizons:gearcutterkit-steel"), variant["ingredients"]![2]!.Any());
            // Every oil MachineOil takes: the game's flax oil among them, the litres on each.
            var oils = variant["ingredients"]![3]!;
            Assert.Contains("game:oilportion-flax", Codes(oils).Select(c => (string)c!));
            Assert.All(oils, o => Assert.Equal(oil / 100.0, (double)o["litres"]!, 6));
            Assert.Equal(new[] { gear }, Codes(variant["outputs"]!).Select(c => (string)c!));
        }
    }

    /// <summary>The gear cutter's frame and kit are the cutter's gameplay's (#480, #481, built on its
    /// own branch); until it is merged their links have no page. Empty this at the merge.</summary>
    private static readonly string[] PendingCutterPages =
        ["block-seraphhorizons:gearcutter-frame-north", "item-seraphhorizons:gearcutterkit-steel"];

    // The gear chain's guide page (#483) and the sections that point at it: every handbook link
    // opens a page, and the guide is exported.
    [AtlasScenario(TimeoutMs = Timeout)]
    public void Gear_chain_guide_links_open_a_page()
    {
        var keys = new[]
        {
            "seraphhorizons:gearreclamation-text", "seraphhorizons:gearreclamation-section-text",
            "seraphhorizons:gearreclamation-large-text", "seraphhorizons:gearreclamation-rusty-text",
        };
        var links = keys.SelectMany(k => System.Text.RegularExpressions.Regex.Matches(Vintagestory.API.Config.Lang.Get(k), "handbook://([^\"]+)\"")
                .Select(m => m.Groups[1].Value))
            .Distinct().Order().ToList();
        Assert.True(links.Count > 15, $"only {links.Count} links");
        var broken = new List<string>();
        foreach (var link in links)
        {
            if (link == "seraphhorizons-gearreclamation") continue;
            var world = World.Api.World;
            CollectibleObject? found = link.StartsWith("item-") ? world.GetItem(new AssetLocation(link["item-".Length..]))
                : link.StartsWith("block-") ? world.GetBlock(new AssetLocation(link["block-".Length..]))
                : null;
            if (found == null || found.Id == 0 || found.Code == null || found.IsMissing)
            {
                if (!PendingCutterPages.Contains(link)) broken.Add($"{link}: no such page");
            }
            else if (found.Attributes?["handbook"]?["exclude"].AsBool(false) == true)
                broken.Add($"{link}: excluded from the handbook");
            else if (!(found.CreativeInventoryTabs?.Length > 0 || found.CreativeInventoryStacks?.Length > 0))
                broken.Add($"{link}: has no handbook page (not in the creative inventory)");
        }
        Assert.Empty(broken);

        var guide = Assert.Single(Doc["guides"]!, g => (string?)g["code"] == "seraphhorizons-gearreclamation");
        Assert.Equal("Gears: reclaiming, cutting, rusting", (string)guide["title"]!);
        Assert.Contains("grey and clean", (string)guide["text"]!);
        Assert.DoesNotContain("furnace", (string)guide["text"]!);
    }

    [AtlasScenario(TimeoutMs = Timeout)]
    public void Tool_molds_are_casting_records_with_a_variant_per_metal()
    {
        Json("""{ "name": "Casting", "shape": "generic", "registry": "BlockToolMold", "mod": "game" }""",
            new JObject(((JObject)Doc["recipeTypes"]![RecipeSection.CastingType]!).Properties().Where(p => p.Name != "count")));

        // seraphhorizons blocktypes/clay/gearblankmold-fired.json: 100 units, steel only, ten colours.
        var blank = Recipe("casting|seraphhorizons:toolmold-black-fired-gearblank|0");
        Assert.Equal("seraphhorizons", (string)blank["mod"]!);
        Json("""
            [
              { "code": "seraphhorizons:toolmold-*-fired-gearblank", "kind": "block", "quantity": 1, "role": "station" },
              { "code": "game:ingot-*", "kind": "item", "quantity": 1, "role": "metal", "wildcardName": "metal", "extra": { "units": 100 } }
            ]
            """, blank["ingredients"]!);
        Json("""[{ "code": "seraphhorizons:gearblank-{metal}", "kind": "item", "quantity": 1 }]""", blank["outputs"]!);
        var steel = Assert.Single(blank["variants"]!);
        Json("""{ "metal": "steel" }""", steel["bindings"]!);
        Assert.Equal(10, steel["ingredients"]![0]!.Count());
        Json("""[{ "code": "game:ingot-steel", "kind": "item", "quantity": 1 }]""", steel["ingredients"]![1]!);
        Json("""[{ "code": "seraphhorizons:gearblank-steel", "kind": "item", "quantity": 1 }]""", steel["outputs"]!);

        // The large blank mold takes 200 units: two ingots.
        var large = Recipe("casting|seraphhorizons:toolmold-black-fired-largegearblank|0");
        Json("""[{ "code": "game:ingot-steel", "kind": "item", "quantity": 2 }]""", Assert.Single(large["variants"]!)["ingredients"]![1]!);

        // survival blocktypes/clay/fired/toolmold.json: the anvil mold takes 900 units, and copper
        // casts an axe head ({tooltype}head-{metal}).
        var anvil = Recipe("casting|game:toolmold-black-fired-anvil|0");
        Assert.Equal(9, (double)anvil["ingredients"]![1]!["quantity"]!);
        var axe = VariantWith(Recipe("casting|game:toolmold-black-fired-axe|0"), "metal", "copper");
        Json("""[{ "code": "game:axehead-copper", "kind": "item", "quantity": 1 }]""", axe["outputs"]!);
    }
}
