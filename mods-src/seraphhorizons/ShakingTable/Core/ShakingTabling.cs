using SeraphHorizons.Mod.Ore.Core;

namespace SeraphHorizons.Mod.ShakingTable.Core;

/// <summary>What leaves one of the shaking table's three outlets for one item washed.</summary>
/// <param name="Concentrate">The concentrate products (units; a <see cref="UnitCarry"/> makes whole items of them).</param>
/// <param name="Middlings">The locked units, as the fed item carrying them, out to the ball mill; null when nothing
/// was locked (a stream already freed, or placer).</param>
/// <param name="Tailings">The rest, as the fed item carrying it, on to the vanner or the heap.</param>
public sealed record TableOutlets(IReadOnlyList<WashProduct> Concentrate, OreStream? Middlings, OreStream Tailings, bool Whole);

/// <summary>
/// The shaking table's pass (#921): a Wilfley table, the third concentrator, on the jig's tailings. One item
/// through <see cref="ConcentratorWash"/> at <see cref="OreMachine.Table"/> (r = 0.70 of the catchable units,
/// × the ore's density, capped), sorted onto its three outlets: the concentrate off the deck's far end, the
/// middlings (the feed's locked units, the one machine that sends them out, <see cref="OreRecovery.Concentrate"/>)
/// off the far end's lower corner, the tailings off the low side. Game-independent.
/// </summary>
public static class ShakingTabling
{
    public const string Concentrate = "concentrate", Middlings = "middlings", Tailings = "tailings";

    /// <summary>The deck's strokes a turn of the axle (the rectifier steps it up 3:1, either way round): the
    /// rig's <c>stroke.strokesPerTurn</c>.</summary>
    public const double StrokesPerTurn = 3;

    /// <summary>Units under this are nothing (a middlings stream with no locked units is not sent).</summary>
    public const double Nothing = 1e-9;

    /// <summary>One item's stream through the table, sorted by outlet.</summary>
    public static TableOutlets Wash(OreRecovery recovery, OreStream feed,
        Func<IEnumerable<(string Code, double ChancePerPan, bool Metal)>>? gravelDrops = null)
    {
        var wash = ConcentratorWash.Wash(recovery, OreMachine.Table, feed, gravelDrops);
        var middlings = wash.Middlings is { Units: > Nothing } m ? m : null;
        return new TableOutlets(wash.Products, middlings, wash.Tailings, wash.Whole);
    }
}
