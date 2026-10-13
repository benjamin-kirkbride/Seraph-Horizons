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

    // #863: with the units at a quarter, a sized vein is widened four times and its band is the height
    // it was at vanilla's units: the same drawn size over four times the ore per layer.
    [Fact]
    public void WidenedVeinsKeepTheirHeightAtAQuarterTheUnits()
    {
        var wide = new DistrictVeins(Sizes, Settings with { Blocks = 4 });
        var before = Enumerable.Range(0, 8).Select(_ => Shoot(Ore("tin", ingots: 0.25))).ToList();
        var after = Enumerable.Range(0, 8).Select(_ => new DistrictZone(DistrictZoneKind.Shoot, [Ore("tin", ingots: 0.0625)], 200, 1, 255, 4)).ToList();
        var old = Rules.Plan(3, before, 110);
        var now = wide.Plan(3, after, 110);
        Assert.All(old, p => Assert.False(p.Widen));
        Assert.All(now, p => Assert.True(p.Widen));
        Assert.Equal(old.Select(p => (p.BandMin, p.BandMax)), now.Select(p => (p.BandMin, p.BandMax)));
        // Not widened: unsized ores, or with Blocks at 1.
        Assert.False(wide.Plan(3, [new DistrictZone(DistrictZoneKind.Shoot, [Ore("sulfur")], 200, 1, 255, 4)], 110)[0].Widen);
        Assert.False(Rules.Plan(3, after, 110)[0].Widen);
    }

    // A lens holding a kept sized metal (gold, silver) is widened too, and still has no band; one of
    // only unsized ores is not.
    [Fact]
    public void LensesOfKeptMetalsAreWidened()
    {
        var wide = new DistrictVeins(Sizes, Settings with { Blocks = 4 });
        var plan = wide.Plan(3, [new DistrictZone(DistrictZoneKind.Lens, [Ore("gold"), Ore("tin")], 0, 40, 60, 4)], 110)[0];
        Assert.True(plan.Widen);
        Assert.Equal([true, false], plan.OreKept);
        Assert.Null(plan.BandMin);
        Assert.False(wide.Plan(3, [new DistrictZone(DistrictZoneKind.Lens, [Ore("sulfur")], 0, 40, 60, 4)], 110)[0].Widen);
        Assert.False(Rules.Plan(3, [new DistrictZone(DistrictZoneKind.Lens, [Ore("gold")], 0, 40, 60, 4)], 110)[0].Widen);
    }

    // A lens's ore is the disc where it crosses its one-block fracture: four times the ore is twice
    // each radius.
    [Fact]
    public void LensRadiiDoubleForFourTimesTheOre()
    {
        var (h, v) = DistrictVeins.LensRadii(6, 4, 4);
        Assert.Equal(12f, h);
        Assert.Equal(8f, v);
        Assert.Equal(4.0, h * v / (6.0 * 4.0), 6);
        Assert.Equal((6f, 4f), DistrictVeins.LensRadii(6, 4, 1));
        // In a country rock host the whole spheroid is ore (#943): its horizontal radius doubles,
        // four times the volume.
        var (ch, cv) = DistrictVeins.LensRadii(6, 4, 4, hostIsCountryRock: true);
        Assert.Equal((12f, 4f), (ch, cv));
        Assert.Equal(4.0, ch * ch * cv / (6.0 * 6.0 * 4.0), 6);
    }

    // A ladder vein's slabs come closer: IOG's spacings over four, rounded, at least one.
    [Fact]
    public void LadderSlabsComeCloser()
    {
        var (spacings, gain) = DistrictVeins.CloserSlabs([2, 3, 4, 6, 8, 10], 4);
        Assert.Equal([1, 1, 1, 2, 2, 3], spacings);
        Assert.Equal(33.0 / 10, gain, 9);
        var unchanged = DistrictVeins.CloserSlabs([4, 8], 1);
        Assert.Equal([4, 8], unchanged.Spacings);
        Assert.Equal(1, unchanged.Gain);
    }

    // #943: a vein whose drawn size needs more height than the room under sea level is cut to the
    // room, not left its whole column (IOG's y 1 to the top of the world).
    [Fact]
    public void ABandTallerThanTheRoomIsTheRoom()
    {
        var tiny = new DistrictZone(DistrictZoneKind.Shoot, [Ore("tin", density: 0.01, ingots: 0.01)], 5, 1, 255);
        var plan = Rules.Plan(1, [tiny], 110)[0];
        Assert.True(plan.Keep);
        Assert.Equal(4, plan.BandMin);
        Assert.Equal(104, plan.BandMax);
        // A zone whose own range misses the room keeps a band inside its range.
        var high = new DistrictZone(DistrictZoneKind.Shoot, [Ore("tin", density: 0.01, ingots: 0.01)], 5, 120, 140);
        var p = Rules.Plan(1, [high], 110)[0];
        Assert.Equal((120, 140), (p.BandMin, p.BandMax));
    }

    // #943: a shoot cut to its fault rock is the fault's width times 4r/3; where the host is a
    // country rock it is the whole ellipse, π r (r/2) / 2 at density 1 − r², about 960 at r = 35.
    [Fact]
    public void ShootsAreCutToTheFaultOrWhole()
    {
        Assert.Equal(6 * 4.0 / 3 * 35, DistrictVeins.ShootBlocksPerLayer(35, 17.5, 3, false), 6);
        Assert.Equal(Math.PI * 35 * 17.5 / 2, DistrictVeins.ShootBlocksPerLayer(35, 17.5, 3, true), 6);
        Assert.InRange(DistrictVeins.ShootBlocksPerLayer(35, 17.5, 3, true), 960, 963);
        // Never more than the whole ellipse, however wide the fault.
        Assert.Equal(DistrictVeins.ShootBlocksPerLayer(35, 17.5, 0, true), DistrictVeins.ShootBlocksPerLayer(35, 17.5, 40, false), 6);
        // Widened along the strike only (#863, #943): four times as long is four times the ore either way.
        Assert.Equal(4, DistrictVeins.ShootBlocksPerLayer(140, 17.5, 3, true) / DistrictVeins.ShootBlocksPerLayer(35, 17.5, 3, true), 6);
        Assert.Equal(4, DistrictVeins.ShootBlocksPerLayer(140, 17.5, 3, false) / DistrictVeins.ShootBlocksPerLayer(35, 17.5, 3, false), 6);
    }

    // IOG's grade draw (ResolveOreCode): at richness 0 nine poorest to one of the next; at 1 the
    // richest, with one in ten of the grade under it. Cassiterite is 1 / 3 / 4 / 6 units a block.
    [Fact]
    public void ExpectedIngotsFollowIogsGradeDraw()
    {
        double[] tin = [0.0125, 0.0375, 0.05, 0.075];
        double w = (1 - 1 / 1.5) * (1 - 1 / 1.5);
        Assert.Equal((0.0125 + w * 0.0375) / (1 + w), DistrictVeins.ExpectedIngots(tin, 0), 9);
        Assert.Equal((0.075 + w * 0.05) / (1 + w), DistrictVeins.ExpectedIngots(tin, 1), 9);
        Assert.InRange(DistrictVeins.ExpectedIngots(tin, 1) / DistrictVeins.ExpectedIngots(tin, 0), 4.5, 5.5);
        Assert.Equal(0.02, DistrictVeins.ExpectedIngots([0.02], 0.7), 9);
        // Richer never gives less.
        for (double r = 0; r < 1; r += 0.05)
            Assert.True(DistrictVeins.ExpectedIngots(tin, r + 0.05) >= DistrictVeins.ExpectedIngots(tin, r) - 1e-12);
    }

    // #943: a band is grown by each layer's expected grade, so one that runs into its fault's rich
    // band stops sooner and holds its drawn size, not two to three times it.
    [Fact]
    public void BandsHoldTheirDrawnSizeThroughRichBands()
    {
        double[] tin = [0.0125, 0.0375, 0.05, 0.075];
        var poorFault = new float[256];
        var richFault = Enumerable.Range(0, 256).Select(y => y is >= 20 and <= 80 ? 1f : 0f).ToArray();
        IReadOnlyList<DistrictZone> Zones(float[] rich) => Enumerable.Range(0, 8)
            .Select(_ => new DistrictZone(DistrictZoneKind.Shoot, [Ore("tin", ingots: 0.0125) with { GradeIngots = tin }], 2000, 1, 255, 1, rich))
            .ToList();
        var poor = Rules.Plan(3, Zones(poorFault), 110);
        var rich = Rules.Plan(3, Zones(richFault), 110);
        double Held(DistrictZonePlan p, float[] r) =>
            Enumerable.Range(p.BandMin!.Value, p.BandMax!.Value - p.BandMin.Value + 1).Sum(y => 2000 * 0.35 * DistrictVeins.ExpectedIngots(tin, r[y]));
        for (int i = 0; i < 8; i++)
        {
            Assert.Equal(poor[i].BandMin, rich[i].BandMin);
            Assert.True(rich[i].BandMax <= poor[i].BandMax);
            // Each holds within half a layer of the other: the same drawn size.
            double a = Held(poor[i], poorFault), b = Held(rich[i], richFault);
            double layer = 2000 * 0.35 * DistrictVeins.ExpectedIngots(tin, 1);
            Assert.InRange(b, a - layer, a + layer);
        }
        Assert.Contains(Enumerable.Range(0, 8), i => rich[i].BandMax < poor[i].BandMax);
    }

    // Without a fault's richness or an ore's grades, the poorest grade times the allowance stands in.
    [Fact]
    public void WithoutGradesTheAllowanceStandsIn()
    {
        double[] tin = [0.0125, 0.0375];
        var flat = Enumerable.Range(0, 8).Select(_ => new DistrictZone(DistrictZoneKind.Shoot, [Ore("tin", ingots: 0.0125)], 2000, 1, 255)).ToList();
        var noFault = Enumerable.Range(0, 8).Select(_ => new DistrictZone(DistrictZoneKind.Shoot, [Ore("tin", ingots: 0.0125) with { GradeIngots = tin }], 2000, 1, 255)).ToList();
        Assert.Equal(Rules.Plan(5, flat, 110).Select(p => (p.BandMin, p.BandMax)), Rules.Plan(5, noFault, 110).Select(p => (p.BandMin, p.BandMax)));
    }

    [Theory]
    [InlineData("rock-granite", true)]
    [InlineData("game:rock-basalt", true)]
    [InlineData("rock-travertine", false)]
    [InlineData("", false)]
    public void HostIsCountryRockWhenItIsAStratum(string host, bool country) =>
        Assert.Equal(country, DistrictVeins.HostIsCountryRock(host, ["rock-granite", "game:rock-basalt", "rock-limestone"]));
}
