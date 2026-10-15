using System.Diagnostics;
using SeraphHorizons.Mod.Eidolon.Core;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace SeraphHorizons.Mod.Eidolon;

/// <summary>
/// The mine planner's host on the server (README "Eidolon", mining, *Stations*: planning off the main
/// thread). Each mining job's picture of its area (<see cref="MineJobCells.Live"/>, <see cref="MineJobCells.Plan"/>)
/// is made here, once, for the first member to start (<see cref="Ensure"/>), and again as the world changes.
///
/// <para><b>Threads.</b> Whatever reads the world runs on the server's main thread, in slices of at most
/// <see cref="MinePlanner.BudgetMs"/> a tick across every job (<see cref="MineTickBudget"/>; the game's
/// block accessor is not thread-safe): the window read a block column at a time (<see cref="MineSite.Load"/>,
/// <see cref="GameMineWorld"/>), and what roofs the area's columns. The plan itself (<see cref="MinePlanner"/>)
/// and the live state built over it (<see cref="MineState.Adopt"/>) are made on a worker
/// (<c>Task.Run</c>), on copies only: the site copied twice (the planner's and the new
/// state's), and the job's skipped and parked cells, its bad stands and the roofed cells snapshotted, so
/// the worker shares nothing mutable with the main thread. The main thread looks for the result each tick
/// and swaps it in whole, playing over it what changed while it was made (the journal); a result the job
/// no longer wants (closed, a newer plan asked for, the server stopping) is cancelled through its token
/// and dropped. A worker that throws is logged and its job kept as it was (the plan asked for again
/// later; a first plan that fails three times leaves the job an empty one, so its members report).</para>
///
/// <para>The world changing under a job is taken in on the main thread: its own breaks at once
/// (<see cref="Broken"/>, from the order); a player's breaks and placings in its window by the server's
/// events; anything else (water, falling sand, other mods, a column that loads) by a sweep of its area's
/// columns, and one round them, every <see cref="SyncSeconds"/> (<see cref="Seen"/>). Only what can matter
/// to the plan marks it stale: a block placed, a new target, a block that holds a column up, a block near
/// the area going; a target taken away, a liquid over dug ground or a change far from the area is taken
/// in (or passed over) without a new plan. A stale plan is made again at most every
/// <see cref="ReplanSeconds"/>; the old one serves meanwhile (every claim and strike is checked against the
/// live state anyway, so a stale plan only costs a worse order). A plan stuck (no member has a station it
/// may work while cells are left) is asked for at once (<see cref="Replan"/>).</para>
///
/// <para>Each plan's blocks out of reach go to the job's owner as boxes (<see cref="MineOutOfReachPacket"/>,
/// <see cref="EidolonMineSystem.Channel"/>), outlined in red on their command tool in mine mode, again when
/// the owner joins, and none once the job is done or its planning fails (<see cref="Close"/>). Server side.</para>
/// </summary>
public class EidolonMinePlans : ModSystem
{
    /// <summary>The least time between two plans made again for a change in the world.</summary>
    public const double ReplanSeconds = 2;

    /// <summary>How often a job's area is swept for changes the events do not show.</summary>
    public const double SyncSeconds = MineOrder.RescanSeconds;

    /// <summary>A first plan that fails this many times leaves the job an empty plan.</summary>
    private const int MostFailures = 3;

    /// <summary>How far round the area, in columns, a change can matter to the plan: a stand outside it
    /// reaching in (its box and reach), with a column to spare.</summary>
    private const int Relevant = 5;

    /// <summary>Whether mining jobs of more than one member are worked by look-ahead routes (README "Eidolon",
    /// working together, *Routes*); off (and with <c>SH_MINE_BASELINE=1</c> in the environment), every job is
    /// dispatched as before routes (<see cref="MineDispatch.Next"/>), for measuring and as the fallback.</summary>
    public static bool Routing { get; set; } = Environment.GetEnvironmentVariable("SH_MINE_BASELINE") != "1";

    /// <summary>The least time between two route solves asked for by repair (drops, a member gone, late).</summary>
    public const double SolveMinSeconds = 2;

