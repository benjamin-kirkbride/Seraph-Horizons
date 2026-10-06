using SeraphHorizons.Mod.Machines.Core;

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
        foreach (var machine in Enum.GetValues<OilMachine>())
            Assert.Equal(1000f, c.For(machine).Tank);
        Assert.Equal(0.1f, c.HelveHammer.DrainPerJob);
        Assert.Equal(0.5f, c.Pulverizer.DrainPerJob);
        Assert.Equal(2f, c.Sawmill.DrainPerJob);
        Assert.Equal(1f, c.Chopper.DrainPerJob);
        Assert.Equal(2f, c.BuckingMill.DrainPerJob);
        Assert.Equal(2f, c.Rosser.DrainPerJob);
        Assert.Empty(c.Sanitise());
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
}
