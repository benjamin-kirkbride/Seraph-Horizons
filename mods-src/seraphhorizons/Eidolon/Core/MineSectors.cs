namespace SeraphHorizons.Mod.Eidolon.Core;

/// <summary>A sector of a mining job's area (README "Eidolon", working together): the block columns
/// <c>MinX</c> to <c>MaxX</c> and <c>MinZ</c> to <c>MaxZ</c>, every layer of the area.</summary>
public readonly record struct MineSector(int MinX, int MinZ, int MaxX, int MaxZ)
{
    public int SizeX => MaxX - MinX + 1;
    public int SizeZ => MaxZ - MinZ + 1;
    public double CentreX => (MinX + MaxX + 1) / 2.0;
    public double CentreZ => (MinZ + MaxZ + 1) / 2.0;

    public bool Contains(int x, int z) => x >= MinX && x <= MaxX && z >= MinZ && z <= MaxZ;

    public override string ToString() => $"({MinX},{MinZ})-({MaxX},{MaxZ})";
}

/// <summary>
/// Splitting a mining job's area into sectors, one a member (README "Eidolon", working together),
/// game-independent: strips across the area's long side for up to <see cref="StripsUpTo"/>, a grid of
/// near-square blocks for more; every sector at least <see cref="MinSide"/> wide each way (or the area's
/// own side, where that is less: it is not cut), and cut only at the 2 × 2 squares' edges, so each
/// square is in one sector. A sector is halved across its long side for a steal (<see cref="Halves"/>).
/// </summary>
public static class EidolonSectors
{
    /// <summary>A sector's least width either way: two squares, room for the strikes and the 1.7-wide box.</summary>
    public const int MinSide = 4;

    /// <summary>Up to this many members, strips; more, a grid.</summary>
    public const int StripsUpTo = 3;

    /// <summary>The most parts a side of <paramref name="length"/> is cut into: one for a side under
    /// <see cref="MinSide"/>, which is not cut.</summary>
    public static int MostParts(int length) => Math.Max(1, length / MinSide);

    /// <summary>Cuts the run <paramref name="min"/> to <paramref name="max"/> into
    /// <paramref name="parts"/> (at most <see cref="MostParts"/>), as evenly as the squares let it: each
    /// cut an even number of blocks from <paramref name="min"/> (the area's or a sector's lower edge,
    /// itself on a square's edge), each part at least <see cref="MinSide"/>.</summary>
    public static IReadOnlyList<(int Min, int Max)> Cut(int min, int max, int parts)
    {
        int length = max - min + 1;
        parts = Math.Clamp(parts, 1, MostParts(length));
        var runs = new List<(int, int)>();
        int from = min;
        for (int i = 1; i <= parts; i++)
        {
            int to = i == parts ? max : min + 2 * (i * length / (2 * parts)) - 1;
            runs.Add((from, to));
            from = to + 1;
        }
        return runs;
    }

    /// <summary>
    /// The sectors of <paramref name="area"/> for <paramref name="count"/> members: strips across its
    /// long side for up to <see cref="StripsUpTo"/>; for more, rows across its short side, each cut into
    /// sectors along the long side, the members spread over the rows (one more in the first rows where
    /// they do not divide), the rows chosen for the most sectors, then the squarest. Fewer than
    /// <paramref name="count"/> where the area holds no more at <see cref="MinSide"/>; none for none.
    /// </summary>
    public static IReadOnlyList<MineSector> Split(MarkArea area, int count)
    {
        if (count <= 0)
            return [];
        bool alongX = area.SizeX >= area.SizeZ;
        int longMin = alongX ? area.Min.X : area.Min.Z, longMax = alongX ? area.Max.X : area.Max.Z;
        int shortMin = alongX ? area.Min.Z : area.Min.X, shortMax = alongX ? area.Max.Z : area.Max.X;
        int longSide = longMax - longMin + 1, shortSide = shortMax - shortMin + 1;
        MineSector Make((int Min, int Max) along, (int Min, int Max) across) =>
            alongX ? new MineSector(along.Min, across.Min, along.Max, across.Max) : new MineSector(across.Min, along.Min, across.Max, along.Max);

        if (count <= StripsUpTo)
            return Cut(longMin, longMax, count).Select(s => Make(s, (shortMin, shortMax))).ToList();

        List<MineSector>? best = null;
        double bestAspect = double.MaxValue;
        for (int rows = 1; rows <= Math.Min(count, MostParts(shortSide)); rows++)
        {
            var sectors = new List<MineSector>();
            var bands = Cut(shortMin, shortMax, rows);
            for (int r = 0; r < bands.Count; r++)
            {
                int inRow = count / rows + (r < count % rows ? 1 : 0);
                sectors.AddRange(Cut(longMin, longMax, inRow).Select(s => Make(s, bands[r])));
            }
            double aspect = sectors.Max(s => (double)Math.Max(s.SizeX, s.SizeZ) / Math.Min(s.SizeX, s.SizeZ));
            if (best == null || sectors.Count > best.Count || (sectors.Count == best.Count && aspect < bestAspect))
            {
                best = sectors;
                bestAspect = aspect;
            }
        }
        return best!;
    }

