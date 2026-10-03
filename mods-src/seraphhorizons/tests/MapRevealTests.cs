using System.IO.Compression;
using SeraphHorizons.Mod.MapReveal.Core;

namespace SeraphHorizons.Mod.MapReveal.Tests;

public class RevealAreaTests
{
    [Fact]
    public void RadiusZeroIsTheCentreColumn() =>
        Assert.Equal([new ColumnPos(10, 10)], RevealArea.Columns(10, 10, 0, 100, 100));

    [Fact]
    public void RadiusOneIsACross()
    {
        var columns = RevealArea.Columns(10, 10, 1, 100, 100);
        Assert.Equal([new(10, 9), new(9, 10), new(10, 10), new(11, 10), new(10, 11)], columns);
    }

    [Fact]
    public void EveryColumnWithinTheRadiusOnceAndNothingElse()
    {
        const int r = 9;
        var columns = RevealArea.Columns(50, 60, r, 1000, 1000);
        Assert.Equal(columns.Count, columns.Distinct().Count());
        for (int x = 50 - r - 1; x <= 50 + r + 1; x++)
        for (int z = 60 - r - 1; z <= 60 + r + 1; z++)
        {
            bool inside = (x - 50) * (x - 50) + (z - 60) * (z - 60) <= r * r;
            Assert.Equal(inside, columns.Contains(new ColumnPos(x, z)));
        }
    }

    [Fact]
    public void ClippedToTheWorld()
    {
        var columns = RevealArea.Columns(1, 1, 5, 4, 3);
        Assert.All(columns, c => Assert.True(c.X is >= 0 and < 4 && c.Z is >= 0 and < 3, c.ToString()));
        Assert.Contains(new ColumnPos(0, 0), columns);
        Assert.Contains(new ColumnPos(3, 2), columns);
    }

    [Fact]
    public void RowByRowSoNorthAndWestNeighboursComeFirst()
    {
        var columns = RevealArea.Columns(20, 20, 7, 100, 100);
        var order = columns.Select((c, i) => (c, i)).ToDictionary(p => p.c, p => p.i);
        foreach (var (c, i) in order)
        {
            if (order.TryGetValue(c with { X = c.X - 1 }, out int west)) Assert.True(west < i);
            if (order.TryGetValue(c with { Z = c.Z - 1 }, out int north)) Assert.True(north < i);
            if (order.TryGetValue(new ColumnPos(c.X - 1, c.Z - 1), out int northWest)) Assert.True(northWest < i);
        }
    }

    [Fact]
    public void TheLargestRadiusIsAboutPiRSquared()
    {
        int count = RevealArea.Columns(300, 300, RevealArea.MaxRadius, 1000, 1000).Count;
        double area = Math.PI * RevealArea.MaxRadius * RevealArea.MaxRadius;
        Assert.InRange(count, area * 0.99, area * 1.01);
    }

    [Fact]
    public void NegativeRadiusIsRejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => RevealArea.Columns(0, 0, -1, 10, 10));
}

public class TerrainShadeTests
{
    [Fact]
    public void LevelGroundIsFlat() => Assert.Equal(1f, TerrainShade.SlopeFactor(0, 0, 0));

    [Fact]
    public void SlopesBrightenUpAndDarkenDown()
    {
        // The game's factors: 1.08 + min(0.5, max/10)/1.25 rising, 0.92 - ... falling.
        Assert.Equal(1.08f + 0.1f / 1.25f, TerrainShade.SlopeFactor(1, 0, 0));
        Assert.Equal(0.92f - 0.3f / 1.25f, TerrainShade.SlopeFactor(-3, -1, 0));
        Assert.Equal(1.08f + 0.5f / 1.25f, TerrainShade.SlopeFactor(20, 20, 20));
        // Signs cancel: level, whatever the size of the steps.
        Assert.Equal(1f, TerrainShade.SlopeFactor(5, -5, 0));
    }

    [Fact]
    public void FlatChunkHasAFlatShadowMap()
    {
        var shadow = TerrainShade.ShadowMap(Fill((ushort)100), NeighbourEdges.From(Fill((ushort)100), Fill((ushort)100), Fill((ushort)100)), All(true));
        Assert.All(shadow, s => Assert.Equal(TerrainShade.Flat, s));
    }

