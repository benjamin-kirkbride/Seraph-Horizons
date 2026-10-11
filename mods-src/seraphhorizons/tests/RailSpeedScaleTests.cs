using SeraphHorizons.Mod.Core;

namespace SeraphHorizons.Mod.Tests;

public class RailSpeedScaleTests
{
    // Yang's Transport Tycoon 1.0.3's track caps (config/trackmaterialspeeds.json): wagon rails
    // (gauge 0) wood 8, metal 14; standard gauge (gauge 1) wood 16, metal 32.
    [Theory]
    [InlineData(8, 4)]
    [InlineData(14, 7)]
    [InlineData(16, 8)]
    [InlineData(32, 16)]
    public void Yangs_track_caps_are_halved_exactly(int cap, int halved) =>
        Assert.Equal((ushort)halved, RailSpeedScale.Cap((ushort)cap, 0.5));

    [Fact]
    public void An_uncapped_track_stays_uncapped() => Assert.Equal((ushort)0, RailSpeedScale.Cap(0, 0.5));

    [Fact]
    public void A_capped_track_is_never_scaled_to_no_cap() => Assert.Equal((ushort)1, RailSpeedScale.Cap(1, 0.1));

    // Every steam vehicle of Yang's, bare (its own weight only), at its engine's top heat, on each
    // material of its gauge: raw power, max °C, self weight, then the speeds before and after on wood
    // and metal. Yang's engine carts run on wagon rails, its locomotives on standard gauge.
    [Theory]
    [InlineData(1.0, 800, 1, 8, 14, 7, 7, 3.5, 3.5)]           // enginecart-primitive
    [InlineData(1.65, 1100, 3, 8, 14, 8, 14, 4, 7)]            // enginecart-simple (15.15 by its engine)
    [InlineData(2.65, 1200, 5, 8, 14, 8, 14, 4, 7)]            // enginecart-standard (26.8)
    [InlineData(2.0, 800, 5, 16, 32, 11, 11, 5.5, 5.5)]        // sglocomotive-primitive
    [InlineData(3.0, 1100, 5, 16, 32, 16, 28, 8, 14)]          // sglocomotive-simple
    [InlineData(4.0, 1100, 10, 16, 32, 16, 32, 8, 16)]         // sglocomotive-standard (34)
    [InlineData(5.5, 1250, 8, 16, 32, 16, 32, 8, 16)]          // sglocomotive-advanced (60.75)
    public void Every_vehicle_runs_at_half_its_top_speed(double power, double maxC, int weight, int wood, int metal,
                                                         double woodBefore, double metalBefore, double woodAfter, double metalAfter)
    {
        Assert.Equal(woodBefore, RailSpeedScale.TopSpeed(power, maxC, weight, (ushort)wood, 1), 6);
        Assert.Equal(metalBefore, RailSpeedScale.TopSpeed(power, maxC, weight, (ushort)metal, 1), 6);
        Assert.Equal(woodAfter, RailSpeedScale.TopSpeed(power, maxC, weight, (ushort)wood, 0.5), 6);
        Assert.Equal(metalAfter, RailSpeedScale.TopSpeed(power, maxC, weight, (ushort)metal, 0.5), 6);
    }

    // A loaded convoy is halved too, not only the bare engine: the primitive engine cart pulling three
    // minecarts (weight 4) goes 8 - 4 = 4 by its engine, 2 halved.
    [Fact]
    public void A_loaded_convoy_is_halved_too()
    {
        Assert.Equal(4, RailSpeedScale.TopSpeed(1.0, 800, 4, 14, 1), 6);
        Assert.Equal(2, RailSpeedScale.TopSpeed(1.0, 800, 4, 14, 0.5), 6);
    }

    [Fact]
    public void An_engine_too_weak_for_its_convoy_does_not_move() =>
        Assert.Equal(0, RailSpeedScale.TopSpeed(1.0, 800, 9, 14, 0.5));

    [Theory]
    [InlineData(0.5, 0.5)]
    [InlineData(1, 1)]
    [InlineData(0.01, 0.01)]
    [InlineData(0, 0.5)]
    [InlineData(-1, 0.5)]
    [InlineData(1.5, 0.5)]
    [InlineData(double.NaN, 0.5)]
    public void A_factor_out_of_range_falls_back_to_half(double factor, double used) =>
        Assert.Equal(used, RailSpeedScale.Sanitize(factor));
}
