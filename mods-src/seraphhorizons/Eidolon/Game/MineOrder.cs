using System.Globalization;
using SeraphHorizons.Mod.Eidolon.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.Eidolon;

/// <summary>
/// Mine a marked area (README "Eidolon", mining; arguments the area's corners <c>minX</c> ...
/// <c>maxZ</c>, and where it stood when ordered, <c>homeX</c>, <c>homeY</c>, <c>homeZ</c>). Its job's area is
/// planned once, in slices over the server's ticks (<see cref="EidolonMinePlans"/>), as stations: a place it
/// stands and the blocks it clears from there (README "Eidolon", mining, *Stations*; <see cref="MinePlanner"/>),
/// in an order that never leaves it, or another, without a way out and never digs the floor under a block it
/// cannot reach (<see cref="MinePlan.OutOfReach"/>, told to its owner once and outlined on their command
/// tool). It takes stations from the plan as they come free (<see cref="MineDispatch.Next"/>, a short queue,
/// <see cref="MineDispatch.QueueLength"/>), each claimed on the job board, walks to its stand and strikes
/// it a group of up to four blocks at a time (<see cref="MineStations.NextGroup"/>: flat on a layer, upright
/// at a wall, a tunnel's face top half first) with the shape's slam, timed to a seraph's steel pickaxe or steel
/// shovel, the quicker for each block (<see cref="EidolonMining.DigSpeed"/>), at three quarters of their
/// pace (<see cref="EidolonMining.Timing"/>), the blocks breaking as a player's would, their drops falling
/// in the world. Each strike costs oil (<see cref="EidolonJob.MiningStrike"/>).
/// Before every strike the job's live state is asked whether it may (<see cref="MineState.Check"/>): never
/// under any eidolon's box or over it, never cutting off its own or another's way out.
///
/// <para>The land claim is asked only of a block it comes to strike; a block its owner may not break, or
/// that would not break, is left for the order's life by the whole job (<see cref="MineJobCells.Skip"/>), and
/// the plan made again round it. A column of the area not loaded is planned without (<see cref="MinePlan.Partial"/>):
/// with nothing it may dig left in the loaded part, it walks toward the nearest unloaded column (its own
/// chunks, held while it works, load ahead of it), or waits for it to load. A stand the game's pathfinder
/// finds no way to is bad (<see cref="MineJobCells.BadStands"/>), and the plan is made again without it.</para>
///
/// <para>Done when no station is left that it or another of its job may work, nothing but the stair it
/// leaves as its way out (<see cref="MinePlan.WayOut"/>) is left, and none of the area is unloaded: it tells its
/// owner how many it mined, left, could not reach, kept under those as their floor and left as its way out,
/// walks back to where it was ordered (if it can) and the order ends. Interrupted (self-defence, a slump,
/// dry), it starts again by looking afresh; interrupted on its way back, it goes on back.</para>
///
/// <para>Eidolons of one owner given the very same area work it as one job (README "Eidolon", working
/// together; <see cref="EidolonJobSystem.Mining"/>, <see cref="JobBoard{TUnit, TShared}"/>) from one plan:
/// each takes the station it would reach first that no other would reach sooner (<see cref="MineDispatch"/>),
/// claims its columns (<see cref="MineDispatch.Claim"/>: a column apart in the open, a lane each at a face),
/// and never breaks a block in another's columns at any height; one with nothing it may work waits at the
/// area's rim (<see cref="EidolonTeamwork.Rim"/>); and the last to find nothing left tells the owner what
/// each mined, and each walks back to where it was ordered. Each saves its own order, with the ids of the
/// others in its job (<c>mates</c>), which the job, formed again on load, waits for
/// (<see cref="EidolonTeamwork.ExpectSeconds"/>); how many blocks out of reach its owner was last told of
/// (<c>outofreach</c>), so a reload does not tell again; and, once it has been told the job is done,
/// <c>done</c>, so it goes on back. Plans, claims and the live state are never saved: made again on load.</para>
/// </summary>
public sealed class MineOrder(MarkArea area, Vec3d? home, IReadOnlyList<long>? mates = null, bool done = false, int toldOutOfReach = -1) : IEidolonOrder
{
    public const string OrderCode = "mine";

    /// <summary>How often it saves who it works with, and the job sweeps its area for changes
    /// (<see cref="EidolonMinePlans.SyncSeconds"/>).</summary>
    public const double RescanSeconds = 10;

    /// <summary>Stands in a row it finds no way to before it waits.</summary>
    private const int PathTries = 3;

    /// <summary>Times it sets off again for a stand it arrived short of.</summary>
    private const int Approaches = 2;

    /// <summary>Within this of where it was ordered, it is back.</summary>
    private const double HomeSlack = 1.5;

    /// <summary>Times it sets off back again when its walk back stops short of where it was ordered.</summary>
    private const int HomeTries = 3;

    /// <summary>How near its place counts as there: its box is 1.7 wide on a 2-block node, so off by
    /// more than 0.15 it stands over a third column, which it may not mine under.</summary>
    private const float ArriveWithin = 0.2f;

    /// <summary>How far past its place it aims when it steps onto it from short of it.</summary>
    private const double SettlePast = 0.15;

    /// <summary>How far short of its place it steps onto it straight, and how far above or below it
    /// (a step its physics takes, perched on the edge of what it mines, say); further off, or two up or
    /// down (a climb, a drop), it walks a path again.</summary>
    private const double SettleWithin = 1.0;
    private const int SettleRise = 1;

    /// <summary>How far at most it walks at a time toward a part of the area not loaded: well inside
    /// the columns it holds round it (<see cref="EidolonConfig.ChunkLoadRadius"/>, a chunk is 32).</summary>
    private const int TravelBlocks = 24;

    /// <summary>How far up or down from its feet it looks for ground to walk to toward an unloaded part.</summary>
    private const int TravelRise = 8;

    /// <summary>Waiting this long for a part of the area to load, with no way toward it, it waits on a
    /// player and lets its chunks go (<see cref="HasWorkLeft"/>).</summary>
    private const double StallSeconds = 30;

    /// <summary>How often a waiting eidolon looks for a station come free.</summary>
    private const double WaitCheckSeconds = 1;

    /// <summary>With blocks left that no member may work, none working, the plan made again and no other
    /// eidolon over the area, it waits this long before it calls the job done and reports them left.</summary>
    private const double StuckSeconds = 30;

    /// <summary>How far round a group (or itself) it looks for eidolons it must not dig under or over: its
    /// reach and their box's half width, with room.</summary>
    private const float OthersRange = 8;

    /// <summary>Not working, another's arrival at a station is reckoned this far off: an eidolon parked in
    /// the area (no member, or one waiting) never takes a station from it.</summary>
    private const double NeverSeconds = 1e6;

    /// <summary>How long the cells under eidolons parked in the area must stay the same before the plan is
    /// made round them.</summary>
    private const double ParkedSeconds = 3;

    /// <summary>How long after finishing a station it leaves the next to a member waiting with under half as much mined
    /// (<see cref="Yields"/>), waiting at the rim meanwhile.</summary>
    private const double YieldSeconds = 3;

    /// <summary>How long a walk to where it waits may go on before it looks again from where it got to.</summary>
    private const double WaitWalkSeconds = 10;

    /// <summary>How long at most it waits for another to make room before it walks a way through it.</summary>
    private const double ThroughSeconds = 20;

    /// <summary>How long one walk on the stair keeps it (refreshed while it walks).</summary>
    private const double StairSeconds = 30;

    private const string SlamAnimation = "slam";

    private enum Phase { Seek, Plan, Walk, Strike, Return, Travel, Wait }

    /// <summary>What came of trying to strike its station from where it stands.</summary>
    private enum Try { Struck, Dropped, NotInReach }

    private readonly MineJobCells _own = new();
    private readonly Dictionary<MarkPos, bool> _mayBreak = new();
    private readonly List<MineStation> _queue = new();
    private int _planVersion = -1;
    private double? _waitingSince;
    private double? _stuckSince;
    private bool _stalled;
    private int _maxTier = EidolonMining.SteelPickTier;
    private string[] _mineThrough = EidolonMining.GroundBlockEntities;
    private EidolonNavigator? _nav;
    private EntityLaborEidolon? _eidolon;
    private Phase _phase;
    private double _rescanAt;
    private int _approaches;
    private int _approached = -1;
    private int _pathFails;
    private bool _settled;
    private MineGroup? _group;
    private MineStrike _strike;
    private double _strikeStart;
    private bool _slammed;
    private bool _struck;
    private bool _done = done;
    private Item? _pick;
    private Item? _shovel;
    private EidolonJobSystem? _jobs;
    private SharedJob<MineJobCells>? _job;
    private IReadOnlyList<long> _mates = mates ?? [];
    private bool _expectMates = true;
    private bool _finished;
    private readonly RimWait _rim = new(area, keepClear: true);
    private double _waitCheckAt;
    private int? _band;
    private MineSector? _sector;
    private int _homeTries;
    private int _toldSaved = toldOutOfReach;
    private bool _stairWait;
    private double? _throughSince;
    private double _yieldUntil;
    private bool _headDone;
    private double? _waitWalkSince;
    private MineStop? _routeWait;
    private double? _claimBlockedSince;
    private int _routeStarted = -1;

    // when it set off for its head, and how long the walk was estimated to take (a routed walk gone on far
    // longer is given up: RouteWalkLate)
    private double? _walkSince;
    private double _walkEstimate;

    // when a routed walk last stopped short against one standing in its way (BumpSeconds)
    private double _bumpedAt = double.NegativeInfinity;
    private int _contacts;
    private double _workSeconds;
    private bool _headAhead;

    public string Code => OrderCode;

    public MarkArea Area => area;

    /// <summary>Blocks it broke.</summary>
    public int Mined { get; private set; }

    /// <summary>Strikes it struck (each breaking at least one block).</summary>
    public int Strikes { get; private set; }

    /// <summary>Slams that landed and broke nothing (never, but a block the game would not break).</summary>
    public int EmptyStrikes { get; private set; }

    /// <summary>Strikes cut short before the slam landed, nothing left in their group to break.</summary>
    public int CutStrikes { get; private set; }

    /// <summary>The columns of the station it last struck (README "Eidolon", working together, *Claims*): at a
    /// tunnel's face its lane, else the bounds of the station's claim; null before its first strike.</summary>
    public MineSector? Sector => _sector;

    /// <summary>Its job's plan (README "Eidolon", mining, *Stations*), or null while it is made.</summary>
    public MinePlan? Plan => Cells.Plan;

    /// <summary>The station it works (or walks to), or null.</summary>
    public MineStation? Station => _queue.Count > 0 ? _queue[0] : null;

    /// <summary>The stations it holds, the one it works first.</summary>
    internal IReadOnlyList<MineStation> Queue => _queue;

    /// <summary>What its job's plan leaves standing for good: out of its reach, the floor kept under those,
    /// and its stair's way out.</summary>
    public IReadOnlySet<MarkPos> Kept => Cells.Plan is { } p ? p.OutOfReach.Concat(p.Held).Concat(p.WayOut).Concat(Cells.Stair).ToHashSet() : new HashSet<MarkPos>();

    /// <summary>Every cell its job's plans left as the stair (<see cref="MineJobCells.Stair"/>).</summary>
    public IReadOnlySet<MarkPos> Stair => Cells.Stair;