    [Fact]
    public void UnshadedCellsStayFlat()
    {
        var heights = Fill((ushort)100);
        heights[5 * 32 + 5] = 120;
        var shadow = TerrainShade.ShadowMap(heights, NeighbourEdges.None, All(false));
        Assert.All(shadow, s => Assert.Equal(TerrainShade.Flat, s));
    }

    [Fact]
    public void AStepIsBrightenedWhereItRises()
    {
        // x >= 16 is 4 blocks higher: the cells at x = 16 rise from their west neighbours and those
        // at x = 17 sit level, so only the x = 16 column is brightened.
        var heights = new ushort[1024];
        for (int k = 0; k < 1024; k++) heights[k] = (ushort)(k % 32 >= 16 ? 104 : 100);
        var shadow = TerrainShade.ShadowMap(heights, NeighbourEdges.None, All(true));
        byte up = (byte)(128f * TerrainShade.SlopeFactor(4, 4, 0));
        Assert.Equal(up, shadow[10 * 32 + 16]);
        Assert.Equal(TerrainShade.Flat, shadow[10 * 32 + 17]);
        Assert.Equal(TerrainShade.Flat, shadow[10 * 32 + 15]);
    }

    [Fact]
    public void EdgeCellsUseTheNeighbourChunksOrCountAsLevelWithoutThem()
    {
        var heights = Fill((ushort)100);
        var lower = Fill((ushort)90);
        // Without neighbours, the north-west corner cell is level.
        Assert.Equal(TerrainShade.Flat, TerrainShade.ShadowMap(heights, NeighbourEdges.None, All(true))[0]);
        // With lower ground west, north and north-west, it rises from all three.
        var shadow = TerrainShade.ShadowMap(heights, NeighbourEdges.From(lower, lower, lower), All(true));
        Assert.Equal((byte)(128f * TerrainShade.SlopeFactor(10, 10, 10)), shadow[0]);
        // A west edge cell reads (x-1, z-1) and (x-1, z) from the west chunk, (x, z-1) from its own.
        Assert.Equal((byte)(128f * TerrainShade.SlopeFactor(10, 10, 0)), shadow[5 * 32]);
        // A north edge cell reads (x-1, z-1) and (x, z-1) from the north chunk.
        Assert.Equal((byte)(128f * TerrainShade.SlopeFactor(10, 0, 10)), shadow[5]);
        // Only the corner reads the north-west chunk.
        var cornerOnly = TerrainShade.ShadowMap(heights, NeighbourEdges.From(null, null, lower), All(true));
        Assert.Equal((byte)(128f * TerrainShade.SlopeFactor(10, 0, 0)), cornerOnly[0]);
        Assert.Equal(TerrainShade.Flat, cornerOnly[1]);
    }

    [Fact]
    public void BlurMatchesAPlainBoxBlur()
    {
        var random = new Random(7);
        for (int round = 0; round < 20; round++)
        {
            var data = new byte[1024];
            random.NextBytes(data);
            var expected = NaiveBlur(data, 32, 32, 1);
            TerrainShade.Blur(data, 32, 32, 2);
            Assert.Equal(expected, data);
        }
    }

    [Fact]
    public void FlatShadowKeepsTheColour()
    {
        var colors = Enumerable.Range(0, 1024).Select(i => 0x00406080 + i).ToArray();
        var pixels = TerrainShade.Render(colors, Fill(TerrainShade.Flat));
        for (int k = 0; k < 1024; k++) Assert.Equal(colors[k] | unchecked((int)0xFF000000), pixels[k]);
    }

    [Fact]
    public void ShadowDarkensAndLightBrightens()
    {
        var colors = Fill(0x00808080);
        var shadow = Fill(TerrainShade.Flat);
        for (int z = 0; z < 32; z++)
        {
            shadow[z * 32 + 4] = 66;  // the steepest fall
            shadow[z * 32 + 20] = 189; // the steepest rise
        }
        var pixels = TerrainShade.Render(colors, shadow);
        Assert.True((pixels[10 * 32 + 4] & 0xFF) < 0x80);
        Assert.True((pixels[10 * 32 + 20] & 0xFF) > 0x80);
        Assert.Equal(unchecked((int)0xFF808080), pixels[10 * 32 + 12]);
    }

