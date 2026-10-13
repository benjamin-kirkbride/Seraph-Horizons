using SeraphHorizons.Mod.Eidolon.Core;
using static SeraphHorizons.Mod.Tests.Eidolon.MineTestWorld;

namespace SeraphHorizons.Mod.Tests.Eidolon;

/// <summary>Dispatch: earliest arrival over the plan's executable front, a station left to the worker that
/// reaches it sooner, one worker a lane at a face, claims ringed in the open and kept to the lane in a
/// tunnel; and the bands that say what may be dug now.</summary>
public class MineDispatchTests
{
    // Every case is lifted L blocks: the world's bottom layer is its mantle, never mined (EidolonMining.BottomY).
    private const int L = 10;

    // A strip of ground 32 long (x), 4 wide (z), one layer: station A at its east end, B at its west end,
    // A first in plan order; each stands on the ground north of the strip.
    private static (MinePlan Plan, MineState Live) Strip()
    {
        var world = new MineTestWorld().Ground(L - 1);
        var area = Area(0, 31, L - 1, L - 1, 0, 3);
        var live = new MineState(world.Site(area), new PathCell(16, L, 10));
        var targets = Cells(0, 31, L - 1, L - 1, 0, 3).ToHashSet();
        live.BuildNow(targets);
        var a = new MineStation(0, new PathCell(30, L, 6), [new MarkPos(29, L - 1, 3), new MarkPos(30, L - 1, 3)], 0, MineStationKind.Bench);
        var b = new MineStation(1, new PathCell(2, L, 6), [new MarkPos(1, L - 1, 3), new MarkPos(2, L - 1, 3)], 0, MineStationKind.Bench);
        var plan = new MinePlan
        {
            Stations = [a, b], OutOfReach = new HashSet<MarkPos>(), Held = new HashSet<MarkPos>(), WayOut = new HashSet<MarkPos>(),
            Left = new HashSet<MarkPos>(), Stair = new HashSet<MarkPos>(), Area = area, Targets = targets,
        };
        live.Bands = plan.Bands(live.Remaining);
        return (plan, live);
    }

    private static MineWorker At(long id, double x, double z, params MineStation[] queue) => new(id, x, L, z, queue, 0);

    [Fact]
    public void ItTakesTheStationItGetsToFirstOverOneEarlierInPlanOrder()
    {
        var (plan, live) = Strip();
        var next = MineDispatch.Next(plan, live, At(1, 3, 8), [], _ => true);
        Assert.Equal(1, next!.Id);
        Assert.True(MineDispatch.Arrival(At(1, 3, 8), plan.Stations[1]) < MineDispatch.Arrival(At(1, 3, 8), plan.Stations[0]));
        // one it holds already is not taken again; the next goes from where that one leaves it
        Assert.Equal(0, MineDispatch.Next(plan, live, At(1, 3, 8, plan.Stations[1]), [], _ => true)!.Id);
    }

    [Fact]
    public void AStationAnotherReachesSoonerIsLeftToIt()
    {
        var (plan, live) = Strip();
        // nearer A than B, but the other is nearer A still: it goes for B
        var me = At(1, 20, 8);
        Assert.True(MineDispatch.Arrival(me, plan.Stations[0]) < MineDispatch.Arrival(me, plan.Stations[1]));
        Assert.Equal(0, MineDispatch.Next(plan, live, me, [], _ => true)!.Id);
        Assert.Equal(1, MineDispatch.Next(plan, live, me, [At(2, 31, 8)], _ => true)!.Id);
        // and none that the board says is claimed
        Assert.Null(MineDispatch.Next(plan, live, me, [At(2, 31, 8, plan.Stations[0])], s => s.Id != 1));
    }

    [Fact]
    public void ItTakesNoStationThatWouldDigUnderAnother()
    {
        var (plan, live) = Strip();
        // another stands on station B's cells
        var other = new MineWorker(2, 2, L, 3, [], 0);
        Assert.False(MineDispatch.Executable(plan, live, plan.Stations[1], [new MineWorkerAt(2, 2, L, 3, [])], out var why));
        Assert.Equal(MineRefusal.WorkerFloor, why);
        Assert.Equal(0, MineDispatch.Next(plan, live, At(1, 3, 8), [other], _ => true)!.Id);
    }

    [Fact]
    public void ATunnelTwoWideAdmitsOneAtItsFaceAndFourWideTwo()
    {
        foreach (int width in new[] { 2, 4 })
        {
            var world = new MineTestWorld().Ground(L - 1).Hill(0, 12, L + 9, -3, 6, L).Box(-2, -1, L, L + 1, -3, 6);
            var area = Area(0, 7, L, L + 5, 0, width - 1);
            var plan = world.Plan(area, new PathCell(-6, L, 0));
            Assert.Equal(width / 2, MineDispatch.FaceCap(plan.Tunnel!));
            var live = new MineState(world.Site(area), new PathCell(-6, L, 0));
            live.AdoptNow(plan);
            var board = new JobBoard<(int X, int Z), MineJobCells>();
            var first = MineDispatch.Next(plan, live, At(1, -6, 0), [], _ => true)!;
            Assert.Equal(MineStationKind.Face, first.Kind);
            Assert.True(board.TryClaim(1, first.Id, MineDispatch.Claim(first, plan.Tunnel), 0));
            var second = MineDispatch.Next(plan, live, At(2, -6, 2), [At(1, first.Stand.X, first.Stand.Z, first)],
                s => MineDispatch.Claim(s, plan.Tunnel).All(u => board.Free(2, u, 0)));
            if (width == 2)
                Assert.Null(second);
            else
            {
                Assert.Equal(MineStationKind.Face, second!.Kind);
                Assert.NotEqual(first.Lane, second.Lane);
            }
        }
    }

