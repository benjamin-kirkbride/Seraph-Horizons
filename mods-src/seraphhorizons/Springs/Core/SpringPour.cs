namespace SeraphHorizons.Mod.Springs.Core;

/// <summary>
/// How an untapped spring pours (README "Springs"), as the game's source block does
/// (<c>BlockBehaviorFiniteSpreadingLiquid.SpreadAndUpdateLiquidLevels</c>): into the cell under it
/// as falling water when it can, and then nowhere else; otherwise sideways at level 6 into every
/// open side. The water it pours stays because the game asks each flowing cell how high its
/// neighbours stand (<c>GetMaxNeighbourLiquidLevel</c>), and a pouring spring answers as a source
/// would (<see cref="Feed"/>). Faces are the game's <c>BlockFacing.ALLFACES</c> indices.
/// </summary>
public static class SpringPour
{
    public const int North = 0, East = 1, South = 2, West = 3, Up = 4, Down = 5;

    public static IReadOnlyList<int> Horizontals { get; } = [North, East, South, West];

    /// <summary>The faces a pouring spring fills. <paramref name="belowOpen"/>: the cell under it
    /// lets liquid in from above (its barrier on that side is under 1); <paramref name="canSpreadDown"/>:
    /// the game's <c>CanSpreadIntoBlock</c> allows falling water there; <paramref name="belowIsSource"/>:
    /// a source of the same liquid is under it (then it spreads sideways, as the game's source does);
    /// <paramref name="canSpreadSideways"/>: the same check for each horizontal face.</summary>
    public static IReadOnlyList<int> Faces(bool belowOpen, bool canSpreadDown, bool belowIsSource, Func<int, bool> canSpreadSideways)
    {
        if (belowOpen)
        {
            if (canSpreadDown)
                return [Down];
            if (!belowIsSource)
                return [];
        }
        var faces = new List<int>(4);
        foreach (int face in Horizontals)
            if (canSpreadSideways(face))
                faces.Add(face);
        return faces;
    }

    /// <summary>The game's answer for a flowing cell, <paramref name="level"/> and
    /// <paramref name="feeds"/> (whether a neighbour one level higher feeds it), with a pouring
    /// spring of its liquid counted as a source block: directly above, it feeds the cell outright
    /// (the game's own answer for the same liquid above); beside it, it stands at level 7 and feeds
    /// a cell at level 6.</summary>
    public static (int Level, bool Feeds) Feed(int level, bool feeds, int ourLevel, bool pouringAbove, bool pouringBeside)
    {
        if (pouringAbove)
            return (SpringCodes.SourceLevel, true);
        if (pouringBeside)
            return (Math.Max(level, SpringCodes.SourceLevel), feeds || ourLevel == SpringCodes.SourceLevel - 1);
        return (level, feeds);
    }
}
