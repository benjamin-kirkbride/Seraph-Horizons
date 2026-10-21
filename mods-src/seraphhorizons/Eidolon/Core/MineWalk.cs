namespace SeraphHorizons.Mod.Eidolon.Core;

/// <summary>
/// A graph of the places the eidolon stands in a mine order's window (README "Eidolon", mining,
/// *Stations*), kept as a tree from the ways out (BFD's cached spanning tree): a node is out while it
/// has a parent, a way out is its own. Two nodes are joined when it steps from either to the other
/// (<see cref="WidePath.Step"/> with the site's <see cref="MineSite.Step"/> both ways: the box free all
/// the way up at the lower one, and half way for a diagonal). Digging only ever adds air, so a join
/// between two nodes that stay nodes never goes: what changes is nodes coming and going, and a node
/// going orphans the nodes it led out, which are joined again to an out node where one is next to them
/// (<see cref="Cut"/>). Changes may be tried and undone (<see cref="Mark"/>, <see cref="Undo"/>). Kept for
/// the live walk (<see cref="MineWalk"/>) and the reach bound's potential stands (<see cref="MineReach"/>).
/// </summary>
public abstract class MineGraph
{
    private const int KindParent = 0, KindKids = 1;

    protected static readonly (int Dx, int Dz)[] Directions =
        [(1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1)];

    private readonly List<(int Kind, int Index, int Old)> _journal = new();
    private int _marks;

    protected MineGraph(MineSite site, PathCell? home, IReadOnlySet<PathCell>? bad)
    {
        Site = site;
        Bad = bad ?? new HashSet<PathCell>();
        HomeNode = site.HomeNode(home);
        Parent = new int[site.Volume];
        Kids = new byte[site.Volume];
        Array.Fill(Parent, -1);
        MineWork.Bytes(site.Volume * 9L);
    }

    protected MineGraph(MineGraph other, MineSite site, IReadOnlySet<PathCell>? bad)
    {
        Site = site;
        Bad = bad ?? other.Bad;
        HomeNode = other.HomeNode;
        Parent = (int[])other.Parent.Clone();
        Kids = (byte[])other.Kids.Clone();
        MineWork.Bytes(Parent.Length * 5L);
    }

    protected MineSite Site { get; }

    /// <summary>Stands it found it could not stand at in the world (README "Eidolon", mining, bad
    /// stands): never nodes.</summary>
    public IReadOnlySet<PathCell> Bad { get; }

    /// <summary>Where it was ordered from, as a node, if it is one in the window: a way out.</summary>
    public PathCell? HomeNode { get; }

    protected int[] Parent { get; }
    protected byte[] Kids { get; }

    /// <summary>Whether a journal is kept (between <see cref="Mark"/> and its <see cref="Undo"/>).</summary>
    protected bool Marking => _marks > 0;

    /// <summary>Whether a block lets its box through.</summary>
    protected abstract bool Clear(int x, int y, int z);

    /// <summary>Whether a path node is a node of this graph.</summary>
    protected abstract bool IsNode(int x, int y, int z);

    /// <summary><see cref="IsNode"/> with the node's index known.</summary>
    protected abstract bool NodeAt(int index, int x, int y, int z);

    /// <summary>A node became out, or stopped being.</summary>
    protected virtual void Gained(int index) { }

    protected virtual void Lost(int index) { }

    /// <summary>Puts back a value of the subclass's own a journal entry holds.</summary>
    protected virtual void Restore(int kind, int index, int old) { }

    /// <summary>Whether a node is out: joined, step by step, to a way out.</summary>
    public bool Out(PathCell node) => Site.InWindow(node.X, node.Y, node.Z) && Parent[Site.Index(node.X, node.Y, node.Z)] >= 0;

    /// <summary>Whether a node is a way out: its box against the window's side, or where it was ordered from.</summary>
    protected bool Root(int x, int y, int z) => Site.OnRing(x, z) || HomeNode == new PathCell(x, y, z);

    protected bool IsBad(int x, int y, int z) => Bad.Count > 0 && Bad.Contains(new PathCell(x, y, z));

    // ---- joins ----

