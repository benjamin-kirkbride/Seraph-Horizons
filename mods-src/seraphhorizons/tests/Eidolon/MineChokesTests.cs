using SeraphHorizons.Mod.Eidolon.Core;
using Xunit.Abstractions;
using static SeraphHorizons.Mod.Tests.Eidolon.MineTestWorld;

namespace SeraphHorizons.Mod.Tests.Eidolon;

/// <summary>Narrow ways and their slots (<see cref="MineChokes"/>, <see cref="MineSlots"/>; README "Eidolon",
/// working together, *Narrow ways*): lanes and tunnels by width, stairs, an open floor, a ramp, keys that
/// hold, and a table that never lets two members into one at once.</summary>
public class MineChokesTests(ITestOutputHelper output)
{
    // Every case is lifted L blocks: the world's bottom layer is its mantle, never mined (EidolonMining.BottomY).
    private const int L = 10;

    private static MineWalk Walk(MineTestWorld world, MarkArea area, PathCell home)
    {
        var state = new MineState(world.Site(area), home);
        foreach (var _ in state.BuildWalk()) { }
        return state.Walk;
    }

    // a ridge with a covered tunnel w wide, 4 high and 8 long dug through its face
    private static (MineTestWorld World, MarkArea Area) Tunnel(int width)
    {
        var world = new MineTestWorld().Ground(L - 1).Hill(0, 12, L + 8, -4, width + 3, L);
        var area = Area(0, 7, L, L + 3, 0, width - 1);
        foreach (var c in Cells(0, 7, L, L + 3, 0, width - 1))
            world.Dig(c);
        return (world, area);
    }

    [Theory]
    [InlineData(2, 1)]
    [InlineData(3, 1)]
    [InlineData(4, 0)]
    public void A_two_wide_lane_is_one_narrow_zone_and_a_four_wide_tunnel_none(int width, int zonesInside)
    {
        var (world, area) = Tunnel(width);
        var walk = Walk(world, area, new PathCell(-6, L, 1));
        var zones = MineChokes.Find(walk, area, new HashSet<MarkPos>());
        // nodes with their box all inside the tunnel, past its mouth
        bool Inside(PathCell n) => n.X >= 2 && n.X <= 7 && n.Z >= 1 && n.Z <= width - 1 && n.Y == L;
        var inside = zones.Where(z => z.Nodes.Any(Inside)).ToList();
        output.WriteLine($"width {width}: {zones.Count} zones, {inside.Count} inside: {string.Join("; ", zones.Select(z => $"{z.Id} {z.Kind} key {z.Key} nodes {z.Nodes.Count}"))}");
        Assert.Equal(zonesInside, inside.Count);
        if (zonesInside == 1)
        {
            // the whole lane, and its mouth (the nodes whose box spans the face)
            var lane = inside[0];
            Assert.All(walk.Nodes.Where(Inside), n => Assert.Contains(n, lane.Nodes));
            Assert.Contains(lane.Nodes, n => n.X is 0 or 1);
        }
    }

    [Theory]
    [InlineData(6, 5)]
    [InlineData(8, 20)]
    public void A_straight_stair_and_a_spiral_one_are_stair_zones(int side, int depth)
    {
        int top = L + 70;
        var world = side == 6 ? new MineTestWorld().Ground(top - 1, bottom: top - 12) : new MineTestWorld().Box(-8, side + 8, 1, top - 1, -8, side + 8);
        var area = Area(0, side - 1, top - depth, top - 1, 0, side - 1);
        var home = new PathCell(-4, top, side / 2);
        var plan = world.Plan(area, home);
        Assert.NotEmpty(plan.Stair);
        PlanRunner.Run(world, area, plan, home);
        var walk = Walk(world, area, home);
        var zones = MineChokes.Find(walk, area, plan.Stair);
        output.WriteLine($"{side} x {side} x {depth}: {string.Join("; ", zones.Select(z => $"{z.Id} {z.Kind} nodes {z.Nodes.Count}"))}");
        // every node on a tread under the rim is in a stair zone
        var treads = walk.Nodes.Where(n => n.Y < top - EidolonTeamwork.FloorStep
            && plan.Stair.Any(c => c.Y == n.Y - 1 && c.X >= n.X - 1 && c.X <= n.X && c.Z >= n.Z - 1 && c.Z <= n.Z)).ToList();
        Assert.NotEmpty(treads);
        var index = new MineZoneIndex(zones);
        Assert.All(treads, n => Assert.Equal(MineZoneKind.Stair, index.Of(n)?.Kind));
    }

    [Fact]
    public void An_open_pit_floor_has_no_zone()
    {
        var world = new MineTestWorld().Ground(L - 1);
        foreach (var c in Cells(0, 15, L - 2, L - 1, 0, 11))
            world.Dig(c);
        foreach (var c in Cells(0, 15, L - 1, L - 1, -4, -1))
            world.Dig(c);
        var area = Area(0, 15, L - 2, L - 1, 0, 11);
        var walk = Walk(world, area, new PathCell(-6, L, 4));
        var zones = MineChokes.Find(walk, area, new HashSet<MarkPos>());
        output.WriteLine(string.Join("; ", zones.Select(z => $"{z.Id} {z.Kind} {string.Join(" ", z.Nodes)}")));
        Assert.Empty(zones);
    }

