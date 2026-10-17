using SeraphHorizons.Mod.Ore.Core;

namespace SeraphHorizons.Mod.Jig.Core;

/// <summary>
/// What the jig does (#919): the second concentrator, a Harz plunger jig. Each rock is one pass of the
/// recovery core (<see cref="ConcentratorWash.Wash"/> as <see cref="OreMachine.Jig"/>, r = 0.60 of what can
/// be caught, × the ore's density): what sinks through the sieve leaves as concentrate, the rock goes on
/// over the weir as tailings carrying the rest, so it chains on the sluice's tailings and before the
/// table's. Its rate, load (0.1 steady) and water (70 L a rock) are ore processing's
/// (<c>config/ore-processing.json</c>, <c>machines.jig</c>).
/// </summary>
public static class Jigging
{
    /// <summary>The rig's content <c>requires</c> (<c>contents</c>): its water and its ore bed.</summary>
    public const string Water = "water", Bed = "bed";

    /// <summary>The blocktype's <c>extraRequires</c>: what it holds while it works, in no stage.</summary>
    public static readonly IReadOnlyList<string> ContentRequires = [Water, Bed];

    /// <summary>
    /// Whether a content part is drawn: the water once the jig is built and its reservoir holds any, the
    /// ore bed on the sieve while it works (it needs the water too). Null for a part that is not content
    /// (its build decides).
    /// </summary>
    public static bool? ShowsContent(string? requires, bool built, bool holdsWater, bool working) => requires switch
    {
        Water => built && holdsWater,
        Bed => built && holdsWater && working,
        _ => null,
    };

    /// <summary>One rock through the jig: <see cref="ConcentratorWash.Wash"/> as the jig.</summary>
    public static WashResult Wash(OreRecovery recovery, OreStream feed,
        Func<IEnumerable<(string Code, double ChancePerPan, bool Metal)>>? gravelDrops = null) =>
        ConcentratorWash.Wash(recovery, OreMachine.Jig, feed, gravelDrops);
}
