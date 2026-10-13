namespace SeraphHorizons.Mod.Eidolon.Core;

/// <summary>Why the guard (<see cref="MineState.Check"/>) refuses a station: (a) its stand is not a place
/// it may stand, or not out; (b) a cell is no remaining target or not in its reach; (c) after the dig its
/// stand is no longer out, a remaining target has no live potential stand left, a cell is another
/// eidolon's floor or over it, another is no longer out where it stands or holds a station, or the stand
/// of a station the plan digs before it, not yet done, is no longer out (<see cref="MineDispatch.Executable"/>).</summary>
public enum MineRefusal { None, NotStandable, NotOut, OutOfReach, CutsOff, Starves, WorkerFloor, OverWorker, CutsOffWorker, CutsOffStation }

/// <summary>Another eidolon at work, as the guard sees it: where it stands (its feet's centre and the
/// block its feet are in) and the stands of the stations it holds.</summary>
public readonly record struct MineWorkerAt(long Id, double X, int Feet, double Z, IReadOnlyList<PathCell> Holds);

/// <summary>
/// A job's live picture of its window (README "Eidolon", mining, *Stations*): the site, the walk graph
/// (where it may stand now and which of those are out), the reach bound over its targets, and the targets
/// still to dig (<see cref="Remaining"/>, clearable and not dug). The planner runs a copy of it forward;
/// the job keeps it as the world changes, and asks it before every claim and strike whether a station may
/// be dug (<see cref="Check"/>: TERMES' guard, BFD's three conditions).
/// </summary>
public sealed class MineState
{
    private HashSet<MarkPos> _remaining = new();
    private IReadOnlySet<MarkPos> _targets = new HashSet<MarkPos>();
    private MineReach? _reach;
    private bool _dirty;
    private bool _built;

    /// <summary>A state over <paramref name="site"/> (loaded), ordered from <paramref name="home"/>, with no
    /// targets yet (<see cref="Build"/>).</summary>
    public MineState(MineSite site, PathCell? home, IReadOnlySet<PathCell>? badStands = null)
    {
        Site = site;
        Home = home;
        BadStands = badStands ?? new HashSet<PathCell>();
        Walk = new MineWalk(site, home, BadStands);
    }

    private MineState(MineState other)
    {
        Site = other.Site.Clone();
        Home = other.Home;
        BadStands = other.BadStands;
        Walk = other.Walk.Clone(Site);
        _reach = other._reach?.Clone(Site);
        _remaining = new HashSet<MarkPos>(other._remaining);
        _targets = other._targets;
        _dirty = other._dirty;
        _built = other._built;
        Stale = other.Stale;
        Bands = other.Bands?.Clone();
    }

    public MineSite Site { get; }
    public MineWalk Walk { get; }

    /// <summary>The reach bound over <see cref="Targets"/> (none until it is built).</summary>
    public MineReach Reach => _reach ??= new MineReach(Site, _targets, Home, BadStands);

    public PathCell? Home { get; }
    public IReadOnlySet<PathCell> BadStands { get; }

    /// <summary>Clearable targets not yet dug.</summary>
    public IReadOnlySet<MarkPos> Remaining => _remaining;

    /// <summary>The targets it was built for (<see cref="Build"/>, <see cref="Retarget"/>).</summary>
    public IReadOnlySet<MarkPos> Targets => _targets;

    /// <summary>Which remaining targets may be dug now (the plan's bands), if a plan set them
    /// (<see cref="Adopt"/>; the planner's own).</summary>
    public MineBands? Bands { get; set; }

    /// <summary>Whether the world changed under it in a way the job did not make (<see cref="Changed"/>):
    /// its plan is to be made again.</summary>
    public bool Stale { get; set; }

    /// <summary>Builds the walk graph, then the reach bound over <paramref name="targets"/>, a step at a time.</summary>
    public IEnumerable<bool> Build(IReadOnlySet<MarkPos> targets)
    {
        foreach (bool _ in Walk.Build())
            yield return false;
        _built = true;
        foreach (bool _ in Retarget(targets))
            yield return false;
        _dirty = false;
        yield return true;
    }

