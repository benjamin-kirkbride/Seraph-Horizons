namespace SeraphHorizons.Mod.Eidolon.Core;

/// <summary>Whether a mine order's covered part is one the eidolon fits in (<see cref="EidolonTunnels.Fit"/>):
/// it is, or why not, told to the player as the area is marked. Only an area covered all over is
/// refused; whether its face can be struck is the planner's to find, and what it cannot reach is
/// reported, not refused.</summary>
public enum TunnelFit
{
    /// <summary>It fits, or not all of it is covered.</summary>
    Fits,

    /// <summary>Under <see cref="EidolonTunnels.MinHeight"/> tall: the eidolon does not fit in it.</summary>
    TooLow,

    /// <summary>Under <see cref="EidolonTunnels.MinWidth"/> across both ways.</summary>
    TooNarrow,
}

/// <summary>A rectangle of block columns, <c>MinX</c> to <c>MaxX</c> and <c>MinZ</c> to <c>MaxZ</c>, every
/// layer: a tunnel's bounds or one of its lanes (README "Eidolon", mining, tunnels), or the columns a
/// miner's station claims (README "Eidolon", working together, *Claims*).</summary>
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
/// The covered part of a mine order's area, dug as a tunnel (README "Eidolon", mining, tunnels): the
/// block columns roofed over (<see cref="Covered"/>), their bounds, one heading (the layers
/// <see cref="Bottom"/> to <see cref="Top"/>, driven face-first: the area's top
/// <see cref="EidolonTunnels.HeadingHeight"/> first, then each next one down, <see cref="Below"/>) and
/// the way it is driven in (<see cref="DriveX"/>, <see cref="DriveZ"/>: one of them ±1, from the open
/// face into the hill), the same for every heading. A slice is a row of columns across the drive,
/// numbered from the mouth; a lane is a run of columns 2 wide (3 for an odd width's last) along it.
/// </summary>
public sealed record MineTunnel(IReadOnlySet<(int X, int Z)> Covered, MineSector Bounds, int Top, int Bottom, int DriveX, int DriveZ)
{
    /// <summary>Whether it is driven along x (else along z).</summary>
    public bool AlongX => DriveX != 0;

    /// <summary>How many slices it is long, and how many columns wide across the drive.</summary>
    public int Length => AlongX ? Bounds.SizeX : Bounds.SizeZ;
    public int Width => AlongX ? Bounds.SizeZ : Bounds.SizeX;

    /// <summary>Whether a cell is in the heading: in a covered column, from <see cref="Bottom"/> to <see cref="Top"/>.</summary>
    public bool Contains(MarkPos cell) => cell.Y >= Bottom && cell.Y <= Top && Covered.Contains((cell.X, cell.Z));

    /// <summary>Whether a cell is in a heading over this one (driven before it): in a covered column, over <see cref="Top"/>.</summary>
    public bool Above(MarkPos cell) => cell.Y > Top && Covered.Contains((cell.X, cell.Z));

    /// <summary>The next heading down, driven once this one is: its top this one's floor layer, at most
    /// <see cref="EidolonTunnels.HeadingHeight"/> high, down to the area's bottom (<paramref name="minY"/>);
    /// null for none (this one reaches the bottom).</summary>
    public MineTunnel? Below(int minY) =>
        Bottom - 1 < minY ? null : this with { Top = Bottom - 1, Bottom = Math.Max(minY, Bottom - EidolonTunnels.HeadingHeight) };

    /// <summary>The slice a column is in: how far from the mouth, 0 the first.</summary>
    public int Slice(int x, int z) =>
        DriveX > 0 ? x - Bounds.MinX : DriveX < 0 ? Bounds.MaxX - x : DriveZ > 0 ? z - Bounds.MinZ : Bounds.MaxZ - z;

    /// <summary>The column at <paramref name="slice"/> and <paramref name="across"/> (a world x for one
    /// driven along z, a world z for one along x).</summary>
    public (int X, int Z) Column(int slice, int across) =>
        DriveX > 0 ? (Bounds.MinX + slice, across) : DriveX < 0 ? (Bounds.MaxX - slice, across)
        : DriveZ > 0 ? (across, Bounds.MinZ + slice) : (across, Bounds.MaxZ - slice);