    [Fact]
    public void A_ramp_three_columns_wide_is_narrow()
    {
        // a cut 3 wide rising a block every 2 from the ground to the top of a hill 10 high
        var world = new MineTestWorld().Ground(L - 1).Hill(0, 24, L + 10, -4, 6, L);
        for (int x = 0; x <= 20; x++)
            foreach (var c in Cells(x, x, L + x / 2, L + 10, 0, 2))
                world.Dig(c);
        var area = Area(0, 20, L, L + 10, 0, 2);
        var walk = Walk(world, area, new PathCell(-6, L, 1));
        var zones = MineChokes.Find(walk, area, new HashSet<MarkPos>());
        var index = new MineZoneIndex(zones);
        var ramp = walk.Nodes.Where(n => n.X >= 4 && n.X <= 16 && n.Z is 1 or 2).ToList();
        Assert.NotEmpty(ramp);
        Assert.All(ramp, n => Assert.True(MineChokes.Narrow(walk, n), $"{n} is open"));
        Assert.All(ramp, n => Assert.NotNull(index.Of(n)));
        Assert.Single(ramp.Select(n => index.Of(n)!.Id).Distinct());
    }

    [Fact]
    public void Zone_keys_hold_while_the_geometry_holds()
    {
        var (world, area) = Tunnel(2);
        var state = new MineState(world.Site(area), new PathCell(-6, L, 1));
        foreach (var _ in state.BuildWalk()) { }
        var a = MineChokes.Find(state.Walk, area, new HashSet<MarkPos>());
        var b = MineChokes.Find(state.Walk, area, new HashSet<MarkPos>());
        Assert.Equal(a.Select(z => (z.Id, z.Key, z.Kind)), b.Select(z => (z.Id, z.Key, z.Kind)));
        Assert.All(a.Zip(b), p => Assert.True(p.First.Nodes.SetEquals(p.Second.Nodes)));
        // a block dug well away from the tunnel leaves its zone as it was
        var far = new MarkPos(-5, L - 1, -5);
        state.Site.Dig(far);
        state.Walk.Dig(far);
        var c = MineChokes.Find(state.Walk, area, new HashSet<MarkPos>());
        foreach (var zone in a)
        {
            var same = c.FirstOrDefault(z => z.Key == zone.Key);
            Assert.NotNull(same);
            Assert.True(same!.Nodes.SetEquals(zone.Nodes));
        }
    }

    [Fact]
    public void Slots_never_overlap()
    {
        var random = new Random(2000);
        var slots = new MineSlots();
        static bool Fits(ILookup<int, MineSlot> others, double t, IReadOnlyList<(int Zone, double Enter, double Leave)> runs) =>
            runs.All(r => others[r.Zone].All(s => t + r.Enter >= s.To + MineSlots.Gap - 1e-6 || s.From >= t + r.Leave + MineSlots.Gap - 1e-6));
        for (int k = 0; k < 2000; k++)
        {
            long member = random.Next(1, 4);
            double from = Math.Round(random.NextDouble() * 2000, 1);
            var runs = new List<(int Zone, double Enter, double Leave)>();
            double at = 0;
            foreach (int zone in Enumerable.Range(0, 4).OrderBy(_ => random.Next()).Take(random.Next(1, 4)))
            {
                double enter = Math.Round(at + random.NextDouble() * 5, 1);
                double leave = Math.Round(enter + 0.1 + random.NextDouble() * 4, 1);
                runs.Add((zone, enter, leave));
                at = leave;
            }
            double t = slots.Earliest(member, from, runs);
            Assert.False(double.IsPositiveInfinity(t));
            Assert.True(t >= from - 1e-9);
            var others = slots.All.Where(s => s.Member != member).ToLookup(s => s.Zone);
            Assert.True(Fits(others, t, runs), $"reservation {k} at {t} does not fit");
            // no earlier time fits (scanned at a tenth of a second)
            for (double u = from; u < t - 0.05; u += 0.1)
                Assert.False(Fits(others, u, runs), $"reservation {k}: {u:0.0} fits before {t:0.0}");
            slots.Reserve(member, k, t, runs);
            Assert.False(slots.Overlaps(), $"overlap after reservation {k}");
        }
    }

    [Fact]
    public void A_member_working_in_a_zone_holds_it_until_it_leaves()
    {
        var slots = new MineSlots();
        // member 1 walks into zone 0 at 2 s and stands there to work: held on
        slots.Reserve(1, 1, 0, [(0, 2, double.PositiveInfinity)]);
        Assert.True(double.IsPositiveInfinity(slots.Earliest(2, 0, [(0, 0, 3)])));
        // its next leg sets off at 30 s and leaves the zone 2 s later: free from 33.5 s
        slots.Reserve(1, 2, 30, [(0, 0, 2)]);
        Assert.Equal(30 + 2 + MineSlots.Gap, slots.Earliest(2, 0, [(0, 0, 3)]), 9);
        // held a while longer while it works there after all
        slots.Extend(1, 0, 40);
        Assert.Equal(40 + MineSlots.Gap, slots.Earliest(2, 0, [(0, 0, 3)]), 9);
        Assert.False(slots.Overlaps());
    }
}
