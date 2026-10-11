using SeraphHorizons.Mod.Eidolon.Core;

namespace SeraphHorizons.Mod.Tests.Eidolon;

/// <summary>Mining a covered area as a tunnel: what is covered, what is refused, the open face and the
/// nearest of them, the face's order (top half, bottom half, a step in), the heading over what is
/// quarried after it, and the headings members work side by side.</summary>
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

    private static (TunnelFit Fit, MineTunnel? Tunnel) Plan(Voxels world, MarkArea area, double fromX = -6, double fromZ = 0) =>
        EidolonTunnels.Plan(area, EidolonTunnels.Covered(area, world.Has), world, world.Has, fromX, fromZ);

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
        // Out on the ground, a pit two deep: nothing over it, nothing planned, nothing refused.
        Assert.Equal((TunnelFit.Fits, (MineTunnel?)null), Plan(world, Area(-10, -3, -2, -1, -5, 5)));
    }

    // ---- refused ----

    [Fact]
    public void ACoveredPartUnder4HighOr2WideIsRefused()
    {
        var world = Hill();
        Assert.Equal(TunnelFit.TooLow, Plan(world, Area(0, 7, 0, 2, 0, 1)).Fit);
        // One wide: its face opens onto the ground, but it is too narrow to drive.
        Assert.Equal(TunnelFit.TooNarrow, Plan(world, Area(0, 7, 0, 3, 0, 0)).Fit);
        // 4 high and 2 wide is enough.
        var (fit, tunnel) = Plan(world, Area(0, 7, 0, 3, 0, 1));
        Assert.Equal(TunnelFit.Fits, fit);
        Assert.Equal((2, 8), (tunnel!.Width, tunnel.Length));
    }

    [Fact]
    public void ACoveredPartWithNoOpenFaceIsRefused()
    {
        // Wholly inside the hill, rock on every side.
        Assert.Equal(TunnelFit.NoFace, Plan(Hill(), Area(3, 10, 0, 3, 0, 1)).Fit);
        // Its heading two up the cliff, over the ground at its foot: from the ground it reaches only the
        // heading's bottom half, so there is no face to start from either.
        Assert.Equal(TunnelFit.NoFace, Plan(Hill(), Area(0, 7, 2, 5, 0, 1)).Fit);
    }

    // ---- the open face ----

    [Fact]
    public void ItStartsFromTheOpenFaceNearestTheEidolon()
    {
        // The hill alone is open to the west; driven in from there, along +x.
        var (_, west) = Plan(Hill(), Area(0, 7, 0, 3, 0, 1), fromX: 20);
        Assert.Equal((1, 0), (west!.DriveX, west.DriveZ));
        // A ridge open both ends: from the end nearer the eidolon.
        var ridge = Hill(hillEnd: 7);
        var (_, fromWest) = Plan(ridge, Area(0, 7, 0, 3, 0, 1), fromX: -6);
        Assert.Equal((1, 0), (fromWest!.DriveX, fromWest.DriveZ));
        var (_, fromEast) = Plan(ridge, Area(0, 7, 0, 3, 0, 1), fromX: 14);
        Assert.Equal((-1, 0), (fromEast!.DriveX, fromEast.DriveZ));
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
        while (EidolonTunnels.Front(tunnel, tunnel.Bounds, left.Contains, -1, 1) is { } group)
        {
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
        var first = EidolonTunnels.Front(tunnel, tunnel.Bounds, left.Contains, -1, 1)!;
        Assert.All(first.Cells, c => Assert.Equal((0, true), (c.X, c.Y <= 1)));
    }

    [Fact]
    public void ItStandsInTheTunnelToStrikeItsFaceNeverOverIt()
    {
        var world = Hill();
        var area = Area(0, 7, 0, 3, 0, 1);
        var tunnel = EidolonTunnels.Make(area, EidolonTunnels.Covered(area, world.Has), 1, 0);
        // The first three slices dug.
        world.Solid.ExceptWith(Box(0, 2, 0, 3, 0, 1));
        var left = Box(3, 7, 0, 3, 0, 1).Select(c => new MarkPos(c.Item1, c.Item2, c.Item3)).ToHashSet();
        var face = EidolonTunnels.Front(tunnel, tunnel.Bounds, left.Contains, 1, 1)!;
        Assert.All(face.Cells, c => Assert.Equal(3, c.X));
        var stands = EidolonMining.Stands(world, face.Cells, left.Contains, 1, 0, 1);
        Assert.NotEmpty(stands);
        // On the tunnel's floor, in the 2-wide way, its box clear of the face's columns.
        var s = stands[0];
        Assert.Equal((0, 1), (s.Y, s.Z));
        Assert.InRange(s.X, 1, 2);
        Assert.All(face.Cells, c => Assert.False(EidolonMining.UnderFoot(s.X, s.Z, c)));
        Assert.True(world.Free(s.X, s.Y, s.Z));
    }

    // ---- benching ----

    [Fact]
    public void ATallCoveredPartIsDrivenAlongItsTop4LayersAndWorkedDownFromItsFloor()
    {
        var world = Hill();
        var area = Area(0, 7, 0, 5, 0, 1);
        // Six high: its heading the top four (2 to 5), its face reached from a ledge two up the cliff.
        world.Solid.UnionWith(Box(-4, -1, 0, 1, -6, 6));
        var (fit, tunnel) = Plan(world, area);
        Assert.Equal(TunnelFit.Fits, fit);
        Assert.Equal((5, 2), (tunnel!.Top, tunnel.Bottom));
        // While it is driven, a search gathers the whole heading apart, and the layer it would quarry is the
        // one under it.
        MineRule Rule(MarkPos c) => world.Has(c) ? MineRule.Mine : MineRule.Air;
        var scan = EidolonMining.Scan(area, (_, _) => true, Rule, _ => false, null, tunnel);
        Assert.Equal(2 * 8 * 4, scan.Tunnel.Count);
        Assert.All(scan.Tunnel, c => Assert.True(tunnel.Contains(c)));
        Assert.Equal(1, scan.Layer);
        Assert.False(scan.Done);
        // Driven: what is under it is quarried a layer at a time from its floor, which it reaches from there.
        world.Solid.ExceptWith(Box(0, 7, 2, 5, 0, 1));
        var after = EidolonMining.Scan(area, (_, _) => true, Rule, _ => false, tunnel.Contains);
        Assert.Equal(1, after.Layer);
        Assert.Empty(after.Tunnel);
        Assert.True(EidolonMining.InReach(4, tunnel.Bottom, 1, new MarkPos(5, 1, 0)));
        Assert.Equal(EidolonTunnels.HeadingHeight, EidolonMining.ReachAbove + 1);
    }

    [Fact]
    public void ALowerHeadingIsDrivenFromItsOwnFaceAtItsLevelAfterTheOneOverIt()
    {
        // Six high (layers 0 to 5): the top heading 2 to 5, the next 0 to 1. A ledge two high in front of the
        // cliff (x -6 to -3) for the top heading's face; the floor at the cliff's foot (x -2, -1) for the
        // lower one's.
        var world = Hill();
        world.Solid.UnionWith(Box(-6, -3, 0, 1, -6, 6));
        var area = Area(0, 7, 0, 5, 0, 1);
        var (fit, top) = Plan(world, area);
        Assert.Equal(TunnelFit.Fits, fit);
        Assert.Equal((5, 2), (top!.Top, top.Bottom));
        var lower = top.Below(area.Min.Y)!;
        Assert.Equal((1, 0), (lower.Top, lower.Bottom));
        Assert.Equal((top.DriveX, top.DriveZ, top.Bounds), (lower.DriveX, lower.DriveZ, lower.Bounds));
        Assert.Null(lower.Below(area.Min.Y));
        // Each opens on the same side at its own level: the top from the ledge, the lower from the floor.
        Assert.True(EidolonTunnels.Opens(top, world, world.Has));
        Assert.True(EidolonTunnels.Opens(lower, world, world.Has));
        var cells = EidolonTunnels.LaneFront(lower, lower.Lanes[0], world.Has)!.Value.Group.Cells;
        var stand = EidolonMining.Stands(world, cells, world.Has, -4, 0, 1).First(s => EidolonTunnels.InFront(lower, s));
        Assert.Equal(0, stand.Y);
        // The ledge running right up to the cliff covers the lower mouth: no face of its own at that level.
        var ledged = Hill();
        ledged.Solid.UnionWith(Box(-6, -1, 0, 1, -6, 6));
        Assert.True(EidolonTunnels.Opens(top, ledged, ledged.Has));
        Assert.False(EidolonTunnels.Opens(lower, ledged, ledged.Has));
        // Below, the lower heading's cells only, the top's over it left to the search's pass.
        var dug = new Voxels(world.Solid.Except(Box(0, 7, 2, 5, 0, 1)));
        MineRule Rule(MarkPos c) => dug.Has(c) ? MineRule.Mine : MineRule.Air;
        var scan = EidolonMining.Scan(area, (_, _) => true, Rule, _ => false, top.Above, lower);
        // (here only layer 1: layer 0 is the world's mantle, never searched, EidolonMining.BottomY)
        Assert.Equal(2 * 8, scan.Tunnel.Count);
        Assert.All(scan.Tunnel, c => Assert.Equal(1, c.Y));
        Assert.True(top.Above(new MarkPos(0, 6, 0)) && lower.Above(new MarkPos(0, 2, 0)) && !lower.Above(new MarkPos(-1, 2, 0)));
    }

    // ---- headings ----

    [Fact]
    public void ACoveredAreaIsSplitInto2WideHeadingsSideBySideOneAMember()
    {
        var area = Area(0, 7, 0, 3, -3, 2);
        var tunnel = EidolonTunnels.Make(area, EidolonTunnels.Covered(area, Hill().Has), 1, 0);
        Assert.Equal(3, tunnel.Lanes.Count);
        Assert.All(tunnel.Lanes, l => Assert.Equal((2, 8), (l.SizeZ, l.SizeX)));
        // one member: all of it; two: two lanes and one; three: one each; more: the rest get none
        Assert.Equal(new[] { tunnel.Bounds }, EidolonTunnels.Headings(tunnel, 1));
        Assert.Equal(new[] { new MineSector(0, -3, 7, 0), new MineSector(0, 1, 7, 2) }, EidolonTunnels.Headings(tunnel, 2));
        Assert.Equal(tunnel.Lanes, EidolonTunnels.Headings(tunnel, 3));
        Assert.Equal(3, EidolonTunnels.Headings(tunnel, 5).Count);
        Assert.Empty(EidolonTunnels.Headings(tunnel, 0));
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

    [Fact]
    public void MembersGetAHeadingEachAndNeverStealOne()
    {
        var area = Area(0, 11, 0, 3, 0, 3);
        var tunnel = EidolonTunnels.Make(area, EidolonTunnels.Covered(area, Hill().Has), 1, 0);
        var sectors = new MineSectors();
        var at = new Dictionary<long, (double X, double Z)?> { [1] = (-2, 0.5), [2] = (-2, 3.5), [3] = (-4, 2) };
        Assert.True(sectors.Sync(area, [1, 2, 3], id => at[id], tunnel));
        // two lanes: one each, nearest; the third waits
        Assert.Equal(new MineSector(0, 0, 11, 1), sectors.Of(1));
        Assert.Equal(new MineSector(0, 2, 11, 3), sectors.Of(2));
        Assert.Null(sectors.Of(3));
        // the second's heading has work: the first, with none left in its own, does not steal it
        Assert.False(sectors.Take(1, s => s == sectors.Of(2) ? 40 : 0, id => at[id], steal: false));
        // the tunnel done, the area is split afresh as a pit's
        Assert.True(sectors.Sync(area, [1, 2, 3], id => at[id]));
        Assert.NotNull(sectors.Of(3));
    }
}