    [Fact]
    public void AClaimIsRingedInTheOpenAndKeptToItsLaneInATunnel()
    {
        var bench = new MineStation(0, new PathCell(3, L, 3), [new MarkPos(5, L - 1, 3)], 0, MineStationKind.Bench);
        var claim = MineDispatch.Claim(bench, null).ToHashSet();
        // its box's columns (2..3, 2..3) and the cell's, a column round them
        Assert.Equal(Cells(1, 4, 0, 0, 1, 4).Select(c => (c.X, c.Z)).Concat(Cells(4, 6, 0, 0, 2, 4).Select(c => (c.X, c.Z))).ToHashSet(), claim);
        // a face station in lane 0 (z 0..1) of a tunnel 4 wide driven along x
        var area = Area(0, 7, L, L + 3, 0, 3);
        var covered = Cells(0, 7, 0, 0, 0, 3).Select(c => (c.X, c.Z)).ToHashSet();
        var tunnel = EidolonTunnels.Make(area, covered, 1, 0);
        var face = new MineStation(1, new PathCell(-1, L, 1), [new MarkPos(0, L + 3, 0), new MarkPos(1, L + 3, 1)], 0, MineStationKind.Face, 0, 0);
        var faceClaim = MineDispatch.Claim(face, tunnel).ToHashSet();
        Assert.All(faceClaim, c => Assert.InRange(c.Z, 0, 1));
        Assert.Contains((2, 0), faceClaim);
        Assert.Contains((-3, 1), faceClaim);
    }

    // ---- bands ----

    [Fact]
    public void TheOpenBandGoesALayerUnderTheHighestLeftAndStairsLast()
    {
        var area = Area(0, 3, L - 3, L - 1, 0, 3);
        var stair = new HashSet<MarkPos> { new(0, L - 3, 0) };
        var cells = Cells(0, 3, L - 3, L - 1, 0, 3).ToList();
        var bands = new MineBands(area, null, stair, cells);
        Assert.True(bands.Eligible(new MarkPos(1, L - 1, 1)));
        Assert.True(bands.Eligible(new MarkPos(1, L - 2, 1)));
        Assert.False(bands.Eligible(new MarkPos(1, L - 3, 1)));
        Assert.False(bands.Eligible(new MarkPos(0, L - 3, 0)));
        foreach (var c in cells.Where(c => c.Y == L - 1))
            bands.Remove(c);
        Assert.True(bands.Eligible(new MarkPos(1, L - 3, 1)));
        Assert.Contains(new MarkPos(1, L - 3, 1), bands.TakeWidened());
        foreach (var c in cells.Where(c => !stair.Contains(c)))
            bands.Remove(c);
        Assert.True(bands.Eligible(new MarkPos(0, L - 3, 0)));
        Assert.Equal(MineBands.Stair, bands.Band(new MarkPos(0, L - 3, 0)));
    }

    [Fact]
    public void TheOpenPartIsNeverDugUnderTheFloorOfTheBandBeingDriven()
    {
        // eight high: covered x 0..7, its band 0 y 4..7 and band 1 y 0..3; the open part x -2..-1 from y 0
        var area = Area(-2, 7, L, L + 7, 0, 1);
        var covered = Cells(0, 7, 0, 0, 0, 1).Select(c => (c.X, c.Z)).ToHashSet();
        var tunnel = EidolonTunnels.Make(area, covered, 1, 0);
        var cells = Cells(-2, 7, L, L + 3, 0, 1).Concat(Cells(0, 7, L + 4, L + 7, 0, 1)).ToList();
        var bands = new MineBands(area, tunnel, new HashSet<MarkPos>(), cells);
        Assert.Equal(0, bands.Band(new MarkPos(0, L + 7, 0)));
        Assert.Equal(1, bands.Band(new MarkPos(0, L + 3, 0)));
        Assert.Equal(2, bands.Band(new MarkPos(-1, L + 3, 0)));
        Assert.Equal(0, bands.Covered);
        Assert.Equal(L + 4, bands.Bottom(0));
        // band 0 left: the open part's top (y 3) is under its floor, so not yet
        Assert.False(bands.Eligible(new MarkPos(-1, L + 3, 0)));
        Assert.False(bands.Eligible(new MarkPos(0, L + 3, 0)));
        foreach (var c in cells.Where(c => c.Y >= L + 4))
            bands.Remove(c);
        Assert.Equal(1, bands.Covered);
        Assert.True(bands.Eligible(new MarkPos(-1, L + 3, 0)));
        Assert.True(bands.Eligible(new MarkPos(-1, L + 2, 0)));
        Assert.False(bands.Eligible(new MarkPos(-1, L + 1, 0)));
        Assert.True(bands.Eligible(new MarkPos(0, L, 0)));
    }
}
