namespace SeraphHorizons.Mod.Eidolon.Core;

/// <summary>One member of a mining job as dispatch sees it: where it is (its feet's centre and the block
/// its feet are in), the stations it holds (the one it works first), and how long until it is done with
/// what it is doing now.</summary>
public readonly record struct MineWorker(long Id, double X, int Feet, double Z,
    IReadOnlyList<MineStation> Queue, double BusySeconds);

/// <summary>
/// Who takes which station (README "Eidolon", working together, *Dispatch*): earliest arrival over the
/// plan's executable front (Factorio's), each worker a short queue (<see cref="QueueLength"/>). Of the
/// first <see cref="Front"/> stations in plan order that nobody holds, whose claim is free and which pass
/// the guard against the live state (<see cref="Executable"/>), a worker takes the one no other would
/// reach sooner (FFF #374), then the one it reaches soonest, then the first. Claims are block columns on
/// the job board (<see cref="Claim"/>): two workers' stations never share one, so their boxes keep a
/// column apart in the open and their lanes apart in a tunnel, at most one a lane at a face
/// (<see cref="FaceCap"/>).
/// </summary>
public static class MineDispatch
{
    /// <summary>The station it works and the next.</summary>
    public const int QueueLength = 2;

    /// <summary>Unclaimed stations in plan order looked at.</summary>
    public const int Front = 24;

    public const double WalkSecondsPerBlock = 0.6;

    /// <summary>A granite strike at 3/4 pace, for estimates only.</summary>
    public const double StrikeSeconds = 1.2;

    /// <summary>When it would get to a station's stand: once done with what it does now, from the last
    /// stand it holds (or where it is), the octile distance with <see cref="WidePath.ClimbCost"/> for each
    /// block climbed, at <see cref="WalkSecondsPerBlock"/>.</summary>
    public static double Arrival(MineWorker worker, MineStation station)
    {
        double x = worker.X, z = worker.Z;
        int feet = worker.Feet;
        if (worker.Queue.Count > 0)
        {
            var last = worker.Queue[^1].Stand;
            (x, z, feet) = (last.X, last.Z, last.Y);
        }
        double dx = Math.Abs(station.Stand.X - x), dz = Math.Abs(station.Stand.Z - z);
        double octile = Math.Max(dx, dz) + (Math.Sqrt(2) - 1) * Math.Min(dx, dz);
        return worker.BusySeconds + (octile + WidePath.ClimbCost * Math.Max(0, station.Stand.Y - feet)) * WalkSecondsPerBlock;
    }

    /// <summary>The station's cells it may dig now: still to dig in the live state, and of a band whose
    /// turn it is (<see cref="MineState.Bands"/>).</summary>
    public static IReadOnlyList<MarkPos> Cells(MineState live, MineStation station) =>
        station.Cells.Where(c => live.Remaining.Contains(c) && (live.Bands?.Eligible(c) ?? true)).ToList();

    /// <summary>Whether a station may be worked now: some of its cells may be dug (<see cref="Cells"/>),
    /// and those pass the guard against the live state with the others where they are
    /// (<see cref="MineState.Check"/>).</summary>
    public static bool Executable(MinePlan plan, MineState live, MineStation station, IReadOnlyList<MineWorkerAt> others, out MineRefusal why) =>
        Executable(plan, live, station, others, out why, null);

    /// <summary><see cref="Executable(MinePlan, MineState, MineStation, IReadOnlyList{MineWorkerAt}, out MineRefusal)"/>,
    /// an earlier station's stand kept only while it has cells left that are <paramref name="usable"/>
    /// (<see cref="Before"/>).</summary>
    public static bool Executable(MinePlan plan, MineState live, MineStation station, IReadOnlyList<MineWorkerAt> others, out MineRefusal why,
        Func<MarkPos, bool>? usable) => Executable(live, station, others, out why, Alive(plan, live, usable));

    // Executable with the plan's stations that have cells left (Alive) found already.
    private static bool Executable(MineState live, MineStation station, IReadOnlyList<MineWorkerAt> others, out MineRefusal why,
        IReadOnlyList<MineStation> alive)
    {
        var cells = Cells(live, station);
        if (cells.Count == 0)
        {
            why = MineRefusal.OutOfReach;
            return false;
        }
        var before = Before(alive, station);
        if (Footing(before, cells))
        {
            why = MineRefusal.CutsOffStation;
            return false;
        }
        why = live.Check(station.Stand, cells, others, null, before);
        return why == MineRefusal.None;
    }

