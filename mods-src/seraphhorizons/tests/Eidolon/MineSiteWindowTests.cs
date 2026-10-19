using SeraphHorizons.Mod.Eidolon.Core;
using Xunit.Abstractions;

namespace SeraphHorizons.Mod.Tests.Eidolon;

/// <summary>The job's window grown round members standing outside it (<see cref="MineSite.Grown"/>; README
/// "Eidolon", working together, *Routes*): a member 10 columns off the area, out of the window, is placed and
/// routed on the first solve over the grown window; one farther than <see cref="MineSite.GrowColumns"/> is not
/// taken in; and a member that cannot be placed says why.</summary>
public class MineSiteWindowTests(ITestOutputHelper output)
{
    private const int L = MineCases.L;

    // a pit 12 × 8, 3 deep, in wide flat ground; one member 3 columns west of it (in the window), one 10 (out of it)
    private static readonly MarkArea Pit = MineTestWorld.Area(0, 11, L - 3, L - 1, 0, 7);
    private static readonly PathCell Near = new(-3, L, 3), Far = new(-10, L, 3);

    private static MineTestWorld World() => new MineTestWorld().Ground(L - 1, bottom: L - 6, from: -60, to: 60);

    private static (MinePlan Plan, MineState Live) Begin(MineTestWorld world, MarkArea window)
    {
        var plan = MinePlanner.Run(new MinePlanInput(new MineState(world.Site(Pit, window: window), Near), Near, _ => false, new HashSet<PathCell>()));
        var live = new MineState(world.Site(Pit, window: window), Near);
        live.BuildNow(plan.Targets);
        live.Bands = plan.Bands(live.Remaining);
        return (plan, live);
    }

    private static MineRoutes Solve(MinePlan plan, MineState live) =>
        MineRoutePlanner.Solve(new MineRouteInput(plan, 1, live.Clone(), [new MineRouteMember(1, Near, null, false, 0), new MineRouteMember(2, Far, null, false, 0)],
            [], 0, null, null) { Keep = 0 });

    private static int Stations(MineRoutes routes, long member) => routes.Routes[member].Stops.Count(s => s.Kind == MineStopKind.Station);

    [Fact]
    public void A_member_ten_columns_off_the_area_is_outside_the_window_and_gets_no_stops()
    {
        var window = MineSite.Around(Pit);
        Assert.False(MineSite.Holds(window, Far));
        Assert.True(MineSite.Holds(window, Near));
        var (plan, live) = Begin(World(), window);
        string? why = MineCosts.Unplaced(live, Far);
        output.WriteLine(why);
        Assert.NotNull(why);
        Assert.Contains("outside the window", why);
        Assert.Contains("10 columns from the area", why);
        Assert.Null(MineCosts.Unplaced(live, Near));
        var routes = Solve(plan, live);
        Assert.Equal(0, Stations(routes, 2));
    }

    [Fact]
    public void Grown_round_it_the_member_gets_stops_on_the_first_solve()
    {
        var window = MineSite.Grown(Pit, MineSite.Around(Pit), [Near, Far]);
        output.WriteLine($"window {window}");
        Assert.True(MineSite.Holds(window, Far));
        Assert.Equal((MineSite.Around(Pit).Max.X, MineSite.Around(Pit).Max.Z), (window.Max.X, window.Max.Z));
        Assert.Equal(Far.X - 1 - MineSite.SideMargin, window.Min.X);
        var (plan, live) = Begin(World(), window);
        Assert.Null(MineCosts.Unplaced(live, Far));
        var routes = Solve(plan, live);
        output.WriteLine($"stops {Stations(routes, 1)}/{Stations(routes, 2)}");
        Assert.True(Stations(routes, 1) > 0);
        Assert.True(Stations(routes, 2) > 0);
    }

    [Fact]
    public void The_window_grows_only_for_members_within_reach_of_the_area_and_never_shrinks()
    {
        var around = MineSite.Around(Pit);
        // in it already: unchanged
        Assert.Equal(around, MineSite.Grown(Pit, around, [Near]));
        // farther than GrowColumns: not taken in, but named
        var far = new List<PathCell>();
        var beyond = new PathCell(Pit.Min.X - MineSite.GrowColumns - 1, L, 3);
        Assert.Equal(around, MineSite.Grown(Pit, around, [beyond], far));
        Assert.Equal([beyond], far);
        // at GrowColumns: taken in, the window bounded by it and its margins
        var edge = new PathCell(Pit.Max.X + MineSite.GrowColumns, L, Pit.Max.Z + MineSite.GrowColumns);
        var grown = MineSite.Grown(Pit, around, [edge]);
        Assert.True(MineSite.Holds(grown, edge));
        Assert.Equal(Pit.Max.X + MineSite.GrowColumns + MineSite.SideMargin, grown.Max.X);
        Assert.Equal(around.Min, grown.Min);
        // a grown window kept as it is for members now inside it
        Assert.Equal(grown, MineSite.Grown(Pit, grown, [Near]));
        var site = new MineSite(Pit, 2, grown);
        Assert.Equal(grown, site.Window);
        Assert.Equal(grown, site.Clone().Window);
    }
}
