using SeraphHorizons.Tests.Trading.Standing;
using SeraphHorizons.Mod.Trading.Core;
using SeraphHorizons.Mod.Trading.Standing.Core;
using static SeraphHorizons.Mod.Tests.Trading.TradeListTests;

namespace SeraphHorizons.Mod.Tests.Trading;

/// <summary>The glue between the wave-2 trading features: rare stock by the shelf's tier, special
/// entries expanded into offers, and standing's recent-trade clock aged by simulated days.</summary>
public class GlueTests
{
    private static readonly Region Plain = new(Region.Temperate, Region.Sedimentary);

    private static TradeEntry Rare(string code) { var e = E(code); e.Rare = true; return e; }

    private static TradeEntry Special(string kind) => new() { Code = "seraphhorizons:" + kind, Kind = kind, Price = new NatSpec(1, 0) };

    [Fact]
    public void RareEntriesAreShelvedOnlyWithRareStock()
    {
        var side = new TradeSide
        {
            Core = [E("coke"), Rare("ingot-gold")],
            Rotating = new RotatingList { MaxItems = 4, List = [E("charcoal"), Rare("gem-emerald")] },
        };
        var plain = TradeListResolver.Resolve(side, Plain, 4);
        Assert.Equal(["item:game:coke"], plain.Core.Select(e => e.Key));
        Assert.Equal(["item:game:charcoal"], plain.Rotating.Select(e => e.Key));
        var rare = TradeListResolver.Resolve(side, Plain, 4, rareStock: true);
        Assert.Equal(["item:game:coke", "item:game:ingot-gold"], rare.Core.Select(e => e.Key));
        Assert.Equal(2, rare.Rotating.Count);
    }

    [Fact]
    public void SpecialEntriesExpandInPlaceAndAreLeftOutWithoutAnExpander()
    {
        var side = new ResolvedSide([E("coke"), Special("lead"), E("charcoal")], [E("ore-borax"), Special("oremap")], 4);
        var none = TradeOffers.Expand(side, null);
        Assert.Equal(["item:game:coke", "item:game:charcoal"], none.Core.Select(e => e.Key));
        Assert.Equal(["item:game:ore-borax"], none.Rotating.Select(e => e.Key));

        var expanded = TradeOffers.Expand(side, e => [E("lead-a", attrs: "{a}"), E("lead-b")]);
        Assert.Equal(["item:game:coke", "item:game:lead-a{a}", "item:game:lead-b", "item:game:charcoal"], expanded.Core.Select(e => e.Key));
        Assert.Equal(1, expanded.MaxRotating);
    }

    [Fact]
    public void OverTheSlotsOptionalOffersGiveWayFirstAndTheRotatingSlotsShrink()
    {
        var core = Enumerable.Range(0, 13).Select(i => E("good" + i)).Append(Special("lead")).ToList();
        var side = new ResolvedSide(core, [E("extra")], 3);
        var expanded = TradeOffers.Expand(side, _ =>
        [
            E("basic"),
            new TradeEntry { Code = "far1", Price = new NatSpec(1, 0), Optional = true },
            new TradeEntry { Code = "far2", Price = new NatSpec(1, 0), Optional = true },
            new TradeEntry { Code = "far3", Price = new NatSpec(1, 0), Optional = true },
        ]);
        Assert.Equal(TradeListResolver.Slots, expanded.Core.Count);
        Assert.Contains(expanded.Core, e => e.Code == "basic");
        Assert.Contains(expanded.Core, e => e.Code == "far2");
        Assert.DoesNotContain(expanded.Core, e => e.Code == "far3");
        Assert.Equal(0, expanded.MaxRotating);
    }

    [Fact]
    public void AgeingMovesRecentTradesIntoThePast()
    {
        var ledger = new StandingLedger(StandingTests.Rules());
        ledger.OnDeal("p", null, "camp:1,1", 10, 0, day: 100);
        Assert.Single(ledger.RecentPlayers("camp:1,1", 100 - 14));
        ledger.Age(10);
        Assert.Single(ledger.RecentPlayers("camp:1,1", 100 - 14));
        ledger.Age(5);
        Assert.Empty(ledger.RecentPlayers("camp:1,1", 100 - 14));
        Assert.Equal(10, ledger.Personal("p", "camp:1,1")!.Points);
    }
}