    // The plan's stations with a cell left (and <paramref name="usable"/>), in its order.
    private static List<MineStation> Alive(MinePlan plan, MineState live, Func<MarkPos, bool>? usable)
    {
        var alive = new List<MineStation>();
        foreach (var s in plan.Stations)
            foreach (var c in s.Cells)
                if (live.Remaining.Contains(c) && (usable?.Invoke(c) ?? true))
                {
                    alive.Add(s);
                    break;
                }
        return alive;
    }

    /// <summary>Whether any of <paramref name="cells"/> is under the box of one of <paramref name="stands"/>,
    /// on the layer its feet stand on: digging it takes that stand's footing.</summary>
    public static bool Footing(IReadOnlyList<PathCell> stands, IEnumerable<MarkPos> cells) =>
        cells.Any(c => stands.Any(s => c.Y == s.Y - 1 && c.X >= s.X - 1 && c.X <= s.X && c.Z >= s.Z - 1 && c.Z <= s.Z));

    /// <summary>The stands of the stations the plan digs before <paramref name="station"/> that still have
    /// cells to dig, near it (within <see cref="Near"/> columns of its cells): a station worked out of the
    /// plan's order (several workers each at their own) must not dig the footing of a place another still
    /// to be worked stands at (<see cref="Footing"/>), nor cut it off, or that one could never be worked as
    /// planned. With <paramref name="usable"/>, only those with cells left it allows count (one whose
    /// cells left are all under an eidolon parked in the area holds no stand for now).</summary>
    public static IReadOnlyList<PathCell> Before(MinePlan plan, MineState live, MineStation station, Func<MarkPos, bool>? usable = null) =>
        Before(Alive(plan, live, usable), station);

    private static List<PathCell> Before(IReadOnlyList<MineStation> alive, MineStation station)
    {
        int minX = station.Cells.Min(c => c.X) - Near, maxX = station.Cells.Max(c => c.X) + Near;
        int minZ = station.Cells.Min(c => c.Z) - Near, maxZ = station.Cells.Max(c => c.Z) + Near;
        var stands = new List<PathCell>();
        foreach (var s in alive)
        {
            if (s.Id >= station.Id)
                break;
            if (s.Stand.X >= minX && s.Stand.X <= maxX && s.Stand.Z >= minZ && s.Stand.Z <= maxZ)
                stands.Add(s.Stand);
        }
        return stands;
    }

    /// <summary>How near a station's cells, in columns, an earlier one's stand is kept for it (<see cref="Before"/>).</summary>
    public const int Near = 8;

    /// <summary>The station <paramref name="worker"/> takes next, or null for none: of the first
    /// <see cref="Front"/> in plan order not in anyone's queue, <paramref name="free"/> (its claim free on
    /// the board) and with cells it may dig now, those <see cref="Executable"/> (the guard asked of these
    /// only, the earlier stations' stands found once), the one no other would reach sooner, then the one it reaches
    /// soonest, then the first. With <paramref name="usable"/>, a station's cells are first cut to those
    /// it allows (the game: none in the columns of an eidolon standing there, which may be no worker, so the
    /// rest of a station round one parked in the area is dug and what is under it waits), and the station
    /// is given back so cut. With <paramref name="fit"/>, a station is first made to fit round what the
    /// others hold (<see cref="Fit"/>; null: none of it does).</summary>
    public static MineStation? Next(MinePlan plan, MineState live, MineWorker worker, IReadOnlyList<MineWorker> others, Func<MineStation, bool> free,
        Func<MarkPos, bool>? usable = null, Func<MineStation, MineStation?>? fit = null)
    {
        // stations held: those of this plan (a queue may still hold a replaced plan's, until its next look)
        var taken = others.SelectMany(o => o.Queue).Concat(worker.Queue)
            .Where(s => s.Id < plan.Stations.Count && plan.Stations[s.Id].Stand == s.Stand).Select(s => s.Id).ToHashSet();
        var at = others.Select(o => new MineWorkerAt(o.Id, o.X, o.Feet, o.Z, o.Queue.Select(s => s.Stand).ToList())).ToList();
        var alive = Alive(plan, live, usable);
        // the front: the first Front not held, that fit and whose cells may be dug now; then the guard
        var front = new List<MineStation>();
        foreach (var listed in alive)
        {
            if (taken.Contains(listed.Id))
                continue;
            if ((fit == null ? listed : fit(listed)) is not { } planned || !planned.Cells.Any(live.Remaining.Contains) || !free(planned))
                continue;
            var station = planned;
            if (usable != null && !planned.Cells.All(usable))
            {
                var cells = planned.Cells.Where(usable).ToList();
                if (!cells.Any(live.Remaining.Contains))
                    continue;
                station = planned with { Cells = cells };
            }
            if (Cells(live, station).Count == 0)
                continue;
            front.Add(station);
            if (front.Count == Front)
                break;
        }
        var found = front.Where(s => Executable(live, s, at, out _, alive)).ToList();
        return found
            .Select(s => (Station: s, Mine: Arrival(worker, s)))
            .OrderBy(s => others.Any(o => Arrival(o, s.Station) < s.Mine))
            .ThenBy(s => s.Mine)
            .ThenBy(s => s.Station.Id)
            .Select(s => s.Station)
            .FirstOrDefault();
    }

