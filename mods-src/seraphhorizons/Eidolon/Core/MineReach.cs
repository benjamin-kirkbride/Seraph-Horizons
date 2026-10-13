namespace SeraphHorizons.Mod.Eidolon.Core;

/// <summary>
/// What any order of digging could ever reach of a mine order's targets (README "Eidolon", mining,
/// *Stations*: the reach bound), the reach-ceiling lemma made general. A cell is clearable
/// (<see cref="Clearable"/>, K) when a potential stand reaches it; a potential stand is a path node in
/// the world as it would be with every cell of K dug: its box air or in K, a block under it solid now
/// (a target may be kept as its footing), and joined in that world to a potential stand that is out, or
/// a way out itself. Found by a worklist from the ways out (<see cref="Run"/>): each stand adds the
/// targets it reaches to K, each cell added may make the nodes round it stands or join them.
///
/// <para>Why it bounds every order: a node any digging ever stands on has its box in air or dug, so in
/// air or K, and its footing solid now; every step it takes is a join in that world. So a target outside
/// K (<see cref="OutOfReach"/>) is out of reach whatever is dug first, short of building.</para>
///
/// <para>After <see cref="Run"/>, digging only takes potential stands away: <see cref="Dig"/> keeps each
/// target's count of live potential stands reaching it (<see cref="Live"/>), taking off those whose last
/// footing is dug, and tells which remaining target has none left: the guard's (c2). A stand cut off
/// from the ways out by others going is not followed (it would cost a search of the potential graph on
/// every try): the count is an upper bound, and what it lets through strands at the simulation's end,
/// where it is held (<see cref="MinePlanner"/>).</para>
/// </summary>
public sealed class MineReach : MineGraph
{
    private const int KindParent = 0, KindDug = 2, KindLive = 3, KindNode = 4;

    private readonly byte[] _target;
    private readonly byte[] _clear;
    private readonly byte[] _dug;
    private readonly byte[] _live;
    private readonly byte[] _node;
    private readonly IReadOnlySet<MarkPos> _targets;
    private HashSet<MarkPos> _clearable = new();
    private bool _ran;
    private ICollection<MarkPos>? _starved;

    public MineReach(MineSite site, IReadOnlySet<MarkPos> targets, PathCell? home, IReadOnlySet<PathCell>? bad = null)
        : base(site, home, bad)
    {
        _targets = targets;
        _target = new byte[site.Volume];
        _clear = new byte[site.Volume];
        _dug = new byte[site.Volume];
        _live = new byte[site.Volume];
        _node = new byte[site.Volume];
        foreach (var t in targets)
            if (site.InWindow(t.X, t.Y, t.Z))
                _target[site.Index(t.X, t.Y, t.Z)] = 1;
    }

    private MineReach(MineReach other, MineSite site) : base(other, site)
    {
        _targets = other._targets;
        _target = (byte[])other._target.Clone();
        _clear = (byte[])other._clear.Clone();
        _dug = (byte[])other._dug.Clone();
        _live = (byte[])other._live.Clone();
        _node = (byte[])other._node.Clone();
        _clearable = new HashSet<MarkPos>(other._clearable);
        _ran = other._ran;
    }

    public MineReach Clone(MineSite site) => new(this, site);

    /// <summary>The targets some potential stand reaches (K).</summary>
    public IReadOnlySet<MarkPos> Clearable => _clearable;

    /// <summary>The targets none does: out of reach under every order.</summary>
    public IReadOnlySet<MarkPos> OutOfReach => _targets.Where(t => !_clearable.Contains(t)).ToHashSet();

    /// <summary>Whether a node is a live potential stand: potential and out.</summary>
    public bool Potential(PathCell node) => Out(node);

    /// <summary>How many live potential stands reach a target.</summary>
    public int Live(MarkPos target) =>
        Site.InWindow(target.X, target.Y, target.Z) ? _live[Site.Index(target.X, target.Y, target.Z)] : 0;

    /// <summary>Whether a target has been dug since <see cref="Run"/>.</summary>
    public bool Dug(MarkPos target) =>
        Site.InWindow(target.X, target.Y, target.Z) && _dug[Site.Index(target.X, target.Y, target.Z)] != 0;

    protected override bool Clear(int x, int y, int z)
    {
        if (!Site.InWindow(x, y, z))
            return false;
        int i = Site.Index(x, y, z);
        return _clear[i] != 0 || !Site.SolidAt(i);
    }

