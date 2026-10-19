namespace SeraphHorizons.Mod.Eidolon.Core;

/// <summary>What the planner reads of one block of its window: whether it collides (the box test,
/// footing), what it is to the order (<see cref="EidolonMining.Rule"/>, the land claim not asked), and its
/// pace (<paramref name="Seconds"/>: the seconds a seraph takes to break it with the eidolon's better tool,
/// <see cref="EidolonMining.PlayerSeconds"/> at <see cref="EidolonMining.DigSpeed"/>; NaN where not known),
/// which the routes' work estimate reads (README "Eidolon", working together, *Routes*).</summary>
public readonly record struct MineVoxel(bool Solid, MineRule Rule, float Seconds = float.NaN);

/// <summary>The world as the mine planner reads it: the game's block accessor, or a test's voxels.</summary>
public interface IMineWorld
{
    /// <summary>Whether the block column at (<paramref name="x"/>, <paramref name="z"/>) is loaded.</summary>
    bool Loaded(int x, int z);

    MineVoxel Read(int x, int y, int z);
}

/// <summary>
/// The mine order's window on the world (README "Eidolon", mining, *Stations*): the area grown by
/// <see cref="SideMargin"/> columns each side, from <see cref="BelowMargin"/> under its bottom to
/// <see cref="AboveMargin"/> over its top, so a stand outside the area that reaches into it, the ground
/// it stands on and the ways round it are all in it. Each block is solid or not (it has a collision box:
/// the box test, footing), and each of the area's (and the layer over it, which may hold its column up,
/// <see cref="EidolonMining.Holds"/>) has its rule. Outside the window reads solid; so does a column not
/// loaded, which is no target either (<see cref="UnloadedColumns"/>). The eidolon's box is tested against
/// it as the game's collision tester does (<see cref="Free"/>), so <see cref="WidePath"/> walks it as it is.
/// A job's window is grown to take in a member standing outside it within <see cref="GrowColumns"/> of the
/// area (<see cref="Grown"/>), so the routes can place it on the walk graph (README "Eidolon", working
/// together, *Routes*).
/// </summary>
public sealed class MineSite : IWideSpace
{
    public const int SideMargin = 6, BelowMargin = 2, AboveMargin = 8;

    /// <summary>How far from the area (columns, or layers) a member standing outside the window grows it to
    /// take it in (<see cref="Grown"/>); one farther is not. It bounds the window's memory.</summary>
    public const int GrowColumns = 32;

    private readonly int _x0, _y0, _z0, _sx, _sy, _sz;
    private readonly ulong[] _solid;
    private readonly byte[] _rule;
    private readonly ushort[] _pace;
    private readonly bool[] _unloaded;
    private readonly List<(int X, int Z)> _unloadedArea;
    private readonly List<(int Index, bool Solid, int RuleIndex, byte Rule, ushort Pace)> _journal = new();
    private int _marks;

    /// <summary>The window round <paramref name="area"/>, all solid until it is loaded (<see cref="Load"/>);
    /// <paramref name="step"/>: the most it climbs and drops between two places it stands, both ways
    /// (the least of its climb and its fall, 1 or 2, so every way in is a way back);
    /// <paramref name="window"/>: one grown round its members (<see cref="Grown"/>), else <see cref="Around"/> the area.</summary>
    public MineSite(MarkArea area, int step, MarkArea? window = null)
    {
        Area = area;
        Step = Math.Clamp(step, 1, 2);
        Window = window ?? Around(area);
        (_x0, _y0, _z0) = (Window.Min.X, Window.Min.Y, Window.Min.Z);
        (_sx, _sy, _sz) = (Window.SizeX, Window.SizeY, Window.SizeZ);
        _solid = new ulong[(Volume + 63) / 64];
        Array.Fill(_solid, ulong.MaxValue);
        _rule = new byte[area.SizeX * (area.SizeY + 1) * area.SizeZ];
        _pace = new ushort[_rule.Length];
        _unloaded = new bool[_sx * _sz];
        _unloadedArea = new List<(int X, int Z)>();
        MineWork.Bytes(_solid.Length * 8L + _rule.Length * 3L + _unloaded.Length);
    }

