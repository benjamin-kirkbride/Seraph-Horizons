using SeraphHorizons.Mod.Trading.Core;

namespace SeraphHorizons.Mod.Tests.Trading;

public class TraderGridTests
{
    // The shipped lists' camp weights.
    public static readonly Dictionary<string, double> Weights = new()
    {
        ["smith"] = 1.0, ["mechanic"] = 0.8, ["prospector"] = 1.0, ["farmer"] = 1.2, ["cook"] = 1.0, ["tailor"] = 1.0,
        ["carpenter"] = 1.0, ["mason"] = 1.0, ["animaldealer"] = 0.9, ["generalstore"] = 1.4, ["curiodealer"] = 0.6,
    };

    private static IEnumerable<CellKey> Area(int from, int to)
    {
        for (int x = from; x < to; x++)
            for (int z = from; z < to; z++)
                yield return new CellKey(x, z);
    }

    [Fact]
    public void StableHashIsAFunctionOfItsInputs()
    {
        Assert.Equal(StableHash.Of(42, "trader", 3, 4, 1), StableHash.Of(42, "trader", 3, 4, 1));
        var variants = new[]
        {
            StableHash.Of(42, "trader", 3, 4, 1), StableHash.Of(43, "trader", 3, 4, 1), StableHash.Of(42, "trader-type", 3, 4, 1),
            StableHash.Of(42, "trader", 4, 3, 1), StableHash.Of(42, "trader", 3, 4, 2), StableHash.Of(42, "trader", -3, 4, 1),
        };
        Assert.Equal(variants.Length, variants.Distinct().Count());
        double mean = Enumerable.Range(0, 4000).Average(i => StableHash.Unit(7, "u", i, -i));
        Assert.InRange(mean, 0.47, 0.53);
    }

    [Fact]
    public void TypesAreTheSameWhateverOrderCellsAreAskedIn()
    {
        var cells = Area(-20, 20).ToList();
        var a = new TraderGrid(1234, Weights);
        var forward = cells.Select(a.TypeOf).ToList();
        var b = new TraderGrid(1234, Weights);
        var backward = Enumerable.Reverse(cells).Select(b.TypeOf).Reverse().ToList();
        Assert.Equal(forward, backward);
        var other = new TraderGrid(4321, Weights);
        Assert.NotEqual(forward, cells.Select(other.TypeOf).ToList());
    }

    [Theory]
    [InlineData(1L)]
    [InlineData(987654321L)]
    [InlineData(-5L)]
    public void NoCellSharesItsTypeWithANeighbour(long seed)
    {
        var grid = new TraderGrid(seed, Weights);
        foreach (var cell in Area(-30, 30))
            foreach (var n in TraderGrid.Neighbours(cell))
                Assert.NotEqual(grid.TypeOf(cell), grid.TypeOf(n));
    }

    [Theory]
    [InlineData(1L)]
    [InlineData(987654321L)]
    public void AProspectorIsWithinTwoCellsOfEveryCellAndNeverNextToAnother(long seed)
    {
        var grid = new TraderGrid(seed, Weights);
        foreach (var cell in Area(-30, 30))
        {
            bool near = false;
            for (int dx = -2; dx <= 2 && !near; dx++)
                for (int dz = -2; dz <= 2 && !near; dz++)
                    near = grid.TypeOf(new CellKey(cell.X + dx, cell.Z + dz)) == TraderTypes.Prospector;
            Assert.True(near, $"no prospector within two cells of {cell}");
        }
        // One per 3x3 block, so about a ninth.
        int prospectors = Area(-30, 30).Count(c => grid.TypeOf(c) == TraderTypes.Prospector);
        Assert.Equal(60 * 60 / 9, prospectors);
    }

    [Fact]
    public void EveryTypeTurnsUpAndWeightsBiasHowOften()
    {
        var grid = new TraderGrid(99, Weights);
        var counts = Area(-60, 60).GroupBy(grid.TypeOf).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(TraderTypes.All.OrderBy(t => t), counts.Keys.OrderBy(t => t));
        Assert.True(counts["generalstore"] > counts["curiodealer"] * 1.5, string.Join(", ", counts));
        Assert.True(counts["farmer"] > counts["mechanic"], string.Join(", ", counts));
    }

    [Fact]
    public void ATypeWithoutWeightNeverGetsACell()
    {
        var weights = new Dictionary<string, double>(Weights) { ["curiodealer"] = 0 };
        var grid = new TraderGrid(5, weights);
        Assert.DoesNotContain(Area(-20, 20), c => grid.TypeOf(c) == TraderTypes.CurioDealer);
        weights["mechanic"] = 0;
        Assert.Throws<ArgumentException>(() => new TraderGrid(5, weights));
    }

