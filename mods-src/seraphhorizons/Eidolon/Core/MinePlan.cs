namespace SeraphHorizons.Mod.Eidolon.Core;

/// <summary>What a station is (README "Eidolon", mining, *Stations*): part of a bench in the open, of a
/// covered band's face, or of a stair dug and kept until last.</summary>
public enum MineStationKind { Bench, Face, Stair }

/// <summary>One station (README "Eidolon", mining, *Stations*): where it stands, what it clears from
/// there, its band (<see cref="MinePlan"/>: precedence), and in a tunnel its lane and slice.</summary>
public sealed record MineStation(int Id, PathCell Stand, IReadOnlyList<MarkPos> Cells, int Band,
    MineStationKind Kind, int Lane = -1, int Slice = -1)
{
    public int Top => Cells.Max(c => c.Y);
}

/// <summary>
/// A mine order's plan (README "Eidolon", mining, *Stations*): its stations in the order the simulation
/// dug them, and what it leaves: cells out of its reach under every order (<see cref="OutOfReach"/>), the
/// floor kept under them (<see cref="Held"/>), cells left for what they are (<see cref="Left"/>), and the
/// stair it digs last, of which what it must stand on to leave stays (<see cref="WayOut"/>).
/// </summary>
public sealed class MinePlan
{
    public required IReadOnlyList<MineStation> Stations { get; init; }
    public required IReadOnlySet<MarkPos> OutOfReach { get; init; }
    public required IReadOnlySet<MarkPos> Held { get; init; }
    public required IReadOnlySet<MarkPos> WayOut { get; init; }
    public required IReadOnlySet<MarkPos> Left { get; init; }
    public required IReadOnlySet<MarkPos> Stair { get; init; }

    /// <summary>The covered part's shape (<see cref="EidolonTunnels.Shape"/>), null with none.</summary>
    public MineTunnel? Tunnel { get; init; }

    public int SiteVersion { get; init; }

    /// <summary>Whether some of the area's columns were not loaded: planned without them.</summary>
    public bool Partial { get; init; }

    /// <summary>How high a step in front of the face would bring what is out of reach in reach
    /// (the out-of-reach report's hint), null for none or not known.</summary>
    public int? StepNeeded { get; init; }

    public MarkArea Area { get; init; }

    /// <summary>Every cell it plans to dig: the stations' and the stair's.</summary>
    public IReadOnlySet<MarkPos> Targets { get; init; } = new HashSet<MarkPos>();

    /// <summary>How many times the simulation ran again holding what it stranded (0 when none was).</summary>
    public int Reruns { get; init; }

    /// <summary>A cell's band (<see cref="MineBands"/>): <see cref="MineBands.Never"/> for one it does not dig.</summary>
    public int Band(MarkPos cell) => Targets.Contains(cell) ? MineBands.BandOf(Area, Tunnel, Stair, cell) : MineBands.Never;

    /// <summary>The bands over <paramref name="remaining"/> (a live state's), for dispatch.</summary>
    public MineBands Bands(IEnumerable<MarkPos> remaining) => new(Area, Tunnel, Stair, remaining.Where(Targets.Contains), widen: false);
}

/// <summary>
/// The precedence of a mine order's targets (README "Eidolon", mining, *Stations*: bands): which of the
/// remaining ones may be dug now. A cell in a covered column is in a tunnel band, its tunnel's top
/// <see cref="EidolonTunnels.HeadingHeight"/> layers band 0, the next 1, and so on; only the lowest
/// covered band with a cell left is open. A cell in the open is in the open band, numbered after them:
/// open while its layer is at most one under the highest open layer with a cell left (the floors within a
/// climb, <see cref="EidolonTeamwork.Lowest"/>). While a covered band is being driven, the open part's
/// approach (<see cref="Approach"/>: the columns within <see cref="ApproachColumns"/> of a covered one, where
/// the face is struck from and walked to) is not dug under that band's floor, and keeps its own floors within
/// a climb; the rest of the open part is quarried meanwhile, its floors within a climb of its own highest
/// layer and at most a climb under that band's floor (so the heading's floor and the open ground stay a walk
/// apart). Once no covered band is left the two are one again. A stair's cells (<see cref="Stair"/>) only once
/// nothing else is left.
/// </summary>
public sealed class MineBands
{
    public const int Stair = int.MaxValue;
    public const int Never = -1;

