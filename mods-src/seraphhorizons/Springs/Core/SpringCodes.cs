namespace SeraphHorizons.Mod.Springs.Core;

/// <summary>A spring's kind: the vanilla rivulet it replaces, and so the water it pours.</summary>
public enum SpringKind
{
    /// <summary>In place of a <c>water-still-7</c> rivulet: pours <c>water</c>.</summary>
    Normal,

    /// <summary>In place of a <c>rapidwater-still-7</c> rivulet (rapids): pours <c>rapidwater</c>.</summary>
    Rapid,
}

/// <summary>
/// The spring's block codes (README "Springs"): <c>seraphhorizons:waterspring-{kind}-{rock}</c>, a
/// variant per kind and per rock of the game's <c>rockwithdeposit</c> property (every rock the game's
/// own <c>rock-*</c> has, GeologyAdditions' included), and the game's liquid codes it pours and
/// reverts to.
/// </summary>
public static class SpringCodes
{
    public const string Domain = "seraphhorizons";
    public const string BlockBase = "waterspring";
    public const string NormalName = "normal";
    public const string RapidName = "rapid";
    public const string Water = "water";
    public const string RapidWater = "rapidwater";

    /// <summary>A vanilla source block's level, and the level a source spreads sideways at.</summary>
    public const int SourceLevel = 7;
    public const int PouredLevel = 6;

    public static IReadOnlyList<SpringKind> Kinds { get; } = [SpringKind.Normal, SpringKind.Rapid];

    public static string NameOf(SpringKind kind) => kind == SpringKind.Rapid ? RapidName : NormalName;

    public static SpringKind? KindNamed(string? name) => name switch
    {
        NormalName => SpringKind.Normal,
        RapidName => SpringKind.Rapid,
        _ => null,
    };

    /// <summary>The block path, <c>waterspring-normal-granite</c>.</summary>
    public static string BlockPath(SpringKind kind, string rock) => $"{BlockBase}-{NameOf(kind)}-{rock}";

    /// <summary>The kind and rock of a spring's block path, or null for anything else.</summary>
    public static (SpringKind Kind, string Rock)? Parse(string? path)
    {
        if (path == null || !path.StartsWith(BlockBase + "-", StringComparison.Ordinal))
            return null;
        var rest = path[(BlockBase.Length + 1)..];
        int dash = rest.IndexOf('-');
        if (dash <= 0 || dash == rest.Length - 1 || KindNamed(rest[..dash]) is not { } kind)
            return null;
        return (kind, rest[(dash + 1)..]);
    }

    /// <summary>The game's liquid a spring of <paramref name="kind"/> pours: <c>water</c> or
    /// <c>rapidwater</c> (both have the liquid code <c>water</c>, so the block's first code part tells
    /// them apart).</summary>
    public static string LiquidOf(SpringKind kind) => kind == SpringKind.Rapid ? RapidWater : Water;

    /// <summary>The source block a spring of <paramref name="kind"/> replaces and reverts to:
    /// <c>water-still-7</c>, <c>rapidwater-still-7</c> (domain game).</summary>
    public static string SourcePath(SpringKind kind) => $"{LiquidOf(kind)}-still-{SourceLevel}";

    /// <summary>The falling water a source pours into the cell under it (the game's
    /// <c>GetFallingLiquidBlockId</c>): <c>water-d-6</c>.</summary>
    public static string FallingPath(SpringKind kind) => $"{LiquidOf(kind)}-d-{PouredLevel}";

    /// <summary>Whether a fluid block's path is the liquid a spring of <paramref name="kind"/>
    /// pours, any flow and height.</summary>
    public static bool IsLiquidOf(SpringKind kind, string? fluidPath) =>
        fluidPath != null && fluidPath.StartsWith(LiquidOf(kind) + "-", StringComparison.Ordinal);

    /// <summary>The kind of spring a rivulet's fluid becomes: a game <c>water-*</c> or
    /// <c>rapidwater-*</c> block; null for anything else (lava rivulets stay lava).</summary>
    public static SpringKind? KindOfFluid(string? domain, string? fluidPath)
    {
        if (domain != "game")
            return null;
        if (IsLiquidOf(SpringKind.Normal, fluidPath))
            return SpringKind.Normal;
        if (IsLiquidOf(SpringKind.Rapid, fluidPath))
            return SpringKind.Rapid;
        return null;
    }

    /// <summary>The host rock's own block, whose drops a broken spring gives:
    /// <c>game:rock-granite</c>.</summary>
    public static string HostRockCode(string rock) => $"game:rock-{rock}";
}
