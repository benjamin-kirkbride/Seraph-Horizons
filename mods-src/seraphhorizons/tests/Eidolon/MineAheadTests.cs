using SeraphHorizons.Mod.Eidolon.Core;
using static SeraphHorizons.Mod.Tests.Eidolon.MineTestWorld;

namespace SeraphHorizons.Mod.Tests.Eidolon;

/// <summary>Work ahead of the band (<see cref="MineAhead"/>; README "Eidolon", working together, *Spare
/// work*): one band deeper at most and under nothing left, never under the driven heading's floor, the bench
/// behind a face, and the guard keeping an earlier station's stand.</summary>
public class MineAheadTests
{
    // Every case is lifted L blocks: the world's bottom layer is its mantle, never mined (EidolonMining.BottomY).
    private const int L = 10;

    private const int Top = L - 1;

    private static readonly PathCell Home = new(-6, L, 2);

    // an open pit 8 x 4, five layers deep, its west half (x 0..3) dug two layers down, its east half not;
    // a stair cell in the west half
    private static readonly MarkPos StairCell = new(0, Top - 2, 0);

    private static MineState Pit()
    {
        var world = new MineTestWorld().Ground(L - 1);
        var area = Area(0, 7, Top - 4, Top, 0, 3);
        var state = new MineState(world.Site(area), Home);
        state.BuildNow(Cells(0, 7, Top - 4, Top, 0, 3).ToHashSet());
        state.Apply(Cells(0, 3, Top - 1, Top, 0, 3).ToList());
        state.Bands = new MineBands(area, null, new HashSet<MarkPos> { StairCell }, state.Remaining, widen: false);
        return state;
    }

    [Fact]
    public void Ahead_cells_are_one_band_deeper_and_under_nothing_left()
    {
        var state = Pit();
        var b = state.Bands!;
        Assert.Equal(Top, b.Layer);
        Assert.Equal(Top - 1, b.OpenFloor);
        // the band being dug: eligible, not ahead
        Assert.True(b.Eligible(new MarkPos(5, Top, 1)));
        Assert.False(MineAhead.Eligible(state, new MarkPos(5, Top, 1)));
        // the west half's next two layers are ahead (the lower under the upper, ahead with it); the one under them is not
        foreach (int y in new[] { Top - 2, Top - 3 })
            Assert.All(Cells(1, 3, y, y, 0, 3), c => Assert.True(MineAhead.Eligible(state, c), $"{c} is not ahead"));
        Assert.True(MineAhead.Eligible(state, new MarkPos(0, Top - 2, 1)));
        Assert.All(Cells(0, 3, Top - 4, Top - 4, 0, 3), c => Assert.False(MineAhead.Eligible(state, c), $"{c} is ahead"));
        // nothing under a cell still to dig
        Assert.All(Cells(4, 7, Top - 3, Top - 2, 0, 3), c => Assert.False(MineAhead.Eligible(state, c), $"{c} is ahead"));
        // nor the stair, nor what is under it
        Assert.False(MineAhead.Eligible(state, StairCell));
        Assert.False(MineAhead.Eligible(state, StairCell with { Y = Top - 3 }));
    }

    // a tunnel 8 high into a stepped slope (MinePlannerTests): covered bands 0 (y 4..7) and 1 (y 0..3), the
    // step in front of it the open band
    private static (MineState State, MinePlan Plan) Tunnel()
    {
        var c = MineCases.TallTunnel(2);
        var world = c.World();
        var plan = c.Plan(world);
        var state = new MineState(world.Site(c.Area), c.Home);
        state.BuildNow(plan.Targets);
        state.Bands = plan.Bands(state.Remaining);
        return (state, plan);
    }

    [Fact]
    public void Ahead_never_goes_under_the_floor_of_the_band_being_driven()
    {
        var (state, plan) = Tunnel();
        var b = state.Bands!;
        Assert.Equal(0, b.Covered);
        var open = state.Remaining.Where(c => b.Band(c) == b.OpenBand).ToList();
        Assert.NotEmpty(open);
        Assert.All(open, c => Assert.False(c.Y < b.Bottom(0) && MineAhead.Eligible(state, c), $"{c} under the heading's floor is ahead"));
        Assert.Contains(open, c => c.Y < b.Bottom(0));
    }

    [Fact]
    public void The_bench_behind_a_face_is_ahead_where_the_heading_over_it_is_dug()
    {
        var (state, plan) = Tunnel();
        var b = state.Bands!;
        // the heading (band 0) dug over the first half of the tunnel, x 0..3
        state.Apply(state.Remaining.Where(c => b.Band(c) == 0 && c.X <= 3).ToList());
        Assert.Equal(0, b.Covered);
        var bench = state.Remaining.Where(c => b.Band(c) == 1).ToList();
        Assert.Contains(bench, c => c.X <= 3);
        Assert.All(bench.Where(c => c.X <= 3), c => Assert.True(MineAhead.Eligible(state, c), $"{c} is not ahead"));
        Assert.All(bench.Where(c => c.X >= 4), c => Assert.False(MineAhead.Eligible(state, c), $"{c} is ahead"));
        // the ground in front (the open band) is under the heading's floor: not ahead either
        Assert.All(state.Remaining.Where(c => b.Band(c) == b.OpenBand), c => Assert.False(MineAhead.Eligible(state, c)));
    }

    [Fact]
    public void An_ahead_station_that_would_take_an_earlier_stand_is_refused()
    {
        var state = Pit();
        // station 0 of the band being dug, worked from the west half's floor; station 1 ahead, under that stand
        var stand = new PathCell(2, Top - 1, 2);
        Assert.True(state.Site.Standable(stand));
        Assert.True(state.Walk.Out(stand));
        var first = new MineStation(0, stand, [new MarkPos(4, Top, 1), new MarkPos(4, Top, 2)], 0, MineStationKind.Bench);
        var ahead = new MineStation(1, new PathCell(2, Top - 1, 3), [new MarkPos(1, Top - 2, 0), new MarkPos(1, Top - 2, 1), new MarkPos(2, Top - 2, 1)],
            0, MineStationKind.Bench);
        var plan = new MinePlan
        {
            Stations = [first, ahead], OutOfReach = new HashSet<MarkPos>(), Held = new HashSet<MarkPos>(), WayOut = new HashSet<MarkPos>(),
            Left = new HashSet<MarkPos>(), Stair = new HashSet<MarkPos>(), Area = state.Site.Area,
            Targets = first.Cells.Concat(ahead.Cells).ToHashSet(),
        };
        Assert.All(ahead.Cells, c => Assert.True(MineAhead.Eligible(state, c)));
        Assert.Empty(MineDispatch.Cells(state, ahead));
        Assert.NotEmpty(MineDispatch.Cells(state, ahead, true));
        Assert.False(MineDispatch.Executable(plan, state, ahead, [], out var why, null, ahead: true));
        Assert.Equal(MineRefusal.CutsOffStation, why);
        // with the first one done, its stand no longer kept, the ahead one may go
        state.Apply(first.Cells);
        Assert.True(MineDispatch.Executable(plan, state, ahead, [], out why, null, ahead: true), $"{why}");
    }
}
