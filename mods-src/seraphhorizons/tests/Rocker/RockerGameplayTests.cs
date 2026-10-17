using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.Rocker.Core;
using Xunit;

namespace SeraphHorizons.Tests.Rocker;

/// <summary>The rocker's rules (#716, Rocker/Core): what it takes, the charge, rocking only while water is
/// poured, what an item gives carried from item to item, the riffles filling, the rig as gameplay reads it,
/// and the clock coming to rest level.</summary>
public class RockerGameplayTests
{
    private static RockerConfig Config() => new();

    private static IReadOnlyList<RockerYield> Concentrate(double units) => [new("seraphhorizons:concentrate-hematite", units, 5)];

    [Theory]
    [InlineData("seraphhorizons:crushedore-hematite-medium", RockerFeedKind.Crushed, "hematite", "medium")]
    [InlineData("seraphhorizons:crushedore-quartz_nativegold-poor", RockerFeedKind.Crushed, "quartz_nativegold", "poor")]
    [InlineData("seraphhorizons:groundore-galena_nativesilver-rich", RockerFeedKind.Ground, "galena_nativesilver", "rich")]
    [InlineData("game:richgravel-granite", RockerFeedKind.Placer, "granite", null)]
    public void It_takes_crushed_and_ground_ore_and_rich_gravel(string code, RockerFeedKind kind, string ore, string? grade)
    {
        var feed = RockerFeed.Parse(code);
        Assert.NotNull(feed);
        Assert.Equal((kind, ore, grade), (feed!.Kind, feed.Ore, feed.Grade));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("game:ore-medium-hematite-granite")]
    [InlineData("seraphhorizons:concentrate-hematite")]
    [InlineData("seraphhorizons:roastedore-galena-medium")]
    [InlineData("seraphhorizons:crushedore-hematite-huge")]
    [InlineData("seraphhorizons:crushedore-notanore-medium")]
    [InlineData("game:gravel-granite")]
    [InlineData("game:richgravel-layered-granite-3")]
    public void It_refuses_anything_else(string? code) => Assert.Null(RockerFeed.Parse(code));

    [Fact]
    public void A_charge_is_one_kind_up_to_the_hopper()
    {
        var s = new RockerState();
        var c = Config();
        Assert.Equal(RockerLoadVerdict.NotFeed, s.CanLoad("x", false, c, out _));
        Assert.Equal(3, s.Load("a", 3, c));
        Assert.Equal(1, s.Load("a", 5, c));
        Assert.Equal(RockerLoadVerdict.Full, s.CanLoad("a", true, c, out _));
        Assert.Equal(RockerLoadVerdict.OtherFeed, s.CanLoad("b", true, c, out _));
        Assert.Equal(4, s.Unload());
        Assert.False(s.HasCharge);
        Assert.Equal(RockerLoadVerdict.Loads, s.CanLoad("b", true, c, out int room));
        Assert.Equal(c.ChargeItems, room);
    }

    [Fact]
    public void It_rocks_only_with_a_charge_and_water()
    {
        var s = new RockerState();
        var c = Config();
        Assert.Equal(RockerStop.NoCharge, s.Stop(10, c));
        Assert.Same(RockStep.None, s.Rock(1, 10, c, () => Concentrate(5)));
        s.Load("a", 1, c);
        Assert.Equal(RockerStop.NoWater, s.Stop(0, c));
        Assert.Same(RockStep.None, s.Rock(1, 0, c, () => Concentrate(5)));
        Assert.Equal(RockerStop.None, s.Stop(0.5, c));
    }

    [Fact]
    public void An_item_takes_its_rocks_and_litres_and_gives_its_concentrate()
    {
        var s = new RockerState();
        var c = Config();
        s.Load("a", 2, c);
        // half an item: half its rocks, half its water
        var half = s.Rock(c.RocksPerItem / 2, 10, c, () => Concentrate(5 * 0.55));
        Assert.Equal(c.RocksPerItem / 2, half.Rocks, 9);
        Assert.Equal(c.LitresPerItem / 2, half.Litres, 9);
        Assert.Equal(0.5, s.ItemWork, 9);
        Assert.False(s.ChargeUntouched);
        Assert.Equal(0, s.Unload());
        // the rest of both items: 5.5 units of concentrate is one item, 0.5 carried
        var rest = s.Rock(100, 10, c, () => Concentrate(5 * 0.55));
        Assert.Equal(c.RocksPerItem * 1.5, rest.Rocks, 9);
        Assert.Equal(2, rest.Washed);
        Assert.Equal(1, rest.Caught["seraphhorizons:concentrate-hematite"]);
        Assert.Equal(0.5, s.Carry.HeldFor("seraphhorizons:concentrate-hematite"), 9);
        Assert.False(s.HasCharge);
        Assert.Equal(1, s.TakeCatch()["seraphhorizons:concentrate-hematite"]);
        Assert.False(s.HasCatch);
    }

