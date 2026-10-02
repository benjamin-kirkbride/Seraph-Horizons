using System.Text.RegularExpressions;
using Atlas.XUnit;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SeraphHorizons.RecipeExport;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace SeraphHorizons.PackTests;

/// <summary>
/// The item half of the export (`mods`, `items`, `guides`). Expected values are read by hand
/// from the asset JSON and English lang files of the game and of the mods, and hard-coded
/// here, so a test fails when the exporter reads the wrong thing, not only when it crashes.
/// Where a mod in the pack patches a vanilla value, the expected value comes from that patch.
/// </summary>
[AtlasWorld]
public class ItemExportScenarios : AtlasScenarioBase
{
    private const int Timeout = 600_000;
    private static readonly Regex Code = new(@"^[a-z0-9_-]+:[^\s:]+$");

    private JObject Doc => ExportUnderTest.Get(World.Api);
    private JObject Items => (JObject)Doc["items"]!;

    private static JObject Item(JObject items, string code)
    {
        Assert.True(items.ContainsKey(code), $"{code} is not exported");
        return (JObject)items[code]!;
    }

    private static JObject Attrs(JObject item) => (JObject)(item["attributes"] ?? new JObject());

    private static IEnumerable<JObject> Sources(JObject item, string type) =>
        (item["sources"] as JArray ?? new JArray()).OfType<JObject>().Where(s => (string?)s["type"] == type);

    // ItemSection.Fill with a referenced set of our choosing, cached per set: a fill takes ~30 s.
    private static readonly Dictionary<string, JObject> Fills = new();

    private JObject Fill(params string[] referenced)
    {
        var key = string.Join(",", referenced);
        lock (Fills)
        {
            if (Fills.TryGetValue(key, out var cached)) return cached;
            var root = new JObject();
            ItemSection.Fill((ICoreServerAPI)World.Api, root, new HashSet<string>(referenced));
            return Fills[key] = root;
        }
    }

    // --- vanilla items against survival/itemtypes and game/lang/en.json -------------------

    [AtlasScenario(TimeoutMs = Timeout)]
    public void Copper_pickaxe_is_a_tier_2_pickaxe_with_300_durability()
    {
        // survival/itemtypes/tool/pickaxe.json: tooltierbytype "*-copper": 2,
        // durabilitybytype "pickaxe-copper": 300. en.json: "item-pickaxe-copper": "Copper pickaxe".
        var item = Item(Items, "game:pickaxe-copper");
        Assert.Equal("Copper pickaxe", (string?)item["name"]);
        Assert.Equal("item", (string?)item["kind"]);
        Assert.Equal("survival", (string?)item["mod"]);
        Assert.True((bool)item["handbookVisible"]!);
        var a = Attrs(item);
        Assert.Equal("pickaxe", (string?)a["tool"]);
        Assert.Equal(2, (int?)a["toolTier"]);
        Assert.Equal(300, (int?)a["durability"]);
        Assert.Null(a["nutrition"]);
    }

    [AtlasScenario(TimeoutMs = Timeout)]
    public void Blueberry_is_fruit_with_80_satiety_and_no_tool_fields()
    {
        // survival/itemtypes/food/fruit.json nutritionPropsByType "*": satiety 80, Fruit.
        var item = Item(Items, "game:fruit-blueberry");
        Assert.Equal("Blueberry", (string?)item["name"]);
        var a = Attrs(item);
        Assert.Equal("fruit", (string?)a["nutrition"]?["category"]);
        Assert.Equal(80.0, (double?)a["nutrition"]?["satiety"]);
        Assert.Null(a["tool"]);
        Assert.Null(a["toolTier"]);
        Assert.Null(a["durability"]);
    }

    [AtlasScenario(TimeoutMs = Timeout)]
    public void Charcoal_burns_at_1300_for_40_seconds()
    {
        // survival/itemtypes/resource/charcoal.json combustibleProps.
        var item = Item(Items, "game:charcoal");
        Assert.Equal("Charcoal", (string?)item["name"]);
        var a = Attrs(item);
        Assert.Equal(1300.0, (double?)a["burn"]?["temperature"]);
        Assert.Equal(40.0, (double?)a["burn"]?["durationSeconds"]);
        Assert.Null(a["nutrition"]);
        Assert.Null(a["toolTier"]);
    }

