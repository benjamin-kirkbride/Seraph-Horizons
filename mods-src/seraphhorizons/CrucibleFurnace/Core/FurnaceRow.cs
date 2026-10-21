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
    /// <summary>The row's chimney has no chimney top on its shaft.</summary>
    NoChimneyTop,
    /// <summary>A chimney top stands partway up the row's chimney, with more chimney on it: the shaft
    /// ends at it, and it is not the top.</summary>
    ChimneyTopBuried,
}

/// <summary>What sits on a chimney's shaft: nothing that is a chimney top, one chimney top that is
/// the column's last chimney block, or a chimney top with more chimney (shaft or another top) on it.</summary>
public enum ChimneyTop
{
    None,
    Top,
    Buried,
}

/// <summary>
/// A chimney as it stands on an outlet: its shaft, the
/// <see cref="CrucibleFurnaceConfig.ChimneyShaftBlocks"/> counted from the outlet up, and what is on
/// the shaft. A chimney draws only with exactly one
/// <see cref="CrucibleFurnaceConfig.ChimneyTopBlocks"/> block on its shaft: the top does not count
/// toward the height, and one lower down ends the shaft there.
/// </summary>
public readonly record struct ChimneyColumn(int Shaft, ChimneyTop Top)
{
    /// <summary>Whether anything of a chimney stands on the outlet.</summary>
    public bool Any => Shaft > 0 || Top != ChimneyTop.None;

    /// <summary>The chimney whose blocks, <paramref name="blockAt"/> giving the code (or null) so many
    /// blocks up from the outlet, are counted to <see cref="FurnaceRow.ChimneyScan"/>.</summary>
    public static ChimneyColumn Measure(Func<int, string?> blockAt, CrucibleFurnaceConfig c)
    {
        int n = 0;
        while (n < FurnaceRow.ChimneyScan && c.IsChimneyShaftBlock(blockAt(n)))
            n++;
        if (!c.IsChimneyTopBlock(blockAt(n)))
            return new ChimneyColumn(n, ChimneyTop.None);
        string? above = blockAt(n + 1);
        return new ChimneyColumn(n, c.IsChimneyShaftBlock(above) || c.IsChimneyTopBlock(above) ? ChimneyTop.Buried : ChimneyTop.Top);
    }
}

/// <summary>A furnace of a row as the row sees it: whether its outlet is capped, and the chimney
/// standing on it.</summary>
public readonly record struct RowMember(bool Capped, ChimneyColumn Chimney);

/// <summary>
/// A row of crucible furnaces on one chimney (README "Crucible furnace", #796): furnaces of the same
/// facing standing side by side along their width, touching, up to
/// <see cref="CrucibleFurnaceConfig.MaxFurnacesInRow"/>. One chimney on any member's outlet serves
/// them all, each at its full draught, as the per-hole flues in one stack did; every other member's
/// outlet must be capped, or it leaks the draught and the whole row has none. Positions are offsets
/// along the row in furnaces from the furnace asking (0), so <see cref="First"/> is 0 or negative.
/// <see cref="ChimneyHeight"/> is the chimney's shaft, which sets the draught.
/// </summary>
public sealed record FurnaceRow(int First, int Count, int? ChimneyAt, int ChimneyHeight, int? OpenOutlet, RowProblem Problem)
{
    /// <summary>The row has its draught: one chimney tall enough and topped, every other outlet capped.</summary>
    public bool Works => Problem == RowProblem.None;

    /// <summary>A furnace alone with no chimney: the row of one not yet found.</summary>
    public static readonly FurnaceRow Alone = new(0, 1, null, 0, null, RowProblem.NoChimney);

    /// <summary>The highest shaft block counted (a taller one is just as good).</summary>
    public const int ChimneyScan = 64;

    /// <summary>The members' offsets, in order along the row.</summary>
    public IEnumerable<int> Members => Enumerable.Range(First, Count);

    /// <summary>
    /// The row of the furnace at offset 0, <paramref name="at"/> telling what stands at an offset (null
    /// for no furnace of the row there). The row runs both ways while there is a member, stopping once
    /// it has more than <paramref name="maxFurnaces"/>. Its chimney is the tallest shaft on an uncapped
    /// outlet (the first along the row on a tie); any other uncapped outlet, with a chimney or without,
    /// leaks. A leak is named first, then a buried chimney top, a short shaft and a missing top.
    /// </summary>
    public static FurnaceRow Find(Func<int, RowMember?> at, int maxFurnaces, int minChimney)
    {
        var members = new Dictionary<int, RowMember> { [0] = at(0) ?? new RowMember(false, default) };
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
            if (!members[i].Capped && members[i].Chimney.Any && (chimney == null || members[i].Chimney.Shaft > members[chimney.Value].Chimney.Shaft))
                chimney = i;
        if (chimney is not { } c)
            return new FurnaceRow(lo, count, null, 0, null, RowProblem.NoChimney);
        var column = members[c].Chimney;
        int height = column.Shaft;
        for (int i = lo; i <= hi; i++)
            if (i != c && !members[i].Capped)
                return new FurnaceRow(lo, count, c, height, i, RowProblem.OutletOpen);
        var problem = column.Top == ChimneyTop.Buried ? RowProblem.ChimneyTopBuried
            : height < minChimney ? RowProblem.ChimneyTooShort
            : column.Top == ChimneyTop.None ? RowProblem.NoChimneyTop
            : RowProblem.None;
        return new FurnaceRow(lo, count, c, height, null, problem);
    }
}
