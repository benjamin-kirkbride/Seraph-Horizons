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
        // In country rock that is host rock too the whole spheroid is ore (#943, #946): its
        // horizontal radius doubles, four times the volume.
        var (ch, cv) = DistrictVeins.LensRadii(6, 4, 4, inHostRock: true);
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

    // #946: IOG's shoot is an ellipse in a sheared frame (the segment's tangent, the fault's global
    // across), clipped to its box; density 1 − r² sums to π r (r/2) / 2 with square axes, and a
    // straight fault along the strike holds its width times 4r/3 of it.
    [Fact]
    public void ShootShapeMeasuresIogsEllipse()
    {
        var square = new ShootShape(0, 0, 1, 0, 0, 1, 35, 17.5, 37);
        Assert.InRange(square.Whole(), 0.98 * Math.PI * 35 * 17.5 / 2, 1.02 * Math.PI * 35 * 17.5 / 2);
        var straight = Enumerable.Range(-40, 81).SelectMany(x => Enumerable.Range(-3, 6).Select(z => (x, z))).ToList();
        Assert.InRange(square.Over(straight), 0.95 * 6 * 4.0 / 3 * 35, 1.05 * 6 * 4.0 / 3 * 35);
        // Sheared: the tangent 30° off the across direction's normal. A fault along the tangent
        // leaves the ellipse long before its strike, and the box clips a long strike.
        double c = Math.Cos(Math.PI / 6), s = Math.Sin(Math.PI / 6);
        var sheared = new ShootShape(0, 0, c, s, 0, 1, 140, 17.5, 142);
        var alongTangent = Enumerable.Range(-150, 301).SelectMany(t => Enumerable.Range(-3, 6)
            .Select(w => ((int)Math.Floor(t * c), (int)Math.Floor(t * s + w)))).Distinct().ToList();
        double sum = sheared.Over(alongTangent);
        Assert.True(sum < 0.5 * 6 * 4.0 / 3 * 140, $"{sum}: a sheared shoot's fault rock is far under its width times 4r/3");
        Assert.Equal(0, sheared.DensityAt(0, 143));
        // Samples: about the asked number, inside the shoot, weighted by density.
        var points = sheared.Samples(24);
        Assert.InRange(points.Count, 12, 48);
        Assert.All(points, p => Assert.Equal(sheared.DensityAt(p.X, p.Z), p.Weight, 9));
        Assert.All(points, p => Assert.True(p.Weight > 0));
    }

    // #946: a shoot's host blocks at a height are its fault rock and the share of its country rock
    // that is host rock there; a band is grown by them, so a shoot in host country rock is short.
    [Fact]
    public void CountryRockThatIsHostRockShortensTheBand()
    {
        var zone = new DistrictZone(DistrictZoneKind.Shoot, [Ore("tin")], 200, 1, 255, CountryBlocks: 800, CountryShare: y => y < 50 ? 1 : 0);
        Assert.Equal(1000, zone.HostBlocksAt(10), 9);
        Assert.Equal(200, zone.HostBlocksAt(60), 9);
        Assert.Equal(200, (zone with { CountryShare = null }).HostBlocksAt(10), 9);
        // The same drawn sizes: fault rock alone needs about five times the height of a band all
        // in host country rock.
        var faultOnly = Enumerable.Range(0, 8).Select(_ => Shoot(Ore("tin"))).ToList();
        var inHost = Enumerable.Range(0, 8).Select(_ => zone with { CountryShare = _ => 1 }).ToList();
        var a = Rules.Plan(3, faultOnly, 110);
        var b = Rules.Plan(3, inHost, 110);
        double perLayer = 0.35 * 0.0625;
        for (int i = 0; i < 8; i++)
        {
            double heldA = (a[i].BandMax!.Value - a[i].BandMin!.Value + 1) * 200 * perLayer;
            double heldB = (b[i].BandMax!.Value - b[i].BandMin!.Value + 1) * 1000 * perLayer;
            Assert.InRange(heldB, heldA - 1000 * perLayer, heldA + 1000 * perLayer);
            Assert.True(b[i].BandMax - b[i].BandMin < a[i].BandMax - a[i].BandMin);
        }
        // A share by height is reckoned layer by layer.
        var mixed = Rules.Plan(3, Enumerable.Range(0, 8).Select(_ => zone).ToList(), 110);
        for (int i = 0; i < 8; i++)
        {
            double held = Enumerable.Range(mixed[i].BandMin!.Value, mixed[i].BandMax!.Value - mixed[i].BandMin!.Value + 1)
                .Sum(y => zone.HostBlocksAt(y) * perLayer);
            double heldA = (a[i].BandMax!.Value - a[i].BandMin!.Value + 1) * 200 * perLayer;
            Assert.InRange(held, heldA - 1000 * perLayer, heldA + 1000 * perLayer);
        }
    }

    // #946: a widened shoot whose band would be the whole room and still short takes the first wider
    // shape that holds its drawn size; one that fits keeps its own.
    [Fact]
    public void ShootsTooThinForTheRoomTakeAWiderShape()
    {
        var wide = new DistrictVeins(Sizes, Settings with { Blocks = 4 });
        var thin = new DistrictZone(DistrictZoneKind.Shoot, [Ore("tin", ingots: 0.0125)], 5, 1, 255, 4,
            Wider: [(10, 0), (100, 0), (1000, 0)]);
        var plans = wide.Plan(3, Enumerable.Range(0, 8).Select(_ => thin).ToList(), 110);
        Assert.All(plans, p =>
        {
            Assert.InRange(p.Wider, 2, 3);
            Assert.True(p.Held >= p.Drawn * 0.9, $"{p.Held} of {p.Drawn}");
            Assert.InRange(p.BandMax!.Value - p.BandMin!.Value + 1, 1, 100);
        });
        var fits = wide.Plan(3, [thin with { BlocksPerLayer = 2000 }], 110)[0];
        Assert.Equal(0, fits.Wider);
        // None wide enough: the widest, cut to the room.
        var hopeless = wide.Plan(3, [thin with { Wider = [(6, 0)] }], 110)[0];
        Assert.Equal(1, hopeless.Wider);
        Assert.Equal((4, 104), (hopeless.BandMin, hopeless.BandMax));
        Assert.True(hopeless.Held < hopeless.Drawn);
    }

    // #946: two ores of one metal in a zone (magnetite and pyrite) are one vein of it: the band holds
    // the drawn size between them, not each.
    [Fact]
    public void OresOfOneMetalShareTheDrawnSize()
    {
        var one = Enumerable.Range(0, 8).Select(_ => Shoot(Ore("tin"))).ToList();
        var two = Enumerable.Range(0, 8).Select(_ => Shoot(Ore("tin"), Ore("tin"))).ToList();
        var a = Rules.Plan(3, one, 110);
        var b = Rules.Plan(3, two, 110);
        for (int i = 0; i < 8; i++)
        {
            // Each tin ore has half the blocks, together the same: the same band.
            Assert.Equal((a[i].BandMin, a[i].BandMax), (b[i].BandMin, b[i].BandMax));
            Assert.Equal(a[i].Held, b[i].Held, 6);
        }
    }

    // #946: a vein of two sized metals, one richer a block than the other, grows to the poorer's size
    // and thins the richer one's ore to its own (both share the zone's draw).
    [Fact]
    public void TwoMetalsInAVeinEachHoldTheirSize()
    {
        var zones = Enumerable.Range(0, 8).Select(_ => Shoot(Ore("tin", ingots: 0.4), Ore("bismuth", ingots: 0.1))).ToList();
        foreach (var p in Rules.Plan(3, zones, 110))
        {
            Assert.True(p.Held >= p.Drawn * 0.9, "cut to the room");
            int h = p.BandMax!.Value - p.BandMin!.Value + 1;
            double layer = 100 * 0.35 * 0.1;
            Assert.InRange(h * layer, p.Drawn - layer, p.Drawn + layer);
            Assert.NotNull(p.Thin);
            Assert.InRange(p.Thin![0], 0.2, 0.3);
            Assert.Equal(1.0, p.Thin[1]);
            Assert.InRange(h * 100 * 0.35 * 0.4 * p.Thin[0], p.Drawn * 0.9, p.Drawn * 1.1);
        }
        // One metal: nothing thinned.
        Assert.All(Rules.Plan(3, Enumerable.Range(0, 8).Select(_ => Shoot(Ore("tin"))).ToList(), 110), p => Assert.Null(p.Thin));
    }

    // #946: an ore that places nothing (density 0: its code names no block in the host rock) is
    // taken out of its zone by the game side, so the ores that place hold all its blocks.
    [Fact]
    public void OresThatPlaceNothingHoldNoShare()
    {
        var alone = Enumerable.Range(0, 8).Select(_ => Shoot(Ore("tin"))).ToList();
        var withDead = Enumerable.Range(0, 8).Select(_ => Shoot(Ore("tin"), Ore("bismuth", density: 0))).ToList();
        Assert.Equal(Rules.Plan(3, alone, 110).Select(p => (p.BandMin, p.BandMax)), Rules.Plan(3, withDead, 110).Select(p => (p.BandMin, p.BandMax)));
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
}