    /// <summary>The least time between two route solves when the one asked for is urgent (a new station plan, a
    /// deadlock).</summary>
    public const double UrgentMinSeconds = 0.5;

    /// <summary>The main thread's share of a tick counted in blocks read instead of
    /// <see cref="MinePlanner.BudgetMs"/> of wall time (<see cref="MineTickBudget"/>), so a plan starts on the
    /// same tick on every machine: for the tests' virtual clock. Null, wall time, in the game.</summary>
    public static int? BudgetReads { get; set; }

    /// <summary>About as many blocks as <see cref="MinePlanner.BudgetMs"/> reads on a typical machine (a
    /// window's block, ruled or not, its roof's, a sweep's), for <see cref="BudgetReads"/>.</summary>
    public const int TypicalReads = 5000;

    private ICoreServerAPI? _api;
    private readonly List<Work> _work = new();
    private MineTickBudget _budget = new(MinePlanner.BudgetMs);
    private bool _stopping;

    public static EidolonMinePlans? Of(ICoreAPI? api) =>
        api?.Side == EnumAppSide.Server ? api.ModLoader.GetModSystem<EidolonMinePlans>() : null;

    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;

    public override void StartServerSide(ICoreServerAPI api)
    {
        _api = api;
        api.Event.RegisterGameTickListener(_ => Tick(), 20);
        api.Event.DidBreakBlock += (_, _, sel) => Seen(sel?.Position);
        api.Event.DidPlaceBlock += (_, _, sel, _) => Seen(sel?.Position);
        api.Event.PlayerNowPlaying += Joined;
        api.Event.ServerRunPhase(EnumServerRunPhase.Shutdown, Stop);
    }

    public override void Dispose() => Stop();

    // The server stopping: every plan under way cancelled, none swapped in.
    private void Stop()
    {
        _stopping = true;
        foreach (var work in _work)
            work.Cancel();
        _work.Clear();
    }

    /// <summary>What a worker made: the plan and the live state built over it, and what it cost.</summary>
    private sealed record Plan(MinePlan Made, MineState State, int Steps, IReadOnlyDictionary<MinePlanPhase, TimeSpan> Spent, TimeSpan Took,
        HashSet<PathCell> Bad, Exception? Error = null);

    /// <summary>One job's planning: its window being read, the plan being made on a worker, and its area's
    /// sweep.</summary>
    private sealed class Work(MineJobCells cells, MarkArea area, PathCell? home, GameMineWorld world, int dimension, string? owner, int step)
    {
        public readonly MineJobCells Cells = cells;
        public readonly MarkArea Area = area;
        public readonly PathCell? Home = home;
        public readonly GameMineWorld World = world;
        public readonly int Dimension = dimension;
        public readonly string? Owner = owner;
        public readonly int Step = step;
        public MineSite? Site;
        public IEnumerator<bool>? Loading;
        public IEnumerator<bool>? Roofing;
        public HashSet<MarkPos>? Roof;
        public MineSite? RoofOf;
        public Task<Plan?>? Job;
        public CancellationTokenSource? Token;
        public List<Action<MineState>>? Journal;
        public double PlannedAt = double.MinValue;
        public bool Urgent;
        public int Failures;
        public double SyncAt;
        public int SyncIndex;
        public readonly HashSet<PathCell> Retried = new();
        public readonly Stopwatch Spent = new();
        public int Ticks;
        public Task<MineRoutes?>? Routing;
        public CancellationTokenSource? RoutingToken;
        public double RoutedAt = double.MinValue;
        public int RoutingPlanVersion;
        public string? RoutingWhy;

        // the members present the solve under way was made for (its choice is theirs)
        public long[] RoutingMembers = [];

        public void Cancel()
        {
            CancelRouting();
            Token?.Cancel();
            Token?.Dispose();
            Token = null;
            Job = null;
            Journal = null;
            Roofing = null;
            Cells.Planning = false;
        }

        public void CancelRouting()
        {
            RoutingToken?.Cancel();
            RoutingToken?.Dispose();
            RoutingToken = null;
            Routing = null;
        }
    }

