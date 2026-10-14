using System.Diagnostics;

namespace SeraphHorizons.Mod.Eidolon.Core;

/// <summary>
/// Makes a job's route plan, off the main thread (README "Eidolon", working together, *Routes*), from a copy
/// of the live state. First a simulation of the crew working forward (the rollout): each member, when it is
/// free, takes of the stations it may dig then the one it would finish soonest (strike by strike,
/// <see cref="MineCosts.WorkSeconds(MineSite, MineStation, MineTunnel?)"/>), its walk on the walk graph,
/// crossing another's claim while it walks, or letting another's planned way cross its claim, only after
/// waiting <see cref="PassAfter"/> for it (or by the way round, if not much longer), waiting its turn at every
/// narrow way
/// (<see cref="MineSlots"/>), guard-checked against the world as it would be then with everyone's later
/// stands kept
/// (<see cref="MineDispatch.Executable(MinePlan, MineState, MineStation, IReadOnlyList{MineWorkerAt}, out MineRefusal, Func{MarkPos, bool}?, bool, IReadOnlyList{PathCell}?)"/>);
/// work ahead of the band (<see cref="MineAhead"/>) only for one that would otherwise wait
/// <see cref="AheadWaitSeconds"/>, and a planned wait out of the narrow ways and the others' claims when there
/// is nothing. How it chooses is one of a few <see cref="Variant"/>s: for a job of at most
/// <see cref="WholeJobStations"/> stations each is simulated to the job's end, as estimated and with its strikes
/// shaken, and the one that finishes soonest on the mean makes the routes; today's dispatch is simulated the same
/// way (<c>TodaySim</c>), and where it finishes no later the job is left to it (<see cref="MineRoutes.Today"/>: no
/// routes, the game dispatches as before). The choice is made once a job and kept by its re-solves
/// (<see cref="MineRouteInput.Keep"/>). A station a stop took only part of (its claim cut round another's) is free again for the rest
/// once that stop is done. Then each member's stations are put in their best order exactly
/// (<see cref="MineOrdering"/>, with when each became workable and what must come first), and stations are
/// moved, one at a time, off the member the simulation finishes last while that brings the whole job's finish
/// in; each change is simulated again from the copy, every check made again, and kept only when it holds and
/// is no worse (<see cref="Validations"/> such simulations at most). The objective is the whole job's
/// estimated finish: the latest member's, or everyone's work and the work no route holds shared out,
/// whichever is later; then less walking, then less waiting. A pure function of its input: every loop is
/// bounded by a count, ties go to ids.
/// </summary>
public static class MineRoutePlanner
{
    /// <summary>Stations a decision looks at: the nearest, and the first in plan order; and ahead.</summary>
    public const int PoolSize = 32, PlanOrderPool = 8, AheadPool = 16;

    /// <summary>Candidates the guard is asked of a decision, best first.</summary>
    public const int GuardTries = 8;

    /// <summary>A member works ahead only when its best station would start this much later; an ahead stop
    /// counts this much later in the choice.</summary>
    public const double AheadWaitSeconds = 10, AheadPenaltySeconds = 10;

    public const int Relocations = 16, Validations = 3;

    /// <summary>The most a member puts off setting off for another's claim on its way.</summary>
    public const double CrossDelayMax = 30;

    /// <summary>How many blocks past the nearest station's stand a decision's search goes: one farther is
    /// not taken this decision.</summary>
    public const double SearchBeyond = 24;

    /// <summary>How much longer a way round others' claims may be than the way through them.</summary>
    public const double DetourSeconds = 4;

    // moves weighed a relocation, the cheapest by an insertion estimate
    private const int MovesWeighed = 20;

    // a planned wait when nothing else happens
    private const double WaitSeconds = 5;

    // joins searched for a place to wait out of the way
    private const int WaitJoins = 6;

    // how long a validated member waits for its next station before the routes are taken as failing
    private const double FixedPatience = 60;

    // the score counts equal within this
    private const double ScoreSlack = 0.5;

    private const int MaxEvents = 20_000;

    /// <summary>How a rollout chooses: the soonest finish (the first rollout), or that among the stations no
    /// other member would finish sooner (FFF #374's rule, today's dispatch's).</summary>
    internal enum RollPolicy { Soonest, Keep }

    /// <summary>How a rollout chooses among the stations a member may take (README "Eidolon", working together,
    /// *Routes*): the soonest finished; or that among those no other member would finish sooner (FFF #374, as
    /// today's dispatch); with or without a bonus for the work a station opens the way to below it (the critical
    /// path); leaving a station to a member that would finish it sooner (none is handed over at a cost); and with
    /// a member that waits stepping off cells still to dig, back where it started if nothing is near.</summary>
    internal sealed record Variant(RollPolicy Policy, double Critical, bool Leave, bool Clear);

    private static readonly Variant[] Variants =
    [
        new(RollPolicy.Soonest, 0, false, false),
        new(RollPolicy.Keep, 0, false, false),
        new(RollPolicy.Soonest, 0.5, false, false),
        new(RollPolicy.Keep, 0.5, false, false),
        new(RollPolicy.Keep, 0, true, false),
        new(RollPolicy.Soonest, 0, true, true),
        new(RollPolicy.Keep, 0, true, true),
        new(RollPolicy.Keep, 0.5, true, true),
    ];

    /// <summary>A job with at most this many stations left is simulated to its end by every variant, and the
    /// variant that finishes it soonest makes the routes; a larger one is made by the first.</summary>
    public const int WholeJobStations = 300;

    /// <summary>Simulations a variant is judged by: as timed, and with its strikes shaken by seeds 1 on.</summary>
    public const int ShakenRuns = 3;

    /// <summary>Seconds a whole-job simulation that stalled with cells left is charged on top.</summary>
    private const double Stalled = 3600;

    /// <summary>How long a way across another's claim waits for it before it goes through (the live rule).</summary>
    public const double PassAfter = 4;

    /// <summary>A station's work shaken by up to ±30 % by <paramref name="seed"/> (0: as estimated), the same for
    /// every simulation with that seed.</summary>
    private static double ShakenWork(int seed, int station, double work)
    {
        if (seed == 0)
            return work;
        uint h = (uint)(station * 73856093) ^ (uint)(seed * 19349663);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return work * (0.7 + 0.6 * (h % 10000) / 10000.0);
    }

    /// <summary>Routes are taken over today's dispatch only when their simulated finish is sooner by more than
    /// this share (0: any sooner; a tie goes to today's, and the choice is kept for the job, so it never flips on
    /// noise).</summary>
    public const double TodayMargin = 0.0;

    /// <summary><see cref="MineRoutes.VariantIndex"/> of routes that leave the job to today's dispatch.</summary>
    public const int TodayIndex = 1000;

    /// <summary>A member leaves a station to another this much sooner at it (one at work: any sooner).</summary>
    private const double LeaveMargin = 5;