    private MineSite(MineSite other)
    {
        Area = other.Area;
        Window = other.Window;
        Step = other.Step;
        Version = other.Version;
        (_x0, _y0, _z0, _sx, _sy, _sz) = (other._x0, other._y0, other._z0, other._sx, other._sy, other._sz);
        _solid = (ulong[])other._solid.Clone();
        _rule = (byte[])other._rule.Clone();
        _pace = (ushort[])other._pace.Clone();
        _unloaded = (bool[])other._unloaded.Clone();
        _unloadedArea = new List<(int X, int Z)>(other._unloadedArea);
        MineWork.Bytes(_solid.Length * 8L + _rule.Length * 3L + _unloaded.Length);
    }

    public MarkArea Area { get; }

    public MarkArea Window { get; }

    /// <summary>The most it climbs or drops between two places it stands, either way.</summary>
    public int Step { get; }

    /// <summary>Counts every change (not a tentative one, <see cref="Mark"/>), so a plan knows the world it
    /// was made for.</summary>
    public int Version { get; private set; }

    /// <summary>The window's blocks in all.</summary>
    public int Volume => _sx * _sy * _sz;

    /// <summary>Reads the window from <paramref name="world"/>, one block column each step, so the game
    /// spreads it over ticks (README "Eidolon", mining, *Stations*: planning in slices).</summary>
    public IEnumerable<bool> Load(IMineWorld world)
    {
        _unloadedArea.Clear();
        for (int x = Window.Min.X; x <= Window.Max.X; x++)
        for (int z = Window.Min.Z; z <= Window.Max.Z; z++)
        {
            ReadColumn(world, x, z);
            yield return true;
        }
        Version++;
    }

    /// <summary>Reads one block column again from <paramref name="world"/> (one that has loaded since, or
    /// changed): what it was is forgotten.</summary>
    public void Reload(IMineWorld world, int x, int z)
    {
        if (!InWindow(x, Window.Min.Y, z))
            return;
        _unloadedArea.Remove((x, z));
        ReadColumn(world, x, z);
        Version++;
    }

    private void ReadColumn(IMineWorld world, int x, int z)
    {
        bool loaded = world.Loaded(x, z);
        _unloaded[(x - _x0) * _sz + (z - _z0)] = !loaded;
        bool inArea = x >= Area.Min.X && x <= Area.Max.X && z >= Area.Min.Z && z <= Area.Max.Z;
        if (!loaded && inArea)
            _unloadedArea.Add((x, z));
        for (int y = Window.Min.Y; y <= Window.Max.Y; y++)
        {
            var voxel = loaded ? world.Read(x, y, z) : new MineVoxel(true, MineRule.Air);
            SetSolid(Index(x, y, z), voxel.Solid);
            if (RuleIndex(x, y, z) is var r and >= 0)
            {
                _rule[r] = (byte)(loaded ? voxel.Rule : MineRule.Air);
                _pace[r] = loaded ? Pace(voxel.Seconds) : (ushort)0;
            }
        }
    }

    // ---- reading ----

    /// <summary>Whether a block is solid: outside the window, or in a column not loaded, it is.</summary>
    public bool Solid(int x, int y, int z) => !InWindow(x, y, z) || SolidAt(Index(x, y, z));

    /// <summary>What a cell of the area (or the layer over it) is to the order; anything else is air to it.</summary>
    public MineRule Rule(MarkPos cell) => RuleIndex(cell.X, cell.Y, cell.Z) is var r and >= 0 ? (MineRule)_rule[r] : MineRule.Air;

