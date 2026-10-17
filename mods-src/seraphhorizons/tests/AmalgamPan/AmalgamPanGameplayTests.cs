using SeraphHorizons.Mod.AmalgamPan.Core;
using SeraphHorizons.Mod.Machines.Core;
using Xunit;

namespace SeraphHorizons.Tests.AmalgamPan;

/// <summary>
/// The amalgam pan's rules (#718; AmalgamPan/Core): what goes in when, what a charge needs before it
/// works, the turns it takes, what finishing spends, which contents the model draws, its settings, and
/// its shipped rig and build file read as the gameplay reads them.
/// </summary>
public class AmalgamPanGameplayTests
{
    private static readonly AmalgamPanConfig Config = new();

    private static string File(string name) =>
        System.IO.File.ReadAllText(Path.Combine(AppContext.BaseDirectory, name));

    private static AmalgamPanBatch Charged(int items = 16, int mercury = 500, double water = 4)
    {
        var b = new AmalgamPanBatch();
        b.AddMercury(mercury);
        b.Load(items);
        b.AddWater(water);
        return b;
    }

    [Fact]
    public void Nothing_goes_in_before_the_pan_is_built()
    {
        var b = new AmalgamPanBatch();
        Assert.Equal(PanRefusal.Incomplete, b.CanPourMercury(false, Config));
        Assert.Equal(PanRefusal.Incomplete, b.CanPourWater(false, Config));
        Assert.Equal(PanRefusal.Incomplete, b.CanLoad(false, true, false, true, Config));
        Assert.Equal(PanRefusal.Incomplete, b.CanWork(false, 0, Config));
    }

    [Fact]
    public void Only_unamalgamated_free_quartz_of_one_kind_loads_up_to_a_stack()
    {
        var b = new AmalgamPanBatch();
        Assert.Equal(PanRefusal.NotFreeQuartz, b.CanLoad(true, false, false, true, Config));
        Assert.Equal(PanRefusal.AlreadyAmalgamated, b.CanLoad(true, true, true, true, Config));
        Assert.Equal(PanRefusal.None, b.CanLoad(true, true, false, true, Config));
        Assert.Equal(10, b.LoadCount(10, Config));
        b.Load(10);
        Assert.Equal(PanRefusal.OtherCharge, b.CanLoad(true, true, false, false, Config));
        Assert.Equal(6, b.LoadCount(32, Config));
        b.Load(6);
        Assert.Equal(PanRefusal.ChargeFull, b.CanLoad(true, true, false, true, Config));
    }

    [Fact]
    public void A_charge_wants_its_water_and_the_mercury_its_amalgam_takes()
    {
        var b = new AmalgamPanBatch();
        Assert.Equal(PanRefusal.NoCharge, b.CanWork(true, 0, Config));
        b.Load(16);
        Assert.Equal(PanRefusal.NoWater, b.CanWork(true, 0, Config));
        b.AddWater(3.99);
        Assert.Equal(PanRefusal.NoWater, b.CanWork(true, 0, Config));
        b.AddWater(0.01);
        Assert.Equal(PanRefusal.WaterFull, b.CanPourWater(true, Config));
        Assert.Equal(PanRefusal.NoMercury, b.CanWork(true, 40, Config));
        b.AddMercury(40);
        Assert.Equal(PanRefusal.None, b.CanWork(true, 40, Config));
    }

    [Fact]
    public void Amalgam_items_follow_the_carry_and_take_ten_portions_each()
    {
        // 16 medium gold quartz at 3 units, 45 % caught: 21.6 units, 4 amalgam of 5 and 1.6 carried
        Assert.Equal(4, AmalgamPanBatch.AmalgamItems(21.6, 0, 5));
        Assert.Equal(4, AmalgamPanBatch.AmalgamItems(21.6, 1.6, 5));
        Assert.Equal(5, AmalgamPanBatch.AmalgamItems(21.6, 3.4, 5));
        Assert.Equal(40, AmalgamPanBatch.MercuryNeeded(4, 10));
        Assert.Equal(0, AmalgamPanBatch.MercuryNeeded(0, 10));
    }

    [Fact]
    public void A_full_charge_takes_its_turns_and_finishing_spends_mercury_and_water()
    {
        var b = Charged();
        Assert.Equal(16, Config.TurnsFor(16));
        Assert.False(b.Advance(15.5, Config));
        Assert.False(b.Untouched);
        Assert.Equal(PanRefusal.Busy, b.CanLoad(true, true, false, true, Config));
        Assert.Equal(0, b.Unload());
        Assert.True(b.Advance(0.5, Config));
        var yield = b.Finish(4, 10);
        Assert.Equal(new PanYield(4, 40), yield);
        Assert.Equal(460, b.Mercury);
        Assert.Equal(0, b.Water);
        Assert.True(b.Worked);
        Assert.Equal(PanRefusal.Worked, b.CanWork(true, 0, Config));
        Assert.Equal(PanRefusal.Worked, b.CanLoad(true, true, false, true, Config));
        b.Empty();
        Assert.False(b.Worked);
        Assert.Equal(0, b.Items);
        Assert.Equal(460, b.Mercury);
    }

    [Fact]
    public void By_hand_a_full_charge_takes_24_seconds()
    {
        Assert.Equal(24, Config.TurnsFor(Config.ChargeItems) / Config.HandTurnsPerSecond, 6);
    }