    /// <summary>The route plan for <paramref name="input"/>: a pure function of it (no clock; ties on ids);
    /// <paramref name="cancel"/> checked between decisions. <paramref name="input"/>.State is consumed
    /// (the planner digs in it).</summary>
    public static MineRoutes Solve(MineRouteInput input, CancellationToken cancel = default)
    {
        var clock = Stopwatch.StartNew();
        var pristine = input.State.Clone();
        var zones = MineChokes.Find(input.State.Walk, input.Plan.Area, input.Plan.Stair);
        var index = new MineZoneIndex(zones);
        var context = new Context(input, pristine, zones, index);
        int horizon = Horizon(context, input.State);
        // a small job: each variant simulates it to its end (as estimated, and with its strikes' lengths shaken, so
        // the choice holds when the world runs a little off the estimates), and the one that finishes it soonest
        // on the mean makes the routes: so a handover that costs more walking and climbing than it saves loses to
        // keeping the work with one member, and a crew that waits on itself to one that does not
        // a re-solve against the routes running keeps their variant (the choice is made when routes are made anew)
        var chosen = Variants[0];
        int? keep = input.Keep ?? input.Current?.VariantIndex;
        if (keep == TodayIndex)
            return Today(input, context, zones, index, input.Current?.Score.J ?? 0, clock);
        if (keep is { } reused && reused >= 0 && reused < Variants.Length)
            chosen = Variants[reused];
        else if (input.Members.Count > 1 && input.Plan.Stations.Count(s => s.Cells.Any(pristine.Remaining.Contains)) <= WholeJobStations)
        {
            double best = double.PositiveInfinity;
            foreach (var variant in Variants)
            {
                double sum = 0;
                for (int seed = 0; seed < ShakenRuns; seed++)
                {
                    // one already well behind the best on its first simulation is not run again
                    if (seed == 1 && sum > best * 1.1)
                    {
                        sum = double.PositiveInfinity;
                        break;
                    }
                    var other = new Context(input, pristine, zones, index);
                    var sim = new Sim(other, pristine.Clone(), Mode.Rollout, int.MaxValue, null) { Variant = variant, Whole = true, Shake = seed };
                    sim.Run(cancel);
                    context.Decisions += other.Decisions;
                    context.GuardChecks += other.GuardChecks;
                    context.Searches += other.Searches;
                    sum += sim.Makespan();
                }
                if (sum / ShakenRuns < best - ScoreSlack)
                    (best, chosen) = (sum / ShakenRuns, variant);
            }
            // today's dispatch, simulated the same way: the job is left to it unless routes finish sooner by a margin
            double today = 0;
            for (int seed = 0; seed < ShakenRuns; seed++)
            {
                var sim = new TodaySim(context, pristine.Clone(), seed);
                sim.Run(cancel);
                today += sim.Makespan();
            }
            today /= ShakenRuns;
            if (!(best < today * (1 - TodayMargin)))
                return Today(input, context, zones, index, today, clock);
            // every variant stalled (the crew in its own way to the end): the first, as for a large job
            if (best >= Stalled)
                chosen = Variants[0];
        }
        var roll = new Sim(context, input.State, Mode.Rollout, horizon, null) { Variant = chosen };
        roll.Run(cancel);
        var routes = roll;
        bool improved = false;
        int relocations = 0;
        if (roll.Lists() is { } lists && lists.Values.Any(l => l.Count > 0))
        {
            roll.ApplyAll();
            var improve = new Improver(context, roll, roll.State, lists);
            // each change is simulated again from the copy, every check made again, and kept only when it holds
            // and the job's estimated finish is no later (a reorder) or sooner (a move)
            Sim? Check(Dictionary<long, List<int>> candidate)
            {
                cancel.ThrowIfCancellationRequested();
                context.Validations++;
                var check = new Sim(context, pristine.Clone(), Mode.Validate, horizon, candidate);
                return check.Run(cancel) ? check : null;
            }
            var reordered = improve.Reorder();
            if (!Same(reordered, lists) && Check(reordered) is { } r && r.Score().J <= roll.Score().J + ScoreSlack)
            {
                (routes, lists, improved) = (r, reordered, true);
            }
            for (int round = 0; round < Relocations && context.Validations < Validations; round++)
            {
                if (improve.Move(lists, routes.Finishes(), cancel) is not { } moved)
                    break;
                if (Check(moved) is not { } m || m.Score().J >= routes.Score().J - ScoreSlack)
                    break;
                (routes, lists, improved) = (m, moved, true);
                relocations++;
            }
        }
        var made = routes.Build(context.Stats(relocations, improved, false, clock.Elapsed));
        made.VariantIndex = Array.IndexOf(Variants, chosen);
        if (input.Current is { } current)
        {
            var now = Evaluate(input with { State = pristine }, current);
            bool someoneNew = input.Members.Any(m => !current.Routes.TryGetValue(m.Id, out var r) || r.Stops.Count(s => s.Kind == MineStopKind.Station) == 0)
                && made.Routes.Values.Any(r => r.Stops.Any(s => s.Kind == MineStopKind.Station && !s.Pinned)
                    && (!current.Routes.TryGetValue(r.Member, out var c) || c.Stops.Count(s => s.Kind == MineStopKind.Station) == 0));
            if (!(made.Score.J <= now.Score.J * (1 - MineRoutes.ImproveShare) || someoneNew || now.Dropped > 0))
            {
                var kept = current.Copy();
                kept.Stats = made.Stats with { KeptCurrent = true, Took = clock.Elapsed };
                return kept;
            }
        }
        made.Stats = made.Stats with { Took = clock.Elapsed };
        return made;
    }

    // routes that leave the job to today's dispatch: none, the choice recorded (a re-solve against them keeps it)
    private static MineRoutes Today(MineRouteInput input, Context context, IReadOnlyList<MineZone> zones, MineZoneIndex index, double j, Stopwatch clock)
    {
        var routes = input.Members.OrderBy(m => m.Id).ToDictionary(m => m.Id, m => new MineRoute(m.Id, []) { FreeAt = input.Now });
        return new MineRoutes(input.Plan, input.PlanVersion, input.Now, routes, zones, index, new MineSlots(), (j, 0, 0),
            context.Stats(0, false, false, clock.Elapsed), 0) { VariantIndex = TodayIndex };
    }

    /// <summary>The objective of <paramref name="routes"/> re-simulated on <paramref name="input"/>'s state (a
    /// copy), the stops that fail dropped (<see cref="MineRoutes.Dropped"/>); for comparing a re-solve with what
    /// runs.</summary>
    public static MineRoutes Evaluate(MineRouteInput input, MineRoutes routes)
    {
        var clock = Stopwatch.StartNew();
        var state = input.State.Clone();
        var zones = MineChokes.Find(state.Walk, input.Plan.Area, input.Plan.Stair);
        var context = new Context(input, input.State, zones, new MineZoneIndex(zones));
        var lists = new Dictionary<long, List<int>>();
        foreach (var m in input.Members)
        {
            lists[m.Id] = new List<int>();
            if (!routes.Routes.TryGetValue(m.Id, out var r))
                continue;
            foreach (var s in r.Stops.Where(s => s.Kind == MineStopKind.Station && !(s.Pinned && s.Station == m.Head) && s.Station < input.Plan.Stations.Count))
            {
                lists[m.Id].Add(s.Seq);
                context.Keys[s.Seq] = (s.Station, s.Cells.Count > 0 ? s.Cells : input.Plan.Stations[s.Station].Cells, s.Ahead);
            }
        }
        var sim = new Sim(context, state, Mode.Evaluate, int.MaxValue, lists);
        sim.Run(default);
        var made = sim.Build(context.Stats(0, false, false, clock.Elapsed));
        made.Dropped = sim.DroppedStops;
        return made;
    }

    /// <summary>Whether stop <paramref name="a"/> must be done before <paramref name="b"/>: of an earlier band,
    /// a cell over one of b's, or a cell where b's stand puts its box (the dig that makes room for it).</summary>
    public static bool Must(MinePlan plan, MineStop a, MineStop b) =>
        Must(Band(plan, a.Station), a.Cells, a.At, a.Ahead, Band(plan, b.Station), b.Cells, b.At, b.Ahead);

    private static int Band(MinePlan plan, int station) => station >= 0 && station < plan.Stations.Count ? plan.Stations[station].Band : 0;

    internal static bool Must(int bandA, IReadOnlyList<MarkPos> cellsA, PathCell standA, bool aheadA,
        int bandB, IReadOnlyList<MarkPos> cellsB, PathCell standB, bool aheadB)
    {
        if (bandA < bandB || aheadB && !aheadA)
            return true;
        foreach (var a in cellsA)
        {
            foreach (var b in cellsB)
                if (a.X == b.X && a.Z == b.Z && a.Y > b.Y)
                    return true;
            if (a.X >= standB.X - 1 && a.X <= standB.X && a.Z >= standB.Z - 1 && a.Z <= standB.Z && a.Y >= standB.Y && a.Y <= standB.Y + 3)
                return true;
        }
        // b's cells are a's footing: a stands there first
        foreach (var b in cellsB)
            if (b.Y == standA.Y - 1 && b.X >= standA.X - 1 && b.X <= standA.X && b.Z >= standA.Z - 1 && b.Z <= standA.Z)
                return true;
        return false;
    }

    private static bool Same(Dictionary<long, List<int>> a, Dictionary<long, List<int>> b) =>
        a.Count == b.Count && a.All(p => b.TryGetValue(p.Key, out var l) && l.SequenceEqual(p.Value));

