using System.Text.Json;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.Ore.Core;

namespace SeraphHorizons.Mod.Tests;

public class MachineOilTests
{
    [Fact]
    public void A_new_tank_is_empty_and_dry()
    {
        var tank = OilTank.Empty(1000);
        Assert.True(tank.Dry);
        Assert.Equal(0, tank.Points);
        Assert.Equal(1000, tank.Room);
    }

    [Fact]
    public void Any_oil_at_all_is_not_dry()
    {
        var tank = OilTank.Empty(1000).Fill(0.01);
        Assert.False(tank.Dry);
        Assert.True(tank.Drain(0.01).Dry);
    }

    [Fact]
    public void Filling_stops_at_the_capacity_and_draining_at_empty()
    {
        var tank = OilTank.Empty(1000).Fill(700).Fill(700);
        Assert.Equal(1000, tank.Points);
        Assert.Equal(0, tank.Room);
        tank = tank.Drain(400).Drain(900);
        Assert.Equal(0, tank.Points);
        Assert.True(tank.Dry);
        // negative amounts do nothing
        Assert.Equal(0, tank.Fill(-5).Points);
        Assert.Equal(0, tank.Drain(-5).Points);
    }

    [Fact]
    public void A_smaller_tank_keeps_only_what_fits()
    {
        var tank = new OilTank(800, 1000).WithCapacity(500);
        Assert.Equal(500, tank.Points);
        Assert.Equal(500, tank.Capacity);
        Assert.Equal(300, new OilTank(300, 1000).WithCapacity(500).Points);
    }

    [Theory]
    [InlineData(0.17f, true, 3f, 0.51f)]
    [InlineData(0.17f, false, 3f, 0.17f)]
    [InlineData(0.125f, true, 1f, 0.125f)]
    public void Dry_multiplies_the_load(float resistance, bool dry, float multiplier, float expected)
    {
        var tank = dry ? OilTank.Empty(10) : new OilTank(1, 10);
        Assert.Equal(expected, tank.Resistance(resistance, multiplier), 5);
    }

    [Fact]
    public void Only_whole_items_go_in_as_many_as_fit()
    {
        var tank = new OilTank(990, 1000);
        // a 100-per-litre oil is a point an item
        Assert.Equal(10, tank.ItemsThatFit(OilTank.PointsPerItem(100), 1000));
        Assert.Equal(4, tank.ItemsThatFit(OilTank.PointsPerItem(100), 4));
        // hardened lard is 5 to the litre: 20 points an item, none fits in 10 points of room
        Assert.Equal(0, tank.ItemsThatFit(OilTank.PointsPerItem(5), 32));
        Assert.Equal(50, OilTank.Empty(1000).ItemsThatFit(OilTank.PointsPerItem(5), 1000));
        // a full bucket of oil (10 L, 1000 items) fills an empty 1000 point tank exactly
        Assert.Equal(1000, OilTank.Empty(1000).ItemsThatFit(1, 1000));
        Assert.Equal(0, tank.ItemsThatFit(0, 10));
        Assert.Equal(0, OilTank.PointsPerItem(0));
    }

    [Theory]
    [InlineData(0, 2, 0)]
    [InlineData(5, 2, 10)]
    [InlineData(5, 0.5, 3)]
    [InlineData(4, 0.5, 2)]
    [InlineData(3, 0, 0)]
    public void A_trunk_costs_its_stored_logs_rounded_up(int logs, double perLog, double expected) =>
        Assert.Equal(expected, OilDrain.PerTrunk(logs, perLog));

    [Fact]
    public void Codes_match_with_wildcards_and_the_game_domain()
    {
        var codes = new OilCodes(["game:oilportion-*", "expandedfoods:foodoilportion-*", " ", null, "Expandedfoods:Lard"]);
        Assert.Equal(3, codes.Patterns.Count);
        Assert.True(codes.Matches("game:oilportion-flax"));
        Assert.True(codes.Matches("oilportion-olive"));
        Assert.True(codes.Matches("expandedfoods:foodoilportion-walnut"));
        Assert.True(codes.Matches("expandedfoods:lard"));
        Assert.False(codes.Matches("game:waterportion"));
        Assert.False(codes.Matches("expandedfoods:lardsomething"));
        Assert.False(codes.Matches(null));
    }

