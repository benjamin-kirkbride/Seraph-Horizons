namespace SeraphHorizons.Mod.Eidolon.Core;

/// <summary>What a station costs in time (README "Eidolon", working together, *Routes*): its strikes at the
/// pace of its blocks, and the walk to it. A station's strikes are taken as its cells sorted slowest first
/// and cut into groups of four, each group as long as a strike at its slowest block
/// (<see cref="EidolonMining.StrikeSeconds"/>, <see cref="EidolonMining.Timing"/>), which is the strike's own
/// rule and over by a strike at most; a walk is the cheapest way on the walk graph, a step 1 or √2 and a
/// climb past the first block <see cref="WidePath.ClimbCost"/> a block, as <see cref="WidePath"/> costs it,
/// at <see cref="WalkSecondsPerBlock"/>, or where either end is no out node yet the octile estimate
/// (<see cref="Estimate"/>, as <see cref="MineDispatch.Arrival"/>).</summary>
public static class MineCosts
{
    /// <summary>A block's pace where it is not known: granite at the steel pick, which is what
    /// <see cref="MineDispatch.StrikeSeconds"/> took (0.9 / 0.75 = 1.2).</summary>
    public const double DefaultPlayerSeconds = 0.9;

    /// <summary>Settling on the stand once there, a stop.</summary>
    public const double ArriveSeconds = 1.0;

    public const double WalkSecondsPerBlock = MineDispatch.WalkSecondsPerBlock;

    /// <summary>Nodes a search settles at most.</summary>
    public const int MaxSearchNodes = 40_000;

    /// <summary>Seconds to strike <paramref name="cells"/>, at the pace the site holds for each.</summary>
    public static double WorkSeconds(MineSite site, IReadOnlyList<MarkPos> cells)
    {
        if (cells.Count == 0)
            return 0;
        var paces = new double[cells.Count];
        for (int k = 0; k < cells.Count; k++)
            paces[k] = site.PlayerSeconds(cells[k]);
        Array.Sort(paces);
        Array.Reverse(paces);
        double seconds = 0;
        for (int k = 0; k < paces.Length; k += 4)
            seconds += EidolonMining.Timing(EidolonMining.StrikeSeconds([paces[k]])).Seconds;
        return seconds;
    }

    /// <summary>Seconds to strike a station's cells from its stand, group by group as it will
    /// (<see cref="MineStations.NextGroup"/>), each group at its slowest block's pace: exact where nothing
    /// else changes meanwhile (the planner's own stations; <see cref="WorkSeconds(MineSite, IReadOnlyList{MarkPos})"/>
    /// takes four a strike, which a station whose cells do not fall in fours outruns).</summary>
    public static double WorkSeconds(MineSite site, MineStation station, MineTunnel? tunnel)
    {
        var left = station.Cells.ToHashSet();
        var at = station.Stand;
        double seconds = 0;
        for (int strikes = 0; left.Count > 0 && strikes < station.Cells.Count; strikes++)
        {
            if (MineStations.NextGroup(station, tunnel, left.Contains, at.X, at.Y, at.Z) is not { } group)
                break;
            seconds += EidolonMining.Timing(EidolonMining.StrikeSeconds(group.Cells.Select(site.PlayerSeconds))).Seconds;
            left.ExceptWith(group.Cells);
        }
        // what no group from the stand takes, at four a strike
        return left.Count == 0 ? seconds : seconds + WorkSeconds(site, left.ToList());
    }

    /// <summary>The walk from <paramref name="from"/> to <paramref name="to"/> estimated: the octile distance
    /// and <see cref="WidePath.ClimbCost"/> a block climbed, at <see cref="WalkSecondsPerBlock"/>.</summary>
    public static double Estimate(PathCell from, PathCell to)
    {
        double dx = Math.Abs(to.X - from.X), dz = Math.Abs(to.Z - from.Z);
        double octile = Math.Max(dx, dz) + (Math.Sqrt(2) - 1) * Math.Min(dx, dz);
        return (octile + WidePath.ClimbCost * Math.Max(0, to.Y - from.Y)) * WalkSecondsPerBlock;
    }

    /// <summary>A step's cost in blocks, as <see cref="WidePath"/> costs it: 1 or √2, and
    /// <see cref="WidePath.ClimbCost"/> for each block climbed past the first.</summary>
    public static double StepCost(PathCell from, PathCell to) =>
        (from.X != to.X && from.Z != to.Z ? Math.Sqrt(2) : 1) + WidePath.ClimbCost * Math.Max(0, to.Y - from.Y - 1);