    // Whether a node is a potential stand, remembered (0 not yet asked, 1 it is, 2 it is not) until a
    // block of its box joins K or one under it is dug.
    protected override bool IsNode(int x, int y, int z) => NodeAt(Site.Index(x, y, z), x, y, z);

    protected override bool NodeAt(int i, int x, int y, int z)
    {
        if (_node[i] == 0)
            Set(KindNode, _node, i, NodeNow(x, y, z) ? 1 : 2);
        return _node[i] == 1;
    }

    private bool NodeNow(int x, int y, int z)
    {
        for (int bx = x - 1; bx <= x; bx++)
        for (int bz = z - 1; bz <= z; bz++)
        for (int by = y; by <= y + 3; by++)
            if (!Clear(bx, by, bz))
                return false;
        return Site.Footing(x, y, z) > 0 && !IsBad(x, y, z);
    }

    protected override void Gained(int index)
    {
        if (_ran)
            Count(index, +1);
    }

    protected override void Lost(int index)
    {
        if (_ran)
            Count(index, -1);
    }

    protected override void Restore(int kind, int index, int old)
    {
        if (kind == KindDug)
            _dug[index] = (byte)old;
        else if (kind == KindLive)
            _live[index] = (byte)old;
        else if (kind == KindNode)
            _node[index] = (byte)old;
    }

    // Whether a stand's reach may hold a cell of the area.
    private bool Near(MarkPos s)
    {
        var a = Site.Area;
        return s.X - 3 <= a.Max.X && s.X + 2 >= a.Min.X && s.Z - 3 <= a.Max.Z && s.Z + 2 >= a.Min.Z
            && s.Y - 1 <= a.Max.Y && s.Y + 3 >= a.Min.Y;
    }

    // A stand's reach: the clearable cells it reaches count it, or stop.
    private void Count(int index, int by)
    {
        var s = Site.At(index);
        if (!Near(s))
            return;
        var area = Site.Area;
        for (int x = Math.Max(s.X - 3, area.Min.X); x <= Math.Min(s.X + 2, area.Max.X); x++)
        for (int z = Math.Max(s.Z - 3, area.Min.Z); z <= Math.Min(s.Z + 2, area.Max.Z); z++)
        {
            if (x >= s.X - 1 && x <= s.X && z >= s.Z - 1 && z <= s.Z)
                continue;
            int column = Site.Column(x, z);
            for (int y = Math.Max(s.Y - 1, area.Min.Y); y <= Math.Min(s.Y + 3, area.Max.Y); y++)
            {
                int t = column + y;
                if (_clear[t] == 0)
                    continue;
                Set(KindLive, _live, t, _live[t] + by);
                if (by < 0 && _live[t] == 0 && _dug[t] == 0)
                    _starved?.Add(new MarkPos(x, y, z));
            }
        }
    }