    /// <summary>Its lanes, side by side across the drive from the bounds' lower edge, each the whole
    /// length (<see cref="EidolonTunnels.Lanes"/>).</summary>
    public IReadOnlyList<MineSector> Lanes => _lanes ??= EidolonTunnels.Lanes(this);
    private IReadOnlyList<MineSector>? _lanes;

    /// <summary>The middle of the mouth, a block outside the first slice: where the way in is measured from.</summary>
    public (double X, double Z) Mouth => (
        AlongX ? (DriveX > 0 ? Bounds.MinX - 0.5 : Bounds.MaxX + 1.5) : Bounds.CentreX,
        AlongX ? Bounds.CentreZ : (DriveZ > 0 ? Bounds.MinZ - 0.5 : Bounds.MaxZ + 1.5));
}

/// <summary>
/// Mining a covered area as a tunnel (README "Eidolon", mining, tunnels), game-independent. A column of
/// the area with the ground over it within <see cref="RoofRise"/> of the area's top is covered: quarried
/// top down, its top layer would be the hill's ceiling, out of reach from the mouth. The covered part is
/// driven in face-first instead, in bands <see cref="HeadingHeight"/> high from the top (a band's floor the
/// next one's ceiling), each lane's face a slice at a time (<see cref="LaneFront"/>: the top half of the
/// face, then the bottom half, then a step in), the way in the one its face is reached from
/// (<see cref="Shape"/>); the planner orders the stations (<see cref="MinePlanner"/>).
/// </summary>
public static class EidolonTunnels
{
    /// <summary>How far over the area's top layer it looks for a roof: a column with ground within this
    /// is covered.</summary>
    public const int RoofRise = 3;

    /// <summary>The least a covered part may be: 4 high (the eidolon is 3.75 tall) and 2 wide across the
    /// drive (it is 1.7 wide).</summary>
    public const int MinHeight = 4;
    public const int MinWidth = 2;

    /// <summary>The heading's height: what it reaches from its floor, its feet' block and the 3 over.</summary>
    public const int HeadingHeight = EidolonMining.ReachAbove + 1;

    /// <summary>A lane's width: one eidolon's face (an odd width's last lane is 3).</summary>
    public const int LaneWidth = 2;

    /// <summary>The ways in, in the order ties go: from the west (along +x), the east, the north (+z), the south.</summary>
    private static readonly (int Dx, int Dz)[] Drives = [(1, 0), (-1, 0), (0, 1), (0, -1)];

    /// <summary>The block columns of <paramref name="area"/> covered: with a block of
    /// <paramref name="roof"/> (the game's: a full block of ground) within <see cref="RoofRise"/> over its
    /// top layer.</summary>
    public static HashSet<(int X, int Z)> Covered(MarkArea area, Func<MarkPos, bool> roof)
    {
        HashSet<(int X, int Z)>? result = null;
        foreach (var _ in Covered(area, roof, c => result = c)) { }
        return result!;
    }

    /// <summary><see cref="Covered(MarkArea, Func{MarkPos, bool})"/> a few block columns a step (the
    /// planner's slices), handing the result to <paramref name="done"/>.</summary>
    public static IEnumerable<bool> Covered(MarkArea area, Func<MarkPos, bool> roof, Action<HashSet<(int X, int Z)>> done)
    {
        var covered = new HashSet<(int X, int Z)>();
        int columns = 0;
        for (int x = area.Min.X; x <= area.Max.X; x++)
        for (int z = area.Min.Z; z <= area.Max.Z; z++)
        {
            MineWork.Add(RoofRise);
            for (int y = area.Max.Y + 1; y <= area.Max.Y + RoofRise; y++)
                if (roof(new MarkPos(x, y, z)))
                {
                    covered.Add((x, z));
                    break;
                }
            if (++columns % 64 == 0)
                yield return false;
        }
        done(covered);
        yield return true;
    }