    [Fact]
    public void ColorMultiplyClampsEachChannelAndKeepsAlpha()
    {
        Assert.Equal(unchecked((int)0x12FFFFFF), TerrainShade.ColorMultiply3Clamped(0x12A0A0A0, 2f));
        Assert.Equal(0x00402010, TerrainShade.ColorMultiply3Clamped(0x00804020, 0.5f));
        Assert.Equal(0x00000000, TerrainShade.ColorMultiply3Clamped(0x00804020, -1f));
    }

    // Each cell the mean of the cells within `half` of it along the row (then the column), clipped
    // at the edges, rounded down.
    private static byte[] NaiveBlur(byte[] data, int sizeX, int sizeZ, int half)
    {
        var rows = new byte[data.Length];
        for (int z = 0; z < sizeZ; z++)
        for (int x = 0; x < sizeX; x++)
        {
            int sum = 0, n = 0;
            for (int i = Math.Max(0, x - half); i <= Math.Min(sizeX - 1, x + half); i++) { sum += data[z * sizeX + i]; n++; }
            rows[z * sizeX + x] = (byte)(sum / n);
        }
        var result = new byte[data.Length];
        for (int x = 0; x < sizeX; x++)
        for (int z = 0; z < sizeZ; z++)
        {
            int sum = 0, n = 0;
            for (int i = Math.Max(0, z - half); i <= Math.Min(sizeZ - 1, z + half); i++) { sum += rows[i * sizeX + x]; n++; }
            result[z * sizeX + x] = (byte)(sum / n);
        }
        return result;
    }

    internal static T[] Fill<T>(T value) => Enumerable.Repeat(value, 1024).ToArray();
    internal static bool[] All(bool value) => Fill(value);
}

public class ColumnSamplerTests
{
    // Block ids of a made-up world.
    const int Air = 0, Stone = 1, Water = 2, Snow = 3, Grass = 4;
    static readonly BlockTraits Traits = new(
        IsLake: [false, false, true, false, false],
        IsSnow: [false, false, false, true, false]);

    /// <summary>Columns of stone up to a height, with whatever is put on top.</summary>
    sealed class FakeWorld : ISurfaceSource
    {
        public readonly Dictionary<ColumnPos, ushort[]> HeightMaps = new();
        public readonly Dictionary<(ColumnPos, int, int, int), int> Blocks = new();
        public readonly HashSet<ColumnPos> Incomplete = new();

        public void AddColumn(ColumnPos c, ushort height, int top = Grass)
        {
            HeightMaps[c] = Enumerable.Repeat(height, 1024).ToArray();
            for (int x = 0; x < 32; x++)
            for (int z = 0; z < 32; z++)
                Blocks[(c, x, height, z)] = top;
        }

        public ushort[]? Heights(ColumnPos column) =>
            !Incomplete.Contains(column) && HeightMaps.TryGetValue(column, out var h) ? h : null;

        public bool TryBlock(ColumnPos column, int x, int y, int z, out int blockId)
        {
            blockId = Air;
            if (Heights(column) is not { } h) return false;
            blockId = Blocks.TryGetValue((column, x, y, z), out int id) ? id : y <= h[z * 32 + x] ? Stone : Air;
            return true;
        }
    }

    static readonly ColumnPos C = new(5, 5);

    [Fact]
    public void MissingOrUnfinishedColumnsAreSkipped()
    {
        var world = new FakeWorld();
        Assert.Null(ColumnSampler.Sample(world, C, Traits, 8, false));
        world.AddColumn(C, 100);
        world.Incomplete.Add(C);
        Assert.Null(ColumnSampler.Sample(world, C, Traits, 8, false));
    }

    [Fact]
    public void LandIsItsTopBlockShaded()
    {
        var world = new FakeWorld();
        world.AddColumn(C, 100);
        var sample = ColumnSampler.Sample(world, C, Traits, 8, false)!;
        Assert.Equal((5, 5), (sample.X, sample.Z));
        Assert.All(sample.BlockIds, id => Assert.Equal(Grass, id));
        Assert.All(sample.Kinds, k => Assert.Equal(CellKind.Land, k));
        Assert.All(sample.Shadow, s => Assert.Equal(TerrainShade.Flat, s));
        Assert.Null(sample.Heights);
    }

