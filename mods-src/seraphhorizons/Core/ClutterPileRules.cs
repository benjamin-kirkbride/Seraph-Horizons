using System.Text.RegularExpressions;

namespace SeraphHorizons.Mod.Core;

/// <summary>
/// No clutter piles (<c>NoClutterPiles</c>, README "Clutter and chiseled blocks do not pile"): which
/// ground piles <c>ClutterPiles.cs</c> takes away. Game-independent, so tests/ runs it without the game.
///
/// More Piles' catch-all block pile (its <c>*</c> rule, <c>MatchesShape</c>
/// <c>game:block/basic/cube</c>) gives a <c>Stacking</c> pile to every block whose type file names
/// the plain cube as its shape and that has no ground storage yet. Many blocks name the cube only
/// as a placeholder and are drawn otherwise:
/// <list type="bullet">
/// <item>from a stack's attributes: the game's clutter (class <c>BlockClutter</c> and the other
/// <c>BlockShapeFromAttributes</c> blocks: clutter, aquatic and devastation clutter, banners, the
/// Jonas lamp, the clutter bookshelves) and the blocks drawn by the <c>ShapeFromAttributes</c> block
/// entity behavior (the elevator rail);</item>
/// <item>by their block entity: chiseled blocks (<c>BlockChisel</c>, <c>BlockMicroBlock</c>), whose
/// voxels are in the stack's attributes, and crates, stationary baskets, cheese, the coal pile and
/// the creative meta blocks.</item>
/// </list>
/// So the rule matched them, and each could be put down as a pile of identical cubes, 64 a block
/// (4,096 with big piles). A pile this rule gave a block that draws its shape from its attributes
/// or has a block entity is taken away. More Piles' own piles of blocks with a block entity (the
/// henbox, tool rack, sign, ...) come from their own rules, which name a real shape or none, and stay.
/// </summary>
public static class ClutterPileRules
{
    /// <summary>The game's base class of blocks that draw their shape from a stack's attributes.</summary>
    public const string ShapeFromAttributesClass = "BlockShapeFromAttributes";

    /// <summary>The block entity behavior that draws a block's shape from its attributes.</summary>
    public const string ShapeFromAttributesBehavior = "ShapeFromAttributes";

    /// <summary>The key More Piles writes into every pile behavior it adds.</summary>
    public const string MorePilesKey = "MorePilesProperties";

    /// <summary>The shape More Piles' catch-all rule matches: the plain cube.</summary>
    public const string CubeShape = "game:block/basic/cube";

    private static readonly Regex CubeMatch = new(
        "\"MatchesShape\"\\s*:\\s*\"" + Regex.Escape(CubeShape) + "\"",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

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

    /// <summary>Whether a pile came from a More Piles rule that matched the block by the plain cube
    /// in its type file (<c>MatchesShape</c> <see cref="CubeShape"/>): the catch-all, or another rule
    /// that, like it, takes the cube to mean the block is one.</summary>
    public static bool IsCubeShapePile(string? pileProperties) =>
        IsMorePilesPile(pileProperties) && CubeMatch.IsMatch(pileProperties!);

    /// <summary>Whether the block's ground storage is taken away: a More Piles rule gave it a pile for
    /// the cube its type file names (<see cref="IsCubeShapePile"/>), and the block is not drawn as
    /// that cube: it draws its shape from its attributes, or it has a block entity
    /// (<paramref name="entityClass"/> set). Any other pile stays: from the block's own type file or
    /// a patch (a mod that meant it to pile said so), or from a More Piles rule for that block.</summary>
    public static bool TakesNoPile(IEnumerable<string?> classChain, IEnumerable<string?> entityBehaviors, string? entityClass, string? pileProperties) =>
        IsCubeShapePile(pileProperties)
        && (!string.IsNullOrEmpty(entityClass) || DrawsShapeFromAttributes(classChain, entityBehaviors));
}
