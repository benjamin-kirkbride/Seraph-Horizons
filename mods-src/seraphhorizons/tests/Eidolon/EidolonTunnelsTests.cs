using SeraphHorizons.Mod.Eidolon.Core;

namespace SeraphHorizons.Mod.Tests.Eidolon;

/// <summary>Mining a covered area as a tunnel: what is covered, what is refused as it is marked (only an
/// area covered all over that the eidolon does not fit in), the way in (the face it reaches, then the one
/// nearest home), the face's order (top half, bottom half, a step in), the bands of a tall one, and its
/// lanes.</summary>
public class EidolonTunnelsTests
{
    private sealed class Voxels(IEnumerable<(int, int, int)> solid) : IWideSpace
    {
        public readonly HashSet<(int, int, int)> Solid = solid.ToHashSet();

        public bool Free(double x, double y, double z)
        {
            const double eps = 1e-6;
            for (int bx = (int)Math.Floor(x - 0.85); bx <= (int)Math.Floor(x + 0.85 - eps); bx++)
            for (int by = (int)Math.Floor(y); by <= (int)Math.Floor(y + 3.75 - eps); by++)
            for (int bz = (int)Math.Floor(z - 0.85); bz <= (int)Math.Floor(z + 0.85 - eps); bz++)
                if (Solid.Contains((bx, by, bz)))
                    return false;
            return true;
        }

        public float Cost(PathCell cell) => 0;

        public bool Has(MarkPos c) => Solid.Contains((c.X, c.Y, c.Z));
    }

    private static IEnumerable<(int, int, int)> Box(int x0, int x1, int y0, int y1, int z0, int z1)
    {
        for (int x = x0; x <= x1; x++)
        for (int y = y0; y <= y1; y++)
        for (int z = z0; z <= z1; z++)
            yield return (x, y, z);
    }

    private static MarkArea Area(int x0, int x1, int y0, int y1, int z0, int z1) =>
        MarkArea.Between(new MarkPos(x0, y0, z0), new MarkPos(x1, y1, z1));

    // Flat ground at y -1, and a hill of stone from x 0 to hillEnd, y 0 to 7, z -6 to 6, its west face
    // a cliff onto the ground.
    private static Voxels Hill(int hillEnd = 15) =>
        new(Box(-12, 24, -1, -1, -12, 12).Concat(Box(0, hillEnd, 0, 7, -6, 6)));

    private static TunnelFit Fit(Voxels world, MarkArea area) => EidolonTunnels.Fit(area, EidolonTunnels.Covered(area, world.Has));

    /// <summary>The covered part's shape (<see cref="EidolonTunnels.Shape"/>) with the reach bound over the
    /// area's blocks, ordered from (<paramref name="homeX"/>, 0, 0); all of it lifted 10 blocks for the
    /// planner, which never digs the world's bottom layer (<see cref="EidolonMining.BottomY"/>).</summary>
    private static MineTunnel? Shape(Voxels world, MarkArea area, int homeX = -6)
    {
        const int lift = 10;
        var voxels = new MineTestWorld();
        voxels.Solid.UnionWith(world.Solid.Select(c => (c.Item1, c.Item2 + lift, c.Item3)));
        var lifted = new MarkArea(area.Min with { Y = area.Min.Y + lift }, area.Max with { Y = area.Max.Y + lift });
        var site = voxels.Site(lifted);
        var home = new PathCell(homeX, lift, 0);
        var state = new MineState(site, home);
        state.BuildNow(EidolonMining.Targets(lifted, site, _ => false).Dig);
        return EidolonTunnels.Shape(lifted, EidolonTunnels.Covered(lifted, voxels.Has), state.Reach, home);
    }

    // ---- covered or open ----

    [Fact]
    public void AColumnIsCoveredWithGroundWithinAFewBlocksOverTheAreasTop()
    {
        // An area from out on the ground (x -4 to -1, open to the sky) into the hill (x 0 to 7, the hill
        // over it): the two parts, per column.
        var world = Hill();
        var area = Area(-4, 7, 0, 3, 0, 1);
        var covered = EidolonTunnels.Covered(area, world.Has);
        Assert.Equal(16, covered.Count);
        Assert.All(covered, c => Assert.InRange(c.X, 0, 7));
        // Ground further up than RoofRise roofs nothing: an overhang high over a pit is open.
        var high = new Voxels(Box(0, 3, 4 + EidolonTunnels.RoofRise, 4 + EidolonTunnels.RoofRise, 0, 3));
        Assert.Empty(EidolonTunnels.Covered(Area(0, 3, 0, 3, 0, 3), high.Has));
        var low = new Voxels(Box(0, 3, 3 + EidolonTunnels.RoofRise, 3 + EidolonTunnels.RoofRise, 0, 3));
        Assert.Equal(16, EidolonTunnels.Covered(Area(0, 3, 0, 3, 0, 3), low.Has).Count);
    }

