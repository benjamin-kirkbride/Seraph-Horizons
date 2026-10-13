using System.Diagnostics;
using SeraphHorizons.Mod.Eidolon.Core;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace SeraphHorizons.Mod.Eidolon;

/// <summary>
/// The mine planner's host on the server (README "Eidolon", mining, *Stations*: planning in slices). Each
/// mining job's picture of its area (<see cref="MineJobCells.Live"/>, <see cref="MineJobCells.Plan"/>) is
/// made here, once, for the first member to start (<see cref="Ensure"/>): its window read from the world a
/// block column at a time (<see cref="MineSite.Load"/>, <see cref="GameMineWorld"/>), the plan made
/// (<see cref="MinePlanner.Step"/>) and the live state built over it (<see cref="MineState.Adopt"/>), all
/// in one queue across every job, oldest first, at most <see cref="MinePlanner.BudgetMs"/> a tick between
/// them. The job's members wait while it is made.
///
/// <para>The world changing under a job is taken in: its own breaks at once (<see cref="Broken"/>, from the
/// order); a player's breaks and placings in its window by the server's events; and anything else (water,
/// falling sand, other mods, a column that loads) by a sweep of its area's columns, and one round them, every
/// <see cref="SyncSeconds"/> (<see cref="Seen"/>). A block gone is dug in the live state; any other change
/// marks it stale, and the plan is made again, at most every <see cref="ReplanSeconds"/>, from a copy of the
/// live state, the old one kept meanwhile (every claim and strike is checked against the live state anyway,
/// so a stale plan only costs a worse order). The new plan is swapped in whole, with what changed while it
/// was made played over it. A plan stuck (no member has a station it may work while cells are left) is made
/// again at once (<see cref="Replan"/>).</para>
///
/// <para>Each plan's blocks out of reach go to the job's owner as boxes (<see cref="MineOutOfReachPacket"/>,
/// <see cref="EidolonMineSystem.Channel"/>), outlined in red on their command tool in mine mode, and none
/// once the job is done (<see cref="Close"/>). Server side.</para>
/// </summary>
public class EidolonMinePlans : ModSystem
{
    /// <summary>The least time between two plans made again for a change in the world.</summary>
    public const double ReplanSeconds = 2;

    /// <summary>How often a job's area is swept for changes the events do not show.</summary>
    public const double SyncSeconds = MineOrder.RescanSeconds;

    private ICoreServerAPI? _api;
    private readonly List<Work> _work = new();
    private readonly Stopwatch _clock = new();

    public static EidolonMinePlans? Of(ICoreAPI? api) =>
        api?.Side == EnumAppSide.Server ? api.ModLoader.GetModSystem<EidolonMinePlans>() : null;

    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;

    public override void StartServerSide(ICoreServerAPI api)
    {
        _api = api;
        api.Event.RegisterGameTickListener(_ => Tick(), 20);
        api.Event.DidBreakBlock += (_, _, sel) => Seen(sel?.Position);
        api.Event.DidPlaceBlock += (_, _, sel, _) => Seen(sel?.Position);
    }