    /// <summary>Whether two nodes side by side (or corner to corner), at most a step apart in height,
    /// are joined: the lower one's columns clear up to the higher one's head, and for a diagonal the
    /// 3 × 3 columns half way clear at the higher one's height.</summary>
    protected bool Joined(int ax, int ay, int az, int bx, int by, int bz)
    {
        int lx = ax, ly = ay, lz = az, hy = by;
        if (by < ay)
            (lx, ly, lz, hy) = (bx, by, bz, ay);
        for (int y = ly + 4; y <= hy + 3; y++)
        for (int x = lx - 1; x <= lx; x++)
        for (int z = lz - 1; z <= lz; z++)
            if (!Clear(x, y, z))
                return false;
        if (ax != bx && az != bz)
        {
            // the 3 x 3 columns half way: both boxes are clear there by now (the higher one's, and the
            // lower one's all the way up), so only the two corners neither covers are asked
            int x0 = Math.Min(ax, bx) - 1, x1 = Math.Max(ax, bx), z0 = Math.Min(az, bz) - 1, z1 = Math.Max(az, bz);
            bool rising = (bx - ax) * (bz - az) > 0;
            int cx0 = rising ? x1 : x0, cz0 = z0, cx1 = rising ? x0 : x1, cz1 = z1;
            for (int y = hy; y <= hy + 3; y++)
                if (!Clear(cx0, y, cz0) || !Clear(cx1, y, cz1))
                    return false;
        }
        return true;
    }

    /// <summary>The nodes joined to one, in a fixed order.</summary>
    protected IEnumerable<int> Neighbours(int index)
    {
        var joined = new int[40];
        int count = NeighboursOf(index, joined);
        return joined.Take(count);
    }

    /// <summary>The nodes joined to one, in a fixed order, into <paramref name="into"/> (40 long); how many.</summary>
    protected int NeighboursOf(int index, Span<int> into)
    {
        var p = Site.At(index);
        int step = Site.Step, count = 0;
        int y0 = Math.Max(p.Y - step, Site.Window.Min.Y), y1 = Math.Min(p.Y + step, Site.Window.Max.Y);
        MineWork.Add(Directions.Length * (y1 - y0 + 1));
        foreach (var (dx, dz) in Directions)
        {
            int x = p.X + dx, z = p.Z + dz;
            if (!Site.InColumns(x, z))
                continue;
            int column = Site.Column(x, z);
            for (int y = y0; y <= y1; y++)
                if (NodeAt(column + y, x, y, z) && Joined(p.X, p.Y, p.Z, x, y, z))
                    into[count++] = column + y;
        }
        return count;
    }

    /// <summary>The nodes joined to one by its index in the site, in a fixed order, into <paramref name="into"/>
    /// (40 long); how many (the routes' searches, <see cref="MineCosts.From"/>, which walk it by index).</summary>
    internal int JoinedTo(int index, Span<int> into) => NeighboursOf(index, into);

    /// <summary>Whether a node is out, by its index in the site.</summary>
    internal bool OutAt(int index) => Parent[index] >= 0;

    /// <summary>The site it is a graph of.</summary>
    internal MineSite Space => Site;

    /// <summary>The nodes joined to <paramref name="node"/> (a node of this graph), in a fixed order.</summary>
    public IEnumerable<PathCell> Joins(PathCell node) =>
        Site.InWindow(node.X, node.Y, node.Z) && IsNode(node.X, node.Y, node.Z)
            ? Neighbours(Site.Index(node.X, node.Y, node.Z)).Select(i => Site.At(i).ToCell())
            : [];

    /// <summary>An out node joined to one, or -1.</summary>
    protected int OutNeighbour(int index)
    {
        var p = Site.At(index);
        int step = Site.Step;
        MineWork.Add(Directions.Length * (2 * step + 1));
        foreach (var (dx, dz) in Directions)
        for (int dy = -step; dy <= step; dy++)
        {
            int x = p.X + dx, y = p.Y + dy, z = p.Z + dz;
            if (!Site.InWindow(x, y, z))
                continue;
            int n = Site.Index(x, y, z);
            if (Parent[n] >= 0 && Joined(p.X, p.Y, p.Z, x, y, z))
                return n;
        }
        return -1;
    }

    // ---- the tree ----

    protected void Attach(int index, int parent)
    {
        Set(KindParent, Parent, index, parent);
        if (parent != index)
            Set(KindKids, Kids, parent, Kids[parent] + 1);
        if (_quiet == null || !_quiet.Contains(index))
            Gained(index);
    }

