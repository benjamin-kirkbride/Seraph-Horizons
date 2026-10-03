using SeraphHorizons.Mod.TidyVariants.Core;

namespace SeraphHorizons.Mod.Core;

/// <summary>
/// Cart reach (<c>CartReach</c>): which entities get a second look when the game picks what is
/// under the crosshair, and when one of them wins. Game-independent, so tests/ runs it without the
/// game.
///
/// The game only tests the selection boxes of entities whose origin is within the picking range
/// of the player's eye. For an entity whose selection boxes reach far past its origin, such as a
/// Cartwright's Caravan cart, the boxes at its far end can be in reach while its origin is not.
/// Those entities are looked at again, within <see cref="SearchPadding"/> more, and one is picked
/// only if the point the ray hits is itself within the picking range and nearer than what the game
/// picked: the reach is the game's, measured to the box instead of to the origin.
/// </summary>
public sealed class EntityReach
{
    /// <summary>How much farther than the picking range an entity's origin may be and still be
    /// looked at. A search bound only: the hit must still be in reach. Cartwright's Caravan's boxes
    /// end at most about 4 blocks from their entity's origin (the basic cart's rear lanterns and
    /// storage), so 6 leaves room for a box that is turned or animated.</summary>
    public const float SearchPadding = 6f;

    private readonly string[] _patterns;

    /// <summary>Entity code patterns, <c>domain:path</c> with <c>*</c> wildcards
    /// (<see cref="Wildcard"/>). A pattern without a domain is in <c>game</c>, as an asset location
    /// is. Case does not matter; blank patterns are ignored.</summary>
    public EntityReach(IEnumerable<string?>? patterns)
    {
        _patterns = (patterns ?? [])
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => Normalize(p!))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>The patterns in use, normalized (lowercase, with a domain).</summary>
    public IReadOnlyList<string> Patterns => _patterns;

    /// <summary>Whether entities with this code (<c>domain:path</c>) get the second look.</summary>
    public bool Applies(string entityCode)
    {
        string code = Normalize(entityCode);
        foreach (string pattern in _patterns)
            if (Wildcard.IsMatch(pattern, code))
                return true;
        return false;
    }

    /// <summary>Whether a box hit at this squared distance from the eye is in reach.</summary>
    public static bool InReach(double hitDistanceSq, double pickingRange) =>
        hitDistanceSq <= pickingRange * pickingRange;

    /// <summary>Whether a far entity's hit replaces the current selection: it must be in reach and
    /// strictly nearer than the current pick (the block or entity the game chose, or
    /// <see cref="double.PositiveInfinity"/> if it chose nothing).</summary>
    public static bool Replaces(double hitDistanceSq, double pickingRange, double currentDistanceSq) =>
        InReach(hitDistanceSq, pickingRange) && hitDistanceSq < currentDistanceSq;

    private static string Normalize(string code)
    {
        code = code.Trim().ToLowerInvariant();
        return code.Contains(':') ? code : "game:" + code;
    }
}
