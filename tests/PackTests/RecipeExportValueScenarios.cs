using Atlas.XUnit;
using Newtonsoft.Json.Linq;
using SeraphHorizons.Mod;
using SeraphHorizons.Mod.Trading.Values;
using SeraphHorizons.RecipeExport.Recipes;
using Vintagestory.API.Common;
using Vintagestory.API.Util;

namespace SeraphHorizons.PackTests;

/// <summary>
/// Switch ownership and item values in the export (#506, #523): the mod's
/// <see cref="SwitchRegistry"/> names real assets and codes, and the exporter writes
/// `recipes[i].switch` and `items[code].switch`, `value`, `floorZero`, `valuePerLitre` and `valueSwitches` from it and
/// from the shipped table. Expected owners are read off the features' own files: the gear cutter's
/// recipes are in recipes/grid/gearcutter.json, its frame in blocktypes/gearcutter/frame.json.
/// </summary>
public partial class RecipeExportScenarios
{
    private JObject Item(string code) =>
        Doc["items"]![code] as JObject ?? throw new Xunit.Sdk.XunitException($"no item {code}");

    [AtlasScenario(TimeoutMs = Timeout)]
    public void Registry_names_only_assets_and_codes_that_exist()
    {
        var api = World.Api;
        foreach (var (name, recipes, types) in SwitchRegistry.Features())
        {
            Assert.True(typeof(SeraphHorizonsConfig).GetProperty(name)?.PropertyType == typeof(bool), $"{name} is not a switch");
            foreach (var asset in recipes.Concat(types))
                Assert.True(api.Assets.TryGet(asset) != null, $"{name}: {asset} is missing");
        }
        var registry = SwitchRegistry.For(api);
        var codes = api.World.Collectibles.Where(c => c?.Code != null && !c.IsMissing).Select(c => c.Code.ToString()).ToList();
        bool Matches(string pattern) => codes.Any(c => WildcardUtil.Match(new AssetLocation(pattern), new AssetLocation(c)));
        // A switch off by default (OreProcessing) registers nothing in this world; its codes are
        // checked where it is on (OreProcessingScenarios).
        var config = SeraphHorizonsSystem.ConfigFor(api);
        bool On(string name) => typeof(SeraphHorizonsConfig).GetProperty(name)?.GetValue(config) is true;
        // Each type file defines registered codes: its code itself or variants of it.
        foreach (var (name, _, types) in SwitchRegistry.Features().Where(f => On(f.Switch)))
            foreach (var type in types)
            {
                var code = SwitchRegistry.CodeOf(api, type);
                Assert.True(code != null, $"{name}: {type} has no code");
                Assert.True(SeraphHorizons.Mod.Core.SwitchOwnership.TypeCodePatterns(type.Domain, code!).Any(Matches),
                    $"{name}: nothing registered from {type} ({code})");
            }
        foreach (var hand in SeraphHorizons.Mod.Core.SwitchOwnership.HandListed.Where(h => On(h.Switch)))
            foreach (var pattern in hand.CodePatterns)
                Assert.True(Matches(pattern), $"{hand.Switch}: no registered code matches {pattern}");
        foreach (var owned in registry.Owned.Where(o => On(o.Switch)))
        {
            foreach (var type in owned.RecipeTypes)
                Assert.True(Doc["recipeTypes"]![type] != null, $"{owned.Switch}: the export has no recipe type {type}");
        }
    }