    /// <summary>An area for the log (whose messages are format strings: no braces).</summary>
    internal static string Describe(MarkArea a) => $"({a.Min.X},{a.Min.Y},{a.Min.Z})-({a.Max.X},{a.Max.Y},{a.Max.Z})";

    private static double Now(IWorldAccessor world) => world.ElapsedMilliseconds / 1000.0;

    /// <summary>Starts making <paramref name="cells"/>' picture of <paramref name="area"/> (its first
    /// member's call; nothing if it is made or being made), for <paramref name="eidolon"/>'s owner, with its
    /// pick's rules (<paramref name="pick"/>, <paramref name="maxTier"/>, <paramref name="mineThrough"/>),
    /// ordered from <paramref name="home"/>.</summary>
    public void Ensure(MineJobCells cells, MarkArea area, PathCell? home, EntityLaborEidolon eidolon, Item? pick, int maxTier, string[] mineThrough,
        Item? shovel = null)
    {
        if (_api == null || _stopping || _work.Any(w => w.Cells == cells))
            return;
        var config = EidolonSystem.Of(eidolon.Api)?.Config ?? EidolonConfig.Defaults;
        int step = Math.Clamp(Math.Min(config.MaxClimbBlocks, config.MaxFallBlocks), 1, 2);
        var world = new GameMineWorld(eidolon.World, eidolon.Pos.Dimension, area, pick, maxTier, mineThrough, shovel) { Skip = cells.Skip };
        _work.Add(new Work(cells, area, home, world, eidolon.Pos.Dimension, eidolon.OwnerUid, step) { SyncAt = Now(eidolon.World) + SyncSeconds });
    }

    /// <summary>Asks for <paramref name="cells"/>' plan to be made again: at once when its members are stuck
    /// (<paramref name="now"/>: one under way is dropped for a fresh one), else as for a change in the world
    /// (at most every <see cref="ReplanSeconds"/>).</summary>
    public void Replan(MineJobCells cells, bool now)
    {
        if (_work.FirstOrDefault(w => w.Cells == cells) is not { } work)
            return;
        if (now)
        {
            if (work.Job != null && cells.Live != null)
                work.Cancel();
            work.Urgent = true;
        }
        else if (cells.Live != null)
            cells.Live.Stale = true;
    }

    /// <summary>Asks for <paramref name="cells"/>' routes to be solved again (README "Eidolon", working together,
    /// *Routes*), for <paramref name="why"/>, once no solve is under way: when <paramref name="urgent"/>, as soon
    /// as <see cref="UrgentMinSeconds"/> have passed since the last started, else <see cref="SolveMinSeconds"/>.</summary>
    public static void RequestRoutes(MineJobCells cells, string why, bool urgent)
    {
        cells.RoutesWanted = why;
        cells.RoutesUrgent |= urgent;
    }

    /// <summary>The job is done: its planning stops, and its owner's outlines go.</summary>
    public void Close(MineJobCells cells)
    {
        var work = _work.FirstOrDefault(w => w.Cells == cells);
        if (work == null)
            return;
        work.Cancel();
        _work.Remove(work);
        Send(work, []);
    }

    /// <summary>Blocks its job broke: dug in the live state (and in the one being made, once it is in).</summary>
    public void Broken(MineJobCells cells, IReadOnlyList<MarkPos> broken)
    {
        if (broken.Count == 0)
            return;
        cells.StuckReplanned = false;
        var list = broken.ToList();
        Take(cells, s => s.Apply(list));
        if (_work.FirstOrDefault(w => w.Cells == cells) is { } work)
            foreach (var cell in list)
                Retry(work, cell);
    }

