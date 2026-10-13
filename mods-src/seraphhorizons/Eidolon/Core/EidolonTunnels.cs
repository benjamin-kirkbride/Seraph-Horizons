namespace SeraphHorizons.Mod.Eidolon.Core;

/// <summary>Whether a mine order's covered part can be dug as a tunnel (<see cref="EidolonTunnels.Plan"/>):
/// it can, or why not, told to the player as the area is marked. Only an area covered all over is
/// refused: one covered in part is quarried top down where its covered part cannot be a tunnel.</summary>
public enum TunnelFit
{
    /// <summary>It can, or there is no tunnel and the whole area is quarried top down: nothing of it is
    /// covered, or only part is and that part cannot be a tunnel.</summary>
    Fits,

    /// <summary>Under <see cref="EidolonTunnels.MinHeight"/> tall: the eidolon does not fit in it.</summary>
    TooLow,

    /// <summary>No side of it opens onto a place the eidolon can stand to strike its first face.</summary>
    NoFace,

    /// <summary>Its open faces are all under <see cref="EidolonTunnels.MinWidth"/> wide across.</summary>
    TooNarrow,
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
/// top down, its top layer would be the hill's ceiling, out of reach from the mouth. The covered part's
/// heading (its top <see cref="HeadingHeight"/> layers) is driven in face-first from its open face instead,
/// a lane at a time (<see cref="Front"/>: the top half of the face, then the bottom half, then a step in),
/// each member of a job its own lanes (<see cref="Headings"/>). A covered part taller than that is benched:
/// once a heading is driven, the next one down (<see cref="MineTunnel.Below"/>), its ceiling the last one's
/// floor, is driven the same way from its own open face, on the same side at its own level
/// (<see cref="Opens"/>), so its floor leads straight out of its own mouth; where a heading has none (the
/// hill's face does not reach down to it), what is left is quarried top down from the floor over it, as
/// the open part is.
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
        var covered = new HashSet<(int X, int Z)>();
        for (int x = area.Min.X; x <= area.Max.X; x++)
        for (int z = area.Min.Z; z <= area.Max.Z; z++)
            for (int y = area.Max.Y + 1; y <= area.Max.Y + RoofRise; y++)
                if (roof(new MarkPos(x, y, z)))
                {
                    covered.Add((x, z));
                    break;
                }
        return covered;
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

    /// <summary>
    /// The face group to strike next in <paramref name="sector"/> (a member's lanes; the whole tunnel
    /// alone): of the lanes in it, the front (<see cref="LaneFront"/>) furthest back first, so the face
    /// goes in level, then nearest (<paramref name="x"/>, <paramref name="z"/>). Null when none of its
    /// lanes has a cell <paramref name="left"/>.
    /// </summary>
    public static MineGroup? Front(MineTunnel tunnel, MineSector sector, Func<MarkPos, bool> left, double x, double z)
    {
        (int Slice, MineGroup Group, double D2)? best = null;
        foreach (var lane in tunnel.Lanes)
        {
            if (!sector.Contains(lane.MinX, lane.MinZ) || !sector.Contains(lane.MaxX, lane.MaxZ))
                continue;
            if (LaneFront(tunnel, lane, left) is not { } front)
                continue;
            double d2 = (front.Group.CentreX - x) * (front.Group.CentreX - x) + (front.Group.CentreZ - z) * (front.Group.CentreZ - z);
            if (best == null || front.Slice < best.Value.Slice || (front.Slice == best.Value.Slice && d2 < best.Value.D2))
                best = (front.Slice, front.Group, d2);
        }
        return best?.Group;
    }

    /// <summary>
    /// The headings of a tunnel for <paramref name="count"/> members (README "Eidolon", working
    /// together): its lanes split into that many runs side by side, as evenly as they go (the first runs
    /// a lane more where they do not divide), each a sector the whole length; one lane each at most, so
    /// members past the lanes get none and wait. None for none.
    /// </summary>
    public static IReadOnlyList<MineSector> Headings(MineTunnel tunnel, int count)
    {
        var lanes = tunnel.Lanes;
        int n = Math.Min(count, lanes.Count);
        var headings = new List<MineSector>();
        int at = 0;
        for (int i = 0; i < n; i++)
        {
            int take = lanes.Count / n + (i < lanes.Count % n ? 1 : 0);
            var first = lanes[at];
            var last = lanes[at + take - 1];
            headings.Add(new MineSector(first.MinX, first.MinZ, last.MaxX, last.MaxZ));
            at += take;
        }
        return headings;
    }

