using SeraphHorizons.Mod.Eidolon.Core;

namespace SeraphHorizons.Mod.Tests.Eidolon;

/// <summary>The eidolon's pathfinder on a voxel grid of full blocks, with the entity's own box
/// (1.7 wide, 3.75 tall, its feet's centre at the node).</summary>
public class WidePathTests
{
    private sealed class Voxels : IWideSpace
    {
        public readonly HashSet<(int, int, int)> Solid = [];
        public readonly HashSet<(int, int, int)> Lava = [];
        public double HalfWidth = 0.85, Height = 3.75;

        public Voxels Floor(int x0, int x1, int z0, int z1, int y = -1)
        {
            for (int x = x0; x <= x1; x++)
            for (int z = z0; z <= z1; z++)
                Solid.Add((x, y, z));
            return this;
        }

        public Voxels Wall(int x, int z0, int z1, int y0, int y1)
        {
            for (int z = z0; z <= z1; z++)
            for (int y = y0; y <= y1; y++)
                Solid.Add((x, y, z));
            return this;
        }

        public bool Free(double x, double y, double z)
        {
            const double eps = 1e-6;
            for (int bx = (int)Math.Floor(x - HalfWidth); bx <= (int)Math.Floor(x + HalfWidth - eps); bx++)
            for (int by = (int)Math.Floor(y); by <= (int)Math.Floor(y + Height - eps); by++)
            for (int bz = (int)Math.Floor(z - HalfWidth); bz <= (int)Math.Floor(z + HalfWidth - eps); bz++)
                if (Solid.Contains((bx, by, bz)))
                    return false;
            return true;
        }

        // The four blocks under the box's footprint, at its feet.
        public float Cost(PathCell c) =>
            Lava.Contains((c.X - 1, c.Y, c.Z - 1)) || Lava.Contains((c.X - 1, c.Y, c.Z))
            || Lava.Contains((c.X, c.Y, c.Z - 1)) || Lava.Contains((c.X, c.Y, c.Z))
                ? WidePath.Impassable + 1 : 0;
    }

    /// <summary>A wall along x = 10 across the whole floor, <paramref name="height"/> high, with a gap of
    /// <paramref name="gapWidth"/> blocks starting at z 0 that is <paramref name="gapHeight"/> high.</summary>
    private static Voxels WallWithGap(int gapWidth, int gapHeight, int height = 8)
    {
        var v = new Voxels().Floor(-5, 25, -12, 12);
        for (int z = -12; z <= 12; z++)
        for (int y = 0; y < height; y++)
        {
            bool inGap = z >= 0 && z < gapWidth && y < gapHeight;
            if (!inGap)
                v.Solid.Add((10, y, z));
        }
        return v;
    }

    private static List<PathCell>? Through(Voxels v) =>
        new WidePath(v, maxFall: 3).Find(new PathCell(0, 0, 1), new PathCell(20, 0, 1), maxNodes: 20000);

    [Fact]
    public void WalksThroughATwoWideFourHighGate()
    {
        var path = Through(WallWithGap(2, 4));
        Assert.NotNull(path);
        // Through the gate: its feet's centre on the corner between the gap's two blocks.
        Assert.Contains(path!, c => c.X == 10 && c.Z == 1);
    }

    [Fact]
    public void ANarrowerGateIsNoWay() => Assert.Null(Through(WallWithGap(1, 4)));

    [Fact]
    public void ALowerArchIsNoWay() => Assert.Null(Through(WallWithGap(2, 3)));

    [Fact]
    public void TheGamesBlockCentredNodesWouldNotFitTheGate()
    {
        // The box centred on a block's middle (as the game's AStar puts it) spans three blocks.
        var v = WallWithGap(2, 4);
        Assert.False(v.Free(10.5, 0, 0.5));
        Assert.False(v.Free(10.5, 0, 1.5));
        Assert.True(v.Free(10, 0, 1));
    }

    [Fact]
    public void StepsUpOneBlockButNotTwo()
    {
        var v = new Voxels().Floor(-5, 15, -5, 5);
        for (int x = 5; x <= 15; x++)
        for (int z = -5; z <= 5; z++)
            v.Solid.Add((x, 0, z));
        var path = new WidePath(v, 3).Find(new PathCell(0, 0, 0), new PathCell(10, 1, 0), 20000);
        Assert.NotNull(path);
        Assert.Equal(1, path![^1].Y);

        for (int x = 5; x <= 15; x++)
        for (int z = -5; z <= 5; z++)
            v.Solid.Add((x, 1, z));
        Assert.Null(new WidePath(v, 3).Find(new PathCell(0, 0, 0), new PathCell(10, 2, 0), 20000));
    }

    [Fact]
    public void WalksDownADropWithinItsFallButNotOffACliff()
    {
        var v = new Voxels().Floor(-5, 4, -5, 5, y: 4).Floor(5, 15, -5, 5, y: 1);
        var path = new WidePath(v, maxFall: 3).Find(new PathCell(0, 5, 0), new PathCell(10, 2, 0), 20000);
        Assert.NotNull(path);
        Assert.Equal(2, path![^1].Y);
        Assert.Null(new WidePath(v, maxFall: 2).Find(new PathCell(0, 5, 0), new PathCell(10, 2, 0), 20000));
    }

    [Fact]
    public void DoesNotCutACornerDiagonally()
    {
        // A post where neither end of the diagonal step touches it but its middle does.
        var v = new Voxels().Floor(-5, 5, -5, 5);
        v.Wall(-1, 1, 1, 0, 5);
        var walker = new WidePath(v, 3);
        Assert.True(v.Free(0, 0, 0));
        Assert.True(v.Free(1, 0, 1));
        Assert.Null(walker.Step(new PathCell(0, 0, 0), 1, 1, out _));
        Assert.NotNull(walker.Step(new PathCell(0, 0, 0), 1, 0, out _));
    }

    [Fact]
    public void GoesRoundLava()
    {
        var v = new Voxels().Floor(-5, 15, -8, 8);
        for (int z = -3; z <= 3; z++)
            v.Lava.Add((5, 0, z));
        var path = new WidePath(v, 3).Find(new PathCell(0, 0, 0), new PathCell(10, 0, 0), 20000);
        Assert.NotNull(path);
        Assert.DoesNotContain(path!, c => v.Cost(c) > WidePath.Impassable);
    }

    [Fact]
    public void GivesUpPastItsNodeBudget()
    {
        var v = new Voxels().Floor(-50, 50, -50, 50);
        var walker = new WidePath(v, 3);
        Assert.Null(walker.Find(new PathCell(0, 0, 0), new PathCell(40, 0, 40), maxNodes: 10));
        Assert.True(walker.Visited > 10);
    }

    [Fact]
    public void StopsWithinTolerance()
    {
        var v = new Voxels().Floor(-5, 15, -5, 5);
        var path = new WidePath(v, 3).Find(new PathCell(0, 0, 0), new PathCell(10, 0, 0), 20000, tolerance: 3);
        Assert.NotNull(path);
        Assert.Equal(7, path![^1].X);
    }

    [Theory]
    [InlineData(0.4, 0.0, 0.6, 0, 0, 1)]
    [InlineData(-0.6, 63.5, 2.49, -1, 63, 2)]
    [InlineData(10.5, 1.999, -3.5, 11, 2, -4)]
    public void TheNodeIsTheNearestCorner(double x, double y, double z, int nx, int ny, int nz) =>
        Assert.Equal(new PathCell(nx, ny, nz), WidePath.NodeAt(x, y, z));
}
