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
}

/// <summary>A mode's marks as the tool keeps them: the first corner (or the block), and for an area
/// its opposite corner once closed.</summary>
public readonly record struct Marks(MarkPos? First, MarkPos? Second)
{
    public static readonly Marks None = new(null, null);

    /// <summary>The closed area, or null while there is none.</summary>
    public MarkArea? Area => First is { } a && Second is { } b ? MarkArea.Between(a, b) : null;
}

/// <summary>
/// Marking with the command tool, one click at a time. An area takes two clicks: the first marks a
/// corner, the second the opposite corner and closes it; a click after a closed area starts a new one.
/// An opposite corner that makes the area wider than <c>maxSide</c> blocks is refused and the first
/// corner kept, so the player can mark a nearer one.
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
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "this mode marks nothing");
        }
    }
}
