namespace SeraphHorizons.Mod.CrucibleFurnace.Core;

/// <summary>Why a row of furnaces has no draught.</summary>
public enum RowProblem
{
    None,
    /// <summary>No member's outlet carries a chimney (every one capped, or bare).</summary>
    NoChimney,
    /// <summary>The row's chimney is shorter than the minimum.</summary>
    ChimneyTooShort,
    /// <summary>More furnaces side by side than one chimney serves.</summary>
    TooManyFurnaces,
    /// <summary>A member other than the chimney's has its outlet open: it leaks the draught
    /// (<see cref="FurnaceRow.OpenOutlet"/> says which).</summary>
    OutletOpen,
}

/// <summary>A furnace of a row as the row sees it: whether its outlet is capped, and how many
/// chimney blocks stand on it, counted from the outlet up (0 for none).</summary>
public readonly record struct RowMember(bool Capped, int Chimney);

/// <summary>
/// A row of crucible furnaces on one chimney (README "Crucible furnace", #796): furnaces of the same
/// facing standing side by side along their width, touching, up to
/// <see cref="CrucibleFurnaceConfig.MaxFurnacesInRow"/>. One chimney on any member's outlet serves
/// them all, each at its full draught, as the per-hole flues in one stack did; every other member's
/// outlet must be capped, or it leaks the draught and the whole row has none. Positions are offsets
/// along the row in furnaces from the furnace asking (0), so <see cref="First"/> is 0 or negative.
/// </summary>
public sealed record FurnaceRow(int First, int Count, int? ChimneyAt, int ChimneyHeight, int? OpenOutlet, RowProblem Problem)
{
    /// <summary>The row has its draught: one chimney tall enough, every other outlet capped.</summary>
    public bool Works => Problem == RowProblem.None;

    /// <summary>The highest chimney block counted (a taller one is just as good).</summary>
    public const int ChimneyScan = 64;

    /// <summary>The members' offsets, in order along the row.</summary>
    public IEnumerable<int> Members => Enumerable.Range(First, Count);

    /// <summary>
    /// The row of the furnace at offset 0, <paramref name="at"/> telling what stands at an offset (null
    /// for no furnace of the row there). The row runs both ways while there is a member, stopping once
    /// it has more than <paramref name="maxFurnaces"/>. Its chimney is the tallest on an uncapped
    /// outlet (the first along the row on a tie); any other uncapped outlet, with a chimney or without,
    /// leaks.
    /// </summary>
    public static FurnaceRow Find(Func<int, RowMember?> at, int maxFurnaces, int minChimney)
    {
        var members = new Dictionary<int, RowMember> { [0] = at(0) ?? new RowMember(false, 0) };
        int lo = 0, hi = 0;
        while (members.Count <= maxFurnaces && at(lo - 1) is { } left)
            members[--lo] = left;
        while (members.Count <= maxFurnaces && at(hi + 1) is { } right)
            members[++hi] = right;
        int count = hi - lo + 1;
        if (count > maxFurnaces)
            return new FurnaceRow(lo, count, null, 0, null, RowProblem.TooManyFurnaces);
        int? chimney = null;
        for (int i = lo; i <= hi; i++)
            if (!members[i].Capped && members[i].Chimney > 0 && (chimney == null || members[i].Chimney > members[chimney.Value].Chimney))
                chimney = i;
        if (chimney is not { } c)
            return new FurnaceRow(lo, count, null, 0, null, RowProblem.NoChimney);
        int height = members[c].Chimney;
        for (int i = lo; i <= hi; i++)
            if (i != c && !members[i].Capped)
                return new FurnaceRow(lo, count, c, height, i, RowProblem.OutletOpen);
        return new FurnaceRow(lo, count, c, height, null, height >= minChimney ? RowProblem.None : RowProblem.ChimneyTooShort);
    }
}
