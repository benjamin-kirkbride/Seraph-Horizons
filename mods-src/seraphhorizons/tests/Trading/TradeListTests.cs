using SeraphHorizons.Mod.Trading.Core;

namespace SeraphHorizons.Mod.Tests.Trading;

public class TradeListTests
{
    internal static TradeEntry E(string code, bool ps = false, string attrs = "") =>
        new() { Code = code, Stock = new NatSpec(4, 0), PlayerSupplied = ps, AttributesKey = attrs };

    private static TradeListDef Def() => new()
    {
        Type = "smith",
        Wallet = [new NatSpec(100, 10), new NatSpec(130, 10)],
        Selling = new TradeSide
        {
            Core = [E("coke"), E("ingot-iron", ps: true)],
            Rotating = new RotatingList { MaxItems = 3, List = [E("chisel-iron", ps: true), E("charcoal"), E("ore-borax"), E("anvil-iron", ps: true)] },
            Regional = new()
            {
                [Region.Cold] = new RegionalList { Core = [E("ore-anthracite")], Rotating = [E("furboots")] },
                [Region.Igneous] = new RegionalList { Core = [E("charcoal")], Rotating = [E("ingot-nickel", ps: true)] },
            },
        },
        Buying = new TradeSide { Core = [E("ingot-copper")], Rotating = new RotatingList { MaxItems = 2, List = [E("ingot-tin"), E("ingot-zinc")] } },
    };

    [Fact]
    public void ARegionAddsItsClimateAndRockTablesAndCoreWinsOverRotating()
    {
        var side = TradeListResolver.Resolve(Def().Selling, new Region(Region.Cold, Region.Igneous));
        Assert.Equal(["item:game:coke", "item:game:ingot-iron", "item:game:ore-anthracite", "item:game:charcoal"], side.Core.Select(e => e.Key));
        // charcoal is core here, so not drawn again; the regional rotating ones join the pool.
        Assert.Equal(["item:game:chisel-iron", "item:game:ore-borax", "item:game:anvil-iron", "item:game:furboots", "item:game:ingot-nickel"],
            side.Rotating.Select(e => e.Key));
        Assert.Equal(3, side.MaxRotating);

        var plain = TradeListResolver.Resolve(Def().Selling, new Region(Region.Temperate, Region.Sedimentary));
        Assert.Equal(["item:game:coke", "item:game:ingot-iron"], plain.Core.Select(e => e.Key));
        Assert.Equal(4, plain.Rotating.Count);
    }

    [Fact]
    public void TheWalletIsByStandingTierAndTheLastTierHolds()
    {
        var list = TradeListResolver.Resolve(Def(), new Region(Region.Hot, Region.Metamorphic));
        Assert.Equal(100, list.Wallet.Avg);
        Assert.Equal(130, Def().WalletFor(1).Avg);
        Assert.Equal(130, Def().WalletFor(9).Avg);
        Assert.Equal(60, new TradeListDef().WalletFor(0).Avg);
    }

    [Fact]
    public void KeysTellDomainsAndAttributesApart()
    {
        Assert.Equal("item:game:charcoal", E("charcoal").Key);
        Assert.Equal(E("game:charcoal").Key, E("charcoal").Key);
        Assert.Equal("item:wool:twine", E("wool:twine").Key);
        Assert.NotEqual(E("gem", attrs: "{\"potential\":\"high\"}").Key, E("gem", attrs: "{\"potential\":\"low\"}").Key);
        Assert.Equal("block:game:crate", new TradeEntry { Type = "block", Code = "crate" }.Key);
    }

    [Fact]
    public void RotatingSlotsAreCutToWhatTheCoreLeaves()
    {
        var side = new TradeSide
        {
            Core = Enumerable.Range(0, 14).Select(i => E($"core{i}")).ToList(),
            Rotating = new RotatingList { MaxItems = 8, List = Enumerable.Range(0, 10).Select(i => E($"rot{i}")).ToList() },
        };
        Assert.Equal(2, TradeListResolver.Resolve(side, new Region(Region.Cold, Region.Igneous)).MaxRotating);
        side.Core.AddRange(Enumerable.Range(14, 6).Select(i => E($"core{i}")));
        var full = TradeListResolver.Resolve(side, new Region(Region.Cold, Region.Igneous));
        Assert.Equal(TradeListResolver.Slots, full.Core.Count);
        Assert.Equal(0, full.MaxRotating);
        // ...and to the pool.
        var few = new TradeSide { Rotating = new RotatingList { MaxItems = 8, List = [E("a"), E("b")] } };
        Assert.Equal(2, TradeListResolver.Resolve(few, new Region(Region.Cold, Region.Igneous)).MaxRotating);
    }