    /// <summary>Walking seconds from <paramref name="from"/> to every out node within
    /// <paramref name="maxNodes"/> settled, on <paramref name="walk"/>'s graph (Dijkstra); stops once every one
    /// of <paramref name="targets"/> that is an out node is settled. A start that is no out node (it stands
    /// between two, or on one not yet out) starts from the out node next to it, if one is. With
    /// <paramref name="avoid"/>, the nodes it is true of are not walked through, but for the start's
    /// neighbours (it may set off from beside what it avoids): a way round others' claims. With
    /// <paramref name="beyond"/>, it also stops once it is that many blocks past the nearest target it reached
    /// (those farther are left unreached: a choice among the near ones).</summary>
    public static MineDistances From(MineWalk walk, PathCell from, IReadOnlyCollection<PathCell> targets, int maxNodes = MaxSearchNodes,
        Func<PathCell, bool>? avoid = null, double beyond = double.PositiveInfinity)
    {
        var site = walk.Space;
        var result = new MineDistances(site, from);
        var start = Snap(walk, from);
        if (start == null)
            return result;
        int s = site.Index(start.Value.X, start.Value.Y, start.Value.Z);
        var wanted = new HashSet<int>();
        foreach (var t in targets)
            if (site.InWindow(t.X, t.Y, t.Z) && walk.Out(t))
                wanted.Add(site.Index(t.X, t.Y, t.Z));
        var open = new PriorityQueue<int, double>();
        result.Cost[s] = (0, -1);
        open.Enqueue(s, 0);
        var settled = new HashSet<int>();
        Span<int> joined = stackalloc int[40];
        double nearest = double.PositiveInfinity;
        while (open.TryDequeue(out int u, out double cost))
        {
            if (!settled.Add(u))
                continue;
            if (cost > nearest + beyond)
                break;
            if (wanted.Remove(u))
                nearest = Math.Min(nearest, cost);
            if (wanted.Count == 0 && targets.Count > 0 || settled.Count >= maxNodes)
                break;
            var p = site.At(u).ToCell();
            int count = walk.JoinedTo(u, joined);
            for (int k = 0; k < count; k++)
            {
                int v = joined[k];
                if (settled.Contains(v) || !walk.OutAt(v))
                    continue;
                var q = site.At(v).ToCell();
                if (avoid != null && u != s && avoid(q))
                    continue;
                double next = cost + StepCost(p, q);
                if (result.Cost.TryGetValue(v, out var old) && old.Blocks <= next)
                    continue;
                result.Cost[v] = (next, u);
                open.Enqueue(v, next);
            }
        }
        return result;
    }

    /// <summary>Why a member at <paramref name="at"/> cannot be placed on <paramref name="state"/>'s walk graph
    /// (no <see cref="Snap"/>: a solve gives it no stops), for the log; null when it can.</summary>
    public static string? Unplaced(MineState state, PathCell at)
    {
        if (Snap(state.Walk, at) != null)
            return null;
        var (site, w) = (state.Site, state.Site.Window);
        int off = MineSite.Off(site.Area, at);
        // no braces: the game's log takes it as a format string
        return MineSite.Holds(w, at)
            ? "no out node a column or a step off where it stands"
            : $"outside the window ({w.Min.X},{w.Min.Y},{w.Min.Z})-({w.Max.X},{w.Max.Y},{w.Max.Z}), {off} columns from the area"
                + (off > MineSite.GrowColumns ? $" (beyond {MineSite.GrowColumns}: the window is not grown for it)" : "");
    }

    /// <summary>The out node a walk from <paramref name="at"/> starts from: itself, or the nearest out node
    /// a column or a step off it; null for none.</summary>
    public static PathCell? Snap(MineWalk walk, PathCell at)
    {
        if (walk.Out(at))
            return at;
        PathCell? best = null;
        int bestD = int.MaxValue;
        for (int dy = -2; dy <= 2; dy++)
        for (int dx = -1; dx <= 1; dx++)
        for (int dz = -1; dz <= 1; dz++)
        {
            var n = new PathCell(at.X + dx, at.Y + dy, at.Z + dz);
            int d = Math.Abs(dx) + Math.Abs(dz) + 2 * Math.Abs(dy);
            if (d < bestD && walk.Out(n))
                (best, bestD) = (n, d);
        }
        return best;
    }
}

/// <summary>A search's result (<see cref="MineCosts.From"/>): the walking seconds to each node it reached,
/// and the way there.</summary>
public sealed class MineDistances
{
    private readonly MineSite _site;

    internal MineDistances(MineSite site, PathCell from)
    {
        _site = site;
        Start = from;
    }

    internal Dictionary<int, (double Blocks, int Parent)> Cost { get; } = new();

    /// <summary>Where the search started from (as asked, before it was put on an out node).</summary>
    public PathCell Start { get; }

    /// <summary>Walking seconds to <paramref name="to"/>; null where it was not reached.</summary>
    public double? Seconds(PathCell to) =>
        _site.InWindow(to.X, to.Y, to.Z) && Cost.TryGetValue(_site.Index(to.X, to.Y, to.Z), out var c)
            ? c.Blocks * MineCosts.WalkSecondsPerBlock
            : null;

    /// <summary>The way to <paramref name="to"/>, from the node it started from; empty where not reached.</summary>
    public IReadOnlyList<PathCell> Path(PathCell to)
    {
        var path = new List<PathCell>();
        if (!_site.InWindow(to.X, to.Y, to.Z) || !Cost.ContainsKey(_site.Index(to.X, to.Y, to.Z)))
            return path;
        int at = _site.Index(to.X, to.Y, to.Z);
        while (at >= 0 && path.Count <= Cost.Count)
        {
            path.Add(_site.At(at).ToCell());
            at = Cost[at].Parent;
        }
        path.Reverse();
        return path;
    }

    /// <summary>Walking seconds to <paramref name="to"/>, or the estimate from <paramref name="from"/> where
    /// it was not reached.</summary>
    public double Or(PathCell to, PathCell from) => Seconds(to) ?? MineCosts.Estimate(from, to);
}