    /// <summary>
    /// A block of a job's window seen as <paramref name="voxel"/> in the world, its job not having made it
    /// so. As the live state has it, or far from the area (more than <see cref="Relevant"/> columns): nothing.
    /// Gone: dug (the plan made again if it was no target, for a way may have opened). Anything else: taken in
    /// (<see cref="MineState.Changed(MarkPos, MineVoxel, bool)"/>), the plan made again only for a block placed
    /// where there was none, a new target, or one that holds its column up; a target turned into one it leaves
    /// or a liquid over dug ground changes no plan. A stand found bad within two columns of it is tried
    /// again, once.
    /// </summary>
    public void Seen(MineJobCells cells, MarkPos cell, MineVoxel voxel)
    {
        if (cells.Live is not { } live)
            return;
        var site = live.Site;
        var area = site.Area;
        if (cell.X < area.Min.X - Relevant || cell.X > area.Max.X + Relevant || cell.Z < area.Min.Z - Relevant || cell.Z > area.Max.Z + Relevant)
            return;
        bool was = site.Solid(cell.X, cell.Y, cell.Z);
        var rule = site.Rule(cell);
        if (was == voxel.Solid && rule == voxel.Rule)
            return;
        bool gone = !voxel.Solid && voxel.Rule == MineRule.Air;
        bool target = live.Remaining.Contains(cell);
        if (gone)
        {
            Take(cells, s => s.Apply([cell]));
            if (!target && was)
                live.Stale = true;
        }
        else
        {
            bool stale = (voxel.Solid && !was) || (voxel.Rule == MineRule.Mine && rule != MineRule.Mine) || EidolonMining.Holds(voxel.Rule);
            Take(cells, s => s.Changed(cell, voxel, stale));
        }
        if (_work.FirstOrDefault(w => w.Cells == cells) is { } work)
            Retry(work, cell);
    }

    // A stand found bad near a block that changed is tried again, once.
    private static void Retry(Work work, MarkPos cell)
    {
        var cells = work.Cells;
        if (cells.BadStands.Count == 0)
            return;
        foreach (var stand in cells.BadStands.Where(s => Math.Abs(s.X - cell.X) <= 2 && Math.Abs(s.Z - cell.Z) <= 2 && work.Retried.Add(s)).ToList())
        {
            cells.BadStands.Remove(stand);
            if (cells.Live != null)
                cells.Live.Stale = true;
        }
    }

    // A change to the live state, and noted for the one being made, which it is played over once in.
    private void Take(MineJobCells cells, Action<MineState> change)
    {
        if (cells.Live is { } live)
            change(live);
        if (_work.FirstOrDefault(w => w.Cells == cells) is { Journal: { } journal })
            journal.Add(change);
    }

    // A player broke or placed a block: for each job whose window it is in.
    private void Seen(BlockPos? pos)
    {
        if (pos == null)
            return;
        foreach (var work in _work)
        {
            if (work.Dimension != pos.dimension || work.Cells.Live is not { } live || !In(live.Site.Window, pos.X, pos.Y, pos.Z))
                continue;
            try
            {
                Seen(work.Cells, new MarkPos(pos.X, pos.Y, pos.Z), work.World.Read(pos.X, pos.Y, pos.Z));
            }
            catch (Exception e)
            {
                _api!.Logger.Error("[eidolon-mine] taking in a change at {0} for {1} failed: {2}", pos, Describe(work.Area), e);
                live.Stale = true;
            }
        }
    }

    // The owner joins: the outlines of their jobs' plans.
    private void Joined(IServerPlayer player)
    {
        foreach (var work in _work.Where(w => w.Owner == player.PlayerUID && w.Cells.Plan != null))
            Send(work, MineOutlines.Boxes(work.Cells.OutOfReach));
    }

    private static bool In(MarkArea box, int x, int y, int z) =>
        x >= box.Min.X && x <= box.Max.X && y >= box.Min.Y && y <= box.Max.Y && z >= box.Min.Z && z <= box.Max.Z;

    private void Tick()
    {
        if (_api == null || _stopping || _work.Count == 0)
            return;
        var board = EidolonJobSystem.Of(_api)?.Mining;
        // a job no longer on the board, or closed: nothing more to plan for it
        foreach (var gone in _work.Where(w => board != null && !board.Jobs.Any(j => j.Shared == w.Cells && !j.Closed)).ToList())
            Close(gone.Cells);
        if (_budget.Reads != BudgetReads)
            _budget = new MineTickBudget(MinePlanner.BudgetMs, BudgetReads);
        _budget.Restart();
        foreach (var work in _work.ToList())
        {
            if (_budget.Spent)
                break;
            work.Spent.Start();
            work.Ticks++;
            try
            {
                Step(work);
            }
            catch (Exception e)
            {
                Failed(work, e);
            }
            work.Spent.Stop();
        }
    }