    private readonly MarkArea _area;
    private readonly MineTunnel? _tunnel;
    private readonly IReadOnlySet<MarkPos> _stair;
    private readonly HashSet<MarkPos> _remaining;
    private readonly int[] _covered;
    private readonly List<MarkPos>[] _bandCells;
    // the open part by layer: [0] away from the covered part, [1] its approach (Approach)
    private readonly int[][] _open;
    private readonly List<MarkPos>[][] _layerCells;
    private readonly bool[]? _approach;
    private readonly List<MarkPos> _stairCells;
    private int _nonStair;
    private readonly int[] _layer;
    private readonly List<MarkPos> _widened = new();
    private readonly bool _widen;

    /// <param name="widen">Whether it notes what each cell gone opens (<see cref="TakeWidened"/>, the
    /// planner's simulation; a live state's bands only answer <see cref="Eligible"/>).</param>
    public MineBands(MarkArea area, MineTunnel? tunnel, IReadOnlySet<MarkPos> stair, IEnumerable<MarkPos> remaining, bool widen = true)
    {
        _widen = widen;
        _area = area;
        _tunnel = tunnel;
        _stair = stair;
        _remaining = remaining.ToHashSet();
        CoveredBands = tunnel == null ? 0 : (tunnel.Top - area.Min.Y) / EidolonTunnels.HeadingHeight + 1;
        _covered = new int[CoveredBands];
        _bandCells = Enumerable.Range(0, CoveredBands).Select(_ => new List<MarkPos>()).ToArray();
        _open = [new int[area.SizeY], new int[area.SizeY]];
        _layerCells = [Layers(area), Layers(area)];
        _approach = tunnel == null ? null : ApproachOf(area, tunnel);
        _stairCells = new List<MarkPos>();
        MineWork.Add(_remaining.Count);
        foreach (var c in widen ? Ordered(area, _remaining) : _remaining)
        {
            int band = Band(c);
            if (band == Stair)
                _stairCells.Add(c);
            else if (band < CoveredBands)
            {
                _covered[band]++;
                if (widen)
                    _bandCells[band].Add(c);
                _nonStair++;
            }
            else if (c.Y >= area.Min.Y && c.Y <= area.Max.Y)
            {
                int g = Group(c.X, c.Z);
                _open[g][c.Y - area.Min.Y]++;
                if (widen)
                    _layerCells[g][c.Y - area.Min.Y].Add(c);
                _nonStair++;
            }
        }
        _layer = [area.SizeY - 1, area.SizeY - 1];
        for (int g = 0; g < 2; g++)
            while (_layer[g] >= 0 && _open[g][_layer[g]] == 0)
                _layer[g]--;
    }

    private static List<MarkPos>[] Layers(MarkArea area) => Enumerable.Range(0, area.SizeY).Select(_ => new List<MarkPos>()).ToArray();

    /// <summary>How far from a covered column, in columns, the open part is its approach: a stand's box
    /// (2 wide) and its reach (2 past it) in front of the face.</summary>
    public const int ApproachColumns = 3;

    // The area's columns not covered within ApproachColumns of one that is.
    private static bool[] ApproachOf(MarkArea area, MineTunnel tunnel)
    {
        var approach = new bool[area.SizeX * area.SizeZ];
        MineWork.Add(tunnel.Covered.Count * (2 * ApproachColumns + 1) * (2 * ApproachColumns + 1));
        foreach (var (cx, cz) in tunnel.Covered)
            for (int x = Math.Max(area.Min.X, cx - ApproachColumns); x <= Math.Min(area.Max.X, cx + ApproachColumns); x++)
            for (int z = Math.Max(area.Min.Z, cz - ApproachColumns); z <= Math.Min(area.Max.Z, cz + ApproachColumns); z++)
                if (!tunnel.Covered.Contains((x, z)))
                    approach[(x - area.Min.X) * area.SizeZ + z - area.Min.Z] = true;
        return approach;
    }