    private HashSet<int>? _quiet;

    private void Detach(int index, bool quiet = false)
    {
        int parent = Parent[index];
        if (parent < 0)
            return;
        if (parent != index && Parent[parent] >= 0)
            Set(KindKids, Kids, parent, Kids[parent] - 1);
        Set(KindParent, Parent, index, -1);
        if (Kids[index] != 0)
            Set(KindKids, Kids, index, 0);
        if (!quiet)
            Lost(index);
    }

    /// <summary>Grows the tree from <paramref name="from"/> (out nodes) through the nodes not out
    /// joined to them; returns how many it added.</summary>
    protected int Grow(IEnumerable<int> from)
    {
        var queue = new Queue<int>(from);
        int added = 0;
        Span<int> joined = stackalloc int[40];
        while (queue.TryDequeue(out int u))
        {
            int count = NeighboursOf(u, joined);
            for (int k = 0; k < count; k++)
            {
                int v = joined[k];
                if (Parent[v] < 0)
                {
                    Attach(v, u);
                    queue.Enqueue(v);
                    added++;
                }
            }
        }
        return added;
    }

    /// <summary>
    /// Takes <paramref name="gone"/> (nodes no longer nodes) out of the tree. Each node they led out is
    /// first given another parent beside it whose way out is clear of them (BFD's cheap case: nothing
    /// under it changes); one with none goes too, and its own are tried the same way. What is left
    /// without a parent is joined again where it can be, as is each of <paramref name="maybe"/> (nodes
    /// that may have become out: new, or with a new join): a way out, or joined to an out node, and what
    /// it leads to.
    /// </summary>
    protected void Cut(IReadOnlyList<int> gone, IEnumerable<int> maybe)
    {
        var dead = new HashSet<int>();
        foreach (int g in gone)
            if (Parent[g] >= 0)
                dead.Add(g);
        var orphans = new List<int>();
        var dirty = new HashSet<int>();
        var clean = new HashSet<int>();
        var queue = new Queue<int>(gone.Where(g => dead.Contains(g) && Kids[g] > 0));
        var children = new List<int>();
        while (queue.TryDequeue(out int u))
        {
            children.Clear();
            Children(u, children);
            foreach (int r in children)
            {
                if (dead.Contains(r) || Parent[r] != u)
                    continue;
                int other = Alternative(r, dead, dirty, clean);
                if (other >= 0)
                {
                    Set(KindKids, Kids, u, Kids[u] - 1);
                    Set(KindParent, Parent, r, other);
                    Set(KindKids, Kids, other, Kids[other] + 1);
                    continue;
                }
                dead.Add(r);
                orphans.Add(r);
                if (Kids[r] > 0)
                    queue.Enqueue(r);
            }
        }
        foreach (int g in gone)
            if (dead.Contains(g))
                Detach(g);
        if (orphans.Count > 0)
        {
            _quiet = new HashSet<int>(orphans);
            foreach (int o in orphans)
                Detach(o, quiet: true);
        }
        var all = new List<int>(orphans);
        all.AddRange(maybe);
        all.Sort();
        MineWork.Add(all.Count);
        for (int k = 0; k < all.Count; k++)
        {
            int c = all[k];
            if ((k > 0 && all[k - 1] == c) || Parent[c] >= 0)
                continue;
            var p = Site.At(c);
            if (!IsNode(p.X, p.Y, p.Z))
                continue;
            int to = Root(p.X, p.Y, p.Z) ? c : OutNeighbour(c);
            if (to < 0)
                continue;
            Attach(c, to);
            Grow([c]);
        }
        if (_quiet != null)
        {
            _quiet = null;
            foreach (int o in orphans)
                if (Parent[o] < 0)
                    Lost(o);
        }
    }

