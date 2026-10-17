using SeraphHorizons.Mod.Ore.Core;

namespace SeraphHorizons.Mod.BallMill.Core;

/// <summary>What the ball mill makes of one item: the ground ore item's code and the stream it carries.</summary>
public sealed record BallMillProduct(string Code, OreStream Stream);

/// <summary>
/// The ball mill's grind (#915): one rock item in, one ground ore item out, nothing lost. The drum grinds
/// (<see cref="OreState.Ground"/>, lifting poor ore's × 0.4) and its screens classify
/// (<see cref="OreState.Classified"/>, lifting the unclassified × 0.85), through <see cref="OreRecovery.Lift"/>:
/// a stream already split (the shaking table's middlings, or tailings) has its locked units freed in
/// proportion and goes back to the head of the concentrators. It takes crushed ore and ground ore (the
/// table's middlings, or ground ore regrinding); raw ore wants crushing first, and a sulfide's roasted
/// rock is past grinding.
/// </summary>
public static class BallMillGrind
{
    /// <summary>What the drum and screens add.</summary>
    public const OreState Adds = OreState.Ground | OreState.Classified;

    /// <summary>Whether the mill takes an item of this form carrying this stream.</summary>
    public static bool Takes(OreForm? form, OreStream? stream) =>
        form is OreForm.Crushed or OreForm.Ground && stream != null && OreProducts.IsGrade(stream.Grade);

    /// <summary>The ground ore an item becomes, or null when the mill does not take it.</summary>
    public static BallMillProduct? Grind(OreRecovery recovery, OreForm? form, OreStream? stream)
    {
        if (!Takes(form, stream))
            return null;
        var lifted = recovery.Lift(stream!, Adds);
        return new BallMillProduct(OreProducts.GroundCode(lifted.Ore, lifted.Grade), lifted);
    }
}