    /// <summary>The fixpoint, a stand a step; then each target's <see cref="Live"/> count.</summary>
    public IEnumerable<bool> Run()
    {
        _ran = false;
        Array.Fill(Parent, -1);
        Array.Clear(Kids);
        Array.Clear(_clear);
        Array.Clear(_dug);
        Array.Clear(_live);
        Array.Clear(_node);
        var w = Site.Window;
        var queue = new Queue<int>();
        for (int x = w.Min.X + 1; x <= w.Max.X; x++)
        for (int z = w.Min.Z + 1; z <= w.Max.Z; z++)
        {
            if (!Site.OnRing(x, z))
                continue;
            for (int y = w.Min.Y; y <= w.Max.Y; y++)
                if (IsNode(x, y, z))
                {
                    int i = Site.Index(x, y, z);
                    Attach(i, i);
                    queue.Enqueue(i);
                }
        }
        if (HomeNode is { } h && Site.InWindow(h.X, h.Y, h.Z) && IsNode(h.X, h.Y, h.Z) && Parent[Site.Index(h.X, h.Y, h.Z)] < 0)
        {
            int i = Site.Index(h.X, h.Y, h.Z);
            Attach(i, i);
            queue.Enqueue(i);
        }
        var added = new List<int>();
        var joined = new int[40];
        var seen = new int[Site.Volume];
        var area = Site.Area;
        int stamp = 0, units = 0;
        while (queue.TryDequeue(out int u))
        {
            var s = Site.At(u);
            // what it reaches joins K
            if (Near(s))
            for (int x = Math.Max(s.X - 3, area.Min.X); x <= Math.Min(s.X + 2, area.Max.X); x++)
            for (int z = Math.Max(s.Z - 3, area.Min.Z); z <= Math.Min(s.Z + 2, area.Max.Z); z++)
            {
                if (x >= s.X - 1 && x <= s.X && z >= s.Z - 1 && z <= s.Z)
                    continue;
                int column = Site.Column(x, z);
                for (int y = Math.Max(s.Y - 1, area.Min.Y); y <= Math.Min(s.Y + 3, area.Max.Y); y++)
                {
                    int t = column + y;
                    if (_target[t] != 0 && _clear[t] == 0)
                    {
                        _clear[t] = 1;
                        added.Add(t);
                        for (int bx = x; bx <= x + 1; bx++)
                        for (int bz = z; bz <= z + 1; bz++)
                        for (int by = y - 3; by <= y; by++)
                            if (Site.InWindow(bx, by, bz))
                                _node[Site.Index(bx, by, bz)] = 0;
                    }
                }
            }
            int count = NeighboursOf(u, joined);
            for (int k = 0; k < count; k++)
                if (Parent[joined[k]] < 0)
                {
                    Attach(joined[k], u);
                    queue.Enqueue(joined[k]);
                }
            // the nodes a new cell of K may have made stands, or joined (each once a stand)
            stamp++;
            foreach (int t in added)
            {
                var c = Site.At(t);
                for (int x = c.X - 1; x <= c.X + 2; x++)
                for (int z = c.Z - 1; z <= c.Z + 2; z++)
                for (int y = c.Y - 5; y <= c.Y; y++)
                {
                    if (!Site.InWindow(x, y, z))
                        continue;
                    int r = Site.Index(x, y, z);
                    if (seen[r] == stamp || Parent[r] >= 0)
                        continue;
                    seen[r] = stamp;
                    if (!IsNode(x, y, z))
                        continue;
                    int to = Root(x, y, z) ? r : OutNeighbour(r);
                    if (to < 0)
                        continue;
                    Attach(r, to);
                    queue.Enqueue(r);
                }
            }
            added.Clear();
            if (++units % 64 == 0)
                yield return false;
        }
        _clearable = new HashSet<MarkPos>();
        for (int i = 0; i < _clear.Length; i++)
            if (_clear[i] != 0)
                _clearable.Add(Site.At(i));
        _ran = true;
        foreach (int i in OutIndices().ToList())
        {
            Count(i, +1);
            if (++units % 64 == 0)
                yield return false;
        }
        yield return true;
    }

    /// <summary><see cref="Run"/> to the end.</summary>
    public void RunNow()
    {
        foreach (var _ in Run()) { }
    }

    /// <summary>
    /// After <see cref="MineSite.Dig"/> of a clearable <paramref name="cell"/>: the potential stands it
    /// was the last footing of go; every target that loses its last live potential stand while not dug is
    /// added to <paramref name="starved"/>.
    /// </summary>
    public void Dig(MarkPos cell, ICollection<MarkPos>? starved = null) => Dig([cell], starved);

    /// <summary><see cref="Dig(MarkPos, ICollection{MarkPos}?)"/> for several cells dug together; the
    /// stands whose last footing they were are added to <paramref name="killed"/>.</summary>
    public void Dig(IReadOnlyList<MarkPos> cells, ICollection<MarkPos>? starved, ICollection<PathCell>? killed = null)
    {
        _starved = starved;
        foreach (var cell in cells)
        {
            if (!Site.InWindow(cell.X, cell.Y, cell.Z))
                continue;
            int i = Site.Index(cell.X, cell.Y, cell.Z);
            if (_dug[i] == 0)
                Set(KindDug, _dug, i, 1);
            int y = cell.Y + 1;
            for (int x = cell.X; x <= cell.X + 1; x++)
            for (int z = cell.Z; z <= cell.Z + 1; z++)
            {
                if (!Site.InWindow(x, y, z))
                    continue;
                int n = Site.Index(x, y, z);
                if (_node[n] != 0)
                    Set(KindNode, _node, n, 0);
                if (Parent[n] >= 0 && Site.Footing(x, y, z) == 0)
                {
                    Set(KindParent, Parent, n, -1);
                    Count(n, -1);
                    killed?.Add(new PathCell(x, y, z));
                }
            }
        }
        _starved = null;
    }
}