    private void Step(Work work)
    {
        var cells = work.Cells;
        double now = Now(_api!.World);
        // a plan made on the worker: swapped in, dropped or its failure taken
        if (work.Job is { IsCompleted: true } job)
        {
            var made = job.Result;
            work.Job = null;
            work.Token?.Dispose();
            work.Token = null;
            if (made is { Error: { } error })
                Failed(work, error);
            else if (made != null)
                Swap(work, made);
            return;
        }
        // the first plan: the window read, a column at a time
        if (cells.Live == null && work.Job == null)
        {
            work.Site ??= new MineSite(work.Area, work.Step);
            if (work.Roof == null)
            {
                work.Loading ??= work.Site.Load(work.World).GetEnumerator();
                while (_budget.Take(work.Site.Window.SizeY))
                    if (!work.Loading.MoveNext())
                    {
                        work.Loading = null;
                        work.Roof = new HashSet<MarkPos>();
                        work.Roofing = Roofs(work).GetEnumerator();
                        break;
                    }
                if (work.Loading != null)
                    return;
            }
            if (!RoofDone(work))
                return;
            Start(work, work.Site, now);
            return;
        }
        if (cells.Live is not { } live)
            return;
        if (Routing && cells.Plan != null && !cells.Planning)
            RouteStep(work, live, now);
        if (work.Job == null && (work.Urgent || live.Stale && now - work.PlannedAt >= ReplanSeconds))
        {
            if (work.Roofing == null && work.RoofOf != live.Site)
            {
                work.Roof = new HashSet<MarkPos>();
                work.RoofOf = live.Site;
                work.Roofing = Roofs(work).GetEnumerator();
            }
            if (!RoofDone(work))
                return;
            work.RoofOf = null;
            Start(work, live.Site, now);
            return;
        }
        if (now >= work.SyncAt)
        {
            try
            {
                Sync(work, live, now);
            }
            catch (Exception e)
            {
                // the sweep starts over later; the job goes on as it is
                _api.Logger.Error("[eidolon-mine] sweeping {0} for changes failed: {1}", Describe(work.Area), e);
                work.SyncIndex = 0;
                work.SyncAt = now + SyncSeconds;
            }
        }
    }

    // ---- routes ----

