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

    /// <summary>Its blocks in all.</summary>
    public long Volume => (long)SizeX * SizeY * SizeZ;

    public bool Contains(int x, int y, int z) =>
        x >= Min.X && x <= Max.X && y >= Min.Y && y <= Max.Y && z >= Min.Z && z <= Max.Z;

    /// <summary>How far the point (<paramref name="x"/>, <paramref name="y"/>, <paramref name="z"/>)
    /// is from the nearest point of the space its blocks fill (from <see cref="Min"/>'s lower corner
    /// to <see cref="Max"/>'s upper one), in blocks: 0 inside it.</summary>
    public double DistanceTo(double x, double y, double z)
    {
        static double Past(double v, int min, int max) => v < min ? min - v : v > max + 1 ? v - (max + 1) : 0;
        double dx = Past(x, Min.X, Max.X), dy = Past(y, Min.Y, Max.Y), dz = Past(z, Min.Z, Max.Z);
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }
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

    /// <summary>The opposite corner would make the area hold more blocks in all than the mode allows:
    /// refused, the first corner kept.</summary>
    TooManyBlocks,

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

/// <summary>The area a click would close (<see cref="EidolonMarking.Preview"/>), and why it would be
/// refused (<see cref="MarkStep.TooLarge"/> or <see cref="MarkStep.TooManyBlocks"/>), null when it
/// would not be.</summary>
public readonly record struct AreaPreview(MarkArea Area, MarkStep? Refusal)
{
    public bool Refused => Refusal != null;
}

/// <summary>
/// Marking with the command tool, one click at a time. An area takes two clicks: the first marks a
/// corner, the second the opposite corner and closes it; a click after a closed area starts a new one.
/// An opposite corner that makes the area wider than <c>maxSide</c> blocks, or hold more than
/// <c>maxBlocks</c> in all, is refused and the first corner kept, so the player can mark a nearer one. An area then a block takes three clicks: the area's
/// two, then the block; a click after the block starts a new area.
/// </summary>
public static class EidolonMarking
{
    public static (Marks Marks, MarkStep Step) Click(EidolonMarkKind kind, Marks marks, MarkPos clicked, int maxSide, long maxBlocks = long.MaxValue)
    {
        switch (kind)
        {
            case EidolonMarkKind.Block:
                return (new Marks(clicked, null), MarkStep.Target);
            case EidolonMarkKind.Area:
                if (Preview(kind, marks, clicked, maxSide, maxBlocks) is not { } area)
                    return (new Marks(clicked, null), MarkStep.FirstCorner);
                return area.Refusal is { } refused ? (marks, refused) : (new Marks(marks.First, clicked), MarkStep.Area);
            case EidolonMarkKind.AreaThenBlock:
                if (Preview(kind, marks, clicked, maxSide, maxBlocks) is { } closing)
                    return closing.Refusal is { } tooBig ? (marks, tooBig) : (new Marks(marks.First, clicked), MarkStep.AreaThenBlock);
                if (marks.First == null || marks.Third != null)
                    return (new Marks(clicked, null), MarkStep.FirstCorner);
                return (marks with { Third = clicked }, MarkStep.Target);
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "this mode marks nothing");
        }
    }

    /// <summary>Whether the next click on a mode of kind <paramref name="kind"/> marks its area's
    /// opposite corner: a first corner is marked and the area is not closed yet.</summary>
    public static bool AwaitsSecondCorner(EidolonMarkKind kind, Marks marks) => kind switch
    {
        EidolonMarkKind.Area => marks is { First: not null, Second: null },
        EidolonMarkKind.AreaThenBlock => marks is { First: not null, Second: null, Third: null },
        _ => false,
    };

    /// <summary>
    /// The area a click on <paramref name="aimed"/> would close, and whether it would be refused (the
    /// same rule <see cref="Click"/> applies, which goes through this); null when the next click does
    /// not mark the area's opposite corner (<see cref="AwaitsSecondCorner"/>). The client draws it as
    /// the live preview of the area before the second click, red when refused.
    /// </summary>
    public static AreaPreview? Preview(EidolonMarkKind kind, Marks marks, MarkPos aimed, int maxSide, long maxBlocks = long.MaxValue)
    {
        if (!AwaitsSecondCorner(kind, marks) || marks.First is not { } first)
            return null;
        var area = MarkArea.Between(first, aimed);
        return new AreaPreview(area, Refused(area, maxSide, maxBlocks));
    }

    /// <summary>
    /// Whether an order reaches a bound eidolon standing at (<paramref name="x"/>, <paramref name="y"/>,
    /// <paramref name="z"/>), <paramref name="fromHolder"/> blocks from the tool's holder: within
    /// <paramref name="range"/> of the holder, or, once the mode's marks hold an <paramref name="area"/>
    /// (an area mode's, or an area then a block's), within <paramref name="range"/> of that area's
    /// nearest point (<see cref="MarkArea.DistanceTo"/>). So a player who marks the first corner beside
    /// the eidolons and walks far off to mark the second still orders them. Without an area, the holder's
    /// range alone.
    /// </summary>
    public static bool InReach(double fromHolder, MarkArea? area, double x, double y, double z, double range) =>
        fromHolder <= range || area is { } marked && marked.DistanceTo(x, y, z) <= range;

    private static MarkStep? Refused(MarkArea area, int maxSide, long maxBlocks) =>
        area.LongestSide > maxSide ? MarkStep.TooLarge : area.Volume > maxBlocks ? MarkStep.TooManyBlocks : null;
}
