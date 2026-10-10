using SeraphHorizons.Mod.Ore.Core;

namespace SeraphHorizons.Tests.Ore;

public class DepositKeyTests
{
    [Theory]
    [InlineData("copper:12,-3", "copper", 12, -3)]
    [InlineData("Gravel:0,0", "gravel", 0, 0)]
    [InlineData("iron:-150,99", "iron", -150, 99)]
    public void ParsesIds(string text, string kind, int x, int z)
    {
        Assert.True(DepositKey.TryParse(text, out var key));
        Assert.Equal(new DepositKey(kind, new CellPos(x, z)), key);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("copper")]
    [InlineData(":1,2")]
    [InlineData("copper:1")]
    [InlineData("copper:a,b")]
    public void RefusesBadIds(string? text) => Assert.False(DepositKey.TryParse(text, out _));

    [Fact]
    public void IdRoundTrips()
    {
        var key = new DepositKey("tin", new CellPos(-1, 40));
        Assert.Equal("tin:-1,40", key.Id);
        Assert.True(DepositKey.TryParse(key.Id, out var back));
        Assert.Equal(key, back);
        Assert.True(new DepositKey(PlacerCells.Kind, default).IsGravel);
    }
}

public class DepositRegistryTests
{
    private static readonly DepositKey Copper = new("copper", new CellPos(3, 4));

    [Fact]
    public void ADepositIsSoldOnce()
    {
        var registry = new DepositRegistry();
        Assert.Equal(DepositState.Unsold, registry.Get(Copper).State);
        Assert.True(registry.MarkSold(Copper, "uid1", "Ada", 12.5));
        Assert.False(registry.MarkSold(Copper, "uid2", "Bo", 13));
        var r = registry.Get(Copper);
        Assert.Equal(DepositState.Sold, r.State);
        Assert.Equal("Ada", r.SoldToName);
        Assert.Equal(12.5, r.SoldAtDays);
    }

    [Fact]
    public void SoldOutIsNeverSoldButCanBeReset()
    {
        var registry = new DepositRegistry();
        registry.MarkSoldOut(Copper);
        Assert.False(registry.MarkSold(Copper, null, null, 1));
        registry.Reset(Copper);
        Assert.Equal(DepositState.Unsold, registry.Get(Copper).State);
        Assert.True(registry.MarkSold(Copper, null, null, 1));
    }

    [Fact]
    public void AWorkedOutMeasurementMarksSoldOutAndKeepsTheBuyer()
    {
        var registry = new DepositRegistry();
        registry.MarkSold(Copper, "uid1", "Ada", 2);
        registry.RecordMeasure(Copper, 412, SizeTier.Small, 1, 2, 3, 5, workedOut: false);
        Assert.Equal(DepositState.Sold, registry.Get(Copper).State);
        registry.RecordMeasure(Copper, 3, SizeTier.Small, 1, 2, 3, 40, workedOut: true);
        var r = registry.Get(Copper);
        Assert.Equal(DepositState.SoldOut, r.State);
        Assert.Equal("Ada", r.SoldToName);
        Assert.Equal(3, r.Ingots);
        Assert.Equal(40, r.MeasuredAtDays);
    }

    [Fact]
    public void ResetKeepsTheMeasurement()
    {
        var registry = new DepositRegistry();
        registry.RecordMeasure(Copper, 500, SizeTier.Medium, 10, 20, 30, 1, false);
        registry.MarkSold(Copper, "u", "n", 2);
        registry.Reset(Copper);
        var r = registry.Get(Copper);
        Assert.Equal(DepositState.Unsold, r.State);
        Assert.Null(r.SoldToName);
        Assert.Equal(SizeTier.Medium, r.Tier);
        Assert.Equal(20, r.Y);
    }

    [Fact]
    public void RoundTripsThroughJson()
    {
        var registry = new DepositRegistry();
        registry.MarkSold(Copper, "uid 1", "Ada \"the\" Bold", 12.25);
        registry.RecordMeasure(Copper, 412.5, SizeTier.Small, -1, 70, 9, 13, false);
        registry.MarkSoldOut(new DepositKey(PlacerCells.Kind, new CellPos(-2, 0)));
        var back = DepositRegistry.Parse(registry.Serialize());
        Assert.Equal(registry.Serialize(), back.Serialize());
        Assert.Equal(registry.Get(Copper), back.Get(Copper));
        Assert.Equal(DepositState.SoldOut, back.Get(new DepositKey("gravel", new CellPos(-2, 0))).State);
        Assert.Contains("\"soldOut\"", registry.Serialize(), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(DepositRegistry.Parse("").All());
    }

    [Fact]
    public void AMeasurementKeepsWhatTheOreIs()
    {
        var registry = new DepositRegistry();
        Assert.False(registry.Get(Copper).Surveyed);
        // Measured before makeups were kept: not surveyed, so it is checked again before it is offered.
        registry.RecordMeasure(Copper, 400, SizeTier.Medium, 1, 2, 3, 4, false);
        Assert.False(registry.Get(Copper).Surveyed);
        var makeup = new DepositMakeup
        {
            Ores = new() { ["malachite"] = 900, ["azurite"] = 100 },
            Grades = new() { ["poor"] = 60, ["medium"] = 10 },
            Rocks = new() { ["limestone"] = 70 },
        };
        registry.RecordMeasure(Copper, 400, SizeTier.Medium, 1, 2, 3, 5, false, makeup);
        Assert.True(registry.Get(Copper).Surveyed);
        var back = DepositRegistry.Parse(registry.Serialize()).Get(Copper);
        Assert.True(back.Surveyed);
        Assert.Equal(["malachite", "azurite"], back.Makeup!.MainOres());
        Assert.Equal("mostly:poor", back.Makeup.Mix()!.Code);
        Assert.Equal("limestone", back.Makeup.HostRock());
        Assert.DoesNotContain("surveyed", registry.Serialize(), StringComparison.OrdinalIgnoreCase);
    }
}

public class DepositSizingTests
{
    private static readonly SizeTargets Copper = new(150, 400, 1000);