    [Fact]
    public void AnAreaWithNothingCoveredIsNoTunnel()
    {
        var world = Hill();
        // Out on the ground, a pit two deep: nothing over it, no shape, nothing refused.
        var pit = Area(-10, -3, -2, -1, -5, 5);
        Assert.Equal(TunnelFit.Fits, Fit(world, pit));
        Assert.Null(Shape(world, pit));
    }

    // ---- refused ----

    [Fact]
    public void AnAreaCoveredAllOverUnder4HighOr2WideIsRefused()
    {
        var world = Hill();
        // 3 high, inside the hill all over: the eidolon does not fit in it.
        Assert.Equal(TunnelFit.TooLow, Fit(world, Area(0, 7, 0, 2, 0, 1)));
        // One wide: too narrow.
        Assert.Equal(TunnelFit.TooNarrow, Fit(world, Area(0, 7, 0, 3, 0, 0)));
        // 4 high and 2 wide is enough.
        Assert.Equal(TunnelFit.Fits, Fit(world, Area(0, 7, 0, 3, 0, 1)));
    }

    [Fact]
    public void AnAreaCoveredAllOverWithNoOpenFaceIsNotRefusedButShaped()
    {
        // Wholly inside the hill, rock on every side, and two up the cliff: it fits, so it is planned, and
        // what it cannot reach is the planner's to report (MinePlannerTests, the flat face).
        Assert.Equal(TunnelFit.Fits, Fit(Hill(), Area(3, 10, 0, 3, 0, 1)));
        Assert.Equal(TunnelFit.Fits, Fit(Hill(), Area(0, 7, 2, 5, 0, 1)));
        // Shaped all the same: the way in nearest home.
        var tunnel = Shape(Hill(), Area(3, 10, 0, 3, 0, 1))!;
        Assert.Equal((1, 0), (tunnel.DriveX, tunnel.DriveZ));
    }

    [Fact]
    public void ACoveredPartTooLowOrNarrowIsNotRefusedWithTheRestOpen()
    {
        var world = Hill();
        // A pit two deep in the ground on the slope's foot, its uphill columns (x 0, 1) under the hill.
        var pit = Area(-4, 1, -2, -1, 0, 3);
        Assert.Equal(8, EidolonTunnels.Covered(pit, world.Has).Count);
        Assert.Equal(TunnelFit.Fits, Fit(world, pit));
        // 4 high, its covered part 1 wide.
        Assert.Equal(TunnelFit.Fits, Fit(world, Area(-4, 7, 0, 3, 0, 0)));
    }

    // ---- the way in ----

    [Fact]
    public void ItDrivesInFromTheFaceItReachesThenTheOneNearestHome()
    {
        // The hill alone is open to the west: driven in from there, along +x, even ordered from the east.
        var west = Shape(Hill(), Area(0, 7, 0, 3, 0, 1), homeX: 20)!;
        Assert.Equal((1, 0), (west.DriveX, west.DriveZ));
        // A ridge open both ends: from the end nearer home.
        var ridge = Hill(hillEnd: 7);
        var fromWest = Shape(ridge, Area(0, 7, 0, 3, 0, 1), homeX: -6)!;
        Assert.Equal((1, 0), (fromWest.DriveX, fromWest.DriveZ));
        var fromEast = Shape(ridge, Area(0, 7, 0, 3, 0, 1), homeX: 14)!;
        Assert.Equal((-1, 0), (fromEast.DriveX, fromEast.DriveZ));
        Assert.Equal(0, fromEast.Slice(7, 0));
        Assert.Equal(7, fromEast.Slice(0, 0));
    }

    // ---- the face ----

    [Fact]
    public void TheFaceGoesTopHalfThenBottomHalfThenAStepIn()
    {
        var tunnel = EidolonTunnels.Make(Area(0, 7, 0, 3, 0, 1), EidolonTunnels.Covered(Area(0, 7, 0, 3, 0, 1), Hill().Has), 1, 0);
        var left = Box(0, 7, 0, 3, 0, 1).Select(c => new MarkPos(c.Item1, c.Item2, c.Item3)).ToHashSet();
        var order = new List<MineGroup>();
        while (EidolonTunnels.LaneFront(tunnel, tunnel.Lanes[0], left.Contains) is { } front)
        {
            var group = front.Group;
            order.Add(group);
            left.ExceptWith(group.Cells);
        }
        // Two strikes a slice, eight slices, every cell once.
        Assert.Equal(16, order.Count);
        Assert.Empty(left);
        for (int i = 0; i < order.Count; i++)
        {
            var g = order[i];
            Assert.True(g.Upright);
            Assert.Equal(4, g.Cells.Count);
            // slice by slice from the mouth, both columns across
            Assert.All(g.Cells, c => Assert.Equal(i / 2, c.X));
            Assert.Equal(new[] { 0, 1 }, g.Cells.Select(c => c.Z).Distinct().Order());
            // top half (3 and 2), then the bottom half (1 and 0)
            Assert.Equal(i % 2 == 0 ? new[] { 2, 3 } : new[] { 0, 1 }, g.Cells.Select(c => c.Y).Distinct().Order());
        }
    }