    // stations a member, by the mean work of those workable now
    private static int Horizon(Context context, MineState state)
    {
        double sum = 0;
        int n = 0;
        foreach (var s in context.Plan.Stations)
        {
            var cells = context.Usable(MineDispatch.Cells(state, s));
            if (cells.Count == 0)
                continue;
            sum += MineCosts.WorkSeconds(state.Site, cells);
            n++;
        }
        if (n == 0 || sum <= 0)
            return MineRoutes.MaxStops;
        return Math.Clamp((int)Math.Round(MineRoutes.HorizonSeconds / (sum / n)), MineRoutes.MinStops, MineRoutes.MaxStops);
    }

    private enum Mode { Rollout, Validate, Evaluate }

    // ---- what every simulation of one solve shares ----

    private sealed class Context(MineRouteInput input, MineState pristine, IReadOnlyList<MineZone> zones, MineZoneIndex index)
    {
        public readonly MineRouteInput Input = input;
        public readonly MinePlan Plan = input.Plan;
        public readonly MineState Pristine = pristine;
        public readonly IReadOnlyList<MineZone> Zones = zones;
        public readonly MineZoneIndex Index = index;
        public readonly Dictionary<int, double> FirstEligible = new();
        // the rollout's stops by key (its Seq): in the order taken, and which station's cells
        public readonly Dictionary<int, int> RollOrder = new();
        public readonly Dictionary<int, (int Station, IReadOnlyList<MarkPos> Cells, bool Ahead)> Keys = new();
        public int Decisions, GuardChecks, Searches, Validations;

        public IReadOnlyList<MarkPos> Usable(IReadOnlyList<MarkPos> cells) =>
            Input.Usable is { } usable ? cells.Where(usable).ToList() : cells;

        public MineRouteStats Stats(int relocations, bool improved, bool kept, TimeSpan took) =>
            new(Decisions, GuardChecks, Searches, relocations, Validations, improved, kept, took);
    }

    // ---- the simulation ----

    private sealed class SimStop
    {
        public MineStopKind Kind;
        public int Station = -1;
        public MineStation? Fitted;
        public PathCell At;
        public bool Ahead, Pinned, Applied;
        public IReadOnlyList<PathCell> Leg = [];
        public double LegSeconds, WorkSeconds, Depart, Arrive, Start, Finish;
        public int Seq;
        public int Key;
        public long Member;
        public HashSet<(int X, int Z)> Claim = new();
        public HashSet<(int X, int Z)>? LegColumns;
    }

    private sealed class Member(MineRouteMember input)
    {
        public readonly long Id = input.Id;
        public PathCell At = input.At;
        public PathCell Rest = input.At;
        public double Free;
        public bool Retired;
        public readonly List<SimStop> Stops = new();
        public int Stations;
        public Queue<int>? Fixed;
        public double FixedSince = double.NaN;
        public PathCell Place(double t)
        {
            // where it is at t: at its last stop's place once there, else where it set off from
            for (int k = Stops.Count - 1; k >= 0; k--)
                if (Stops[k].Arrive <= t)
                    return Stops[k].At;
                else if (Stops[k].Depart <= t && Stops[k].Leg.Count > 0)
                    return Stops[k].Leg[0];
            return input.At;
        }
    }

    private sealed class Sim
    {
        private readonly Context _c;
        private readonly MineState _state;
        private readonly Mode _mode;
        private readonly int _horizon;
        private readonly List<Member> _members;
        private readonly MineSlots _slots = new();
        private readonly List<SimStop> _pending = new();
        // stations with a stop not yet done: no other stop of them meanwhile (what is left of one after it is
        // free again)
        private readonly HashSet<int> _busy = new();
        private readonly List<MineStation> _universe;
        private int _universeVersion = -1;
        private readonly Dictionary<int, int> _top;
        private readonly double _now;
        private int _seq;
        private int _progress;

        public int DroppedStops;

        public Variant Variant { get; init; } = Variants[0];

        private RollPolicy Policy => Variant.Policy;

        private double Critical => Variant.Critical;

        /// <summary>A seed that shakes each station's work by up to ±30 % (0: as estimated).</summary>
        public int Shake { get; init; }

        private double Shaken(int station, double work) => ShakenWork(Shake, station, work);

        /// <summary>Whether it runs to the job's end (no horizon in time).</summary>
        public bool Whole { get; init; }

        /// <summary>When the simulated job is done, from the start: its last stop's finish, and if it stalled with
        /// cells left, those at four a strike by one member and <see cref="Stalled"/> more.</summary>
        public double Makespan()
        {
            double end = _members.SelectMany(m => m.Stops).Where(s => s.Kind == MineStopKind.Station).Select(s => s.Finish).DefaultIfEmpty(_now).Max();
            var left = _state.Remaining.Where(c => !_c.Plan.WayOut.Contains(c)).ToList();
            // a simulation that stalled with cells left loses to any that finished: what is left as if one did it,
            // and an hour more
            return end - _now + (left.Count == 0 ? 0 : MineCosts.WorkSeconds(_state.Site, left) + Stalled);
        }

        // the work left under a station's cells in their columns: what digging it opens the way to
        private double Beneath(MineStation s)
        {
            if (Critical == 0)
                return 0;
            int n = 0;
            int bottom = _state.Site.Area.Min.Y;
            foreach (var (x, z) in s.Cells.Select(c => (c.X, c.Z)).Distinct())
            {
                int low = s.Cells.Where(c => c.X == x && c.Z == z).Min(c => c.Y);
                for (int y = low - 1; y >= bottom; y--)
                    if (_state.Remaining.Contains(new MarkPos(x, y, z)))
                        n++;
            }
            return n * MineDispatch.StrikeSeconds / 4;
        }

        public MineState State => _state;

        public Sim(Context context, MineState state, Mode mode, int horizon, Dictionary<long, List<int>>? lists)
        {
            _c = context;
            _state = state;
            _mode = mode;
            _horizon = horizon;
            _now = context.Input.Now;
            _members = context.Input.Members.OrderBy(m => m.Id).Select(m => new Member(m) { Free = _now }).ToList();
            var pinned = context.Input.Members.Where(m => m.Head is { } h && h >= 0 && h < context.Plan.Stations.Count).Select(m => m.Head!.Value).ToHashSet();
            _universe = context.Plan.Stations.Where(s => !pinned.Contains(s.Id) && s.Cells.Any(state.Remaining.Contains)).ToList();
            _top = _universe.ToDictionary(s => s.Id, s => s.Cells.Max(c => c.Y));
            foreach (var (m, input) in _members.Zip(context.Input.Members.OrderBy(m => m.Id)))
            {
                Pin(m, input);
                if (lists != null)
                    m.Fixed = new Queue<int>(lists.GetValueOrDefault(m.Id) ?? []);
            }
        }

        // a pinned head: worked until its FreeAt, its claim from now, its stand's zone held
        private void Pin(Member m, MineRouteMember input)
        {
            if (input.Head is not { } h || h < 0 || h >= _c.Plan.Stations.Count)
                return;
            var station = _c.Plan.Stations[h];
            var cells = _c.Usable(MineDispatch.Cells(_state, station, input.HeadAhead));
            if (cells.Count == 0)
                cells = station.Cells.Where(_state.Remaining.Contains).ToList();
            if (cells.Count == 0)
                return;
            var fitted = station with { Cells = cells };
            var d = MineCosts.From(_state.Walk, input.At, [station.Stand]);
            _c.Searches++;
            double walk = d.Or(station.Stand, input.At);
            var leg = d.Path(station.Stand);
            var stop = new SimStop
            {
                Kind = MineStopKind.Station, Station = h, Fitted = fitted, At = station.Stand, Ahead = input.HeadAhead, Pinned = true,
                Leg = leg.Count > 0 ? leg : [input.At, station.Stand], LegSeconds = walk,
                WorkSeconds = MineCosts.WorkSeconds(_state.Site, fitted, _c.Plan.Tunnel),
                Depart = _now, Arrive = Math.Min(_now + walk, Math.Max(_now, input.FreeAt)), Seq = ++_seq, Member = m.Id,
                Claim = MineDispatch.Claim(fitted, _c.Plan.Tunnel).ToHashSet(),
            };
            stop.Start = Math.Max(_now, stop.Arrive);
            stop.Finish = Math.Max(stop.Start, input.FreeAt);
            if (_c.Index.Of(station.Stand) is { } z)
                _slots.Hold(m.Id, stop.Seq, z.Id, _now, double.PositiveInfinity);
            m.Stops.Add(stop);
            m.Stations++;
            m.At = station.Stand;
            m.Free = stop.Finish;
            _pending.Add(stop);
            _busy.Add(h);
        }