    [Fact]
    public void SnowShowsTheBlockUnderIt()
    {
        var world = new FakeWorld();
        world.AddColumn(C, 100, top: Snow);
        world.Blocks[(C, 3, 99, 4)] = Grass;
        var sample = ColumnSampler.Sample(world, C, Traits, 8, false)!;
        Assert.Equal(Grass, sample.BlockIds[4 * 32 + 3]);
        Assert.Equal(Stone, sample.BlockIds[0]);
    }

    [Fact]
    public void ColourAccurateKeepsSnowShadesWaterAndSendsHeights()
    {
        var world = new FakeWorld();
        world.AddColumn(C, 100, top: Snow);
        world.Blocks[(C, 0, 100, 0)] = Water;
        var sample = ColumnSampler.Sample(world, C, Traits, 8, colorAccurate: true)!;
        Assert.Equal(Snow, sample.BlockIds[1]);
        Assert.Equal(Water, sample.BlockIds[0]);
        Assert.Equal(CellKind.Land, sample.Kinds[0]);
        Assert.NotNull(sample.Heights);
        Assert.All(sample.Heights!, h => Assert.Equal(100, h));
    }

    [Fact]
    public void WaterIsLakeInsideAndShoreAtItsEdge()
    {
        var world = new FakeWorld();
        world.AddColumn(C, 100);
        for (int x = 10; x <= 12; x++)
        for (int z = 10; z <= 12; z++)
            world.Blocks[(C, x, 100, z)] = Water;
        var sample = ColumnSampler.Sample(world, C, Traits, 8, false)!;
        Assert.Equal(CellKind.Lake, sample.Kinds[11 * 32 + 11]);
        Assert.Equal(CellKind.WaterEdge, sample.Kinds[10 * 32 + 10]);
        Assert.Equal(CellKind.WaterEdge, sample.Kinds[12 * 32 + 11]);
        Assert.Equal(Water, sample.BlockIds[11 * 32 + 11]);
        // Water is never shaded.
        Assert.Equal(TerrainShade.Flat, sample.Shadow[10 * 32 + 10]);
    }

    [Fact]
    public void ShoreAcrossAChunkEdgeLooksAtTheNeighbourColumn()
    {
        var world = new FakeWorld();
        var east = C with { X = C.X + 1 };
        world.AddColumn(C, 100, top: Water);
        // Without the east column, the east edge can't be told apart from lake.
        Assert.Equal(CellKind.Lake, ColumnSampler.Sample(world, C, Traits, 8, false)!.Kinds[5 * 32 + 31]);
        // With land to the east, it is shore; with water, lake.
        world.AddColumn(east, 100, top: Grass);
        Assert.Equal(CellKind.WaterEdge, ColumnSampler.Sample(world, C, Traits, 8, false)!.Kinds[5 * 32 + 31]);
        world.AddColumn(east, 100, top: Water);
        Assert.Equal(CellKind.Lake, ColumnSampler.Sample(world, C, Traits, 8, false)!.Kinds[5 * 32 + 31]);
        // An unfinished neighbour counts as missing.
        world.AddColumn(east, 100, top: Grass);
        world.Incomplete.Add(east);
        Assert.Equal(CellKind.Lake, ColumnSampler.Sample(world, C, Traits, 8, false)!.Kinds[5 * 32 + 31]);
    }

    [Fact]
    public void EdgesAreShadedFromTheNeighbourColumns()
    {
        var world = new FakeWorld();
        world.AddColumn(C, 100);
        Assert.Equal(TerrainShade.Flat, ColumnSampler.Sample(world, C, Traits, 8, false)!.Shadow[0]);
        world.AddColumn(C with { X = C.X - 1 }, 95);
        world.AddColumn(C with { Z = C.Z - 1 }, 95);
        world.AddColumn(new ColumnPos(C.X - 1, C.Z - 1), 95);
        var sample = ColumnSampler.Sample(world, C, Traits, 8, false)!;
        Assert.Equal((byte)(128f * TerrainShade.SlopeFactor(5, 5, 5)), sample.Shadow[0]);
        Assert.Equal(TerrainShade.Flat, sample.Shadow[5 * 32 + 5]);
    }