    [Fact]
    public void SpotsLieInsideTheirCellAwayFromItsEdge()
    {
        var grid = new TraderGrid(77, Weights);
        foreach (var cell in Area(-5, 5))
        {
            var spots = grid.Spots(cell);
            Assert.NotEmpty(spots);
            Assert.Equal(spots, grid.Spots(cell));
            foreach (var s in spots)
            {
                Assert.Equal(cell, TraderGrid.CellOf(s.X, s.Z));
                Assert.InRange(s.X - cell.X * TraderGrid.CellSize, TraderGrid.Margin, TraderGrid.CellSize - TraderGrid.Margin);
                Assert.InRange(s.Z - cell.Z * TraderGrid.CellSize, TraderGrid.Margin, TraderGrid.CellSize - TraderGrid.Margin);
                Assert.Contains(grid.AttemptsInChunk(cell, s.ChunkX, s.ChunkZ), i => spots[i] == s);
            }
        }
        // A cell far from any settlement centre has every attempt.
        Assert.Equal(TraderGrid.Attempts, grid.Spots(new CellKey(0, 0)).Count);
    }

    [Fact]
    public void NoSpotIsInASettlementReserve()
    {
        var grid = new TraderGrid(31337, Weights);
        // The four cells around the settlement centre at (4096, 4096) and others further out.
        foreach (var cell in Area(-4, 8))
            foreach (var s in grid.Spots(cell))
            {
                Assert.False(TraderGrid.InSettlementReserve(s.X, s.Z));
                foreach (int cx in new[] { -4096, 4096, 12288 })
                    foreach (int cz in new[] { -4096, 4096, 12288 })
                    {
                        double d = Math.Sqrt((double)(s.X - cx) * (s.X - cx) + (double)(s.Z - cz) * (s.Z - cz));
                        Assert.True(d >= TraderGrid.SettlementReserve, $"spot {s} is {d:0} from settlement centre {cx},{cz}");
                    }
            }
        Assert.True(TraderGrid.InSettlementReserve(4096 + 100, 4096 - 100));
        Assert.True(TraderGrid.InSettlementReserve(-4096, -4096 + 511));
        Assert.False(TraderGrid.InSettlementReserve(4096 + 600, 4096));
        Assert.Equal((4096, 4096), TraderGrid.SettlementCentre(100, 8000));
    }

    [Fact]
    public void CellsAreFloorDividedIncludingNegativeCoordinates()
    {
        Assert.Equal(new CellKey(0, 0), TraderGrid.CellOf(0, 2047));
        Assert.Equal(new CellKey(-1, 1), TraderGrid.CellOf(-1, 2048));
        Assert.Equal(new CellKey(1, -1), TraderGrid.CellOfChunk(64, -1));
        Assert.True(CellKey.TryParse("12,-3", out var key));
        Assert.Equal(new CellKey(12, -3), key);
        Assert.Equal("12,-3", key.ToString());
        Assert.False(CellKey.TryParse("12;3", out _));
    }

    [Fact]
    public void CellsAroundAreNearestFirstAndCoverTheRadius()
    {
        var cells = TraderGrid.CellsAround(1000, 1000, 3000).ToList();
        Assert.Equal(new CellKey(0, 0), cells[0]);
        Assert.Contains(new CellKey(-1, -1), cells);
        Assert.Contains(new CellKey(1, 1), cells);
        Assert.Equal(cells.Count, cells.Distinct().Count());
    }

    [Fact]
    public void ASpotTriesItselfFirstThenItsWholeChunkOnce()
    {
        var grid = new TraderGrid(12, Weights);
        var spot = grid.Spots(new CellKey(3, 3))[0];
        var positions = grid.PositionsInChunk(spot).ToList();
        Assert.Equal((spot.X & 31, spot.Z & 31), positions[0]);
        Assert.Equal(positions.Count, positions.Distinct().Count());
        Assert.All(positions, p => { Assert.InRange(p.X, 0, 31); Assert.InRange(p.Z, 0, 31); });
        Assert.True(positions.Count >= 256);
        Assert.Equal(positions, grid.PositionsInChunk(spot).ToList());
    }