    /// <summary>A sector cut in two across its long side, at a square's edge (as <see cref="Cut"/>): the
    /// half nearer (<paramref name="x"/>, <paramref name="z"/>), its owner, and the far one; null for a
    /// sector too small to halve (a half under <see cref="MinSide"/>).</summary>
    public static (MineSector Near, MineSector Far)? Halves(MineSector sector, double x, double z)
    {
        bool alongX = sector.SizeX >= sector.SizeZ;
        int length = alongX ? sector.SizeX : sector.SizeZ;
        if (length < 2 * MinSide)
            return null;
        var cut = Cut(alongX ? sector.MinX : sector.MinZ, alongX ? sector.MaxX : sector.MaxZ, 2);
        var a = alongX ? sector with { MaxX = cut[0].Max } : sector with { MaxZ = cut[0].Max };
        var b = alongX ? sector with { MinX = cut[1].Min } : sector with { MinZ = cut[1].Min };
        return Distance2(b, x, z) < Distance2(a, x, z) ? (b, a) : (a, b);
    }

    internal static double Distance2(MineSector s, double x, double z) =>
        (s.CentreX - x) * (s.CentreX - x) + (s.CentreZ - z) * (s.CentreZ - z);
}

/// <summary>
/// The sectors of one mining job and who works each (README "Eidolon", working together), shared by
/// its members (<see cref="MineJobCells.Sectors"/>), game-independent. Split afresh, one sector a
/// present member (<see cref="EidolonSectors.Split"/>), only when who is present changes
/// (<see cref="Sync"/>: one joins, leaves, is away or back), each member given the sector nearest it;
/// those past what the area holds get none. Otherwise a sector changes hands only when its member has
/// nothing left in it it may work: it <see cref="Take"/>s a sector let go with work in it, or steals
/// the far half of the sector with most work left, or lets its own go (<see cref="LetGo"/>).
/// <see cref="Version"/> counts every change, so a member sees that its sector may have moved.
/// </summary>
public sealed class MineSectors
{
    private readonly List<(MineSector Sector, long? Owner)> _sectors = new();
    private long[] _splitFor = [];
    private MarkArea? _area;

    /// <summary>Every sector and its member, null for one let go.</summary>
    public IReadOnlyList<(MineSector Sector, long? Owner)> All => _sectors;

    /// <summary>Counts each change: a split, a steal, a sector taken or let go.</summary>
    public int Version { get; private set; }

    /// <summary>The steals so far (the job's life).</summary>
    public int Steals { get; private set; }

    /// <summary>The sector of <paramref name="member"/>, or null for none (a spare, or it let its go).</summary>
    public MineSector? Of(long member)
    {
        foreach (var (sector, owner) in _sectors)
            if (owner == member)
                return sector;
        return null;
    }

    /// <summary>The member working the column (x, z), or null for none.</summary>
    public long? OwnerAt(int x, int z)
    {
        foreach (var (sector, owner) in _sectors)
            if (sector.Contains(x, z))
                return owner;
        return null;
    }

