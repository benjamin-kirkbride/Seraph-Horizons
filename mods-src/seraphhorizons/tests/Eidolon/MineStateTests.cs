using SeraphHorizons.Mod.Eidolon.Core;
using static SeraphHorizons.Mod.Tests.Eidolon.MineTestWorld;

namespace SeraphHorizons.Mod.Tests.Eidolon;

/// <summary>The guard (<see cref="MineState.Check"/>): each refusal from a small case, a tried dig undone
/// to the last bit, the walk graph kept up as cells are dug, and a world change taken in.</summary>
public class MineStateTests
{
    // Every case is lifted L blocks: the world's bottom layer is its mantle, never mined (EidolonMining.BottomY).
    private const int L = 10;

    private static readonly PathCell Home = new(-6, L, 4);

    private static MineState State(MineTestWorld world, MarkArea area, PathCell? home = null)
    {
        var state = new MineState(world.Site(area), home ?? Home);
        state.BuildNow(EidolonMining.Targets(area, state.Site, _ => false).Dig);
        return state;
    }

    // A pit 10 x 10, 3 deep, dug out but for a pillar 2 x 2 (x 4..5, z 4..5) and a bridge one block thick at
    // its top (x 6..9, z 4..5) to the ground east of the pit: on the pillar it is out over the bridge.
    private static (MineTestWorld World, MarkArea Area) Pillar()
    {
        var world = new MineTestWorld().Ground(L - 1);
        var area = Area(0, 9, L - 3, L - 1, 0, 9);
        foreach (var c in Cells(0, 9, L - 3, L - 1, 0, 9))
            if (!(c.X is >= 4 and <= 5 && c.Z is >= 4 and <= 5) && !(c.X >= 6 && c.Z is >= 4 and <= 5 && c.Y == L - 1))
                world.Dig(c);
        return (world, area);
    }

    private static readonly PathCell OnPillar = new(5, L, 5);

    [Fact]
    public void ItRefusesAStandItCannotStandAtOrNotOut()
    {
        var (world, area) = Pillar();
        var state = State(world, area);
        // in the ground, and in the air
        Assert.Equal(MineRefusal.NotStandable, state.Check(new PathCell(-4, L - 1, 0), [], []));
        Assert.Equal(MineRefusal.NotStandable, state.Check(new PathCell(5, L + 2, 5), [], []));
        // the pit's floor (3 under the ground all round, no way up)
        Assert.True(state.Site.Standable(new PathCell(2, L - 3, 2)));
        Assert.Equal(MineRefusal.NotOut, state.Check(new PathCell(2, L - 3, 2), [], []));
        Assert.Equal(MineRefusal.None, state.Check(OnPillar, [new MarkPos(6, L - 1, 4)], []));
    }

    [Fact]
    public void ItRefusesACellOutOfReachOrNotLeft()
    {
        var (world, area) = Pillar();
        var state = State(world, area);
        Assert.Equal(MineRefusal.OutOfReach, state.Check(OnPillar, [new MarkPos(9, L - 1, 4)], []));
        // under itself
        Assert.Equal(MineRefusal.OutOfReach, state.Check(OnPillar, [new MarkPos(5, L - 1, 5)], []));
        // already dug
        Assert.Equal(MineRefusal.OutOfReach, state.Check(OnPillar, [new MarkPos(3, L - 1, 3)], []));
    }

    [Fact]
    public void ItRefusesTheDigThatLeavesItOnAPillar()
    {
        var (world, area) = Pillar();
        var state = State(world, area);
        Assert.True(state.Walk.Out(OnPillar));
        var bridge = Cells(6, 7, L - 1, L - 1, 4, 5).ToList();
        var blame = new HashSet<MarkPos>();
        Assert.Equal(MineRefusal.CutsOff, state.Check(OnPillar, bridge, [], blame));
        // what it stands on to get off the pillar is to blame
        Assert.NotEmpty(blame);
        Assert.Subset(bridge.ToHashSet(), blame);
        // another eidolon on the pillar is cut off by the same dig from the far end of the bridge
        var far = new PathCell(9, L, 5);
        var other = new MineWorkerAt(2, OnPillar.X, OnPillar.Y, OnPillar.Z, []);
        Assert.Equal(MineRefusal.CutsOffWorker, state.Check(far, bridge, [other]));
        // the station of the plan's to come is kept the same way
        Assert.Equal(MineRefusal.CutsOffStation, state.Check(far, bridge, [], null, [OnPillar]));
        Assert.Equal(MineRefusal.None, state.Check(far, bridge, []));
    }