    /// <summary>
    /// A job's routes (README "Eidolon", working together, *Routes*): a solve finished on the worker swapped in
    /// (dropped if the station plan changed meanwhile, or kept out when it kept the routes running); else, with
    /// none under way, one started when asked (<see cref="RequestRoutes"/>), on a new station plan, or every
    /// <see cref="MineRoutes.ResolveSeconds"/>, from a copy of the live state taken here (the one main-thread
    /// cost, a memory copy, made on the tick a solve is due: no wall-time budget decides when). A job of one member present has none (it
    /// is dispatched as before); a job its routes left to today's dispatch keeps that until a new station plan.
    /// </summary>
    private void RouteStep(Work work, MineState live, double now)
    {
        var cells = work.Cells;
        if (work.Routing is { IsCompleted: true } task)
        {
            var made = task.Result;
            work.CancelRouting();
            if (made == null)
                return;
            if (work.RoutingPlanVersion != cells.PlanVersion)
            {
                RequestRoutes(cells, "plan", true);
                return;
            }
            cells.RouteChoice.Made(made, work.RoutingMembers, work.Area);
            if (made.Stats.KeptCurrent && cells.Routes != null)
                return;
            // Members gone from the job while it was solved: their routes are taken out as Stop does, their
            // stations handed on.
            var present = _api == null ? null : EidolonJobSystem.Of(_api)?.Mining.Jobs.FirstOrDefault(j => j.Shared == cells && !j.Closed)?.Present;
            if (present == null || present.Count < 2)
                return;
            foreach (long gone in made.Routes.Keys.Where(m => !present.Contains(m)).ToList())
                if (made.Gone(gone, now, live) is { } again)
                    RequestRoutes(cells, again, false);
            if (cells.Routes == null || cells.Routes.PlanVersion != made.PlanVersion)
                cells.RoutesPlanSince = now;
            cells.Routes = made;
            cells.RoutesVersion++;
            made.Sweep(live, now);
            _api!.Logger.Debug($"[eidolon-mine] routes {cells.RoutesVersion} for {Describe(work.Area)} ({work.RoutingWhy}): "
                + (made.Today ? "left to today's dispatch" : $"variant {made.VariantIndex}, stops {string.Join("/", made.Routes.Values.Select(r => r.Stops.Count(s => s.Kind == MineStopKind.Station)))}")
                + $", J {made.Score.J:0.0} s, zones {made.Zones.Count}, solved in {made.Stats.Took.TotalMilliseconds:0} ms off the main thread");
            return;
        }
        if (work.Routing != null || _api == null)
            return;
        var jobs = EidolonJobSystem.Of(_api);
        var job = jobs?.Mining.Jobs.FirstOrDefault(j => j.Shared == cells && !j.Closed);
        if (job == null || job.Present.Count < 2)
        {
            // dispatched as before; the choice (RouteChoice) is kept for when the job is a crew again
            cells.Routes = null;
            cells.RoutesWanted = null;
            cells.RoutesUrgent = false;
            return;
        }
        // the job's choice, kept by every solve (a new station plan's too) while its members present are those
        // it was made for
        int? choice = cells.RouteChoice.KeepFor(job.Present, work.Area);
        bool fresh = cells.Routes == null || cells.Routes.PlanVersion != cells.PlanVersion;
        bool joined = cells.Routes is { Today: false } r && job.Present.Any(m => !r.Routes.ContainsKey(m));
        // a choice made for other members (one joined or left since): made again
        bool rechoose = cells.RouteChoice.Index != null && choice == null;
        string? why = fresh ? (cells.Routes == null ? "first" : "plan")
            : rechoose || joined ? "members"
            : cells.RoutesWanted ?? (now - work.RoutedAt >= MineRoutes.ResolveSeconds ? "periodic" : null);
        // at most every SolveMinSeconds (UrgentMinSeconds when asked so), but for the first solve of a station plan
        double least = cells.RoutesUrgent && cells.RoutesWanted != null ? UrgentMinSeconds : SolveMinSeconds;
        if (why == null || now - work.RoutedAt < least && !(fresh && work.RoutingPlanVersion != cells.PlanVersion))
            return;
        // left to today's dispatch: kept so, with no solve, until a new station plan (whose solve keeps it too
        // while the members are the same)
        if (!fresh && cells.Routes!.Today && choice == MineRoutePlanner.TodayIndex)
        {
            cells.RoutesWanted = null;
            cells.RoutesUrgent = false;
            work.RoutedAt = now;
            return;
        }
        // not held back by the planner's wall-time budget: a solve starts on the tick it is due, whatever the
        // machine (its one main-thread cost, a copy of the live state, is small)
        var members = MineOrder.RouteMembers(job, jobs!, now);
        if (members.Count < 2)
            return;
        var input = new MineRouteInput(cells.Plan!, cells.PlanVersion, live.Clone(), members, [], now,
            why == "periodic" ? cells.Routes!.Copy() : null, null)
        {
            Keep = choice,
        };
        var token = new CancellationTokenSource();
        var cancel = token.Token;
        var logger = _api.Logger;
        var area = work.Area;
        work.RoutingToken = token;
        work.RoutingPlanVersion = cells.PlanVersion;
        work.RoutedAt = now;
        work.RoutingWhy = why;
        work.RoutingMembers = job.Present.ToArray();
        cells.RoutesWanted = null;
        cells.RoutesUrgent = false;
        work.Routing = Task.Run(() =>
        {
            try
            {
                return MineRoutePlanner.Solve(input, cancel);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (Exception e)
            {
                logger.Error("[eidolon-mine] routes for {0} failed: {1}", Describe(area), e);
                return null;
            }
        });
    }

    // The roof check run on: true once done.
    private bool RoofDone(Work work)
    {
        if (work.Roofing == null)
            return true;
        while (_budget.Take(RoofStep * EidolonTunnels.RoofRise))
            if (!work.Roofing.MoveNext())
            {
                work.Roofing = null;
                return true;
            }
        return false;
    }

    // The columns of roofs read a step.
    private const int RoofStep = 64;

    // What roofs the area's columns (EidolonTunnels.Covered, MineOrder.Roof), read here on the main thread
    // for the worker: the cells over the area's top it may ask of, a few columns a step.
    private IEnumerable<bool> Roofs(Work work)
    {
        var blocks = _api!.World.BlockAccessor;
        var pos = new BlockPos(work.Dimension);
        var area = work.Area;
        int columns = 0;
        for (int x = area.Min.X; x <= area.Max.X; x++)
        for (int z = area.Min.Z; z <= area.Max.Z; z++)
        {
            for (int y = area.Max.Y + 1; y <= area.Max.Y + EidolonTunnels.RoofRise; y++)
                if (blocks.GetChunkAtBlockPos(pos.Set(x, y, z)) != null && MineOrder.Roof(blocks.GetBlock(pos)))
                    work.Roof!.Add(new MarkPos(x, y, z));
            if (++columns % RoofStep == 0)
                yield return false;
        }
    }

    // A plan started on a worker, from copies of the site and snapshots of the job's sets.
    private void Start(Work work, MineSite site, double now)
    {
        var cells = work.Cells;
        var planSite = site.Clone();
        var stateSite = site.Clone();
        var bad = new HashSet<PathCell>(cells.BadStands);
        var skip = new HashSet<MarkPos>(cells.Skip);
        skip.UnionWith(cells.Parked);
        var roof = work.Roof ?? new HashSet<MarkPos>();
        var home = work.Home;
        var token = new CancellationTokenSource();
        var cancel = token.Token;
        work.Token = token;
        work.Journal = cells.Live != null ? new() : null;
        work.PlannedAt = now;
        work.Urgent = false;
        if (cells.Live != null)
            cells.Live.Stale = false;
        cells.Planning = true;
        work.Job = Task.Run(() => Make(planSite, stateSite, home, skip, bad, roof, cancel));
    }

    // On the worker: the plan, and the live state over it, from what it was given alone. Never throws: a
    // failure comes back as the result's error, a cancelled one as null.
    private static Plan? Make(MineSite planSite, MineSite stateSite, PathCell? home, HashSet<MarkPos> skip, HashSet<PathCell> bad,
        HashSet<MarkPos> roof, CancellationToken cancel)
    {
        try
        {
            var clock = Stopwatch.StartNew();
            var input = new MinePlanInput(new MineState(planSite, home, bad), home, skip.Contains, bad) { Roof = roof.Contains };
            var planner = new MinePlanner(input);
            while (!planner.Step(TimeSpan.FromMilliseconds(5)))
                if (cancel.IsCancellationRequested)
                    return null;
            var state = new MineState(stateSite, home, bad);
            foreach (var _ in state.Adopt(planner.Plan!))
                if (cancel.IsCancellationRequested)
                    return null;
            return new Plan(planner.Plan!, state, planner.Steps, planner.Spent, clock.Elapsed, bad);
        }
        catch (Exception e)
        {
            return new Plan(null!, null!, 0, new Dictionary<MinePlanPhase, TimeSpan>(), TimeSpan.Zero, bad, e);
        }
    }

    // The new plan in: what changed while it was made played over its state, which becomes the live one; the
    // worker's copy of the bad stands, with those found since, becomes the job's.
    private void Swap(Work work, Plan made)
    {
        var cells = work.Cells;
        var state = made.State;
        made.Bad.UnionWith(cells.BadStands);
        cells.BadStands = made.Bad;
        foreach (var change in work.Journal ?? [])
            change(state);
        work.Journal = null;
        cells.Live = state;
        cells.Plan = made.Made;
        cells.Stair.UnionWith(made.Made.Stair);
        cells.PlanVersion++;
        cells.Planning = false;
        work.Site = null;
        work.Failures = 0;
        _api!.Logger.Debug($"[eidolon-mine] plan {cells.PlanVersion} for {Describe(work.Area)}: {made.Made.Stations.Count} stations, "
            + $"{made.Made.OutOfReach.Count} out of reach, {made.Made.Held.Count} kept under those, {made.Made.Stair.Count} stair "
            + $"({made.Made.WayOut.Count} way out), {made.Made.Reruns} reruns; made in {made.Took.TotalMilliseconds:0} ms off the main thread "
            + $"({string.Join(", ", made.Spent.Select(p => $"{p.Key} {p.Value.TotalMilliseconds:0.0} ms"))}); {state.Remaining.Count} to dig");
        Send(work, MineOutlines.Boxes(cells.OutOfReach));
    }

    // A failure, on the worker or here: logged; the job kept as it was and its plan asked for again later, or,
    // a first plan failing again and again, given an empty one.
    private void Failed(Work work, Exception e)
    {
        _api!.Logger.Error("[eidolon-mine] planning {0} failed: {1}", Describe(work.Area), e);
        var cells = work.Cells;
        work.Cancel();
        work.Failures++;
        work.PlannedAt = Now(_api.World);
        if (cells.Live != null)
            cells.Live.Stale = true;
        else if (work.Failures >= MostFailures)
            Fail(work);
        else
        {
            work.Site = null;
            work.Loading = null;
            work.Roof = null;
        }
    }

    // A job that cannot be planned: an empty plan, so its members report and go, and its owner's outlines cleared.
    private void Fail(Work work)
    {
        var cells = work.Cells;
        var site = cells.Live?.Site.Clone() ?? work.Site ?? new MineSite(work.Area, work.Step);
        var plan = new MinePlan
        {
            Stations = [],
            OutOfReach = new HashSet<MarkPos>(),
            Held = new HashSet<MarkPos>(),
            WayOut = new HashSet<MarkPos>(),
            Left = new HashSet<MarkPos>(),
            Stair = new HashSet<MarkPos>(),
            Area = work.Area,
        };
        var state = new MineState(site, work.Home, cells.BadStands);
        state.AdoptNow(plan);
        cells.Live = state;
        cells.Plan = plan;
        cells.PlanVersion++;
        cells.Planning = false;
        _work.Remove(work);
        Send(work, []);
    }

    // The area's columns and one round them, a column at a time, against the live state.
    private void Sync(Work work, MineState live, double now)
    {
        var area = work.Area;
        int sx = area.SizeX + 2, sz = area.SizeZ + 2;
        var window = live.Site.Window;
        while (_budget.Take(window.SizeY))
        {
            if (work.SyncIndex >= sx * sz)
            {
                work.SyncIndex = 0;
                work.SyncAt = now + SyncSeconds;
                return;
            }
            int x = area.Min.X - 1 + work.SyncIndex / sz, z = area.Min.Z - 1 + work.SyncIndex % sz;
            work.SyncIndex++;
            if (!work.World.Loaded(x, z))
                continue;
            if (live.Site.Unloaded(x, z))
            {
                // a column loaded since: read again, and the plan made again with it
                Take(work.Cells, s => s.Reloaded(work.World, x, z));
                continue;
            }
            for (int y = window.Min.Y; y <= window.Max.Y; y++)
                Seen(work.Cells, new MarkPos(x, y, z), work.World.Read(x, y, z));
        }
    }

    // The owner's outlines of what is out of reach (none: cleared).
    private void Send(Work work, IReadOnlyList<MarkArea> boxes)
    {
        if (work.Owner == null || _api?.World.PlayerByUid(work.Owner) is not IServerPlayer { ConnectionState: EnumClientState.Playing } owner)
            return;
        var a = work.Area;
        _api.Network.GetChannel(EidolonMineSystem.Channel)?.SendPacket(new MineOutOfReachPacket
        {
            Area = [a.Min.X, a.Min.Y, a.Min.Z, a.Max.X, a.Max.Y, a.Max.Z],
            Boxes = boxes.SelectMany(b => new[] { b.Min.X, b.Min.Y, b.Min.Z, b.Max.X, b.Max.Y, b.Max.Z }).ToArray(),
        }, owner);
    }
}
