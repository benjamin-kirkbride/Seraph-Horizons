namespace SeraphHorizons.Mod.Core;

/// <summary>What a cell under a surface ruin holds, as the foundation reads it.</summary>
public enum FoundationCell
{
    /// <summary>Air or anything without a collision box (a plant): filled over.</summary>
    Open,
    /// <summary>A liquid: the column is left as it is.</summary>
    Liquid,
    /// <summary>Anything else (the ground, another structure's block): the foundation stands on it.</summary>
    Ground,
}

/// <summary>
/// The foundation under one column of a surface ruin's footprint under <c>RuinsOnMedianGround</c>:
/// seated on the median of its ground, a ruin on a slope has its low side off the ground, and the
/// air between the ground and the lowest block the ruin has in the column is filled. Never above the
/// height the ruin was seated on (the ground it was placed for), so a column under an arch or an
/// overhang is filled to that ground and no higher; never through a liquid (the column is left), and
/// never deeper than <see cref="MaxDepth"/> (a ravine or cave mouth the game's samples missed is
/// left). Game-independent; <c>RuinFoundations</c> applies it.
/// </summary>
public static class RuinFoundationColumn
{
    /// <summary>The deepest gap filled: the game's <c>MaxYDiff</c> (3 by default) keeps the samples
    /// within 3 of each other, so the ground inside the footprint is seldom more than a few blocks
    /// under the seat; a gap deeper than this is something the samples missed.</summary>
    public const int MaxDepth = 8;

    /// <summary>The lowest of a column's <paramref name="sizeY"/> layers the ruin stands on there,
    /// or null for a column with nothing to stand on.</summary>
    public static int? LowestSolid(Func<int, bool> standsOn, int sizeY)
    {
        for (int y = 0; y < sizeY; y++)
            if (standsOn(y))
                return y;
        return null;
    }

    /// <summary>The highest cell a column's foundation may fill: just under the ruin's lowest solid
    /// block there (<paramref name="lowestSolidY"/>, a world height), and no higher than the
    /// <paramref name="seat"/>. Null for a column with no solid block.</summary>
    public static int? Top(int? lowestSolidY, int seat) => lowestSolidY is { } y ? Math.Min(y - 1, seat) : null;

    /// <summary>The cells to fill, from just above the ground up to <paramref name="top"/>,
    /// scanning down from <paramref name="top"/>: null when <paramref name="top"/> is already ground,
    /// when the scan meets a liquid, or when it finds no ground within <paramref name="maxDepth"/>
    /// cells.</summary>
    public static (int From, int To)? Span(int top, Func<int, FoundationCell> cell, int maxDepth = MaxDepth)
    {
        for (int y = top; y >= top - maxDepth && y > 0; y--)
        {
            switch (cell(y))
            {
                case FoundationCell.Ground:
                    return y == top ? null : (y + 1, top);
                case FoundationCell.Liquid:
                    return null;
            }
        }
        return null;
    }
}
