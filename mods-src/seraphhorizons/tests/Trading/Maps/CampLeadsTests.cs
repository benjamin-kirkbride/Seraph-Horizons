using System.Text.Json;
using SeraphHorizons.Mod.Trading.Core;
using SeraphHorizons.Mod.Trading.Maps.Core;

namespace SeraphHorizons.Mod.Tests.Trading.Maps;

/// <summary>Leads to other trader camps: distance in rings of grid cells, which a buyer is offered
/// by standing (count, reach in rings, prospector first, the stranger's one map chained from camp to
/// camp) and what they cost.</summary>
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

    private static readonly string[] TierCodes = ["stranger", "known", "regular", "trusted", "partner"];

    private static readonly CellKey Own = new(0, 0);

    /// <summary>A camp in a cell, its site 2,000 blocks per cell from the trader (on the line).</summary>
    private static CampOption Camp(int cx, int cz, string type = TraderTypes.Smith) =>
        new(new CellKey(cx, cz), type, cx * 2000, cz * 2000, CampLeads.Ring(Own, new CellKey(cx, cz)), Math.Sqrt(cx * cx + cz * cz) * 2000);

    /// <summary>Camps east of the trader on a line, one per cell (rings 1..7), all smiths but where
    /// <paramref name="prospectors"/> says; and one west, in ring 1, a little further than the first.</summary>
    private static List<CampOption> Line(params int[] prospectors)
    {
        var camps = new List<CampOption>();
        for (int i = 1; i <= 7; i++) camps.Add(Camp(i, 0, prospectors.Contains(i) ? TraderTypes.Prospector : TraderTypes.Smith));
        camps.Add(new CampOption(new CellKey(-1, 0), TraderTypes.Cook, -2500, 0, 1, 2500));
        return camps;
    }

    private static List<CellKey> Cells(List<CampLeadOffer> offers) => offers.Select(o => o.Cell).ToList();

    // ---- Rings ----

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(1, 0, 1)]
    [InlineData(1, 1, 1)]
    [InlineData(-1, 1, 1)]
    [InlineData(2, -1, 2)]
    [InlineData(-3, 3, 3)]
    [InlineData(5, -2, 5)]
    public void TheRingIsTheChebyshevDistanceBetweenCells(int x, int z, int ring)
    {
        Assert.Equal(ring, CampLeads.Ring(Own, new CellKey(x, z)));
        Assert.Equal(ring, CampLeads.Ring(new CellKey(x + 7, z - 4), new CellKey(7, -4)));
    }

    [Fact]
    public void RingOneIsTheEightCellsAroundAndEachRingHasEightTimesItsNumber()
    {
        Assert.Equal([Own], CampLeads.RingCells(Own, 0));
        var one = CampLeads.RingCells(Own, 1).ToHashSet();
        Assert.Equal(8, one.Count);
        Assert.Equal(TraderGrid.Neighbours(Own).ToHashSet(), one);
        for (int r = 1; r <= 10; r++)
        {
            var ring = CampLeads.RingCells(new CellKey(3, -2), r).ToList();
            Assert.Equal(8 * r, ring.Count);
            Assert.Equal(ring.Count, ring.Distinct().Count());
            Assert.All(ring, c => Assert.Equal(r, CampLeads.Ring(new CellKey(3, -2), c)));
        }
        var around = CampLeads.CellsAround(Own, 2).ToList();
        Assert.Equal(25, around.Count);
        Assert.Equal(around.Select(c => CampLeads.Ring(Own, c)).Order(), around.Select(c => CampLeads.Ring(Own, c)));
    }

    // ---- The shipped table ----

    [Fact]
    public void TheShippedTableIsTheDecidedOne()
    {
        var r = Shipped();
        Assert.Empty(r.Problems());
        Assert.Equal(12, r.Base);
        Assert.Equal(1.5, r.DistanceCurve);
        Assert.Equal(3, r.RepeatCurve);
        Assert.Equal([(1, 1, 1.0, 1.0), (2, 1, 0.85, 0.85), (3, 2, 0.7, 0.7), (5, 3, 0.6, 0.55), (8, 5, 0.5, 0.4)],
            TierCodes.Select(c => r.TierFor(c)).Select(t => (t.Maps, t.Reach, t.Ease, t.Discount)));
        Assert.True(r.IsStranger("stranger"));
        Assert.True(r.IsStranger("no-such-tier"));
        Assert.True(r.IsStranger(null));
        Assert.False(r.IsStranger("known"));
        // A tier not listed gets the stranger's rule.
        Assert.Same(r.TierFor("stranger"), r.TierFor("no-such-tier"));
    }

    [Fact]
    public void ReachIsInRingsByStanding()
    {
        var r = Shipped();
        Assert.Equal([1, 1, 2, 3, 5], TierCodes.Select(c => r.TierFor(c).Reach));
    }

    // ---- Price ----

    [Theory]
    [InlineData("stranger", 1, 0, 24)]
    [InlineData("known", 1, 1, 53)]
    [InlineData("regular", 2, 4, 79)]
    [InlineData("trusted", 3, 9, 76)]
    [InlineData("partner", 5, 12, 55)]
    public void ThePriceIsBaseTimesTheTwoCurvesTimesTheDiscount(string tier, int ring, int bought, int gears)
    {
        var r = Shipped();
        Assert.Equal(gears, r.Price(ring, bought, tier));
        var t = r.TierFor(tier);
        double expected = 12 * (1 + 1.5 * t.Ease * Math.Log(1 + ring)) * (1 + 3 * t.Ease * Math.Log(1 + bought)) * t.Discount;
        Assert.Equal((int)Math.Round(expected, MidpointRounding.AwayFromZero), r.Price(ring, bought, tier));
    }

    [Fact]
    public void ThePriceRisesWithRingAndCountAndIsAtLeastAGear()
    {
        var r = Shipped();
        foreach (string tier in TierCodes)
            for (int n = 0; n < 20; n++)
                for (int ring = 0; ring < 10; ring++)
                {
                    Assert.True(r.Price(ring + 1, n, tier) >= r.Price(ring, n, tier));
                    Assert.True(r.Price(ring, n + 1, tier) >= r.Price(ring, n, tier));
                }
        Assert.Equal(12, r.Price(0, 0, "stranger"));
        Assert.Equal(1, new CampLeadRules { Base = 0.01 }.Price(0, 0, null));
        Assert.Equal(12, new CampLeadRules().Price(-3, -1, "anything"));
    }

    [Fact]
    public void ForTheSameRingAndCountThePriceNeverRisesWithStanding()
    {
        var r = Shipped();
        for (int ring = 0; ring <= 10; ring++)
            for (int n = 0; n <= 40; n++)
            {
                var prices = TierCodes.Select(c => r.Price(ring, n, c)).ToList();
                for (int i = 1; i < prices.Count; i++)
                    Assert.True(prices[i] <= prices[i - 1], $"ring {ring}, n {n}: {string.Join(", ", prices)}");
            }
    }

    [Fact]
    public void ProblemsNameBadTiers()
    {
        var r = new CampLeadRules
        {
            Base = 0, DistanceCurve = -1, RepeatCurve = -1,
            Tiers = { ["known"] = new() { Maps = 0, Reach = 0, Ease = 0, Discount = 1.5 } },
        };
        var problems = r.Problems();
        Assert.Contains(problems, p => p.Contains("base"));
        Assert.Contains(problems, p => p.Contains("distanceCurve"));
        Assert.Contains(problems, p => p.Contains("repeatCurve"));
        Assert.Contains(problems, p => p.Contains("no 'stranger' tier"));
        Assert.Contains(problems, p => p.Contains("'known' has no maps"));
        Assert.Contains(problems, p => p.Contains("'known' reaches"));
        Assert.Contains(problems, p => p.Contains("'known' has an ease"));
        Assert.Contains(problems, p => p.Contains("'known' has a discount"));
    }

    // ---- Which camps ----

    [Fact]
    public void KnownGetsTheTwoNearestItLacksInRingOne()
    {
        var (offers, why) = CampLeads.Pick(Shipped(), Own, Line(), new LeadBuyer { Tier = "known", Have = new HashSet<CellKey> { new(-5, -5) } });
        Assert.Equal(CampLeadsWhy.None, why);
        Assert.Equal([new CellKey(1, 0), new CellKey(-1, 0)], Cells(offers));
        Assert.All(offers, o => Assert.Equal(1, o.Ring));
        Assert.All(offers, o => Assert.False(o.Prospector));
    }

    [Fact]
    public void ADiagonalNeighbourIsRingOneAndPricedAsAStraightOne()
    {
        var r = Shipped();
        var camps = new List<CampOption> { Camp(1, 1), Camp(2, 0) };
        var (offers, _) = CampLeads.Pick(r, Own, camps, new LeadBuyer { Tier = "known" });
        var diagonal = Assert.Single(offers);
        Assert.Equal(new CellKey(1, 1), diagonal.Cell);
        Assert.Equal(r.Price(1, 0, "known"), diagonal.Price);
        Assert.Equal(r.Price(1, 0, "known"), CampLeads.Pick(r, Own, [Camp(0, -1)], new LeadBuyer { Tier = "known" }).Offers.Single().Price);
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
        // Ring 1 west and ring 2 stay; nothing else within 2 rings.
        Assert.Equal([new CellKey(-1, 0), new CellKey(2, 0)], Cells(next));
        Assert.All(next, o => Assert.Equal(rules.Price(o.Ring, 1, "regular"), o.Price));
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
        Assert.Equal([new CellKey(3, 0), new CellKey(1, 0), new CellKey(-1, 0), new CellKey(2, 0)], Cells(offers));
        // With a prospector marked within reach, plain nearest first.
        var (marked, _) = CampLeads.Pick(rules, Own, Line(prospectors: [2, 3]), new LeadBuyer { Tier = "trusted", Have = new HashSet<CellKey> { new(3, 0) } });
        Assert.Equal([new CellKey(1, 0), new CellKey(-1, 0), new CellKey(2, 0)], Cells(marked));
        Assert.All(marked, o => Assert.False(o.Prospector));
        // A prospector beyond the reach is neither preferred nor offered.
        var (far, _) = CampLeads.Pick(rules, Own, Line(prospectors: 3), new LeadBuyer { Tier = "regular" });
        Assert.DoesNotContain(new CellKey(3, 0), Cells(far));
    }

    [Fact]
    public void APartnerGetsEightWithinFiveRings()
    {
        var camps = Line();
        camps.Add(Camp(0, 3, TraderTypes.Farmer));
        camps.Add(Camp(-2, 2, TraderTypes.Farmer));
        camps.Add(Camp(-4, 5, TraderTypes.Farmer));
        camps.Add(Camp(0, -6, TraderTypes.Farmer));
        var (offers, _) = CampLeads.Pick(Shipped(), Own, camps, new LeadBuyer { Tier = "partner" });
        Assert.Equal(8, offers.Count);
        Assert.All(offers, o => Assert.True(o.Ring <= 5));
        Assert.Equal(offers.OrderBy(o => o.Ring).ThenBy(o => o.Distance).Select(o => o.Cell), Cells(offers));
    }

    [Fact]
    public void TheTradersOwnCampIsNeverATarget()
    {
        var camps = Line();
        camps.Add(new CampOption(Own, TraderTypes.Prospector, 10, 10, 0, 14));
        var (offers, _) = CampLeads.Pick(Shipped(), Own, camps, new LeadBuyer { Tier = "partner" });
        Assert.DoesNotContain(Own, Cells(offers));
        var (stranger, _) = CampLeads.Pick(Shipped(), Own, camps, new LeadBuyer());
        Assert.DoesNotContain(Own, Cells(stranger));
    }

    // ---- The stranger ----

    [Fact]
    public void AStrangerGetsOneMapToTheNearestCampInRingOneTheyLackAndHaveNotVisited()
    {
        var rules = Shipped();
        var (offers, why) = CampLeads.Pick(rules, Own, Line(prospectors: 2), new LeadBuyer());
        Assert.Equal(CampLeadsWhy.None, why);
        var only = Assert.Single(offers);
        Assert.Equal(new CellKey(1, 0), only.Cell);
        Assert.Equal(rules.Price(1, 0, "stranger"), only.Price);
        Assert.False(only.Prospector);
        var (skip, _) = CampLeads.Pick(rules, Own, Line(), new LeadBuyer
        {
            Have = new HashSet<CellKey> { new(1, 0) }, Visited = new HashSet<CellKey> { new(5, 5) },
        });
        Assert.Equal(new CellKey(-1, 0), Assert.Single(skip).Cell);
    }

    [Fact]
    public void AStrangersReachIsRingOne()
    {
        // Ring 1 all marked or visited: nothing, though ring 2 has camps.
        var (offers, why) = CampLeads.Pick(Shipped(), Own, Line(), new LeadBuyer
        {
            Have = new HashSet<CellKey> { new(1, 0) }, Visited = new HashSet<CellKey> { new(-1, 0) },
        });
        Assert.Empty(offers);
        Assert.Equal(CampLeadsWhy.NoneInReach, why);
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
        // At A (0,0): the nearest is B (1,0) (the cook west is further). At B, having visited A and
        // B, the nearest they lack in ring 1 is C (2,0), not A again; and so on.
        var visited = new HashSet<CellKey> { Own };
        var have = new HashSet<CellKey>();
        var at = Own;
        var path = new List<CellKey>();
        for (int step = 0; step < 3; step++)
        {
            var here = at;
            var camps = Line().Where(c => c.Cell.Z == 0 && c.Cell.X > 0)
                .Select(c => c with { Ring = CampLeads.Ring(here, c.Cell), Distance = Math.Abs(c.X - here.X * 2000) }).ToList();
            camps.Add(new CampOption(Own, TraderTypes.Smith, 0, 0, CampLeads.Ring(here, Own), Math.Abs(here.X * 2000)));
            var (offers, _) = CampLeads.Pick(rules, here, camps, new LeadBuyer { Have = have, Visited = visited });
            var next = Assert.Single(offers).Cell;
            path.Add(next);
            have.Add(next);
            visited.Add(next);
            at = next;
        }
        Assert.Equal([new CellKey(1, 0), new CellKey(2, 0), new CellKey(3, 0)], path);
    }
}
