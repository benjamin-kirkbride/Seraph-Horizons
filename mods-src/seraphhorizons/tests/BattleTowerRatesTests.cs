using SeraphHorizons.Mod.Core;

namespace SeraphHorizons.Tests;

public class BattleTowerRatesTests
{
    // The shape of Battle Towers 1.1.0's patches/survival-worldgen-structures.json, trimmed.
    private const string Patch = """
        [
          { "op": "add", "path": "/structures/-", "file": "game:worldgen/structures.json", "side": "Server",
            "value": { "code": "surfacetowers", "chance": 0.03, "group": "surfacetowers", "minGroupDistance": 200, "schematics": ["surfacetowers/*"] } },
          { "op": "add", "path": "/structures/-", "file": "game:worldgen/structures.json", "side": "Server",
            "value": { "code": "surfacehardtowers", "chance": 0.005, "minGroupDistance": 1000 } },
          { "op": "add", "path": "/structures/-", "file": "game:worldgen/structures.json", "side": "Server",
            "value": { "code": "undergroundtowers", "chance": 200.0, "minGroupDistance": 50, "placement": "underground" } }
        ]
        """;

    private static string Shipped => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "battletowers-rates.json"));

    [Fact]
    public void The_shipped_rates_parse_and_cover_the_three_towers()
    {
        var rates = BattleTowerRates.ParseRates(Shipped);
        Assert.Equal(["surfacehardtowers", "surfacetowers", "undergroundtowers"], rates.Keys.Order());
        // Rarer than Battle Towers ships every one of them.
        Assert.True(rates["surfacetowers"].Chance < 0.03);
        Assert.True(rates["surfacehardtowers"].Chance < 0.005);
        Assert.True(rates["undergroundtowers"].Chance < 200);
        Assert.All(rates.Values, rate => Assert.True(rate.Chance > 0));
    }

    [Fact]
    public void Each_tower_found_by_code_gets_its_rate_and_the_rest_is_kept()
    {
        var rates = new Dictionary<string, TowerRate>
        {
            ["surfacetowers"] = new(0.002, 1500),
            ["undergroundtowers"] = new(0.5, null),
            ["notatower"] = new(1, 1),
        };
        var (text, applied) = BattleTowerRates.Apply(Patch, rates);
        Assert.Equal(["surfacetowers", "undergroundtowers"], applied);

        var ops = System.Text.Json.Nodes.JsonNode.Parse(text)!.AsArray();
        Assert.Equal(3, ops.Count);
        var surface = ops[0]!["value"]!;
        Assert.Equal(0.002, surface["chance"]!.GetValue<double>());
        Assert.Equal(1500, surface["minGroupDistance"]!.GetValue<int>());
        Assert.Equal("surfacetowers/*", surface["schematics"]![0]!.GetValue<string>());
        Assert.Equal("/structures/-", ops[0]!["path"]!.GetValue<string>());
        // Not in the rates: as shipped.
        Assert.Equal(0.005, ops[1]!["value"]!["chance"]!.GetValue<double>());
        Assert.Equal(1000, ops[1]!["value"]!["minGroupDistance"]!.GetValue<int>());
        // A rate without a distance keeps the shipped one.
        Assert.Equal(0.5, ops[2]!["value"]!["chance"]!.GetValue<double>());
        Assert.Equal(50, ops[2]!["value"]!["minGroupDistance"]!.GetValue<int>());
    }

    [Fact]
    public void Bad_input_is_a_format_error()
    {
        Assert.Throws<FormatException>(() => BattleTowerRates.ParseRates("[]"));
        Assert.Throws<FormatException>(() => BattleTowerRates.ParseRates("""{ "surfacetowers": { "minGroupDistance": 5 } }"""));
        Assert.Throws<FormatException>(() => BattleTowerRates.ParseRates("""{ "surfacetowers": { "chance": -1 } }"""));
        Assert.Throws<FormatException>(() => BattleTowerRates.Apply("{}", new Dictionary<string, TowerRate>()));
    }
}
