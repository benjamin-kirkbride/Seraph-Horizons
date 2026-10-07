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
/// `recipes[i].switch` and `items[code].switch`, `value`, `floorZero` and `valueSwitches` from it and
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
        // Each type file defines registered codes: its code itself or variants of it.
        foreach (var (name, _, types) in SwitchRegistry.Features())
            foreach (var type in types)
            {
                var code = SwitchRegistry.CodeOf(api, type);
                Assert.True(code != null, $"{name}: {type} has no code");
                Assert.True(SeraphHorizons.Mod.Core.SwitchOwnership.TypeCodePatterns(type.Domain, code!).Any(Matches),
                    $"{name}: nothing registered from {type} ({code})");
            }
        foreach (var hand in SeraphHorizons.Mod.Core.SwitchOwnership.HandListed)
            foreach (var pattern in hand.CodePatterns)
                Assert.True(Matches(pattern), $"{hand.Switch}: no registered code matches {pattern}");
        foreach (var owned in registry.Owned)
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
        Assert.Equal("GearCutter", (string?)Recipe("gearcutter|seraphhorizons:gearblank-steel|0")["switch"]);
        Assert.Equal("GearReclamation", (string?)Recipe("lottery|seraphhorizons:gear-oiled|0")["switch"]);
        Assert.Equal("GearReclamation", (string?)Recipe("picklingtub|seraphhorizons:gear-degreased|game:vinegarportion")["switch"]);
        Assert.Equal("GearBlanks", (string?)Recipe("casting|seraphhorizons:toolmold-black-fired-gearblank|0")["switch"]);
        Assert.Null(Recipe("grid|game:recipes/grid/ladder.json|0")["switch"]);

        Assert.Equal("GearCutter", (string?)Item("seraphhorizons:gearcutter-frame-north")["switch"]);
        Assert.Equal("GearBlanks", (string?)Item("seraphhorizons:gearblank-steel")["switch"]);
        Assert.Equal("GearReclamation", (string?)Item("seraphhorizons:picklingtub")["switch"]);
        // The debarked trunks: Logging Expanded's trunk with the state the Rosser switch's patch adds.
        var debarked = World.Api.World.Blocks.Where(b => b?.Code != null && b.Code.Domain == "loggingmod"
                                                         && b.Code.Path.StartsWith("treetrunk-") && b.Code.Path.Contains("-debarked-")).ToList();
        Assert.NotEmpty(debarked);
        Assert.All(debarked, b => Assert.Equal("Rosser", SwitchRegistry.For(World.Api).SwitchForCode(b.Code.ToString())));
        foreach (var b in debarked.Where(b => Doc["items"]![b.Code.ToString()] != null))
            Assert.Equal("Rosser", (string?)Item(b.Code.ToString())["switch"]);
        // The steel gear exists whatever the switches: made by the cutter or reclaimed, it is not owned.
        Assert.Null(Item("seraphhorizons:gear-steel")["switch"]);
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
        foreach (var (code, token) in (JObject)Doc["items"]!)
        {
            var item = (JObject)token!;
            if (values[code] is { } v)
            {
                Assert.Equal((double)v, (double)item["value"]!);
                Assert.Equal(floorZero.Contains(code), item["floorZero"] != null);
                Assert.True(JToken.DeepEquals(switches?[code] is JArray { Count: > 0 } s ? s : null, item["valueSwitches"]), code);
            }
            else
            {
                Assert.Null(item["value"]);
                Assert.Null(item["valueSwitches"]);
            }
        }
        // The steel gear's every route goes through a feature: the gear cutter (blanks too) or
        // the reclamation line. Once the table prices it (a table built from an export that has
        // the gear chain), its value says so.
        if (values["seraphhorizons:gear-steel"] != null)
        {
            var gear = Item("seraphhorizons:gear-steel");
            Assert.NotNull(gear["value"]);
            var depends = ((JArray?)gear["valueSwitches"])?.Values<string>().ToList() ?? [];
            Assert.True(depends.Contains("GearCutter") || depends.Contains("GearReclamation"),
                $"the steel gear's value depends on {string.Join(", ", depends)}");
        }
        Assert.Equal(ItemValuesSystem.For(World.Api).Count, values.Count);
    }
}