    [Fact]
    public void ALaneWithItsTopHalfLeftInPlaceGoesOnToTheBottomHalf()
    {
        var area = Area(0, 7, 0, 3, 0, 1);
        var tunnel = EidolonTunnels.Make(area, EidolonTunnels.Covered(area, Hill().Has), 1, 0);
        // The first slice's top half is not to mine (a chest, say): its bottom half is next, then slice 1.
        var left = Box(0, 7, 0, 3, 0, 1).Select(c => new MarkPos(c.Item1, c.Item2, c.Item3)).Where(c => !(c.X == 0 && c.Y >= 2)).ToHashSet();
        var first = EidolonTunnels.LaneFront(tunnel, tunnel.Lanes[0], left.Contains)!.Value.Group;
        Assert.All(first.Cells, c => Assert.Equal((0, true), (c.X, c.Y <= 1)));
    }

    // ---- bands ----

    [Fact]
    public void ATallCoveredPartIsCutIntoBandsAReachHighFromItsTop()
    {
        // Six high (layers 0 to 5): its first band the top four (2 to 5), the next 0 to 1, the same way in.
        var area = Area(0, 7, 0, 5, 0, 1);
        var top = EidolonTunnels.Make(area, EidolonTunnels.Covered(area, Hill().Has), 1, 0);
        Assert.Equal((5, 2), (top.Top, top.Bottom));
        var lower = top.Below(area.Min.Y)!;
        Assert.Equal((1, 0), (lower.Top, lower.Bottom));
        Assert.Equal((top.DriveX, top.DriveZ, top.Bounds), (lower.DriveX, lower.DriveZ, lower.Bounds));
        Assert.Null(lower.Below(area.Min.Y));
        Assert.True(top.Above(new MarkPos(0, 6, 0)) && lower.Above(new MarkPos(0, 2, 0)) && !lower.Above(new MarkPos(-1, 2, 0)));
        // A band is what it reaches from its floor.
        Assert.True(EidolonMining.InReach(4, top.Bottom, 1, new MarkPos(5, top.Top, 0)));
        Assert.Equal(EidolonTunnels.HeadingHeight, EidolonMining.ReachAbove + 1);
    }

    // ---- lanes ----

    [Fact]
    public void ACoveredAreaIsLaidIn2WideLanesSideBySide()
    {
        var area = Area(0, 7, 0, 3, -3, 2);
        var tunnel = EidolonTunnels.Make(area, EidolonTunnels.Covered(area, Hill().Has), 1, 0);
        Assert.Equal(3, tunnel.Lanes.Count);
        Assert.All(tunnel.Lanes, l => Assert.Equal((2, 8), (l.SizeZ, l.SizeX)));
        Assert.Equal(new[] { new MineSector(0, -3, 7, -2), new MineSector(0, -1, 7, 0), new MineSector(0, 1, 7, 2) }, tunnel.Lanes);
        // An odd width's last lane is 3 wide, struck two across and then one.
        var odd = EidolonTunnels.Make(Area(0, 7, 0, 3, 0, 4), EidolonTunnels.Covered(Area(0, 7, 0, 3, 0, 4), Hill().Has), 1, 0);
        Assert.Equal(new[] { 2, 3 }, odd.Lanes.Select(l => l.SizeZ));
        var left = Box(0, 7, 0, 3, 2, 4).Select(c => new MarkPos(c.Item1, c.Item2, c.Item3)).ToHashSet();
        var g = EidolonTunnels.LaneFront(odd, odd.Lanes[1], left.Contains)!.Value.Group;
        Assert.Equal(new[] { 2, 3 }, g.Cells.Select(c => c.Z).Distinct().Order());
        left.ExceptWith(g.Cells);
        g = EidolonTunnels.LaneFront(odd, odd.Lanes[1], left.Contains)!.Value.Group;
        Assert.Equal(new[] { 4 }, g.Cells.Select(c => c.Z).Distinct());
    }
}