    [Fact]
    public void UnitsBecomeIngotsAsTheSurveyCountsThem()
    {
        // 5 units a nugget, 20 nuggets an ingot.
        Assert.Equal(1, DepositSizing.Ingots(100));
        // A medium malachite block: 1.25 chunks of 20 units and 0.01 crystallised ore of 40.
        Assert.Equal(0.254, DepositSizing.Ingots(1.25 * 20 + 0.01 * 40), 6);
    }

    [Theory]
    [InlineData(0, SizeTier.Small)]
    [InlineData(150, SizeTier.Small)]
    [InlineData(433, SizeTier.Small)]
    [InlineData(434, SizeTier.Medium)]
    [InlineData(716, SizeTier.Medium)]
    [InlineData(717, SizeTier.Large)]
    [InlineData(5000, SizeTier.Large)]
    public void TiersAreThirdsOfTheMetalsRange(double ingots, SizeTier tier) =>
        Assert.Equal(tier, DepositSizing.Classify(ingots, Copper));

    [Fact]
    public void BelowATenthOfSmallIsWorkedOut()
    {
        Assert.True(DepositSizing.IsWorkedOut(14.9, Copper));
        Assert.False(DepositSizing.IsWorkedOut(15, Copper));
    }

    [Fact]
    public void TheSizeTableGivesRangesOnlyWhereItHasThem()
    {
        var table = new OreSizeTable(new Dictionary<string, OreSizeTable.Entry>
        {
            ["copper"] = new(3567, 400, null, 150, 1000),
            ["coal"] = new(Factor: 0.75),
            ["bad"] = new(null, 100, null, 300, 200),
        });
        Assert.Equal(new SizeTargets(150, 400, 1000), table.TargetsFor("copper"));
        Assert.Null(table.TargetsFor("coal"));
        Assert.Null(table.TargetsFor("bad"));
        Assert.Null(table.TargetsFor("tin"));
        Assert.Equal(400.0 / 3567, table.FactorFor("copper")!.Value, 6);
    }
}

public class MapPrecisionTests
{
    private static readonly DepositKey Key = new("copper", new CellPos(7, -3));

    [Fact]
    public void EveryCopyOfATierMarksTheSamePlace()
    {
        Assert.Equal(MapPrecision.Offset(42, Key, 1), MapPrecision.Offset(42, Key, 1));
        Assert.NotEqual(MapPrecision.Offset(42, Key, 1), MapPrecision.Offset(43, Key, 1));
        Assert.NotEqual(MapPrecision.Offset(42, Key, 1), MapPrecision.Offset(42, Key with { Kind = "iron" }, 1));
    }

    [Fact]
    public void TiersReachTheirDistanceAndExactIsExact()
    {
        for (int cx = 0; cx < 200; cx++)
        {
            var key = new DepositKey("tin", new CellPos(cx, cx * 3));
            var (rx, rz) = MapPrecision.Offset(9, key, MapPrecision.Rough);
            var (fx, fz) = MapPrecision.Offset(9, key, MapPrecision.Fair);
            Assert.True(Math.Sqrt(rx * rx + rz * rz) <= 400.5);
            Assert.True(Math.Sqrt(fx * fx + fz * fz) <= 150.5);
            // A better tier's marker lies between the worse one's and the deposit.
            Assert.InRange(fx, Math.Min(0, rx) - 1, Math.Max(0, rx) + 1);
            Assert.InRange(fz, Math.Min(0, rz) - 1, Math.Max(0, rz) + 1);
            Assert.Equal((0, 0), MapPrecision.Offset(9, key, MapPrecision.Exact));
        }
        Assert.False(MapPrecision.IsValid(0));
        Assert.False(MapPrecision.IsValid(4));
    }

    [Fact]
    public void RoughOffsetsSpreadOverTheDisc()
    {
        var far = Enumerable.Range(0, 500)
            .Select(i => MapPrecision.Offset(5, new DepositKey("iron", new CellPos(i, 0)), MapPrecision.Rough))
            .Count(o => Math.Sqrt(o.Dx * o.Dx + o.Dz * o.Dz) > 200);
        // Uniform over the disc: three quarters of the area lies beyond half the reach.
        Assert.InRange(far, 300, 450);
    }
}

public class CellSearchTests
{
    [Fact]
    public void FindsTheCellsACircleReaches()
    {
        Assert.Equal([new CellPos(0, 0)], CellSearch.Within(5000, 2500, 2500, 100));
        var cells = CellSearch.Within(5000, 4990, 4990, 100).ToHashSet();
        Assert.Equal(4, cells.Count); // the corner
        Assert.Contains(new CellPos(1, 1), cells);
        // A radius of one cell from a cell's centre reaches its eight neighbours but no further.
        var ring = CellSearch.Within(1500, 750, 750, 1500).ToList();
        Assert.Equal(9, ring.Count);
        Assert.Contains(new CellPos(-1, -1), ring);
    }
}
