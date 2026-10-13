using SeraphHorizons.Mod.Eidolon.Core;
using static SeraphHorizons.Mod.Tests.Eidolon.MineTestWorld;

namespace SeraphHorizons.Mod.Tests.Eidolon;

/// <summary>The reach bound (what any order of digging could reach) on hand-built cases, and holding: the
/// floor under what is out of reach kept, as under a block left for what it is.</summary>
public class MineReachTests
{
    // Every case is lifted L blocks: the world's bottom layer is its mantle, never mined (EidolonMining.BottomY).
    private const int L = 10;

    private static MineState Reach(MineTestWorld world, MarkArea area, PathCell home)
    {
        var state = new MineState(world.Site(area), home);
        state.BuildNow(EidolonMining.Targets(area, state.Site, _ => false).Dig);
        return state;
    }

    [Fact]
    public void AnOpenPitIsClearableAllOver()
    {
        var area = Area(0, 5, L - 3, L - 1, 0, 5);
        var state = Reach(new MineTestWorld().Ground(L - 1), area, new PathCell(-6, L, 2));
        Assert.Equal(6 * 6 * 3, state.Reach.Clearable.Count);
        Assert.Empty(state.Reach.OutOfReach);
        Assert.All(state.Reach.Clearable, c => Assert.True(state.Reach.Live(c) > 0));
    }

    [Fact]
    public void ATunnelFourHighIntoAFlatFaceIsClearableAllOver()
    {
        var world = new MineTestWorld().Ground(L - 1).Hill(0, 12, L + 9, -3, 4, L);
        var area = Area(0, 7, L, L + 3, 0, 1);
        var state = Reach(world, area, new PathCell(-6, L, 0));
        Assert.Equal(8 * 4 * 2, state.Reach.Clearable.Count);
        // the walk graph does not reach in yet: only what digging would open
        Assert.False(state.Walk.Out(new PathCell(4, L, 1)));
        Assert.True(state.Reach.Potential(new PathCell(4, L, 1)));
    }

    [Fact]
    public void ATrenchOneWideIsOutOfReachUnderItsTopLayer()
    {
        // the eidolon is 2 wide: it never stands in it, and from the ground it reaches one layer down
        var area = Area(0, 7, L - 3, L - 1, 0, 0);
        var state = Reach(new MineTestWorld().Ground(L - 1), area, new PathCell(-6, L, 0));
        Assert.Equal(Cells(0, 7, L - 1, L - 1, 0, 0).ToHashSet(), state.Reach.Clearable.ToHashSet());
        Assert.Equal(Cells(0, 7, L - 3, L - 2, 0, 0).ToHashSet(), state.Reach.OutOfReach.ToHashSet());
        // two wide, it stands in it as it goes down
        var wide = Reach(new MineTestWorld().Ground(L - 1), Area(0, 7, L - 3, L - 1, 0, 1), new PathCell(-6, L, 0));
        Assert.Empty(wide.Reach.OutOfReach);
    }

    [Fact]
    public void ALedgeOnTheHillsFlankBesideTheFaceBringsATallTopInReach()
    {
        // six high from flat ground: its top two layers are out of reach from the ground
        var world = new MineTestWorld().Ground(L - 1).Hill(0, 12, L + 9, -3, 4, L);
        var area = Area(0, 7, L, L + 5, 0, 1);
        var flat = Reach(world, area, new PathCell(-6, L, 0));
        Assert.Equal(Cells(0, 7, L + 4, L + 5, 0, 1).ToHashSet(), flat.Reach.OutOfReach.ToHashSet());
        // a ledge 2 high on the flank beside the mouth (not in front of it): from it, the face's top
        world.Box(-2, -1, L, L + 1, -3, -1);
        var ledged = Reach(world, area, new PathCell(-6, L, 0));
        Assert.Empty(ledged.Reach.OutOfReach);
    }

    // ---- holding ----

    [Fact]
    public void ACellOutOfReachHoldsItsColumnDownToTheFirstAir()
    {
        var world = new MineTestWorld().Ground(L - 1);
        world.Dig(new MarkPos(2, L - 4, 2));
        var area = Area(0, 3, L - 6, L - 1, 0, 3);
        var site = world.Site(area);
        var targets = EidolonMining.Targets(area, site, _ => false, new HashSet<MarkPos> { new(2, L - 1, 2) });
        Assert.Equal(Cells(2, 2, L - 3, L - 2, 2, 2).ToHashSet(), targets.Held.ToHashSet());
        Assert.DoesNotContain(new MarkPos(2, L - 1, 2), targets.Dig);
        Assert.DoesNotContain(new MarkPos(2, L - 1, 2), targets.Held);
        // under the air, mined again
        Assert.Contains(new MarkPos(2, L - 5, 2), targets.Dig);
    }

    [Fact]
    public void AChestStillHoldsItsColumnAndALiquidBesideOneHoldsNothing()
    {
        var world = new MineTestWorld().Ground(L - 1);
        world.Rules[new MarkPos(1, L - 1, 1)] = new MineVoxel(true, MineRule.BlockEntity);
        world.Rules[new MarkPos(2, L - 1, 2)] = new MineVoxel(true, MineRule.LiquidBeside);
        var area = Area(0, 3, L - 3, L - 1, 0, 3);
        var targets = EidolonMining.Targets(area, world.Site(area), _ => false);
        Assert.Equal(Cells(1, 1, L - 3, L - 1, 1, 1).Append(new MarkPos(2, L - 1, 2)).ToHashSet(), targets.Left.ToHashSet());
        Assert.Contains(new MarkPos(2, L - 2, 2), targets.Dig);
        Assert.Empty(targets.Held);
        // and a plan digs none of them
        var plan = world.Plan(area, new PathCell(-6, L, 2));
        Assert.Equal(targets.Left.ToHashSet(), plan.Left.ToHashSet());
        Assert.DoesNotContain(plan.Stations.SelectMany(s => s.Cells), targets.Left.Contains);
    }
}