    /// <summary>The walk graph alone, a step at a time.</summary>
    public IEnumerable<bool> BuildWalk()
    {
        foreach (bool _ in Walk.Build())
            yield return false;
        _built = true;
        yield return true;
    }

    /// <summary><see cref="Build"/> to the end.</summary>
    public void BuildNow(IReadOnlySet<MarkPos> targets)
    {
        foreach (var _ in Build(targets)) { }
    }

    /// <summary>The reach bound made again over <paramref name="targets"/> (the walk graph kept, built
    /// first if it never was); <see cref="Remaining"/> becomes those clearable.</summary>
    public IEnumerable<bool> Retarget(IReadOnlySet<MarkPos> targets)
    {
        if (!_built)
        {
            foreach (bool _ in Walk.Build())
                yield return false;
            _built = true;
        }
        _targets = targets;
        _reach = new MineReach(Site, targets, Home, BadStands);
        foreach (bool _ in _reach.Run())
            yield return false;
        _remaining = new HashSet<MarkPos>(Reach.Clearable);
        yield return true;
    }

    /// <summary><see cref="Retarget"/> to the end.</summary>
    public void RetargetNow(IReadOnlySet<MarkPos> targets)
    {
        foreach (var _ in Retarget(targets)) { }
    }

    /// <summary>Takes a plan's targets and bands (README "Eidolon", mining, *Stations*: a new plan swaps
    /// in): what is left of its targets (those still solid), the reach bound over them made again.</summary>
    public IEnumerable<bool> Adopt(MinePlan plan)
    {
        var targets = plan.Targets.Where(c => Site.Rule(c) == MineRule.Mine).ToHashSet();
        foreach (bool _ in Retarget(targets))
            yield return false;
        Bands = plan.Bands(_remaining);
        Stale = false;
        yield return true;
    }

    /// <summary><see cref="Adopt"/> to the end.</summary>
    public void AdoptNow(MinePlan plan)
    {
        foreach (var _ in Adopt(plan)) { }
    }

    /// <summary>
    /// The guard (README "Eidolon", mining, *Stations*; BFD's three conditions): whether <paramref name="cells"/> may be dug from
    /// <paramref name="stand"/>, tried and undone. (a) The stand is a place it may stand, and out; (b)
    /// every cell is a remaining target in its reach; (c) no cell is in a column of another's box at any
    /// height (under it is its floor, over it is over it), and after the dig the stand is still out, no
    /// other remaining target has lost its last live potential stand, and each other eidolon's node and
    /// held stands that were out still are. <paramref name="workers"/>: the others (none in the plan's
    /// simulation).
    /// </summary>
    public MineRefusal Check(PathCell stand, IReadOnlyList<MarkPos> cells, IReadOnlyList<MineWorkerAt> workers) =>
        Check(stand, cells, workers, null);