    /// <summary>The bounds of <paramref name="columns"/> (at least one).</summary>
    public static MineSector Bounds(IEnumerable<(int X, int Z)> columns)
    {
        var list = columns.ToList();
        return new MineSector(list.Min(c => c.X), list.Min(c => c.Z), list.Max(c => c.X), list.Max(c => c.Z));
    }

    /// <summary>The tunnel of <paramref name="covered"/> (some columns of <paramref name="area"/>) driven
    /// in along (<paramref name="dx"/>, <paramref name="dz"/>).</summary>
    public static MineTunnel Make(MarkArea area, IReadOnlySet<(int X, int Z)> covered, int dx, int dz) =>
        new(covered, Bounds(covered), area.Max.Y, Math.Max(area.Min.Y, area.Max.Y - HeadingHeight + 1), dx, dz);

    /// <summary>A tunnel's lanes: 2 wide across the drive from its bounds' lower edge, the last 3 where
    /// the width is odd, one lane for a width under 2.</summary>
    public static IReadOnlyList<MineSector> Lanes(MineTunnel tunnel)
    {
        var b = tunnel.Bounds;
        int min = tunnel.AlongX ? b.MinZ : b.MinX, max = tunnel.AlongX ? b.MaxZ : b.MaxX;
        int count = Math.Max(1, (max - min + 1) / LaneWidth);
        var lanes = new List<MineSector>();
        for (int i = 0; i < count; i++)
        {
            int from = min + i * LaneWidth, to = i == count - 1 ? max : from + LaneWidth - 1;
            lanes.Add(tunnel.AlongX ? b with { MinZ = from, MaxZ = to } : b with { MinX = from, MaxX = to });
        }
        return lanes;
    }

    /// <summary>
    /// The next face group of one lane: the first slice with a cell <paramref name="left"/> in the
    /// heading; of it, the top half (the heading's top two layers) while any of it is left, then the
    /// bottom half (the two under), each two columns across at a time, upright. Null for a lane done.
    /// </summary>
    public static (int Slice, MineGroup Group)? LaneFront(MineTunnel tunnel, MineSector lane, Func<MarkPos, bool> left)
    {
        int acrossMin = tunnel.AlongX ? lane.MinZ : lane.MinX, acrossMax = tunnel.AlongX ? lane.MaxZ : lane.MaxX;
        for (int s = 0; s < tunnel.Length; s++)
        for (int half = 0; half < 2; half++)
        {
            int high = tunnel.Top - 2 * half, low = Math.Max(tunnel.Bottom, high - 1);
            if (high < tunnel.Bottom)
                break;
            for (int a = acrossMin; a <= acrossMax; a += 2)
            {
                var cells = new List<MarkPos>();
                for (int across = a; across <= Math.Min(a + 1, acrossMax); across++)
                {
                    var (x, z) = tunnel.Column(s, across);
                    for (int y = high; y >= low; y--)
                    {
                        var cell = new MarkPos(x, y, z);
                        if (tunnel.Contains(cell) && left(cell))
                            cells.Add(cell);
                    }
                }
                if (cells.Count > 0)
                    return (s, new MineGroup(cells, true));
            }
        }
        return null;
    }

    /// <summary>Whether the covered part of <paramref name="area"/> (<paramref name="covered"/>) is one the
    /// eidolon fits in, as the area is marked (README "Eidolon", mining, tunnels): only an area covered all
    /// over is refused, under <see cref="MinHeight"/> tall or under <see cref="MinWidth"/> across both
    /// ways. Whether its face can be struck is the planner's to find (<see cref="Shape"/>, the reach
    /// bound): what it cannot reach is reported, not refused.</summary>
    public static TunnelFit Fit(MarkArea area, IReadOnlySet<(int X, int Z)> covered)
    {
        if (covered.Count == 0 || covered.Count < area.SizeX * area.SizeZ)
            return TunnelFit.Fits;
        if (area.SizeY < MinHeight)
            return TunnelFit.TooLow;
        var b = Bounds(covered);
        return Math.Min(b.SizeX, b.SizeZ) < MinWidth ? TunnelFit.TooNarrow : TunnelFit.Fits;
    }

