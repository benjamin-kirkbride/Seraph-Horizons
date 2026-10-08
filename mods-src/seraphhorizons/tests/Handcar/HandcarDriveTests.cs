using SeraphHorizons.Mod.Handcar.Core;
using Xunit;

namespace SeraphHorizons.Tests.Handcar;

/// <summary>The handcar's drive (Handcar/Core/HandcarDrive.cs): who pumps, which way, how fast, and
/// what it costs; the branch selector; the settings.</summary>
public class HandcarDriveTests
{
    private static readonly HandcarConfig Config = new();

    // The rear seat faces the car's front (+1), the front seat its back (-1).
    private static SeatInput Rear(bool w = false, bool s = false, bool a = false, bool d = false) => new(true, w, s, a, d, 1);
    private static SeatInput Front(bool w = false, bool s = false, bool a = false, bool d = false) => new(true, w, s, a, d, -1);
    private static readonly SeatInput Empty = new(false, false, false, false, false, 1);

    [Fact]
    public void A_rider_pushes_the_car_the_way_they_face_with_forward_and_the_other_way_with_back()
    {
        Assert.Equal(1, HandcarDrive.PumpDirection(Rear(w: true)));
        Assert.Equal(-1, HandcarDrive.PumpDirection(Rear(s: true)));
        Assert.Equal(-1, HandcarDrive.PumpDirection(Front(w: true)));
        Assert.Equal(1, HandcarDrive.PumpDirection(Front(s: true)));
        // both keys, or none, is no pumping; nor is an empty seat's
        Assert.Equal(0, HandcarDrive.PumpDirection(Rear(w: true, s: true)));
        Assert.Equal(0, HandcarDrive.PumpDirection(Rear()));
        Assert.Equal(0, HandcarDrive.PumpDirection(Empty with { Forward = true }));
    }

    [Fact]
    public void Nobody_pumping_coasts()
    {
        var c = HandcarDrive.Command([Rear(), Empty], speed: 3, motion: 1, weight: 2, selfWeight: 2, Config);
        Assert.Equal(0, c.Throttle);
        Assert.Equal(0, c.Pumpers);
        Assert.False(c.Locked);
    }

    [Fact]
    public void One_rider_pumps_to_the_solo_top_speed_two_to_the_pair_speed()
    {
        var one = HandcarDrive.Command([Rear(w: true), Front()], 0, 1, 2, 2, Config);
        Assert.Equal(1, one.Throttle);
        Assert.Equal(1, one.Pumpers);
        Assert.Equal(Config.TopSpeedOne, one.TopSpeed, 6);
        Assert.Equal(Config.AccelerationOne, one.Acceleration, 6);
        // the front rider pumping back pushes the car forward too: they help
        var two = HandcarDrive.Command([Rear(w: true), Front(s: true)], 0, 1, 2, 2, Config);
        Assert.Equal(1, two.Throttle);
        Assert.Equal(2, two.Pumpers);
        Assert.Equal(Config.TopSpeedTwo, two.TopSpeed, 6);
        Assert.Equal(Config.AccelerationTwo, two.Acceleration, 6);
        // and both the other way
        var back = HandcarDrive.Command([Rear(s: true), Front(w: true)], 0, 1, 2, 2, Config);
        Assert.Equal(-1, back.Throttle);
        Assert.Equal(2, back.Pumpers);
    }

    [Fact]
    public void Two_riders_pumping_against_each_other_brake_the_car_and_hold_it()
    {
        var rolling = HandcarDrive.Command([Rear(w: true), Front(w: true)], speed: 2, motion: 1, 2, 2, Config);
        Assert.True(rolling.Locked);
        Assert.Equal(-1, rolling.Throttle);          // against the motion: Yang brakes to a stop
        var rollingBack = HandcarDrive.Command([Rear(w: true), Front(w: true)], speed: 2, motion: -1, 2, 2, Config);
        Assert.Equal(1, rollingBack.Throttle);
        var still = HandcarDrive.Command([Rear(w: true), Front(w: true)], speed: 0.01, motion: 1, 2, 2, Config);
        Assert.True(still.Locked);
        Assert.Equal(0, still.Throttle);             // stopped: no throttle, so Yang does not turn the drive round
    }

    [Fact]
    public void Load_costs_top_speed_but_never_below_the_least()
    {
        Assert.Equal(Config.TopSpeedOne - Config.SpeedLossPerWeight, HandcarDrive.TopSpeed(1, 3, 2, Config), 6);
        Assert.Equal(Config.TopSpeedTwo - 4 * Config.SpeedLossPerWeight, HandcarDrive.TopSpeed(2, 6, 2, Config), 6);
        Assert.Equal(Config.MinTopSpeed, HandcarDrive.TopSpeed(1, 40, 2, Config), 6);
        Assert.Equal(0, HandcarDrive.TopSpeed(0, 2, 2, Config));
        // a lighter convoy than the bare car (it cannot be) costs nothing
        Assert.Equal(Config.TopSpeedOne, HandcarDrive.TopSpeed(1, 1, 2, Config), 6);
    }

