using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using SeraphHorizons.Mod.Trading.Core;
using SeraphHorizons.Mod.Trading.Schematics.Core;

namespace SeraphHorizons.Mod.Tests.Trading.Schematics;

/// <summary>
/// Schematics (#468, #469): the shipped table (config/schematic-gates.json) is consistent, every
/// machine has its item variant, name and handbook text, the trade lists sell every schematic from
/// the table's sellers at its tier, and the pure parts (code patterns, slot placement, the recipe
/// rules, the standing gate) behave. Whether the game's recipes really take the schematic is checked
/// in the pack (tests/PackTests/TradingSchematicsScenarios.cs).
/// </summary>
public class SchematicTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static string Shipped(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, name));

    private static readonly Lazy<SchematicTable> Table = new(() => SchematicTable.Parse(Shipped("schematic-gates.json")));

    private static TradeListDef List(string type) =>
        JsonSerializer.Deserialize<TradeListDef>(Shipped(Path.Combine("tradelists", $"trader-{type}.json")), Options)!;

    [Fact]
    public void TheShippedTableHasNoProblems()
    {
        var table = Table.Value;
        Assert.Empty(table.Problems(TraderTypes.All, TradeListResolver.MaxStandingTier));
        Assert.Equal(15, table.Gates.Count);
        Assert.Equal("game:paper-parchment", table.Replacement);
        Assert.All(table.Gates, g => Assert.True(table.IsSold(g.Schematic), g.Schematic));
    }

    [Fact]
    public void EveryGateHasItsItemVariantNameAndHandbookText()
    {
        // The item type is the game's lenient JSON (unquoted keys): read the machine states by text.
        string itemtype = Shipped("schematic-itemtype.json");
        var states = Regex.Match(itemtype, @"code: ""machine"", states: \[(.*?)\]", RegexOptions.Singleline).Groups[1].Value;
        var machines = Regex.Matches(states, @"""([a-z]+)""").Select(m => m.Groups[1].Value).ToList();
        var lang = JsonNode.Parse(Shipped("lang-en.json"))!.AsObject();
        Assert.Equal(Table.Value.Gates.Select(g => g.Machine).OrderBy(m => m), machines.OrderBy(m => m));
        foreach (var gate in Table.Value.Gates)
        {
            Assert.True(lang.ContainsKey($"item-schematic-{gate.Machine}"), gate.Machine);
            var sale = Table.Value.Sales.Single(s => s.Code == gate.Schematic);
            string text = (string)lang[$"itemdesc-schematic-{gate.Machine}"]!;
            // The handbook says who sells it and from which tier, as the table does.
            Assert.Contains($"standing tier {sale.Tier} ", text);
            Assert.Contains(sale.Sellers[0] == "generalstore" ? "general store" : sale.Sellers[0], text);
        }
    }

    [Fact]
    public void EverySchematicIsInItsSellersCoreAtItsTier()
    {
        foreach (var sale in Table.Value.Sales)
            foreach (string seller in sale.Sellers)
            {
                var entry = List(seller).Selling.Core.SingleOrDefault(e => CodePattern.Normalise(e.Code) == sale.Code);
                Assert.True(entry != null, $"{seller} does not sell {sale.Code}");
                Assert.Equal(sale.Tier, entry!.StandingTier);
                Assert.True(entry.Price!.Avg >= 10, $"{sale.Code} at {entry.Price.Avg} gears");
            }
        // And nothing is sold as a schematic that the table doesn't know.
        foreach (string type in TraderTypes.All)
            foreach (var e in List(type).Selling.Core.Where(e => Table.Value.IsSold(e.Code)))
                Assert.Contains(Table.Value.Sales, s => s.Code == CodePattern.Normalise(e.Code) && s.Sellers.Contains(type));
    }

    [Fact]
    public void AtTheTopTierEveryListStillHasRotatingSlots()
    {
        foreach (string type in TraderTypes.All)
            foreach (var region in Region.All)
            {
                var side = TradeListResolver.Resolve(List(type).Selling, region, TradeListResolver.MaxStandingTier);
                Assert.True(side.Core.Count <= 14, $"{type} in {region}: {side.Core.Count} core entries");
                Assert.True(side.MaxRotating >= 2, $"{type} in {region}: {side.MaxRotating} rotating slots");
            }
    }

    [Theory]
    [InlineData("game:windmillrotor-*", "game:windmillrotor-wood-north", true)]
    [InlineData("windmillrotor-*", "game:windmillrotor-wood-north", true)]
    [InlineData("game:windmillrotor-*", "millwright:windmillrotor-single-north", false)]
    [InlineData("mpegearbox:gearbox*", "mpegearbox:gearbox12brass-north", true)]
    [InlineData("game:largegear3", "game:largegear3", true)]
    [InlineData("game:largegear3", "game:largegearsection-wood", false)]
    [InlineData("game:a*b*c", "game:axxbyyc", true)]
    [InlineData("game:a*b*c", "game:axxbyy", false)]
    public void CodePatternsMatchAsTheGameWritesThem(string pattern, string code, bool matches) =>
        Assert.Equal(matches, CodePattern.Matches(pattern, code));

    [Fact]
    public void GatesFindTheirOutputsOnlyWithTheirModLoaded()
    {
        var table = Table.Value;
        Assert.Equal("windmill", table.GateFor("millwright:windmillrotor-six-north", _ => true)?.Machine);
        Assert.Null(table.GateFor("millwright:windmillrotor-six-north", mod => mod != "millwright"));
        Assert.Equal("transmission", table.GateFor("game:clutch-north", _ => true)?.Machine);
        Assert.Equal("biplane", table.GateFor("flyingmachine:trestles", _ => true)?.Machine);
        Assert.Null(table.GateFor("flyingmachine:wing", _ => true));
        Assert.Null(table.GateFor("madmechanics:clutch-north-down", _ => true));
        Assert.True(table.IsSold("betterruins:br-schematic-door"));
        Assert.True(table.IsSold("schematic-glider"));
        Assert.False(table.IsSold("scrolled:br-rolled-schematic-door"));
        Assert.True(table.IsMachineSchematic("seraphhorizons:schematic-biplane"));
        Assert.False(table.IsMachineSchematic("betterruins:br-schematic-door"));
    }

    private static GridPlacement? Place(string pattern, int w, int h, Func<char, bool>? canDouble = null) =>
        GridGate.Place(pattern, w, h, pattern.Where(c => !GridGate.IsEmpty(c) && c != ','), canDouble ?? (_ => true));

    [Fact]
    public void TheSchematicTakesTheFirstEmptySlot()
    {
        // The game's wooden windmill rotor.
        var p = Place("_H_,_C_,RSF", 3, 3)!;
        Assert.Equal("ZH__C_RSF", p.Pattern);
        Assert.Equal((3, 3, 'Z', (char?)null), (p.Width, p.Height, p.SchematicKey, p.DoubledFrom));
    }

    [Fact]
    public void ANarrowRecipeGetsAColumnAndAShortOneARow()
    {
        var narrow = Place("H_,C_,SF".Replace("_", "X"), 2, 3)!;
        Assert.Equal("HXZCX_SF_", narrow.Pattern);
        Assert.Equal((3, 3), (narrow.Width, narrow.Height));
        // The water wheel, 3 × 2 and full.
        var wheel = Place("BHB,BAB", 3, 2)!;
        Assert.Equal("BHBBABZ__", wheel.Pattern);
        Assert.Equal((3, 3), (wheel.Width, wheel.Height));
    }

    [Fact]
    public void AFullGridDoublesTheIngredientFillingTheMostSlots()
    {
        // The game's large gear: four S, four P (S comes first), the axle in the middle.
        var p = Place("SPS,PAP,SPS", 3, 3)!;
        Assert.Equal('S', p.DoubledFrom);
        Assert.Equal("SPSPAPYPZ", p.Pattern);
        Assert.Equal('Y', p.DoubledKey);
        Assert.Equal(2, p.Pattern.Count(c => c == 'S'));
        // Keys the recipe already uses are not reused.
        var q = GridGate.Place("ZZZ,YYY,XXA", 3, 3, "ZYXA", _ => true)!;
        Assert.DoesNotContain(q.SchematicKey, "ZYXA");
        Assert.DoesNotContain(q.DoubledKey!.Value, "ZYXA");
    }

    [Fact]
    public void ToolsAndStacksThatWouldNotFitAreNotDoubled()
    {
        // A sawmill frame with the hammer only once and nothing else allowed: no room.
        Assert.Null(Place("HMP,BBB,NMP", 3, 3, _ => false));
        var p = Place("HMP,BBB,NMP", 3, 3, c => c != 'B')!;
        Assert.Equal('M', p.DoubledFrom);
        Assert.Null(Place("ABC,DEF,GHI", 3, 3));
        Assert.Null(Place("AB", 3, 3));
    }

    [Fact]
    public void ConversionsAreLeftAloneAndEverythingElseKeepsItsSchematic()
    {
        Assert.True(SchematicRecipeRules.IsConversion(1, true, 1));
        Assert.False(SchematicRecipeRules.IsConversion(2, true, 1));
        Assert.False(SchematicRecipeRules.IsConversion(1, false, 1));
        Assert.False(SchematicRecipeRules.IsConversion(1, true, 2));
        // The game's glider copy (parchment + schematic): removed. Scrolled's unrolling: kept.
        Assert.True(SchematicRecipeRules.RemoveRecipe(outputIsSold: true, isConversion: false));
        Assert.False(SchematicRecipeRules.RemoveRecipe(outputIsSold: true, isConversion: true));
        Assert.False(SchematicRecipeRules.RemoveRecipe(outputIsSold: false, isConversion: false));
        // Scrolled's rolling consumes it; a door recipe keeps it.
        Assert.False(SchematicRecipeRules.KeepIngredient(ingredientIsSold: true, isConversion: true));
        Assert.True(SchematicRecipeRules.KeepIngredient(ingredientIsSold: true, isConversion: false));
    }

    [Fact]
    public void StandingTierIsParsedAndGatesTheCore()
    {
        var def = JsonSerializer.Deserialize<TradeListDef>("""
            { "type": "mechanic", "wallet": [{"avg": 100, "var": 0}],
              "selling": { "core": [
                {"code": "rope", "price": {"avg": 2, "var": 0}},
                {"code": "seraphhorizons:schematic-biplane", "price": {"avg": 250, "var": 0}, "standingTier": 4},
                {"code": "seraphhorizons:schematic-windmill", "price": {"avg": 30, "var": 0}, "standingTier": 1}
              ], "rotating": { "maxItems": 2, "list": [ {"code": "stick", "price": {"avg": 1, "var": 0}, "standingTier": 2} ] } } }
            """, Options)!;
        Assert.Equal([0, 4, 1], def.Selling.Core.Select(e => e.StandingTier));
        var region = Region.All.First();
        Assert.Equal(["item:game:rope"], TradeListResolver.Resolve(def.Selling, region, 0).Core.Select(e => e.Key));
        Assert.Empty(TradeListResolver.Resolve(def.Selling, region, 0).Rotating);
        // Lowest tier first after the ungated core.
        Assert.Equal(["item:game:rope", "item:seraphhorizons:schematic-windmill", "item:seraphhorizons:schematic-biplane"],
            TradeListResolver.Resolve(def.Selling, region, 4).Core.Select(e => e.Key));
        Assert.Single(TradeListResolver.Resolve(def.Selling, region, 2).Rotating);
        Assert.Empty(TradeListResolver.Problems(def));
        def.Selling.Core[1].StandingTier = 9;
        Assert.Contains(TradeListResolver.Problems(def), p => p.Contains("standing tier 9"));
    }
}
