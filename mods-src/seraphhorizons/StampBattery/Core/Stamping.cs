using SeraphHorizons.Mod.Ore.Core;

namespace SeraphHorizons.Mod.StampBattery.Core;

/// <summary>What the stamp battery does with an item fed to it.</summary>
public enum StampFeed
{
    /// <summary>Not its feed: it does not go in.</summary>
    None,
    /// <summary>Raw or crystallised ore: stamped to crushed ore and screened.</summary>
    Crush,
    /// <summary>Crushed ore not yet classified (the jaw breaker's product, or spalled ore): it goes
    /// through the stamps and out through the screen, classified.</summary>
    Screen,
}

/// <summary>One product of a rock stamped: its item code, how many, and the stream each carries.</summary>
public readonly record struct StampProduct(string Code, int Count, OreStream Stream);

/// <summary>
/// The stamp battery's job (#728, StampBattery/README.md): a wet five-stamp battery crushes and
/// classifies in one. Raw ore (<c>game:ore-{grade}-{ore}-{rock}</c>, and crystallised ore,
/// <c>game:crystalizedore-...</c>) goes in by chute or by hand at the feed box's mouth and comes out
/// through the screen as crushed ore of its grade, one for one (crystallised ore two, holding twice the
/// units), <see cref="OreState.Classified"/> and carrying its host rock for the tailings (#694). Crushed
/// ore not yet classified (the jaw breaker's) goes through and out classified. No metal is lost: the
/// units are carried unchanged. Already classified, ground or other ore does not go in.
/// One rock fed is one item of the machine's work (its rate, 6 an hour at the reference speed) and
/// one use of the shoes.
/// </summary>
public static class Stamping
{
    public const string CrushedPrefix = "seraphhorizons:crushedore-";

    /// <summary>The machine's id: its build file, settings and lang codes.</summary>
    public const string MachineId = "stampbattery";

    /// <summary>The parts of a raw or crystallised ore code: its first part, grade, ore and rock;
    /// null when the code is not one of the pack's graded ores.</summary>
    public static (string First, string Grade, string Ore, string Rock)? RawParts(string? code)
    {
        if (code == null || !code.StartsWith("game:", StringComparison.Ordinal))
            return null;
        var parts = code[5..].Split('-');
        if (parts.Length != 4 || parts[0] is not ("ore" or "crystalizedore") || !OreProducts.IsGrade(parts[1])
            || !OreProducts.Ores.Contains(parts[2]) || parts[3].Length == 0)
            return null;
        return (parts[0], parts[1], parts[2], parts[3]);
    }

    /// <summary>The ore and grade of the pack's crushed ore code, or null.</summary>
    public static (string Ore, string Grade)? CrushedParts(string? code)
    {
        if (code == null || !code.StartsWith(CrushedPrefix, StringComparison.Ordinal))
            return null;
        var rest = code[CrushedPrefix.Length..];
        int dash = rest.LastIndexOf('-');
        if (dash <= 0 || dash == rest.Length - 1)
            return null;
        string ore = rest[..dash], grade = rest[(dash + 1)..];
        return OreProducts.Ores.Contains(ore) && OreProducts.IsGrade(grade) ? (ore, grade) : null;
    }

    /// <summary>What the battery does with <paramref name="code"/> carrying <paramref name="state"/>.</summary>
    public static StampFeed FeedOf(string? code, OreState state)
    {
        if (RawParts(code) != null)
            return StampFeed.Crush;
        if (CrushedParts(code) != null && !state.HasFlag(OreState.Classified) && !state.HasFlag(OreState.Ground))
            return StampFeed.Screen;
        return StampFeed.None;
    }

    /// <summary>
    /// What one rock of <paramref name="code"/>, its stream <paramref name="feed"/>, comes out as; null
    /// when the battery does not take it. Crushed ore of the rock's ore and grade, classified, its
    /// units unchanged (crystallised ore: two, each half its units), and its host rock carried.
    /// </summary>
    public static StampProduct? Stamp(string? code, OreStream feed)
    {
        switch (FeedOf(code, feed.State))
        {
            case StampFeed.Crush:
            {
                var (first, grade, ore, rock) = RawParts(code)!.Value;
                int count = first == "crystalizedore" ? 2 : 1;
                double units = feed.Units / count;
                var stream = feed with { Ore = ore, Grade = grade, Units = units, State = feed.State | OreState.Classified, Rock = feed.Rock ?? rock };
                return new StampProduct(OreProducts.CrushedCode(ore, grade), count, stream);
            }
            case StampFeed.Screen:
                return new StampProduct(code!, 1, feed with { State = feed.State | OreState.Classified });
            default:
                return null;
        }
    }
}