    /// <summary>The seconds a seraph takes to break a cell of the area with the eidolon's better tool, as
    /// read (<see cref="MineVoxel.Seconds"/>): <see cref="MineCosts.DefaultPlayerSeconds"/> where not known or
    /// outside the area (README "Eidolon", working together, *Routes*).</summary>
    public double PlayerSeconds(MarkPos cell) =>
        RuleIndex(cell.X, cell.Y, cell.Z) is var r and >= 0 && _pace[r] != 0 ? _pace[r] / 100.0 : MineCosts.DefaultPlayerSeconds;

    // centiseconds, 1..65535; 0 for not known
    private static ushort Pace(float seconds) =>
        float.IsNaN(seconds) || seconds < 0 ? (ushort)0 : (ushort)Math.Clamp(Math.Round(seconds * 100.0), 1, 65535);

    /// <summary>Whether a block column of the window is not loaded.</summary>
    public bool Unloaded(int x, int z) =>
        x >= _x0 && x < _x0 + _sx && z >= _z0 && z < _z0 + _sz && _unloaded[(x - _x0) * _sz + (z - _z0)];

    /// <summary>The area's block columns not loaded: no targets until they are.</summary>
    public IReadOnlyList<(int X, int Z)> UnloadedColumns => _unloadedArea;

    // ---- changes ----

    /// <summary>A change seen in the world; a pace not known (NaN) keeps the one read before.</summary>
    public void Set(MarkPos cell, MineVoxel voxel)
    {
        if (!InWindow(cell.X, cell.Y, cell.Z))
            return;
        Write(cell.X, cell.Y, cell.Z, voxel.Solid, voxel.Rule, float.IsNaN(voxel.Seconds) ? null : Pace(voxel.Seconds));
    }

    /// <summary>Broken by the job: air, nothing to do.</summary>
    public void Dig(MarkPos cell)
    {
        if (!InWindow(cell.X, cell.Y, cell.Z))
            return;
        Write(cell.X, cell.Y, cell.Z, false, MineRule.Air, null);
    }

    private void Write(int x, int y, int z, bool solid, MineRule rule, ushort? pace)
    {
        MineWork.Add(1);
        int i = Index(x, y, z);
        int r = RuleIndex(x, y, z);
        if (_marks > 0)
            _journal.Add((i, SolidAt(i), r, r >= 0 ? _rule[r] : (byte)0, r >= 0 ? _pace[r] : (ushort)0));
        else
            Version++;
        SetSolid(i, solid);
        if (r >= 0)
        {
            _rule[r] = (byte)rule;
            if (pace is { } p)
                _pace[r] = p;
        }
    }

    /// <summary>Starts a tentative change: <see cref="Undo"/> with what this returns puts everything
    /// changed since back.</summary>
    public int Mark()
    {
        _marks++;
        return _journal.Count;
    }

    public void Undo(int mark)
    {
        MineWork.Add(_journal.Count - mark);
        for (int k = _journal.Count - 1; k >= mark; k--)
        {
            var (i, solid, r, rule, pace) = _journal[k];
            SetSolid(i, solid);
            if (r >= 0)
            {
                _rule[r] = rule;
                _pace[r] = pace;
            }
        }
        _journal.RemoveRange(mark, _journal.Count - mark);
        if (--_marks <= 0)
        {
            _marks = 0;
            _journal.Clear();
        }
    }

    // ---- the eidolon's box ----

    /// <summary>Whether its box (1.7 wide, 3.75 tall), its feet's centre at (x, y, z), touches no solid block.</summary>
    public bool Free(double x, double y, double z)
    {
        const double eps = 1e-6;
        int x0 = (int)Math.Floor(x - EidolonMining.HalfWidth), x1 = (int)Math.Floor(x + EidolonMining.HalfWidth - eps);
        int z0 = (int)Math.Floor(z - EidolonMining.HalfWidth), z1 = (int)Math.Floor(z + EidolonMining.HalfWidth - eps);
        int y0 = (int)Math.Floor(y), y1 = (int)Math.Floor(y + EidolonMining.Height - eps);
        for (int bx = x0; bx <= x1; bx++)
        for (int bz = z0; bz <= z1; bz++)
        for (int by = y0; by <= y1; by++)
            if (Solid(bx, by, bz))
                return false;
        return true;
    }