    // Another parent for a node whose parent goes: an out node joined to it whose own way out passes
    // through neither it (so not one it leads out) nor any of those going.
    private int Alternative(int index, HashSet<int> dead, HashSet<int> dirty, HashSet<int> clean)
    {
        var p = Site.At(index);
        int step = Site.Step;
        MineWork.Add(Directions.Length * (2 * step + 1));
        foreach (var (dx, dz) in Directions)
        for (int dy = -step; dy <= step; dy++)
        {
            int x = p.X + dx, y = p.Y + dy, z = p.Z + dz;
            if (!Site.InWindow(x, y, z))
                continue;
            int n = Site.Index(x, y, z);
            if (Parent[n] < 0 || dead.Contains(n) || dirty.Contains(n) || !Joined(p.X, p.Y, p.Z, x, y, z) || !Clean(n, index, dead, dirty, clean))
                continue;
            return n;
        }
        return -1;
    }

    // Whether a node's way out passes through neither `index` nor any of `dead`; those found to pass
    // through one of `dead` are remembered (`dirty`), and those found clear too (`clean`: a node whose way
    // out is clear of every dead one cannot pass through `index` either, whose own way out does, so each
    // chain is walked once a cut).
    private bool Clean(int from, int index, HashSet<int> dead, HashSet<int> dirty, HashSet<int> clean)
    {
        var path = new List<int>();
        bool result = Clean(from, index, dead, dirty, clean, path);
        MineWork.Add(path.Count + 1);
        return result;
    }

    private bool Clean(int from, int index, HashSet<int> dead, HashSet<int> dirty, HashSet<int> clean, List<int> path)
    {
        int at = from;
        for (int steps = 0; steps < Parent.Length; steps++)
        {
            if (at == index)
                return false;
            if (clean.Contains(at))
            {
                clean.UnionWith(path);
                return true;
            }
            if (dead.Contains(at) || dirty.Contains(at))
            {
                dirty.UnionWith(path);
                return false;
            }
            path.Add(at);
            int parent = Parent[at];
            if (parent == at)
            {
                clean.UnionWith(path);
                return true;
            }
            if (parent < 0)
                return false;
            at = parent;
        }
        return false;
    }

    // The nodes a node is the parent of.
    private void Children(int index, List<int> into)
    {
        var p = Site.At(index);
        int step = Site.Step;
        MineWork.Add(Directions.Length * (2 * step + 1));
        foreach (var (dx, dz) in Directions)
        for (int dy = -step; dy <= step; dy++)
        {
            int x = p.X + dx, y = p.Y + dy, z = p.Z + dz;
            if (!Site.InWindow(x, y, z))
                continue;
            int n = Site.Index(x, y, z);
            if (n != index && Parent[n] == index)
                into.Add(n);
        }
    }

    // ---- trying ----

    protected void Set(int kind, byte[] array, int index, int value)
    {
        if (_marks > 0)
            _journal.Add((kind, index, array[index]));
        array[index] = (byte)value;
    }

    protected void Set(int kind, int[] array, int index, int value)
    {
        if (_marks > 0)
            _journal.Add((kind, index, array[index]));
        array[index] = value;
    }

    /// <summary>Starts a tentative change: <see cref="Undo"/> with what this returns puts the graph back
    /// as it was.</summary>
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
            var (kind, index, old) = _journal[k];
            if (kind == KindParent)
                Parent[index] = old;
            else if (kind == KindKids)
                Kids[index] = (byte)old;
            else
                Restore(kind, index, old);
        }
        _journal.RemoveRange(mark, _journal.Count - mark);
        if (--_marks <= 0)
        {
            _marks = 0;
            _journal.Clear();
        }
    }

    /// <summary>The out nodes, in index order.</summary>
    protected IEnumerable<int> OutIndices()
    {
        MineWork.Bytes(Parent.Length * 4L);
        for (int i = 0; i < Parent.Length; i++)
            if (Parent[i] >= 0)
            {
                MineWork.Add(1);
                yield return i;
            }
    }
}

/// <summary>
/// The live walk graph of a mine order's window (README "Eidolon", mining, *Stations*): every place it
/// may stand now (<see cref="MineSite.Standable"/>, not a bad stand), and which of them are out
/// (<see cref="MineGraph.Out"/>): joined, a step at a time, to the window's side or to where it was
/// ordered from. Kept up as the job digs (<see cref="Dig"/>): only the nodes round a block can change.
/// </summary>
public sealed class MineWalk : MineGraph
{
    private const int KindNode = 2;

    private readonly byte[] _node;
    private readonly List<PathCell> _opened = new();

