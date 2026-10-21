using SeraphHorizons.Mod.Eidolon.Core;
using Xunit.Abstractions;

namespace SeraphHorizons.Mod.Tests.Eidolon;

/// <summary>
/// A player's mining area at the foot of a cliff (tests/Eidolon/fixtures/cliff-foot.mine, made from the WorldEdit
/// export tests/PackTests/fixtures/eidolon-cliff holds, EidolonCliffFootScenarios): the ground rises from the foot
/// at y 116 to about 127, then a sheer face of rock to a hilltop at 138 to 144 that reaches past the area to the
/// window's side, and no walk from the foot gets onto it. Every place on the window's side is taken as joined to
/// every other through the world outside (README "Eidolon", mining, *Stations*), so the plan dug the area from the
/// hilltop down; the game's pathfinder found no way to any of its stands, and three eidolons at the foot found
/// one bad after another, the plan made again each time with the next, nothing dug, for good. A stand found bad
/// that is joined to where it stands only by the window's side shuts those ways out too
/// (<see cref="MineWalk.SideWays"/>): the hilltop is out of reach, and the crew digs all it reaches from the foot.
/// </summary>
public class MineCliffFootTests(ITestOutputHelper output)
{
    private static readonly PathCell Foot = new(511950, 116, 512029);
    private static readonly PathCell[] Crew = [new(511950, 116, 512029), new(511950, 116, 512031), new(511950, 116, 512034)];

    private MinePlan Plan(MineTestWorld.Dump dump, HashSet<PathCell> bad, string what)
    {
        var input = new MinePlanInput(new MineState(dump.Site(), dump.Home, bad), dump.Home, _ => false, bad) { Roof = dump.Roof.Contains };
        var plan = MinePlanner.Run(input);
        output.WriteLine($"{what}: {plan.Stations.Count} stations, {plan.Targets.Count} to dig, {plan.OutOfReach.Count} out of reach, "
            + $"{plan.Held.Count} held, {plan.Stair.Count} stair, {plan.WayOut.Count} way out, {plan.Reruns} reruns");
        return plan;
    }

    [Fact]
    public void A_hilltop_joined_to_the_foot_only_by_the_windows_side_is_no_way_in_once_a_stand_on_it_is_found_bad()
    {
        var dump = MineTestWorld.FromDump("cliff-foot.mine");
        var plan = Plan(dump, [], "first");
        var live = new MineState(dump.Site(), dump.Home, new HashSet<PathCell>());
        live.BuildNow(plan.Targets);

        // the plan begins on the hilltop, out by the window's side alone: no walk from the foot gets there
        var first = plan.Stations[0].Stand;
        Assert.True(first.Y >= 138, $"the first station stands at {first}, not on the hilltop");
        Assert.True(live.Walk.Out(first));
        var path = new WidePath(live.Site, 2, 2);
        Assert.Null(path.Find(Foot, first, PlanRunner.MaxNodes));

        // found bad from the foot: the ways out by the side it hangs from go with it, all on the window's side
        var joined = new HashSet<PathCell>();
        var side = live.Walk.SideWays(first, Foot, joined);
        Assert.NotNull(side);
        Assert.All(side, s => Assert.True(live.Site.OnRing(s.X, s.Z), $"{s} is not on the window's side"));
        // and every stand of the plan out now that no walk from the foot gets to is one those alone led out, none
        // taken until the plan made again comes in
        var hilltop = plan.Stations.Select(s => s.Stand).Where(s => live.Walk.Out(s) && path.Find(Foot, s, PlanRunner.MaxNodes) == null).ToList();
        Assert.True(hilltop.Count >= 10, $"{hilltop.Count} stations on the hilltop out now");
        Assert.All(hilltop, s => Assert.Contains(s, joined));
        output.WriteLine($"{side.Count} ways out by the side shut: x {side.Min(s => s.X)}..{side.Max(s => s.X)}, y {side.Min(s => s.Y)}..{side.Max(s => s.Y)}");
        // a stand joined in the window to where it stands shuts nothing: it was only in the way for now
        var low = plan.Stations.First(s => path.Find(Foot, s.Stand, PlanRunner.MaxNodes) != null).Stand;
        Assert.Null(live.Walk.SideWays(low, Foot));
        // nor one joined to where it was ordered from, wherever it stands now
        Assert.Null(live.Walk.SideWays(low, new PathCell(511950, 140, 512029)));

        // planned again with them: the hilltop's blocks out of its reach (it reaches 3 over its feet, and from the
        // foot's slope that is 134 at most), and three at the foot dig all the plan has them dig, each walking to
        // every station it takes from where it is (TeamRunner)
        var bad = side.Append(first).ToHashSet();
        var again = Plan(dump, bad, "again");
        Assert.NotEmpty(again.Stations);
        Assert.All(again.OutOfReach, c => Assert.True(c.Y >= 134, $"{c} held out of reach under the cliff's top"));
        Assert.True(again.Targets.Count >= 1400, $"{again.Targets.Count} to dig");
        var world = MineTestWorld.FromDump("cliff-foot.mine").World;
        var team = new TeamRunner(world, dump.Area, again, dump.Home!.Value, Crew);
        team.Run(20_000);
        output.WriteLine($"mined {string.Join(" + ", team.Mined)} = {team.Mined.Sum()} in {team.Seconds:0} s, {team.Replans} replans");
        Assert.Empty(team.Live.Remaining);
        Assert.Equal(again.Targets.Count, team.Mined.Sum());
    }
}