        /// <summary>Runs it; false when a validated member could not keep to its list.</summary>
        public bool Run(CancellationToken cancel)
        {
            int idle = 0;
            for (int events = 0; events < MaxEvents; events++)
            {
                var m = _members.Where(x => !x.Retired).MinBy(x => (x.Free, x.Id));
                if (m == null)
                    break;
                cancel.ThrowIfCancellationRequested();
                double t = m.Free;
                if (ApplyFinished(t))
                    idle = 0;
                if (_mode == Mode.Rollout && (m.Stations >= _horizon || !Whole && t > _now + 2 * MineRoutes.HorizonSeconds))
                {
                    m.Retired = true;
                    continue;
                }
                if (_mode != Mode.Rollout && m.Fixed!.Count == 0)
                {
                    m.Retired = true;
                    continue;
                }
                _c.Decisions++;
                bool took = _mode == Mode.Rollout ? Decide(m, t) : Follow(m, t, out bool failed) || Failed(failed);
                if (_failed)
                    return false;
                if (took)
                {
                    idle = 0;
                    continue;
                }
                Wait(m, t);
                if (++idle > 3 * _members.Count + 3)
                    break;
            }
            if (_mode != Mode.Rollout)
                foreach (var m in _members.Where(m => m.Fixed!.Count > 0))
                {
                    if (_mode == Mode.Validate)
                        return false;
                    DroppedStops += m.Fixed!.Count;
                    m.Fixed.Clear();
                }
            return true;
        }

        private bool _failed;

        private bool Failed(bool failed)
        {
            _failed = failed;
            return false;
        }

        // the digs of the stops done by t, in time order
        private bool ApplyFinished(double t)
        {
            bool any = false;
            foreach (var s in _pending.Where(s => !s.Applied && s.Finish <= t).OrderBy(s => s.Finish).ThenBy(s => s.Member).ThenBy(s => s.Seq).ToList())
            {
                _state.Apply(s.Fitted!.Cells.Where(_state.Remaining.Contains).ToList());
                s.Applied = true;
                _busy.Remove(s.Station);
                any = true;
                _progress++;
            }
            _pending.RemoveAll(s => s.Applied);
            return any;
        }

        /// <summary>Every dig still pending applied: the world as the rollout leaves it.</summary>
        public void ApplyAll() => ApplyFinished(double.PositiveInfinity);

        // ---- the rollout's decision ----

        private bool Decide(Member m, double t)
        {
            var eligible = new List<(MineStation Station, IReadOnlyList<MarkPos> Cells)>();
            var bands = _state.Bands;
            // stations wholly under the deepest layer ahead work reaches are none of this decision's (open parts)
            int floor = bands is { CoveredBands: 0, Layer: not null } ? bands.OpenFloor - MineAhead.AheadLayers : int.MinValue;
            if (_universeVersion != _state.Version)
            {
                _universe.RemoveAll(s => !s.Cells.Any(_state.Remaining.Contains));
                _universeVersion = _state.Version;
            }
            // the pool: the nearest stations it may dig now, and the first of them in plan order (looked at in those
            // orders until found, so a large area's stations are not all asked each decision)
            var open = _universe.Where(s => !_busy.Contains(s.Id) && _top[s.Id] >= floor).ToList();
            void Take(IEnumerable<MineStation> order, int count)
            {
                int found = 0;
                foreach (var s in order)
                {
                    if (found >= count)
                        break;
                    if (bands != null && !s.Cells.Any(bands.Eligible))
                        continue;
                    var cells = _c.Usable(MineDispatch.Cells(_state, s));
                    if (cells.Count == 0)
                        continue;
                    found++;
                    if (!eligible.Any(e => e.Station.Id == s.Id))
                        eligible.Add((s, cells));
                    _c.FirstEligible.TryAdd(s.Id, t);
                }
            }
            Take(open.OrderBy(s => MineCosts.Estimate(m.At, s.Stand)).ThenBy(s => s.Id), PoolSize);
            Take(open, PlanOrderPool);
            var pool = eligible.OrderBy(e => e.Station.Id).ToList();
            var candidates = Time(m, t, pool, ahead: false);
            if (candidates.Count == 0 || candidates.Min(c => c.Start) > t + AheadWaitSeconds)
            {
                var ahead = new List<(MineStation Station, IReadOnlyList<MarkPos> Cells)>();
                foreach (var s in open)
                {
                    if (bands != null && (s.Cells.Any(bands.Eligible) || !s.Cells.Any(c => MineAhead.Eligible(_state, c))))
                        continue;
                    var cells = _c.Usable(MineDispatch.Cells(_state, s, true));
                    if (cells.Count == 0)
                        continue;
                    ahead.Add((s, cells));
                }
                if (ahead.Count > 0)
                {
                    var aheadPool = Pool(m, ahead, AheadPool, 0);
                    foreach (var (s, _) in aheadPool)
                        _c.FirstEligible.TryAdd(s.Id, t);
                    candidates.AddRange(Time(m, t, aheadPool, ahead: true));
                }
            }
            int tries = 0;
            bool Sooner(Candidate c) => Policy == RollPolicy.Keep && _members.Any(o => o != m && !o.Retired
                && Math.Max(o.Free, t) + MineCosts.Estimate(o.At, c.Station.Stand) + MineCosts.ArriveSeconds + c.Work < c.Finish - 1e-9);
            // a station a member at work would finish sooner is left to it (it is not handed over)
            bool Busy(Member o) => o.Stops.Any(s => s.Kind == MineStopKind.Station && s.Depart <= t + 1e-9 && s.Finish > t + 1e-9);
            bool Left(Candidate c) => Variant.Leave && _members.Any(o => o != m && !o.Retired
                && Math.Max(o.Free, t) + MineCosts.Estimate(o.At, c.Station.Stand) + MineCosts.ArriveSeconds + c.Work < c.Finish - (Busy(o) ? 1e-9 : LeaveMargin));
            foreach (var c in candidates.Where(c => !Left(c)).OrderBy(Sooner).ThenBy(c => c.Finish + (c.Ahead ? AheadPenaltySeconds : 0) - Critical * Beneath(c.Station)).ThenBy(c => c.Station.Id))
            {
                if (tries++ >= GuardTries)
                    break;
                if (Guard(m, c))
                {
                    Assign(m, c);
                    return true;
                }
            }
            return false;
        }

        // the nearest of a list, and the first of it in plan order
        private List<(MineStation Station, IReadOnlyList<MarkPos> Cells)> Pool(Member m,
            List<(MineStation Station, IReadOnlyList<MarkPos> Cells)> list, int nearest, int first)
        {
            var pool = list.OrderBy(e => MineCosts.Estimate(m.At, e.Station.Stand)).ThenBy(e => e.Station.Id).Take(nearest)
                .Concat(list.OrderBy(e => e.Station.Id).Take(first))
                .DistinctBy(e => e.Station.Id).OrderBy(e => e.Station.Id).ToList();
            return pool;
        }

        private sealed record Candidate(MineStation Station, bool Ahead, IReadOnlyList<PathCell> Leg, double LegSeconds,
            IReadOnlyList<(int Zone, double Enter, double Leave)> Runs, double Depart, double Arrive, double Start, double Finish, double Work);