    /// <summary>One job's planning: its window being read, its plan being made, the live state being built
    /// over a new plan, and its area's sweep.</summary>
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
        public MineSite? Copy;
        public MinePlanner? Planner;
        public MineState? Pending;
        public IEnumerator<bool>? Adopting;
        public MinePlan? Made;
        public List<Action<MineState>>? Journal;
        public double PlannedAt = double.MinValue;
        public bool Urgent;
        public double SyncAt;
        public int SyncIndex;
        public readonly HashSet<PathCell> Retried = new();
        public readonly Stopwatch Spent = new();
        public int Ticks;
    }

    /// <summary>An area for the log (whose messages are format strings: no braces).</summary>
    internal static string Describe(MarkArea a) => $"({a.Min.X},{a.Min.Y},{a.Min.Z})-({a.Max.X},{a.Max.Y},{a.Max.Z})";

    private static double Now(IWorldAccessor world) => world.ElapsedMilliseconds / 1000.0;

    /// <summary>Starts making <paramref name="cells"/>' picture of <paramref name="area"/> (its first
    /// member's call; nothing if it is made or being made), for <paramref name="eidolon"/>'s owner, with its
    /// pick's rules (<paramref name="pick"/>, <paramref name="maxTier"/>, <paramref name="mineThrough"/>),
    /// ordered from <paramref name="home"/>.</summary>
    public void Ensure(MineJobCells cells, MarkArea area, PathCell? home, EntityLaborEidolon eidolon, Item? pick, int maxTier, string[] mineThrough)
    {
        if (_api == null || _work.Any(w => w.Cells == cells))
            return;
        var config = EidolonSystem.Of(eidolon.Api)?.Config ?? EidolonConfig.Defaults;
        int step = Math.Clamp(Math.Min(config.MaxClimbBlocks, config.MaxFallBlocks), 1, 2);
        var world = new GameMineWorld(eidolon.World, eidolon.Pos.Dimension, area, pick, maxTier, mineThrough) { Skip = cells.Skip };
        _work.Add(new Work(cells, area, home, world, eidolon.Pos.Dimension, eidolon.OwnerUid, step) { SyncAt = Now(eidolon.World) + SyncSeconds });
    }

    /// <summary>Asks for <paramref name="cells"/>' plan to be made again: at once when its members are stuck
    /// (<paramref name="now"/>), else as for a change in the world (at most every <see cref="ReplanSeconds"/>).</summary>
    public void Replan(MineJobCells cells, bool now)
    {
        if (_work.FirstOrDefault(w => w.Cells == cells) is not { } work)
            return;
        if (now)
            work.Urgent = true;
        else if (cells.Live != null)
            cells.Live.Stale = true;
    }

    /// <summary>The job is done: its planning stops, and its owner's outlines go.</summary>
    public void Close(MineJobCells cells)
    {
        var work = _work.FirstOrDefault(w => w.Cells == cells);
        if (work == null)
            return;
        _work.Remove(work);
        cells.Planning = null;
        Send(work, []);
    }

    /// <summary>Blocks its job broke: dug in the live state (and in the one being built over a new plan).</summary>
    public void Broken(MineJobCells cells, IReadOnlyList<MarkPos> broken)
    {
        if (broken.Count == 0)
            return;
        cells.StuckReplanned = false;
        var list = broken.ToList();
        Take(cells, s => s.Apply(list));
    }

    /// <summary>A block of a job's window seen as <paramref name="voxel"/> in the world, its job not having
    /// made it so: as the live state has it, nothing; gone, dug (and the plan made again if it was no
    /// cell the job digs); anything else, taken in and the plan made again. A stand found bad within two
    /// columns of it is tried again, once.</summary>
    public void Seen(MineJobCells cells, MarkPos cell, MineVoxel voxel)
    {
        if (cells.Live is not { } live)
            return;
        var site = live.Site;
        if (site.Solid(cell.X, cell.Y, cell.Z) == voxel.Solid && site.Rule(cell) == voxel.Rule)
            return;
        bool gone = !voxel.Solid && voxel.Rule == MineRule.Air;
        bool target = live.Remaining.Contains(cell);
        if (gone)
            Take(cells, s => s.Apply([cell]));
        else
            Take(cells, s => s.Changed(cell, voxel));
        if (gone && !target)
            live.Stale = true;
        if (_work.FirstOrDefault(w => w.Cells == cells) is { } work)
            foreach (var stand in cells.BadStands.Where(s => Math.Abs(s.X - cell.X) <= 2 && Math.Abs(s.Z - cell.Z) <= 2 && work.Retried.Add(s)).ToList())
            {
                cells.BadStands.Remove(stand);
                live.Stale = true;
            }
    }

    // A change to the live state, and to the one being built over a new plan once it is.
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
            Seen(work.Cells, new MarkPos(pos.X, pos.Y, pos.Z), work.World.Read(pos.X, pos.Y, pos.Z));
        }
    }

    private static bool In(MarkArea box, int x, int y, int z) =>
        x >= box.Min.X && x <= box.Max.X && y >= box.Min.Y && y <= box.Max.Y && z >= box.Min.Z && z <= box.Max.Z;

    private void Tick()
    {
        if (_api == null || _work.Count == 0)
            return;
        var board = EidolonJobSystem.Of(_api)?.Mining;
        // a job no longer on the board, or closed: nothing more to plan for it
        foreach (var gone in _work.Where(w => board != null && !board.Jobs.Any(j => j.Shared == w.Cells && !j.Closed)).ToList())
            Close(gone.Cells);
        _clock.Restart();
        foreach (var work in _work.ToList())
        {
            if (Spent)
                break;
            work.Spent.Start();
            work.Ticks++;
            try
            {
                Step(work);
            }
            catch (Exception e)
            {
                // A plan that fails leaves the job a plan with nothing in it, so its members report and go.
                _api.Logger.Error("[eidolon-mine] planning {0} failed: {1}", Describe(work.Area), e);
                Fail(work);
            }
            work.Spent.Stop();
        }
    }

    private bool Spent => _clock.Elapsed.TotalMilliseconds >= MinePlanner.BudgetMs;

    private void Step(Work work)
    {
        var cells = work.Cells;
        double now = Now(_api!.World);
        // the first plan: the window read, a column at a time
        if (cells.Live == null && work.Planner == null && work.Adopting == null)
        {
            work.Site ??= new MineSite(work.Area, work.Step);
            work.Loading ??= work.Site.Load(work.World).GetEnumerator();
            while (!Spent)
                if (!work.Loading.MoveNext())
                {
                    work.Loading = null;
                    StartPlan(work, work.Site, now);
                    break;
                }
            if (work.Loading != null)
                return;
        }
        if (work.Planner is { } planner)
        {
            var left = TimeSpan.FromMilliseconds(Math.Max(0.1, MinePlanner.BudgetMs - _clock.Elapsed.TotalMilliseconds));
            if (!planner.Step(left))
                return;
            work.Made = planner.Plan!;
            work.Planner = null;
            work.Pending = new MineState(work.Copy!, work.Home, cells.BadStands);
            work.Adopting = work.Pending.Adopt(work.Made).GetEnumerator();
            _api.Logger.Debug($"[eidolon-mine] planned {Describe(work.Area)}: {work.Made.Stations.Count} stations, {work.Made.OutOfReach.Count} out of reach, "
                + $"{work.Made.Held.Count} kept under those, {work.Made.Stair.Count} stair ({work.Made.WayOut.Count} way out), {work.Made.Reruns} reruns; "
                + $"{planner.Steps} slices, " + string.Join(", ", planner.Spent.Select(p => $"{p.Key} {p.Value.TotalMilliseconds:0.0} ms")));
        }
        if (work.Adopting is { } adopting)
        {
            while (!Spent)
                if (!adopting.MoveNext())
                {
                    Swap(work);
                    break;
                }
            return;
        }
        if (cells.Live is not { } live)
            return;
        if (work.Urgent || live.Stale && now - work.PlannedAt >= ReplanSeconds)
        {
            StartPlan(work, live.Site.Clone(), now);
            return;
        }
        if (now >= work.SyncAt)
            Sync(work, live, now);
    }

    private void StartPlan(Work work, MineSite site, double now)
    {
        var cells = work.Cells;
        var bad = new HashSet<PathCell>(cells.BadStands);
        var blocks = _api!.World.BlockAccessor;
        var pos = new BlockPos(work.Dimension);
        var parked = cells.Parked;
        var input = new MinePlanInput(new MineState(site, work.Home, bad), work.Home, c => cells.Skip.Contains(c) || parked.Contains(c), bad)
        {
            Roof = c => blocks.GetChunkAtBlockPos(pos.Set(c.X, c.Y, c.Z)) != null && MineOrder.Roof(blocks.GetBlock(pos)),
        };
        work.Copy = site;
        work.Planner = new MinePlanner(input);
        work.Journal = cells.Live != null ? new() : null;
        work.PlannedAt = now;
        work.Urgent = false;
        if (cells.Live != null)
            cells.Live.Stale = false;
        cells.Planning = work.Planner;
    }

    // The new plan in: what changed while it was made played over its state, which becomes the live one.
    private void Swap(Work work)
    {
        var cells = work.Cells;
        var state = work.Pending!;
        foreach (var change in work.Journal ?? [])
            change(state);
        cells.Live = state;
        cells.Plan = work.Made;
        cells.Stair.UnionWith(work.Made!.Stair);
        cells.PlanVersion++;
        cells.Planning = null;
        work.Pending = null;
        work.Adopting = null;
        work.Journal = null;
        work.Copy = null;
        work.Site = null;
        _api!.Logger.Debug($"[eidolon-mine] plan {cells.PlanVersion} for {Describe(work.Area)} in: {state.Remaining.Count} to dig; "
            + $"{work.Spent.Elapsed.TotalMilliseconds:0} ms over {work.Ticks} ticks so far");
        Send(work, MineOutlines.Boxes(cells.OutOfReach));
    }

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
        _work.Remove(work);
        cells.Planning = null;
    }

    // The area's columns and one round them, a column at a time, against the live state.
    private void Sync(Work work, MineState live, double now)
    {
        var area = work.Area;
        int sx = area.SizeX + 2, sz = area.SizeZ + 2;
        var window = live.Site.Window;
        while (!Spent)
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