    public MineWalk(MineSite site, PathCell? home, IReadOnlySet<PathCell>? bad = null) : base(site, home, bad)
    {
        _node = new byte[site.Volume];
        MineWork.Bytes(_node.Length);
    }

    private MineWalk(MineWalk other, MineSite site, IReadOnlySet<PathCell>? bad) : base(other, site, bad)
    {
        _node = (byte[])other._node.Clone();
        MineWork.Bytes(_node.Length);
    }

    /// <summary>A copy on <paramref name="site"/> (a copy of this one's), reading <paramref name="bad"/> for its
    /// bad stands if given (else this one's set).</summary>
    public MineWalk Clone(MineSite site, IReadOnlySet<PathCell>? bad = null) => new(this, site, bad);

    protected override bool Clear(int x, int y, int z) => !Site.Solid(x, y, z);

    protected override bool IsNode(int x, int y, int z) => _node[Site.Index(x, y, z)] != 0;

    protected override bool NodeAt(int index, int x, int y, int z) => _node[index] != 0;

    private bool NodeNow(int x, int y, int z)
    {
        MineWork.Add(20);
        return Site.BoxFree(x, y, z) && Site.Footing(x, y, z) > 0 && !IsBad(x, y, z);
    }

    protected override void Gained(int index)
    {
        if (!Marking)
            _opened.Add(Site.At(index).ToCell());
    }

    protected override void Restore(int kind, int index, int old) => _node[index] = (byte)old;

    /// <summary>Whether a path node is a place it may stand now (out or not).</summary>
    public bool Node(PathCell node) => Site.InWindow(node.X, node.Y, node.Z) && _node[Site.Index(node.X, node.Y, node.Z)] != 0;

    /// <summary>Finds every node, a block column a step, then grows the tree from the ways out, a node a
    /// step.</summary>
    public IEnumerable<bool> Build()
    {
        Array.Fill(Parent, -1);
        Array.Clear(Kids);
        MineWork.Bytes(Parent.Length * 5L);
        var w = Site.Window;
        var free = new bool[w.SizeY + 1];
        for (int x = w.Min.X; x <= w.Max.X; x++)
        for (int z = w.Min.Z; z <= w.Max.Z; z++)
        {
            // a layer is free where all four columns of the box are; a node, four free over one not
            for (int y = w.Min.Y - 1; y <= w.Max.Y; y++)
                free[y - w.Min.Y + 1] = !Site.Solid(x - 1, y, z - 1) && !Site.Solid(x, y, z - 1) && !Site.Solid(x - 1, y, z) && !Site.Solid(x, y, z);
            for (int y = w.Min.Y; y <= w.Max.Y; y++)
            {
                int k = y - w.Min.Y + 1;
                bool node = !free[k - 1] && y + 3 <= w.Max.Y && free[k] && free[k + 1] && free[k + 2] && free[k + 3] && !IsBad(x, y, z);
                _node[Site.Index(x, y, z)] = (byte)(node ? 1 : 0);
            }
            MineWork.Add(5 * w.SizeY);
            yield return false;
        }
        var queue = new Queue<int>();
        for (int x = w.Min.X; x <= w.Max.X; x++)
        {
            for (int z = w.Min.Z; z <= w.Max.Z; z++)
            for (int y = w.Min.Y; y <= w.Max.Y; y++)
            {
                int i = Site.Index(x, y, z);
                if (_node[i] != 0 && Root(x, y, z))
                {
                    Attach(i, i);
                    queue.Enqueue(i);
                }
            }
            MineWork.Add(w.SizeZ * w.SizeY);
            yield return false;
        }
        int units = 0;
        var joined = new int[40];
        while (queue.TryDequeue(out int u))
        {
            int count = NeighboursOf(u, joined);
            for (int k = 0; k < count; k++)
                if (Parent[joined[k]] < 0)
                {
                    Attach(joined[k], u);
                    queue.Enqueue(joined[k]);
                }
            if (++units % 64 == 0)
                yield return false;
        }
        _opened.Clear();
        yield return true;
    }

    /// <summary><see cref="Build"/> to the end.</summary>
    public void BuildNow()
    {
        foreach (var _ in Build()) { }
    }