    [Fact]
    public void Defaults_are_the_documented_ones()
    {
        var c = new MachineOilConfig();
        Assert.Equal(["game:oilportion-*", "expandedfoods:foodoilportion-*", "expandedfoods:lard", "expandedfoods:hardlardliquid"], c.OilLiquids);
        Assert.Equal(0.5f, c.LumpLitres("game:fat-rendered"));
        Assert.Equal(0.5f, c.LumpLitres("fat-rendered"));
        Assert.Equal(0f, c.LumpLitres("game:fat"));
        Assert.Equal(3f, c.DryResistanceMultiplier);
        foreach (var machine in Workshop)
            Assert.Equal(1000f, c.For(machine).Tank);
        Assert.Equal(0.1f, c.HelveHammer.DrainPerJob);
        Assert.Equal(0.5f, c.Pulverizer.DrainPerJob);
        Assert.Equal(2f, c.Sawmill.DrainPerJob);
        Assert.Equal(1f, c.Chopper.DrainPerJob);
        Assert.Equal(2f, c.BuckingMill.DrainPerJob);
        Assert.Equal(2f, c.Rosser.DrainPerJob);
        Assert.Equal(10f, c.GearCutter.DrainPerJob);
        Assert.Equal(2f, c.DrawBench.DrainPerJob);
        Assert.Equal(10f, c.CylinderBorer.DrainPerJob);
        Assert.Empty(c.Sanitise());
    }

    /// <summary>The machines whose tank is one bucket; the ore machines' are scaled by load.</summary>
    private static readonly OilMachine[] Workshop =
    [
        OilMachine.HelveHammer, OilMachine.Pulverizer, OilMachine.Sawmill, OilMachine.Chopper, OilMachine.BuckingMill,
        OilMachine.Rosser, OilMachine.GearCutter, OilMachine.DrawBench, OilMachine.CylinderBorer,
    ];

