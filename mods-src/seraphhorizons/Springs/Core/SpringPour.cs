namespace SeraphHorizons.Mod.Springs.Core;

/// <summary>
/// How an untapped spring pours (README "Springs"), as the game's source block does
/// (<c>BlockBehaviorFiniteSpreadingLiquid.SpreadAndUpdateLiquidLevels</c>): into the cell under it
/// as falling water when it can, and then nowhere else; otherwise sideways at level 6 into every
/// open side. A rapids spring pours out of one of those faces at a time (<see cref="RapidFaces"/>,
/// <see cref="OneFace"/>). The water it pours stays because the game asks each flowing cell how
/// high its neighbours stand (<c>GetMaxNeighbourLiquidLevel</c>), and a pouring spring answers as
/// a source would (<see cref="Feed"/>), a rapids spring only for the cell on its face
/// (<see cref="FeedsFace"/>). Faces are the game's <c>BlockFacing.ALLFACES</c> indices.
/// </summary>
public static class SpringPour
{
    public const int North = 0, East = 1, South = 2, West = 3, Up = 4, Down = 5;

    /// <summary>A rapids spring that has no face to pour out of.</summary>
    public const int NoFace = -1;

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

    /// <summary>The faces a rapids spring may pour out of: <see cref="Faces"/>, with a face whose
    /// cell already holds its water (<paramref name="holdsWater"/>: at the poured level or higher)
    /// counted as open even where the game's <c>CanSpreadIntoBlock</c> says no, as it does into
    /// water as high as the spring's, so the face it is running out of stays one. Under it, that
    /// holds only when the water there is no source of its own (over a source it spreads
    /// sideways).</summary>
    public static IReadOnlyList<int> RapidFaces(bool belowOpen, bool canSpreadDown, bool belowIsSource,
        Func<int, bool> canSpreadSideways, Func<int, bool> holdsWater) =>
        Faces(belowOpen, canSpreadDown || (!belowIsSource && holdsWater(Down)), belowIsSource,
            f => canSpreadSideways(f) || holdsWater(f));

    /// <summary>The one face a rapids spring pours out of, of <paramref name="faces"/>: the one it
    /// was pouring out of (<paramref name="current"/>) while it still may; otherwise the first, in
    /// north, east, south, west order, whose cell already holds its water (so a spring found
    /// pouring out of several keeps one that is running); otherwise the first.
    /// <see cref="NoFace"/> when there is none.</summary>
    public static int OneFace(IReadOnlyList<int> faces, int? current, Func<int, bool> holdsWater)
    {
        if (faces.Count == 0)
            return NoFace;
        if (current is { } c && faces.Contains(c))
            return c;
        foreach (int face in faces)
            if (holdsWater(face))
                return face;
        return faces[0];
    }

    /// <summary>Whether a pouring spring feeds the cell on its <paramref name="face"/>: a normal
    /// spring (<paramref name="pourFace"/> null) every one, as the game's source does; a rapids
    /// spring only the one it pours out of. One that has not chosen yet (null too: an old world's,
    /// before its first look) feeds every one until it does.</summary>
    public static bool FeedsFace(int? pourFace, int face) => pourFace == null || pourFace == face;

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