        // each station of the pool fit round the others' claims and timed from m's place at t
        private List<Candidate> Time(Member m, double t, List<(MineStation Station, IReadOnlyList<MarkPos> Cells)> pool, bool ahead)
        {
            var result = new List<Candidate>();
            if (pool.Count == 0)
                return result;
            var claimed = new HashSet<(int X, int Z)>();
            var places = new List<PathCell>();
            foreach (var o in _members)
            {
                if (o == m)
                    continue;
                foreach (var s in o.Stops)
                    if (s.Kind == MineStopKind.Station && s.Finish > t)
                        claimed.UnionWith(s.Claim);
                places.Add(o.At);
                places.Add(o.Place(t));
            }
            var fitted = new List<MineStation>();
            foreach (var (s, cells) in pool)
            {
                if (MineDispatch.Fit(s with { Cells = cells }, _c.Plan.Tunnel, col => !claimed.Contains(col)) is not { } fit)
                    continue;
                if (places.Any(p => Taken(p, fit.Stand)))
                    continue;
                fitted.Add(fit);
            }
            if (fitted.Count == 0)
                return result;
            var d = MineCosts.From(_state.Walk, m.At, fitted.Select(f => f.Stand).Distinct().ToList(), beyond: SearchBeyond);
            _c.Searches++;
            foreach (var f in fitted)
            {
                if (d.Seconds(f.Stand) is not { } walk)
                    continue;
                var leg = d.Path(f.Stand);
                if (leg.Count == 0)
                    continue;
                double release = _c.FirstEligible.GetValueOrDefault(f.Id, t);
                if (Timed(m, t, f, ahead, leg, walk, release) is { } c)
                    result.Add(c);
                else if (Round(m, t, f) is { } round && round.Seconds(f.Stand) is { } longer && longer <= walk + DetourSeconds
                    && Timed(m, t, f, ahead, round.Path(f.Stand), longer, release) is { } around)
                    // its way crosses another's claim: the way round it, if not much longer
                    result.Add(around);
            }
            return result;
        }

        private Candidate? Timed(Member m, double t, MineStation f, bool ahead, IReadOnlyList<PathCell> leg, double walk, double release)
        {
            var runs = _c.Index.Timed(leg, holdEnd: true);
            var columns = LegColumns(leg);
            var claim = MineDispatch.Claim(f, _c.Plan.Tunnel).ToHashSet();
            double work = Shaken(f.Id, MineCosts.WorkSeconds(_state.Site, f, _c.Plan.Tunnel));
            double depart = t;
            for (int round = 0; ; round++)
            {
                // its way crosses no other's claim while it walks
                double slot = _slots.Earliest(m.Id, depart, runs);
                if (double.IsPositiveInfinity(slot))
                    return null;
                double finish = Math.Max(slot + walk + MineCosts.ArriveSeconds, release) + work;
                double next = Math.Max(Crossing(m, columns, slot, slot + walk), Crossed(m, claim, slot, finish));
                // a way across another's claim waits for it at most PassAfter, then goes (as the game goes through)
                if (next <= slot || slot >= t + PassAfter - 1e-9)
                {
                    depart = slot;
                    break;
                }
                next = Math.Min(next, t + PassAfter);
                if (next > t + CrossDelayMax || round == 7)
                    return null;
                depart = next;
            }
            double arrive = depart + walk;
            double start = Math.Max(arrive + MineCosts.ArriveSeconds, release);
            return new Candidate(f, ahead, leg, walk, runs, depart, arrive, start, start + work, work);
        }

        // a way to a stand round the claims others hold from now to CrossDelayMax on
        private MineDistances? Round(Member m, double t, MineStation f)
        {
            var claimed = new HashSet<(int X, int Z)>();
            foreach (var o in _members)
                if (o != m)
                    foreach (var s in o.Stops)
                        if (s.Kind == MineStopKind.Station && s.Finish > t && s.Depart < t + CrossDelayMax)
                            claimed.UnionWith(s.Claim);
            if (claimed.Count == 0)
                return null;
            _c.Searches++;
            return MineCosts.From(_state.Walk, m.At, [f.Stand], avoid: n => BoxIn(n, claimed));
        }

        private static bool BoxIn(PathCell n, HashSet<(int X, int Z)> columns) =>
            columns.Contains((n.X - 1, n.Z - 1)) || columns.Contains((n.X, n.Z - 1)) || columns.Contains((n.X - 1, n.Z)) || columns.Contains((n.X, n.Z));

        // the box columns of a way past its first two nodes (it may set off from beside another's)
        private static HashSet<(int X, int Z)> LegColumns(IReadOnlyList<PathCell> leg)
        {
            var columns = new HashSet<(int X, int Z)>();
            foreach (var n in leg.Skip(2))
                for (int x = n.X - 1; x <= n.X; x++)
                for (int z = n.Z - 1; z <= n.Z; z++)
                    columns.Add((x, z));
            return columns;
        }

        // the latest arrival of another's way that crosses the claim while it is held (from - when none)
        private double Crossed(Member m, HashSet<(int X, int Z)> claim, double from, double to)
        {
            double end = double.NegativeInfinity;
            foreach (var o in _members)
            {
                if (o == m)
                    continue;
                foreach (var s in o.Stops)
                    if (s.Kind == MineStopKind.Station && s.Arrive > from && s.Depart < to && s.Leg.Count > 2 && (s.LegColumns ??= LegColumns(s.Leg)).Overlaps(claim))
                        end = Math.Max(end, s.Arrive);
            }
            return end;
        }

        // the latest end of another's claim the leg's columns meet while it walks (from - when none)
        private double Crossing(Member m, HashSet<(int X, int Z)> columns, double from, double to)
        {
            double end = double.NegativeInfinity;
            foreach (var o in _members)
            {
                if (o == m)
                    continue;
                foreach (var s in o.Stops)
                    if (s.Kind == MineStopKind.Station && s.Depart < to && s.Finish > from && s.Claim.Overlaps(columns))
                        end = Math.Max(end, s.Finish);
            }
            return end;
        }

        private static bool Taken(PathCell at, PathCell stand) =>
            Math.Abs(at.X - stand.X) < 2 * EidolonMining.HalfWidth && Math.Abs(at.Z - stand.Z) < 2 * EidolonMining.HalfWidth && Math.Abs(at.Y - stand.Y) < 4;

        private bool Guard(Member m, Candidate c)
        {
            _c.GuardChecks++;
            var others = new List<MineWorkerAt>();
            var keep = new List<PathCell>();
            foreach (var o in _members)
            {
                if (o == m)
                    continue;
                var at = o.Place(c.Depart);
                var holds = new List<PathCell>();
                foreach (var s in o.Stops)
                {
                    if (s.Kind != MineStopKind.Station || s.Finish <= c.Depart)
                        continue;
                    holds.Add(s.At);
                    if (s.Finish > c.Start)
                        keep.Add(s.At);
                }
                if (o.At != at)
                    holds.Add(o.At);
                others.Add(new MineWorkerAt(o.Id, at.X, at.Y, at.Z, holds));
            }
            others.AddRange(_c.Input.Parked);
            return MineDispatch.Executable(_c.Plan, _state, c.Station, others, out _, _c.Input.Usable, c.Ahead, keep);
        }

        private void Assign(Member m, Candidate c, int key = -1)
        {
            var stop = new SimStop
            {
                Kind = MineStopKind.Station, Station = c.Station.Id, Fitted = c.Station with { Cells = MineDispatch.Cells(_state, c.Station, c.Ahead) },
                At = c.Station.Stand, Ahead = c.Ahead, Leg = c.Leg, LegSeconds = c.LegSeconds, WorkSeconds = c.Work,
                Depart = c.Depart, Arrive = c.Arrive, Start = c.Start, Finish = c.Finish, Seq = ++_seq, Member = m.Id,
            };
            stop.Claim = MineDispatch.Claim(stop.Fitted, _c.Plan.Tunnel).ToHashSet();
            stop.Key = key >= 0 ? key : stop.Seq;
            _slots.Reserve(m.Id, stop.Seq, c.Depart, c.Runs);
            m.Stops.Add(stop);
            m.Stations++;
            m.At = stop.At;
            m.Free = stop.Finish;
            _pending.Add(stop);
            _busy.Add(c.Station.Id);
            if (_mode == Mode.Rollout)
            {
                _c.RollOrder[stop.Seq] = _c.RollOrder.Count;
                _c.Keys[stop.Seq] = (stop.Station, stop.Fitted.Cells, stop.Ahead);
            }
            _progress++;
        }

        // ---- a fixed list (validation, evaluation) ----

