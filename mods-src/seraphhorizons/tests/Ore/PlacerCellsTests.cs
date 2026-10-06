using SeraphHorizons.Mod.Ore.Core;

namespace SeraphHorizons.Tests.Ore;

public class PlacerCellsTests
{
    [Fact]
    public void SpotsAreDeterministicAndUnrelatedToOreCells()
    {
        var a = new PlacerCells(12345).Spots(new CellPos(3, -2));
        Assert.Equal(a, new PlacerCells(12345).Spots(new CellPos(3, -2)));
        Assert.NotEqual(a, new PlacerCells(12346).Spots(new CellPos(3, -2)));
        Assert.NotEqual(a, new PlacerCells(12345).Spots(new CellPos(3, -1)));
        // Same seed and cell size as an ore cell: still other spots (their own salt).
        var ore = new OreCells(12345, 1500).Spots(PlacerCells.Kind, new CellPos(3, -2));
        Assert.NotEqual(ore[0].X, a[0].X);
    }

    [Fact]
    public void SpotsArePinnedAcrossBuilds()
    {
        // The hash must never change: a world's fields are where its seed put them.
        var spot = new PlacerCells(42).Spots(new CellPos(100, 100))[0];
        Assert.Equal(new OreSpot(0, PinnedX, PinnedZ), spot);
        Assert.Equal(new PlacerFieldSpec(PinnedBlocks, PinnedThickness, PinnedDepth), new PlacerCells(42).Field(new CellPos(100, 100), 0));
    }

    private const int PinnedX = 150312, PinnedZ = 150624, PinnedBlocks = 329, PinnedThickness = 2, PinnedDepth = 0;

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1, -1)]
    [InlineData(204, -97)]
    public void SpotsStayInsideTheCellAwayFromItsEdge(int cx, int cz)
    {
        var spots = new PlacerCells(7).Spots(new CellPos(cx, cz));
        Assert.Equal(PlacerCells.SpotCount, spots.Length);
        foreach (var s in spots)
        {
            Assert.InRange(s.X, cx * 1500 + 150, cx * 1500 + 1350);
            Assert.InRange(s.Z, cz * 1500 + 150, cz * 1500 + 1350);
        }
    }

    [Fact]
    public void FieldsAre300To600BlocksOneOrTwoThickAndFitInAColumn()
    {
        var cells = new PlacerCells(99);
        var specs = Enumerable.Range(0, 400).Select(i => cells.Field(new CellPos(i, -i), i % PlacerCells.SpotCount)).ToList();
        Assert.All(specs, s =>
        {
            Assert.InRange(s.Blocks, PlacerCells.MinBlocks, PlacerCells.MaxBlocks);
            Assert.InRange(s.Thickness, 1, 2);
            Assert.InRange(s.Depth, 0, 1);
            // Centred at least its radius from the column's edges, so it never leaves the column.
            Assert.True(2 * (int)Math.Ceiling(s.Radius) + 1 <= TerrainPatch.Size, $"{s} is too wide");
        });
        Assert.Contains(specs, s => s.Thickness == 1);
        Assert.Contains(specs, s => s.Thickness == 2);
        Assert.Contains(specs, s => s.Depth == 1);
        // The disc's cells times its thickness come close to the block target.
        foreach (var s in specs.Take(50))
            Assert.InRange(PlacerSite.Disc(s.Radius).Count * s.Thickness, s.Blocks * 0.85, s.Blocks * 1.15);
    }

    [Fact]
    public void CellOfFloorsNegativeCoordinates()
    {
        var cells = new PlacerCells(1);
        Assert.Equal(new CellPos(-1, 0), cells.CellOf(-1, 1499));
        Assert.Equal(new CellPos(1, -2), cells.CellOf(1500, -1501));
        Assert.Equal(500, new PlacerCells(1, 100).CellSize); // clamped like ore cells
    }
}

public class PlacerSiteTests
{
    private const int N = TerrainPatch.Size;
    private static readonly PlacerFieldSpec Field = new(400, 2, 0);

    private static TerrainPatch Patch(Func<int, int, int> height, Func<int, int, bool>? water = null, Func<int, int, bool>? loose = null)
    {
        var h = new int[N * N];
        var w = new bool[N * N];
        var r = new bool[N * N];
        for (int z = 0; z < N; z++)
            for (int x = 0; x < N; x++)
            {
                int i = TerrainPatch.Index(x, z);
                h[i] = height(x, z);
                w[i] = water?.Invoke(x, z) ?? false;
                r[i] = loose?.Invoke(x, z) ?? true;
            }
        return new TerrainPatch(h, w, r);
    }

    [Fact]
    public void FlatLandBesideAStreamSuits()
    {
        var patch = Patch((_, _) => 110, water: (x, _) => x >= 29);
        var centre = PlacerSite.FindCentre(patch, Field, 16, 16);
        Assert.NotNull(centre);
        Assert.Equal((16, 16), centre); // the nearest centre to the spot that suits
    }

    [Fact]
    public void FlatDryLandWithoutAValleyDoesNot()
    {
        Assert.Null(PlacerSite.FindCentre(Patch((_, _) => 110), Field, 16, 16));
    }

