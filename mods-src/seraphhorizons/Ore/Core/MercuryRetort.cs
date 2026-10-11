namespace SeraphHorizons.Mod.Ore.Core;

/// <summary>One thing the still retorts: <see cref="Portions"/> of mercury out of each item of
/// <see cref="Code"/>, and the item each leaves behind in the boiler (<see cref="Residue"/>: the
/// sponge of an amalgam), or nothing (cinnabar).</summary>
public sealed record RetortInput(string Code, double Portions, string? Residue);

/// <summary>
/// Retorting in the still (#726; README "Ore processing: retorting mercury in the still"): the game's
/// boiler, with a condenser beside it, drives mercury out of cinnabar and out of amalgam, one portion
/// at a time, into the condenser's bucket; an amalgam's gold or silver stays in the boiler as sponge.
/// Game-independent: which items retort and the maths of one portion. The figures are
/// <c>config/ore-processing.json</c>'s <c>retort</c> (<see cref="OreRecovery.Retort"/>).
/// </summary>
public static class MercuryRetort
{
    private const double Epsilon = 1e-9;

    /// <summary>The game's still ticks ten times a second and moves at most one portion a tick.</summary>
    public const double MaxPortionsPerSecond = 10;

    /// <summary>
    /// Everything that retorts: each amalgam of <see cref="OreProducts.Ores"/> (a free metal's), its
    /// mercury the pan's <c>amalgamMercury</c> × <c>mercuryReturn</c> (10 × 0.9: 9 portions) and its
    /// residue its sponge; and each cinnabar item of <c>retort.cinnabar</c>, with no residue.
    /// </summary>
    public static IReadOnlyList<RetortInput> Inputs(OreRecovery recovery)
    {
        var retort = recovery.Retort;
        var inputs = new List<RetortInput>();
        double perAmalgam = AmalgamPortions(retort);
        foreach (var ore in OreProducts.Ores)
            if (OreProducts.Amalgamates(recovery.Ore(ore)))
                inputs.Add(new RetortInput(OreProducts.AmalgamCode(ore), perAmalgam, OreProducts.SpongeCode(ore)));
        foreach (var (code, portions) in (retort.Cinnabar ?? new()).OrderBy(kv => kv.Key, StringComparer.Ordinal))
            if (portions >= 1)
                inputs.Add(new RetortInput(code, portions, null));
        return inputs;
    }

    /// <summary>The mercury the still returns from one amalgam, in portions.</summary>
    public static double AmalgamPortions(OreProcessingConfig.RetortEntry retort) =>
        retort.AmalgamMercury * Math.Clamp(retort.MercuryReturn, 0, 1);

    /// <summary>The game's distillation ratio that makes a still move <paramref name="portionsPerSecond"/>:
    /// it adds the ratio per second toward the next portion and moves one at every 0.2.</summary>
    public static float Pace(double portionsPerSecond) =>
        (float)(Math.Clamp(portionsPerSecond > 0 && double.IsFinite(portionsPerSecond) ? portionsPerSecond : 2,
            0.01, MaxPortionsPerSecond) / 5);

    /// <summary>
    /// One portion of mercury driven over from the item being retorted. <paramref name="owed"/> is
    /// what that item (with the fraction carried from the one before) still holds, null for a fresh
    /// stack. Returns what it holds after this portion, and whether the item is done: once less than
    /// a whole portion is left, the item is used up (its residue left behind) and the fraction carried
    /// to the next item, which <see cref="Owed"/> already includes. So a stack's mercury comes out
    /// exactly, but for the last item's fraction (under a portion), which is lost.
    /// </summary>
    public static (double Owed, bool ItemDone) Portion(double? owed, double portionsPerItem)
    {
        double perItem = Math.Max(1, portionsPerItem);
        double left = (owed ?? perItem) - 1;
        return left < 1 - Epsilon ? (Math.Max(0, left) + perItem, true) : (left, false);
    }
}