        private bool Follow(Member m, double t, out bool failed)
        {
            failed = false;
            while (m.Fixed!.Count > 0)
            {
                var key = _c.Keys[m.Fixed.Peek()];
                var left = _c.Usable(key.Cells).Where(_state.Remaining.Contains).ToList();
                if (left.Count == 0)
                {
                    // dug by now: nothing to do there
                    m.Fixed.Dequeue();
                    m.FixedSince = double.NaN;
                    continue;
                }
                var s = _c.Plan.Stations[key.Station] with { Cells = left };
                if (_busy.Contains(key.Station))
                    return false;
                bool ahead = false;
                var cells = _c.Usable(MineDispatch.Cells(_state, s));
                if (cells.Count == 0)
                {
                    cells = _c.Usable(MineDispatch.Cells(_state, s, true));
                    ahead = true;
                }
                Candidate? candidate = null;
                if (cells.Count > 0)
                    candidate = Time(m, t, [(s, cells)], ahead).FirstOrDefault();
                if (candidate != null && Guard(m, candidate))
                {
                    Assign(m, candidate, m.Fixed.Peek());
                    m.Fixed.Dequeue();
                    m.FixedSince = double.NaN;
                    return true;
                }
                if (double.IsNaN(m.FixedSince))
                    m.FixedSince = t;
                if (t - m.FixedSince <= FixedPatience)
                    return false;
                if (_mode == Mode.Validate)
                {
                    failed = true;
                    return false;
                }
                DroppedStops++;
                m.Fixed.Dequeue();
                m.FixedSince = double.NaN;
            }
            return false;
        }

        // ---- waiting ----

        private void Wait(Member m, double t)
        {
            double until = double.PositiveInfinity;
            foreach (var o in _members)
                if (o != m && !o.Retired && o.Free > t)
                    until = Math.Min(until, o.Free);
            foreach (var s in _pending)
                if (s.Finish > t)
                    until = Math.Min(until, s.Finish);
            if (double.IsPositiveInfinity(until))
                until = t + WaitSeconds;
            var (place, leg) = WaitPlace(m, t);
            double depart = t, walk = 0;
            IReadOnlyList<(int Zone, double Enter, double Leave)> runs = [];
            if (leg.Count > 1)
            {
                walk = 0;
                for (int k = 1; k < leg.Count; k++)
                    walk += MineCosts.StepCost(leg[k - 1], leg[k]) * MineCosts.WalkSecondsPerBlock;
                runs = _c.Index.Timed(leg, holdEnd: true);
                depart = _slots.Earliest(m.Id, t, runs);
                if (double.IsPositiveInfinity(depart) || depart > t + WaitSeconds)
                {
                    (place, leg, depart, walk, runs) = (m.At, [m.At], t, 0, []);
                }
            }
            double arrive = depart + walk;
            until = Math.Max(until, arrive + 0.5);
            var last = m.Stops.Count > 0 ? m.Stops[^1] : null;
            if (last is { Kind: MineStopKind.Wait } && last.At == place && leg.Count <= 1)
                last.Finish = until;
            else
            {
                var stop = new SimStop
                {
                    Kind = MineStopKind.Wait, At = place, Leg = leg.Count > 0 ? leg : [m.At], LegSeconds = walk,
                    Depart = depart, Arrive = arrive, Start = arrive, Finish = until, Seq = ++_seq, Member = m.Id,
                };
                if (runs.Count > 0)
                    _slots.Reserve(m.Id, stop.Seq, depart, runs);
                m.Stops.Add(stop);
            }
            m.At = place;
            m.Free = until;
        }

        // where it waits: where it is, if out of every narrow way and every other's claim; else the nearest such
        // node a few joins off, with the way there; else where it is
        private (PathCell Place, IReadOnlyList<PathCell> Leg) WaitPlace(Member m, double t)
        {
            var claimed = new HashSet<(int X, int Z)>();
            foreach (var o in _members)
                if (o != m)
                    foreach (var s in o.Stops)
                        if (s.Kind == MineStopKind.Station && s.Finish > t)
                            claimed.UnionWith(s.Claim);
            var area = _state.Site.Area;
            bool Clear(PathCell n)
            {
                if (_c.Index.Of(n) != null)
                    return false;
                // nor over cells still to dig: it would stand in the way of their stations
                if (Variant.Clear)
                    for (int x = n.X - 1; x <= n.X; x++)
                    for (int z = n.Z - 1; z <= n.Z; z++)
                        if (x >= area.Min.X && x <= area.Max.X && z >= area.Min.Z && z <= area.Max.Z)
                            for (int y = area.Min.Y; y <= Math.Min(area.Max.Y, n.Y - 1); y++)
                                if (_state.Remaining.Contains(new MarkPos(x, y, z)) && !_c.Plan.Stair.Contains(new MarkPos(x, y, z)))
                                    return false;
                for (int x = n.X - 1; x <= n.X; x++)
                for (int z = n.Z - 1; z <= n.Z; z++)
                    if (claimed.Contains((x, z)))
                        return false;
                return true;
            }
            if (Clear(m.At) || !_state.Walk.Out(m.At))
                return (m.At, [m.At]);
            var parent = new Dictionary<PathCell, PathCell> { [m.At] = m.At };
            var frontier = new List<PathCell> { m.At };
            for (int depth = 0; depth < WaitJoins && frontier.Count > 0; depth++)
            {
                var next = new List<PathCell>();
                foreach (var u in frontier)
                foreach (var v in _state.Walk.Joins(u))
                {
                    if (!_state.Walk.Out(v) || parent.ContainsKey(v))
                        continue;
                    parent[v] = u;
                    next.Add(v);
                }
                var found = next.Where(Clear).OrderBy(n => n, MineChokes.NodeOrder.Instance).FirstOrDefault();
                if (next.Any(Clear))
                {
                    var path = new List<PathCell> { found };
                    while (path[^1] != m.At)
                        path.Add(parent[path[^1]]);
                    path.Reverse();
                    return (found, path);
                }
                frontier = next;
            }
            // none near: back to where it started, out of the way (as today's dispatch walks to the rim)
            if (Variant.Clear && m.Rest != m.At && MineCosts.From(_state.Walk, m.At, [m.Rest]).Path(m.Rest) is { Count: > 1 } home && home[0] == m.At)
                return (m.Rest, home);
            return (m.At, [m.At]);
        }

        // ---- results ----

        /// <summary>Each member's stations after its pinned head, in the order taken.</summary>
        public Dictionary<long, List<int>> Lists() =>
            _members.ToDictionary(m => m.Id, m => m.Stops.Where(s => s.Kind == MineStopKind.Station && !s.Pinned).Select(s => s.Key).ToList());

        /// <summary>When each member is done with its last station (its pinned head's finish, or the start, if none).</summary>
        public Dictionary<long, double> Finishes() =>
            _members.ToDictionary(m => m.Id, m => m.Stops.Where(s => s.Kind == MineStopKind.Station).Select(s => s.Finish).DefaultIfEmpty(_now).Max());

        public IReadOnlyList<SimStop> StopsOf(long member) => _members.First(m => m.Id == member).Stops;

        public (double J, double Walk, double Wait) Score()
        {
            var finishes = new List<double>();
            double walk = 0, wait = 0;
            var routed = new HashSet<int>();
            int legs = 0;
            foreach (var m in _members)
            {
                double f = _now;
                foreach (var s in m.Stops)
                {
                    if (s.Kind == MineStopKind.Station)
                    {
                        f = s.Finish;
                        routed.Add(s.Station);
                        walk += s.LegSeconds;
                        legs++;
                    }
                    else
                        wait += s.Finish - s.Arrive;
                }
                finishes.Add(f);
            }
            double legMean = legs > 0 ? walk / legs : 3;
            double rest = 0;
            foreach (var s in _c.Plan.Stations)
            {
                if (routed.Contains(s.Id))
                    continue;
                var cells = s.Cells.Where(_c.Pristine.Remaining.Contains).ToList();
                if (cells.Count > 0)
                    rest += MineCosts.WorkSeconds(_c.Pristine.Site, cells) + legMean;
            }
            int p = Math.Max(1, _members.Count);
            double j = finishes.Count == 0 ? 0 : Math.Max(finishes.Max(), (finishes.Sum() + rest) / p) - _now;
            return (j, walk, wait);
        }