    /// <summary><see cref="Check"/>, and on <see cref="MineRefusal.CutsOff"/> or <see cref="MineRefusal.Starves"/>
    /// the cells to blame (<paramref name="blame"/>): those under the stand's way out, or those that were
    /// the last footing of a potential stand (one of them a starved target's last); and the stands of
    /// <paramref name="keep"/> (stations still to be worked before it) that were out must stay out.</summary>
    public MineRefusal Check(PathCell stand, IReadOnlyList<MarkPos> cells, IReadOnlyList<MineWorkerAt> workers, ICollection<MarkPos>? blame,
        IReadOnlyList<PathCell>? keep = null)
    {
        Refresh();
        if (!Site.Standable(stand) || BadStands.Contains(stand))
            return MineRefusal.NotStandable;
        if (!Walk.Out(stand))
            return MineRefusal.NotOut;
        foreach (var c in cells)
            if (!_remaining.Contains(c) || !EidolonMining.InReach(stand.X, stand.Y, stand.Z, c))
                return MineRefusal.OutOfReach;
        foreach (var w in workers)
        foreach (var c in cells)
            if (EidolonMining.UnderFoot(w.X, w.Z, c))
                return c.Y <= w.Feet ? MineRefusal.WorkerFloor : MineRefusal.OverWorker;
        var watched = new List<PathCell>();
        foreach (var w in workers)
        {
            var at = WidePath.NodeAt(w.X, w.Feet, w.Z);
            if (Walk.Out(at))
                watched.Add(at);
            foreach (var h in w.Holds)
                if (Walk.Out(h))
                    watched.Add(h);
        }
        var kept = keep?.Where(Walk.Out).ToList() ?? [];
        int ms = Site.Mark(), mw = Walk.Mark(), mr = Reach.Mark();
        var starved = new List<MarkPos>();
        var killed = new List<PathCell>();
        foreach (var c in cells)
            Site.Dig(c);
        Walk.Try(cells);
        Reach.Dig(cells, starved, killed);
        var result = MineRefusal.None;
        var cellSet = cells.ToHashSet();
        if (!Walk.OutAfter(stand))
            result = MineRefusal.CutsOff;
        else if (starved.Where(t => !cellSet.Contains(t) && _remaining.Contains(t) && Reach.Live(t) == 0).Distinct().ToList() is { Count: > 0 } lost)
        {
            result = MineRefusal.Starves;
            if (blame != null)
                foreach (var k in killed)
                for (int x = k.X - 1; x <= k.X; x++)
                for (int z = k.Z - 1; z <= k.Z; z++)
                    if (cellSet.Contains(new MarkPos(x, k.Y - 1, z)))
                        blame.Add(new MarkPos(x, k.Y - 1, z));
        }
        else if (watched.Any(n => !Walk.OutAfter(n)))
            result = MineRefusal.CutsOffWorker;
        else if (kept.Any(n => !Walk.OutAfter(n)))
            result = MineRefusal.CutsOffStation;
        Reach.Undo(mr);
        Walk.Undo(mw);
        Site.Undo(ms);
        if (result == MineRefusal.CutsOff && blame != null)
            foreach (var node in Walk.Route(stand))
                for (int x = node.X - 1; x <= node.X; x++)
                for (int z = node.Z - 1; z <= node.Z; z++)
                    if (cellSet.Contains(new MarkPos(x, node.Y - 1, z)))
                        blame.Add(new MarkPos(x, node.Y - 1, z));
        return result;
    }

    /// <summary>Cells broken (by the job, or seen gone): dug for good.</summary>
    public void Apply(IEnumerable<MarkPos> broken)
    {
        Refresh();
        var dug = new List<MarkPos>();
        foreach (var c in broken)
        {
            _remaining.Remove(c);
            Bands?.Remove(c);
            if (!Site.Solid(c.X, c.Y, c.Z) && Site.Rule(c) == MineRule.Air)
                continue;
            Site.Dig(c);
            dug.Add(c);
        }
        Walk.Dig(dug);
        Reach.Dig(dug, null);
    }

    /// <summary>A change seen in the world that the job did not make (placed, broken by a player, a
    /// liquid): taken in, the graphs made again before they are next asked (<see cref="Refresh"/>); the
    /// plan is stale (<see cref="Stale"/>).</summary>
    public void Changed(MarkPos cell, MineVoxel voxel)
    {
        Site.Set(cell, voxel);
        if (voxel.Rule != MineRule.Mine)
        {
            _remaining.Remove(cell);
            Bands?.Remove(cell);
        }
        _dirty = true;
        Stale = true;
    }

    /// <summary>A block column of the window read again from <paramref name="world"/> (one loaded since):
    /// taken in as <see cref="Changed"/> takes a block.</summary>
    public void Reloaded(IMineWorld world, int x, int z)
    {
        Site.Reload(world, x, z);
        _dirty = true;
        Stale = true;
    }

    /// <summary>After <see cref="Changed"/>: the walk graph and the reach bound made again over what is
    /// left of the targets, a step at a time (the game slices it; asking the state does it at once).</summary>
    public IEnumerable<bool> Rebuild()
    {
        var targets = _remaining.ToHashSet();
        foreach (bool _ in Build(targets))
            yield return false;
        if (Bands != null)
            foreach (var c in Bands.RemainingCells.Where(c => !_remaining.Contains(c)).ToList())
                Bands.Remove(c);
        yield return true;
    }

    private void Refresh()
    {
        if (!_dirty)
            return;
        foreach (var _ in Rebuild()) { }
    }

    public MineState Clone() => new(this);
}
