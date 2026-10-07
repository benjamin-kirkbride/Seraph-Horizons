using System.Text.Json.Nodes;
using SeraphHorizons.Mod.Pipes.Core;

namespace SeraphHorizons.Tests.Pipes;

/// <summary>Unified pipes (Pipes/Core): the burst figures, the lead rule, the recipe cost filter
/// and the guard on ppex's assets, with the shipped patch and recipes held to the guard.</summary>
public class UnifiedPipesTests
{
    [Theory]
    [InlineData("lead", 0.5f)]
    [InlineData("copper", 3f)]
    [InlineData("iron", 5f)]
    [InlineData("steel", 10f)]
    [InlineData("tinbronze", 5f)]
    [InlineData("bismuthbronze", 5f)]
    [InlineData("blackbronze", 5f)]
    public void Each_metal_has_its_default_figure(string material, float atm) =>
        Assert.Equal(atm, new UnifiedPipesConfig().BurstPressureFor(material));

    [Theory]
    [InlineData(null)]
    [InlineData("gold")]
    [InlineData("")]
    public void An_unknown_metal_keeps_ppex_figure(string? material) =>
        Assert.Null(new UnifiedPipesConfig().BurstPressureFor(material));

    [Fact]
    public void Lead_is_below_every_gas_producer_and_copper_below_a_choked_cornish_boiler()
    {
        var s = UnifiedPipesConfig.Defaults;
        Assert.True(s.LeadBurstPressure < 0.8f); // ppex's boiler exhaust
        Assert.True(s.CopperBurstPressure < 5f); // a choked Cornish boiler
        Assert.True(s.CopperBurstPressure > 2.5f); // blast air for the converter
        Assert.True(s.LeadBurstPressure < s.CopperBurstPressure && s.CopperBurstPressure < s.IronBurstPressure
                    && s.IronBurstPressure < s.SteelBurstPressure);
    }

    [Fact]
    public void Settings_out_of_range_fall_back_to_the_defaults()
    {
        var s = new UnifiedPipesConfig
        {
            LeadBurstPressure = -1, CopperBurstPressure = float.NaN, IronBurstPressure = 0,
            SteelBurstPressure = 5000, BronzeBurstPressure = 7,
        };
        var fixes = s.Sanitise();
        Assert.Equal(4, fixes.Count);
        Assert.Contains("LeadBurstPressure -1 is out of range, using 0.5", fixes);
        Assert.Equal(0.5f, s.LeadBurstPressure);
        Assert.Equal(3f, s.CopperBurstPressure);
        Assert.Equal(5f, s.IronBurstPressure);
        Assert.Equal(10f, s.SteelBurstPressure);
        Assert.Equal(7f, s.BronzeBurstPressure);
        Assert.Empty(new UnifiedPipesConfig().Sanitise());
    }

    [Theory]
    [InlineData(0.5f, "0.5")]
    [InlineData(3f, "3")]
    [InlineData(12.25f, "12.25")]
    public void Figures_print_without_trailing_zeros(float atm, string text) =>
        Assert.Equal(text, UnifiedPipesConfig.Format(atm));

    [Theory]
    [InlineData(false, 10f, "Steam", true)]
    [InlineData(false, 10f, "Exhaust", true)]
    [InlineData(false, 10f, "Air", false)]
    [InlineData(false, 0f, "Steam", false)] // an empty run
    [InlineData(true, 10f, "Water", false)]
    [InlineData(true, 10f, "Steam", false)] // a liquid never bursts lead
    [InlineData(false, 10f, "", false)]
    [InlineData(false, 10f, null, false)]
    public void Lead_bursts_on_steam_and_exhaust_only(bool isLiquid, float volume, string? medium, bool bursts) =>
        Assert.Equal(bursts, PipeRules.LeadBursts(isLiquid, volume, medium));

    [Theory]
    [InlineData("ppex", "ppex", true)]
    [InlineData("ppex", "seraphhorizons", false)]
    [InlineData("ppex", null, false)]
    [InlineData("smex", "smex", true)]
    [InlineData("smex", "seraphhorizons", true)]
    [InlineData("game", "seraphhorizons", true)]
    public void Ppex_cost_levels_touch_only_ppex_recipes(string outputDomain, string? recipeDomain, bool kept) =>
        Assert.Equal(kept, PipeRules.KeepsCostRecipe(outputDomain, recipeDomain));

    // ppex 0.7.1's straight pipe, trimmed to what the guard reads.
    private const string Straight = """
        {
          "code": "pipe",
          "variantgroups": [
            { "code": "type", "states": ["straight"] },
            { "code": "orientation", "states": ["ns", "we", "ud"] },
            { "code": "material", "states": ["iron", "steel"] }
          ],
          "texturesByType": {
            "*-iron": { "iron4": { "base": "game:block/metal/sheet-plain/iron4" } },
            "*-steel": { "iron4": { "base": "game:block/metal/sheet-plain/steel4" } }
          }
        }
        """;

    [Fact]
    public void Guard_passes_ppex_pipe_as_it_ships() =>
        Assert.Null(PipeAssetGuard.CheckBlocktype("straight", Straight, PipeRules.AddedPipeMaterials));

