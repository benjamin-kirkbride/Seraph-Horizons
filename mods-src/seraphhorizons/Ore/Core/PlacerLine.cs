namespace SeraphHorizons.Mod.Ore.Core;

/// <summary>What a placer feed is: Wilderlands Panning's rich gravel, or Geology Additions' bog iron.</summary>
public enum PlacerKind { Gravel, BogIron }

/// <summary>A placer feed item, by its code: rich gravel of a rock, or bog iron.</summary>
/// <param name="Rock">The gravel's rock; null for bog iron.</param>
public sealed record PlacerFeed(string Code, PlacerKind Kind, string? Rock)
{
    /// <summary>Wilderlands Panning's rich gravel, the placer fields' gravel: <c>game:richgravel-{rock}</c>.</summary>
    public const string GravelPrefix = "game:richgravel-";

    /// <summary>Geology Additions' bog iron, the stone its block drops (#701's ruling: one a block).</summary>
    public const string BogIronCode = "game:stone-bogiron";

    /// <summary>The placer feed <paramref name="code"/> is, or null.</summary>
    public static PlacerFeed? Parse(string? code)
    {
        if (code == null)
            return null;
        if (code == BogIronCode)
            return new PlacerFeed(code, PlacerKind.BogIron, null);
        if (!code.StartsWith(GravelPrefix, StringComparison.Ordinal))
            return null;
        string rock = code[GravelPrefix.Length..];
        return rock.Length > 0 && !rock.Contains('-') ? new PlacerFeed(code, PlacerKind.Gravel, rock) : null;
    }
}

/// <summary>One output of a placer pass: an item code and how many of it (expected, fractional: a
/// <see cref="UnitCarry"/> makes whole items of it at one a unit).</summary>
public readonly record struct PlacerOutput(string Code, double Count);

/// <summary>
/// Placer on the line (#701; the owner's rulings of 2026-10-13 and 2026-10-14): rich gravel and bog iron
/// join the line at the concentrators, with no crushing, grinding or riddle (placer counts as already
/// classified, takes no free-metal loss and skips the amalgamation plates). Each is an
/// <see cref="OreStream"/> that <see cref="OreRecovery.Concentrate"/> works like any other: split at once
/// with nothing locked and every lift set, so every concentrator recovers its r of what is left and nothing
/// from its own kind's tailings (the chain rule, #765), and its tailings go on as the same item carrying
/// the rest (<c>oreUnits</c>, <c>oreWorked</c>).
/// <list type="bullet">
/// <item><b>Rich gravel</b> (<see cref="Gravel"/>): its units are the block's content as a share, 1; the
/// pan catches <c>handStations.pan</c> (45 %) of it, so a pass that catches c gives each metal of the gold
/// pan's table c / 0.45 of the pan's yield (<see cref="GravelYield"/>): the sluice alone 1.2×, the sluice
/// and jig 1.8×, as nuggets, with no one-item cap. The by-products (stones, flint) come once, at the pan's
/// rate, from the first concentrator to work the block.</item>
/// <item><b>Bog iron</b> (<see cref="BogIron"/>): <see cref="BogIronUnits"/> units a block (one item, the
/// owner's ruling), washed to <c>seraphhorizons:concentrate-limonite</c> (5 units), since bog ore is
/// limonite, an oxide; it does not bloom raw any more.</item>
/// </list>
/// </summary>
public static class PlacerLine
{
    /// <summary>The stream's ore for rich gravel (no ore of the config: <see cref="Flags"/> make it whole).</summary>
    public const string GravelOre = "placer";

    /// <summary>The stream's ore for bog iron.</summary>
    public const string BogIronOre = "bogiron";

    /// <summary>The ore whose concentrate bog iron washes to.</summary>
    public const string BogIronConcentrateOre = "limonite";

    /// <summary>Bog iron's units a block (the owner's ruling, 2026-10-14: about 14, what a block smelts to today).</summary>
    public const double BogIronUnits = 14;

    /// <summary>Wilderlands Panning: a block of rich gravel is this many pans.</summary>
    public const double PansPerBlock = 8;

    /// <summary>Every lift: placer is classified, fine and loose, and never amalgamated (it skips the plates).</summary>
    public const OreState Flags = OreState.Classified | OreState.Ground | OreState.Amalgamated;

    /// <summary>Whether a stream is placer's (gravel or bog iron).</summary>
    public static bool IsPlacer(OreStream s) => s.Ore is GravelOre or BogIronOre;

    /// <summary>A fresh block of rich gravel of <paramref name="rock"/>: its whole content, nothing locked.</summary>
    public static OreStream Gravel(string? rock) => new(GravelOre, "-", 1, 0, Flags, WorkedBy.None, rock);

    /// <summary>A fresh bog iron stone.</summary>
    public static OreStream BogIron() => new(BogIronOre, "-", BogIronUnits, 0, Flags, WorkedBy.None, null);

    /// <summary>
    /// What a pass that caught <paramref name="caught"/> of a rich gravel block's content (a share of 1)
    /// gives: each metal drop of the pan's table (<paramref name="drops"/>: code, chance a pan, whether it
    /// is metal) at <paramref name="pansPerBlock"/> pans × its chance × caught / <paramref name="pan"/>; the
    /// rest at the pan's own rate, only when <paramref name="first"/> (the first concentrator to work it).
    /// </summary>
    public static IReadOnlyList<PlacerOutput> GravelYield(double caught, bool first, double pan,
        IEnumerable<(string Code, double ChancePerPan, bool Metal)> drops, double pansPerBlock = PansPerBlock)
    {
        double scale = pan > 0 ? Math.Max(0, caught) / pan : 0;
        var outputs = new List<PlacerOutput>();
        foreach (var (code, chance, metal) in drops)
        {
            if (!(chance > 0))
                continue;
            double count = pansPerBlock * chance * (metal ? scale : first ? 1 : 0);
            if (count > 0)
                outputs.Add(new PlacerOutput(code, count));
        }
        return outputs;
    }

    /// <summary>
    /// The chance that one pan gives drop <paramref name="i"/> of a table with roll chances
    /// <paramref name="chances"/>: the game's pan shuffles the table, rolls each drop in turn and stops at
    /// the first hit, so a pan gives at most one item (the recipe exporter's <c>Panning.ChancePerPan</c>).
    /// </summary>
    public static double ChancePerPan(IReadOnlyList<double> chances, int i)
    {
        var n = chances.Count;
        var e = new double[n];
        e[0] = 1;
        var m = 0;
        for (var j = 0; j < n; j++)
        {
            if (j == i) continue;
            var q = 1 - chances[j];
            m++;
            for (var k = m; k > 0; k--) e[k] += e[k - 1] * q;
        }
        double reached = 0, binom = 1;
        for (var k = 0; k < n; k++)
        {
            reached += e[k] / binom;
            binom = binom * (n - 1 - k) / (k + 1);
        }
        return chances[i] * reached / n;
    }
}
