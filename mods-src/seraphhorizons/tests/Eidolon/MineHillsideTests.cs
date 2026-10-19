using SeraphHorizons.Mod.Eidolon.Core;
using Xunit.Abstractions;
using static SeraphHorizons.Mod.Tests.Eidolon.MineTestWorld;

namespace SeraphHorizons.Mod.Tests.Eidolon;

/// <summary>
/// Mining into a hillside whose edge wanders across the area (README "Eidolon", mining, *Stations*,
/// *Tunnels*, *Out of reach*), as a player's world has it: the roof over part of the area, bare rock
/// columns standing in front of the face, a ledge to one side. Nothing the reach bound reaches is held as
/// out of reach for the plan's precedence alone, no dig the guard lets through strands a block still to
/// dig, and what is kept under a block out of reach is never told again as newly out of reach.
/// </summary>
public class MineHillsideTests(ITestOutputHelper output)
{
    // Every case is lifted L blocks: the world's bottom layer is its mantle, never mined (EidolonMining.BottomY).
    private const int L = 10;

    /// <summary>
    /// A hill 3 over an area 8 high, its edge wandering 2 either way a column, from z 2 to 9; in front of
    /// it, bare columns of rock the area's height with nothing over them (one in four of the
    /// <paramref name="seed"/>'s chosen few rows in front of the edge), ground <paramref name="step"/>
    /// high for 3 rows, then flat ground; and to the west (x up to 2) a ledge 4 high with a step 2 high in
    /// front of it, the way up onto the face. tests/PackTests/EidolonOverhangScenarios.cs builds the same.
    /// </summary>
    internal static MineTestWorld Hillside(int seed, int step)
    {
        var rng = new Random(seed);
        var world = new MineTestWorld().Ground(L - 1, 1, -30, 50);
        int top = L + 7;
        var edge = new int[40];
        int e = 5;
        for (int x = 0; x < 40; x++)
            edge[x] = e = Math.Clamp(e + rng.Next(-2, 3), 2, 9);
        int bare = rng.Next(2, 5);
        for (int x = -12; x <= 27; x++)
        for (int z = -12; z <= 30; z++)
        {
            int ex = edge[Math.Clamp(x + 6, 0, 39)];
            int h = z >= ex ? top + 3
                : z >= ex - bare && rng.Next(4) == 0 ? top
                : x <= 2 && z >= -2 ? L + 3
                : x <= 2 && z >= -4 ? L + 1
                : z >= ex - 3 ? L - 1 + step
                : L - 1;
            for (int y = L; y <= h; y++)
                world.Solid.Add((x, y, z));
        }
        return world;
    }

    private static readonly MarkArea Small = Area(0, 9, L, L + 7, 0, 9);
    private static readonly PathCell Home = new(4, L, -8);

    [Theory]
    [InlineData(34, 0)]
    [InlineData(44, 2)]
    [InlineData(10, 0)]
    public void Two_workers_finish_a_hillside_with_bare_columns_in_front_of_its_face(int seed, int step)
    {
        // A bare column in front of the face, in a lane of the tunnel but not under its roof, is reached only
        // from inside the tunnel: counted as the face, it held the lane's face for good, and the plan held
        // what lay behind it as out of reach (181 blocks of 652 on the first).
        var world = Hillside(seed, step);
        var plan = world.Plan(Small, Home);
        output.WriteLine($"{plan.Stations.Count} stations, {plan.Targets.Count} blocks, out of reach {plan.OutOfReach.Count}, held {plan.Held.Count}, free {plan.Free.Count}, reruns {plan.Reruns}");
        Assert.NotNull(plan.Tunnel);
        Assert.Empty(plan.OutOfReach);
        Assert.Empty(plan.Held);
        var team = new TeamRunner(world, Small, plan, Home, [new PathCell(2, L, -8), new PathCell(7, L, -8)]);
        team.Run(20_000);
        output.WriteLine($"mined {string.Join(", ", team.Mined)} in {team.Seconds:0} s, {team.Replans} replans");
        Assert.DoesNotContain(Cells(0, 9, L, L + 7, 0, 9), world.Has);
        Assert.True(team.AllLeave(Home), "both leave");
    }