    /// <summary>The tunnel planned for the area's covered part (README "Eidolon", mining, tunnels), as the
    /// band it last struck a face of (its top and bottom layers), or the whole covered part before; null for
    /// none (none covered, or not planned yet).</summary>
    public MineTunnel? Tunnel
    {
        get
        {
            if (Cells.Plan?.Tunnel is not { } t || _band is not { } b)
                return Cells.Plan?.Tunnel;
            int top = t.Top - b * EidolonTunnels.HeadingHeight;
            return t with { Top = top, Bottom = Math.Max(area.Min.Y, top - EidolonTunnels.HeadingHeight + 1) };
        }
    }

    /// <summary>Whether the station it works is at a tunnel's face.</summary>
    public bool Driving => Station?.Kind == MineStationKind.Face;

    /// <summary>What its job shares (its own, alone or with no jobs on this server).</summary>
    private MineJobCells Cells => _job?.Shared ?? _own;

    /// <summary>The group it is striking, or null.</summary>
    public MineGroup? Group => _phase == Phase.Strike ? _group : null;

    /// <summary>Blocks of its area out of its reach under every order (<see cref="MinePlan.OutOfReach"/>).</summary>
    public int Unreachable => Cells.OutOfReach.Count;

    /// <summary>The block columns of the area not loaded when its job last read them, which it has yet to mine.</summary>
    public int UnloadedColumns => Cells.Live?.Site.UnloadedColumns.Count ?? 0;

    /// <summary>Whether it still has blocks to mine (README "Eidolon", chunk loading): false once it has
    /// told its owner it is done (walking back is not work), and while it has waited
    /// <see cref="StallSeconds"/> for a part of the area to load with no way toward it.</summary>
    public bool HasWorkLeft => !_done && !_stalled;

    /// <summary>Its area, held loaded while it has work left (unless it covers too many columns).</summary>
    public IEnumerable<MarkArea> WorkAreas => [area];

    public static ITreeAttribute Args(MarkArea area, Vec3d? home)
    {
        var tree = new TreeAttribute();
        tree.SetInt("minX", area.Min.X);
        tree.SetInt("minY", area.Min.Y);
        tree.SetInt("minZ", area.Min.Z);
        tree.SetInt("maxX", area.Max.X);
        tree.SetInt("maxY", area.Max.Y);
        tree.SetInt("maxZ", area.Max.Z);
        if (home != null)
        {
            tree.SetDouble("homeX", home.X);
            tree.SetDouble("homeY", home.Y);
            tree.SetDouble("homeZ", home.Z);
        }
        return tree;
    }

