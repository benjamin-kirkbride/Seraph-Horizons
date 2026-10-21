using SeraphHorizons.Mod.Eidolon.Core;
using Xunit.Abstractions;

namespace SeraphHorizons.Mod.Tests.Eidolon;

/// <summary>
/// Plans made again from live jobs' inputs on ground of the game's own generation (tests/PackTests/EidolonRealTerrain*
/// scenarios, seed 31337; dumped by RealMineRun, tests/Eidolon/fixtures/*.mine), each once held cells the reach bound
/// reaches as out of reach, red on blocks plainly minable (README "Eidolon", mining, *Out of reach*). Now none is.
/// </summary>
public class MineRealGroundTests(ITestOutputHelper output)
{
    private MinePlan Plan(MineTestWorld.Dump dump, string name)
    {
        var plan = MinePlanner.Run(dump.Input());
        output.WriteLine($"{name}: {plan.Stations.Count} stations, {plan.Targets.Count} to dig, {plan.OutOfReach.Count} out of reach, "
            + $"{plan.Held.Count} held, {plan.Stair.Count} stair, {plan.WayOut.Count} way out, {plan.Reruns} reruns");
        return plan;
    }

    [Theory]
    // the large hillside mid-job (37 x 12 x 28, its tunnel's top band driven, the open ground in front quarried to its
    // floor): the next band was driven under the approach before it was done, the open ground quarried a climb under
    // that band's floor, and the approach left a wall nothing stood by (95 out of reach, 578 held under them)
    [InlineData("hillside-approach.mine")]
    // later in the same job: two columns open to the sky in the covered part, dug as the approach, their lowest cells
    // stranded over the next band's floor (4 out of reach)
    [InlineData("hillside-island.mine")]
    public void A_band_is_driven_only_once_the_approach_over_its_floor_is_done(string name)
    {
        var dump = MineTestWorld.FromDump(name);
        var plan = Plan(dump, name);
        Assert.NotNull(plan.Tunnel);
        Assert.Empty(plan.OutOfReach);
        Assert.Empty(plan.Held);
        Assert.Equal(0, plan.Reruns);
    }

    [Fact]
    public void A_stairs_treads_are_solid_ground_not_grass_grown_on_the_dug_floor()
    {
        // a flat pit mid-job, one eidolon: grass grown on the dug soil floor was taken for the stair's top tread, the
        // stair passed a step short of the ground beside it, and the blocks left in a corner, its only way out, were
        // held out of reach (3, 6 under them)
        var dump = MineTestWorld.FromDump("pit-grass-tread.mine");
        var site = dump.Site();
        var plan = Plan(dump, "pit-grass-tread.mine");
        Assert.Empty(plan.OutOfReach);
        Assert.NotEmpty(plan.Stair);
        Assert.All(plan.Stair, c => Assert.True(site.Solid(c.X, c.Y, c.Z), $"{c} of the stair is not solid"));
        // the stair is a way out: with all else dug, the pit's floor is out by it
        var final = dump.Site();
        foreach (var c in plan.Targets.Where(c => !plan.Stair.Contains(c)))
            final.Dig(c);
        var walk = new MineWalk(final, dump.Home, dump.Bad);
        foreach (var _ in walk.Build()) { }
        var floor = new PathCell(511986, dump.Area.Min.Y, 512041);
        Assert.True(walk.Node(floor) && walk.Out(floor), $"the pit's floor at {floor} has no way out by the stair");
    }

    [Fact]
    public void What_the_way_out_stands_on_is_told_as_the_way_out_not_as_out_of_reach()
    {
        // the same pit later: no stair is left to lay (the job has dug round the old one), the plan's stair is a corner's
        // own blocks, found only once their top step was held as out of reach (1, 4 under it)
        var dump = MineTestWorld.FromDump("pit-corner-stair.mine");
        var plan = Plan(dump, "pit-corner-stair.mine");
        Assert.Empty(plan.OutOfReach);
        Assert.Empty(plan.Held);
        Assert.Contains(new MarkPos(511990, 145, 512043), plan.WayOut);
        Assert.Subset(plan.Stair.ToHashSet(), plan.WayOut.ToHashSet());
    }
}
