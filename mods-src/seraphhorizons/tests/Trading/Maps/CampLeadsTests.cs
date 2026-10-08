using System.Text.Json;
using SeraphHorizons.Mod.Trading.Core;
using SeraphHorizons.Mod.Trading.Maps.Core;

namespace SeraphHorizons.Mod.Tests.Trading.Maps;

/// <summary>Leads to other trader camps: which a buyer is offered by standing (count, radius,
/// prospector first, the stranger's one map chained from camp to camp) and what they cost.</summary>
public class CampLeadsTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static CampLeadRules Shipped() =>
        JsonSerializer.Deserialize<MapPriceTable>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "map-prices.json")), Options)!.CampLeads;

    private static readonly CellKey Own = new(0, 0);

    /// <summary>Camps east of the trader on a line, one per cell, 2 km apart (2, 4, 6, ... km), all
    /// smiths but where <paramref name="prospectors"/> says; and one west at 2.5 km.</summary>
    private static List<CampOption> Line(params int[] prospectors)
    {
        var camps = new List<CampOption>();
        for (int i = 1; i <= 7; i++)
            camps.Add(new CampOption(new CellKey(i, 0), prospectors.Contains(i) ? TraderTypes.Prospector : TraderTypes.Smith, i * 2000, 0, i * 2000));
        camps.Add(new CampOption(new CellKey(-1, 0), TraderTypes.Cook, -2500, 0, 2500));
        return camps;
    }

    private static List<CellKey> Cells(List<CampLeadOffer> offers) => offers.Select(o => o.Cell).ToList();

    [Fact]
    public void TheShippedTableIsTheModerateOne()
    {
        var r = Shipped();
        Assert.Empty(r.Problems());
        Assert.Null(r.TierFor("stranger"));
        Assert.Equal([(2, 3000, 0.85), (3, 5000, 0.7), (5, 8000, 0.55), (8, 12000, 0.4)],
            new[] { "known", "regular", "trusted", "partner" }.Select(c => r.TierFor(c)!).Select(t => (t.Maps, t.Radius, t.Discount)));
        Assert.Equal(2, r.PerBought);
    }

    [Fact]
    public void AStrangersFirstNearbyMapIsCheapAndAPartnersEighthFarOneVeryDear()
    {
        var r = Shipped();
        Assert.InRange(r.Price(2000, 0, "stranger"), 2, 6);
        Assert.InRange(r.Price(12000, 7, "partner"), 1000, int.MaxValue);
    }

    [Fact]
    public void ThePriceDoublesPerMapBoughtHereAndEveryDoublingDistance()
    {
        var r = new CampLeadRules { Base = 1000, DoublingDistance = 2500, PerBought = 2, Tiers = { ["known"] = new() { Maps = 2, Radius = 3000, Discount = 0.5 } } };
        Assert.Equal(1000, r.Price(0, 0, "stranger"));
        Assert.Equal(2000, r.Price(0, 1, "stranger"));
        Assert.Equal(8000, r.Price(0, 3, "stranger"));
        Assert.Equal(2000, r.Price(2500, 0, "stranger"));
        Assert.Equal(4000, r.Price(5000, 0, "stranger"));
        Assert.Equal(1000, r.Price(2500, 0, "known"));
        Assert.Equal(1000, r.Price(-50, 0, "unknown-tier"));
        Assert.Equal(1, new CampLeadRules { Base = 0.01 }.Price(0, 0, null));
    }

    [Fact]
    public void BetterStandingPaysLessForTheSameMap()
    {
        var r = Shipped();
        var prices = new[] { "stranger", "known", "regular", "trusted", "partner" }.Select(c => r.Price(5000, 2, c)).ToList();
        for (int i = 1; i < prices.Count; i++) Assert.True(prices[i] < prices[i - 1], string.Join(", ", prices));
    }

    [Fact]
    public void KnownGetsTheTwoNearestItLacksWithinThreeKm()
    {
        var (offers, why) = CampLeads.Pick(Shipped(), Own, Line(), new LeadBuyer { Tier = "known", Have = new HashSet<CellKey> { new(-5, -5) } });
        Assert.Equal(CampLeadsWhy.None, why);
        // A prospector beyond 3 km is not pulled in; 2 km east and 2.5 km west are the two.
        Assert.Equal([new CellKey(1, 0), new CellKey(-1, 0)], Cells(offers));
        Assert.All(offers, o => Assert.False(o.Prospector));
    }

    [Fact]
    public void BuyingOneBringsTheNextNearestInItsPlace()
    {
        var rules = Shipped();
        var have = new HashSet<CellKey>();
        var (first, _) = CampLeads.Pick(rules, Own, Line(), new LeadBuyer { Tier = "regular", Have = have });
        Assert.Equal([new CellKey(1, 0), new CellKey(-1, 0), new CellKey(2, 0)], Cells(first));
        have.Add(new CellKey(1, 0));
        var (next, _) = CampLeads.Pick(rules, Own, Line(), new LeadBuyer { Tier = "regular", Have = have, Bought = 1 });
        // 4 km and 2.5 km stay; nothing else within 5 km.
        Assert.Equal([new CellKey(-1, 0), new CellKey(2, 0)], Cells(next));
        Assert.All(next, o => Assert.Equal(rules.Price(o.Distance, 1, "regular"), o.Price));
        have.UnionWith([new CellKey(-1, 0), new CellKey(2, 0)]);
        var (none, why) = CampLeads.Pick(rules, Own, Line(), new LeadBuyer { Tier = "regular", Have = have });
        Assert.Empty(none);
        Assert.Equal(CampLeadsWhy.NoneInReach, why);
    }

    [Fact]
    public void TheFirstSlotGoesToTheNearestProspectorWhenTheBuyerHasNoneInReach()
    {
        var rules = Shipped();
        var (offers, _) = CampLeads.Pick(rules, Own, Line(prospectors: 3), new LeadBuyer { Tier = "trusted" });
        Assert.Equal(new CellKey(3, 0), offers[0].Cell);
        Assert.True(offers[0].Prospector);
        Assert.Equal([new CellKey(3, 0), new CellKey(1, 0), new CellKey(-1, 0), new CellKey(2, 0), new CellKey(4, 0)], Cells(offers));
        // With a prospector marked within the radius, plain nearest first.
        var (marked, _) = CampLeads.Pick(rules, Own, Line(prospectors: [3, 4]), new LeadBuyer { Tier = "trusted", Have = new HashSet<CellKey> { new(4, 0) } });
        Assert.Equal([new CellKey(1, 0), new CellKey(-1, 0), new CellKey(2, 0), new CellKey(3, 0)], Cells(marked));
        Assert.All(marked, o => Assert.False(o.Prospector));
        // A prospector beyond the radius is neither preferred nor offered.
        var (far, _) = CampLeads.Pick(rules, Own, Line(prospectors: 5), new LeadBuyer { Tier = "regular" });
        Assert.DoesNotContain(new CellKey(5, 0), Cells(far));
    }

    [Fact]
    public void APartnerGetsEightWithinTwelveKm()
    {
        var camps = Line();
        camps.Add(new CampOption(new CellKey(0, 3), TraderTypes.Farmer, 0, 6100, 6100));
        camps.Add(new CampOption(new CellKey(0, -6), TraderTypes.Farmer, 0, -13000, 13000));
        var (offers, _) = CampLeads.Pick(Shipped(), Own, camps, new LeadBuyer { Tier = "partner" });
        Assert.Equal(8, offers.Count);
        Assert.All(offers, o => Assert.True(o.Distance <= 12000));
        Assert.Equal(offers.OrderBy(o => o.Distance).Select(o => o.Cell), Cells(offers));
    }

    [Fact]
    public void TheTradersOwnCampIsNeverATarget()
    {
        var camps = Line();
        camps.Add(new CampOption(Own, TraderTypes.Prospector, 10, 10, 14));
        var (offers, _) = CampLeads.Pick(Shipped(), Own, camps, new LeadBuyer { Tier = "partner" });
        Assert.DoesNotContain(Own, Cells(offers));
        var (stranger, _) = CampLeads.Pick(Shipped(), Own, camps, new LeadBuyer());
        Assert.DoesNotContain(Own, Cells(stranger));
    }

    [Fact]
    public void AStrangerGetsOneMapToTheNearestCampTheyLackAndHaveNotVisited()
    {
        var rules = Shipped();
        var (offers, why) = CampLeads.Pick(rules, Own, Line(prospectors: 2), new LeadBuyer());
        Assert.Equal(CampLeadsWhy.None, why);
        var only = Assert.Single(offers);
        Assert.Equal(new CellKey(1, 0), only.Cell);
        Assert.False(only.Prospector);
        var (skip, _) = CampLeads.Pick(rules, Own, Line(), new LeadBuyer
        {
            Have = new HashSet<CellKey> { new(1, 0) }, Visited = new HashSet<CellKey> { new(-1, 0) },
        });
        Assert.Equal(new CellKey(2, 0), Assert.Single(skip).Cell);
    }

    [Fact]
    public void AStrangerWhoseGroupHadItsMapHereGetsNoMore()
    {
        var (offers, why) = CampLeads.Pick(Shipped(), Own, Line(), new LeadBuyer { StrangerUsed = true });
        Assert.Empty(offers);
        Assert.Equal(CampLeadsWhy.StrangerUsed, why);
    }

    [Fact]
    public void AStrangerChainsFromCampToCamp()
    {
        var rules = Shipped();
        // At A (0,0): the nearest is B (1,0). At B, having visited A and B, the nearest they lack
        // is C (2,0), not A again; and so on.
        var visited = new HashSet<CellKey> { Own };
        var have = new HashSet<CellKey>();
        var at = Own;
        var path = new List<CellKey>();
        for (int step = 0; step < 3; step++)
        {
            var camps = Line().Select(c => c with { Distance = Math.Abs(c.X - at.X * 2000) }).ToList();
            camps.Add(new CampOption(Own, TraderTypes.Smith, 0, 0, Math.Abs(at.X * 2000)));
            var (offers, _) = CampLeads.Pick(rules, at, camps, new LeadBuyer { Have = have, Visited = visited });
            var next = Assert.Single(offers).Cell;
            path.Add(next);
            have.Add(next);
            visited.Add(next);
            at = next;
        }
        Assert.Equal([new CellKey(1, 0), new CellKey(2, 0), new CellKey(3, 0)], path);
    }

    [Fact]
    public void AStrangersMapReachesNoFurtherThanStrangerReach()
    {
        var rules = new CampLeadRules { StrangerReach = 3000, Tiers = { ["known"] = new() { Maps = 1, Radius = 1 } } };
        var (offers, why) = CampLeads.Pick(rules, Own, Line(), new LeadBuyer { Have = new HashSet<CellKey> { new(1, 0), new(-1, 0) } });
        Assert.Empty(offers);
        Assert.Equal(CampLeadsWhy.NoneInReach, why);
    }

    [Fact]
    public void ProblemsNameBadTiers()
    {
        var r = new CampLeadRules { Base = 0, PerBought = 0.5, Tiers = { ["known"] = new() { Maps = 0, Radius = 0, Discount = 1.5 } } };
        var problems = r.Problems();
        Assert.Contains(problems, p => p.Contains("base"));
        Assert.Contains(problems, p => p.Contains("perBought"));
        Assert.Contains(problems, p => p.Contains("'known' has no maps"));
        Assert.Contains(problems, p => p.Contains("'known' has a radius"));
        Assert.Contains(problems, p => p.Contains("'known' has a discount"));
    }

    [Fact]
    public void TheCellsAroundCoverTheReach()
    {
        var cells = CampLeads.CellsAround(1000, 1000, 12000).ToHashSet();
        foreach (var (x, z) in new[] { (13000, 1000), (-11000, 1000), (1000, -11000), (9000, 9000) })
            Assert.Contains(TraderGrid.CellOf(x, z), cells);
    }
}