    /// <summary>
    /// The block columns a station claims (README "Eidolon", working together, *Claims*): those of its cells and of its stand's box;
    /// in the open the box's grown by a column all round (two workers' boxes keep two columns apart, and
    /// neither digs within a column of the other's box, while their cells may lie side by side); at a face, the
    /// lane's whole width at each slice it digs, grown by one along the drive but never across the lane's
    /// edges (the lane is the separation).
    /// </summary>
    public static IEnumerable<(int X, int Z)> Claim(MineStation station, MineTunnel? tunnel)
    {
        var own = station.Cells.Select(c => (c.X, c.Z)).ToHashSet();
        for (int x = station.Stand.X - 1; x <= station.Stand.X; x++)
        for (int z = station.Stand.Z - 1; z <= station.Stand.Z; z++)
            own.Add((x, z));
        var claim = new HashSet<(int X, int Z)>(own);
        if (station.Kind == MineStationKind.Face && tunnel != null && station.Lane >= 0 && station.Lane < tunnel.Lanes.Count)
        {
            var lane = tunnel.Lanes[station.Lane];
            int a0 = tunnel.AlongX ? lane.MinZ : lane.MinX, a1 = tunnel.AlongX ? lane.MaxZ : lane.MaxX;
            foreach (var c in station.Cells)
            {
                int slice = tunnel.Slice(c.X, c.Z);
                for (int a = a0; a <= a1; a++)
                    claim.Add(tunnel.Column(slice, a));
            }
            foreach (var (x, z) in claim.ToList())
            for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz <= 1; dz++)
            {
                int across = tunnel.AlongX ? z + dz : x + dx;
                if (across >= a0 && across <= a1)
                    claim.Add((x + dx, z + dz));
            }
        }
        else
            for (int x = station.Stand.X - 2; x <= station.Stand.X + 1; x++)
            for (int z = station.Stand.Z - 2; z <= station.Stand.Z + 1; z++)
                claim.Add((x, z));
        return claim.OrderBy(c => c.X).ThenBy(c => c.Z);
    }

    /// <summary>
    /// A station cut to fit round the block columns others hold (README "Eidolon", working together,
    /// *Claims*): in the open (a bench, a stair) the cells whose column is <paramref name="free"/>, while its
    /// stand's box and a column round it are, so a station another works
    /// next to is shared, the rest of it left for later, rather than waited for whole (in a pit too narrow
    /// for two whole stations, two work it side by side); at a face, all of its claim or nothing (the lane
    /// is the unit). Null when none of it fits.
    /// </summary>
    public static MineStation? Fit(MineStation station, MineTunnel? tunnel, Func<(int X, int Z), bool> free)
    {
        if (station.Kind == MineStationKind.Face)
            return Claim(station, tunnel).All(free) ? station : null;
        var s = station.Stand;
        for (int x = s.X - 2; x <= s.X + 1; x++)
        for (int z = s.Z - 2; z <= s.Z + 1; z++)
            if (!free((x, z)))
                return null;
        bool Open(MarkPos c) => free((c.X, c.Z));
        if (station.Cells.All(Open))
            return station;
        var cells = station.Cells.Where(Open).ToList();
        return cells.Count == 0 ? null : station with { Cells = cells };
    }

    /// <summary>How many may work a covered band's face at once: one a lane.</summary>
    public static int FaceCap(MineTunnel tunnel) => tunnel.Lanes.Count;
}
