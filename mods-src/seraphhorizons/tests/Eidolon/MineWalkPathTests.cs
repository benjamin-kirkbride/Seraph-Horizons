using SeraphHorizons.Mod.Eidolon.Core;
using Xunit.Abstractions;
using static SeraphHorizons.Mod.Tests.Eidolon.MineTestWorld;

namespace SeraphHorizons.Mod.Tests.Eidolon;

/// <summary>
/// The walk graph (<see cref="MineWalk"/>) against the eidolon's pathfinder (<see cref="WidePath"/>, climb and
/// drop 2, as the game's config has them) on the same blocks (README "Eidolon", mining, *Stations*): a node is
/// joined to another exactly where the pathfinder steps from one to the other, so a stand the graph holds out
/// is one the game finds a way to, and one it does not hold out is one the game finds none to. On the
/// hillside (tests/PackTests/EidolonHillsideScenarios.cs) every stand the game found no way to was of the
/// second kind: still in rock in the live state, handed out by a route whose earlier stops were to open it.
/// </summary>
public class MineWalkPathTests(ITestOutputHelper output)
{
    // Every case is lifted L blocks: the world's bottom layer is its mantle, never mined (EidolonMining.BottomY).
    private const int L = 10;

    private static readonly (int Dx, int Dz)[] Directions =
        [(1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1)];

    // Every node's joins against the pathfinder's steps from it, both ways: what the graph joins it to, and
    // the nodes the pathfinder lands on from it. Returns how many nodes were compared.
    private static int AssertAgree(MineState state, string what)
    {
        var site = state.Site;
        var path = new WidePath(site, 2, 2);
        var w = site.Window;
        int nodes = 0;
        for (int x = w.Min.X + 1; x <= w.Max.X; x++)
        for (int z = w.Min.Z + 1; z <= w.Max.Z; z++)
        for (int y = w.Min.Y; y <= w.Max.Y - 3; y++)
        {
            var node = new PathCell(x, y, z);
            if (!state.Walk.Node(node))
                continue;
            nodes++;
            var joined = state.Walk.Joins(node).ToHashSet();
            var steps = new HashSet<PathCell>();
            foreach (var (dx, dz) in Directions)
                if (path.Step(node, dx, dz, out _) is { } to && state.Walk.Node(to))
                    steps.Add(to);
            Assert.True(joined.SetEquals(steps),
                $"{what}: from {node} the graph joins {string.Join(" ", joined.Except(steps))} the pathfinder does not step to, "
                + $"and the pathfinder steps to {string.Join(" ", steps.Except(joined))} the graph does not join");
        }
        return nodes;
    }

    [Theory]
    [InlineData(34, 0)]
    [InlineData(44, 2)]
    [InlineData(10, 0)]
    [InlineData(3, 2)]
    public void Every_join_of_the_walk_graph_is_a_step_of_the_pathfinder_and_back(int seed, int step)
    {
        // The hillside's tunnel, its bare columns and ledges, as dug: before any dig and after each tenth of
        // the plan's stations, in the plan's order.
        var world = MineHillsideTests.Hillside(seed, step);
        var area = Area(0, 13, L, L + 7, 0, 13);
        var home = new PathCell(6, L, -8);
        var plan = world.Plan(area, home);
        var live = new MineState(world.Site(area), home);
        live.BuildNow(plan.Targets);
        int compared = AssertAgree(live, "before any dig");
        int every = Math.Max(1, plan.Stations.Count / 10);
        for (int k = 0; k < plan.Stations.Count; k++)
        {
            var cells = plan.Stations[k].Cells.Where(live.Remaining.Contains).ToList();
            live.Apply(cells);
            if (k % every == every - 1)
                compared += AssertAgree(live, $"after station {k}");
        }
        output.WriteLine($"{plan.Stations.Count} stations, {compared} nodes compared");
    }

    // The blocks round a stand the game found no way to on the hillside (run 3: member 2 at (511281,222,513600),
    // its route's next stop station 12, stand (511281,222,513602)), x 511278..511283 across, z 513597..513604
    // down, y 220..226 from the bottom; here x and z from 0 and y 220 at L - 2.
    private static readonly string[][] Captured =
    [
        ["....##", "......", "......", ".####.", ".####.", ".#####", ".#####", ".#####"],
        ["......", "......", "......", ".##...", ".##...", ".##...", ".##...", ".##..."],
        ["......", "......", "......", "......", "......", ".##...", ".##...", "......"],
        ["......", "......", "......", "......", "......", ".##...", ".##...", "......"],
        ["......", "......", "......", "......", "......", ".##...", ".##...", ".##..."],
        ["......", "......", "......", "......", ".##...", ".##...", ".#####", ".#####"],
        ["......", "#.....", "###...", "####..", "#####.", "######", "######", "######"],
    ];

    [Fact]
    public void A_stand_the_game_found_no_way_to_on_the_hillside_was_in_rock_on_the_walk_graph_too()
    {
        // Under the capture, ground; round it, open air to the window's side.
        var world = new MineTestWorld().Ground(L - 3, 1, -20, 25);
        for (int layer = 0; layer < Captured.Length; layer++)
        for (int z = 0; z < Captured[layer].Length; z++)
        for (int x = 0; x < Captured[layer][z].Length; x++)
            if (Captured[layer][z][x] == '#')
                world.Solid.Add((x, L - 2 + layer, z));
        var area = Area(0, 5, L - 2, L + 4, 0, 7);
        var home = new PathCell(-4, L - 2, 3);
        var live = new MineState(world.Site(area), home);
        live.BuildNow(EidolonMining.Targets(area, live.Site, _ => false).Dig);
        PathCell from = new(3, L, 3), stand = new(3, L, 5);
        Assert.True(live.Walk.Out(from), "where it stood is out");
        // The stand's box holds rock (x 2 at z 5 from its feet to its head, at z 4 by its head): no
        // node, and no way there for the pathfinder either.
        Assert.False(live.Site.Standable(stand));
        Assert.False(live.Walk.Node(stand));
        Assert.False(live.Walk.Out(stand));
        Assert.Null(new WidePath(live.Site, 2, 2).Find(from, stand, 200_000));
        Assert.Equal(MineRefusal.NotStandable, live.Check(stand, [new MarkPos(2, L, 6)], []));
        AssertAgree(live, "the capture");
        // Once the rock in its box is dug (what the stops before it were to dig), both find it.
        live.Apply(MineTestWorld.Cells(1, 2, L, L + 3, 4, 6).Where(live.Remaining.Contains).ToList());
        Assert.True(live.Walk.Out(stand));
        Assert.NotNull(new WidePath(live.Site, 2, 2).Find(from, stand, 200_000));
        AssertAgree(live, "the capture dug");
    }
}
