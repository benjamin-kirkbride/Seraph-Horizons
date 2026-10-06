using SeraphHorizons.Mod.Ore.Core;

namespace SeraphHorizons.Tests.Ore;

public class DistrictVeinsTests
{
    private static readonly OreSizeTable Sizes = new(new Dictionary<string, OreSizeTable.Entry>
    {
        ["tin"] = new(TargetIngots: 150, SmallIngots: 60, LargeIngots: 400),
        ["bismuth"] = new(TargetIngots: 150, SmallIngots: 60, LargeIngots: 400),
        ["gold"] = new(TargetIngots: 80, SmallIngots: 30, LargeIngots: 200),
        ["sulfur"] = new(Factor: 0.75),
    });

    private static readonly DistrictVeinSettings Settings = new(8, ["gold"], 1.0, 4, 6);

    private static DistrictVeins Rules => new(Sizes, Settings);

    private static DistrictOre Ore(string? metal, double weight = 1, double density = 0.35, double ingots = 0.0625) =>
        new(metal, weight, density, ingots);

    private static DistrictZone Shoot(params DistrictOre[] ores) => new(DistrictZoneKind.Shoot, ores, 200, 1, 255);

    [Theory]
    [InlineData("ore-*-cassiterite-granite", "cassiterite")]
    [InlineData("ore-poor-galena-granite", "galena")]
    [InlineData("ore-sulfur-travertine", "sulfur")]
    [InlineData("game:ore-*-quartz_nativegold-granite", "quartz_nativegold")]
    [InlineData("interestingoregen:saltpeterore", null)]
    [InlineData("rock-halite", null)]
    [InlineData("", null)]
    public void OreOfCode(string code, string? ore) => Assert.Equal(ore, DistrictVeins.OreOfCode(code));

    [Fact]
    public void DrawSizeHitsTheTargetsAtTheirQuantiles()
    {
        var t = new SizeTargets(60, 150, 400);
        Assert.Equal(60, DistrictVeins.DrawSize(t, 0.1), 6);
        Assert.Equal(150, DistrictVeins.DrawSize(t, 0.5), 6);
        Assert.Equal(400, DistrictVeins.DrawSize(t, 0.9), 6);
        Assert.True(DistrictVeins.DrawSize(t, 0) < 60);
        Assert.True(DistrictVeins.DrawSize(t, 0.999) > 400);
    }

    [Fact]
    public void CapsVeinsPerMetalAndKeepsTheKeptMetals()
    {
        var zones = Enumerable.Range(0, 40).Select(i => Shoot(Ore("tin"), Ore(i % 2 == 0 ? "gold" : "bismuth"))).ToList();
        var plans = Rules.Plan(42, zones, 110);
        int tin = plans.Count(p => p.Keep && p.OreKept[0]);
        int bismuth = plans.Where((p, i) => i % 2 == 1 && p.Keep && p.OreKept[1]).Count();
        int gold = plans.Where((p, i) => i % 2 == 0 && p.Keep && p.OreKept[1]).Count();
        Assert.Equal(8, tin);
        Assert.Equal(8, bismuth);
        Assert.Equal(20, gold);
        // A tin + bismuth vein with both over the cap places nothing: dropped.
        Assert.Contains(plans, p => !p.Keep);
    }

    [Fact]
    public void TheSameDistrictGetsTheSamePlan()
    {
        var zones = Enumerable.Range(0, 30).Select(_ => Shoot(Ore("tin"))).ToList();
        var a = Rules.Plan(7, zones, 110);
        var b = Rules.Plan(7, zones, 110);
        Assert.Equal(a.Select(p => (p.Keep, p.BandMin, p.BandMax)), b.Select(p => (p.Keep, p.BandMin, p.BandMax)));
        var c = Rules.Plan(8, zones, 110);
        Assert.NotEqual(a.Select(p => p.Keep), c.Select(p => p.Keep));
    }

    [Fact]
    public void BandsGiveTheDrawnSizesInRock()
    {
        var zones = Enumerable.Range(0, 8).Select(_ => Shoot(Ore("tin"))).ToList();
        var plans = Rules.Plan(3, zones, 110);
        double perLayer = 200 * 0.35 * 0.0625;
        var sizes = new List<double>();
        foreach (var p in plans)
        {
            Assert.True(p.Keep);
            Assert.NotNull(p.BandMin);
            Assert.InRange(p.BandMin!.Value, 4, 104);
            Assert.InRange(p.BandMax!.Value, p.BandMin.Value, 104);
            sizes.Add((p.BandMax.Value - p.BandMin.Value + 1) * perLayer);
        }
        // Drawn between about small / 2 and large x 1.3; 4.4 ingots a layer rounds within 2.2.
        Assert.All(sizes, s => Assert.InRange(s, 25, 560));
    }

    [Fact]
    public void LensesLoseCappedMetalsOnlyAndKeepTheirHeight()
    {
        var lens = new DistrictZone(DistrictZoneKind.Lens, [Ore("gold"), Ore("tin")], 0, 40, 60);
        var plan = Rules.Plan(1, [lens], 110)[0];
        Assert.True(plan.Keep);
        Assert.Equal([true, false], plan.OreKept);
        Assert.Null(plan.BandMin);
        var tinOnly = new DistrictZone(DistrictZoneKind.Lens, [Ore("tin")], 0, 40, 60);
        Assert.False(Rules.Plan(1, [tinOnly], 110)[0].Keep);
    }

    [Fact]
    public void UnmanagedOresAreLeftAlone()
    {
        var zones = Enumerable.Range(0, 20).Select(_ => Shoot(Ore(null), Ore("sulfur"))).ToList();
        Assert.All(Rules.Plan(5, zones, 110), p =>
        {
            Assert.True(p.Keep);
            Assert.All(p.OreKept, Assert.True);
            Assert.Null(p.BandMin);
        });
    }

    [Fact]
    public void OresThatPlaceNothingTakeNoSlot()
    {
        var dead = Enumerable.Range(0, 10).Select(_ => Shoot(Ore("tin", density: 0))).ToList();
        var live = Enumerable.Range(0, 10).Select(_ => Shoot(Ore("tin"))).ToList();
        var plans = Rules.Plan(9, [.. dead, .. live], 110);
        Assert.All(plans.Take(10), p => Assert.False(p.Keep));
        Assert.Equal(8, plans.Skip(10).Count(p => p.Keep));
    }

    [Fact]
    public void NoBandWhenTheVeinIsSmallerThanTheRock()
    {
        var tiny = new DistrictZone(DistrictZoneKind.Shoot, [Ore("tin", density: 0.01, ingots: 0.01)], 5, 1, 255);
        var plan = Rules.Plan(1, [tiny], 110)[0];
        Assert.True(plan.Keep);
        Assert.Null(plan.BandMin);
    }
}