    private static readonly OreRecovery Recovery = new(JsonSerializer.Deserialize<OreProcessingConfig>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ore-processing.json")),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })!);

    // The ore machines' defaults are scaled by load from the shipped ore-processing.json (each machine's
    // README, "Oil"): a tank of 5000 points a unit of working load, and 2 points a job a unit of load and a
    // shaft turn of the job, rounded; so each runs the same hours on a full tank, and a stamp battery
    // drinks more than a jig.
    [Theory]
    [InlineData(OilMachine.JawBreaker, OreMachine.JawBreaker)]
    [InlineData(OilMachine.StampBattery, OreMachine.StampBattery)]
    [InlineData(OilMachine.Arrastra, OreMachine.Arrastra)]
    [InlineData(OilMachine.BallMill, OreMachine.BallMill)]
    [InlineData(OilMachine.Jig, OreMachine.Jig)]
    [InlineData(OilMachine.ShakingTable, OreMachine.Table)]
    [InlineData(OilMachine.Vanner, OreMachine.Vanner)]
    [InlineData(OilMachine.ReverberatoryRoaster, OreMachine.Reverberatory)]
    [InlineData(OilMachine.ParkesKettle, OreMachine.ParkesKettle)]
    public void An_ore_machines_oil_is_scaled_by_its_load(OilMachine machine, OreMachine ore)
    {
        var spec = Recovery.Machine(ore);
        // a job: an item (a rock, or a roaster's item of weight 1); the arrastra's a rock of its charge, the kettle's a charge
        double turns = machine switch
        {
            OilMachine.Arrastra => spec.TurnsPerCharge / spec.ChargeItems,
            OilMachine.ParkesKettle => spec.TurnsPerCharge,
            _ => spec.TurnsPerItem,
        };
        var entry = new MachineOilConfig().For(machine);
        Assert.Equal(5000 * spec.Load, entry.Tank, 6);
        double drain = 2 * spec.Load * turns;
        Assert.True(Math.Abs(entry.DrainPerJob - drain) <= 0.05 * drain, $"{machine}: {entry.DrainPerJob} a job, {drain:0.###} by its load");
    }

    [Fact]
    public void A_stamp_battery_drinks_more_than_a_jig()
    {
        var c = new MachineOilConfig();
        Assert.True(c.StampBattery.DrainPerJob > 10 * c.Jig.DrainPerJob);
        Assert.True(c.BallMill.DrainPerJob > c.StampBattery.DrainPerJob);
    }

    // The README's arithmetic: a full tank (one bucket) lasts this many jobs.
    [Fact]
    public void A_full_tank_lasts_the_documented_number_of_jobs()
    {
        var c = new MachineOilConfig();
        Assert.Equal(10000, OilDrain.JobsPerTank(c.HelveHammer.Tank, c.HelveHammer.DrainPerJob), 3);
        Assert.Equal(2000, OilDrain.JobsPerTank(c.Pulverizer.Tank, c.Pulverizer.DrainPerJob), 3);
        Assert.Equal(500, OilDrain.JobsPerTank(c.Sawmill.Tank, c.Sawmill.DrainPerJob), 3);
        Assert.Equal(1000, OilDrain.JobsPerTank(c.Chopper.Tank, c.Chopper.DrainPerJob), 3);
        Assert.Equal(500, OilDrain.JobsPerTank(c.BuckingMill.Tank, c.BuckingMill.DrainPerJob), 3);
        Assert.Equal(500, OilDrain.JobsPerTank(c.Rosser.Tank, c.Rosser.DrainPerJob), 3);
        Assert.True(double.IsPositiveInfinity(OilDrain.JobsPerTank(1000, 0)));
    }

    [Fact]
    public void Out_of_range_values_fall_back_to_the_defaults()
    {
        var c = new MachineOilConfig
        {
            DryResistanceMultiplier = 0.5f,
            HelveHammer = new OilMachineConfig(-1, 0.1f),
            Pulverizer = new OilMachineConfig(100, 500),
            Sawmill = null!,
            Chopper = new OilMachineConfig(float.NaN, float.PositiveInfinity),
            OilLumps = new() { ["game:fat-rendered"] = 0, ["game:fat"] = 1 },
            OilLiquids = null!,
        };
        var fixes = c.Sanitise();
        Assert.Equal(7, fixes.Count);
        Assert.Equal(3f, c.DryResistanceMultiplier);
        Assert.Equal(1000f, c.HelveHammer.Tank);
        Assert.Equal(0.5f, c.Pulverizer.DrainPerJob);
        Assert.Equal(2f, c.Sawmill.DrainPerJob);
        Assert.Equal(1000f, c.Chopper.Tank);
        Assert.Equal(1f, c.Chopper.DrainPerJob);
        Assert.Equal(["game:fat"], c.OilLumps.Keys);
        Assert.Empty(c.OilLiquids);
    }

    [Theory]
    [InlineData(0, "0")]
    [InlineData(-0.0, "0")]
    [InlineData(1e-12, "0")]
    [InlineData(0.001, "1")]
    [InlineData(199.5, "200")]
    [InlineData(1000, "1000")]
    public void Points_are_shown_whole_rounded_up_and_never_minus_zero(double points, string shown) =>
        Assert.Equal(shown, OilText.Points(points));

    [Theory]
    [InlineData(0.085f, "8.5")]
    [InlineData(0.255f, "25.5")]
    [InlineData(0.17f, "17")]
    [InlineData(0.51f, "51")]
    [InlineData(0.005f, "0.5")]
    [InlineData(0f, "0")]
    [InlineData(3f, "300")]
    public void Loads_are_shown_in_kN_as_the_game_scales_torque_to_one_place(float load, string shown) =>
        Assert.Equal(shown, OilText.Load(load));
}