        public MineRoutes Build(MineRouteStats stats)
        {
            var routes = new Dictionary<long, MineRoute>();
            var slots = new MineSlots();
            foreach (var m in _members)
            {
                // a wait with no station after it waits for nothing the routes know of: left out
                int last = m.Stops.FindLastIndex(s => s.Kind == MineStopKind.Station);
                var stops = m.Stops.Take(last + 1).Select(s => new MineStop(s.Kind, s.Station, s.At, s.Ahead, s.Leg, s.Depart, s.Arrive, s.Start, s.Finish)
                {
                    Seq = s.Seq, Cells = s.Fitted?.Cells ?? [], LegSeconds = s.LegSeconds, WorkSeconds = s.WorkSeconds, Pinned = s.Pinned,
                }).ToList();
                routes[m.Id] = new MineRoute(m.Id, stops) { FreeAt = stops.Count > 0 && stops[0].Pinned ? stops[0].Finish : _now };
                int lastSeq = stops.Count > 0 ? stops[^1].Seq : -1;
                foreach (var slot in _slots.All.Where(x => x.Member == m.Id && x.Stop <= lastSeq))
                    slots.Hold(slot.Member, slot.Stop, slot.Zone, slot.From, slot.To);
            }
            return new MineRoutes(_c.Plan, _c.Input.PlanVersion, _now, routes, _c.Zones, _c.Index, slots, Score(), stats, _seq);
        }
    }

    // ---- today's dispatch, simulated ----

    /// <summary>Today's dispatch (README "Eidolon", working together, *Dispatch*) worked to the job's end on the
    /// same model as the routes' simulations, for the choice between them: each member, free, takes
    /// <see cref="MineDispatch.Next"/> of the plan with the others' stations as their queues and their claims,
    /// walks there on the walk graph (waiting up to 20 s while its way crosses another's claim, then going, as
    /// the game does), and digs it whole at its finish; with nothing for it, it walks back to where it started and
    /// waits there.</summary>
    private sealed class TodaySim
    {
        private const double ThroughSeconds = 20;

        private sealed class Member(MineRouteMember input)
        {
            public readonly long Id = input.Id;
            public PathCell At = input.At;
            public readonly PathCell Rest = input.At;
            public double Free;
            public MineStation? Station;
            public HashSet<(int X, int Z)> Claim = new();
            public double? Through;
            public bool Done;
        }

        private readonly Context _c;
        private readonly MineState _state;
        private readonly int _shake;
        private readonly double _now;
        private readonly List<Member> _members;
        private double _end;

        public TodaySim(Context context, MineState state, int shake)
        {
            _c = context;
            _state = state;
            _shake = shake;
            _now = context.Input.Now;
            _members = context.Input.Members.OrderBy(m => m.Id).Select(m => new Member(m) { Free = _now }).ToList();
            _end = _now;
            // a head held now: worked until its member is free
            foreach (var (m, input) in _members.Zip(context.Input.Members.OrderBy(m => m.Id)))
                if (input.Head is { } h && h >= 0 && h < _c.Plan.Stations.Count)
                {
                    var station = _c.Plan.Stations[h];
                    var cells = station.Cells.Where(state.Remaining.Contains).ToList();
                    if (cells.Count == 0)
                        continue;
                    m.Station = station with { Cells = cells };
                    m.Claim = MineDispatch.Claim(m.Station, _c.Plan.Tunnel).ToHashSet();
                    m.At = station.Stand;
                    m.Free = Math.Max(_now, input.FreeAt);
                }
        }

        public double Makespan()
        {
            var left = _state.Remaining.Where(c => !_c.Plan.WayOut.Contains(c)).ToList();
            return _end - _now + (left.Count == 0 ? 0 : MineCosts.WorkSeconds(_state.Site, left) + Stalled);
        }

        public void Run(CancellationToken cancel)
        {
            int idle = 0;
            for (int events = 0; events < MaxEvents; events++)
            {
                var m = _members.Where(x => !x.Done).MinBy(x => (x.Free, x.Id));
                if (m == null)
                    return;
                cancel.ThrowIfCancellationRequested();
                double t = m.Free;
                if (m.Station is { } done)
                {
                    _state.Apply(done.Cells.Where(_state.Remaining.Contains).ToList());
                    m.Station = null;
                    m.Claim.Clear();
                    _end = Math.Max(_end, t);
                    idle = 0;
                }
                var me = new MineWorker(m.Id, m.At.X, m.At.Y, m.At.Z, [], 0);
                var others = _members.Where(o => o != m).Select(o =>
                    new MineWorker(o.Id, o.At.X, o.At.Y, o.At.Z, o.Station is { } s ? [s] : [], Math.Max(0, o.Free - t))).ToList();
                bool Free((int X, int Z) col) => !_members.Any(o => o != m && o.Claim.Contains(col));
                _c.Decisions++;
                var next = MineDispatch.Next(_c.Plan, _state, me, others, s => MineDispatch.Claim(s, _c.Plan.Tunnel).All(Free),
                    _c.Input.Usable, s => MineDispatch.Fit(s, _c.Plan.Tunnel, Free));
                IReadOnlyList<PathCell> path = [];
                double walk = 0;
                if (next != null)
                {
                    var d = MineCosts.From(_state.Walk, m.At, [next.Stand]);
                    _c.Searches++;
                    path = d.Path(next.Stand);
                    walk = d.Seconds(next.Stand) ?? 0;
                    if (path.Count == 0)
                        next = null;
                }
                if (next == null)
                {
                    bool busy = _members.Any(o => o.Station != null);
                    if (!busy && (!_state.Remaining.Any(c => _state.Bands?.Eligible(c) ?? true) || ++idle > 3 * _members.Count + 3))
                    {
                        m.Done = true;
                        continue;
                    }
                    // nothing for it: it walks out of the others' way, to where it started, and waits there
                    if (m.At != m.Rest && MineCosts.From(_state.Walk, m.At, [m.Rest]).Seconds(m.Rest) is { } back)
                    {
                        m.At = m.Rest;
                        m.Free = t + back;
                        continue;
                    }
                    double until = _members.Where(o => o != m && !o.Done && o.Free > t).Select(o => o.Free).DefaultIfEmpty(t + 1).Min();
                    m.Free = Math.Max(until, t + 0.5);
                    continue;
                }
                // its way crosses another's claim: it waits for room, at most 20 s, then goes through
                var columns = new HashSet<(int X, int Z)>();
                foreach (var n in path.Skip(2))
                    for (int x = n.X - 1; x <= n.X; x++)
                    for (int z = n.Z - 1; z <= n.Z; z++)
                        columns.Add((x, z));
                if (_members.Any(o => o != m && o.Claim.Overlaps(columns)) && (m.Through ??= t) > t - ThroughSeconds)
                {
                    m.Free = t + 0.5;
                    continue;
                }
                m.Through = null;
                var cells = MineDispatch.Cells(_state, next);
                var station = next with { Cells = cells };
                m.Station = station;
                m.Claim = MineDispatch.Claim(station, _c.Plan.Tunnel).ToHashSet();
                m.At = next.Stand;
                m.Free = t + walk + MineCosts.ArriveSeconds + ShakenWork(_shake, next.Id, MineCosts.WorkSeconds(_state.Site, station, _c.Plan.Tunnel));
                idle = 0;
            }
        }
    }

    // ---- the improvements: exact reorder, relocations ----

    private sealed class Improver
    {
        private readonly Context _c;
        private readonly MineState _final;
        private readonly Dictionary<int, Info> _info = new();
        private readonly Dictionary<long, (PathCell At, double Free)> _start = new();
        private readonly Dictionary<PathCell, MineDistances> _from = new();
        private readonly List<PathCell> _stands;
        private readonly double _rest;
        private readonly List<long> _members;

        private sealed record Info(int Id, PathCell Stand, IReadOnlyList<MarkPos> Cells, double Work, double Release, int Order, bool Ahead, int Band);

