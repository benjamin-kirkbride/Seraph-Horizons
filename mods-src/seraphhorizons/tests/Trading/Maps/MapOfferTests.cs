using System.Text.Json;
using SeraphHorizons.Mod.Trading.Core;
using SeraphHorizons.Mod.Trading.Maps.Core;

namespace SeraphHorizons.Mod.Tests.Trading.Maps;

/// <summary>Traders' maps and leads (#455): which deposits a trader offers, what standing allows,
/// the shipped price table, and where leads go.</summary>
public class MapOfferTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static MapPriceTable Shipped() =>
        JsonSerializer.Deserialize<MapPriceTable>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "map-prices.json")), Options)!;

    [Fact]
    public void OneOfferPerMetalTheNearestUnsoldNearestMetalsFirst()
    {
        DepositOption[] options =
        [
            new("copper:1,1", "copper", 900, Unsold: false),
            new("copper:2,1", "copper", 3000, Unsold: true),
            new("copper:2,2", "copper", 4500, Unsold: true),
            new("tin:1,1", "tin", 1200, Unsold: true),
            new("iron:0,0", "iron", 4000, Unsold: true),
            new("zinc:0,0", "zinc", 200, Unsold: true),
        ];
        var (offers, soldOut) = MapOffers.PickOre(options, 3);
        Assert.False(soldOut);
        Assert.Equal(["zinc:0,0", "tin:1,1", "copper:2,1"], offers.Select(o => o.Id));
        // A deposit whose sale is being checked is not offered again.
        var (reserved, _) = MapOffers.PickOre(options, 4, new HashSet<string> { "copper:2,1" });
        Assert.Equal("copper:2,2", reserved.Single(o => o.Metal == "copper").Id);
    }

    [Fact]
    public void SoldOutOnlyWhenThereWereDepositsAndNoneIsLeft()
    {
        Assert.Equal((0, false), Count(MapOffers.PickOre([], 4)));
        Assert.Equal((0, true), Count(MapOffers.PickOre([new("copper:1,1", "copper", 10, false), new("tin:1,1", "tin", 20, false)], 4)));
        var (gravel, gravelSoldOut) = MapOffers.PickGravel([new("gravel:1,1", "gravel", 300, true), new("gravel:1,2", "gravel", 100, false)]);
        Assert.Equal("gravel:1,1", gravel?.Id);
        Assert.False(gravelSoldOut);
        var (none, soldOut) = MapOffers.PickGravel([new("gravel:1,2", "gravel", 100, false)]);
        Assert.Null(none);
        Assert.True(soldOut);
    }

    private static (int, bool) Count((List<DepositOption> Offers, bool SoldOut) r) => (r.Offers.Count, r.SoldOut);

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 3)]
    [InlineData(4, 3)]
    [InlineData(-1, 1)]
    public void MapTierGatesPrecision(int mapTier, int precision) => Assert.Equal(precision, MapOffers.MaxPrecision(mapTier));

    [Fact]
    public void TheShippedPriceTableIsWholeAndRisesWithPrecisionAndSize()
    {
        var t = Shipped();
        Assert.Empty(t.Problems());
        Assert.Equal(5000, t.OreRadius);
        Assert.Equal(2000, t.GravelRadius);
        foreach (string size in new[] { "unsurveyed", "small", "medium", "large" })
            for (int p = 1; p < 3; p++)
                Assert.True(t.OrePrice("copper", size, p + 1) > t.OrePrice("copper", size, p), $"{size} precision {p + 1}");
        Assert.True(t.OrePrice("copper", "large", 2) > t.OrePrice("copper", "small", 2));
        Assert.True(t.OrePrice("gold", null, 1) > t.OrePrice("copper", null, 1));
        Assert.Equal(t.OrePrice("copper", "unsurveyed", 2), t.OrePrice("copper", null, 2));
        Assert.Equal(t.OrePrice("copper", "unsurveyed", 2), t.OrePrice("copper", "nonsense", 2));
        Assert.Equal(6, t.SettlementPrice());
    }

    [Fact]
    public void ProblemsNameAMissingRowAndTheCampLeadRules()
    {
        var t = new MapPriceTable { Ore = { ["small"] = [1, 2] }, Settlement = 0 };
        var problems = t.Problems();
        Assert.Contains(problems, p => p.Contains("unsurveyed"));
        Assert.Contains(problems, p => p.Contains("'small' has 2 prices"));
        Assert.Contains(problems, p => p.Contains("settlement"));
        Assert.Contains(problems, p => p.Contains("campLeads has no 'stranger' tier"));
    }

    [Fact]
    public void TheSettlementLeadGoesToTheCentreOfItsEightKmCell() =>
        Assert.Equal(TraderGrid.SettlementCentre(1024, -3000), LeadTargets.Settlement(1024, -3000));

    [Fact]
    public void LeadKindsRoundTrip()
    {
        foreach (var kind in Enum.GetValues<LeadKind>())
        {
            Assert.True(LeadTargets.TryParse(LeadTargets.Code(kind), out var back));
            Assert.Equal(kind, back);
        }
        Assert.False(LeadTargets.TryParse("nowhere", out _));
        Assert.False(LeadTargets.TryParse(null, out _));
    }
}
