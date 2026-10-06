using SeraphHorizons.Mod.Ore.Core;

namespace SeraphHorizons.Tests.Ore;

public class OreCellsTests
{
    [Fact]
    public void SpotsAreDeterministicPerSeedMetalAndCell()
    {
        var a = new OreCells(12345).Spots("copper", new CellPos(3, -2));
        var b = new OreCells(12345).Spots("copper", new CellPos(3, -2));
        Assert.Equal(a, b);
        Assert.NotEqual(a, new OreCells(12346).Spots("copper", new CellPos(3, -2)));
        Assert.NotEqual(a, new OreCells(12345).Spots("iron", new CellPos(3, -2)));
        Assert.NotEqual(a, new OreCells(12345).Spots("copper", new CellPos(3, -1)));
    }

    [Fact]
    public void SpotsArePinnedAcrossBuilds()
    {
        // The hash must never change: a world's deposits are where its seed put them.
        var spot = new OreCells(42).Spots("copper", new CellPos(100, 100))[0];
        Assert.Equal(new OreSpot(0, 503922, 503958), spot);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1, -1)]
    [InlineData(204, 97)]
    public void SpotsStayInsideTheCellAwayFromItsEdge(int cx, int cz)
    {
        var cells = new OreCells(7);
        foreach (var metal in OreMetals.All)
        {
            var spots = cells.Spots(metal, new CellPos(cx, cz));
            Assert.Equal(OreCells.SpotCount, spots.Length);
            Assert.Equal(Enumerable.Range(0, OreCells.SpotCount), spots.Select(s => s.Index));
            foreach (var s in spots)
            {
                Assert.InRange(s.X, cx * 5000 + 500, cx * 5000 + 4500);
                Assert.InRange(s.Z, cz * 5000 + 500, cz * 5000 + 4500);
                Assert.Equal(new CellPos(cx, cz), cells.CellOf(metal, s.X, s.Z));
            }
        }
    }

    [Fact]
    public void SpotsSpreadOverTheCell()
    {
        // Over many cells, primary spots cover the inner square roughly evenly.
        var cells = new OreCells(99);
        var quadrants = new int[4];
        for (int i = 0; i < 400; i++)
        {
            var s = cells.Spots("tin", new CellPos(i, 0))[0];
            int q = (s.X - i * 5000 < 2500 ? 0 : 1) + (s.Z < 2500 ? 0 : 2);
            quadrants[q]++;
        }
        Assert.All(quadrants, n => Assert.InRange(n, 70, 130));
    }

    [Fact]
    public void CellSizePerMetalOverridesTheDefault()
    {
        var cells = new OreCells(1, 5000, new Dictionary<string, int> { ["gold"] = 8000, ["tin"] = 10 });
        Assert.Equal(5000, cells.CellSize("copper"));
        Assert.Equal(8000, cells.CellSize("gold"));
        Assert.Equal(OreCells.MinCellSize, cells.CellSize("tin"));
        Assert.Equal(new CellPos(1, 0), cells.CellOf("gold", 8000, 7999));
        Assert.Equal(new CellPos(-1, -1), cells.CellOf("copper", -1, -5000));
    }

    [Fact]
    public void ApprovesOnlyTheFirstTryFromTheActiveSpotsChunk()
    {
        var spot = new OreSpot(0, 1000, 2000); // chunk (31, 62)
        Assert.True(OreCells.Approves(spot, 992, 1984, firstOfMetalInChunk: true));
        Assert.True(OreCells.Approves(spot, 1023, 2015, firstOfMetalInChunk: true));
        Assert.False(OreCells.Approves(spot, 1023, 2015, firstOfMetalInChunk: false));
        Assert.False(OreCells.Approves(spot, 1024, 2000, firstOfMetalInChunk: true));
        Assert.False(OreCells.Approves(spot, 991, 2000, firstOfMetalInChunk: true));
        Assert.False(OreCells.Approves(null, 1000, 2000, firstOfMetalInChunk: true));
    }

    [Theory]
    [InlineData(0, 32, 0)]
    [InlineData(31, 32, 0)]
    [InlineData(32, 32, 1)]
    [InlineData(-1, 32, -1)]
    [InlineData(-32, 32, -1)]
    [InlineData(-33, 32, -2)]
    public void FloorDivRoundsDown(int value, int divisor, int expected) =>
        Assert.Equal(expected, OreCells.FloorDiv(value, divisor));
}