    [AtlasScenario(TimeoutMs = Timeout)]
    public void Stick_has_its_description_and_no_tool_or_food_fields()
    {
        // en.json "itemdesc-stick"; survival/itemtypes/resource/stick.json burns at 700 for 8 s.
        var item = Item(Items, "game:stick");
        Assert.Equal("Stick", (string?)item["name"]);
        Assert.StartsWith("A simple, but versatile tool", (string?)item["description"]);
        var a = Attrs(item);
        Assert.Equal(700.0, (double?)a["burn"]?["temperature"]);
        Assert.Equal(8.0, (double?)a["burn"]?["durationSeconds"]);
        Assert.Null(a["tool"]);
        Assert.Null(a["toolTier"]);
        Assert.Null(a["nutrition"]);
        Assert.Null(a["attackPower"]);
    }

    [AtlasScenario(TimeoutMs = Timeout)]
    public void Native_copper_nugget_smelts_20_to_one_copper_ingot()
    {
        // survival/itemtypes/resource/nugget.json combustiblePropsByType "*-nativecopper".
        var item = Item(Items, "game:nugget-nativecopper");
        Assert.Equal("Nugget (Native copper)", (string?)item["name"]);
        var s = Attrs(item)["smelting"] as JObject;
        Assert.NotNull(s);
        Assert.Equal(1084.0, (double?)s!["meltingPoint"]);
        Assert.Equal(30.0, (double?)s["durationSeconds"]);
        Assert.Equal(20.0, (double?)s["inputQuantity"]);
        Assert.Equal("game:ingot-copper", (string?)s["output"]?["code"]);
        Assert.Equal("item", (string?)s["output"]?["kind"]);
        Assert.Equal(1.0, (double?)s["output"]?["quantity"]);
    }

    [AtlasScenario(TimeoutMs = Timeout)]
    public void Native_copper_bits_drop_nuggets_and_carry_their_handbook_text()
    {
        // survival/blocktypes/stone/looseores.json dropsByType "*": nugget-{ore}, avg 2, var 1.
        // en.json "block-looseores-nativecopper-*" and "block-handbooktext-looseores-nativecopper-*".
        var nugget = Item(Items, "game:nugget-nativecopper");
        var drop = Sources(nugget, "blockDrop").SingleOrDefault(s => (string?)s["from"] == "game:looseores-nativecopper-granite-free");
        Assert.NotNull(drop);
        Assert.Equal("Native copper bits", (string?)drop!["fromName"]);
        Assert.Equal(2.0, (double?)drop["quantity"]?["avg"]);
        Assert.Equal(1.0, (double?)drop["quantity"]?["var"]);

        var bits = Item(Items, "game:looseores-nativecopper-granite-free");
        Assert.Equal("block", (string?)bits["kind"]);
        Assert.Contains("There's likely a copper deposit right below here", (string?)bits["description"]);
        Assert.Contains("Surface stones containing native copper can be found lying atop the ground", (string?)bits["description"]);
    }

    [AtlasScenario(TimeoutMs = Timeout)]
    public void Rooster_drops_feathers_as_good_hunting_sets_them()
    {
        // Vanilla chicken-adult.json gives 12 +- 4 feathers; goodhunting's
        // assets/game/patches/goodHunting_Chix.json replaces that with 15 +- 5 for roosters.
        var feather = Item(Items, "game:feather");
        Assert.Equal("Feather", (string?)feather["name"]);
        var drop = Sources(feather, "entityDrop").SingleOrDefault(s => (string?)s["from"] == "game:chicken-rooster");
        Assert.NotNull(drop);
        Assert.Equal("Rooster", (string?)drop!["fromName"]);
        Assert.Equal(15.0, (double?)drop["quantity"]?["avg"]);
        Assert.Equal(5.0, (double?)drop["quantity"]?["var"]);
        // chicken-adult.json is code "chicken"; "rooster" is a state of its variant groups.
        Assert.Equal("game:chicken", (string?)drop["extra"]?["entityType"]);
    }

    [AtlasScenario(TimeoutMs = Timeout)]
    public void Agriculture_trader_sells_blueberries()
    {
        // survival/config/tradelists/trader-agriculture.json selling: fruit-blueberry,
        // stacksize 8, price avg 1. en.json "item-creature-trader-*-agriculture-temperate".
        var item = Item(Items, "game:fruit-blueberry");
        var trade = Sources(item, "traderSells").SingleOrDefault(s => (string?)s["from"] == "game:trader-male-agriculture-temperate");
        Assert.NotNull(trade);
        Assert.Equal("Agriculture trader (temperate)", (string?)trade!["fromName"]);
        Assert.Equal(8.0, (double?)trade["quantity"]?["avg"]);
        Assert.Equal(1.0, (double?)trade["price"]);
        // Every survival/entities/humanoid/trader-*.json is code "trader", told apart by variants.
        Assert.Equal("game:trader", (string?)trade["extra"]?["entityType"]);
    }