    [Fact]
    public void ItRefusesDiggingUnderOrOverAnotherEidolon()
    {
        var world = new MineTestWorld().Ground(L - 1);
        var area = Area(0, 9, L - 2, L - 1, 0, 9);
        var state = State(world, area);
        var stand = new PathCell(3, L, 3);
        var other = new MineWorkerAt(2, 5, L, 3, []);
        Assert.Equal(MineRefusal.WorkerFloor, state.Check(stand, [new MarkPos(5, L - 1, 3)], [other]));
        var under = new MineWorkerAt(2, 5, L - 4, 3, []);
        Assert.Equal(MineRefusal.OverWorker, state.Check(stand, [new MarkPos(5, L - 1, 3)], [under]));
        Assert.Equal(MineRefusal.None, state.Check(stand, [new MarkPos(5, L - 1, 3)], []));
    }

    [Fact]
    public void ItRefusesTheDigThatTakesATargetsLastStand()
    {
        // a slab 2 x 2 (x 0..1, z 0..1) on the ground, and a block floating 4 up (x -2): only standing on the
        // slab does it reach that block; dug from beside it, the slab takes the block's last stand with it
        var world = new MineTestWorld().Ground(L - 1).Box(0, 1, L, L, 0, 1).Box(-2, -2, L + 4, L + 4, 0, 0);
        var area = Area(-2, 1, L, L + 4, 0, 1);
        var state = State(world, area, new PathCell(-8, L, 0));
        var floating = new MarkPos(-2, L + 4, 0);
        Assert.Contains(floating, state.Remaining);
        Assert.True(state.Reach.Live(floating) > 0);
        var blame = new HashSet<MarkPos>();
        var slab = Cells(0, 1, L, L, 0, 1).ToList();
        Assert.Equal(MineRefusal.Starves, state.Check(new PathCell(3, L, 1), slab, [], blame));
        Assert.Equal(slab.ToHashSet(), blame);
        // the block dug first, the slab may go
        state.Apply([floating]);
        Assert.Equal(MineRefusal.None, state.Check(new PathCell(3, L, 1), slab, []));
    }

    [Fact]
    public void ATriedDigLeavesTheStateAsItWas()
    {
        var (world, area) = Pillar();
        var state = State(world, area);
        string before = Hash(state);
        state.Check(OnPillar, Cells(6, 7, L - 1, L - 1, 4, 5).ToList(), []);
        state.Check(OnPillar, [new MarkPos(6, L - 1, 4)], []);
        Assert.Equal(before, Hash(state));
    }

    private static string Hash(MineState state)
    {
        var w = state.Site.Window;
        var solid = Cells(w.Min.X, w.Max.X, w.Min.Y, w.Max.Y, w.Min.Z, w.Max.Z).Count(c => state.Site.Solid(c.X, c.Y, c.Z));
        var nodes = string.Join(";", state.Walk.Nodes);
        var live = string.Join(";", state.Remaining.OrderBy(c => c.X).ThenBy(c => c.Y).ThenBy(c => c.Z).Select(c => state.Reach.Live(c)));
        return $"{solid}|{nodes}|{live}|{state.Site.Version}";
    }

    [Fact]
    public void TheWalkGraphKeptUpAsCellsAreDugIsTheOneBuiltAfresh()
    {
        var world = new MineTestWorld().Ground(L - 1).Hill(0, 12, L + 9, -3, 4, L).Box(-2, -1, L, L + 1, -3, 4);
        var area = Area(0, 7, L, L + 5, 0, 1);
        var plan = world.Plan(area, Home);
        var state = State(world, area);
        foreach (var station in plan.Stations)
        {
            state.Apply(station.Cells);
            var fresh = new MineWalk(state.Site.Clone(), Home);
            fresh.BuildNow();
            Assert.Equal(fresh.Nodes.ToHashSet(), state.Walk.Nodes.ToHashSet());
        }
    }

    [Fact]
    public void AChangeInTheWorldIsTakenInAndMarksThePlanStale()
    {
        var (world, area) = Pillar();
        var state = State(world, area);
        Assert.False(state.Stale);
        // a player puts a block on the pillar, by where it stands: taken in, the graphs made again before
        // the state is next asked
        state.Changed(new MarkPos(5, L, 6), new MineVoxel(true, MineRule.Mine));
        Assert.True(state.Stale);
        Assert.True(state.Site.Solid(5, L, 6));
        Assert.Equal(MineRefusal.NotStandable, state.Check(new PathCell(5, L, 6), [], []));
        Assert.True(state.Walk.Out(OnPillar));
        // a player breaks a target: no longer one
        state.Changed(new MarkPos(4, L - 1, 4), new MineVoxel(false, MineRule.Air));
        Assert.DoesNotContain(new MarkPos(4, L - 1, 4), state.Remaining);
    }

