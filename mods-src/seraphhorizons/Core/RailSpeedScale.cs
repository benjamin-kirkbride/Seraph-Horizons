namespace SeraphHorizons.Mod.Core;

/// <summary>
/// Slower rail vehicles (<c>SlowerRailVehicles</c>): Yang's Transport Tycoon's figures a vehicle's
/// top speed is made of, each scaled by the same factor, so the top speed is scaled by it.
/// Game-independent, so tests/ runs it without the game.
///
/// Yang's top speed is the least of the engine's (raw power x heat - convoy weight) and the track's
/// material cap (per gauge and material, whole blocks a second), times the track piece's own speed
/// factor; offscreen, a timetabled convoy cruises at 80 % of the same. Scaling the engine's figure and
/// the cap alike scales their least: <c>min(f x engine, f x cap) = f x min(engine, cap)</c>.
/// </summary>
public static class RailSpeedScale
{
    public const double DefaultFactor = 0.5;

    /// <summary>A usable factor: above 0, at most 1 (the switch only slows).</summary>
    public static bool ValidFactor(double factor) => factor is > 0 and <= 1;

    /// <summary><paramref name="factor"/>, or <see cref="DefaultFactor"/> when it is not usable.</summary>
    public static double Sanitize(double factor) => ValidFactor(factor) ? factor : DefaultFactor;

    /// <summary>An engine's top speed, blocks a second.</summary>
    public static double Speed(double speedBPS, double factor) => speedBPS * factor;

    /// <summary>A track's material cap, whole blocks a second as Yang keeps it: 0 (none) stays 0, any
    /// other is rounded and kept at 1 or more, so a capped track never becomes uncapped.</summary>
    public static ushort Cap(ushort capBPS, double factor) =>
        capBPS == 0 ? (ushort)0 : (ushort)Math.Clamp(Math.Round(capBPS * factor, MidpointRounding.AwayFromZero), 1, ushort.MaxValue);

    /// <summary>A steam-driven convoy's top speed on a track piece with no speed penalty, as Yang
    /// works it out (<c>EngineCartDrive.TryComputeLoadedKinematicLimits</c>, then the material cap in
    /// the vehicle's tick) from the scaled figures: the engine's raw power (per 100 °C) times its heat
    /// (°C / 100) less the convoy's weight, and the cap (0: none).</summary>
    public static double TopSpeed(double rawPowerPer100C, double temperatureC, int convoyWeight, ushort capBPS, double factor)
    {
        double engine = rawPowerPer100C * temperatureC / 100 - convoyWeight;
        double speed = engine <= 0 ? 0 : Speed(engine, factor);
        ushort cap = Cap(capBPS, factor);
        return cap > 0 ? Math.Min(speed, cap) : speed;
    }
}