    [Theory]
    [InlineData(23, 2)]
    [InlineData(23, 23)]
    [InlineData(43, 43)]
    public void No_dig_the_guard_lets_through_strands_a_block_still_to_dig(int seed, int order)
    {
        // The plan's groups dug in a shuffled order (members working out of the plan's order, as far as it
        // goes), each through the strike's guard; after each, the reach bound made from scratch over what is left
        // still reaches every block still to dig. Ceiling blocks each reached only from a stand under
        // another kept each other's counts up once the stands they began from were dug away, as did stands
        // cut off from the ways out by the dig, and the dig that stranded them went through.
        var world = Hillside(seed, 2);
        var area = Area(0, 13, L, L + 7, 0, 13);
        var home = new PathCell(6, L, -8);
        var plan = world.Plan(area, home);
        var live = new MineState(world.Site(area), home);
        live.BuildNow(plan.Targets);
        var rng = new Random(order);
        var pending = plan.Stations.ToList();
        int dug = 0;
        for (int pass = 0; pass < 50 && pending.Count > 0; pass++)
        {
            var next = new List<MineStation>();
            foreach (var station in pending.OrderBy(_ => rng.Next()))
            {
                var cells = station.Cells.Where(live.Remaining.Contains).ToList();
                if (cells.Count == 0)
                    continue;
                var group = cells.OrderByDescending(c => c.Y).Take(1 + rng.Next(4)).ToList();
                if (live.Check(station.Stand, group, [], null, exact: true) != MineRefusal.None)
                {
                    next.Add(station);
                    continue;
                }
                live.Apply(group, exact: true);
                foreach (var c in group)
                    world.Dig(c);
                dug += group.Count;
                if (cells.Count > group.Count)
                    next.Add(station);
                var fresh = new MineState(world.Site(area), home);
                fresh.BuildNow(live.Remaining.ToHashSet());
                var lost = live.Remaining.Where(c => !fresh.Reach.Clearable.Contains(c)).ToList();
                Assert.True(lost.Count == 0, $"digging {string.Join(" ", group)} from {station.Stand} stranded {string.Join(" ", lost)}");
            }
            pending = next;
        }
        output.WriteLine($"dug {dug} of {plan.Targets.Count} in a shuffled order, {live.Remaining.Count} left");
        Assert.True(dug > 100, $"only {dug} dug");
    }

    [Fact]
    public void A_ceiling_held_up_by_the_next_bands_ground_is_struck_from_a_step_left_in_it()
    {
        // Precedence alone (the bands, the lanes) runs dry with blocks left that the bound reaches; the last
        // sweep digs them whatever their band, and the plan lets the job dig those out of turn.
        var world = Hillside(3, 2);
        var area = Area(0, 13, L, L + 7, 0, 13);
        var home = new PathCell(6, L, -8);
        var plan = world.Plan(area, home);
        output.WriteLine($"out of reach {plan.OutOfReach.Count}, free {plan.Free.Count}");
        Assert.NotEmpty(plan.Free);
        Assert.Subset(plan.Targets.ToHashSet(), plan.Free.ToHashSet());
        var live = new MineState(world.Site(area), home);
        live.AdoptNow(plan);
        var strict = new MineBands(area, plan.Tunnel, plan.Stair, live.Remaining.Where(plan.Targets.Contains), widen: false);
        Assert.Contains(plan.Free, c => live.Remaining.Contains(c) && !strict.Eligible(c) && live.Bands!.Eligible(c));
    }

    [Fact]
    public void What_was_kept_under_a_block_out_of_reach_is_not_told_again_as_out_of_reach()
    {
        // Once the ground round a column kept under a block out of reach is dug, the column stands alone and
        // its blocks are out of reach too; told so, the count would grow with nothing new stranded.
        var world = Hillside(7, 0);
        var area = Area(0, 13, L, L + 7, 0, 13);
        var home = new PathCell(6, L, -8);
        var plan = world.Plan(area, home);
        Assert.NotEmpty(plan.OutOfReach);
        Assert.NotEmpty(plan.Held);
        var team = new TeamRunner(world, area, plan, home, [new PathCell(3, L, -8), new PathCell(9, L, -8)]);
        team.Run(20_000);
        Assert.DoesNotContain(plan.Targets, c => world.Has(c) && !plan.WayOut.Contains(c));
        var unaware = world.Plan(area, home);
        var again = MinePlanner.Run(world.Input(area, home) with { Kept = plan.Held });
        output.WriteLine($"out of reach {plan.OutOfReach.Count} kept {plan.Held.Count}; after: {unaware.OutOfReach.Count} told without what was kept, {again.OutOfReach.Count} with ({again.Held.Count} kept)");
        Assert.True(unaware.OutOfReach.Count > plan.OutOfReach.Count, "the pillars left are out of reach now");
        Assert.True(again.OutOfReach.Count <= plan.OutOfReach.Count, $"{again.OutOfReach.Count} told, {plan.OutOfReach.Count} before");
        Assert.Equal(plan.OutOfReach.Count + plan.Held.Count, again.OutOfReach.Count + again.Held.Count);
    }
}