    public float Cost(PathCell cell) => 0;

    /// <summary>Whether its box is free at a path node: the 2 × 2 columns <c>x-1..x</c>, <c>z-1..z</c>,
    /// layers <c>y..y+3</c>.</summary>
    public bool BoxFree(int x, int y, int z)
    {
        for (int bx = x - 1; bx <= x; bx++)
        for (int bz = z - 1; bz <= z; bz++)
        for (int by = y; by <= y + 3; by++)
            if (Solid(bx, by, bz))
                return false;
        return true;
    }

    /// <summary>How many of the four blocks under a path node's box are solid: its footing.</summary>
    public int Footing(int x, int y, int z)
    {
        int n = 0;
        for (int bx = x - 1; bx <= x; bx++)
        for (int bz = z - 1; bz <= z; bz++)
            if (Solid(bx, y - 1, bz))
                n++;
        return n;
    }

    /// <summary>Whether it may stand at <paramref name="node"/>: its box free, one block or more under it solid.</summary>
    public bool Standable(PathCell node) => BoxFree(node.X, node.Y, node.Z) && Footing(node.X, node.Y, node.Z) > 0;

    /// <summary>Whether a node's box touches the window's side: the world outside is taken as joined to it.</summary>
    public bool OnRing(int x, int z) => x - 1 == Window.Min.X || x == Window.Max.X || z - 1 == Window.Min.Z || z == Window.Max.Z;

    /// <summary>The ways out (README "Eidolon", mining, *Stations*): the nodes it may stand at with their box against the window's
    /// side, and the node nearest <paramref name="home"/> (<see cref="HomeNode"/>).</summary>
    public IEnumerable<PathCell> Exits(PathCell? home)
    {
        var h = HomeNode(home);
        if (h != null)
            yield return h.Value;
        for (int x = Window.Min.X + 1; x <= Window.Max.X; x++)
        for (int z = Window.Min.Z + 1; z <= Window.Max.Z; z++)
        {
            if (!OnRing(x, z))
                continue;
            for (int y = Window.Min.Y; y <= Window.Max.Y - 3; y++)
                if (Standable(new PathCell(x, y, z)) && h != new PathCell(x, y, z))
                    yield return new PathCell(x, y, z);
        }
    }

    /// <summary>Where it was ordered from, as a node of the window: <paramref name="home"/> itself, or the
    /// nearest height within two over or under it where it may stand; null when neither is in the window.</summary>
    public PathCell? HomeNode(PathCell? home)
    {
        if (home is not { } h)
            return null;
        foreach (int dy in (ReadOnlySpan<int>)[0, 1, -1, 2, -2])
        {
            var n = h with { Y = h.Y + dy };
            if (n.X - 1 >= Window.Min.X && n.X <= Window.Max.X && n.Z - 1 >= Window.Min.Z && n.Z <= Window.Max.Z && Standable(n))
                return n;
        }
        return null;
    }

    public MineSite Clone() => new(this);

    // ---- the window's size ----

    /// <summary>The window round <paramref name="area"/>: <see cref="SideMargin"/> columns each side,
    /// <see cref="BelowMargin"/> under and <see cref="AboveMargin"/> over.</summary>
    public static MarkArea Around(MarkArea area) =>
        new(new MarkPos(area.Min.X - SideMargin, area.Min.Y - BelowMargin, area.Min.Z - SideMargin),
            new MarkPos(area.Max.X + SideMargin, area.Max.Y + AboveMargin, area.Max.Z + SideMargin));

