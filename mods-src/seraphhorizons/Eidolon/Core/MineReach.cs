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
/// footing is dug, and tells which remaining target has none left: the guard's (c2). The count follows
/// what the fixpoint found each target from: a target that loses the stand it was reached from, with no
/// other stand found before it to take its place, is found again from the stands left or goes, and the
/// stands standing in it go with it (<see cref="Take"/>). So ceiling cells each reached only from a stand
/// under another lose their stands together once the stands they began from are dug away, and the guard
/// refuses the dig that would strand them, in the plan and in the world alike. A stand cut off from the
/// ways out by others going is followed only when <see cref="Exact"/> (the live job's strikes): elsewhere
/// it would cost a search of the potential graph on every try, the count is an upper bound for it, and
/// what it lets through strands at the simulation's end, where it is held (<see cref="MinePlanner"/>).</para>
/// </summary>
public sealed class MineReach : MineGraph
{
    private const int KindParent = 0, KindDug = 2, KindLive = 3, KindNode = 4, KindClear = 5, KindNodeRank = 6, KindCellRank = 7, KindFrom = 8;

    // A target of K nothing can reach any more (<see cref="Take"/>): solid to the bound from then on.
    private const byte Gone = 2;

    private readonly byte[] _target;
    private readonly byte[] _clear;
    private readonly byte[] _dug;
    private readonly byte[] _live;
    private readonly byte[] _node;
    // The derivation (Take), made by Run: each out node's rank and each target of K's (the order they
    // were found in, a stand after the targets of its box, a target after the stand it is reached from),
    // and the stand a target is reached from.
    private int[] _nodeRank = [];
    private int[] _cellRank = [];
    private int[] _from = [];
    private int _rank;
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
        MineWork.Bytes(site.Volume * 5L);
        MineWork.Add(targets.Count);
        foreach (var t in targets)
            if (site.InWindow(t.X, t.Y, t.Z))
                _target[site.Index(t.X, t.Y, t.Z)] = 1;
    }

    private MineReach(MineReach other, MineSite site, IReadOnlySet<PathCell>? bad) : base(other, site, bad)
    {
        _targets = other._targets;
        _target = (byte[])other._target.Clone();
        _clear = (byte[])other._clear.Clone();
        _dug = (byte[])other._dug.Clone();
        _live = (byte[])other._live.Clone();
        _node = (byte[])other._node.Clone();
        _nodeRank = (int[])other._nodeRank.Clone();
        _cellRank = (int[])other._cellRank.Clone();
        _from = (int[])other._from.Clone();
        _rank = other._rank;
        _clearable = new HashSet<MarkPos>(other._clearable);
        _ran = other._ran;
        MineWork.Bytes(_node.Length * 5L + _nodeRank.Length * 12L);
        MineWork.Add(_clearable.Count);
    }

    /// <summary>A copy on <paramref name="site"/>, reading <paramref name="bad"/> for its bad stands if given.</summary>
    public MineReach Clone(MineSite site, IReadOnlySet<PathCell>? bad = null) => new(this, site, bad);

    /// <summary>
    /// Whether a dig follows the stands it cuts off from the ways out too (<see cref="Take"/>): those it
    /// led out with no way round found go, and a target is taken over only by a stand that still leads
    /// out. The live job's own digs and its strike's guard ask it (README "Eidolon", mining, *Stations*);
    /// the plan's simulation and the dispatch over many stations do not, its searches costing too much on a
    /// deep pit's potential graph, and there the count stays an upper bound for a stand cut off.
    /// </summary>
    public bool Exact { get; set; }

    /// <summary>The targets some potential stand reaches (K).</summary>
    public IReadOnlySet<MarkPos> Clearable => _clearable;

    /// <summary>The targets none does: out of reach under every order.</summary>
    public IReadOnlySet<MarkPos> OutOfReach => CountTargets()._targets.Where(t => !_clearable.Contains(t)).ToHashSet();

    private MineReach CountTargets()
    {
        MineWork.Add(_targets.Count);
        return this;
    }

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
        return _clear[i] == 1 ? _dug[i] != 0 || !_airJoins && (_doubt.Count == 0 || !_doubt.Contains(i)) : _clear[i] == 0 && !Site.SolidAt(i);
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
        MineWork.Add(20);
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
        else if (kind == KindClear)
            _clear[index] = (byte)old;
        else if (kind == KindNodeRank)
            _nodeRank[index] = old;
        else if (kind == KindCellRank)
            _cellRank[index] = old;
        else if (kind == KindFrom)
            _from[index] = old;
    }

    // Whether a stand's reach may hold a cell of the area.
    private bool Near(MarkPos s)
    {
        var a = Site.Area;
        return s.X - 3 <= a.Max.X && s.X + 2 >= a.Min.X && s.Z - 3 <= a.Max.Z && s.Z + 2 >= a.Min.Z
            && s.Y - 1 <= a.Max.Y && s.Y + 3 >= a.Min.Y;
    }

    // The cells of a stand's reach box (6 x 6 columns less its own 2 x 2, 5 high), each looked at a unit.
    private const int ReachBox = (6 * 6 - 2 * 2) * 5;

    // A stand's reach: the clearable cells it reaches count it, or stop.
    private void Count(int index, int by)
    {
        var s = Site.At(index);
        if (!Near(s))
            return;
        MineWork.Add(ReachBox);
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
                if (_clear[t] != 1)
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
        foreach (var _ in Derivation())
            yield return false;
        Array.Fill(Parent, -1);
        Array.Clear(Kids);
        Array.Clear(_clear);
        Array.Clear(_dug);
        Array.Clear(_live);
        Array.Clear(_node);
        MineWork.Bytes(Parent.Length * 9L);
        var w = Site.Window;
        var queue = new Queue<int>();
        int ring = 0;
        for (int x = w.Min.X + 1; x <= w.Max.X; x++)
        for (int z = w.Min.Z + 1; z <= w.Max.Z; z++)
        {
            MineWork.Add(1);
            if (!Site.OnRing(x, z))
                continue;
            // each block of a ring column asked whether it is a stand: a few columns a step
            MineWork.Add(w.SizeY);
            for (int y = w.Min.Y; y <= w.Max.Y; y++)
                if (IsNode(x, y, z))
                {
                    int i = Site.Index(x, y, z);
                    Attach(i, i);
                    _nodeRank[i] = ++_rank;
                    queue.Enqueue(i);
                }
            if (++ring % 8 == 0)
                yield return false;
        }
        if (HomeNode is { } h && Site.InWindow(h.X, h.Y, h.Z) && IsNode(h.X, h.Y, h.Z) && Parent[Site.Index(h.X, h.Y, h.Z)] < 0)
        {
            int i = Site.Index(h.X, h.Y, h.Z);
            Attach(i, i);
            _nodeRank[i] = ++_rank;
            queue.Enqueue(i);
        }
        var added = new List<int>();
        var joined = new int[40];
        var seen = new int[Site.Volume];
        MineWork.Bytes(seen.Length * 4L);
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
                        _cellRank[t] = ++_rank;
                        _from[t] = u;
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
                    _nodeRank[joined[k]] = ++_rank;
                    queue.Enqueue(joined[k]);
                }
            // the nodes a new cell of K may have made stands, or joined (each once a stand)
            stamp++;
            MineWork.Add((Near(s) ? ReachBox : 0) + added.Count * 4 * 4 * 6);
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
                    _nodeRank[r] = ++_rank;
                    queue.Enqueue(r);
                }
            }
            added.Clear();
            if (++units % 64 == 0)
                yield return false;
        }
        _clearable = new HashSet<MarkPos>();
        MineWork.Bytes(_clear.Length);
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

    // The derivation's arrays, made or cleared a checkpoint each (window-sized).
    private IEnumerable<bool> Derivation()
    {
        int volume = Site.Volume;
        _rank = 0;
        if (_nodeRank.Length != volume)
            _nodeRank = new int[volume];
        else
            Array.Clear(_nodeRank);
        MineWork.Bytes(volume * 4L);
        yield return false;
        if (_cellRank.Length != volume)
            _cellRank = new int[volume];
        else
            Array.Clear(_cellRank);
        MineWork.Bytes(volume * 4L);
        yield return false;
        if (_from.Length != volume)
            _from = new int[volume];
        else
            Array.Clear(_from);
        MineWork.Bytes(volume * 4L);
        yield return false;
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
        MineWork.Add(cells.Count * 4);
        foreach (var cell in cells)
            if (Site.InWindow(cell.X, cell.Y, cell.Z) && _dug[Site.Index(cell.X, cell.Y, cell.Z)] == 0)
                Set(KindDug, _dug, Site.Index(cell.X, cell.Y, cell.Z), 1);
        var gone = new List<int>();
        foreach (var cell in cells)
        {
            if (!Site.InWindow(cell.X, cell.Y, cell.Z))
                continue;
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
                    gone.Add(n);
                    killed?.Add(new PathCell(x, y, z));
                }
            }
        }
        Take(gone);
        _starved = null;
    }

    /// <summary>
    /// After <see cref="MineSite.Set"/> made <paramref name="cell"/> solid (placed, a liquid, the world
    /// changed, not the job): the potential stands whose box now holds it go, as dug footing takes them
    /// (<see cref="Dig(IReadOnlyList{MarkPos}, ICollection{MarkPos}?, ICollection{PathCell}?)"/>), with what
    /// came from them. What it might open (new footing) is left to the next plan: the bound only shrinks
    /// here, so it stays an upper bound.
    /// </summary>
    public void Block(MarkPos cell)
    {
        MineWork.Add(2 * 2 * 4);
        var gone = new List<int>();
        for (int x = cell.X; x <= cell.X + 1; x++)
        for (int z = cell.Z; z <= cell.Z + 1; z++)
        for (int y = cell.Y - 3; y <= cell.Y; y++)
        {
            if (!Site.InWindow(x, y, z))
                continue;
            int n = Site.Index(x, y, z);
            if (_node[n] != 0)
                Set(KindNode, _node, n, 0);
            if (Parent[n] >= 0)
                gone.Add(n);
        }
        Take(gone);
    }

    // ---- delete and rederive ----

    // What a dig is taking in (Take): the targets in doubt (solid to Clear meanwhile).
    private readonly HashSet<int> _doubt = new();

    /// <summary>
    /// Takes <paramref name="gone"/> (stands no longer stands) out, with what came only from them (README
    /// "Eidolon", mining, *Stations*: the reach bound kept as the job digs). Each target first reached from
    /// a stand that goes is asked, the lowest rank first, whether a stand of a lower rank still out
    /// reaches it (that one is what it comes from now); one none does is in doubt, solid meanwhile, and the
    /// stands whose box holds it go too, and what was reached from them is asked in turn. A rank is the
    /// order the fixpoint found a stand or target in, after all it came from, so nothing is kept only by
    /// what it keeps: ceiling cells each reached only from a stand under another go together once
    /// the stands they began from are dug away. Then what is in doubt is found again from the stands still
    /// out, as the fixpoint would find it (a new rank, the stands in its box joined again where they join
    /// an out one); what is left can never be dug, solid to the bound from then on.
    /// A stand cut off from the ways out by others going is still counted (following it would cost a
    /// search of the potential graph on every try): there the count stays an upper bound.
    /// </summary>
    private void Take(List<int> gone)
    {
        if (gone.Count == 0)
            return;
        _doubt.Clear();
        _leads.Clear();
        var ask = new PriorityQueue<int, int>();
        var going = new List<int>(gone);
        while (going.Count > 0)
        {
            going.Sort();
            var led = new List<int>();
            foreach (int n in going)
            {
                if (Parent[n] < 0)
                    continue;
                Set(KindParent, Parent, n, -1);
                Count(n, -1);
                foreach (int t in Reached(Site.At(n)))
                    if (_from[t] == n && !_doubt.Contains(t))
                        ask.Enqueue(t, _cellRank[t]);
                Led(n, led);
            }
            going.Clear();
            // with Exact, the stands they led out go too where no way round is found
            _leads.Clear();
            if (Exact)
                foreach (int m in led)
                    if (Parent[m] >= 0 && !LeadsOut(m))
                        going.Add(m);
            if (going.Count > 0)
                continue;
            while (ask.TryDequeue(out int t, out _))
            {
                if (_doubt.Contains(t) || !Standing(t) || Holds(t, true))
                    continue;
                _doubt.Add(t);
                foreach (int n in Boxes(t))
                {
                    if (_node[n] != 0)
                        Set(KindNode, _node, n, 0);
                    if (Parent[n] >= 0)
                        going.Add(n);
                }
                if (Exact)
                    JoinedThrough(t, going);
                if (going.Count > 0)
                    break;
            }
        }
        // found again from the stands still out
        while (_doubt.Count > 0)
        {
            var found = _doubt.Order().Where(t => Holds(t, false)).ToList();
            if (found.Count == 0)
                break;
            var maybe = new List<int>();
            foreach (int t in found)
            {
                _doubt.Remove(t);
                Set(KindCellRank, _cellRank, t, ++_rank);
                maybe.AddRange(Boxes(t));
            }
            maybe.Sort();
            for (int k = 0; k < maybe.Count; k++)
            {
                int n = maybe[k];
                if (k > 0 && maybe[k - 1] == n)
                    continue;
                if (_node[n] != 0)
                    Set(KindNode, _node, n, 0);
                var q = Site.At(n);
                if (Parent[n] >= 0 || !IsNode(q.X, q.Y, q.Z))
                    continue;
                int to = Root(q.X, q.Y, q.Z) ? n : OutNeighbour(n);
                if (to < 0)
                    continue;
                Set(KindParent, Parent, n, to);
                Set(KindNodeRank, _nodeRank, n, ++_rank);
                Count(n, +1);
            }
        }
        MineWork.Add(_doubt.Count);
        foreach (int t in _doubt.Order())
            Set(KindClear, _clear, t, Gone);
        _doubt.Clear();
    }

    // Whether an out stand still leads out with what this dig took (Take): its parents to a way out are all
    // still out, or a short search from it finds one whose are, by joins through what is dug or air, the way
    // found its parents from then on (a stand led out only by one that went is cut off, and counts for
    // nothing). Remembered for the dig; one past SearchCap stands is taken as leading out, and not
    // remembered (there the count stays an upper bound).
    private bool LeadsOut(int n)
    {
        if (Chain(n))
            return true;
        var from = new Dictionary<int, int> { [n] = -1 };
        var queue = new Queue<int>([n]);
        Span<int> joined = stackalloc int[40];
        while (queue.TryDequeue(out int u))
        {
            if (from.Count > SearchCap)
                return true;
            _airJoins = true;
            int count = NeighboursOf(u, joined);
            _airJoins = false;
            for (int k = 0; k < count; k++)
            {
                int m = joined[k];
                if (Parent[m] < 0 || from.ContainsKey(m))
                    continue;
                from[m] = u;
                if (!Chain(m))
                {
                    queue.Enqueue(m);
                    continue;
                }
                // led out along the way found from then on (only those: the rest it saw still have
                // their old parents)
                for (int v = u, to = m; v >= 0; to = v, v = from[v])
                {
                    if (Parent[v] != to)
                        Set(KindParent, Parent, v, to);
                    _leads[v] = true;
                }
                return true;
            }
        }
        foreach (int v in from.Keys)
            _leads[v] = false;
        return false;
    }

    private const int SearchCap = 256;

    // While a way round is searched (LeadsOut): only joins through what is dug or air count, a join
    // through a block still to dig perhaps needing the very stand searched from.
    private bool _airJoins;

    // The stands a stand leads out (its children in the tree).
    private void Led(int n, List<int> into)
    {
        var p = Site.At(n);
        int step = Site.Step;
        MineWork.Add(Directions.Length * (2 * step + 1));
        foreach (var (dx, dz) in Directions)
        for (int dy = -step; dy <= step; dy++)
        {
            int x = p.X + dx, y = p.Y + dy, z = p.Z + dz;
            if (!Site.InWindow(x, y, z))
                continue;
            int m = Site.Index(x, y, z);
            if (m != n && Parent[m] == n)
                into.Add(m);
        }
    }

    // Whether a stand's parents to a way out are all still out (remembered for the dig).
    private bool Chain(int n)
    {
        var path = new List<int>();
        int at = n;
        bool clean;
        while (true)
        {
            if (_leads.TryGetValue(at, out clean))
                break;
            int parent = Parent[at];
            if (parent < 0)
            {
                clean = false;
                break;
            }
            path.Add(at);
            if (parent == at || path.Count > Parent.Length)
            {
                clean = parent == at;
                break;
            }
            at = parent;
        }
        MineWork.Add(path.Count + 1);
        foreach (int p in path)
            _leads[p] = clean;
        return clean;
    }

    private readonly Dictionary<int, bool> _leads = new();

    // A target of K still to dig.
    private bool Standing(int t) => _clear[t] == 1 && _dug[t] == 0;

    // The targets of K still to dig a stand reaches.
    private IEnumerable<int> Reached(MarkPos p)
    {
        if (!Near(p))
            yield break;
        MineWork.Add(ReachBox);
        var area = Site.Area;
        for (int x = Math.Max(p.X - 3, area.Min.X); x <= Math.Min(p.X + 2, area.Max.X); x++)
        for (int z = Math.Max(p.Z - 3, area.Min.Z); z <= Math.Min(p.Z + 2, area.Max.Z); z++)
        {
            if (x >= p.X - 1 && x <= p.X && z >= p.Z - 1 && z <= p.Z)
                continue;
            int column = Site.Column(x, z);
            for (int y = Math.Max(p.Y - 1, area.Min.Y); y <= Math.Min(p.Y + 3, area.Max.Y); y++)
                if (Standing(column + y))
                    yield return column + y;
        }
    }

    // With Exact: the out stands near a target now in doubt whose join to their parent passed through it
    // (solid meanwhile): they are led out no longer that way, and go as a stand dug away does (what they
    // led out then looks for a way round).
    private void JoinedThrough(int cell, List<int> going)
    {
        var c = Site.At(cell);
        int step = Site.Step;
        MineWork.Add(4 * 4 * (4 + step) * 4);
        for (int x = c.X - 1; x <= c.X + 2; x++)
        for (int z = c.Z - 1; z <= c.Z + 2; z++)
        for (int y = c.Y - 3 - step; y <= c.Y; y++)
        {
            if (!Site.InWindow(x, y, z))
                continue;
            int n = Site.Index(x, y, z);
            int p = Parent[n];
            if (p < 0 || p == n)
                continue;
            var q = Site.At(p);
            if (!Joined(x, y, z, q.X, q.Y, q.Z))
                going.Add(n);
        }
    }

    // The nodes whose box holds a cell.
    private IEnumerable<int> Boxes(int cell)
    {
        var c = Site.At(cell);
        MineWork.Add(2 * 2 * 4);
        for (int x = c.X; x <= c.X + 1; x++)
        for (int z = c.Z; z <= c.Z + 1; z++)
        for (int y = c.Y - 3; y <= c.Y; y++)
            if (Site.InWindow(x, y, z))
                yield return Site.Index(x, y, z);
    }

    // Whether a stand out reaches a target, of a lower rank if <paramref name="lower"/>; it is what the
    // target comes from now.
    private bool Holds(int t, bool lower)
    {
        var p = Site.At(t);
        MineWork.Add(6 * 6 * 5);
        for (int x = p.X - 2; x <= p.X + 3; x++)
        for (int z = p.Z - 2; z <= p.Z + 3; z++)
        {
            if (p.X >= x - 1 && p.X <= x && p.Z >= z - 1 && p.Z <= z)
                continue;
            for (int y = p.Y - 3; y <= p.Y + 1; y++)
            {
                if (!Site.InWindow(x, y, z))
                    continue;
                int n = Site.Index(x, y, z);
                if (Parent[n] < 0 || lower && _nodeRank[n] >= _cellRank[t] || !Near(new MarkPos(x, y, z)) || Exact && !LeadsOut(n))
                    continue;
                if (_from[t] != n)
                    Set(KindFrom, _from, t, n);
                return true;
            }
        }
        return false;
    }
}
