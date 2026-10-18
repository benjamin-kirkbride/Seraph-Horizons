using System.Globalization;

namespace SeraphHorizons.Mod.Ore.Core;

/// <summary>
/// The counts the handbook's smelting slots show (README "Ore processing: the handbook's smelting
/// counts"): how many of an input make one of the output, never the game's raw ratio. The pack's exact
/// rates (<see cref="OreProducts"/>' <c>Rate</c>) are whole numbers of items per smelted stack, such as
/// 51 bullion per 20,000 poor roasted rock, and the game draws the 20,000 under the input's icon, where
/// several in a row run into each other. Game-independent; the handbook side is
/// <c>Ore/Processing/SmeltingHandbook.cs</c>.
/// </summary>
public static class SmeltCount
{
    /// <summary>Units of metal one smelted output item holds (an ingot, a bar of bullion, a bloom).</summary>
    public const int UnitsPerOutput = 100;

    /// <summary>
    /// Inputs that make one output at <paramref name="ratio"/> inputs per <paramref name="output"/>
    /// outputs (the game's <c>smeltedRatio</c> and <c>smeltedStack</c> size), rounded up: 20,000 for 51
    /// is 393. At least 1; a ratio or output that is not positive counts as 1.
    /// </summary>
    public static int PerOne(int ratio, int output)
    {
        long r = Math.Max(1, ratio), o = Math.Max(1, output);
        return (int)Math.Min(int.MaxValue, Math.Max(1, (r + o - 1) / o));
    }

    /// <summary>
    /// Inputs that hold <paramref name="units"/> of metal, rounded up, at <paramref name="ratio"/> inputs
    /// per <paramref name="output"/> outputs of <see cref="UnitsPerOutput"/> units each: what a mold of
    /// <paramref name="units"/> takes (the game counts every input as a nugget's 5 units, so 20 to an
    /// ingot whatever it is). A nugget (20 for 1) is 20 for 100 units, a bar of bullion (1 for 1) 1.
    /// </summary>
    public static int ForUnits(int units, int ratio, int output)
    {
        long u = Math.Max(1, units), r = Math.Max(1, ratio), o = Math.Max(1, output);
        long per = o * UnitsPerOutput;
        return (int)Math.Min(int.MaxValue, Math.Max(1, (u * r + per - 1) / per));
    }

    /// <summary>
    /// <paramref name="count"/> as a slot draws it: as it is below 1,000, then with an SI suffix, K from
    /// 1,000 and M from 1,000,000, to one decimal with a trailing .0 dropped (999, 1K, 1.6K, 20K, 2.5M).
    /// The decimal is rounded half away from zero, and a count that rounds up to the next unit takes its
    /// suffix (999,950 is 1M, not 1000K).
    /// </summary>
    public static string Abbreviated(long count)
    {
        if (count < 0) return "-" + Abbreviated(-count);
        if (count < 1000) return count.ToString(CultureInfo.InvariantCulture);
        decimal k = Math.Round(count / 1000m, 1, MidpointRounding.AwayFromZero);
        if (k < 1000) return Format(k) + "K";
        decimal m = Math.Round(count / 1_000_000m, 1, MidpointRounding.AwayFromZero);
        return Format(m) + "M";
    }

    private static string Format(decimal v) => v.ToString("0.#", CultureInfo.InvariantCulture);
}
