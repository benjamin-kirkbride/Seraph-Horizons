using System.Text.Json;
using SeraphHorizons.Mod.Ore.Core;
using SeraphHorizons.Mod.Trading.Core;
using SeraphHorizons.Mod.Trading.Maps.Core;
using SeraphHorizons.Mod.Trading.Values.Core;

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

    private static readonly string[] SizeClasses = ["unsurveyed", "small", "medium", "large"];

    /// <summary>The shipped ore-sizes.json's metals that have a size range (the ones mapped).</summary>
    private static Dictionary<string, SizeTargets> ShippedSizes()
    {
        var table = new OreSizeTable(JsonSerializer.Deserialize<Dictionary<string, OreSizeTable.Entry>>(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ore-sizes.json")), Options)!);
        return table.Metals.Where(m => table.TargetsFor(m) != null).ToDictionary(m => m, m => table.TargetsFor(m)!.Value);
    }

    private static readonly ItemValues ShippedValues =
        ItemValues.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "item-values.json")));

    private static int Ore(MapPriceTable t, string metal, string? size, int precision) =>
        t.OrePrice(metal, size, precision, ShippedSizes()[metal], ShippedValues.ValueOf(MapPriceTable.IngotCode(metal)))!.Value;

    [Fact]
    public void TheShippedPriceTableIsWholeAndRisesWithPrecisionAndSize()
    {
        var t = Shipped();
        Assert.Empty(t.Problems());
        Assert.Equal(5000, t.OreRadius);
        Assert.Equal(2000, t.GravelRadius);
        Assert.Equal([0.03, 0.06, 0.10], t.Share);
        foreach (string metal in new[] { "gold", "silver", "nickel", "titanium", "chromium", "platinum" })
            Assert.Equal(3, t.Scarcity[metal]);
        Assert.False(t.Scarcity.ContainsKey("copper"));
        foreach (string size in SizeClasses)
            for (int p = 1; p < 3; p++)
                Assert.True(Ore(t, "copper", size, p + 1) > Ore(t, "copper", size, p), $"{size} precision {p + 1}");
        Assert.True(Ore(t, "copper", "large", 2) > Ore(t, "copper", "medium", 2));
        Assert.True(Ore(t, "copper", "medium", 2) > Ore(t, "copper", "small", 2));
        Assert.True(Ore(t, "gold", null, 1) > Ore(t, "copper", null, 1));
        Assert.Equal(Ore(t, "copper", "medium", 2), Ore(t, "copper", "unsurveyed", 2));
        Assert.Equal(Ore(t, "copper", "unsurveyed", 2), Ore(t, "copper", null, 2));
        Assert.Equal(Ore(t, "copper", "unsurveyed", 2), Ore(t, "copper", "nonsense", 2));
        Assert.Equal(6, t.SettlementPrice());
    }

    [Fact]
    public void EveryMetalWithASizeRangeHasAnIngotValueSoItsMapsSell()
    {
        var t = Shipped();
        foreach (var (metal, targets) in ShippedSizes())
        {
            Assert.True(ShippedValues.ValueOf(MapPriceTable.IngotCode(metal)) > 0, $"no value for {MapPriceTable.IngotCode(metal)}");
            Assert.NotNull(t.OrePrice(metal, null, 1, targets, ShippedValues.ValueOf(MapPriceTable.IngotCode(metal))));
        }
    }

    [Theory]
    // Copper 150..1000 ingots: thirds of 283⅓, middles 291⅔, 575, 858⅓.
    [InlineData("small", 291.6667)]
    [InlineData("medium", 575)]
    [InlineData("unsurveyed", 575)]
    [InlineData(null, 575)]
    [InlineData("large", 858.3333)]
    public void TheBandMiddleIsTheMiddleOfItsThirdOfSmallToLarge(string? size, double ingots) =>
        Assert.Equal(ingots, MapPriceTable.BandMiddleIngots(new SizeTargets(150, 400, 1000), size), 3);

    [Fact]
    public void AnOreMapIsTheBandsIngotsTimesValueShareAndScarcity()
    {
        var t = new MapPriceTable { Share = [0.03, 0.06, 0.10], Scarcity = { ["gold"] = 3 } };
        var copper = new SizeTargets(150, 400, 1000);
        // 575 ingots × 2 gears × 0.06 = 69; × 3 for a scarce metal = 207.
        Assert.Equal(69, t.OrePrice("copper", "medium", 2, copper, 2));
        Assert.Equal(207, t.OrePrice("gold", "medium", 2, copper, 2));
        // Precision clamps to the shares there are; no floor beyond a gear.
        Assert.Equal(t.OrePrice("copper", "large", 3, copper, 2), t.OrePrice("copper", "large", 9, copper, 2));
        Assert.Equal(1, t.OrePrice("copper", "small", 1, new SizeTargets(1, 2, 4), 0.01));
        // Nothing to price by: no size range, no ingot value, no shares.
        Assert.Null(t.OrePrice("copper", "small", 1, null, 2));
        Assert.Null(t.OrePrice("copper", "small", 1, copper, 0));
        Assert.Null(new MapPriceTable().OrePrice("copper", "small", 1, copper, 2));
    }

    [Fact]
    public void ProblemsNameTheSharesAndTheCampLeadRules()
    {
        var t = new MapPriceTable { Share = [0.03, 0], Scarcity = { ["gold"] = 0 }, Settlement = 0 };
        var problems = t.Problems();
        Assert.Contains(problems, p => p.Contains("share has 2 entries"));
        Assert.Contains(problems, p => p.Contains("a share of 0 or less"));
        Assert.Contains(problems, p => p.Contains("scarcity 'gold'"));
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