    /// <summary>
    /// The ways out by the window's side alone of <paramref name="stand"/>, a node the game's pathfinder found no
    /// way to from <paramref name="from"/> (README "Eidolon", mining, bad stands): the nodes on the window's side
    /// joined to it in the window, when neither <paramref name="from"/> (or a node within a block of it) nor where
    /// it was ordered from is. Every node of the window's side is taken as joined to every other through the
    /// world outside, which is all that joins those to where it stands; the pathfinder, which sees that world,
    /// found that it does not, so they are no ways out (the hilltop over a cliff whose foot it stands at, which
    /// the window's side touches: each stand on it found bad one at a time, and the plan made again with the next,
    /// for good). Null when <paramref name="stand"/> is no node, when no node is within a block of
    /// <paramref name="from"/> (where it stands is not known to the graph: outside the window, a partial block),
    /// when the stand is joined in the window to where it stands, or joined to no way out by the side. <paramref name="joined"/>, if given, is filled with the nodes joined to it
    /// in the window (itself among them), which those ways out alone led out.
    /// </summary>
    public List<PathCell>? SideWays(PathCell stand, PathCell from, ISet<PathCell>? joined = null)
    {
        // where it stands must be a node (or within a block of one) of the window: else what it is joined to is not
        // known, and the stand's own part could be the one every way out is in
        if (!Node(stand) || !Near(from))
            return null;
        int start = Site.Index(stand.X, stand.Y, stand.Z);
        var seen = new HashSet<int> { start };
        var queue = new Queue<int>([start]);
        var side = new List<PathCell>();
        var nodes = new List<PathCell>();
        var next = new int[40];
        while (queue.TryDequeue(out int u))
        {
            var p = Site.At(u).ToCell();
            if (Math.Abs(p.X - from.X) <= 1 && Math.Abs(p.Y - from.Y) <= 1 && Math.Abs(p.Z - from.Z) <= 1 || p == HomeNode)
                return null;
            nodes.Add(p);
            if (Site.OnRing(p.X, p.Z))
                side.Add(p);
            int count = NeighboursOf(u, next);
            for (int k = 0; k < count; k++)
                if (seen.Add(next[k]))
                    queue.Enqueue(next[k]);
        }
        if (side.Count == 0)
            return null;
        if (joined != null)
            foreach (var n in nodes)
                joined.Add(n);
        return side;

        bool Near(PathCell at)
        {
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            for (int dz = -1; dz <= 1; dz++)
                if (Node(new PathCell(at.X + dx, at.Y + dy, at.Z + dz)))
                    return true;
            return false;
        }
    }

    /// <summary>
    /// After <see cref="MineSite.Dig"/> of <paramref name="cell"/>: the nodes round it tested again
    /// (those whose box or footing holds it), those gone taken out with what they led out, those new, or
    /// beside it and not out (a join through it may be new), grown from.
    /// </summary>
    public void Dig(MarkPos cell) => Dig([cell]);

    /// <summary><see cref="Dig(MarkPos)"/> for several cells dug together (a station), the nodes round
    /// them tested once.</summary>
    public void Dig(IReadOnlyList<MarkPos> cells)
    {
        var status = new HashSet<int>();
        var region = new HashSet<int>();
        MineWork.Add(cells.Count * 4 * 4 * 7);
        foreach (var cell in cells)
        {
            for (int x = cell.X - 1; x <= cell.X + 2; x++)
            for (int z = cell.Z - 1; z <= cell.Z + 2; z++)
            for (int y = cell.Y - 5; y <= cell.Y + 1; y++)
            {
                if (!Site.InWindow(x, y, z))
                    continue;
                int i = Site.Index(x, y, z);
                if (x >= cell.X && x <= cell.X + 1 && z >= cell.Z && z <= cell.Z + 1 && y >= cell.Y - 3)
                    status.Add(i);
                else
                    region.Add(i);
            }
        }
        var gone = new List<int>();
        var maybe = new List<int>();
        foreach (int i in status)
        {
            var p = Site.At(i);
            bool was = _node[i] != 0, now = NodeNow(p.X, p.Y, p.Z);
            if (was != now)
            {
                Set(KindNode, _node, i, now ? 1 : 0);
                if (!now)
                    gone.Add(i);
            }
            if (now && Parent[i] < 0)
                maybe.Add(i);
        }
        MineWork.Add(region.Count);
        foreach (int i in region)
            if (!status.Contains(i) && _node[i] != 0 && Parent[i] < 0)
                maybe.Add(i);
        gone.Sort();
        Cut(gone, maybe);
    }