    /// <summary>
    /// The covered part's shape (README "Eidolon", mining, *Stations*), for its bands, lanes and slices:
    /// of the four ways in, the one whose first slice has most clearable cells (<paramref name="reach"/>)
    /// reached by a potential stand in front of its mouth, its box short of that slice and across the
    /// tunnel's width (its natural footing: the ground at the mouth, a ledge, a step); ties to the mouth nearest <paramref name="home"/>,
    /// then the order of <see cref="Drives"/>. With no face anywhere it is still shaped (the way in
    /// nearest home): what it cannot reach is the reach bound's to say. Null with nothing covered.
    /// </summary>
    public static MineTunnel? Shape(MarkArea area, IReadOnlySet<(int X, int Z)> covered, MineReach reach, PathCell? home)
    {
        MineTunnel? result = null;
        foreach (var _ in Shape(area, covered, reach, home, t => result = t)) { }
        return result;
    }

    /// <summary><see cref="Shape(MarkArea, IReadOnlySet{ValueTuple{int, int}}, MineReach, PathCell?)"/> a
    /// column of a way in's first slice a step (the planner's slices), handing the result to
    /// <paramref name="done"/>.</summary>
    public static IEnumerable<bool> Shape(MarkArea area, IReadOnlySet<(int X, int Z)> covered, MineReach reach, PathCell? home,
        Action<MineTunnel?> done)
    {
        if (covered.Count == 0)
        {
            done(null);
            yield return true;
            yield break;
        }
        (MineTunnel Tunnel, int Count, double D2)? best = null;
        foreach (var (dx, dz) in Drives)
        {
            var tunnel = Make(area, covered, dx, dz);
            var b = tunnel.Bounds;
            int count = 0;
            for (int across = tunnel.AlongX ? b.MinZ : b.MinX; across <= (tunnel.AlongX ? b.MaxZ : b.MaxX); across++)
            {
                var (x, z) = tunnel.Column(0, across);
                if (!covered.Contains((x, z)))
                    continue;
                MineWork.Add(area.SizeY);
                for (int y = area.Min.Y; y <= area.Max.Y; y++)
                    if (reach.Clearable.Contains(new MarkPos(x, y, z)) && ReachedFromFront(new MarkPos(x, y, z), tunnel, reach))
                        count++;
                yield return false;
            }
            var (mx, mz) = tunnel.Mouth;
            double d2 = home is { } h ? (mx - h.X) * (mx - h.X) + (mz - h.Z) * (mz - h.Z) : 0;
            if (best == null || count > best.Value.Count || (count == best.Value.Count && d2 < best.Value.D2))
                best = (tunnel, count, d2);
        }
        done(best!.Value.Tunnel);
        yield return true;
    }

    // Whether a potential stand in front of the mouth reaches a cell: its box's columns short of the first
    // slice, and across the tunnel's width at least in part (one off to the side reaches a corner of the
    // face, not the face).
    private static bool ReachedFromFront(MarkPos cell, MineTunnel tunnel, MineReach reach)
    {
        var b = tunnel.Bounds;
        MineWork.Add(6 * 6 * 5);
        for (int x = cell.X - 2; x <= cell.X + 3; x++)
        for (int z = cell.Z - 2; z <= cell.Z + 3; z++)
        {
            if (tunnel.Slice(x, z) >= 0 || tunnel.Slice(x - 1, z - 1) >= 0)
                continue;
            if (tunnel.AlongX ? z - 1 > b.MaxZ || z < b.MinZ : x - 1 > b.MaxX || x < b.MinX)
                continue;
            for (int y = cell.Y - 3; y <= cell.Y + 1; y++)
                if (EidolonMining.InReach(x, y, z, cell) && reach.Potential(new PathCell(x, y, z)))
                    return true;
        }
        return false;
    }
}
