using SeraphHorizons.Mod.Pipes.Core;

namespace SeraphHorizons.Tests.Pipes;

/// <summary>Hydrate or Diedrate's hand pump on ppex pipes (Pipes/Core/HandPumpSearch.cs): the
/// spring search over the pipe cells, the priming count as Hydrate counts it, the intake's scan and
/// allowance, and the name fallbacks.</summary>
public class HandPumpSearchTests
{
    private static readonly PipeCell Pump = new(0, 10, 0);
    private static readonly PipeCell Under = new(0, 9, 0);

    /// <summary>Every pipe in <paramref name="pipes"/> couples to every pipe beside it.</summary>
    private static PumpPath? Search(IEnumerable<PipeCell> pipes, IEnumerable<PipeCell> springs,
        Func<PipeCell, int, bool>? linked = null, int max = HandPumpSearch.MaxVisited)
    {
        var set = pipes.ToHashSet();
        var springSet = springs.ToHashSet();
        return HandPumpSearch.Nearest(Under, set.Contains, linked ?? ((_, _) => true), springSet.Contains, max);
    }

    private static IEnumerable<PipeCell> Column(int length) =>
        Enumerable.Range(0, length).Select(i => new PipeCell(0, 9 - i, 0));

    [Fact]
    public void A_spring_under_a_straight_run_is_as_many_pipes_away_as_the_run_is_long()
    {
        // Hydrate's PipeTraversal.Distance: the cell under the pump is 0, the spring past the last pipe N.
        foreach (int n in new[] { 1, 2, 5, 12 })
        {
            var spring = new PipeCell(0, 9 - n, 0);
            var path = Search(Column(n), [spring]);
            Assert.Equal(new PumpPath(spring, n), path);
        }
    }

    [Fact]
    public void A_spring_beside_any_pipe_on_any_face_is_found()
    {
        var pipes = Column(4).Concat([new PipeCell(1, 6, 0), new PipeCell(2, 6, 0)]).ToList();
        foreach (var face in Enumerable.Range(0, 6))
        {
            var spring = new PipeCell(2, 6, 0).Step(face);
            if (pipes.Contains(spring))
                continue;
            var path = Search(pipes, [spring]);
            Assert.NotNull(path);
            Assert.Equal(spring, path!.Value.Spring);
        }
    }

    [Fact]
    public void The_nearest_spring_wins()
    {
        var near = new PipeCell(1, 8, 0);
        var far = new PipeCell(0, 3, 0);
        Assert.Equal(new PumpPath(near, 2), Search(Column(6), [far, near]));
    }

    [Fact]
    public void Of_two_springs_as_near_the_first_face_in_game_order_wins()
    {
        // North (z - 1) comes before east (x + 1) in BlockFacing.ALLFACES.
        var north = new PipeCell(0, 9, -1);
        var east = new PipeCell(1, 9, 0);
        Assert.Equal(north, Search(Column(1), [east, north])!.Value.Spring);
    }

    [Fact]
    public void Nothing_is_found_without_a_pipe_under_the_pump()
    {
        Assert.Null(Search([new PipeCell(0, 8, 0)], [new PipeCell(0, 7, 0)]));
    }

    [Fact]
    public void Nothing_is_found_without_a_spring()
    {
        Assert.Null(Search(Column(8), []));
    }

    [Fact]
    public void An_unlinked_face_stops_the_walk_as_a_closed_valve_does()
    {
        var spring = new PipeCell(0, 4, 0);
        // The pipe at y = 7 does not couple downwards.
        Func<PipeCell, int, bool> linked = (cell, face) => !(cell.Y == 7 && face == 5);
        Assert.Null(Search(Column(5), [spring], linked));
        Assert.Equal(new PumpPath(spring, 5), Search(Column(5), [spring]));
    }

    [Fact]
    public void A_branch_round_a_cut_still_reaches_the_spring_by_the_longer_way()
    {
        // 9 .. 5 straight down, cut between 7 and 6; a loop out east: (0,7) -> (1,7) -> (1,6) -> (0,6).
        var pipes = Column(5).Concat([new PipeCell(1, 7, 0), new PipeCell(1, 6, 0)]);
        Func<PipeCell, int, bool> linked = (cell, face) => !(cell == new PipeCell(0, 7, 0) && face == 5)
                                                           && !(cell == new PipeCell(0, 6, 0) && face == 4);
        var spring = new PipeCell(0, 4, 0);
        // under(9) 8 7 (1,7) (1,6) 6 5 -> spring: 7 pipes
        Assert.Equal(new PumpPath(spring, 7), Search(pipes, [spring], linked));
    }