    [AtlasScenario(TimeoutMs = Timeout)]
    public void Knapping_guide_is_exported_with_its_english_title()
    {
        // survival/config/handbook/02-knapping.json; en.json "craftinginfo-knapping-title".
        var guides = ((JArray)Doc["guides"]!).OfType<JObject>().ToList();
        var knapping = guides.SingleOrDefault(g => (string?)g["code"] == "craftinginfo-knapping");
        Assert.NotNull(knapping);
        Assert.Equal("Crafting Mechanic: Knapping", (string?)knapping!["title"]);
        Assert.Contains("'Knapping' means chipping away at raw stones", (string?)knapping["text"]);
        Assert.Equal("survival", (string?)knapping["mod"]);
        // All 32 vanilla pages (survival/config/handbook/00..31) plus the mods' own.
        Assert.True(guides.Count(g => (string?)g["mod"] == "survival") == 32,
            $"expected 32 vanilla guides, got {guides.Count(g => (string?)g["mod"] == "survival")}");
    }

    // --- items from mods in the pack, against the mods' own files --------------------------

    [AtlasScenario(TimeoutMs = Timeout)]
    public void Primitive_survival_crab_meat_matches_the_mod_files()
    {
        // primitivesurvival_5.1.4.zip: itemtypes/food/crabmeat.json combustiblePropsByType
        // "*-raw" (cook at 150 for 7.5 s into crabmeat-cooked), lang/en.json, and
        // entities/land/crab-landcrab.json (code "landcrab", harvestable 5 +- 2 crab meat).
        var item = Item(Items, "primitivesurvival:crabmeat-raw");
        Assert.Equal("Crab Meat (Raw)", (string?)item["name"]);
        Assert.Equal("primitivesurvival", (string?)item["mod"]);
        Assert.Equal("A delicious source of protein once cooked.", (string?)item["description"]);
        var s = Attrs(item)["smelting"] as JObject;
        Assert.NotNull(s);
        Assert.Equal("cook", (string?)s!["method"]);
        Assert.Equal(150.0, (double?)s["meltingPoint"]);
        Assert.Equal(7.5, (double?)s["durationSeconds"]);
        Assert.False((bool?)s["requiresContainer"]);
        Assert.Equal("primitivesurvival:crabmeat-cooked", (string?)s["output"]?["code"]);

        var drop = Sources(item, "entityDrop").SingleOrDefault(d => (string?)d["from"] == "primitivesurvival:landcrab");
        Assert.NotNull(drop);
        Assert.Equal(5.0, (double?)drop!["quantity"]?["avg"]);
        Assert.Equal(2.0, (double?)drop["quantity"]?["var"]);
        // No variant groups: the type is the entity itself.
        Assert.Equal("primitivesurvival:landcrab", (string?)drop["extra"]?["entityType"]);

        // modinfo.json of the same zip.
        var mod = (JObject)Doc["mods"]!["primitivesurvival"]!;
        Assert.Equal("Primitive Survival", (string?)mod["name"]);
        Assert.Equal("5.1.4", (string?)mod["version"]);
        Assert.Equal(new[] { "SpearAndFang" }, mod["authors"]!.Values<string>());
        Assert.Equal("https://mods.vintagestory.at/primitivesurvival", (string?)mod["website"]);
    }

    [AtlasScenario(TimeoutMs = Timeout)]
    public void Expanded_foods_fruit_bread_matches_the_mod_files()
    {
        // ExpandedFoods 2.0.0-dev.15.zip: itemtypes/food/mixing/berrybread.json (maxstacksize 32,
        // "*-cooked" bakes into berrybread-charred), lang/en.json "item-berrybread-cooked".
        var item = Item(Items, "expandedfoods:berrybread-cooked");
        Assert.Equal("Fruit bread", (string?)item["name"]);
        Assert.Equal("expandedfoods", (string?)item["mod"]);
        Assert.True((bool)item["handbookVisible"]!);
        Assert.Equal(32, (int?)Attrs(item)["maxStackSize"]);
        Assert.Equal("expandedfoods:berrybread-charred", (string?)Attrs(item)["smelting"]?["output"]?["code"]);

        var mod = (JObject)Doc["mods"]!["expandedfoods"]!;
        Assert.Equal("Expanded Foods: Core", (string?)mod["name"]);
        Assert.Equal("2.0.0-dev.15", (string?)mod["version"]);
        Assert.Equal(new[] { "l33tmaan" }, mod["authors"]!.Values<string>());
    }

