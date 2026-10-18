using SeraphHorizons.Mod.Ore.Core;

namespace SeraphHorizons.Mod.Vanner.Core;

/// <summary>
/// The vanner's pass (#923): the last of the concentrators, on the shaking table's tailings. Each item goes
/// through <see cref="ConcentratorWash"/> at r = 0.80 (× the ore's density); on its class's whole ideal route
/// (classified, ground if poor, amalgamated if free, and worked by the sluice, jig and table before it) the
/// vanner is the final step for oxides, placer and free silver and catches all that is left; for a sulfide
/// or a by-product ore it marks its concentrate whole for the later final step (#765,
/// <see cref="OreRecovery.Concentrate"/>). Whole and plain concentrate are carried over apart
/// (<see cref="CarryKey"/>), since a whole item says so on its stack.
/// </summary>
public static class Vanning
{
    private const string WholeSuffix = "|whole";

    /// <summary>One item through the vanner.</summary>
    public static WashResult Wash(OreRecovery recovery, OreStream feed,
        Func<IEnumerable<(string Code, double ChancePerPan, bool Metal)>>? gravelDrops = null) =>
        ConcentratorWash.Wash(recovery, OreMachine.Vanner, feed, gravelDrops);

    /// <summary>The key a product's fraction is carried under: its code, apart for a whole-route concentrate.</summary>
    public static string CarryKey(string code, bool whole) => whole ? code + WholeSuffix : code;
}