    public static MineOrder From(ITreeAttribute args) => new(
        new MarkArea(new MarkPos(args.GetInt("minX"), args.GetInt("minY"), args.GetInt("minZ")),
            new MarkPos(args.GetInt("maxX"), args.GetInt("maxY"), args.GetInt("maxZ"))),
        args.HasAttribute("homeX") ? new Vec3d(args.GetDouble("homeX"), args.GetDouble("homeY"), args.GetDouble("homeZ")) : null,
        (args.GetString("mates") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(m => long.TryParse(m, NumberStyles.Integer, CultureInfo.InvariantCulture, out long id) ? id : 0).Where(id => id != 0).ToList(),
        args.GetBool("done"),
        args.HasAttribute("outofreach") ? args.GetInt("outofreach") : -1);

    /// <summary>The report it made to its owner, in the server's language, if it made its job's (the
    /// last of its job to finish makes it; the others none).</summary>
    public string? Reported { get; private set; }

    /// <summary>What it told its owner of the blocks out of its reach, in the server's language, if it was
    /// the one of its job that told them (the last time).</summary>
    public string? ToldOutOfReach { get; private set; }

    /// <summary>The shared job it is in (README "Eidolon", working together), or null before it starts.</summary>
    public SharedJob<MineJobCells>? Job => _job;

    private EidolonMinePlans? Plans => EidolonMinePlans.Of(_eidolon?.Api);

    public void Start(EntityLaborEidolon eidolon)
    {
        _nav ??= new EidolonNavigator(eidolon);
        _eidolon = eidolon;
        _pick ??= eidolon.World.GetItem(new AssetLocation(EidolonMining.PickCode));
        _shovel ??= eidolon.World.GetItem(new AssetLocation(EidolonMining.ShovelCode));
        _maxTier = EidolonMining.PickTier(_pick?.ToolTier);
        _mineThrough = (EidolonSystem.Of(eidolon.Api)?.Config ?? EidolonConfig.Defaults).MineThroughBlockEntities;
        _jobs ??= EidolonJobSystem.Of(eidolon.Api);
        _group = null;
        _rescanAt = 0;
        _queue.Clear();
        _headDone = false;
        _rim.Leave(eidolon, _jobs);
        if (_done)
            GoHome(eidolon);
        else
        {
            Join(eidolon);
            _phase = Phase.Seek;
            // back (from dry, or loaded again) to a job reported while it was away: back to its place,
            // with no report of its own
            if (_job is { Closed: true })
            {
                Told(eidolon);
                GoHome(eidolon);
            }
            else
                Ensure(eidolon);
        }
    }

    public bool Continue(EntityLaborEidolon eidolon, float dt)
    {
        double now = eidolon.World.ElapsedMilliseconds / 1000.0;
        // its walk on the stair over (or none): the stair is the others' again
        if (Cells.StairHolder == eidolon.EntityId)
        {
            if (_nav?.Active == true)
                Cells.StairUntil = now + StairSeconds;
            else
                Cells.StairHolder = 0;
        }
        switch (_phase)
        {
            case Phase.Seek:
                return Seek(eidolon, now);
            case Phase.Plan:
                if (_job is { Closed: true })
                {
                    Told(eidolon);
                    return GoHome(eidolon);
                }
                if (Cells.Plan == null || Cells.Live == null)
                {
                    Ensure(eidolon);
                    return true;
                }
                _phase = Phase.Seek;
                return Seek(eidolon, now);
            case Phase.Walk:
                // a new plan: the station it walks to kept if the new one has it, else it looks again
                if (_planVersion != Cells.PlanVersion && (Cells.Plan is not { } replanned || !Remap(eidolon, replanned)) || Station is not { } head)
                {
                    _nav!.Stop();
                    _phase = Phase.Seek;
                    return true;
                }
                if (_nav!.Active)
                {
                    // routed, walking into one standing that its box already touches: it stops short and waits
                    // (Approach) rather than push on
                    if (Routed(eidolon) && WalksInto(eidolon))
                    {
                        _nav.Stop();
                        _bumpedAt = now;
                        _phase = Phase.Seek;
                        return true;
                    }
                    if (Routed(eidolon) && _walkSince is { } since && now - since > 2 * _walkEstimate + RouteWalkLateSeconds)
                    {
                        _nav.Stop();
                        _walkSince = null;
                        DropQueue(eidolon);
                        Repaired(Cells.Routes!.Drop(eidolon.EntityId, head.Id, MineRefusal.None, now) ?? "late");
                        _phase = Phase.Seek;
                        return true;
                    }
                    // its stations kept while it walks to them, however long the way
                    Board?.Struck(eidolon.EntityId, now);
                    return true;
                }
                return Arrived(eidolon, head, now);
            case Phase.Strike:
                return Strike(eidolon, now);
            case Phase.Return:
                // waiting for the stair to come free to climb out by
                if (_stairWait)
                    return GoHome(eidolon);
                // stopped short (stuck, or off an edge into the pit): it sets off again, a few times
                return _nav!.Active || ++_homeTries <= HomeTries && GoHome(eidolon);
            case Phase.Travel:
                if (_nav!.Active)
                    return true;
                // there (or as far as it got): look again at once
                _phase = Phase.Seek;
                _waitingSince = null;
                return true;
            case Phase.Wait:
                // Another member made the report: back to where it was ordered.
                if (_finished && _job is { Closed: true })
                {
                    Told(eidolon);
                    return GoHome(eidolon);
                }
                if (now < _waitCheckAt)
                    return true;
                // making for where it waits: it looks again only once there, or once that walk has gone on
                // too long (stuck, or giving way for ever in a narrow pit)
                if (_nav!.Active)
                {
                    _waitWalkSince ??= now;
                    if (now - _waitWalkSince < WaitWalkSeconds)
                        return true;
                    _nav.Stop();
                }
                _waitWalkSince = null;
                _waitCheckAt = now + WaitCheckSeconds;
                if (_finished && Board?.TryClose(eidolon.EntityId, now) == true)
                {
                    _jobs!.SaveDone(_job!);
                    return Report(eidolon);
                }
                _phase = Phase.Seek;
                return Seek(eidolon, now);
        }
        return true;
    }

    public void Stop(EntityLaborEidolon eidolon, bool cancelled)
    {
        Halt(eidolon);
        if (Routed(eidolon) && Cells.Live is { } live)
            Repaired(Cells.Routes!.Gone(eidolon.EntityId, EidolonJobSystem.Now(eidolon.World), live));
        if (Cells.StairHolder == eidolon.EntityId)
            Cells.StairHolder = 0;
        _queue.Clear();
        // Its columns are freed at once; it leaves its job when done, and is away from it meanwhile
        // (the server's sweep takes it out if the order was replaced).
        if (_done)
            Board?.Leave(eidolon.EntityId);
        else
            Board?.Away(eidolon.EntityId);
        _rim.Leave(eidolon, _jobs);
        _phase = Phase.Seek;
    }

    private JobBoard<(int X, int Z), MineJobCells>? Board => _jobs?.Mining;

    /// <summary>Joins (or rejoins) its job: the one of its owner's eidolons over the very same area, or
    /// a new one. The first time, it brings the ids of the others it worked with (saved with the
    /// order), for the job to wait for while they load.</summary>
    private void Join(EntityLaborEidolon eidolon)
    {
        if (_jobs is not { } jobs)
            return;
        long id = eidolon.EntityId;
        // A job closed while it was away is rejoined (closed: it goes back) unless the order is new (a
        // player's, with no mates saved): one ordered again to the same area starts a new job.
        _job = jobs.Mining.Join(id, id.ToString(CultureInfo.InvariantCulture), EidolonJobSystem.KeyOf(eidolon, OrderCode, area),
            _expectMates ? _mates : null, EidolonJobSystem.Now(eidolon.World) + EidolonTeamwork.ExpectSeconds,
            rejoinClosed: !_expectMates || _mates.Count > 0);
        _expectMates = false;
        jobs.Track(jobs.Mining, eidolon, this);
        _finished = false;
        jobs.Mining.SetFinished(id, false);
    }

    /// <summary>Has its job's plan made if it is not (the first member to start does: <see cref="EidolonMinePlans.Ensure"/>).</summary>
    private void Ensure(EntityLaborEidolon eidolon)
    {
        if (Cells.Live != null && Cells.Plan != null)
            return;
        var h = home ?? eidolon.Pos.XYZ;
        Plans?.Ensure(Cells, area, WidePath.NodeAt(h.X, h.Y, h.Z), eidolon, _pick, _maxTier, _mineThrough, _shovel);
    }

    /// <summary>Saves the ids of the others in its job with its order, when they changed.</summary>
    private void SaveMates(EntityLaborEidolon eidolon)
    {
        if (_job == null)
            return;
        var mates = _job.Members.Where(m => m != eidolon.EntityId).ToList();
        if (mates.SequenceEqual(_mates))
            return;
        _mates = mates;
        Save(eidolon, args => args.SetString("mates", string.Join(",", mates.Select(m => m.ToString(CultureInfo.InvariantCulture)))));
    }

    // Writes into the order's arguments (saved with the world).
    private void Save(EntityLaborEidolon eidolon, Action<ITreeAttribute> write)
    {
        if (eidolon.Orders is not { } orders || !ReferenceEquals(orders.Current, this) || orders.OrderArgs is not { } args)
            return;
        write(args);
        eidolon.WatchedAttributes.MarkPathDirty(EntityBehaviorEidolonOrders.OrderKey);
    }

    /// <summary>The other eidolons loaded within <see cref="OthersRange"/> of a point, where each stands,
    /// its feet' block and whether it walks: it digs in none of their columns.</summary>
    private static List<(double X, int Feet, double Z, bool Walking)> Others(EntityLaborEidolon eidolon, Vec3d at,
        float range = OthersRange, float rise = OthersRange) =>
        eidolon.World.GetEntitiesAround(at, range, rise, e => e is EntityLaborEidolon && e.EntityId != eidolon.EntityId && e.Alive)
            .Select(e => (e.Pos.X, Feet(e), e.Pos.Z, Math.Sqrt(e.Pos.Motion.X * e.Pos.Motion.X + e.Pos.Motion.Z * e.Pos.Motion.Z) >= EidolonNavigator.StandingMotion))
            .ToList();

    /// <summary>
    /// Every other eidolon loaded over the area, as dispatch sees it (<see cref="MineWorker"/>): a member of
    /// its job at work with the stations it holds; any other (a member waiting, one of another job, or none
    /// at all, parked in the area) holding none and never arriving anywhere first, but still one the guard
    /// keeps a way out for and digs no column of.
    /// </summary>
    private List<MineWorker> Workers(EntityLaborEidolon eidolon)
    {
        var centre = new Vec3d((area.Min.X + area.Max.X + 1) / 2.0, (area.Min.Y + area.Max.Y + 1) / 2.0, (area.Min.Z + area.Max.Z + 1) / 2.0);
        float range = area.LongestSide / 2f + OthersRange;
        var workers = new List<MineWorker>();
        foreach (var e in eidolon.World.GetEntitiesAround(centre, range, area.SizeY / 2f + OthersRange,
                     e => e is EntityLaborEidolon && e.EntityId != eidolon.EntityId && e.Alive))
        {
            var queue = e is EntityLaborEidolon { Orders.Current: MineOrder o } && _job != null && o._job == _job ? o._queue.ToList() : [];
            workers.Add(new MineWorker(e.EntityId, e.Pos.X, Feet(e), e.Pos.Z, queue, queue.Count > 0 ? 0 : NeverSeconds));
        }
        return workers;
    }

    private static List<MineWorkerAt> At(IEnumerable<MineWorker> workers) =>
        workers.Select(w => new MineWorkerAt(w.Id, w.X, w.Feet, w.Z, w.Queue.Select(s => s.Stand).ToList())).ToList();

    // ---- look-ahead routes (README "Eidolon", working together, *Routes*) ----

    /// <summary>How long it waits for its route's station to come free to claim before it drops the stop
    /// (the job's routes are solved again).</summary>
    private const double RouteClaimSeconds = 2;

    /// <summary>How long it waits for another eidolon in its way before it passes it, within its share of
    /// passes (<see cref="MayPass"/>).</summary>
    private const double PassSeconds = 2;

    /// <summary>Seconds of strikes for each pass of another eidolon it may make (and one more).</summary>
    private const double PassEach = 120;

    /// <summary>Seconds over twice its estimate after which a routed member's walk to its head is given up: the
    /// stop dropped and the routes solved again, so that the others are not held to their turns behind a walk
    /// that is getting nowhere (a stand the game finds a long way round to, or keeps giving way on).</summary>
    private const double RouteWalkLateSeconds = 6;

    /// <summary>How long a routed member that walked into one standing waits before it sets off again (the
    /// game's way round a standing one can be too tight: it brushes it rather than push on).</summary>
    private const double BumpSeconds = 3;

    /// <summary>Whether it works by its job's routes now: routing on, a job of more than one, routes made for
    /// the station plan in use, not left to today's dispatch, and a route of its own.</summary>
    private bool Routed(EntityLaborEidolon eidolon) =>
        EidolonMinePlans.Routing && _job != null && Cells.Routes is { Today: false } r && r.PlanVersion == Cells.PlanVersion
        && r.Routes.ContainsKey(eidolon.EntityId);

    /// <summary>The present members of a mining job as the route solver takes them (README "Eidolon", working
    /// together, *Routes*): the node each stands at, the station it walks to or strikes (its pinned head), and
    /// when it will be done with it (the walk estimated, the work at its blocks' pace).</summary>
    internal static List<MineRouteMember> RouteMembers(SharedJob<MineJobCells> job, EidolonJobSystem jobs, double now)
    {
        var members = new List<MineRouteMember>();
        var live = job.Shared.Live;
        foreach (long id in job.Present.Order())
        {
            if (jobs.Member(id) is not { } e || e.Orders?.Current is not MineOrder o)
                continue;
            var at = WidePath.NodeAt(e.Pos.X, e.Pos.Y, e.Pos.Z);
            int? head = null;
            double free = now;
            // Whatever station it holds is pinned, whatever it is doing (a held station is claimed and its own).
            if (o.Station is { } st && live != null)
            {
                head = st.Id;
                var left = st.Cells.Where(live.Remaining.Contains).ToList();
                free = now + (o._phase == Phase.Strike ? 0 : MineCosts.Estimate(at, st.Stand) + MineCosts.ArriveSeconds) + MineCosts.WorkSeconds(live.Site, left);
            }
            members.Add(new MineRouteMember(id, at, head, head != null && o._headAhead, free));
        }
        return members;
    }

    /// <summary>A repair of its job's routes done: the routes counted changed, and a solve asked for if the
    /// repair wants one.</summary>
    private void Repaired(string? why)
    {
        Cells.RoutesVersion++;
        if (why != null)
            EidolonMinePlans.RequestRoutes(Cells, why, why is "empty" or "plan" or "deadlock");
    }

    /// <summary>
    /// Holds its route's head (README "Eidolon", working together, *Routes*) in place of what dispatch would
    /// give it: a stop with nothing left is finished, a planned wait waited (ended at once when the station
    /// after it is ready), and a station claimed when it may set off: at its planned time, or sooner when the
    /// station is ready for it and the plan has no other set off first there (<see cref="Ready"/>); a station
    /// whose claim stays taken for <see cref="RouteClaimSeconds"/> is dropped and the routes solved again.
    /// With no stop at all it has none, and asks for routes once (its job's stations may be all others').
    /// </summary>
    private void RefillRoute(EntityLaborEidolon eidolon, MinePlan plan, MineState live, List<MineWorker> workers, double now)
    {
        var routes = Cells.Routes!;
        long id = eidolon.EntityId;
        var board = Board;
        _routeWait = null;
        for (int guard = 0; guard < 16; guard++)
        {
            var head = routes.Head(id);
            if (head == null)
            {
                if (_queue.Count == 0 && routes.Unrouted(live))
                    EidolonMinePlans.RequestRoutes(Cells, "empty", true);
                return;
            }
            if (head.Kind == MineStopKind.Wait)
            {
                if (now >= head.Finish || routes.Next(id) is { Kind: MineStopKind.Station } next && next.Station < plan.Stations.Count
                    && Ready(eidolon, next, plan, live, workers, now))
                {
                    Repaired(routes.Waited(id, now));
                    continue;
                }
                _routeWait = head;
                return;
            }
            if (head.Station >= plan.Stations.Count)
            {
                Repaired(routes.Drop(id, head.Station, MineRefusal.None, now));
                continue;
            }
            var station = plan.Stations[head.Station];
            var cells = (head.Cells.Count > 0 ? head.Cells : station.Cells).Where(live.Remaining.Contains).ToList();
            if (cells.Count == 0)
            {
                if (_queue.Count > 0 && _queue[0].Id == station.Id)
                    DropQueue(eidolon);
                Repaired(routes.Finished(id, station.Id, now, live));
                continue;
            }
            if (_queue.Count > 0 && _queue[0].Id == station.Id)
            {
                // The route holds one station at a time: any dispatch queued behind it is let go.
                DropQueueAfterHead(eidolon);
                return;
            }
            DropQueue(eidolon);
            if (now < head.Depart && !Ready(eidolon, head, plan, live, workers, now))
            {
                _routeWait = head;
                return;
            }
            var fit = MineDispatch.Fit(station with { Cells = cells }, plan.Tunnel, u => board == null || board.Free(id, u, now));
            if (fit != null && !Taken(fit.Stand, workers) && (board == null || board.TryClaim(id, fit.Id, MineDispatch.Claim(fit, plan.Tunnel), now)))
            {
                _queue.Add(fit);
                _headAhead = head.Ahead;
                _claimBlockedSince = null;
                return;
            }
            _claimBlockedSince ??= now;
            if (now - _claimBlockedSince > RouteClaimSeconds)
            {
                _claimBlockedSince = null;
                Repaired(routes.Drop(id, station.Id, MineRefusal.None, now));
                continue;
            }
            _routeWait = head;
            return;
        }
    }

    /// <summary>Whether its route's station <paramref name="stop"/> may be set off for now, ahead of its
    /// planned time (the world ran ahead of the plan): cells it may dig, its stand out, its claim free, no one
    /// at its stand, and no other member's stop the plan sets off first whose claim or stand meets it.</summary>
    private bool Ready(EntityLaborEidolon eidolon, MineStop stop, MinePlan plan, MineState live, List<MineWorker> workers, double now)
    {
        var station = plan.Stations[stop.Station];
        var cells = (stop.Cells.Count > 0 ? stop.Cells : station.Cells).Where(live.Remaining.Contains).ToList();
        if (cells.Count == 0 || MineDispatch.Cells(live, station with { Cells = cells }, stop.Ahead).Count == 0 || !live.Walk.Out(station.Stand))
            return false;
        long id = eidolon.EntityId;
        var board = Board;
        if (MineDispatch.Fit(station with { Cells = cells }, plan.Tunnel, u => board == null || board.Free(id, u, now)) is not { } fit || Taken(fit.Stand, workers))
            return false;
        var claim = MineDispatch.Claim(fit, plan.Tunnel).ToHashSet();
        foreach (var route in Cells.Routes!.Routes.Values.Where(r => r.Member != id))
        foreach (var other in route.Stops.Where(x => x.Kind == MineStopKind.Station).Take(2))
            if (other.Depart < stop.Depart && other.Station < plan.Stations.Count
                && (MineDispatch.Claim(plan.Stations[other.Station] with { Cells = other.Cells.Count > 0 ? other.Cells : plan.Stations[other.Station].Cells }, plan.Tunnel).Any(claim.Contains)
                    || Math.Abs(other.At.X - station.Stand.X) < BoxWidth + 0.3 && Math.Abs(other.At.Z - station.Stand.Z) < BoxWidth + 0.3 && Math.Abs(other.At.Y - station.Stand.Y) < 4))
                return false;
        return true;
    }

    /// <summary>Waits as its route plans (a wait, or its next station's time not come): at the planned place
    /// for a planned wait (walked to if it is not there), else where it is, out of no one's way the plan
    /// knows of, stepping aside for another walking up to it; its claims let go meanwhile.</summary>
    private bool RouteWait(EntityLaborEidolon eidolon, MineStop stop, double now)
    {
        DropQueue(eidolon);
        _rim.Leave(eidolon, _jobs);
        _phase = Phase.Wait;
        _waitCheckAt = now + 0.5;
        // another walking up to where it stands: it steps aside to a place clear of the work left, rather
        // than stand in that one's way (the plan knows nothing of where the others walk between its stops)
        if (!_nav!.Active && Others(eidolon, eidolon.Pos.XYZ, 3, 3).Any(o => o.Walking
            && Math.Abs(o.X - eidolon.Pos.X) < BoxWidth + 1 && Math.Abs(o.Z - eidolon.Pos.Z) < BoxWidth + 1))
            _rim.Aside(eidolon, _nav, _jobs, AsideClear(eidolon.EntityId, now));
        if (stop.Kind == MineStopKind.Wait && !_nav.Active)
        {
            var place = stop.At;
            double dx = place.X - eidolon.Pos.X, dz = place.Z - eidolon.Pos.Z;
            if (dx * dx + dz * dz > 1.5 * 1.5 || Math.Abs(Feet(eidolon) - place.Y) > 1)
                _nav.GoTo(new Vec3d(place.X, place.Y, place.Z), false, () => { }, () => { }, arriveWithin: ArriveWithin);
        }
        eidolon.Orders?.SetStatus("seraphhorizons:eidolon-status-mine-waiting-route");
        return true;
    }

    /// <summary>Whether the station it holds is its route's stop worked ahead of its band.</summary>
    private bool Ahead(EntityLaborEidolon eidolon) => _headAhead && _queue.Count > 0 && Routed(eidolon);

    /// <summary>Whether it may pass another eidolon in its way (README "Eidolon", working together, *Routes*):
    /// within its share (one each <see cref="PassEach"/> seconds of strikes, and one more), and none of those
    /// near it on the stair (a pass there could push one off it).</summary>
    private bool MayPass(EntityLaborEidolon eidolon)
    {
        if (_contacts + 1 > 1 + _workSeconds / PassEach)
            return false;
        var others = Others(eidolon, eidolon.Pos.XYZ, 6, 4);
        // none on the stair, and none standing near it: one that stands (waiting, at work) is pushed
        // through, not slid past, which is no brief contact
        return !others.Any(o => OnStair(o.X, o.Feet, o.Z)
            || !o.Walking && Math.Abs(o.X - eidolon.Pos.X) < 3 && Math.Abs(o.Z - eidolon.Pos.Z) < 3 && Math.Abs(o.Feet - Feet(eidolon)) < 3);
    }

    /// <summary>Whether it walks into another eidolon standing still whose box its own touches.</summary>
    private static bool WalksInto(EntityLaborEidolon eidolon)
    {
        double mx = eidolon.Pos.Motion.X, mz = eidolon.Pos.Motion.Z;
        if (Math.Sqrt(mx * mx + mz * mz) < EidolonNavigator.StandingMotion)
            return false;
        return Others(eidolon, eidolon.Pos.XYZ, 3, 2).Any(o => !o.Walking
            && Math.Abs(o.X - eidolon.Pos.X) < BoxWidth + 0.1 && Math.Abs(o.Z - eidolon.Pos.Z) < BoxWidth + 0.1
            && Math.Abs(o.Feet - Feet(eidolon)) < 2
            && (o.X - eidolon.Pos.X) * mx + (o.Z - eidolon.Pos.Z) * mz > 0);
    }

    private bool Seek(EntityLaborEidolon eidolon, double now)
    {
        // Waiting for a part of the area to load: look again only now and then.
        if (_waitingSince != null && now < _rescanAt)
            return true;
        var cells = Cells;
        if (cells.Plan is not { } plan || cells.Live is not { } live)
        {
            Ensure(eidolon);
            _phase = Phase.Plan;
            eidolon.Orders?.SetStatus("seraphhorizons:eidolon-status-mine-planning");
            return true;
        }
        if (now >= _rescanAt)
        {
            eidolon.Orders?.SetStatus("seraphhorizons:eidolon-status-mine-seeking");
            SaveMates(eidolon);
            _rescanAt = now + RescanSeconds;
        }
        TellOutOfReach(eidolon, plan);
        if (_planVersion != cells.PlanVersion)
            Remap(eidolon, plan);
        var workers = Workers(eidolon);
        var at = At(workers);
        // what it holds that is done, or no longer its to work now (another stands there, the world changed)
        while (_queue.Count > 0 && !MineDispatch.Executable(plan, live, Usable(_queue[0], workers), at, out _, ParkedClear(workers), Ahead(eidolon)))
            DropHead(eidolon);
        if (_queue.Count == 0 && Yields(eidolon, workers, now))
        {
            // just done with a station, another of its job waiting with less to show: its turn to take one,
            // and this one makes room, off to the rim like any other waiting
            Unfinish(eidolon);
            return Wait(eidolon, now);
        }
        Refill(eidolon, plan, live, workers, now);
        if (Station is not { } head)
            return _routeWait is { } planned && Routed(eidolon) ? RouteWait(eidolon, planned, now) : Idle(eidolon, live, workers, now);
        _waitingSince = null;
        _stalled = false;
        _stuckSince = null;
        Unfinish(eidolon);
        if (head.Id != _approached)
        {
            _approached = head.Id;
            _approaches = 0;
            _settled = false;
        }
        if (AtStand(eidolon, head.Stand))
            return Arrived(eidolon, head, now);
        return Approach(eidolon, head, now);
    }

    /// <summary>Whether a cell is in no column of an eidolon that holds no station (parked in the area), or
    /// null with none.</summary>
    private static System.Func<MarkPos, bool>? ParkedClear(IReadOnlyList<MineWorker> workers)
    {
        var parked = workers.Where(w => w.Queue.Count == 0).ToList();
        return parked.Count == 0 ? null : c => !parked.Any(w => EidolonMining.UnderFoot(w.X, w.Z, c));
    }

    /// <summary>The station cut to the cells in no column of an eidolon that holds no station (parked in the
    /// area): those wait for it to move, the rest are dug round it.</summary>
    private static MineStation Usable(MineStation station, IReadOnlyList<MineWorker> workers)
    {
        var parked = workers.Where(w => w.Queue.Count == 0).ToList();
        if (parked.Count == 0 || !station.Cells.Any(c => parked.Any(w => EidolonMining.UnderFoot(w.X, w.Z, c))))
            return station;
        var cells = station.Cells.Where(c => !parked.Any(w => EidolonMining.UnderFoot(w.X, w.Z, c))).ToList();
        return station with { Cells = cells };
    }

    /// <summary>Takes stations until it holds <see cref="MineDispatch.QueueLength"/> (<see cref="MineDispatch.Next"/>),
    /// each claimed on the board (all of its columns or none); only one while another member of its job holds
    /// none, so the next is left for that one (a second held lets go of).</summary>
    private void Refill(EntityLaborEidolon eidolon, MinePlan plan, MineState live, List<MineWorker> workers, double now)
    {
        _routeWait = null;
        if (Routed(eidolon))
        {
            RefillRoute(eidolon, plan, live, workers, now);
            return;
        }
        if (_queue.Count == 0)
            _headAhead = false;
        long id = eidolon.EntityId;
        var board = Board;
        var parked = workers.Where(w => w.Queue.Count == 0).ToList();
        bool Clear(MarkPos c) => !parked.Any(w => EidolonMining.UnderFoot(w.X, w.Z, c));
        int length = _job != null && parked.Any(w => _job.Present.Contains(w.Id)) ? 1 : MineDispatch.QueueLength;
        while (_queue.Count > Math.Max(1, length))
        {
            board?.Unclaim(id, _queue[^1].Id);
            _queue.RemoveAt(_queue.Count - 1);
        }
        while (_queue.Count < length)
        {
            var me = new MineWorker(id, eidolon.Pos.X, Feet(eidolon), eidolon.Pos.Z, _queue.ToList(), 0);
            var next = MineDispatch.Next(plan, live, me, workers,
                s => !Taken(s.Stand, workers) && (board == null || MineDispatch.Claim(s, plan.Tunnel).All(u => board.Free(id, u, now))),
                parked.Count > 0 ? Clear : null, s => board == null ? s : MineDispatch.Fit(s, plan.Tunnel, u => board.Free(id, u, now)));
            if (next == null || board != null && !board.TryClaim(id, next.Id, MineDispatch.Claim(next, plan.Tunnel), now))
                break;
            _queue.Add(next);
        }
    }

    /// <summary>Whether it leaves the next station to another member of its job (README "Eidolon", working
    /// together, *Dispatch*): within <see cref="YieldSeconds"/> of finishing one, while a member present holds
    /// none and has mined less than half as much. In an area too small for two stations at once the one at work would
    /// otherwise always be nearer the next, and the other never get one.</summary>
    private bool Yields(EntityLaborEidolon eidolon, IReadOnlyList<MineWorker> workers, double now)
    {
        // never down a pit with a stair: making room there is the whole stair up and the other's down; nor by
        // routes, whose balance is the job's finish
        if (_job == null || Routed(eidolon) || now >= _yieldUntil || Cells.Plan is { Stair.Count: > 0 } && Feet(eidolon) < area.Max.Y + 1 - EidolonTeamwork.FloorStep)
            return false;
        var shares = _job.Shares.ToDictionary(s => s.Member, s => s.Count);
        int mine = shares.GetValueOrDefault(eidolon.EntityId);
        return workers.Any(w => w.Queue.Count == 0 && _job.Present.Contains(w.Id) && 2 * shares.GetValueOrDefault(w.Id) < mine);
    }

    /// <summary>Whether another eidolon stands at <paramref name="stand"/>, their boxes overlapping: not a
    /// place to make for now (the plan knows nothing of eidolons; one parked there moves in time).</summary>
    private static bool Taken(PathCell stand, IReadOnlyList<MineWorker> workers) =>
        workers.Any(w => w.Queue.Count == 0 && Math.Abs(w.X - stand.X) < BoxWidth && Math.Abs(w.Z - stand.Z) < BoxWidth && Math.Abs(w.Feet - stand.Y) < 4);

    private const double BoxWidth = 2 * EidolonMining.HalfWidth;

    /// <summary>
    /// A new plan swapped in: each station it holds is kept as the new plan's station at the same stand, of the
    /// same kind, that has cells of it still to dig (its claim moved to that one's), else let go; true when
    /// the one it works was kept (README "Eidolon", working together, *Dispatch*: a replan does not turn
    /// back one already on its way).
    /// </summary>
    private bool Remap(EntityLaborEidolon eidolon, MinePlan plan)
    {
        _planVersion = Cells.PlanVersion;
        if (_queue.Count == 0)
            return false;
        var live = Cells.Live;
        var board = Board;
        long id = eidolon.EntityId;
        double now = EidolonJobSystem.Now(eidolon.World);
        var held = _queue.ToList();
        bool headKept = false;
        DropQueue(eidolon);
        foreach (var old in held)
        {
            var mapped = plan.Stations.FirstOrDefault(s => s.Stand == old.Stand && s.Kind == old.Kind
                && s.Cells.Any(c => live?.Remaining.Contains(c) != false && old.Cells.Contains(c)));
            if (mapped == null || (board != null && !board.TryClaim(id, mapped.Id, MineDispatch.Claim(mapped, plan.Tunnel), now)))
                break;
            _queue.Add(mapped);
            if (old == held[0])
                headKept = true;
        }
        return headKept;
    }

    /// <summary>Lets go of the station it works (its claim freed).</summary>
    private void DropHead(EntityLaborEidolon eidolon)
    {
        if (_queue.Count == 0)
            return;
        var head = _queue[0];
        Board?.Unclaim(eidolon.EntityId, head.Id);
        _queue.RemoveAt(0);
        // its route's stop finished (nothing left of it) or dropped (refused, a bad stand)
        if (Routed(eidolon) && Cells.Live is { } live)
        {
            double now = EidolonJobSystem.Now(eidolon.World);
            var routes = Cells.Routes!;
            Repaired(head.Cells.Any(live.Remaining.Contains)
                ? routes.Drop(eidolon.EntityId, head.Id, MineRefusal.None, now)
                : routes.Finished(eidolon.EntityId, head.Id, now, live));
        }
    }

    /// <summary>Lets go of every station it holds.</summary>
    /// <summary>Lets go of every station it holds but its head.</summary>
    private void DropQueueAfterHead(EntityLaborEidolon eidolon)
    {
        for (int i = 1; i < _queue.Count; i++)
            Board?.Unclaim(eidolon.EntityId, _queue[i].Id);
        if (_queue.Count > 1)
            _queue.RemoveRange(1, _queue.Count - 1);
    }

    private void DropQueue(EntityLaborEidolon eidolon)
    {
        foreach (var s in _queue)
            Board?.Unclaim(eidolon.EntityId, s.Id);
        _queue.Clear();
    }

    /// <summary>
    /// Nothing it may take now. Only the stair's way out (or nothing) left: done (<see cref="Finish"/>), or it
    /// makes for a part not loaded (<see cref="Travel"/>). Blocks left that no member may work, none at work:
    /// the plan is made again at once, once (<see cref="EidolonMinePlans.Replan"/>), and then, with nothing
    /// changed for <see cref="StuckSeconds"/> and no other eidolon over the area, done with them left; with
    /// another at work or parked over the area, it waits at the rim.
    /// </summary>
    private bool Idle(EntityLaborEidolon eidolon, MineState live, List<MineWorker> workers, double now)
    {
        var cells = Cells;
        bool unloaded = live.Site.UnloadedColumns.Count > 0;
        bool working = workers.Any(w => w.Queue.Count > 0);
        // Eidolons parked in the area hold up what is under them: with none at work, the plan is made again
        // round what is under them now (or with it, once they have moved), whenever that changes.
        if (!working)
        {
            // not a member: one waiting moves on by itself, and one stepped aside deep in a pit would hold
            // the whole pit under it
            var parked = workers.Where(w => w.Queue.Count == 0 && _job?.Present.Contains(w.Id) != true).ToList();
            var under = cells.Parked.Concat(live.Remaining).Where(c => parked.Any(w => EidolonMining.UnderFoot(w.X, w.Z, c))).ToHashSet();
            if (!under.SetEquals(cells.Parked))
            {
                // only once it has stayed so a while: one crossing the area stirs no plan
                if (!under.SetEquals(cells.ParkedSeen))
                {
                    cells.ParkedSeen = under;
                    cells.ParkedSince = now;
                }
                else if (now - cells.ParkedSince >= ParkedSeconds)
                {
                    cells.Parked = under;
                    Plans?.Replan(cells, false);
                    eidolon.World.Logger.Debug($"[eidolon-mine] {eidolon.EntityId} on {EidolonMinePlans.Describe(area)}: {under.Count} blocks under eidolons parked there, planned again");
                }
                Unfinish(eidolon);
                return Wait(eidolon, now);
            }
        }
        bool left = live.Bands is { } bands ? !bands.StairOpen : live.Remaining.Count > 0;
        if (!left && cells.Parked.Count > 0)
        {
            // what is left is under eidolons parked in the area: it waits for them to move
            Unfinish(eidolon);
            return Wait(eidolon, now);
        }
        if (!left)
            return unloaded ? Travel(eidolon, now) : Finish(eidolon, now);
        if (!working)
        {
            if (!cells.StuckReplanned)
            {
                cells.StuckReplanned = true;
                Plans?.Replan(cells, true);
                eidolon.World.Logger.Debug($"[eidolon-mine] {eidolon.EntityId} stuck on {EidolonMinePlans.Describe(area)} with {live.Remaining.Count} left: planned again");
            }
            else
            {
                if (unloaded)
                    return Travel(eidolon, now);
                _stuckSince ??= now;
                if (now - _stuckSince >= StuckSeconds && !workers.Any(w => OverArea(w.X, w.Z)))
                    return Finish(eidolon, now);
            }
        }
        else
            _stuckSince = null;
        Unfinish(eidolon);
        return Wait(eidolon, now);
    }

    private bool OverArea(double x, double z)
    {
        var f = EidolonMining.Footprint(x, z);
        return f.MaxX >= area.Min.X && f.MinX <= area.Max.X && f.MaxZ >= area.Min.Z && f.MinZ <= area.Max.Z;
    }

    /// <summary>Whether it stands at <paramref name="stand"/>: the node its feet are nearest, at its height.</summary>
    private static bool AtStand(EntityLaborEidolon eidolon, PathCell stand) => WidePath.NodeAt(eidolon.Pos.X, eidolon.Pos.Y, eidolon.Pos.Z) == stand;

    /// <summary>
    /// At (or near) its station's stand: strikes its next group (<see cref="StrikeAt"/>); with none in reach
    /// from where it stands (arrived short of the place, on a ledge's edge, its box over the block beside),
    /// it steps onto it, straight, once, then sets off for it again, a few times, before it calls the stand
    /// bad (<see cref="BadStand"/>).
    /// </summary>
    private bool Arrived(EntityLaborEidolon eidolon, MineStation head, double now)
    {
        switch (StrikeAt(eidolon, head, now))
        {
            case Try.Struck:
                return true;
            case Try.Dropped:
                _phase = Phase.Seek;
                return true;
        }
        // Aimed a little past it, as the steer stops 0.2 short, so it ends within 0.15, its box over the
        // place's own columns. Only from close by and at most a block above or below it: the steer never
        // climbs, and must not walk it off a drop, so from anywhere else it walks a path again.
        var stand = head.Stand;
        double dx = stand.X - eidolon.Pos.X, dz = stand.Z - eidolon.Pos.Z, d = Math.Sqrt(dx * dx + dz * dz);
        if (!_settled && Math.Abs(Feet(eidolon) - stand.Y) <= SettleRise && d <= SettleWithin)
        {
            _settled = true;
            var aim = new Vec3d(stand.X, stand.Y, stand.Z);
            if (d > 0.01)
                aim.Add(dx / d * SettlePast, 0, dz / d * SettlePast);
            if (d > 0.01 && _nav!.Steer(aim, false, () => { }))
            {
                _phase = Phase.Walk;
                return true;
            }
        }
        if (++_approaches > Approaches)
        {
            BadStand(eidolon, head, now);
            return true;
        }
        return Approach(eidolon, head, now);
    }

    /// <summary>
    /// Strikes the next group of its station from where it stands (<see cref="MineStations.NextGroup"/>, from
    /// its stand, of the cells it may break, <see cref="Left"/>), once the live state lets it
    /// (<see cref="MineState.Check"/>, the others where they stand, and the stands of the plan's stations still
    /// to be worked before it, <see cref="MineDispatch.Before"/>). Its station done, or refused, is let go.
    /// </summary>
    private Try StrikeAt(EntityLaborEidolon eidolon, MineStation head, double now)
    {
        var cells = Cells;
        if (cells.Plan is not { } plan || cells.Live is not { } live)
            return Try.Dropped;
        var others = Others(eidolon, eidolon.Pos.XYZ, OthersRange, area.SizeY + OthersRange);
        var workable = head.Cells.Where(c => Left(c, others)).ToHashSet();
        if (workable.Count == 0)
        {
            DropHead(eidolon);
            return Try.Dropped;
        }
        var stand = head.Stand;
        var group = MineStations.NextGroup(head, plan.Tunnel, workable.Contains, stand.X, stand.Y, stand.Z);
        if (group == null)
        {
            DropHead(eidolon);
            return Try.Dropped;
        }
        if (head.Kind == MineStationKind.Stair && Unhung(eidolon, plan, group) is { } hung)
        {
            if (hung.Count == 0)
                return Try.Dropped;
            group = group with { Cells = hung };
        }
        var before = MineDispatch.Before(plan, live, head, ParkedClear(Workers(eidolon)));
        if (MineDispatch.Footing(before, group.Cells) || live.Check(stand, group.Cells, At(Workers(eidolon)), null, before) != MineRefusal.None)
        {
            DropHead(eidolon);
            return Try.Dropped;
        }
        // Only from its stand: the guard was asked of it, and from anywhere else (a walk that ended short,
        // at the bottom of the pit under a stand on its rim, say) a block in reach may be its own way out.
        int feet = Feet(eidolon);
        if (!AtStand(eidolon, stand) || !group.Cells.Any(c => EidolonMining.InReach(eidolon.Pos.X, feet, eidolon.Pos.Z, c)))
            return Try.NotInReach;
        if (!BeginStrike(eidolon, head, group, now))
            return Try.Dropped;
        _pathFails = 0;
        return Try.Struck;
    }

    /// <summary>
    /// A stair's group cut to the cells that leave none of the stair standing with nothing under it or
    /// beside it (the game's rock caves in where it hangs free: a tread so left would fall and take its way
    /// out with it); those it may not dig are left for the order's life, kept as the stair (the plan is not
    /// made again for them). Null when all may go.
    /// </summary>
    private List<MarkPos>? Unhung(EntityLaborEidolon eidolon, MinePlan plan, MineGroup group)
    {
        var blocks = eidolon.World.BlockAccessor;
        var pos = new BlockPos(eidolon.Pos.Dimension);
        var cells = group.Cells.ToHashSet();
        bool Solid(int x, int y, int z) => !cells.Contains(new MarkPos(x, y, z)) && blocks.GetBlock(pos.Set(x, y, z)).Id != 0;
        bool Held(MarkPos n) => Solid(n.X, n.Y - 1, n.Z) || Solid(n.X - 1, n.Y, n.Z) || Solid(n.X + 1, n.Y, n.Z) || Solid(n.X, n.Y, n.Z - 1) || Solid(n.X, n.Y, n.Z + 1);
        var keep = new List<MarkPos>();
        foreach (var c in group.Cells)
        {
            MarkPos[] near = [c with { Y = c.Y + 1 }, c with { X = c.X - 1 }, c with { X = c.X + 1 }, c with { Z = c.Z - 1 }, c with { Z = c.Z + 1 }];
            if (near.Any(n => plan.Stair.Contains(n) && !cells.Contains(n) && blocks.GetBlock(pos.Set(n.X, n.Y, n.Z)).Id != 0 && !Held(n)))
            {
                Leave(c);
                if (Cells.Live is { } live)
                    live.Stale = false;
                continue;
            }
            keep.Add(c);
        }
        return keep.Count == group.Cells.Count ? null : keep;
    }

    /// <summary>Sets off for its station's stand; a stand the game finds no way to is bad (<see cref="BadStand"/>).</summary>
    private bool Approach(EntityLaborEidolon eidolon, MineStation head, double now)
    {
        var stand = head.Stand;
        if (!TakeStair(eidolon, stand.Y))
        {
            // another is on the stair: it waits off it and looks again, its stations let go meanwhile (held,
            // they would keep the one on the stair from working them, and so from ever getting off it)
            DropQueue(eidolon);
            _phase = Phase.Wait;
            _waitCheckAt = now + WaitCheckSeconds;
            OffStair(eidolon);
            return true;
        }
        // its way crosses the columns another holds (where that one works, or makes for): it waits for room,
        // as below, rather than walk, climb or drop past it there
        bool routed = Routed(eidolon);
        if (routed && now - _bumpedAt < BumpSeconds)
        {
            _phase = Phase.Wait;
            _waitCheckAt = _bumpedAt + BumpSeconds;
            eidolon.Orders?.SetStatus("seraphhorizons:eidolon-status-mine-waiting");
            return true;
        }
        if (CrossesClaims(eidolon, stand, now) && (_throughSince ??= now) > now - (routed ? MineRoutePlanner.PassAfter : ThroughSeconds))
        {
            DropQueue(eidolon);
            _phase = Phase.Wait;
            _waitCheckAt = now + WaitCheckSeconds;
            eidolon.Orders?.SetStatus("seraphhorizons:eidolon-status-mine-waiting");
            return true;
        }
        if (_nav!.GoTo(new Vec3d(stand.X, stand.Y, stand.Z), false, () => { }, () => { }, arriveWithin: ArriveWithin))
        {
            // its way goes through another (no way round it, a pit too narrow to pass one at work): it waits
            // for room, at most ThroughSeconds, rather than push past it
            if (!_nav.LastWentAround && (_throughSince ??= now) > now - (routed && MayPass(eidolon) ? PassSeconds : ThroughSeconds))
            {
                _nav.Stop();
                DropQueue(eidolon);
                _phase = Phase.Wait;
                _waitCheckAt = now + WaitCheckSeconds;
                eidolon.Orders?.SetStatus("seraphhorizons:eidolon-status-mine-waiting");
                return true;
            }
            if (!_nav.LastWentAround)
                _contacts++;
            _throughSince = null;
            _rim.Leave(eidolon, _jobs);
            _walkSince = now;
            _walkEstimate = MineCosts.Estimate(WidePath.NodeAt(eidolon.Pos.X, eidolon.Pos.Y, eidolon.Pos.Z), stand) + MineCosts.ArriveSeconds;
            _phase = Phase.Walk;
            _settled = false;
            eidolon.Orders?.SetStatus("seraphhorizons:eidolon-status-mine-walking");
            return true;
        }
        BadStand(eidolon, head, now);
        return true;
    }

    /// <summary>Whether its way to <paramref name="stand"/> passes with its box over a column another member
    /// holds, past its first steps (it may be setting off from by one): with no other holding any, false
    /// with no search.</summary>
    private bool CrossesClaims(EntityLaborEidolon eidolon, PathCell stand, double now)
    {
        if (Board is not { } board || _job == null)
            return false;
        long id = eidolon.EntityId;
        if (!_job.Present.Any(m => m != id && board.Held(m).Count > 0))
            return false;
        if (_nav!.FindPath(new Vec3d(stand.X, stand.Y, stand.Z)) is not { } path)
            return false;
        foreach (var node in path.Skip(2))
            for (int x = node.X - 1; x <= node.X; x++)
            for (int z = node.Z - 1; z <= node.Z; z++)
                if (board.Holder((x, z), now) is { } holder && holder != id)
                    return true;
        return false;
    }

    /// <summary>A stand the game's pathfinder finds no way to, or that it arrives short of again and again
    /// (README "Eidolon", mining, *Stations*: the game's blocks are not all whole): bad, for its job, until a
    /// block near it changes; the station let go and the plan made again without it. After
    /// <see cref="PathTries"/> in a row, it waits.</summary>
    private void BadStand(EntityLaborEidolon eidolon, MineStation head, double now)
    {
        _nav?.Stop();
        // another eidolon at or by it: no way there for now, not a bad stand
        var stand = head.Stand;
        if (Others(eidolon, new Vec3d(stand.X, stand.Y, stand.Z), 3, 4).Count > 0)
        {
            DropHead(eidolon);
            Wait(eidolon, now);
            return;
        }
        Cells.BadStands.Add(head.Stand);
        eidolon.World.Logger.Debug($"[eidolon-mine] {eidolon.EntityId}: no way to stand ({head.Stand.X},{head.Stand.Y},{head.Stand.Z}) (station {head.Id}) from {eidolon.Pos.XYZ}: bad");
        DropHead(eidolon);
        Plans?.Replan(Cells, false);
        _phase = Phase.Seek;
        if (++_pathFails >= PathTries)
        {
            _pathFails = 0;
            DropQueue(eidolon);
            Wait(eidolon, now);
        }
    }

    /// <summary>
    /// Whether it may set off for feet <paramref name="toFeet"/> (README "Eidolon", working together, *The
    /// stair*): a walk in a pit with a stair more than a climb up or down, from or to more than a climb under
    /// its rim, is on the stair, which one member walks at a time (it is 2 wide: two
    /// meeting on it push each other off); such a walk takes it (<see cref="MineJobCells.StairHolder"/>),
    /// unless another holds it, and it is let go once the walk is over. Any other walk may go.
    /// </summary>
    private bool TakeStair(EntityLaborEidolon eidolon, int toFeet)
    {
        var cells = Cells;
        if (_job == null || cells.Plan is not { Stair.Count: > 0 })
            return true;
        int feet = Feet(eidolon), rim = area.Max.Y + 1, climb = EidolonTeamwork.FloorStep;
        if (Math.Abs(toFeet - feet) <= climb || Math.Min(feet, toFeet) >= rim - climb)
            return true;
        double now = EidolonJobSystem.Now(eidolon.World);
        long id = eidolon.EntityId;
        if (cells.StairHolder != 0 && cells.StairHolder != id && now < cells.StairUntil && _job.Present.Contains(cells.StairHolder))
            return false;
        // nor while another stands on it, unless it stands on it too (one of them has to go first)
        if (!OnStair(eidolon.Pos.X, feet, eidolon.Pos.Z)
            && _job.Present.Any(m => m != id && _jobs?.Member(m) is { } o && OnStair(o.Pos.X, Feet(o), o.Pos.Z)))
            return false;
        cells.StairHolder = id;
        cells.StairUntil = now + StairSeconds;
        return true;
    }

    /// <summary>Whether a box with its feet' centre at (<paramref name="x"/>, <paramref name="z"/>) is over a
    /// column of the stair, under the pit's rim.</summary>
    private bool OnStair(double x, int feet, double z)
    {
        if (feet >= area.Max.Y + 1 - EidolonTeamwork.FloorStep)
            return false;
        var f = EidolonMining.Footprint(x, z);
        var stair = Cells.Stair;
        var planned = Cells.Plan?.Stair;
        for (int cx = f.MinX; cx <= f.MaxX; cx++)
        for (int cz = f.MinZ; cz <= f.MaxZ; cz++)
        for (int y = area.Min.Y; y <= area.Max.Y; y++)
            if (stair.Contains(new MarkPos(cx, y, cz)) || planned?.Contains(new MarkPos(cx, y, cz)) == true)
                return true;
        return false;
    }

    /// <summary>Waits for the stair to come free off it: standing over a column of it (or of its plan's) or
    /// next to one,
    /// it steps aside to the nearest place with its box a column clear of them (<see cref="RimWait.Aside"/>), out of the way of the
    /// one walking it; else it waits where it is.</summary>
    private void OffStair(EntityLaborEidolon eidolon)
    {
        eidolon.Orders?.SetStatus("seraphhorizons:eidolon-status-mine-waiting-stair");
        var columns = Cells.Stair.Concat(Cells.Plan?.Stair ?? new HashSet<MarkPos>()).Select(c => (c.X, c.Z)).ToHashSet();
        // its box a column clear of the stair: the one walking it overhangs its treads
        bool Clear(int x, int z)
        {
            for (int cx = x - 2; cx <= x + 1; cx++)
            for (int cz = z - 2; cz <= z + 1; cz++)
                if (columns.Contains((cx, cz)))
                    return false;
            return true;
        }
        var at = WidePath.NodeAt(eidolon.Pos.X, eidolon.Pos.Y, eidolon.Pos.Z);
        if (!Clear(at.X, at.Z))
            _rim.Aside(eidolon, _nav!, _jobs, Clear);
    }

    /// <summary>It has work again after it said it had none.</summary>
    private void Unfinish(EntityLaborEidolon eidolon)
    {
        if (!_finished)
            return;
        _finished = false;
        Board?.SetFinished(eidolon.EntityId, false);
    }

    /// <summary>Nothing it may work now (every station left held by another, under another, or of a band
    /// not open yet, or nothing left while the others finish): frees its columns and waits at the area's
    /// rim, out of the pit and out of the others' way (<see cref="RimWait"/>), looking again every
    /// <see cref="WaitCheckSeconds"/>.</summary>
    private bool Wait(EntityLaborEidolon eidolon, double now)
    {
        // its way to where it waits keeps out of the columns the others work, as its way to a station does
        _rim.Crosses = to => CrossesClaims(eidolon, WidePath.NodeAt(to.X, to.Y, to.Z), now);
        DropQueue(eidolon);
        Board?.Release(eidolon.EntityId);
        _phase = Phase.Wait;
        _waitCheckAt = now + WaitCheckSeconds;
        // in the pit, the stair another's: it waits where it is rather than climb out past that one
        if (!_nav!.Active && !TakeStair(eidolon, area.Max.Y + 1))
        {
            OffStair(eidolon);
            return true;
        }
        // Down a pit with a stair, with work left: it waits down there, out of the way, rather than climb
        // the whole stair out to the rim and back for the next station.
        if (!_finished && Cells.Plan is { Stair.Count: > 0 } && Feet(eidolon) < area.Max.Y + 1 - EidolonTeamwork.FloorStep)
        {
            _rim.Leave(eidolon, _jobs);
            if (!_nav.Active)
            {
                var clear = PitClear(eidolon.EntityId, now);
                var at = WidePath.NodeAt(eidolon.Pos.X, eidolon.Pos.Y, eidolon.Pos.Z);
                if (!clear(at.X, at.Z))
                    _rim.Aside(eidolon, _nav, _jobs, clear);
            }
            eidolon.Orders?.SetStatus("seraphhorizons:eidolon-status-mine-waiting-aside");
            return true;
        }
        _rim.Wait(eidolon, _nav!, _jobs,
            _finished ? "seraphhorizons:eidolon-status-mine-waiting-done"
                : Cells.Live?.Bands?.Covered != null ? "seraphhorizons:eidolon-status-mine-waiting-face" : "seraphhorizons:eidolon-status-mine-waiting",
            "seraphhorizons:eidolon-status-mine-waiting-aside", AsideClear(eidolon.EntityId, now), now, RescanSeconds);
        return true;
    }

    /// <summary>Where it may step aside to when it cannot reach the rim: a path node whose four columns
    /// have no block left it would mine (mined floor, the stair, or outside the area) and none another holds.</summary>
    /// <summary>Where it may wait down a pit: a path node whose box is a column clear of the stair and of
    /// the columns another holds (the work under its feet waits for it to move, the stations cut round it).</summary>
    private System.Func<int, int, bool> PitClear(long id, double now)
    {
        var stair = Cells.Stair.Concat(Cells.Plan?.Stair ?? new HashSet<MarkPos>()).Select(c => (c.X, c.Z)).ToHashSet();
        var board = Board;
        return (x, z) =>
        {
            for (int cx = x - 2; cx <= x + 1; cx++)
            for (int cz = z - 2; cz <= z + 1; cz++)
                if (stair.Contains((cx, cz)) || board?.Free(id, (cx, cz), now) == false)
                    return false;
            return true;
        };
    }

    private System.Func<int, int, bool> AsideClear(long id, double now)
    {
        var work = (Cells.Live?.Remaining ?? new HashSet<MarkPos>()).Concat(Cells.Plan?.Stair ?? new HashSet<MarkPos>())
            .Select(c => (c.X, c.Z)).ToHashSet();
        var board = Board;
        return (x, z) =>
        {
            for (int cx = x - 1; cx <= x; cx++)
            for (int cz = z - 1; cz <= z; cz++)
                if (work.Contains((cx, cz)) || board?.Free(id, (cx, cz), now) == false)
                    return false;
            return true;
        };
    }

    /// <summary>With nothing left it may break in the loaded part of the area: walks toward the nearest
    /// part not loaded, at most <see cref="TravelBlocks"/> to ground that is loaded, or waits for it to
    /// load, looking again every <see cref="RescanSeconds"/>.</summary>
    private bool Travel(EntityLaborEidolon eidolon, double now)
    {
        Unfinish(eidolon);
        // not working a station: its columns free for the others
        DropQueue(eidolon);
        Board?.Release(eidolon.EntityId);
        var unloaded = Cells.Live?.Site.UnloadedColumns ?? [];
        if (EidolonMining.NearestColumn(unloaded, eidolon.Pos.X, eidolon.Pos.Z) is { } column && TravelTo(eidolon, column))
        {
            _rim.Leave(eidolon, _jobs);
            _phase = Phase.Travel;
            _waitingSince = null;
            _stalled = false;
            eidolon.Orders?.SetStatus("seraphhorizons:eidolon-status-mine-travelling");
            return true;
        }
        _waitingSince ??= now;
        _rescanAt = now + RescanSeconds;
        _stalled = now - _waitingSince >= StallSeconds;
        eidolon.Orders?.SetStatus("seraphhorizons:eidolon-status-mine-unloaded");
        return true;
    }

    // Sets off toward a block column: to the furthest ground on the way, loaded, it finds a path to.
    private bool TravelTo(EntityLaborEidolon eidolon, (int X, int Z) column)
    {
        var blocks = eidolon.World.BlockAccessor;
        var space = new GameWideSpace(blocks, eidolon.CollisionBox);
        var pos = new BlockPos(eidolon.Pos.Dimension);
        double dx = column.X + 0.5 - eidolon.Pos.X, dz = column.Z + 0.5 - eidolon.Pos.Z, d = Math.Sqrt(dx * dx + dz * dz);
        int feet = Feet(eidolon);
        for (double step = Math.Min(d, TravelBlocks); step >= 2; step -= 4)
        {
            int x = (int)Math.Round(eidolon.Pos.X + dx / d * step), z = (int)Math.Round(eidolon.Pos.Z + dz / d * step);
            // the node's four columns, its box over them, loaded
            if (blocks.GetChunkAtBlockPos(pos.Set(x - 1, feet, z - 1)) == null || blocks.GetChunkAtBlockPos(pos.Set(x, feet, z)) == null)
                continue;
            for (int rise = 0; rise <= TravelRise; rise++)
            foreach (int y in rise == 0 ? [feet] : new[] { feet + rise, feet - rise })
            {
                if (y < 1 || !space.Free(x, y, z) || space.Free(x, y - 1, z))
                    continue;
                if (_nav!.GoTo(new Vec3d(x, y, z), false, () => { }, () => { }, tolerance: 1))
                    return true;
            }
        }
        return false;
    }

    /// <summary>Whether an area covered all over is one the eidolon fits in (<see cref="EidolonTunnels.Fit"/>),
    /// as the world is now: covered where a full block of ground is within <see cref="EidolonTunnels.RoofRise"/>
    /// over its top layer (<see cref="Roof"/>), in a column loaded.</summary>
    public static TunnelFit TunnelFitOf(IWorldAccessor world, MarkArea area)
    {
        var blocks = world.BlockAccessor;
        var pos = new BlockPos(0);
        var covered = EidolonTunnels.Covered(area, c => blocks.GetChunkAtBlockPos(pos.Set(c.X, c.Y, c.Z)) != null && Roof(blocks.GetBlock(pos)));
        return EidolonTunnels.Fit(area, covered);
    }

    /// <summary>The lang key an area that does not fit is refused with (arguments the least height and
    /// width, and the heading's height), or null for one that fits.</summary>
    public static string? RefusalKey(TunnelFit fit) => fit switch
    {
        TunnelFit.TooLow => "seraphhorizons:eidoloncommander-mine-toolow",
        TunnelFit.TooNarrow => "seraphhorizons:eidoloncommander-mine-narrow",
        _ => null,
    };

    /// <summary>Whether a block over the area roofs it: a full block of ground under its underside
    /// (stone, ore, soil, gravel, sand, brick, clay work), not a tree, plants, snow or a loose stone.</summary>
    internal static bool Roof(Block block) =>
        block.Id != 0 && !block.IsLiquid() && block.SideSolid[BlockFacing.DOWN.Index]
        && block.BlockMaterial is EnumBlockMaterial.Stone or EnumBlockMaterial.Ore or EnumBlockMaterial.Soil or EnumBlockMaterial.Gravel
            or EnumBlockMaterial.Sand or EnumBlockMaterial.Brick or EnumBlockMaterial.Ceramic or EnumBlockMaterial.Mantle;

    /// <summary>Whether its owner may break blocks at <paramref name="pos"/> (land claims), as felling
    /// asks: their access when the server knows them, else only where nothing is claimed.</summary>
    private static bool MayBreak(EntityLaborEidolon eidolon, BlockPos pos)
    {
        var world = eidolon.World;
        if (eidolon.OwnerUid is { } uid && world.PlayerByUid(uid) is { } owner)
            return world.Claims.TestAccess(owner, pos, EnumBlockAccessFlags.BuildOrBreak) == EnumWorldAccessResponse.Granted;
        return world.Claims.Get(pos) is not { Length: > 0 };
    }

    /// <summary>
    /// Whether it may break a cell of its station now: still to dig in its job's live state and of a band
    /// whose turn it is, still standing (gone, it is taken as dug), in no column of another eidolon at any
    /// height (<paramref name="others"/>: under it is its floor, over it is over it), and its owner may break
    /// it, asked the first time (one they may not is left for the order's life, by the whole job, and the
    /// plan made again round it).
    /// </summary>
    private bool Left(MarkPos cell, List<(double X, int Feet, double Z, bool Walking)> others)
    {
        if (Cells.Live is not { } live || !live.Remaining.Contains(cell))
            return false;
        // of a band whose turn it is, or ahead of it for a route's stop worked ahead (README "Eidolon", working
        // together, *Spare work*)
        if (live.Bands?.Eligible(cell) == false && !(_headAhead && _eidolon != null && Ahead(_eidolon) && MineAhead.Eligible(live, cell)))
            return false;
        if (_eidolon != null && _eidolon.World.BlockAccessor.GetBlock(_probe.Set(cell.X, cell.Y, cell.Z)).Id == 0)
        {
            Plans?.Seen(Cells, cell, new MineVoxel(false, MineRule.Air));
            return false;
        }
        // its columns, and a little round them for one walking: it may step over in the time a strike lands
        if (others.Any(o => o.Walking ? Near(o.X, o.Z, cell) : EidolonMining.UnderFoot(o.X, o.Z, cell)))
            return false;
        if (!_mayBreak.TryGetValue(cell, out bool may))
        {
            may = _mayBreak[cell] = _eidolon != null && MayBreak(_eidolon, new BlockPos(cell.X, cell.Y, cell.Z, _eidolon.Pos.Dimension));
            if (!may)
                Leave(cell);
        }
        return may;
    }

    /// <summary>Whether a cell is in a column of a box centred within <see cref="NearMargin"/> of
    /// (<paramref name="x"/>, <paramref name="z"/>).</summary>
    private static bool Near(double x, double z, MarkPos cell) =>
        EidolonMining.UnderFoot(x, z, cell) || EidolonMining.UnderFoot(x - NearMargin, z - NearMargin, cell) || EidolonMining.UnderFoot(x + NearMargin, z - NearMargin, cell)
        || EidolonMining.UnderFoot(x - NearMargin, z + NearMargin, cell) || EidolonMining.UnderFoot(x + NearMargin, z + NearMargin, cell);

    private const double NearMargin = 0.25;

    /// <summary>A cell left for the order's life, by the whole job: its live state takes it as one it may
    /// not break, and the plan is made again round it.</summary>
    private void Leave(MarkPos cell)
    {
        Cells.Skip.Add(cell);
        Plans?.Seen(Cells, cell, new MineVoxel(true, MineRule.Claimed));
    }

    private readonly BlockPos _probe = new(0);

    private static int Feet(Entity eidolon) => (int)Math.Floor(eidolon.Pos.Y + 0.01);

    /// <summary>Starts a strike at <paramref name="group"/> of <paramref name="head"/>, of its blocks it may
    /// break now (<see cref="Breakable"/>); false, and none, when none is left.</summary>
    private bool BeginStrike(EntityLaborEidolon eidolon, MineStation head, MineGroup group, double now)
    {
        var cells = Breakable(eidolon, group.Cells);
        if (cells.Count == 0)
            return false;
        if (cells.Count < group.Cells.Count)
            group = group with { Cells = cells };
        _nav?.Stop();
        _rim.Leave(eidolon, _jobs);
        _phase = Phase.Strike;
        _group = group;
        if (head.Kind == MineStationKind.Face && Cells.Plan?.Tunnel is { } tunnel && head.Lane >= 0 && head.Lane < tunnel.Lanes.Count)
        {
            _band = head.Band;
            _sector = tunnel.Lanes[head.Lane];
        }
        else
        {
            var claim = MineDispatch.Claim(head, Cells.Plan?.Tunnel).ToList();
            _sector = new MineSector(claim.Min(c => c.X), claim.Min(c => c.Z), claim.Max(c => c.X), claim.Max(c => c.Z));
        }
        var blocks = eidolon.World.BlockAccessor;
        var pos = new BlockPos(eidolon.Pos.Dimension);
        _strike = EidolonMining.Timing(EidolonMining.StrikeSeconds(group.Cells.Select(c =>
            GameMineWorld.PlayerSeconds(blocks, pos.Set(c.X, c.Y, c.Z), blocks.GetBlock(pos), _pick, _shovel))));
        _strikeStart = now;
        _workSeconds += _strike.Seconds;
        if (Routed(eidolon) && _routeStarted != head.Id)
        {
            _routeStarted = head.Id;
            Repaired(Cells.Routes!.Started(eidolon.EntityId, head.Id, now));
        }
        _slammed = false;
        _struck = false;
        Face(eidolon);
        eidolon.Orders?.SetStatus("seraphhorizons:eidolon-status-mine-mining");
        return true;
    }

    private bool Strike(EntityLaborEidolon eidolon, double now)
    {
        Face(eidolon);
        double t = now - _strikeStart;
        // Never a slam at nothing: as it starts and until it lands, the group is looked at again (another
        // eidolon stepped over it, it was pushed out of reach, a block fell or was mined); with nothing
        // left in it to break, the strike is cut short and it chooses again.
        if (!_struck && t >= _strike.SlamAt && _group != null && Breakable(eidolon, _group.Cells).Count == 0)
        {
            CutStrikes++;
            eidolon.AnimManager.StopAnimation(SlamAnimation);
            _group = null;
            _phase = Phase.Seek;
            return true;
        }
        if (!_slammed && t >= _strike.SlamAt)
        {
            _slammed = true;
            if (eidolon.Properties.Client?.AnimationsByMetaCode?.TryGetValue(SlamAnimation, out var slam) == true)
            {
                var played = slam.Clone();
                played.AnimationSpeed = (float)_strike.SlamSpeed;
                eidolon.AnimManager.StopAnimation(SlamAnimation);
                eidolon.AnimManager.StartAnimation(played);
            }
        }
        if (!_struck && t >= _strike.ImpactAt)
        {
            _struck = true;
            Break(eidolon);
        }
        if (t < _strike.Seconds)
            return true;
        eidolon.AnimManager.StopAnimation(SlamAnimation);
        _group = null;
        _phase = Phase.Seek;
        if (_headDone)
        {
            _headDone = false;
            DropHead(eidolon);
            _yieldUntil = now + YieldSeconds;
        }
        return true;
    }

    /// <summary>The cells of <paramref name="cells"/> it may break now from where it stands: still left
    /// (<see cref="Left"/>), in its reach, and still a block it mines (<see cref="EidolonMining.Rule"/>: a liquid
    /// may have come beside it); one it no longer mines for what it is goes to its job's live state as such.</summary>
    private List<MarkPos> Breakable(EntityLaborEidolon eidolon, IReadOnlyList<MarkPos> cells)
    {
        var breakable = new List<MarkPos>();
        if (cells.Count == 0)
            return breakable;
        // any height: a column another stands in is its floor under it and over its head over it
        var others = Others(eidolon, new Vec3d(cells.Average(c => c.X + 0.5), cells.Average(c => c.Y + 0.5), cells.Average(c => c.Z + 0.5)),
            OthersRange, area.SizeY + OthersRange);
        int feet = Feet(eidolon);
        var world = eidolon.World;
        var pos = new BlockPos(eidolon.Pos.Dimension);
        var near = new BlockPos(eidolon.Pos.Dimension);
        foreach (var cell in cells)
        {
            if (!Left(cell, others) || !EidolonMining.InReach(eidolon.Pos.X, feet, eidolon.Pos.Z, cell))
                continue;
            pos.Set(cell.X, cell.Y, cell.Z);
            var block = world.BlockAccessor.GetBlock(pos);
            var rule = GameMineWorld.Rule(world, pos, block, near, _pick, _maxTier, _mineThrough);
            if (rule != MineRule.Mine)
            {
                Plans?.Seen(Cells, cell, new MineVoxel(GameMineWorld.Solid(block.GetCollisionBoxes(world.BlockAccessor, pos)), rule));
                continue;
            }
            breakable.Add(cell);
        }
        return breakable;
    }

    /// <summary>Breaks what is still to break in the group (<see cref="Breakable"/>), as a player's pick
    /// does with no player: the block's own drops fall in the world, and its job's live state takes them as
    /// dug (<see cref="EidolonMinePlans.Broken"/>). A block that will not break is left for the order's life,
    /// by the whole job. Its station done, it lets it go.</summary>
    private void Break(EntityLaborEidolon eidolon)
    {
        if (_group == null)
            return;
        var world = eidolon.World;
        var blocks = world.BlockAccessor;
        var broken = new List<MarkPos>();
        bool sounded = false;
        foreach (var cell in Breakable(eidolon, _group.Cells))
        {
            var pos = new BlockPos(cell.X, cell.Y, cell.Z, eidolon.Pos.Dimension);
            var block = blocks.GetBlock(pos);
            if (!sounded && block.Sounds?.GetBreakSound(EnumTool.Pickaxe).Location is { } sound)
            {
                world.PlaySoundAt(sound, cell.X + 0.5, cell.Y + 0.5, cell.Z + 0.5, null, true, 24);
                sounded = true;
            }
            blocks.BreakBlock(pos, null);
            if (blocks.GetBlock(pos).Id == block.Id)
            {
                Leave(cell);
                continue;
            }
            broken.Add(cell);
        }
        if (broken.Count == 0)
        {
            EmptyStrikes++;
            return;
        }
        if (Plans is { } plans)
            plans.Broken(Cells, broken);
        else
            Cells.Live?.Apply(broken);
        Mined += broken.Count;
        Strikes++;
        _job?.Tally(eidolon.EntityId, broken.Count);
        Board?.Struck(eidolon.EntityId, EidolonJobSystem.Now(world));
        // its station done: let go once the strike is over (it stands in its claim till then)
        if (Station is { } head && Cells.Live is { } live && !head.Cells.Any(live.Remaining.Contains))
            _headDone = true;
        // Last: a dry reservoir stops it at once (the order is cancelled and started again later).
        eidolon.SpendOil(EidolonJob.MiningStrike);
    }

    /// <summary>
    /// Tells its owner, once for its job, what of the area is out of its reach under every order
    /// (<see cref="MinePlan.OutOfReach"/>), how many blocks it keeps under those as their floor, and how
    /// high a step in front of the face would bring them in reach (<see cref="MinePlan.StepNeeded"/>, when
    /// known); again only when that count grows, or falls to none (the world changed: it can reach them
    /// now). The count told is saved with its order (<c>outofreach</c>).
    /// </summary>
    private void TellOutOfReach(EntityLaborEidolon eidolon, MinePlan plan)
    {
        var cells = Cells;
        if (cells.ToldOutOfReach < 0 && _toldSaved >= 0)
            cells.ToldOutOfReach = _toldSaved;
        int count = cells.OutOfReach.Count;
        if (count != cells.ToldOutOfReach)
        {
            int told = Math.Max(0, cells.ToldOutOfReach);
            if (count > told)
            {
                (string key, object[] args) = plan.StepNeeded is { } step
                    ? ("seraphhorizons:eidolon-mine-outofreach", new object[] { count, plan.Held.Count, step })
                    : ("seraphhorizons:eidolon-mine-outofreach-nostep", new object[] { count, plan.Held.Count });
                ToldOutOfReach = Lang.Get(key, args);
                FellOrder.TellOwner(eidolon, key, args);
            }
            else if (count == 0 && told > 0)
            {
                ToldOutOfReach = Lang.Get("seraphhorizons:eidolon-mine-inreach-now");
                FellOrder.TellOwner(eidolon, "seraphhorizons:eidolon-mine-inreach-now");
            }
            cells.ToldOutOfReach = count;
        }
        if (_toldSaved != cells.ToldOutOfReach)
        {
            _toldSaved = cells.ToldOutOfReach;
            Save(eidolon, args => args.SetInt("outofreach", _toldSaved));
        }
    }

    /// <summary>Nothing left it or another may take: says so to its job, and if the job is done (every
    /// member at work has nothing left, none still to load) makes the report; else waits at the rim
    /// for the others.</summary>
    private bool Finish(EntityLaborEidolon eidolon, double now)
    {
        if (!_finished)
        {
            _finished = true;
            Board?.SetFinished(eidolon.EntityId, true);
        }
        if (Board is { } board && _job != null)
        {
            if (!board.TryClose(eidolon.EntityId, now))
                return Wait(eidolon, now);
            // every member's order saved done, so none mines and reports again after a restart
            _jobs!.SaveDone(_job);
        }
        return Report(eidolon);
    }

    /// <summary>Tells its owner what the job mined (each member's share, when there were more than
    /// one), how many it left as they were, how many were out of reach, how many it kept under those as
    /// their floor and how many it left as its way out (<see cref="MinePlan"/>), and walks back.</summary>
    private bool Report(EntityLaborEidolon eidolon)
    {
        var blocks = eidolon.World.BlockAccessor;
        var pos = new BlockPos(eidolon.Pos.Dimension);
        var plan = Cells.Plan;
        int left = 0, unreachable = 0, held = 0, wayOut = 0;
        for (int y = Math.Max(EidolonMining.BottomY, area.Min.Y); y <= area.Max.Y; y++)
        for (int x = area.Min.X; x <= area.Max.X; x++)
        for (int z = area.Min.Z; z <= area.Max.Z; z++)
        {
            if (blocks.GetChunkAtBlockPos(pos.Set(x, y, z)) == null)
                continue;
            var block = blocks.GetBlock(pos);
            if (block.Id == 0 || block.IsLiquid())
                continue;
            var cell = new MarkPos(x, y, z);
            if (Cells.Stair.Contains(cell))
                wayOut++;
            else if (plan?.OutOfReach.Contains(cell) == true)
                unreachable++;
            else if (plan?.Held.Contains(cell) == true)
                held++;
            else
                left++;
        }
        var shares = _job?.Shares ?? [];
        if (shares.Count > 1)
        {
            Reported = EidolonJobSystem.ShareReport(SharedDoneKey, ShareKey, shares, _job!.Total, left, unreachable, held, wayOut);
            EidolonJobSystem.TellShares(eidolon, SharedDoneKey, ShareKey, shares, _job.Total, left, unreachable, held, wayOut);
        }
        else
        {
            Reported = Lang.Get("seraphhorizons:eidolon-mine-done", Mined, left, unreachable, held, wayOut);
            FellOrder.TellOwner(eidolon, "seraphhorizons:eidolon-mine-done", Mined, left, unreachable, held, wayOut);
        }
        Plans?.Close(Cells);
        Told(eidolon);
        return GoHome(eidolon);
    }

    /// <summary>The job's report for more than one: how many, all they mined, each one's share
    /// (<c>eidolon-mine-share</c>, "40 by eidolon 1234"), what was left, out of reach, kept as their floor
    /// and left as the way out.</summary>
    private const string SharedDoneKey = "seraphhorizons:eidolon-mine-done-shared";
    private const string ShareKey = "seraphhorizons:eidolon-mine-share";

    /// <summary>Its job is done and reported: it leaves it and saves that it is done, so it goes on
    /// back after a restart rather than mining again.</summary>
    private void Told(EntityLaborEidolon eidolon)
    {
        _done = true;
        _finished = false;
        _queue.Clear();
        Board?.Leave(eidolon.EntityId);
        _rim.Leave(eidolon, _jobs);
        Save(eidolon, args => args.SetBool("done", true));
    }

    /// <summary>Walks back to where it was ordered; false (the order over) when it is there or finds
    /// no way.</summary>
    private bool GoHome(EntityLaborEidolon eidolon)
    {
        _phase = Phase.Return;
        if (home == null || eidolon.Pos.XYZ.HorizontalSquareDistanceTo(home) <= HomeSlack * HomeSlack && Math.Abs(eidolon.Pos.Y - home.Y) < 1.5)
            return false;
        _stairWait = !TakeStair(eidolon, (int)Math.Floor(home.Y + 0.01));
        if (_stairWait)
        {
            OffStair(eidolon);
            return true;
        }
        if (!_nav!.GoTo(home, false, () => { }, () => { }))
            return false;
        eidolon.Orders?.SetStatus("seraphhorizons:eidolon-status-mine-returning");
        return true;
    }

    private void Face(EntityLaborEidolon eidolon)
    {
        if (_group == null)
            return;
        float yaw = EidolonFelling.Yaw(eidolon.Pos.X, eidolon.Pos.Z, _group.CentreX, _group.CentreZ);
        eidolon.Pos.Yaw = yaw;
        eidolon.BodyYaw = yaw;
    }

    private void Halt(EntityLaborEidolon eidolon)
    {
        _nav?.Stop();
        eidolon.AnimManager.StopAnimation(SlamAnimation);
    }
}