    [Fact]
    public void An_untouched_charge_comes_back_and_mercury_only_with_no_charge()
    {
        var b = Charged(items: 5);
        Assert.Equal(0, b.TakeMercury(100));
        Assert.Equal(5, b.Unload());
        Assert.Equal(100, b.TakeMercury(100));
        Assert.Equal(400, b.Mercury);
        Assert.Equal(400, b.TakeMercury(1000));
        Assert.Equal(PanRefusal.None, b.CanPourMercury(true, Config));
        b.AddMercury(500);
        Assert.Equal(PanRefusal.MercuryFull, b.CanPourMercury(true, Config));
    }

    [Fact]
    public void The_model_draws_one_content_at_a_time()
    {
        var b = new AmalgamPanBatch();
        Assert.DoesNotContain(AmalgamPanRequires.Known.Where(AmalgamPanRequires.IsContents), b.Shows);
        b.AddMercury(10);
        Assert.Equal(["mercury"], Shown(b));
        b.Load(4);
        Assert.Equal(["slurry"], Shown(b));
        b.AddWater(4);
        Assert.Equal(["slurry", "water"], Shown(b));
        b.Advance(4, Config);
        b.Finish(1, 10);
        Assert.Equal(["amalgam"], Shown(b));
        b.Empty();
        Assert.Equal([], Shown(b));
    }

    private static List<string> Shown(AmalgamPanBatch b) =>
        new[] { "mercury", "slurry", "water", "amalgam" }.Where(b.Shows).ToList();

    [Fact]
    public void A_saved_batch_restores_and_bad_figures_are_cleaned()
    {
        var b = AmalgamPanBatch.Restore(-3, 8, double.NaN, -1, true);
        Assert.Equal((0, 8, 0.0, 0.0, true), (b.Mercury, b.Items, b.Water, b.Turns, b.Worked));
        Assert.False(AmalgamPanBatch.Restore(0, 0, 0, 0, true).Worked);
    }

    [Fact]
    public void Settings_out_of_range_fall_back_to_their_defaults()
    {
        Assert.Empty(new AmalgamPanConfig().Sanitise());
        var c = new AmalgamPanConfig { ChargeItems = 0, TurnsPerItem = -1, HandTurnsPerSecond = double.NaN, MercuryCapacityPortions = 0 };
        Assert.Equal(4, c.Sanitise().Count);
        Assert.Equal(AmalgamPanConfig.Defaults.ChargeItems, c.ChargeItems);
        Assert.Equal(AmalgamPanConfig.Defaults.HandTurnsPerSecond, c.HandTurnsPerSecond);
    }

    [Fact]
    public void The_hand_clock_turns_while_held_and_the_axle_sets_the_angle()
    {
        var clock = new AmalgamPanClock();
        clock.Advance(1.5f, null, true, 2.0 / 3);
        Assert.Equal(2 * Math.PI, clock.Theta, 6);
        clock.Advance(1f, null, false, 2.0 / 3);
        Assert.Equal(2 * Math.PI, clock.Theta, 6);
        clock.Advance(0.1f, 1.25, false, 2.0 / 3);
        Assert.Equal(1.25, clock.Theta, 6);
    }

    [Fact]
    public void The_shipped_rig_parses_as_the_gameplay_reads_it()
    {
        var rig = AmalgamPanRig.Parse(File("amalgampan-rig.json"));
        Assert.Equal(Int3.Zero, rig.Cell.Pos);
        Assert.Equal(Side.North, rig.OperatorSide);
        Assert.Equal(["muller", "shoes", "mercury", "slurry", "water", "amalgam", "frame"], rig.MovingParts.Parts.Select(p => p.Id));
        Assert.Throws<FormatException>(() => AmalgamPanRig.Parse(File("amalgampan-rig.json").Replace("\"down\"", "\"up\"")));
    }

    [Fact]
    public void The_build_file_is_the_muller_then_the_shoes_and_agrees_with_the_rig()
    {
        var build = MachineBuildJson.Parse(File("builds/amalgampan.json"));
        Assert.Equal("amalgampan", build.Machine);
        Assert.Equal(FeedMode.Batch, build.Feed);
        Assert.Equal(["muller", "shoes"], build.Stages.Select(s => s.Id));
        Assert.Empty(build.Wearing);
        using var rig = RigJson.Parse(File("amalgampan-rig.json"));
        Assert.Empty(MachineBuildJson.CheckAgainstRig(build, rig.RootElement));
        var assembly = new MachineAssembly(build);
        Assert.Equal(AssemblyVerdict.OutOfOrder, assembly.Fit("game:metalplate-iron", 1).Verdict);
        Assert.Equal(AssemblyVerdict.NotAPart, assembly.Fit("game:rod-copper", 1).Verdict);
        Assert.Equal(AssemblyVerdict.Fitted, assembly.Fit("game:rod-meteoriciron", 1).Verdict);
        Assert.True(assembly.Shows("muller"));
        Assert.False(assembly.Shows("shoes"));
        Assert.Equal(AssemblyVerdict.NotAPart, assembly.Fit("game:metalplate-copper", 1).Verdict);
        Assert.Equal(AssemblyVerdict.Fitted, assembly.Fit("game:metalplate-steel", 1).Verdict);
        Assert.True(assembly.Built);
        Assert.Equal(["game:rod-meteoriciron", "game:metalplate-steel"], assembly.Returns().Select(d => d.Code));
    }
}
