namespace SeraphHorizons.Mod.Ore.Core;

/// <summary>
/// Hand-tier roasting (#720; README "Ore processing: roasting in the firepit"): a sulfide's
/// concentrate cooks in the game's firepit, on any fuel, into roasted concentrate at the firepit's
/// share (<see cref="OreRecovery.Roasting"/> with <see cref="Roaster.Firepit"/>, 85 %), the fraction
/// carried over from one item to the next (<see cref="UnitCarry"/>), so a run of 20 concentrate
/// gives exactly 17. No sulfur: open roasting lost it to the air.
///
/// The firepit cooks one item at a time: the whole stack in its input slot heats together, and
/// once it is at <see cref="MeltingPoint"/>, one item turns every cook time (the game's
/// <c>meltingDuration</c>, <see cref="DefaultSeconds"/> by default, set by the config), faster in a
/// fire hotter than twice the point (the game counts the cook time by whole multiples of it).
/// </summary>
public static class OreRoasting
{
    /// <summary>The temperature a sulfide roasts at, about 600 °C; dry grass (600) and firewood
    /// (700) reach it, so any fuel works.</summary>
    public const int MeltingPoint = 600;

    /// <summary>Seconds of cooking per item at the roasting point (the config's
    /// <c>FirepitRoastSeconds</c>).</summary>
    public const double DefaultSeconds = 10;

    /// <summary>The cook time the config asks for, or the default for one that is not positive or
    /// not a number.</summary>
    public static float Seconds(double configured) =>
        (float)(configured > 0 && double.IsFinite(configured) ? configured : DefaultSeconds);

    /// <summary>
    /// Roasts one concentrate item of <paramref name="unitsIn"/> at <paramref name="share"/>: the
    /// roasted units go to <paramref name="carry"/> under <paramref name="key"/> (the firepit and the
    /// output), and the whole roasted items of <paramref name="unitsPerRoasted"/> that makes come out.
    /// </summary>
    public static int Roast(UnitCarry carry, string key, double unitsIn, double share, double unitsPerRoasted) =>
        carry.Add(key, unitsIn * Math.Clamp(share, 0, 1), unitsPerRoasted);
}
