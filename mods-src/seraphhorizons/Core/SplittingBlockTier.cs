namespace SeraphHorizons.Mod.Core;

/// <summary>A splitting block's tier (<c>UnifiedWoodworking</c>), in upgrade order: Logging
/// Expanded's four splitting log stages, on Immersive Woodworking's chopping block.</summary>
public enum SplittingBlockTier
{
    Primitive,
    Debarked,
    Bound,
    Advanced,
}

/// <summary>
/// What every part of the unified woodworking tweak agrees on about a splitting block's tier.
/// Game-independent, so tests/ runs it without the game.
///
/// The tier is a string attribute (<see cref="AttributeKey"/>, one of <see cref="Names"/>) on the
/// chopping block's item stack and in its block entity's saved attributes. A block or stack
/// without it, or with a name this does not know, is <see cref="SplittingBlockTier.Primitive"/>:
/// every chopping block Immersive Woodworking made before the tweak is one.
/// </summary>
public static class SplittingBlockTiers
{
    /// <summary>The attribute key, on the stack and in the block entity's tree alike. Namespaced,
    /// so it never meets one of Immersive Woodworking's own (<c>wood</c>, <c>inventory</c>, ...).</summary>
    public const string AttributeKey = "seraphhorizons:tier";

    /// <summary>The tiers' attribute values, in upgrade order (index = the enum's value).</summary>
    public static readonly IReadOnlyList<string> Names = ["primitive", "debarked", "bound", "advanced"];

    /// <summary>Firewood per log by tier, the hand chopping block's and (advanced only) the
    /// chopper's: Logging Expanded's 6 on its primitive splitting log and 8 on its advanced one,
    /// with the two stages between at the primitive's. Immersive Woodworking's default is 8.</summary>
    private static readonly int[] FirewoodPerLogByTier = [6, 6, 6, 8];

    public static string Name(this SplittingBlockTier tier) => Names[(int)tier];

    /// <summary>The tier an attribute value names; missing or unknown is primitive.</summary>
    public static SplittingBlockTier Parse(string? name)
    {
        for (int i = 0; i < Names.Count; i++)
            if (string.Equals(Names[i], name, StringComparison.Ordinal))
                return (SplittingBlockTier)i;
        return SplittingBlockTier.Primitive;
    }

    /// <summary>The tier an upgrade makes of this one, or null for the advanced tier.</summary>
    public static SplittingBlockTier? Next(this SplittingBlockTier tier) =>
        tier == SplittingBlockTier.Advanced ? null : tier + 1;

    public static int FirewoodPerLog(this SplittingBlockTier tier) => FirewoodPerLogByTier[(int)tier];

    /// <summary>Whether the mechanical chopper takes a splitting block of this tier as its bed:
    /// only the advanced one, so the chopper's yield is the advanced tier's.</summary>
    public static bool IsChopperBed(this SplittingBlockTier tier) => tier == SplittingBlockTier.Advanced;

    /// <summary>The tier's name without a wood, e.g. "Bound splitting block".</summary>
    public static string NameKey(this SplittingBlockTier tier) => "seraphhorizons:splittingblock-" + tier.Name();

    /// <summary>The tier's name with the wood as <c>{0}</c> (Immersive Woodworking's species word,
    /// as in its <c>block-choppingblock-name</c>), e.g. "{0} bound splitting block".</summary>
    public static string WoodNameKey(this SplittingBlockTier tier) => NameKey(tier) + "-name";
}