    [Fact]
    public void AValleyFloorSuitsWithoutWater()
    {
        // A bowl: 100 in the middle, rising to 112 at the edges, gentle under the disc.
        var patch = Patch((x, z) =>
        {
            int d = Math.Max(Math.Abs(x - 16), Math.Abs(z - 16));
            return d <= 12 ? 100 + d / 6 : 100 + (d - 10) * 4;
        });
        Assert.NotNull(PlacerSite.FindCentre(patch, Field, 16, 16));
    }

    [Fact]
    public void SteepGroundDoesNot()
    {
        var patch = Patch((x, _) => 100 + x, water: (x, _) => x == 0);
        Assert.Null(PlacerSite.FindCentre(patch, Field, 16, 16));
    }

    [Fact]
    public void ALakeDoesNot()
    {
        var patch = Patch((_, _) => 90, water: (x, _) => x < 28);
        Assert.Null(PlacerSite.FindCentre(patch, Field, 16, 16));
    }

    [Fact]
    public void BareRockDoesNot()
    {
        var patch = Patch((_, _) => 110, water: (x, _) => x >= 29, loose: (_, _) => false);
        Assert.Null(PlacerSite.FindCentre(patch, Field, 16, 16));
    }

    [Fact]
    public void OnlyThePartNearWaterIsTaken()
    {
        // Water along the west edge; the spot is east. The nearest suiting centre is pulled west.
        var patch = Patch((_, _) => 110, water: (x, _) => x == 0);
        var centre = PlacerSite.FindCentre(patch, Field, 20, 16);
        Assert.NotNull(centre);
        double reach = Field.Radius + PlacerSite.NearWater;
        Assert.True(centre.Value.X <= reach, $"{centre} is too far from the water");
    }
}

public class PlacerBookTests
{
    private static readonly CellPos Cell = new(4, -7);

    [Fact]
    public void APlacedFieldIsRememberedAndSettlesTheCell()
    {
        var book = new PlacerBook();
        var field = new PlacedField(0, 6100, 112, -10000, "granite", 412);
        Assert.True(book.OnAnchorGenerated(Cell, 0, field));
        Assert.True(book.Get(Cell).Placed);
        Assert.Equal(field, book.FieldIn(Cell));
        Assert.False(book.OnAnchorGenerated(Cell, 0, null));
    }

    [Fact]
    public void UnsuitableSpotsMoveOnThroughAllSixteen()
    {
        var book = new PlacerBook();
        for (int i = 0; i < PlacerCells.SpotCount; i++)
        {
            Assert.Equal(i, book.Get(Cell).Active);
            Assert.True(book.OnAnchorGenerated(Cell, i, null));
        }
        Assert.True(book.Get(Cell).None);
        Assert.Null(book.FieldIn(Cell));
    }

    [Fact]
    public void RoundTripsThroughText()
    {
        var book = new PlacerBook();
        book.OnAnchorGenerated(Cell, 0, null);
        book.OnFallbackGenerated(Cell, 3);
        book.OnAnchorGenerated(Cell, 1, new PlacedField(1, 6100, 112, -10000, "granite", 412));
        book.OnAnchorGenerated(new CellPos(0, 0), 0, null);
        var back = PlacerBook.Parse(book.Serialize());
        Assert.Equal(book.Serialize(), back.Serialize());
        Assert.Equal(1, back.Get(Cell).Active);
        Assert.True(back.Get(Cell).Placed);
        Assert.Equal("granite", back.FieldIn(Cell)!.Rock);
        Assert.Equal(PlacerCells.SpotCount, back.Get(new CellPos(9, 9)).SpotCount);
        Assert.Equal(SpotStatus.Consumed, back.Cells.StatusOf(PlacerCells.Kind, Cell, 3));
    }

    [Fact]
    public void EmptyTextIsAnEmptyBook() => Assert.Empty(PlacerBook.Parse(null).Fields);
}

public class OreWorldRecordPlacerTests
{
    [Fact]
    public void ARecordFromBeforePlacerFieldsHasThemOff()
    {
        var old = "v1\noreCells=True\ncellSize=5000\nnoSurfaceCopper=True\nsmallerDeposits=True\nrarerDistricts=True\n";
        var record = OreWorldRecord.Parse(old);
        Assert.True(record.OreCells);
        Assert.False(record.PlacerFields);
        Assert.Equal(PlacerCells.DefaultCellSize, record.PlacerCellSize);
    }

    [Fact]
    public void PlacerFieldsRoundTripAndTheConfigCanSwitchThemOff()
    {
        var record = OreWorldRecord.AllOff with { PlacerFields = true, PlacerCellSize = 2000 };
        Assert.Equal(record, OreWorldRecord.Parse(record.Serialize()));
        Assert.False(record.Effective(record with { PlacerFields = false }).PlacerFields);
        Assert.True(record.Effective(record).PlacerFields);
    }
}

public class PlacerSiteShoreTests
{
    [Fact]
    public void AShoreSuitsHoweverDeepTheWaterBeside()
    {
        // Dry land at 110 on the east; a lake bed at 90 under the west third.
        const int n = TerrainPatch.Size;
        var h = new int[n * n];
        var w = new bool[n * n];
        var r = new bool[n * n];
        for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                int i = TerrainPatch.Index(x, z);
                w[i] = x < 10;
                h[i] = w[i] ? 90 : 110 + x / 12;
                r[i] = true;
            }
        Assert.NotNull(PlacerSite.FindCentre(new TerrainPatch(h, w, r), new PlacerFieldSpec(600, 1, 0), 16, 16));
    }
}