    // --- scope ---------------------------------------------------------------------------

    [AtlasScenario(TimeoutMs = Timeout)]
    public void Scope_is_the_handbook_plus_referenced_codes()
    {
        // survival/itemtypes/resource/ingot.json: handbook.excludeByType "*-platinum": true.
        // (expanded_matter and VintageEngineering remove the chromium/uranium/titanium
        // exclusions, not platinum.)
        const string hidden = "game:ingot-platinum";
        const string visible = "game:ingot-copper";
        const string unregistered = "game:ingot-seraphexporttest";
        Assert.NotNull(World.Api.World.GetItem(new AssetLocation(hidden)));
        Assert.Null(World.Api.World.GetItem(new AssetLocation(unregistered)));

        var plain = (JObject)Fill()["items"]!;
        Assert.False(plain.ContainsKey(hidden), $"{hidden} is excluded from the handbook but was exported");
        Assert.True((bool)Item(plain, visible)["handbookVisible"]!);
        Assert.False(plain.ContainsKey(unregistered));

        var withRefs = (JObject)Fill(hidden, visible, unregistered)["items"]!;
        var platinum = Item(withRefs, hidden);
        Assert.False((bool)platinum["handbookVisible"]!);
        Assert.Equal("Platinum ingot", (string?)platinum["name"]);
        Assert.Equal("survival", (string?)platinum["mod"]);
        Assert.True((bool)Item(withRefs, visible)["handbookVisible"]!);
        Assert.False(withRefs.ContainsKey(unregistered), "an unregistered referenced code was exported");
        // Referencing codes adds exactly those codes and nothing else.
        Assert.Equal(plain.Count + 1, withRefs.Count);

        // Only a fraction of the registry is in the handbook; exporting everything would not be.
        var registered = World.Api.World.Collectibles.Count(c => c?.Code != null);
        Assert.True(plain.Count < registered * 0.6, $"{plain.Count} of {registered} collectibles exported");
    }

    // --- invariants over the whole document ----------------------------------------------

    [AtlasScenario(TimeoutMs = Timeout)]
    public void Every_item_is_well_formed_and_belongs_to_a_listed_mod()
    {
        var mods = (JObject)Doc["mods"]!;
        var problems = new List<string>();
        var untranslated = new List<string>();
        foreach (var (code, token) in Items)
        {
            var item = (JObject)token!;
            if (!Code.IsMatch(code)) problems.Add($"bad code {code}");
            if (!mods.ContainsKey((string)item["mod"]!)) problems.Add($"{code}: mod {item["mod"]} is not in mods");
            var name = (string?)item["name"];
            if (string.IsNullOrWhiteSpace(name)) problems.Add($"{code}: empty name");
            else if (name.StartsWith(code.Split(':')[0] + ":") && !name.Contains(' '))
            {
                untranslated.Add(code);
                if (item["extra"]?["untranslated"]?.Value<bool>() != true) problems.Add($"{code}: lang key name not flagged");
            }
            foreach (var s in item["sources"] as JArray ?? new JArray())
            {
                if (!Code.IsMatch((string?)s["from"] ?? "")) problems.Add($"{code}: bad source {s["from"]}");
                if (string.IsNullOrEmpty((string?)s["fromName"])) problems.Add($"{code}: source {s["from"]} has no name");
            }
            var output = item["attributes"]?["smelting"]?["output"]?["code"];
            if (output != null && !Code.IsMatch((string)output!)) problems.Add($"{code}: bad smelting output {output}");
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems.Take(50)));