    [Fact]
    public void StructureOrderIsASeededWeightedPermutation()
    {
        var grid = new TraderGrid(8, Weights);
        double[] weights = [1.5, 1.5, 2, 0.8, 1, 0, 0.3];
        var order = grid.StructureOrder(new CellKey(2, 3), 0, weights);
        Assert.Equal(order, grid.StructureOrder(new CellKey(2, 3), 0, weights));
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 6 }, order.OrderBy(i => i));
        // The heaviest comes first more often than the lightest.
        int heavyFirst = 0, lightFirst = 0;
        for (int i = 0; i < 2000; i++)
        {
            int first = grid.StructureOrder(new CellKey(i, -i), i % 8, weights)[0];
            if (first == 2) heavyFirst++;
            if (first == 6) lightFirst++;
        }
        Assert.True(heavyFirst > lightFirst * 4, $"{heavyFirst} vs {lightFirst}");
    }

    [Fact]
    public void EverySchematicAndRotationIsACandidateInASeededOrder()
    {
        var grid = new TraderGrid(8, Weights);
        var order = grid.CandidateOrder(new CellKey(2, 3), 1, 4, 5).ToList();
        Assert.Equal(20, order.Count);
        Assert.Equal(Enumerable.Range(0, 5).SelectMany(s => Enumerable.Range(0, 4).Select(r => (s, r))),
            order.OrderBy(c => c.Schematic).ThenBy(c => c.Rotation));
        Assert.Equal(order, grid.CandidateOrder(new CellKey(2, 3), 1, 4, 5));
        Assert.NotEqual(order, grid.CandidateOrder(new CellKey(2, 3), 2, 4, 5));
        Assert.NotEqual(order, grid.CandidateOrder(new CellKey(2, 3), 1, 5, 5));
        // Not schematic by schematic: the first few come from more than one.
        Assert.True(order.Take(6).Select(c => c.Schematic).Distinct().Count() > 1);
    }

    [Fact]
    public void SecondChancesAreAQuarterOfTheChunksInsideTheMargin()
    {
        var grid = new TraderGrid(31, Weights);
        var cell = new CellKey(5, -3);
        int per = TraderGrid.CellSize / 32, picked = 0, inner = 0;
        for (int cx = cell.X * per; cx < (cell.X + 1) * per; cx++)
            for (int cz = cell.Z * per; cz < (cell.Z + 1) * per; cz++)
            {
                int ox = cx * 32 - cell.X * TraderGrid.CellSize, oz = cz * 32 - cell.Z * TraderGrid.CellSize;
                bool inside = ox >= TraderGrid.Margin && ox + 32 <= TraderGrid.CellSize - TraderGrid.Margin
                              && oz >= TraderGrid.Margin && oz + 32 <= TraderGrid.CellSize - TraderGrid.Margin;
                if (inside) inner++;
                if (!grid.SecondChanceChunk(cx, cz)) continue;
                picked++;
                Assert.True(inside, $"{cx},{cz} is in the margin");
                var spot = TraderGrid.SecondChanceSpot(cx, cz);
                Assert.Equal((cx, cz), (spot.ChunkX, spot.ChunkZ));
                Assert.Equal(cell, TraderGrid.CellOf(spot.X, spot.Z));
            }
        Assert.InRange(picked, inner / TraderGrid.SecondChanceEvery * 0.8, inner / TraderGrid.SecondChanceEvery * 1.2);
        Assert.True(picked > TraderGrid.SecondChances * 4, "enough second chances that the cap, not the pick, bounds the cost");
        // None in a settlement reserve (around the settlement centre at 4096, 4096).
        int reserved = 0;
        for (int cx = 4096 / 32 - 20; cx < 4096 / 32 + 20; cx++)
            for (int cz = 4096 / 32 - 20; cz < 4096 / 32 + 20; cz++)
            {
                if (TraderGrid.InSettlementReserve(cx * 32 + 16, cz * 32 + 16)) reserved++;
                if (grid.SecondChanceChunk(cx, cz))
                    Assert.False(TraderGrid.InSettlementReserve(cx * 32 + 16, cz * 32 + 16));
            }
        Assert.True(reserved > 100);
    }

    [Fact]
    public void OutfitClimateFollowsVanillasTraderThresholds()
    {
        Assert.Equal("desert", TraderTypes.OutfitClimate(20, 0.3f));
        Assert.Equal("temperate", TraderTypes.OutfitClimate(20, 0.6f));
        Assert.Equal("cold", TraderTypes.OutfitClimate(-5, 0.6f));
        Assert.Equal("trader-female-smith-cold", TraderTypes.EntityPath("female", "smith", "cold"));
    }
}
