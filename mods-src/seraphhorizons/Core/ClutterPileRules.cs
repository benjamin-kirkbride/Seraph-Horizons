namespace SeraphHorizons.Mod.Core;

/// <summary>
/// No clutter piles (<c>NoClutterPiles</c>, README "Clutter does not pile"): which ground piles
/// <c>ClutterPiles.cs</c> takes away. Game-independent, so tests/ runs it without the game.
///
/// More Piles' catch-all block pile (its <c>*</c> rule, <c>MatchesShape</c>
/// <c>game:block/basic/cube</c>) gives a <c>Stacking</c> pile to every block whose type file names
/// the plain cube as its shape and that has no ground storage yet. The game's clutter (class
/// <c>BlockClutter</c> and the other <c>BlockShapeFromAttributes</c> blocks: clutter, aquatic and
/// devastation clutter, banners, the Jonas lamp, the clutter bookshelves) and the other blocks drawn
/// by the <c>ShapeFromAttributes</c> block entity behavior (the elevator rail) name the cube only as
/// a placeholder: each stack draws the shape its <c>type</c> attribute picks. So the rule matches
/// them, and every one of them could be put down as a pile of cubes, 64 a block (4,096 with big
/// piles), where the game sets them down one at a time as decoration.
/// </summary>
public static class ClutterPileRules
{
    /// <summary>The game's base class of blocks that draw their shape from a stack's attributes.</summary>
    public const string ShapeFromAttributesClass = "BlockShapeFromAttributes";

    /// <summary>The block entity behavior that draws a block's shape from its attributes.</summary>
    public const string ShapeFromAttributesBehavior = "ShapeFromAttributes";

    /// <summary>The key More Piles writes into every pile behavior it adds.</summary>
    public const string MorePilesKey = "MorePilesProperties";

    /// <summary>Whether a block draws its shape from a stack's attributes: one of
    /// <paramref name="classChain"/> (its class and every base class, by name) is
    /// <see cref="ShapeFromAttributesClass"/>, or one of its block entity behaviors is
    /// <see cref="ShapeFromAttributesBehavior"/>.</summary>
    public static bool DrawsShapeFromAttributes(IEnumerable<string?> classChain, IEnumerable<string?> entityBehaviors) =>
        classChain.Any(c => c == ShapeFromAttributesClass)
        || entityBehaviors.Any(b => string.Equals(b, ShapeFromAttributesBehavior, StringComparison.OrdinalIgnoreCase));

    /// <summary>Whether a pile behavior's properties (as text) are More Piles': they carry
    /// <see cref="MorePilesKey"/> (in any case, as the game reads them).</summary>
    public static bool IsMorePilesPile(string? pileProperties) =>
        pileProperties != null && pileProperties.Contains('"' + MorePilesKey + '"', StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether the block's ground storage is taken away: More Piles gave it a pile and the
    /// block draws its shape from its attributes. A pile from the block's own type file or a JSON
    /// patch stays: a mod that meant its clutter to pile said so.</summary>
    public static bool TakesNoPile(IEnumerable<string?> classChain, IEnumerable<string?> entityBehaviors, string? pileProperties) =>
        IsMorePilesPile(pileProperties) && DrawsShapeFromAttributes(classChain, entityBehaviors);
}
