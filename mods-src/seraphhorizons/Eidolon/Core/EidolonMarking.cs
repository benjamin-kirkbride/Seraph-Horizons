namespace SeraphHorizons.Mod.Eidolon.Core;

/// <summary>A block position marked with the command tool.</summary>
public readonly record struct MarkPos(int X, int Y, int Z);

/// <summary>A box of blocks between two marked corners, both included.</summary>
public readonly record struct MarkArea(MarkPos Min, MarkPos Max)
{
    public static MarkArea Between(MarkPos a, MarkPos b) => new(
        new MarkPos(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z)),
        new MarkPos(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z)));

    public int SizeX => Max.X - Min.X + 1;
    public int SizeY => Max.Y - Min.Y + 1;
    public int SizeZ => Max.Z - Min.Z + 1;

    /// <summary>Its longest horizontal side, in blocks.</summary>
    public int LongestSide => Math.Max(SizeX, SizeZ);

    public bool Contains(int x, int y, int z) =>
        x >= Min.X && x <= Max.X && y >= Min.Y && y <= Max.Y && z >= Min.Z && z <= Max.Z;
}

/// <summary>What a command tool mode marks before its order is given (README "Eidolon", the command tool).</summary>
public enum EidolonMarkKind
{
    /// <summary>Nothing: a right-click gives the order (follow, stay).</summary>
    None,

    /// <summary>A block: a right-click on it marks it and gives the order (haul to an infeed).</summary>
    Block,

    /// <summary>An area: a right-click marks its first corner, a second its opposite corner and gives
    /// the order (fell an area).</summary>
    Area,

    /// <summary>An area, then a block: two clicks mark the area as for <see cref="Area"/>, a third
    /// marks the block and gives the order (haul from an area to a machine's infeed; the crew order's
    /// fell area and infeed). The order gets both.</summary>
    AreaThenBlock,
}

public enum MarkStep
{
    /// <summary>A block marked: the order goes out.</summary>
    Target,

    /// <summary>An area's first corner marked: the next click marks the opposite one.</summary>
    FirstCorner,

    /// <summary>The area closed: the order goes out.</summary>
    Area,

    /// <summary>The opposite corner would make the area larger than the mode allows: refused, the
    /// first corner kept.</summary>
    TooLarge,

    /// <summary>For <see cref="EidolonMarkKind.AreaThenBlock"/>: the area closed, and the next click
    /// marks the block (which gives the order, as <see cref="Target"/>).</summary>
    AreaThenBlock,
}

/// <summary>A mode's marks as the tool keeps them: the first corner (or the block), for an area
/// its opposite corner once closed, and for <see cref="EidolonMarkKind.AreaThenBlock"/> the block
/// marked after the area (<see cref="Third"/>).</summary>
public readonly record struct Marks(MarkPos? First, MarkPos? Second, MarkPos? Third = null)
{
    public static readonly Marks None = new(null, null);

    /// <summary>The closed area, or null while there is none.</summary>
    public MarkArea? Area => First is { } a && Second is { } b ? MarkArea.Between(a, b) : null;
}

/// <summary>
/// Marking with the command tool, one click at a time. An area takes two clicks: the first marks a
/// corner, the second the opposite corner and closes it; a click after a closed area starts a new one.
/// An opposite corner that makes the area wider than <c>maxSide</c> blocks is refused and the first
/// corner kept, so the player can mark a nearer one. An area then a block takes three clicks: the area's
/// two, then the block; a click after the block starts a new area.
/// </summary>
public static class EidolonMarking
{
    public static (Marks Marks, MarkStep Step) Click(EidolonMarkKind kind, Marks marks, MarkPos clicked, int maxSide)
    {
        switch (kind)
        {
            case EidolonMarkKind.Block:
                return (new Marks(clicked, null), MarkStep.Target);
            case EidolonMarkKind.Area:
                if (marks.First is not { } first || marks.Second != null)
                    return (new Marks(clicked, null), MarkStep.FirstCorner);
                if (MarkArea.Between(first, clicked).LongestSide > maxSide)
                    return (marks, MarkStep.TooLarge);
                return (new Marks(first, clicked), MarkStep.Area);
            case EidolonMarkKind.AreaThenBlock:
                if (marks.First is not { } start || marks.Third != null)
                    return (new Marks(clicked, null), MarkStep.FirstCorner);
                if (marks.Second == null)
                    return MarkArea.Between(start, clicked).LongestSide > maxSide
                        ? (marks, MarkStep.TooLarge)
                        : (new Marks(start, clicked), MarkStep.AreaThenBlock);
                return (marks with { Third = clicked }, MarkStep.Target);
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "this mode marks nothing");
        }
    }
}