    /// <summary>Whether an open column is the covered part's approach (<see cref="ApproachColumns"/> from it).</summary>
    public bool Approach(int x, int z) =>
        _approach != null && _area.Contains(x, _area.Min.Y, z) && _approach[(x - _area.Min.X) * _area.SizeZ + z - _area.Min.Z];

    private int Group(int x, int z) => Approach(x, z) ? 1 : 0;

    // The cells by x, then y, then z: read off the area in that order where they all lie in it and that
    // is the cheaper (a lookup a block of the area; the planner's targets always lie in it), else sorted.
    private static IEnumerable<MarkPos> Ordered(MarkArea area, HashSet<MarkPos> cells)
    {
        long sort = (long)cells.Count * (int.Log2(cells.Count + 1) + 1);
        if (area.Volume <= sort && cells.All(c => area.Contains(c.X, c.Y, c.Z)))
        {
            MineWork.Add(cells.Count + (int)area.Volume);
            var ordered = new List<MarkPos>(cells.Count);
            for (int x = area.Min.X; x <= area.Max.X; x++)
            for (int y = area.Min.Y; y <= area.Max.Y; y++)
            for (int z = area.Min.Z; z <= area.Max.Z; z++)
                if (cells.Contains(new MarkPos(x, y, z)))
                    ordered.Add(new MarkPos(x, y, z));
            return ordered;
        }
        MineWork.Add((int)sort);
        return cells.OrderBy(c => c.X).ThenBy(c => c.Y).ThenBy(c => c.Z);
    }

    private MineBands(MineBands other)
    {
        (_area, _tunnel, _stair) = (other._area, other._tunnel, other._stair);
        _remaining = new HashSet<MarkPos>(other._remaining);
        CoveredBands = other.CoveredBands;
        _covered = (int[])other._covered.Clone();
        _bandCells = other._bandCells;
        _open = [(int[])other._open[0].Clone(), (int[])other._open[1].Clone()];
        _layerCells = other._layerCells;
        _approach = other._approach;
        _stairCells = other._stairCells;
        _nonStair = other._nonStair;
        _layer = (int[])other._layer.Clone();
        _widen = other._widen;
    }

    public MineBands Clone() => new(this);

    /// <summary>How many tunnel bands the covered part has (0 without one); the open band's number.</summary>
    public int CoveredBands { get; }

    public int OpenBand => CoveredBands;

    /// <summary>A cell's band in <paramref name="area"/> with <paramref name="tunnel"/> and <paramref name="stair"/>.</summary>
    public static int BandOf(MarkArea area, MineTunnel? tunnel, IReadOnlySet<MarkPos> stair, MarkPos cell)
    {
        if (stair.Contains(cell))
            return Stair;
        if (tunnel != null && tunnel.Covered.Contains((cell.X, cell.Z)) && cell.Y <= tunnel.Top)
            return (tunnel.Top - cell.Y) / EidolonTunnels.HeadingHeight;
        return tunnel == null ? 0 : (tunnel.Top - area.Min.Y) / EidolonTunnels.HeadingHeight + 1;
    }

    public int Band(MarkPos cell) => BandOf(_area, _tunnel, _stair, cell);

    /// <summary>A covered band's floor layer.</summary>
    public int Bottom(int band) =>
        _tunnel == null ? _area.Min.Y : Math.Max(_area.Min.Y, _tunnel.Top - band * EidolonTunnels.HeadingHeight - EidolonTunnels.HeadingHeight + 1);

    /// <summary>The covered band being driven: the lowest with a cell left; null for none.</summary>
    public int? Covered
    {
        get
        {
            for (int b = 0; b < CoveredBands; b++)
                if (_covered[b] > 0)
                    return b;
            return null;
        }
    }