    [Fact]
    public void A_change_on_a_largest_area_is_taken_in_locally_within_a_tick()
    {
        // a pit 128 x 128 and one deep, half dug (its west half): its walk graph is the largest flat one
        var world = new MineTestWorld().Ground(L - 1, from: -10, to: 140);
        var area = Area(0, 127, L - 1, L - 1, 0, 127);
        foreach (var c in Cells(0, 63, L - 1, L - 1, 0, 127))
            world.Dig(c);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var state = State(world, area);
        var built = clock.Elapsed;
        // a player puts blocks down in the dug half, and breaks some of the rest: each taken in at once
        var changes = new List<(MarkPos Cell, MineVoxel Voxel)>();
        for (int i = 0; i < 20; i++)
        {
            changes.Add((new MarkPos(5 + 3 * i, L - 1, 7 + 5 * i), new MineVoxel(true, MineRule.Mine)));
            changes.Add((new MarkPos(70 + 2 * i, L - 1, 9 + 6 * i), new MineVoxel(false, MineRule.Air)));
        }
        var worst = TimeSpan.Zero;
        foreach (var (cell, voxel) in changes)
        {
            clock.Restart();
            state.Changed(cell, voxel);
            state.Check(new PathCell(-3, L, 3), [], []);
            worst = TimeSpan.FromTicks(Math.Max(worst.Ticks, clock.Elapsed.Ticks));
            if (voxel.Solid)
                world.Solid.Add((cell.X, cell.Y, cell.Z));
            else
                world.Dig(cell);
        }
        // a fraction of building it, and well within a tick's planning budget
        Assert.True(worst.TotalMilliseconds < Math.Max(MinePlanner.BudgetMs * 5, built.TotalMilliseconds / 20),
            $"a change took {worst.TotalMilliseconds:0.0} ms, building {built.TotalMilliseconds:0} ms");
        Assert.True(state.Stale);
        // and it is the walk graph built afresh
        var fresh = new MineWalk(world.Site(area), Home);
        fresh.BuildNow();
        Assert.Equal(fresh.Nodes.ToHashSet(), state.Walk.Nodes.ToHashSet());
    }

    [Fact]
    public void The_site_keeps_each_block_s_pace_and_copies_it()
    {
        var world = new MineTestWorld().Ground(L - 1);
        var area = Area(0, 3, L - 2, L - 1, 0, 3);
        var slow = new MarkPos(1, L - 1, 1);
        world.Pace[slow] = 2.5f;
        var site = world.Site(area);
        Assert.Equal(2.5, site.PlayerSeconds(slow), 6);
        Assert.Equal(MineCosts.DefaultPlayerSeconds, site.PlayerSeconds(new MarkPos(2, L - 1, 2)), 6);
        // a change that does not know the pace keeps it; one that does takes it
        site.Set(slow, new MineVoxel(true, MineRule.Mine));
        Assert.Equal(2.5, site.PlayerSeconds(slow), 6);
        site.Set(slow, new MineVoxel(true, MineRule.Mine, 1.2f));
        Assert.Equal(1.2, site.PlayerSeconds(slow), 6);
        // a copy has it
        var copy = site.Clone();
        Assert.Equal(1.2, copy.PlayerSeconds(slow), 6);
        site.Set(slow, new MineVoxel(true, MineRule.Mine, 3f));
        Assert.Equal(1.2, copy.PlayerSeconds(slow), 6);
        // a tried change is undone with its pace
        int mark = site.Mark();
        site.Set(slow, new MineVoxel(true, MineRule.Mine, 0.5f));
        site.Undo(mark);
        Assert.Equal(3, site.PlayerSeconds(slow), 6);
        // outside the area (in the window or not), the default
        Assert.Equal(MineCosts.DefaultPlayerSeconds, site.PlayerSeconds(new MarkPos(-3, L - 1, 1)), 6);
        Assert.Equal(MineCosts.DefaultPlayerSeconds, site.PlayerSeconds(new MarkPos(500, L - 1, 1)), 6);
        // and the strike's work estimate reads it: the slowest of each four
        world.Pace[new MarkPos(2, L - 1, 2)] = 1.8f;
        var paced = world.Site(area);
        double strike(double seconds) => EidolonMining.Timing(EidolonMining.StrikeSeconds([seconds])).Seconds;
        var cells = Cells(0, 3, L - 1, L - 1, 0, 1).Append(new MarkPos(2, L - 1, 2)).Append(slow).ToList();
        Assert.Equal(strike(2.5) + strike(0.9) + strike(0.9), MineCosts.WorkSeconds(paced, cells), 6);
    }
}
