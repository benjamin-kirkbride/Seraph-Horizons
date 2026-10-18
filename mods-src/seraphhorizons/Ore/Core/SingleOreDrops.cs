namespace SeraphHorizons.Mod.Ore.Core;

/// <summary>One of a block's drops as <see cref="SingleOreDrops"/> reads it: an item drop's path
/// (its code without the domain), whether the item carries <c>metalUnits</c>, and its average count.</summary>
public readonly record struct OreDropView(bool IsItem, string Path, bool HasMetalUnits, double Avg);

/// <summary>What happens to one drop: kept as it is, set to exactly one, or removed.</summary>
public enum OreDropChange { Keep, One, Remove }

/// <summary>
/// One raw ore per ore block (<c>SingleOreDrops</c>): vanilla's ore blocks drop 1.25 ore on average
/// (one, or two a quarter of the time) and crystallised ore one time in a hundred. With the switch an
/// ore block, of any mod, drops exactly one of its ore as its base count (the player's
/// <c>oreDropRate</c> and the like still multiply it) and no crystallised ore.
/// <list type="bullet">
/// <item>An ore drop is an item whose path starts with <c>ore-</c> or that carries
/// <c>metalUnits</c>, crystallised ore (<c>crystalizedore-</c>) aside, and that is not in
/// <see cref="KeptOres"/>.</item>
/// <item>An ore block is a block with an ore drop of one or more on average: a drop below one is a
/// chance (a bonus ore from some other block) and is left alone.</item>
/// <item>In an ore block every ore drop becomes one, crystallised ore goes, and every other drop
/// (quartz's clear quartz) stays.</item>
/// </list>
/// Deposits are sized at one ore per block whether the switch is on or not
/// (<see cref="DepositBasis"/>), so with it off ore gives a quarter more than the sizes say.
/// </summary>
public static class SingleOreDrops
{
    /// <summary>Ore items whose blocks keep their drops: sylvite's 2.5 is a yield of potash salt, not
    /// a chunking of the vein.</summary>
    public static readonly IReadOnlyList<string> KeptOres = ["ore-sylvite"];

    public static bool IsCrystallised(string path) => path.StartsWith("crystalizedore-", StringComparison.Ordinal);

    public static bool IsOre(OreDropView d) =>
        d.IsItem && !IsCrystallised(d.Path) && (d.Path.StartsWith("ore-", StringComparison.Ordinal) || d.HasMetalUnits)
        && !KeptOres.Any(k => d.Path == k || d.Path.StartsWith(k + "-", StringComparison.Ordinal));

    /// <summary>The change to each of a block's drops, or null if it is not an ore block (nothing
    /// changes) or already drops exactly that.</summary>
    public static OreDropChange[]? Plan(IReadOnlyList<OreDropView> drops)
    {
        if (!drops.Any(d => IsOre(d) && d.Avg >= 1)) return null;
        var plan = drops.Select(d => IsOre(d) ? OreDropChange.One : d.IsItem && IsCrystallised(d.Path) ? OreDropChange.Remove : OreDropChange.Keep)
            .ToArray();
        return plan.Any(c => c == OreDropChange.Remove) || drops.Where((d, i) => plan[i] == OreDropChange.One).Any(d => d.Avg != 1)
            ? plan : null;
    }
}

/// <summary>
/// The ore a deposit is sized at. A world created since <c>SingleOreDrops</c> is sized
/// at one ore per block with no crystallised ore, at twice the ingots of before: its veins are 2.5
/// times the volume (twice the metal at four fifths of the ore a block), its size tiers twice the
/// ingots (<c>config/ore-sizes.json</c>, which holds those). A world created before keeps the old
/// basis: 1.25 ore a block, its factors <see cref="BeforeFactorShare"/> of the table's (and never
/// above 1, as then) and its tiers <see cref="BeforeSizeShare"/>.
/// </summary>
public static class DepositBasis
{
    /// <summary>Ore a block on the old basis (vanilla's average drop).</summary>
    public const double BeforeOrePerBlock = 1.25;

    /// <summary>Old factor over new: half the metal at 1.25 ore a block, 0.5 / 1.25.</summary>
    public const double BeforeFactorShare = 0.4;

    /// <summary>Old size tiers over new.</summary>
    public const double BeforeSizeShare = 0.5;

    /// <summary>A block's metal units on the current basis, whatever it drops in this run: one of
    /// each ore drop (a chance below one as it is), crystallised ore left out.</summary>
    public static double UnitsAtOneOre(IEnumerable<(OreDropView Drop, double MetalUnits)> drops) =>
        drops.Where(d => SingleOreDrops.IsOre(d.Drop)).Sum(d => Math.Min(1, Math.Max(0, d.Drop.Avg)) * d.MetalUnits);
}