        // Names the game itself has no English for (missing lang entries in the game and in
        // mods, e.g. game:richgravel-* from Wilderlands Panning, hardcorewaterforked blocks).
        // They are flagged, not hidden; this bound catches a regression that loses the lang.
        Assert.True(untranslated.Count < Items.Count / 20,
            $"{untranslated.Count} of {Items.Count} names are lang keys, e.g. {string.Join(", ", untranslated.Take(20))}");
        Assert.DoesNotContain("game:pickaxe-copper", untranslated);
    }

    [AtlasScenario(TimeoutMs = Timeout)]
    public void Every_locked_mod_is_listed_at_its_locked_version()
    {
        var mods = (JObject)Doc["mods"]!;
        var missing = new List<string>();
        foreach (var (id, version, side) in PackLock.Mods)
        {
            if (side == "client") continue;
            if (mods[id] is not JObject mod) missing.Add($"{id} missing");
            else if ((string?)mod["version"] != version) missing.Add($"{id} at {mod["version"]}, locked {version}");
        }
        Assert.True(missing.Count == 0, string.Join("\n", missing));
        foreach (var baseMod in new[] { "game", "survival", "creative" })
            Assert.True(mods.ContainsKey(baseMod), $"base game mod {baseMod} missing");
        Assert.Equal("Survival Mode", (string?)mods["survival"]!["name"]);
    }

    [AtlasScenario(TimeoutMs = Timeout)]
    public void Sections_only_use_fields_the_schema_defines()
    {
        // A structural check against the frozen schema (property names, required fields,
        // enums); the full validation runs on the whole document elsewhere.
        // Not AppContext.BaseDirectory: after boot it points into the game install.
        var dir = Path.GetDirectoryName(typeof(ItemExportScenarios).Assembly.Location)!;
        var schema = JObject.Parse(File.ReadAllText(Path.Combine(dir, "recipe-export.schema.json")));
        var defs = (JObject)schema["$defs"]!;
        var problems = new List<string>();

        void Check(JToken? value, string def, string where)
        {
            if (value is not JObject o) { problems.Add($"{where}: not an object"); return; }
            var d = (JObject)defs[def]!;
            var props = (JObject)d["properties"]!;
            foreach (var p in o.Properties())
                if (!props.ContainsKey(p.Name)) problems.Add($"{where}: {p.Name} is not in $defs/{def}");
            foreach (var r in d["required"] as JArray ?? new JArray())
                if (!o.ContainsKey((string)r!)) problems.Add($"{where}: missing {r}");
        }

        foreach (var (id, mod) in (JObject)Doc["mods"]!) Check(mod, "mod", $"mods.{id}");
        var sourceTypes = defs["source"]!["properties"]!["type"]!["enum"]!.Values<string>().ToHashSet();
        foreach (var (code, item) in Items)
        {
            Check(item, "item", code);
            if (item!["attributes"] is JObject a)
            {
                Check(a, "itemAttributes", code + ".attributes");
                if (a["smelting"]?["output"] != null) Check(a["smelting"]!["output"], "stack", code + ".smelting.output");
                if (a["maxStackSize"] is { } m && (int)m < 1) problems.Add($"{code}: maxStackSize {m}");
            }
            foreach (var s in item["sources"] as JArray ?? new JArray())
            {
                Check(s, "source", code + ".sources");
                if (!sourceTypes.Contains((string?)s["type"])) problems.Add($"{code}: source type {s["type"]}");
            }
        }
        foreach (var g in (JArray)Doc["guides"]!) Check(g, "guide", "guides");
        var nutritionProps = defs["itemAttributes"]!["properties"]!["nutrition"]!["properties"]!.ToObject<JObject>()!;
        foreach (var (code, item) in Items)
            if (item!["attributes"]?["nutrition"] is JObject n)
                foreach (var p in n.Properties())
                    if (!nutritionProps.ContainsKey(p.Name)) problems.Add($"{code}: nutrition.{p.Name}");
        Assert.True(problems.Count == 0, string.Join("\n", problems.Take(50)));
    }

    // --- determinism and language --------------------------------------------------------

    [AtlasScenario(TimeoutMs = Timeout)]
    public void Two_fills_produce_identical_sections()
    {
        var first = Fill();
        var second = new JObject();
        ItemSection.Fill((ICoreServerAPI)World.Api, second, new HashSet<string>());
        foreach (var section in new[] { "mods", "items", "guides" })
        {
            Assert.Equal(first[section]!.ToString(Formatting.None), second[section]!.ToString(Formatting.None));
        }
    }

    [AtlasScenario(TimeoutMs = Timeout)]
    public void Names_are_english_whatever_the_server_language()
    {
        var english = Fill();
        var previous = Lang.CurrentLocale;
        var german = new JObject();
        try
        {
            Lang.ChangeLanguage("de");
            // Precondition: the switch is real, so a leak would show.
            Assert.NotEqual("Copper pickaxe", Lang.Get("game:item-pickaxe-copper"));
            ItemSection.Fill((ICoreServerAPI)World.Api, german, new HashSet<string>());
            Assert.Equal("de", Lang.CurrentLocale);
        }
        finally
        {
            Lang.ChangeLanguage(previous);
        }
        Assert.Equal("Copper pickaxe", (string?)german["items"]!["game:pickaxe-copper"]!["name"]);
        Assert.Equal(english["items"]!.ToString(Formatting.None), german["items"]!.ToString(Formatting.None));
        Assert.Equal(english["guides"]!.ToString(Formatting.None), german["guides"]!.ToString(Formatting.None));
    }
}
