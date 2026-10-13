using SeraphHorizons.Mod.Eidolon.Core;

namespace SeraphHorizons.Mod.Tests.Eidolon;

/// <summary>A mining job's sectors: strips for two or three, a grid of near-square blocks for more,
/// each at least 4 wide and cut at the squares' edges, tiling the area; members past what fits get none;
/// each given the sector nearest it; split afresh only when who is present changes; a member with nothing
/// left takes a sector let go with work in it, or the far half of the busiest other's.</summary>
public class MineSectorsTests
{
    private static MarkArea Area(int sizeX, int sizeZ, int x0 = 100, int z0 = -50) =>
        MarkArea.Between(new MarkPos(x0, 10, z0), new MarkPos(x0 + sizeX - 1, 12, z0 + sizeZ - 1));

    private static Func<long, (double X, double Z)?> At(params (long Id, double X, double Z)[] at) =>
        id => at.Where(a => a.Id == id).Select(a => ((double X, double Z)?)(a.X, a.Z)).FirstOrDefault();

    // ---- the split ----

    [Fact]
    public void TwoOrThreeAreStripsAcrossTheLongSide()
    {
        var area = Area(16, 12, 0, 0);
        Assert.Equal([new MineSector(0, 0, 7, 11), new MineSector(8, 0, 15, 11)], EidolonSectors.Split(area, 2));
        // 8 squares among three: 2, 3 and 3
        Assert.Equal([new MineSector(0, 0, 3, 11), new MineSector(4, 0, 9, 11), new MineSector(10, 0, 15, 11)], EidolonSectors.Split(area, 3));
        // longer along z: cut along z
        Assert.Equal([new MineSector(0, 0, 7, 9), new MineSector(0, 10, 7, 19)], EidolonSectors.Split(Area(8, 20, 0, 0), 2));
        Assert.Equal([new MineSector(0, 0, 15, 11)], EidolonSectors.Split(area, 1));
        Assert.Empty(EidolonSectors.Split(area, 0));
    }

    [Fact]
    public void FourOrMoreAreAGridOfNearSquareBlocks()
    {
        var area = Area(16, 12, 0, 0);
        Assert.Equal([new MineSector(0, 0, 7, 5), new MineSector(8, 0, 15, 5), new MineSector(0, 6, 7, 11), new MineSector(8, 6, 15, 11)],
            EidolonSectors.Split(area, 4));
        // five: two rows, three and two, none longer than half again its width
        var five = EidolonSectors.Split(area, 5);
        Assert.Equal(5, five.Count);
        Assert.Equal(2, five.Select(s => s.MinZ).Distinct().Count());
        Assert.All(five, s => Assert.True(Math.Max(s.SizeX, s.SizeZ) <= 1.5 * Math.Min(s.SizeX, s.SizeZ), s.ToString()));
        // a long thin area holds only strips: four across 16 × 3
        Assert.Equal(4, EidolonSectors.Split(Area(16, 3, 0, 0), 4).Count);
        Assert.All(EidolonSectors.Split(Area(16, 3, 0, 0), 4), s => Assert.Equal((4, 3), (s.SizeX, s.SizeZ)));
    }

    [Fact]
    public void SectorsTileTheAreaAtLeastFourWideCutAtTheSquaresEdges()
    {
        for (int sx = 1; sx <= 21; sx++)
        for (int sz = 1; sz <= 21; sz += 2)
        for (int n = 1; n <= 9; n++)
        {
            var area = Area(sx, sz);
            var sectors = EidolonSectors.Split(area, n);
            Assert.InRange(sectors.Count, 1, n);
            var seen = new HashSet<(int, int)>();
            foreach (var s in sectors)
            {
                Assert.True(s.SizeX >= Math.Min(EidolonSectors.MinSide, sx) && s.SizeZ >= Math.Min(EidolonSectors.MinSide, sz),
                    $"{sx} × {sz} for {n}: {s}");
                Assert.Equal(0, (s.MinX - area.Min.X) % 2);
                Assert.Equal(0, (s.MinZ - area.Min.Z) % 2);
                for (int x = s.MinX; x <= s.MaxX; x++)
                for (int z = s.MinZ; z <= s.MaxZ; z++)
                    Assert.True(seen.Add((x, z)), $"{sx} × {sz} for {n}: ({x}, {z}) twice");
            }
            Assert.Equal(sx * sz, seen.Count);
            // as many as fit: one a member up to what the area holds at 4 wide
            if (n <= EidolonSectors.StripsUpTo)
                Assert.Equal(Math.Min(n, EidolonSectors.MostParts(Math.Max(sx, sz))), sectors.Count);
        }
    }

    [Fact]
    public void ASectorHalvesAcrossItsLongSideTheNearHalfItsMembers()
    {
        var sector = new MineSector(10, 0, 15, 11);
        var (near, far) = EidolonSectors.Halves(sector, 12, 2)!.Value;
        Assert.Equal(new MineSector(10, 0, 15, 5), near);
        Assert.Equal(new MineSector(10, 6, 15, 11), far);
        // its member at the other end
        Assert.Equal(new MineSector(10, 6, 15, 11), EidolonSectors.Halves(sector, 12, 10)!.Value.Near);
        // 9 long: 4 and 5, at a square's edge
        var odd = EidolonSectors.Halves(new MineSector(0, 0, 8, 3), 0, 0)!.Value;
        Assert.Equal((new MineSector(0, 0, 3, 3), new MineSector(4, 0, 8, 3)), (odd.Near, odd.Far));
        // under 8: no halves of 4
        Assert.Null(EidolonSectors.Halves(new MineSector(0, 0, 6, 6), 0, 0));
    }

    // ---- who works which ----