    /// <summary>The reference: Yang's primitive engine cart (enginecart-primitive: RawPowerNPer100C 1,
    /// MaxTemperatureC 800, RailVehicle.SelfWeight 1) tops out at 1 x 800/100 - 1 = 7 blocks a second
    /// on its own (SteamPowered.EngineCartDrive.TryComputeLoadedKinematicLimits), and accelerates at
    /// 0.1 x 1 x 8 = 0.8. One rider makes 60 % of that speed, two 85 %, each below the engine cart's pull.</summary>
    [Fact]
    public void The_defaults_are_60_and_85_percent_of_the_primitive_engine_cart()
    {
        const double engineCart = 1.0 * 800 / 100 - 1;
        Assert.Equal(7.0, engineCart);
        Assert.Equal(0.60 * engineCart, HandcarConfig.Defaults.TopSpeedOne, 4);
        Assert.Equal(0.85 * engineCart, HandcarConfig.Defaults.TopSpeedTwo, 4);
        Assert.True(HandcarConfig.Defaults.AccelerationOne < HandcarConfig.Defaults.AccelerationTwo);
        Assert.True(HandcarConfig.Defaults.AccelerationTwo < 0.8 * 1.25);
    }

    /// <summary>Sprinting costs the hunger behaviour 1.5 satiety a second on top of the base rate, times
    /// the calendar's speed (EntityBehaviorHunger.OnGameTick: 1.5 x sprint seconds, then x speed of time x
    /// calendar speed multiplier / 30, then / 10 into ReduceSaturation, whose loss is x 10; ConsumeSaturation(a)
    /// is ReduceSaturation(a / 10)). Pumping charges the same.</summary>
    [Fact]
    public void Pumping_costs_what_sprinting_does()
    {
        // the game's default calendar: 60 times speed of time, 0.5 multiplier: the factor is 1
        Assert.Equal(15.0, HandcarDrive.Satiety(10, HandcarConfig.Defaults.SatietyPerPumpSecond, 60, 0.5), 9);
        Assert.Equal(30.0, HandcarDrive.Satiety(10, 1.5, 120, 0.5), 9);
        Assert.Equal(0.0, HandcarDrive.Satiety(-1, 1.5, 60, 0.5));
    }

    [Fact]
    public void Left_and_right_step_the_branch_selector_as_the_rider_faces()
    {
        // the rear rider faces the front: left is left
        Assert.Equal(-1, HandcarTurn.Step(left: true, right: false, facing: 1));
        Assert.Equal(1, HandcarTurn.Step(false, true, 1));
        // the front rider faces back: their left is the car's right
        Assert.Equal(1, HandcarTurn.Step(true, false, -1));
        Assert.Equal(-1, HandcarTurn.Step(false, true, -1));
        Assert.Equal(0, HandcarTurn.Step(true, true, 1));
        Assert.Equal(HandcarTurn.Left, HandcarTurn.Move(HandcarTurn.Straight, -1));
        Assert.Equal(HandcarTurn.Left, HandcarTurn.Move(HandcarTurn.Left, -1));
        Assert.Equal(HandcarTurn.Straight, HandcarTurn.Move(HandcarTurn.Right, -1));
        Assert.Equal(HandcarTurn.Right, HandcarTurn.Move(7, 0));
    }

    [Fact]
    public void Settings_out_of_range_fall_back_to_the_defaults()
    {
        var c = new HandcarConfig { TopSpeedOne = -1, AccelerationTwo = float.NaN, BrakeDeceleration = 0, PumpFadeSeconds = 99 };
        var fixes = c.Sanitise();
        Assert.Equal(4, fixes.Count);
        Assert.Equal(HandcarConfig.Defaults.TopSpeedOne, c.TopSpeedOne);
        Assert.Equal(HandcarConfig.Defaults.AccelerationTwo, c.AccelerationTwo);
        Assert.Equal(HandcarConfig.Defaults.BrakeDeceleration, c.BrakeDeceleration);
        Assert.Equal(HandcarConfig.Defaults.PumpFadeSeconds, c.PumpFadeSeconds);
        var slow = new HandcarConfig { TopSpeedOne = 5, TopSpeedTwo = 4, MinTopSpeed = 6 };
        Assert.Equal(2, slow.Sanitise().Count);
        Assert.Equal(5, slow.TopSpeedTwo);
        Assert.Equal(5, slow.MinTopSpeed);
        Assert.Empty(new HandcarConfig().Sanitise());
    }
}