    /// <summary>Splits <paramref name="area"/> afresh among <paramref name="present"/> (in the order they
    /// joined) when they are not those it was last split for (or there is no split yet): each member
    /// given the sector nearest where it is (<paramref name="at"/>, null where it is not known), nearest
    /// pair first. True when it split.</summary>
    public bool Sync(MarkArea area, IReadOnlyList<long> present, Func<long, (double X, double Z)?> at)
    {
        var members = present.OrderBy(id => id).ToArray();
        if (_area == area && members.SequenceEqual(_splitFor))
            return false;
        _area = area;
        _splitFor = members;
        _sectors.Clear();
        var sectors = EidolonSectors.Split(area, present.Count);
        var owners = new long?[sectors.Count];
        var pairs = new List<(int Member, int Sector, bool Known, double D2)>();
        for (int m = 0; m < present.Count; m++)
        for (int s = 0; s < sectors.Count; s++)
            pairs.Add(at(present[m]) is { } p ? (m, s, true, EidolonSectors.Distance2(sectors[s], p.X, p.Z)) : (m, s, false, 0));
        var given = new HashSet<int>();
        foreach (var (m, s, _, _) in pairs.OrderByDescending(p => p.Known).ThenBy(p => p.D2).ThenBy(p => p.Member).ThenBy(p => p.Sector))
        {
            if (owners[s] != null || given.Contains(m))
                continue;
            owners[s] = present[m];
            given.Add(m);
        }
        for (int s = 0; s < sectors.Count; s++)
            _sectors.Add((sectors[s], owners[s]));
        Version++;
        return true;
    }

    /// <summary>
    /// For <paramref name="member"/>, which has nothing left in its sector it may work (or has none): a
    /// sector let go with work in it (<paramref name="work"/>: how much it may work there now), the most
    /// first; else a steal, from the sector of another with the most work, of the half of it away from
    /// its member (<see cref="EidolonSectors.Halves"/>, by where it is, <paramref name="at"/>), if that
    /// half has work and the sector halves; and the member's own sector is let go. True when it has a
    /// new sector.
    /// </summary>
    public bool Take(long member, Func<MineSector, int> work, Func<long, (double X, double Z)?> at)
    {
        var from = at(member);
        int best = -1, most = 0;
        double near = double.MaxValue;
        for (int i = 0; i < _sectors.Count; i++)
        {
            if (_sectors[i].Owner != null)
                continue;
            int w = work(_sectors[i].Sector);
            if (w <= 0)
                continue;
            double d = from is { } f ? EidolonSectors.Distance2(_sectors[i].Sector, f.X, f.Z) : 0;
            if (w > most || (w == most && d < near))
                (best, most, near) = (i, w, d);
        }
        if (best >= 0)
        {
            LetGo(member);
            _sectors[best] = (_sectors[best].Sector, member);
            Version++;
            return true;
        }
        var busiest = Enumerable.Range(0, _sectors.Count)
            .Where(i => _sectors[i].Owner is { } o && o != member)
            .Select(i => (Index: i, Work: work(_sectors[i].Sector)))
            .Where(s => s.Work > 0).OrderByDescending(s => s.Work).ThenBy(s => s.Index).ToList();
        foreach (var (i, _) in busiest)
        {
            var (sector, owner) = _sectors[i];
            var o = at(owner!.Value);
            if (EidolonSectors.Halves(sector, o?.X ?? sector.MinX, o?.Z ?? sector.MinZ) is not { } halves || work(halves.Far) <= 0)
                continue;
            LetGo(member);
            _sectors[i] = (halves.Near, owner);
            _sectors.Add((halves.Far, member));
            Steals++;
            Version++;
            return true;
        }
        return false;
    }

    /// <summary>The member lets its sector go: no one's, for any member with nothing to <see cref="Take"/>.</summary>
    public void LetGo(long member)
    {
        for (int i = 0; i < _sectors.Count; i++)
            if (_sectors[i].Owner == member)
            {
                _sectors[i] = (_sectors[i].Sector, null);
                Version++;
            }
    }
}