    /// <summary>The highest open layer with a cell left; null for none.</summary>
    public int? Layer => Math.Max(_layer[0], _layer[1]) is var l and >= 0 ? _area.Min.Y + l : null;

    /// <summary>The lowest open layer it may dig now, anywhere in the open part.</summary>
    public int OpenFloor => Math.Min(Floor(0), Floor(1));

    /// <summary>The lowest layer it may dig now in an open column: in the approach while a covered band is
    /// being driven, within a climb of the approach's highest layer and not under that band's floor; else
    /// within a climb of the highest layer of the rest and, while a covered band is being driven, at most a
    /// climb under its floor (of all the open part, once no covered band is left).</summary>
    public int OpenFloorAt(int x, int z) => Floor(Group(x, z));

    /// <summary>The lowest open layer of the approach (1) or of the rest (0) it may dig now.</summary>
    public int Floor(int group)
    {
        var covered = Covered;
        int layer = covered == null ? Math.Max(_layer[0], _layer[1]) : _layer[group];
        if (layer < 0)
            return int.MaxValue;
        int floor = _area.Min.Y + layer - EidolonTeamwork.FloorStep + 1;
        return covered is { } b ? Math.Max(floor, Bottom(b) - (group == 1 ? 0 : EidolonTeamwork.FloorStep)) : floor;
    }

    /// <summary>Whether the stair's turn has come: nothing else left.</summary>
    public bool StairOpen => _nonStair == 0;

    public bool Has(MarkPos cell) => _remaining.Contains(cell);

    public IEnumerable<MarkPos> RemainingCells => _remaining;

    /// <summary>Whether a remaining cell may be dug now.</summary>
    public bool Eligible(MarkPos cell)
    {
        if (!_remaining.Contains(cell))
            return false;
        int band = Band(cell);
        if (band == Stair)
            return StairOpen;
        if (band < CoveredBands)
            return Covered == band;
        return cell.Y >= OpenFloorAt(cell.X, cell.Z);
    }

    /// <summary>A cell dug (or gone): what it opens is noted (<see cref="TakeWidened"/>).</summary>
    public void Remove(MarkPos cell)
    {
        if (!_remaining.Contains(cell))
            return;
        var covered = Covered;
        int floor0 = Floor(0), floor1 = Floor(1);
        bool stairOpen = StairOpen;
        _remaining.Remove(cell);
        int band = Band(cell);
        if (band == Stair) { }
        else if (band < CoveredBands)
        {
            _covered[band]--;
            _nonStair--;
        }
        else if (cell.Y >= _area.Min.Y && cell.Y <= _area.Max.Y)
        {
            int g = Group(cell.X, cell.Z);
            _open[g][cell.Y - _area.Min.Y]--;
            _nonStair--;
            while (_layer[g] >= 0 && _open[g][_layer[g]] == 0)
                _layer[g]--;
        }
        if (!_widen)
            return;
        if (Covered is { } b && b != covered)
            Widen(_bandCells[b]);
        for (int g = 0; g < 2; g++)
        {
            int now = Floor(g), floor = g == 0 ? floor0 : floor1;
            if (now < floor)
                for (int y = Math.Max(now, _area.Min.Y); y < Math.Min(floor, _area.Max.Y + 1); y++)
                    Widen(_layerCells[g][y - _area.Min.Y]);
        }
        if (StairOpen && !stairOpen)
            Widen(_stairCells);
    }

    private void Widen(List<MarkPos> cells)
    {
        MineWork.Add(cells.Count);
        _widened.AddRange(cells.Where(_remaining.Contains));
    }

    /// <summary>The cells that became eligible since the last call.</summary>
    public IReadOnlyList<MarkPos> TakeWidened()
    {
        MineWork.Add(_widened.Count);
        var list = _widened.ToList();
        _widened.Clear();
        return list;
    }
}