    private readonly HashSet<int> _gone = new();
    private readonly Dictionary<int, bool> _clean = new();

    /// <summary>
    /// A tentative <see cref="Dig(IReadOnlyList{MarkPos})"/> (between <see cref="MineGraph.Mark"/> and its
    /// <see cref="MineGraph.Undo"/>, after the site's): only which nodes are nodes is kept up, the tree is
    /// left as it was, and <see cref="OutAfter"/> asks it.
    /// </summary>
    public void Try(IReadOnlyList<MarkPos> cells)
    {
        _gone.Clear();
        _clean.Clear();
        foreach (var cell in cells)
            for (int x = cell.X; x <= cell.X + 1; x++)
            for (int z = cell.Z; z <= cell.Z + 1; z++)
            for (int y = cell.Y - 3; y <= cell.Y + 1; y++)
            {
                if (!Site.InWindow(x, y, z))
                    continue;
                int i = Site.Index(x, y, z);
                bool was = _node[i] != 0, now = NodeNow(x, y, z);
                if (was == now)
                    continue;
                Set(KindNode, _node, i, now ? 1 : 0);
                if (!now)
                    _gone.Add(i);
            }
    }

    /// <summary>
    /// After <see cref="Try"/>: whether <paramref name="node"/> is out in the world as tried. A search from
    /// it through the nodes as they are now, until one that was out whose way out (its parents) is all
    /// still there, or a way out: joins between nodes that stay nodes never go, so that way out still
    /// holds. Only a node cut off searches the whole of its part.
    /// </summary>
    public bool OutAfter(PathCell node)
    {
        if (!Node(node))
            return false;
        int start = Site.Index(node.X, node.Y, node.Z);
        var seen = new HashSet<int> { start };
        var queue = new Queue<int>([start]);
        Span<int> joined = stackalloc int[40];
        while (queue.TryDequeue(out int u))
        {
            var p = Site.At(u);
            if (Root(p.X, p.Y, p.Z) || (Parent[u] >= 0 && StillOut(u)))
                return true;
            int count = NeighboursOf(u, joined);
            for (int k = 0; k < count; k++)
                if (seen.Add(joined[k]))
                    queue.Enqueue(joined[k]);
        }
        return false;
    }

    // Whether an out node's way out (it and its parents) has no node gone in the try.
    private bool StillOut(int index)
    {
        var path = new List<int>();
        int at = index;
        bool clean;
        while (true)
        {
            if (_clean.TryGetValue(at, out clean))
                break;
            if (_gone.Contains(at))
            {
                clean = false;
                break;
            }
            path.Add(at);
            int parent = Parent[at];
            if (parent == at)
            {
                clean = true;
                break;
            }
            if (parent < 0 || path.Count > Parent.Length)
            {
                clean = false;
                break;
            }
            at = parent;
        }
        MineWork.Add(path.Count + 1);
        foreach (int n in path)
            _clean[n] = clean;
        return clean;
    }

    /// <summary>The out nodes, in index order.</summary>
    public IReadOnlyCollection<PathCell> Nodes => OutIndices().Select(i => Site.At(i).ToCell()).ToList();

    /// <summary>Nodes that became out since the last call (not by a tentative change): new places to stand.</summary>
    public IReadOnlyList<PathCell> TakeOpened()
    {
        MineWork.Add(_opened.Count);
        var list = _opened.ToList();
        _opened.Clear();
        return list;
    }

    /// <summary>The way out from an out node: its parents to its way out, itself first; empty when it is not out.</summary>
    public IReadOnlyList<PathCell> Route(PathCell node)
    {
        var route = new List<PathCell>();
        if (!Out(node))
            return route;
        int i = Site.Index(node.X, node.Y, node.Z);
        while (true)
        {
            route.Add(Site.At(i).ToCell());
            int p = Parent[i];
            if (p == i || p < 0 || route.Count > Parent.Length)
                break;
            i = p;
        }
        MineWork.Add(route.Count);
        return route;
    }
}

internal static class MinePositions
{
    public static PathCell ToCell(this MarkPos p) => new(p.X, p.Y, p.Z);
}