    [Fact]
    public void The_walk_stops_at_its_limit()
    {
        var spring = new PipeCell(0, -100, 0);
        Assert.Null(Search(Column(109), [spring], max: 50));
        Assert.Equal(109, Search(Column(109), [spring])!.Value.Pipes);
    }

    [Theory]
    [InlineData(0, true, 3, 0)]
    [InlineData(1, true, 3, 0)]
    [InlineData(3, true, 3, 1)]
    [InlineData(10, true, 3, 3)]
    [InlineData(10, true, 0, 3)]
    [InlineData(10, true, -2, 3)]
    [InlineData(10, true, 2, 5)]
    [InlineData(10, false, 3, 0)]
    [InlineData(-4, true, 3, 0)]
    public void Priming_is_Hydrates_pipes_over_blocks_per_stroke(int pipes, bool enabled, int perStroke, int strokes)
    {
        Assert.Equal(strokes, HandPumpSearch.PrimingStrokes(pipes, enabled, perStroke));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("Water", true)]
    [InlineData("Steam", false)]
    [InlineData("Exhaust", false)]
    [InlineData("Air", false)]
    [InlineData("water", false)]
    public void Only_an_empty_or_water_run_reaches_a_well(string? medium, bool reaches)
    {
        Assert.Equal(reaches, HandPumpSearch.CarriesWater(medium));
    }

    [Fact]
    public void The_intake_scans_ppexs_volume_nearest_first()
    {
        var cells = HandPumpSearch.IntakeCells(3).ToList();
        // ppex's default FluidIntakeWaterDepth 3: 3 deep, one block out each way.
        Assert.Equal(27, cells.Count);
        Assert.Equal(27, cells.Distinct().Count());
        Assert.Equal(new PipeCell(0, -1, 0), cells[0]);
        Assert.All(cells, c => Assert.InRange(c.Y, -3, -1));
        Assert.All(cells, c => Assert.InRange(c.X, -1, 1));
        Assert.All(cells, c => Assert.InRange(c.Z, -1, 1));
        // Each layer from the centre out before the next layer down.
        Assert.Equal(new PipeCell(0, -2, 0), cells[9]);
        Assert.Equal(cells.OrderBy(c => -c.Y).ThenBy(c => Math.Abs(c.X) + Math.Abs(c.Z)).ToList(), cells);
    }

    [Fact]
    public void A_shallow_intake_scans_at_least_the_cell_below()
    {
        Assert.Equal([new PipeCell(0, -1, 0)], HandPumpSearch.IntakeCells(0).ToList());
        Assert.Equal([new PipeCell(0, -1, 0)], HandPumpSearch.IntakeCells(1).ToList());
    }

    [Theory]
    [InlineData(10f, 100f, 10f)]
    [InlineData(10f, 4f, 4f)]
    [InlineData(10f, 0f, 0f)]
    [InlineData(10f, -3f, 0f)]
    public void An_intake_gets_no_more_than_the_spring_holds(float asked, float held, float allowed)
    {
        Assert.Equal(allowed, HandPumpSearch.IntakeAllowance(asked, held));
    }

    [Fact]
    public void Faces_are_in_the_games_order_and_opposites_pair()
    {
        Assert.Equal(new PipeCell(0, 0, -1), PipeCell.Faces[0]);
        Assert.Equal(new PipeCell(0, 1, 0), PipeCell.Faces[4]);
        Assert.Equal(new PipeCell(0, -1, 0), PipeCell.Faces[5]);
        for (int face = 0; face < 6; face++)
        {
            var there = new PipeCell(3, 4, 5).Step(face).Step(PipeCell.Opposite(face));
            Assert.Equal(new PipeCell(3, 4, 5), there);
            Assert.Equal(face, PipeCell.Opposite(PipeCell.Opposite(face)));
        }
    }

    [Fact]
    public void Names_are_tried_newest_first_and_fall_back()
    {
        var known = new Dictionary<string, string> { ["Old.Manager"] = "old", ["New.Manager"] = "new" };
        string? Resolve(string name) => known.GetValueOrDefault(name);
        Assert.Equal(("New.Manager", "new"), NameCandidates.First(["New.Manager", "Old.Manager"], Resolve));
        Assert.Equal(("Old.Manager", "old"), NameCandidates.First(["Gone.Manager", "Old.Manager"], Resolve));
        Assert.Equal(("Old.Manager", "old"), NameCandidates.First([null, "", "Old.Manager"], Resolve));
        Assert.Null(NameCandidates.First(["Gone.Manager"], Resolve));
        Assert.Null(NameCandidates.First([], Resolve));
    }
}