    [Fact]
    public void ProblemsNameWhatIsWrong()
    {
        Assert.Empty(TradeListResolver.Problems(Def()));
        var bad = Def();
        bad.Type = "banker";
        bad.Wallet.Clear();
        bad.Selling.Regional["arctic"] = new RegionalList();
        bad.Selling.Rotating.List.Add(E("charcoal"));
        bad.Buying.Core.Add(E(""));
        var gold = E("ingot-gold");
        gold.Price = 12;
        bad.Buying.Core.Add(gold);
        var silver = E("ingot-silver");
        silver.Price = 0;
        silver.PriceReason = "free";
        bad.Buying.Core.Add(silver);
        bad.Buying.Core.AddRange(Enumerable.Range(0, 16).Select(i => E($"x{i}")));
        var problems = TradeListResolver.Problems(bad);
        Assert.Contains(problems, p => p.Contains("'banker'"));
        Assert.Contains("no wallet", problems);
        Assert.Contains(problems, p => p.Contains("unknown region key 'arctic'"));
        Assert.Contains(problems, p => p.Contains("selling.rotating: item:game:charcoal is listed 2 times"));
        Assert.Contains(problems, p => p.Contains("buying.core: an entry has no code"));
        // A price override needs its reason, and a positive price.
        Assert.Contains(problems, p => p.Contains("item:game:ingot-gold overrides its price without a priceReason"));
        Assert.Contains(problems, p => p.Contains("item:game:ingot-silver has a price override of 0"));
        Assert.Contains(problems, p => p.Contains("more than the 16 slots"));
    }

    [Fact]
    public void RegionsParseAndPrint()
    {
        Assert.Equal(9, Region.All.Count());
        foreach (var r in Region.All)
        {
            Assert.True(Region.TryParse(r.ToString(), out var back));
            Assert.Equal(r, back);
        }
        Assert.False(Region.TryParse("arctic/igneous", out _));
        Assert.False(Region.TryParse("cold", out _));
    }

    [Fact]
    public void ClimateBandsFollowTheYearlyMeanTemperature()
    {
        Assert.Equal(Region.Cold, RegionClassifier.ClimateOf(-12));
        Assert.Equal(Region.Cold, RegionClassifier.ClimateOf(-0.1f));
        Assert.Equal(Region.Temperate, RegionClassifier.ClimateOf(0));
        Assert.Equal(Region.Temperate, RegionClassifier.ClimateOf(15.9f));
        Assert.Equal(Region.Hot, RegionClassifier.ClimateOf(16));
        Assert.Equal(Region.Hot, RegionClassifier.ClimateOf(31));
    }

    [Fact]
    public void RockGroupsComeFromTheGamesRockGroupThenTheFallback()
    {
        var game = new Dictionary<string, string>
        {
            ["andesite"] = "igneous_extrusive", ["granite"] = "igneous_intrusive", ["chalk"] = "sedimentary",
            ["slate"] = "metamorphic", ["komatiite"] = "igneous_extrusive", ["arkose"] = "Sedimentary", ["odd"] = "volcanic?",
        };
        var c = new RegionClassifier(game);
        Assert.Equal(Region.Igneous, c.RockGroupOf("andesite"));
        Assert.Equal(Region.Igneous, c.RockGroupOf("granite"));
        Assert.Equal(Region.Sedimentary, c.RockGroupOf("chalk"));
        Assert.Equal(Region.Metamorphic, c.RockGroupOf("slate"));
        Assert.Equal(Region.Sedimentary, c.RockGroupOf("arkose"));
        // Not in the game's table: the fallback, then sedimentary.
        Assert.Equal(Region.Metamorphic, c.RockGroupOf("whitemarble"));
        Assert.Equal(Region.Sedimentary, c.RockGroupOf("odd"));
        Assert.Equal(Region.Sedimentary, c.RockGroupOf(null));
        Assert.Equal(new Region(Region.Hot, Region.Igneous), c.Classify(22, "basalt"));
        // The game's table wins over the fallback.
        Assert.Equal(Region.Sedimentary, new RegionClassifier(new Dictionary<string, string> { ["bauxite"] = "sedimentary" }).RockGroupOf("bauxite"));
    }
}