    /// <summary>Whether <paramref name="window"/> holds what placing a member at path node <paramref name="at"/>
    /// on the walk graph reads (<see cref="MineCosts.Snap"/>): its box and the blocks under it, a column and a
    /// step either way.</summary>
    public static bool Holds(MarkArea window, PathCell at) =>
        at.X - 2 >= window.Min.X && at.X + 1 <= window.Max.X && at.Z - 2 >= window.Min.Z && at.Z + 1 <= window.Max.Z
        && at.Y - 3 >= window.Min.Y && at.Y + 5 <= window.Max.Y;

    /// <summary>How far path node <paramref name="at"/> is from <paramref name="area"/>, in columns or layers
    /// (the most of the three; 0 over or in it).</summary>
    public static int Off(MarkArea area, PathCell at)
    {
        static int Past(int v, int min, int max) => Math.Max(0, Math.Max(min - v, v - max));
        return Math.Max(Past(at.X, area.Min.X, area.Max.X), Math.Max(Past(at.Y, area.Min.Y, area.Max.Y), Past(at.Z, area.Min.Z, area.Max.Z)));
    }

    /// <summary>
    /// <paramref name="window"/> (the job's, at least <see cref="Around"/> the area) grown to take in each of
    /// <paramref name="members"/> it does not hold (<see cref="Holds"/>) within <see cref="GrowColumns"/> of
    /// <paramref name="area"/> (<see cref="Off"/>), with the usual margins round where it stands (its box and
    /// a step under it taken as an area); never smaller, so a member once in stays in. Those farther are put
    /// in <paramref name="far"/>.
    /// </summary>
    public static MarkArea Grown(MarkArea area, MarkArea window, IEnumerable<PathCell> members, List<PathCell>? far = null)
    {
        foreach (var at in members)
        {
            if (Holds(window, at))
                continue;
            if (Off(area, at) > GrowColumns)
            {
                far?.Add(at);
                continue;
            }
            var round = Around(new MarkArea(new MarkPos(at.X - 1, at.Y - 2, at.Z - 1), new MarkPos(at.X, at.Y + 3, at.Z)));
            window = new MarkArea(
                new MarkPos(Math.Min(window.Min.X, round.Min.X), Math.Min(window.Min.Y, round.Min.Y), Math.Min(window.Min.Z, round.Min.Z)),
                new MarkPos(Math.Max(window.Max.X, round.Max.X), Math.Max(window.Max.Y, round.Max.Y), Math.Max(window.Max.Z, round.Max.Z)));
        }
        return window;
    }

    // ---- indexing ----

    internal bool InWindow(int x, int y, int z) =>
        (uint)(x - _x0) < (uint)_sx && (uint)(y - _y0) < (uint)_sy && (uint)(z - _z0) < (uint)_sz;

    internal int Index(int x, int y, int z) => ((x - _x0) * _sz + (z - _z0)) * _sy + (y - _y0);

    /// <summary>A block column's index less its bottom: add a layer's y for the block's (hot loops).</summary>
    internal int Column(int x, int z) => ((x - _x0) * _sz + (z - _z0)) * _sy - _y0;

    internal bool InColumns(int x, int z) => (uint)(x - _x0) < (uint)_sx && (uint)(z - _z0) < (uint)_sz;

    internal MarkPos At(int index)
    {
        int y = index % _sy, rest = index / _sy;
        return new MarkPos(rest / _sz + _x0, y + _y0, rest % _sz + _z0);
    }

    internal bool SolidAt(int index) => (_solid[index >> 6] & (1UL << (index & 63))) != 0;

    private void SetSolid(int index, bool solid)
    {
        if (solid)
            _solid[index >> 6] |= 1UL << (index & 63);
        else
            _solid[index >> 6] &= ~(1UL << (index & 63));
    }

    private int RuleIndex(int x, int y, int z)
    {
        if (x < Area.Min.X || x > Area.Max.X || z < Area.Min.Z || z > Area.Max.Z || y < Area.Min.Y || y > Area.Max.Y + 1)
            return -1;
        return ((x - Area.Min.X) * Area.SizeZ + (z - Area.Min.Z)) * (Area.SizeY + 1) + (y - Area.Min.Y);
    }
}
