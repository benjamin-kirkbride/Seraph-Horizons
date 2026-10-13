using SeraphHorizons.Mod.Ore.Core;

namespace SeraphHorizons.Tests.Ore;

public class GradePocketsTests
{
    private static readonly double[] Units4 = [3, 5, 7, 10];
    private static readonly GradePocketSettings Default = GradePocketSettings.Default;

    // Every block of a 96-block cube, so a few thousand pockets.
    private static IEnumerable<(int X, int Y, int Z)> Cube(int ox = 1000, int oy = 0, int oz = -500, int side = 96)
    {
        for (int x = 0; x < side; x++)
        for (int y = 0; y < side; y++)
        for (int z = 0; z < side; z++)
            yield return (ox + x, oy + y, oz + z);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void InteriorGradesKeepTheirMeanUnitsExactly(int grade)
    {
        var (p, q) = GradePockets.Odds(Units4, grade, Default);
        Assert.Equal(Default.Spread, p + q, 9);
        Assert.Equal(Units4[grade], GradePockets.MeanUnits(Units4, grade, Default), 9);
    }

    [Fact]
    public void TheDefaultSplitsAreAsDocumented()
    {
        // Medium: 5 sits halfway between 3 and 7. Rich: 7 is 2 above 5 and 3 below 10.
        Assert.Equal((0.175, 0.175), Round(GradePockets.Odds(Units4, 1, Default)));
        Assert.Equal((0.21, 0.14), Round(GradePockets.Odds(Units4, 2, Default)));
    }

    [Fact]
    public void EndsGoOneWayByTheEndSpread()
    {
        Assert.Equal((0.0, 0.3), GradePockets.Odds(Units4, 0, Default));
        Assert.Equal((0.3, 0.0), GradePockets.Odds(Units4, 3, Default));
        // A grade past the rock's cap is the cap, the top end.
        Assert.Equal((0.3, 0.0), GradePockets.Odds([3, 5], 3, Default));
        Assert.Equal((0.0, 0.0), GradePockets.Odds([3], 0, Default));
        // The units they move by, as documented: poor +20 %, bountiful -9 %.
        Assert.Equal(3.6, GradePockets.MeanUnits(Units4, 0, Default), 9);
        Assert.Equal(9.1, GradePockets.MeanUnits(Units4, 3, Default), 9);
        var none = Default with { EndSpread = 0 };
        Assert.Equal((0.0, 0.0), GradePockets.Odds(Units4, 0, none));
        Assert.Equal((0.0, 0.0), GradePockets.Odds(Units4, 3, none));

        var end = Default with { EndSpread = 0.15 };
        Assert.Equal((0.0, 0.15), GradePockets.Odds(Units4, 0, end));
        Assert.Equal((0.15, 0.0), GradePockets.Odds(Units4, 3, end));
        Assert.Equal((0.15, 0.0), GradePockets.Odds([3, 5, 7], 3, end));
        Assert.Equal(3 + 0.15 * 2, GradePockets.MeanUnits(Units4, 0, end), 9);
    }

    [Fact]
    public void IndexWeightingSplitsEvenly()
    {
        var even = Default with { Weighting = GradeWeighting.Index };
        Assert.Equal((0.175, 0.175), Round(GradePockets.Odds(Units4, 2, even)));
    }

    [Fact]
    public void PocketsAreDeterministicPerSeedAndPosition()
    {
        var a = new GradePockets(12345, Default);
        var b = new GradePockets(12345, Default);
        var c = new GradePockets(12346, Default);
        int differ = 0;
        foreach (var (x, y, z) in Cube(side: 16))
        {
            Assert.Equal(a.Pocket(x, y, z), b.Pocket(x, y, z));
            if (a.Pocket(x, y, z) != c.Pocket(x, y, z)) differ++;
        }
        Assert.True(differ > 16 * 16 * 16 / 2, $"only {differ} blocks differ with another seed");
    }

    [Fact]
    public void GradesStayWithinOneStepAndTheRocksRange()
    {
        var pockets = new GradePockets(7, Default with { EndSpread = 0.3 });
        double[] units3 = [3, 5, 7];
        foreach (var (x, y, z) in Cube(side: 24))
        {
            Assert.InRange(pockets.Pocket(x, y, z), 0, 0.9999999999);
            for (int drawn = 0; drawn < 4; drawn++)
            {
                int g = pockets.GradeAt(Units4, drawn, x, y, z);
                Assert.InRange(g, Math.Max(0, drawn - 1), Math.Min(3, drawn + 1));
                int capped = pockets.GradeAt(units3, drawn, x, y, z);
                Assert.InRange(capped, Math.Max(0, Math.Min(drawn, 2) - 1), 2);
            }
        }
    }

    [Theory]
    [InlineData(1, 0.3)]
    [InlineData(2, 0.3)]
    [InlineData(0, 0.3)]
    [InlineData(3, 0.3)]
    [InlineData(0, 0.0)]
    public void PlacedMeanUnitsMatchTheOdds(int drawn, double endSpread)
    {
        var settings = Default with { EndSpread = endSpread };
        var pockets = new GradePockets(424242, settings);
        double sum = 0;
        int n = 0;
        var counts = new int[4];
        foreach (var (x, y, z) in Cube())
        {
            int g = pockets.GradeAt(Units4, drawn, x, y, z);
            counts[g]++;
            sum += Units4[g];
            n++;
        }
        // A 96-block cube holds ~1,700 pockets of 8: the shares land within a couple of percent.
        double expected = GradePockets.MeanUnits(Units4, drawn, settings);
        Assert.InRange(sum / n, expected * 0.98, expected * 1.02);
        if (drawn is 1 or 2 || endSpread == 0) Assert.Equal(Units4[drawn], expected, 9);
        var (p, q) = GradePockets.Odds(Units4, drawn, settings);
        Assert.InRange((double)counts[drawn] / n, 1 - p - q - 0.03, 1 - p - q + 0.03);
    }

    [Fact]
    public void PocketsAreBlobsAboutTheirSize()
    {
        // Neighbouring blocks mostly share a pocket: with pockets ~8 across, about one in eight
        // steps along an axis crosses into another.
        var pockets = new GradePockets(99, Default);
        int same = 0, steps = 0;
        foreach (var (x, y, z) in Cube(side: 48))
        {
            steps++;
            if (pockets.Pocket(x, y, z) == pockets.Pocket(x + 1, y, z)) same++;
        }
        double share = (double)same / steps;
        Assert.InRange(share, 0.75, 0.95);
    }

    [Fact]
    public void ChunkBordersDontShowInThePockets()
    {
        // The same field either side of x = 32k: no seam, as the grade comes from the position alone.
        var pockets = new GradePockets(5, Default);
        int same = 0, steps = 0;
        for (int y = 0; y < 64; y++)
        for (int z = 0; z < 64; z++)
        {
            steps++;
            if (pockets.Pocket(31, y, z) == pockets.Pocket(32, y, z)) same++;
        }
        Assert.InRange((double)same / steps, 0.75, 0.95);
    }

    [Fact]
    public void SettingsAreClamped()
    {
        var s = new GradePocketSettings(0.5, 3, -1).Clamped();
        Assert.Equal((2.0, 1.0, 0.0), (s.PocketSize, s.Spread, s.EndSpread));
        Assert.Equal(GradePocketSettings.DefaultEndSpread, new GradePocketSettings(EndSpread: double.NaN).Clamped().EndSpread);
        Assert.Equal(GradePocketSettings.DefaultPocketSize, new GradePocketSettings(double.NaN).Clamped().PocketSize);
    }

    [Fact]
    public void ARecordFromBeforeGradePocketsHasThemOff()
    {
        var old = "v1\noreCells=True\ncellSize=5000\nnoSurfaceCopper=True\nsmallerDeposits=True\nrarerDistricts=True\nplacerFields=True\nplacerCellSize=1500\n";
        Assert.False(OreWorldRecord.Parse(old).GradePockets);
        var record = OreWorldRecord.AllOff with { GradePockets = true };
        Assert.Equal(record, OreWorldRecord.Parse(record.Serialize()));
        Assert.False(record.Effective(record with { GradePockets = false }).GradePockets);
        Assert.True(record.Effective(record).GradePockets);
    }

    private static (double, double) Round((double A, double B) t) => (Math.Round(t.A, 9), Math.Round(t.B, 9));
}