    [AtlasScenario(TimeoutMs = Timeout)]
    public void Recipes_and_items_carry_the_switch_that_adds_them()
    {
        // recipes/grid/gearcutter.json, entry 0: the frame.
        Assert.Equal("GearCutter", (string?)Recipe("grid|seraphhorizons:recipes/grid/gearcutter.json|0")["switch"]);
        Assert.Equal("GearCutter", (string?)Recipe("gearcutter|seraphhorizons:gearblank-stainlesssteel|0")["switch"]);
        Assert.Equal("GearReclamation", (string?)Recipe("lottery|seraphhorizons:gear-neutralized|0")["switch"]);
        Assert.Equal("GearReclamation", (string?)Recipe("picklingtub|seraphhorizons:gear-degreased|game:vinegarportion")["switch"]);
        Assert.Equal("GearBlanks", (string?)Recipe("casting|seraphhorizons:toolmold-black-fired-gearblank|0")["switch"]);
        Assert.Null(Recipe("grid|game:recipes/grid/ladder.json|0")["switch"]);

        Assert.Equal("GearCutter", (string?)Item("seraphhorizons:gearcutter-frame-north")["switch"]);
        Assert.Equal("GearBlanks", (string?)Item("seraphhorizons:gearblank-stainlesssteel")["switch"]);
        Assert.Equal("GearReclamation", (string?)Item("seraphhorizons:picklingtub")["switch"]);
        // The debarked trunks: Logging Expanded's trunk with the state the Rosser switch's patch adds.
        var debarked = World.Api.World.Blocks.Where(b => b?.Code != null && b.Code.Domain == "loggingmod"
                                                         && b.Code.Path.StartsWith("treetrunk-") && b.Code.Path.Contains("-debarked-")).ToList();
        Assert.NotEmpty(debarked);
        Assert.All(debarked, b => Assert.Equal("Rosser", SwitchRegistry.For(World.Api).SwitchForCode(b.Code.ToString())));
        foreach (var b in debarked.Where(b => Doc["items"]![b.Code.ToString()] != null))
            Assert.Equal("Rosser", (string?)Item(b.Code.ToString())["switch"]);
        // The stainless gear exists whatever the switches: made by the cutter or reclaimed, it is not owned.
        Assert.Null(Item("seraphhorizons:gear-stainless")["switch"]);
        Assert.Null(Item("game:gear-rusty")["switch"]);
    }

    [AtlasScenario(TimeoutMs = Timeout)]
    public void Items_carry_the_table_value_and_its_switches()
    {
        var table = Switches.Table((Vintagestory.API.Server.ICoreServerAPI)World.Api)!;
        Assert.Equal(1.0, (double)Item("game:gear-rusty")["value"]!);
        var values = (JObject)table["values"]!;
        var switches = table["switches"] as JObject;
        var floorZero = ((JArray?)table["floorZero"])?.Values<string>().ToHashSet() ?? [];
        var perLitre = table["perLitre"] as JObject;
        foreach (var (code, token) in (JObject)Doc["items"]!)
        {
            var item = (JObject)token!;
            if (values[code] is { } v)
            {
                Assert.Equal((double)v, (double)item["value"]!);
                Assert.Equal(floorZero.Contains(code), item["floorZero"] != null);
                Assert.Equal(perLitre?[code] != null, item["valuePerLitre"] != null);
                Assert.True(JToken.DeepEquals(switches?[code] is JArray { Count: > 0 } s ? s : null, item["valueSwitches"]), code);
            }
            else
            {
                Assert.Null(item["value"]);
                Assert.Null(item["valuePerLitre"]);
                Assert.Null(item["valueSwitches"]);
            }
        }
        // The stainless gear's every route goes through a feature: the gear cutter (fed by the blanks)
        // or the reclamation line, whichever is cheaper. The shipped table is built from an export
        // that carries the mod, so it prices the gear and says so.
        Assert.NotNull(values["seraphhorizons:gear-stainless"]);
        var gear = Item("seraphhorizons:gear-stainless");
        Assert.NotNull(gear["value"]);
        var depends = ((JArray?)gear["valueSwitches"])?.Values<string>().ToList() ?? [];
        Assert.True(depends.Contains("GearCutter") || depends.Contains("GearReclamation"), string.Join(", ", depends));
        Assert.Equal(new string?[] { "GearReclamation" }, ((JArray?)Item("seraphhorizons:picklingtub")["valueSwitches"])?.Values<string>().ToArray());
        Assert.Equal(ItemValuesSystem.For(World.Api).Count, values.Count);
    }

    [AtlasScenario(TimeoutMs = Timeout)]
    public void Liquids_are_valued_per_litre_and_solids_per_item()
    {
        // The table prices liquids by the litre (its perLitre, from the export's
        // attributes.extra.liquid) and the export says so on the item; the value is as the table has it.
        var cider = Item("game:ciderportion-apple");
        Assert.NotNull(cider["value"]);
        Assert.True((bool?)cider["valuePerLitre"]);
        var copper = Item("game:ingot-copper");
        Assert.NotNull(copper["value"]);
        Assert.Null(copper["valuePerLitre"]);
        // Every item priced per litre is a liquid.
        foreach (var (code, token) in (JObject)Doc["items"]!)
            if (token?["valuePerLitre"] != null)
                Assert.True(token["attributes"]?["extra"]?["liquid"] != null, $"{code} is valued per litre but is no liquid");
    }
}