    [Fact]
    public void EachIsGivenTheSectorNearestItAndThoseThatDoNotFitNone()
    {
        var area = Area(8, 3, 0, 0);
        var sectors = new MineSectors();
        // two strips of 4 fit 8 × 3: the third member gets none and waits
        Assert.True(sectors.Sync(area, [1, 2, 3], At((1, 7, 1), (2, 5, 1), (3, 0, 1))));
        Assert.Equal(new MineSector(4, 0, 7, 2), sectors.Of(1));
        Assert.Equal(new MineSector(0, 0, 3, 2), sectors.Of(3));
        Assert.Null(sectors.Of(2));
        Assert.Equal(3L, sectors.OwnerAt(1, 1));
    }

    [Fact]
    public void ItIsSplitAfreshOnlyWhenWhoIsPresentChanges()
    {
        var area = Area(16, 12, 0, 0);
        var at = At((1, 0, 0), (2, 16, 0), (3, 8, 6));
        var sectors = new MineSectors();
        Assert.True(sectors.Sync(area, [1, 2], at));
        int version = sectors.Version;
        // the same members, in any order: no new split
        Assert.False(sectors.Sync(area, [2, 1], at));
        Assert.Equal(version, sectors.Version);
        Assert.Equal(new MineSector(0, 0, 7, 11), sectors.Of(1));
        // one joins: three strips
        Assert.True(sectors.Sync(area, [1, 2, 3], at));
        Assert.Equal(new MineSector(4, 0, 9, 11), sectors.Of(3));
        Assert.Equal(new MineSector(10, 0, 15, 11), sectors.Of(2));
        // one is away (or leaves): two again
        Assert.True(sectors.Sync(area, [1, 3], at));
        Assert.Equal(2, sectors.All.Count);
        Assert.Null(sectors.Of(2));
        Assert.True(sectors.Version > version);
    }

    // ---- stealing ----

    [Fact]
    public void OneWithNothingLeftStealsTheFarHalfOfTheBusiest()
    {
        var area = Area(16, 12, 0, 0);
        var at = At((1, 1, 6), (2, 6, 6), (3, 13, 2));
        var sectors = new MineSectors();
        sectors.Sync(area, [1, 2, 3], at);
        // work left: 10 blocks in 2's sector, 30 in 3's, half of them away from 3
        var work = new List<(int X, int Z)>();
        work.AddRange(Enumerable.Range(0, 10).Select(i => (5, i)));
        work.AddRange(Enumerable.Range(0, 15).Select(i => (11, i % 12)));
        work.AddRange(Enumerable.Range(0, 15).Select(i => (14, 6 + i % 6)));
        int Work(MineSector s) => work.Count(c => s.Contains(c.X, c.Z));
        Assert.True(sectors.Take(1, Work, at));
        Assert.Equal(1, sectors.Steals);
        Assert.Equal(new MineSector(10, 6, 15, 11), sectors.Of(1));
        Assert.Equal(new MineSector(10, 0, 15, 5), sectors.Of(3));
        // its own, mined out, let go
        Assert.Contains((new MineSector(0, 0, 3, 11), (long?)null), sectors.All);
        Assert.Null(sectors.OwnerAt(0, 0));
    }

    [Fact]
    public void ASectorTooSmallOrWhoseFarHalfHasNothingIsNotStolen()
    {
        var area = Area(16, 12, 0, 0);
        var at = At((1, 1, 6), (2, 6, 1), (3, 13, 9));
        var sectors = new MineSectors();
        sectors.Sync(area, [1, 2, 3], at);
        // 2's sector has the most, all in the half by 2; of 3's, one block is in the half away from it
        var work = new List<(int X, int Z)> { (5, 0), (5, 1), (6, 2), (7, 3), (4, 4), (12, 6), (12, 7), (12, 8), (12, 0) };
        int Work(MineSector s) => work.Count(c => s.Contains(c.X, c.Z));
        Assert.True(sectors.Take(1, Work, at));
        // 2's far half has nothing: 3's far half is taken
        Assert.Equal(new MineSector(10, 0, 15, 5), sectors.Of(1));
        // a sector under 8 long is not halved: nothing to take, nothing changes
        var small = new MineSectors();
        small.Sync(Area(8, 6, 0, 0), [1, 2], At((1, 0, 0), (2, 8, 0)));
        int version = small.Version;
        Assert.False(small.Take(1, s => s.Contains(5, 3) ? 9 : 0, At((1, 0, 0), (2, 8, 0))));
        Assert.Equal(version, small.Version);
        Assert.Equal(new MineSector(0, 0, 3, 5), small.Of(1));
    }

    [Fact]
    public void ASectorLetGoWithWorkInItIsTakenBeforeAnyIsStolen()
    {
        var area = Area(16, 12, 0, 0);
        var at = At((1, 1, 6), (2, 13, 6));
        var sectors = new MineSectors();
        sectors.Sync(area, [1, 2], at);
        // 1 can work nothing in its own now (its blocks under the job's lowest layer): it lets it go
        sectors.LetGo(1);
        Assert.Null(sectors.Of(1));
        // later, with work there it may do (the pit came down), 2, out of work in its own, takes it rather
        // than steal; then 1, with no sector, finds 2's let go mined out, and steals half of its own back
        Assert.True(sectors.Take(2, s => s.MinX == 0 ? 12 : 0, at));
        Assert.Equal(new MineSector(0, 0, 7, 11), sectors.Of(2));
        Assert.Equal(0, sectors.Steals);
        Assert.True(sectors.Take(1, s => s.MinX == 0 ? 12 : 0, at));
        Assert.Equal(1, sectors.Steals);
        Assert.NotNull(sectors.Of(1));
        Assert.NotNull(sectors.Of(2));
    }
}