    [Fact]
    public void The_water_runs_out_part_way()
    {
        var s = new RockerState();
        var c = Config();
        s.Load("a", 1, c);
        // a quarter of an item's water: a quarter of its rocks
        var step = s.Rock(100, c.LitresPerItem / 4, c, () => Concentrate(1));
        Assert.Equal(c.RocksPerItem / 4, step.Rocks, 9);
        Assert.Equal(0.25, s.ItemWork, 9);
        Assert.Equal(0, step.Washed);
    }

    [Fact]
    public void Full_riffles_stop_it()
    {
        var s = new RockerState();
        var c = new RockerConfig { RiffleItems = 2, ChargeItems = 4 };
        s.Load("a", 4, c);
        var step = s.Rock(100, 100, c, () => Concentrate(5));
        Assert.Equal(2, step.Washed);
        Assert.Equal(RockerStop.RifflesFull, s.Stop(100, c));
        Assert.Equal(2, s.ChargeCount);
        s.TakeCatch();
        Assert.Equal(RockerStop.None, s.Stop(100, c));
    }

    [Fact]
    public void Placer_gravel_gives_the_pans_metals_at_the_rockers_rate_and_its_stones_at_the_pans()
    {
        var yields = RockerRecovery.PlacerYield(0.55, 0.45,
            [("game:nugget-nativegold", 0.02, true), ("game:stone-granite", 0.3, false), ("game:flint", 0, false)], 8);
        Assert.Equal(2, yields.Count);
        Assert.Equal(8 * 0.02 * 0.55 / 0.45, yields[0].Units, 9);
        Assert.Equal(8 * 0.3, yields[1].Units, 9);
        Assert.All(yields, y => Assert.Equal(1, y.UnitsPerItem));
    }

    [Fact]
    public void A_pan_gives_at_most_one_item()
    {
        double[] chances = [0.5, 0.3, 0.2];
        double total = Enumerable.Range(0, 3).Sum(i => RockerRecovery.ChancePerPan(chances, i));
        Assert.True(total <= 1 + 1e-9);
        // with one drop, the chance is its own
        Assert.Equal(0.4, RockerRecovery.ChancePerPan([0.4], 0), 9);
        // a certain drop and an impossible one
        Assert.Equal(1, RockerRecovery.ChancePerPan([1, 0], 0), 9);
    }

    [Fact]
    public void A_saved_state_comes_back()
    {
        var s = new RockerState();
        s.Restore("a", 3, 0.25, [new("x", 2)], [new("x", 1.5)]);
        Assert.Equal(("a", 3, 0.25, 2, 1.5), (s.ChargeKey, s.ChargeCount, s.ItemWork, s.RiffleCount, s.Carry.HeldFor("x")));
        s.Restore(null, 3, 0.25, [], []);
        Assert.False(s.HasCharge);
    }

    [Fact]
    public void Settings_out_of_range_fall_back()
    {
        var c = new RockerConfig { RocksPerItem = 0, RocksPerSecond = float.NaN, LitresPerItem = -1, ChargeItems = 0, RiffleItems = 9999 };
        Assert.Equal(5, c.Sanitise().Count);
        Assert.Equal(RockerConfig.Defaults.RocksPerItem, c.RocksPerItem);
        Assert.Empty(new RockerConfig().Sanitise());
    }

    [Fact]
    public void The_clock_rocks_while_held_and_comes_to_rest_level()
    {
        var clock = new RockerClock();
        clock.Advance(0.3, true, 1);
        Assert.Equal(0.6 * Math.PI, clock.Theta, 9);
        clock.Advance(0.1, false, 1);
        Assert.Equal(0.8 * Math.PI, clock.Theta, 9);
        Assert.True(clock.Moving);
        clock.Advance(1, false, 1);
        Assert.Equal(Math.PI, clock.Theta, 9);
        Assert.False(clock.Moving);
        clock.Advance(1, false, 1);
        Assert.Equal(Math.PI, clock.Theta, 9);
    }

    private static string RigFile() => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "rocker-rig.json"));

    [Fact]
    public void The_shipped_rig_reads_as_a_hand_station_with_no_transfer()
    {
        var rig = RockerRig.Parse(RigFile());
        Assert.Equal(Side.North, rig.OperatorSide);
        Assert.Equal(RockerRig.CradlePart, rig.Pour.Part);
        Assert.Equal(10, rig.RockDegrees, 3);
        Assert.Equal(new Int3(-1, 0, 0), rig.TailingsCell);
        // the pour point rides the cradle: it moves as it rocks
        var still = rig.Posed(rig.Pour, 0);
        var leaning = rig.Posed(rig.Pour, Math.PI / 2);
        Assert.NotEqual(still, leaning);
        Assert.Equal(rig.Tailings, rig.Posed(new RockerPoint(rig.Tailings, null), 1));
    }

    [Theory]
    [InlineData("powerCell")]
    [InlineData("outputSide")]
    [InlineData("infeedSide")]
    [InlineData("waterCell")]
    public void A_rig_with_automated_input_or_output_is_refused(string key)
    {
        var json = RigFile().TrimEnd().TrimEnd('}') + $", \"{key}\": \"north\"}}";
        var e = Assert.Throws<FormatException>(() => RockerRig.Parse(json));
        Assert.Contains(key, e.Message);
    }
}