    [Fact]
    public void HeightsAboveTheWorldAreLeftBlank()
    {
        var world = new FakeWorld();
        world.AddColumn(C, 100);
        world.HeightMaps[C][7] = 256;
        var sample = ColumnSampler.Sample(world, C, Traits, 8, false)!;
        Assert.Equal(CellKind.None, sample.Kinds[7]);
        Assert.Equal(TerrainShade.Flat, sample.Shadow[7]);
    }
}

public class RevealCodecTests
{
    [Fact]
    public void RoundTrips()
    {
        var random = new Random(3);
        var columns = new List<ColumnSample>
        {
            RandomSample(random, 3, 4, distinctBlocks: 12, heights: false),
            RandomSample(random, 0, 70000, distinctBlocks: 600, heights: true), // wide palette
            RandomSample(random, int.MaxValue, 1, distinctBlocks: 1, heights: false),
        };
        var decoded = RevealCodec.Decode(RevealCodec.Encode(columns));
        Assert.Equal(columns.Count, decoded.Count);
        for (int i = 0; i < columns.Count; i++)
        {
            Assert.Equal(columns[i].Pos, decoded[i].Pos);
            Assert.Equal(columns[i].BlockIds, decoded[i].BlockIds);
            Assert.Equal(columns[i].Kinds, decoded[i].Kinds);
            Assert.Equal(columns[i].Shadow, decoded[i].Shadow);
            Assert.Equal(columns[i].Heights, decoded[i].Heights);
        }
    }

    [Fact]
    public void AnEmptyBatchRoundTrips() => Assert.Empty(RevealCodec.Decode(RevealCodec.Encode([])));

    [Fact]
    public void TypicalColumnsAreSmall()
    {
        // A grassy hillside: patches of a few blocks, a slope here and there.
        var random = new Random(5);
        var columns = Enumerable.Range(0, 64).Select(i =>
        {
            var s = new ColumnSample { X = i, Z = 0 };
            int seed = random.Next(8);
            for (int k = 0; k < 1024; k++)
            {
                int x = k % 32, z = k / 32;
                s.BlockIds[k] = 4000 + (x / 6 + z / 5 + seed) % 4;
                s.Shadow[k] = (byte)((x + 2 * z + seed) % 9 == 0 ? 138 : (x * z + seed) % 13 == 0 ? 118 : 128);
            }
            return s;
        }).ToList();
        Assert.InRange(RevealCodec.Encode(columns).Length, 1, 64 * 512);
    }

    [Fact]
    public void GarbageIsRejected()
    {
        Assert.Throws<InvalidDataException>(() => RevealCodec.Decode(Deflate([9, 1])));
        var good = RevealCodec.Encode([RandomSample(new Random(1), 1, 1, 5, false)]);
        var raw = Inflate(good);
        Assert.Throws<InvalidDataException>(() => RevealCodec.Decode(Deflate(raw[..^10])));
    }

    static ColumnSample RandomSample(Random random, int x, int z, int distinctBlocks, bool heights)
    {
        var s = new ColumnSample { X = x, Z = z, Heights = heights ? new ushort[1024] : null };
        for (int k = 0; k < 1024; k++)
        {
            s.BlockIds[k] = k < distinctBlocks ? 100 + k : 100 + random.Next(distinctBlocks);
            s.Kinds[k] = (CellKind)random.Next(4);
            s.Shadow[k] = (byte)random.Next(256);
            if (s.Heights is { } h) h[k] = (ushort)random.Next(ushort.MaxValue + 1);
        }
        return s;
    }

    static byte[] Deflate(byte[] raw)
    {
        using var buffer = new MemoryStream();
        using (var d = new DeflateStream(buffer, CompressionLevel.Fastest, leaveOpen: true)) d.Write(raw);
        return buffer.ToArray();
    }

    static byte[] Inflate(byte[] data)
    {
        using var d = new DeflateStream(new MemoryStream(data), CompressionMode.Decompress);
        using var o = new MemoryStream();
        d.CopyTo(o);
        return o.ToArray();
    }
}
