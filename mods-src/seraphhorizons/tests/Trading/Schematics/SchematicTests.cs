using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using SeraphHorizons.Mod.Trading.Core;
using SeraphHorizons.Mod.Trading.Schematics.Core;

namespace SeraphHorizons.Mod.Tests.Trading.Schematics;

/// <summary>
/// Schematics (#468, #469): the shipped table (config/schematic-gates.json) is consistent, every
/// machine has its item variant, name and handbook text, no trade list sells or buys a schematic
/// (the catalogs sell them, <see cref="SchematicCatalog"/>), the catalogs follow the switches and the
/// standing tiers, and the pure parts (code patterns, slot placement, the recipe rules, the standing
/// gate) behave. Whether the game's recipes really take the schematic, and a purchase from a
/// catalog, are checked in the pack (tests/PackTests/TradingSchematicsScenarios.cs).
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

    /// <summary>The config class's bool switches, read from its source.</summary>
    private static readonly Lazy<List<string>> Switches = new(() =>
        Regex.Matches(Shipped(Path.Combine("schematic-switches", "SeraphHorizons.cs.txt")), @"public bool (\w+) \{ get; set; \}")
            .Select(m => m.Groups[1].Value).ToList());

    private static readonly Func<string, bool> AllOn = _ => false;

    [Fact]
    public void TheShippedTableHasNoProblems()
    {
        var table = Table.Value;
        Assert.Contains("MachineSchematics", Switches.Value);
        Assert.Empty(table.Problems(TraderTypes.All.Concat(TraderTypes.Story).ToList(), TradeListResolver.MaxStandingTier, Switches.Value));
        Assert.Equal(12, table.Gates.Count);
        Assert.Equal("game:paper-parchment", table.Replacement);
        Assert.All(table.Gates, g => Assert.True(table.IsSold(g.Schematic), g.Schematic));
        // The switches named are the machines' own.
        Assert.Equal(["BuckingSawmill", "Eidolon", "Rosser"], table.Gates.Where(g => g.Switch != null).Select(g => g.Switch!).Order());
    }

    [Fact]
    public void TheCentredSpurGearIsTheTransmissionsAndTheHandCrankIsUngated()
    {
        var table = Table.Value;
        Assert.Equal("transmission", table.GateFor("mpecenteredspurgear:centeredspurgear-ns", _ => true)?.Machine);
        Assert.Null(table.GateFor("handcrank:handcrank-north", _ => true));
        Assert.DoesNotContain(table.Gates, g => g.Machine is "handcrank" or "centeredspurgear");
        Assert.DoesNotContain(table.Sales, s => s.Code.EndsWith("schematic-handcrank") || s.Code.EndsWith("schematic-centeredspurgear"));
        // BetterRuins' mechanical blueprint: the mechanic's, for a trusted customer, at 60.
        var mechanical = Assert.Single(table.Sales, s => s.Code == "betterruins:br-schematic-mechanical");
        Assert.Equal(["mechanic"], mechanical.Sellers);
        Assert.Equal((3, 60), (mechanical.Tier, mechanical.Price));
    }

    [Fact]
    public void ABadSwitchNameIsAProblem()
    {
        var table = SchematicTable.Parse("""
            { "sold": [ { "code": "seraphhorizons:schematic-*" } ],
              "gates": [ { "machine": "mill", "switch": "NoSuchSwitch", "outputs": [ { "code": "game:mill" } ] } ],
              "sales": [ { "code": "seraphhorizons:schematic-mill", "sellers": ["smith"], "tier": 1, "price": 0 } ] }
            """);
        var problems = table.Problems(["smith"], 4, ["Mill"]);
        Assert.Contains(problems, p => p.Contains("no switch 'NoSuchSwitch'"));
        Assert.Contains(problems, p => p.Contains("has no price"));
        Assert.DoesNotContain(table.Problems(["smith"], 4), p => p.Contains("switch"));
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
            Assert.Contains(sale.Sellers[0] switch { "generalstore" => "general store", "curiodealer" => "curio dealer", var other => other }, text);
        }
        // No item, name or text is left of the schematics that went.
        foreach (string gone in new[] { "handcrank", "centeredspurgear" })
        {
            Assert.DoesNotContain(gone, machines);
            Assert.False(lang.ContainsKey($"item-schematic-{gone}"), gone);
            Assert.False(lang.ContainsKey($"itemdesc-schematic-{gone}"), gone);
            Assert.DoesNotContain("*-" + gone, itemtype);
        }
    }

    [Fact]
    public void NoTradeListSellsOrBuysASchematic()
    {
        foreach (string type in TraderTypes.All.Concat(TraderTypes.Story))
        {
            var def = List(type);
            var entries = new[] { def.Selling, def.Buying }.SelectMany(s => s.Core.Concat(s.Rotating.List)
                .Concat(s.Regional.Values.SelectMany(r => r.Core.Concat(r.Rotating))));
            foreach (var e in entries)
                Assert.False(Table.Value.NeverBoughtBack(e.Code), $"{type} lists {e.Code}: schematics are the catalogs'");
        }
    }

    [Fact]
    public void EverySaleHasAFlatPriceAtOrAboveTheFloor()
    {
        foreach (var sale in Table.Value.Sales)
        {
            // Half the shelves' former hand-set gates, rounded up.
            Assert.True(sale.Price >= 5, $"{sale.Code} at {sale.Price} gears");
            Assert.True(sale.Sellers.Count > 0, sale.Code);
        }
        Assert.Equal(75, Table.Value.Sales.Single(s => s.Code == "game:schematic-glider").Price);
        Assert.Equal(125, Table.Value.Sales.Single(s => s.Code == "seraphhorizons:schematic-biplane").Price);
        Assert.Equal(18, Table.Value.Sales.Single(s => s.Code == "seraphhorizons:schematic-waterwheel").Price);
    }

    [Fact]
    public void ACatalogIsItsTypesSalesLowestTierFirstEachUnlockedByTheTier()
    {
        var table = Table.Value;
        var all = SchematicCatalog.For(table, "mechanic", AllOn, null);
        Assert.Equal(table.Sales.Count(s => s.Sellers.Contains("mechanic")), all.Count);
        Assert.All(all, r => Assert.True(r.Unlocked));
        Assert.Equal(all.OrderBy(r => r.Tier).ThenBy(r => r.Price).Select(r => r.Code), all.Select(r => r.Code));
        var known = SchematicCatalog.For(table, "mechanic", AllOn, 2);
        Assert.All(known, r => Assert.Equal(r.Tier <= 2, r.Unlocked));
        Assert.Contains(known, r => r.Code == "seraphhorizons:schematic-biplane" && !r.Unlocked && r.Tier == 4 && r.Price == 125);
        Assert.Contains(known, r => r.Code == "seraphhorizons:schematic-transmission" && r.Unlocked);
        // Both sellers of the diving gear sell it at one price.
        var curio = SchematicCatalog.For(table, "curiodealer", AllOn, 0).Single(r => r.Code == "abyssaldepths:ad-schematic-divinggear");
        var hunter = SchematicCatalog.For(table, "treasurehunter", AllOn, 0).Single(r => r.Code == "abyssaldepths:ad-schematic-divinggear");
        Assert.Equal((curio.Price, curio.Tier, false), (hunter.Price, hunter.Tier, hunter.Unlocked));
        Assert.Empty(SchematicCatalog.For(table, "animaldealer", AllOn, 4));
    }

    [Fact]
    public void SwitchesTakeSchematicsOutOfEveryCatalog()
    {
        var table = Table.Value;
        Func<string, bool> noMachines = s => s == SchematicCatalog.MachineSwitch;
        Assert.DoesNotContain(SchematicCatalog.For(table, "mechanic", noMachines, null), r => table.IsMachineSchematic(r.Code));
        Assert.Contains(SchematicCatalog.For(table, "mechanic", noMachines, null), r => r.Code == "betterruins:br-schematic-mechanical");
        Func<string, bool> noRosser = s => s == "Rosser";
        var mechanic = SchematicCatalog.For(table, "mechanic", noRosser, null);
        Assert.DoesNotContain(mechanic, r => r.Code == "seraphhorizons:schematic-rosser");
        Assert.Contains(mechanic, r => r.Code == "seraphhorizons:schematic-buckingmill");
        Assert.DoesNotContain(SchematicCatalog.For(table, "curiodealer", s => s == "Eidolon", null), r => r.Code == "seraphhorizons:schematic-eidolon");
        Assert.Null(SchematicCatalog.Sellers(table, "seraphhorizons:schematic-eidolon", s => s == "Eidolon"));
        var (sellers, tier, price) = SchematicCatalog.Sellers(table, "seraphhorizons:schematic-eidolon", AllOn)!.Value;
        Assert.Equal(["curiodealer"], sellers);
        Assert.Equal((3, 90), (tier, price));
        Assert.Null(SchematicCatalog.Sellers(table, "game:stick", AllOn));
    }

    [Fact]
    public void TheBestTierInTheGroupUnlocks()
    {
        Assert.Equal(1, SchematicCatalog.GroupTier(1, []));
        Assert.Equal(3, SchematicCatalog.GroupTier(1, [0, 3, 2]));
        Assert.Equal(2, SchematicCatalog.GroupTier(2, [0, 1]));
    }

    [Theory]
    [InlineData("betterruins:br-schematic-door", true)]
    [InlineData("game:schematic-glider", true)]
    [InlineData("seraphhorizons:schematic-windmill", true)]
    [InlineData("scrolled:br-rolled-schematic-door", true)]
    [InlineData("scrolled:rolled-schematic-glider", true)]
    [InlineData("othermod:ad-rolled-schematic-divinggear", true)]
    [InlineData("scrolled:br-rolled-locatormap-mine1", false)]
    [InlineData("game:paper-parchment", false)]
    public void SchematicsAndTheirRolledCopiesAreNeverBoughtBack(string code, bool never) =>
        Assert.Equal(never, Table.Value.NeverBoughtBack(code));

    [Fact]
    public void AtTheTopTierEveryListStillHasRotatingSlots()
    {
        foreach (string type in TraderTypes.All.Concat(TraderTypes.Story))
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
    [InlineData("game:windmillrotor-*", "othermod:windmillrotor-single-north", false)]
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
        Assert.Equal("gearbox", table.GateFor("mpegearbox:gearbox12brass-north", _ => true)?.Machine);
        Assert.Null(table.GateFor("mpegearbox:gearbox12brass-north", mod => mod != "mpegearbox"));
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
            { "type": "mechanic", "wallet": {"avg": 100, "var": 0},
              "selling": { "core": [
                {"code": "rope"},
                {"code": "seraphhorizons:schematic-biplane", "price": 250, "priceReason": "a gate", "standingTier": 4},
                {"code": "seraphhorizons:schematic-windmill", "price": 30, "priceReason": "a gate", "standingTier": 1}
              ], "rotating": { "maxItems": 2, "list": [ {"code": "stick", "standingTier": 2} ] } } }
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