        public Improver(Context context, Sim roll, MineState final, Dictionary<long, List<int>> lists)
        {
            _c = context;
            _final = final;
            _members = lists.Keys.Order().ToList();
            foreach (long id in _members)
            {
                var stops = roll.StopsOf(id);
                var head = stops.FirstOrDefault(s => s.Pinned);
                var input = context.Input.Members.First(m => m.Id == id);
                _start[id] = head != null ? (head.At, head.Finish) : (input.At, context.Input.Now);
                foreach (var s in stops.Where(s => s.Kind == MineStopKind.Station && !s.Pinned))
                    _info[s.Seq] = new Info(s.Seq, s.At, s.Fitted!.Cells, s.WorkSeconds,
                        Math.Max(context.Input.Now, context.FirstEligible.GetValueOrDefault(s.Station, context.Input.Now)),
                        context.RollOrder.GetValueOrDefault(s.Seq), s.Ahead, context.Plan.Stations[s.Station].Band);
            }
            _stands = _info.Values.Select(i => i.Stand).Concat(_start.Values.Select(s => s.At)).Distinct().ToList();
            _lists = lists;
            _rest = RestOf(roll);
        }

        private readonly Dictionary<long, List<int>> _lists;

        // the work no route holds, as the objective counts it (its legs at the rollout's mean)
        private double RestOf(Sim roll)
        {
            double walk = 0;
            int legs = 0;
            var routed = new HashSet<int>();
            foreach (long id in _members)
                foreach (var s in roll.StopsOf(id).Where(s => s.Kind == MineStopKind.Station))
                {
                    routed.Add(s.Station);
                    walk += s.LegSeconds;
                    legs++;
                }
            double legMean = legs > 0 ? walk / legs : 3, rest = 0;
            foreach (var s in _c.Plan.Stations)
            {
                if (routed.Contains(s.Id))
                    continue;
                var cells = s.Cells.Where(_c.Pristine.Remaining.Contains).ToList();
                if (cells.Count > 0)
                    rest += MineCosts.WorkSeconds(_c.Pristine.Site, cells) + legMean;
            }
            return rest;
        }

        private double Travel(PathCell from, PathCell to)
        {
            if (from == to)
                return 0;
            if (!_from.TryGetValue(from, out var d))
            {
                _from[from] = d = MineCosts.From(_final.Walk, from, _stands, beyond: SearchBeyond);
                _c.Searches++;
            }
            return d.Or(to, from);
        }

        private (int[] Order, double Finish)? Solve(long member, IReadOnlyList<int> stations, bool exact)
        {
            int n = stations.Count;
            var (at, free) = _start[member];
            var travel = new double[n + 1, n + 1];
            var work = new double[n];
            var release = new double[n];
            var before = new int[n];
            // settling on the stand counts with the walk: a stop starts work at max(arrive + settle, release)
            for (int i = 0; i < n; i++)
            {
                var a = _info[stations[i]];
                work[i] = a.Work;
                release[i] = a.Release;
                travel[n, i] = Travel(at, a.Stand) + MineCosts.ArriveSeconds;
                for (int j = 0; j < n; j++)
                {
                    if (i == j)
                        continue;
                    var b = _info[stations[j]];
                    travel[i, j] = Travel(a.Stand, b.Stand) + MineCosts.ArriveSeconds;
                    if (a.Order < b.Order && Must(a.Band, a.Cells, a.Stand, a.Ahead, b.Band, b.Cells, b.Stand, b.Ahead))
                        before[j] |= 1 << i;
                }
            }
            return exact ? MineOrdering.Order(free, travel, work, release, before) : MineOrdering.Quick(free, travel, work, release, before);
        }

        /// <summary>Each member's stations in their best order.</summary>
        public Dictionary<long, List<int>> Reorder() => Exact(_lists);

        /// <summary>Lists put in their exact best order (those that cannot be ordered kept as they are).</summary>
        public Dictionary<long, List<int>> Exact(Dictionary<long, List<int>> lists)
        {
            var result = new Dictionary<long, List<int>>();
            foreach (long id in _members)
            {
                var list = lists[id];
                result[id] = list.Count is > 1 and <= MineOrdering.MaxStations && Solve(id, list, true) is { } o
                    ? o.Order.Select(k => list[k]).ToList()
                    : list.ToList();
            }
            return result;
        }

        private double J(IReadOnlyDictionary<long, double> f)
        {
            int p = Math.Max(1, f.Count);
            return Math.Max(f.Values.Max(), (f.Values.Sum() + _rest) / p) - _c.Input.Now;
        }

        /// <summary>A station moved off the member that finishes last (by <paramref name="finishes"/>, as the
        /// simulation found them, waits and all) to the member and place where the job's estimated finish comes in
        /// most, the change to each member's finish taken from the exact order of its stations without and with
        /// it (<see cref="MineOrdering.Quick"/>); null when no move brings it in by more than half a second. The
        /// <see cref="MovesWeighed"/> cheapest by an insertion estimate are weighed.</summary>
        public Dictionary<long, List<int>>? Move(Dictionary<long, List<int>> lists, Dictionary<long, double> finishes, CancellationToken cancel)
        {
            if (_members.Count < 2)
                return null;
            cancel.ThrowIfCancellationRequested();
            var f = _members.ToDictionary(id => id, id => finishes.GetValueOrDefault(id, _c.Input.Now));
            double j = J(f);
            long b = _members.OrderByDescending(id => f[id]).ThenBy(id => id).First();
            var from = lists[b].Where(_info.ContainsKey).ToList();
            if (from.Count == 0)
                return null;
            var model = _members.ToDictionary(id => id, id => Finish(id, lists[id]));
            var moves = new List<(double Estimate, int Station, long To)>();
            foreach (int s in from)
            foreach (long o in _members)
            {
                if (o == b || lists[o].Count + (_start[o].Free > _c.Input.Now ? 1 : 0) >= MineRoutes.MaxStops)
                    continue;
                moves.Add((Insertion(o, lists[o], s), s, o));
            }
            (double J, int Station, long To, List<int> From, List<int> Into)? best = null;
            var without = new Dictionary<int, (List<int> List, double Finish)>();
            foreach (var (_, s, o) in moves.OrderBy(m => m.Estimate).ThenBy(m => m.Station).ThenBy(m => m.To).Take(MovesWeighed))
            {
                if (!without.TryGetValue(s, out var w))
                {
                    var rest = from.Where(x => x != s).ToList();
                    var q = rest.Count == 0 ? ([], _start[b].Free) : Solve(b, rest, false);
                    w = without[s] = q is { } qq ? (qq.Order.Select(k => rest[k]).ToList(), qq.Finish) : (rest, double.PositiveInfinity);
                }
                var into = lists[o].Append(s).ToList();
                if (Solve(o, into, false) is not { } r)
                    continue;
                var trial = new Dictionary<long, double>(f)
                {
                    [b] = f[b] + (w.Finish - model[b]),
                    [o] = f[o] + (r.Finish - model[o]),
                };
                double jt = J(trial);
                if (best is not { } bb || jt < bb.J - 1e-9)
                    best = (jt, s, o, w.List, r.Order.Select(k => into[k]).ToList());
            }
            if (best is not { } chosen || chosen.J >= j - ScoreSlack)
                return null;
            var moved = lists.ToDictionary(p => p.Key, p => p.Value.ToList());
            moved[b] = chosen.From;
            moved[chosen.To] = chosen.Into;
            return Exact(moved);
        }

        private double Finish(long member, List<int> list) =>
            list.Count == 0 ? _start[member].Free : Solve(member, list, false)?.Finish ?? double.PositiveInfinity;

        // the cheapest insertion of a station into a list by walking, and its work
        private double Insertion(long member, List<int> list, int station)
        {
            var s = _info[station];
            var places = list.Select(i => _info[i].Stand).Prepend(_start[member].At).ToList();
            double best = double.PositiveInfinity;
            for (int k = 0; k < places.Count; k++)
            {
                double added = MineCosts.Estimate(places[k], s.Stand);
                if (k + 1 < places.Count)
                    added += MineCosts.Estimate(s.Stand, places[k + 1]) - MineCosts.Estimate(places[k], places[k + 1]);
                best = Math.Min(best, added);
            }
            return best + s.Work;
        }
    }
}
