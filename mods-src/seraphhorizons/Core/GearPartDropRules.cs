namespace SeraphHorizons.Mod.Core;

/// <summary>
/// BetterLoot+'s gear parts (<c>GearPartsRemoved</c>): which loot drops are gear parts, and what
/// each becomes. Four parts made one rusty gear, so a drop of parts becomes a drop of rusty gears at
/// a quarter of its average (and of its variance): the same rusty gears, on average, come in.
/// Game-independent, so tests/ runs it without the game.
/// </summary>
public static class GearPartDropRules
{
    /// <summary>BetterLoot+'s gear part item.</summary>
    public const string GearPart = "betterloot:gearpart";

    /// <summary>What a gear part drop becomes.</summary>
    public const string RustyGear = "game:gear-rusty";

    /// <summary>Gear parts in a rusty gear (BetterLoot+'s <c>recipes/grid/rustygear.json</c>).</summary>
    public const int PartsPerGear = 4;

    /// <summary>Whether a drop of this code is a gear part. Case and surrounding spaces do not
    /// matter, as BetterLoot+ trims the code and the game lowercases it; a code without a domain is
    /// in <c>game</c>, so never a gear part.</summary>
    public static bool IsGearPart(string? code) =>
        code != null && string.Equals(code.Trim(), GearPart, StringComparison.OrdinalIgnoreCase);

    /// <summary>The rusty gear drop that replaces a gear part drop of this average and variance.</summary>
    public static (string Code, double Avg, double Var) Replacement(double avg, double var) =>
        (RustyGear, avg / PartsPerGear, var / PartsPerGear);
}