    [Theory]
    [InlineData("\"material\", \"states\": [\"iron\", \"steel\"]", "\"material\", \"states\": [\"iron\", \"steel\", \"copper\"]")]
    [InlineData("\"material\", \"states\": [\"iron\", \"steel\"]", "\"metal\", \"states\": [\"iron\", \"steel\"]")]
    [InlineData("\"*-steel\": { \"iron4\"", "\"*-steel\": { \"steel4\"")]
    public void Guard_refuses_a_changed_pipe(string from, string to) =>
        Assert.NotNull(PipeAssetGuard.CheckBlocktype("straight", Straight.Replace(from, to), PipeRules.AddedPipeMaterials));

    [Fact]
    public void Guard_refuses_a_pipe_that_already_has_copper()
    {
        var json = Straight.Replace("\"*-steel\":", "\"*-copper\": { \"iron4\": {} }, \"*-steel\":");
        Assert.NotNull(PipeAssetGuard.CheckBlocktype("straight", json, PipeRules.AddedPipeMaterials));
    }

    private static string Recipes(Func<int, string> output) =>
        "[" + string.Join(",", Enumerable.Range(0, 11).Select(i => $"{{ \"output\": {{ \"code\": \"{output(i)}\" }} }}")) + "]";

    private static readonly string[] PpexOutputs =
    [
        "ppex:pipe-straight-ns-{metal}", "ppex:pipe-bend-nw-{metal}", "ppex:pipe-tjunction-uns-{metal}",
        "ppex:pipe-xjunction-nswe-{metal}", "ppex:pipe-passthrough-{brick}-ns", "ppex:pipe-passthroughbend-{brick}-nw",
        "ppex:pipe-outlet-{brick}-n", "ppex:pipe-valve-sn-{metal}", "ppex:pipe-pressurevalve-sn-{metal}",
        "ppex:pipe-valve-sn-{metal}", "ppex:pipe-pressurevalve-sn-{metal}",
    ];

    [Fact]
    public void Guard_passes_ppex_recipes_as_they_ship_and_refuses_moved_ones()
    {
        Assert.Null(PipeAssetGuard.CheckRecipes(Recipes(i => PpexOutputs[i])));
        // a recipe inserted before the valves moves them
        Assert.NotNull(PipeAssetGuard.CheckRecipes(Recipes(i => i == 7 ? "ppex:pipe-outlet-{brick}-n" : PpexOutputs[i])));
        Assert.NotNull(PipeAssetGuard.CheckRecipes("[]"));
    }

    private static JsonArray Shipped(string name) => (JsonArray)PipeAssetGuard.Parse(File.ReadAllText(name))!;

    [Fact]
    public void Shipped_patch_switches_off_exactly_the_guarded_recipes_and_adds_only_the_guarded_states()
    {
        var patch = Shipped("unifiedpipes-ppex.json");
        Assert.All(patch, op => Assert.Equal("ppex", (string?)op!["dependsOn"]![0]!["modid"]));
        var disabled = patch.Where(op => (string?)op!["file"] == "ppex:" + PipeAssetGuard.RecipeFile)
            .Select(op => (string)op!["path"]!).ToList();
        Assert.Equal(PipeAssetGuard.DisabledRecipes.Keys.Order().Select(i => $"/{i}/enabled"), disabled);
        foreach (var (files, added) in new[] { (PipeAssetGuard.PipeBlocktypes, PipeRules.AddedPipeMaterials), (PipeAssetGuard.ValveBlocktypes, PipeRules.Bronzes) })
        {
            foreach (var file in files)
            {
                var ops = patch.Where(op => (string?)op!["file"] == "ppex:" + file).ToList();
                Assert.Equal(added, ops.Where(op => (string?)op!["path"] == $"/variantgroups/{PipeAssetGuard.MaterialGroupIndex}/states/-")
                    .Select(op => (string?)op!["value"]));
                foreach (var metal in added)
                    Assert.Equal($"game:block/metal/sheet-plain/{metal}4",
                        (string?)ops.Single(op => (string?)op!["path"] == $"/texturesByType/*-{metal}")!["value"]![PipeAssetGuard.TextureKey]!["base"]);
            }
        }
    }

    [Fact]
    public void Shipped_valve_recipes_take_a_pipe_of_any_metal_and_a_bronze()
    {
        var recipes = Shipped("unifiedpipes-grid.json");
        string[] Pipes(JsonNode r) => r["ingredients"]!.AsObject().Where(i => (string?)i.Value!["code"] == "ppex:pipe-straight-ns-*")
            .Select(i => i.Key).ToArray();
        foreach (var valve in new[] { "valve", "pressurevalve" })
        {
            var r = recipes.Single(r => (string?)r!["output"]!["code"] == $"ppex:pipe-{valve}-sn-{{metal}}")!;
            Assert.Null(Assert.Single(Pipes(r)) is { } p ? r["ingredients"]![p]!["name"] : null); // any metal
            Assert.Equal(PipeRules.Bronzes, r["ingredients"]!["B"]!["allowedVariants"]!.AsArray().Select(v => (string)v!));
        }
    }
}