    /// <summary>
    /// Plans the covered part of <paramref name="area"/> (<paramref name="covered"/>; none: nothing to
    /// plan, <see cref="TunnelFit.Fits"/> and null): no tunnel under <see cref="MinHeight"/> tall; else of
    /// the four ways in, those whose first face it can stand to strike (a lane's front, with the cells
    /// <paramref name="solid"/> as left, that <see cref="EidolonMining.Stands"/> finds a place for in
    /// <paramref name="space"/>), at least <see cref="MinWidth"/> across, the one whose mouth is nearest
    /// (<paramref name="fromX"/>, <paramref name="fromZ"/>); none with none open, or none open wide
    /// enough. A heading with nothing left to break is open from every side. With no tunnel, an area
    /// covered only in part (a pit on a slope, its uphill columns roofed) is quarried top down,
    /// <see cref="TunnelFit.Fits"/> and null; one covered all over is refused, since nothing of it could
    /// be dug, with why.
    /// </summary>
    public static (TunnelFit Fit, MineTunnel? Tunnel) Plan(MarkArea area, IReadOnlySet<(int X, int Z)> covered, IWideSpace space,
        Func<MarkPos, bool> solid, double fromX, double fromZ)
    {
        if (covered.Count == 0)
            return (TunnelFit.Fits, null);
        bool whole = covered.Count == area.SizeX * area.SizeZ;
        if (area.SizeY < MinHeight)
            return (whole ? TunnelFit.TooLow : TunnelFit.Fits, null);
        bool open = false;
        (MineTunnel Tunnel, double D2)? best = null;
        foreach (var (dx, dz) in Drives)
        {
            var tunnel = Make(area, covered, dx, dz);
            if (!Opens(tunnel, space, solid))
                continue;
            open = true;
            if (tunnel.Width < MinWidth)
                continue;
            var (mx, mz) = tunnel.Mouth;
            double d2 = (mx - fromX) * (mx - fromX) + (mz - fromZ) * (mz - fromZ);
            if (best == null || d2 < best.Value.D2)
                best = (tunnel, d2);
        }
        if (best is { } b)
            return (TunnelFit.Fits, b.Tunnel);
        if (!whole)
            return (TunnelFit.Fits, null);
        return (open ? TunnelFit.TooNarrow : TunnelFit.NoFace, null);
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
    /// reached by a live potential stand outside the covered bounds (its natural footing: the ground at
    /// the mouth, a ledge, a step, the hill's flank); ties to the mouth nearest <paramref name="home"/>,
    /// then the order of <see cref="Drives"/>. With no face anywhere it is still shaped (the way in
    /// nearest home): what it cannot reach is the reach bound's to say. Null with nothing covered.
    /// </summary>
    public static MineTunnel? Shape(MarkArea area, IReadOnlySet<(int X, int Z)> covered, MineReach reach, PathCell? home)
    {
        if (covered.Count == 0)
            return null;
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
                for (int y = area.Min.Y; y <= area.Max.Y; y++)
                    if (reach.Clearable.Contains(new MarkPos(x, y, z)) && ReachedFromOutside(new MarkPos(x, y, z), b, reach))
                        count++;
            }
            var (mx, mz) = tunnel.Mouth;
            double d2 = home is { } h ? (mx - h.X) * (mx - h.X) + (mz - h.Z) * (mz - h.Z) : 0;
            if (best == null || count > best.Value.Count || (count == best.Value.Count && d2 < best.Value.D2))
                best = (tunnel, count, d2);
        }
        return best!.Value.Tunnel;
    }

    // Whether a live potential stand whose box is clear of the bounds reaches a cell.
    private static bool ReachedFromOutside(MarkPos cell, MineSector bounds, MineReach reach)
    {
        for (int x = cell.X - 2; x <= cell.X + 3; x++)
        for (int z = cell.Z - 2; z <= cell.Z + 3; z++)
        {
            bool clear = x < bounds.MinX || x - 1 > bounds.MaxX || z < bounds.MinZ || z - 1 > bounds.MaxZ;
            if (!clear)
                continue;
            for (int y = cell.Y - 3; y <= cell.Y + 1; y++)
                if (EidolonMining.InReach(x, y, z, cell) && reach.Potential(new PathCell(x, y, z)))
                    return true;
        }
        return false;
    }

    /// <summary>Whether a tunnel driven its way has an open face: a lane whose front (its cells
    /// <paramref name="solid"/>) it finds a place to stand and strike from in front of the mouth
    /// (<see cref="InFront"/>), or none with any left.</summary>
    public static bool Opens(MineTunnel tunnel, IWideSpace space, Func<MarkPos, bool> solid)
    {
        bool any = false;
        foreach (var lane in tunnel.Lanes)
        {
            if (LaneFront(tunnel, lane, solid) is not { } front)
                continue;
            any = true;
            var (mx, mz) = tunnel.Mouth;
            if (EidolonMining.Stands(space, front.Group.Cells, solid, mx, tunnel.Bottom, mz).Any(s => InFront(tunnel, s)))
                return true;
        }
        return !any;
    }

    /// <summary>Whether a path node stands in front of a tunnel's mouth: its box's columns (the two each
    /// side of it) short of the first slice, and across the tunnel's width, at least in part; one beside
    /// it, or at a corner, reaches a lane's first face but none past it.</summary>
    public static bool InFront(MineTunnel tunnel, PathCell node)
    {
        if (tunnel.Slice(node.X - 1, node.Z - 1) >= 0 || tunnel.Slice(node.X, node.Z) >= 0)
            return false;
        var b = tunnel.Bounds;
        return tunnel.AlongX ? node.Z - 1 <= b.MaxZ && node.Z >= b.MinZ : node.X - 1 <= b.MaxX && node.X >= b.MinX;
    }
}
