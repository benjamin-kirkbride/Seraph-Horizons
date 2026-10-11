using System.Globalization;
using SeraphHorizons.Mod.Eidolon.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent.Mechanics;

namespace SeraphHorizons.Mod.Eidolon;

/// <summary>
/// Mine a marked area (README "Eidolon", mining; arguments the area's corners <c>minX</c> ...
/// <c>maxZ</c>, and where it stood when ordered, <c>homeX</c>, <c>homeY</c>, <c>homeZ</c>). It quarries
/// top down, a layer at a time (<see cref="Scan"/>: the highest layer with a block left it may break,
/// <see cref="EidolonMining.Rule"/>), striking a group of up to four blocks at once
/// (<see cref="EidolonMining.Next"/>: flat on the layer, or upright at a wall where the layer under it
/// is left too) with the shape's slam, timed to a seraph's steel pickaxe at three quarters of their pace
/// (<see cref="EidolonMining.Timing"/>), the blocks breaking as a player's would, their drops falling
/// in the world. Each strike costs oil (<see cref="EidolonJob.MiningStrike"/>). It never breaks under
/// its own box; for what it stood on it walks to a place beside (<see cref="EidolonMining.Stands"/>).
///
/// <para>The land claim is asked only of a block it comes to (a square it makes for, a group it may
/// strike), not of every block a search finds; a block its owner may not break is left for the
/// order's life. A column of the area not loaded reads as air, so it is never done: with no block
/// it may break left in the loaded part, it walks toward the nearest unloaded column (its own
/// chunks, held while it works, load ahead of it), or waits for it to load
/// (<see cref="EidolonMining.Scan"/>).</para>
///
/// <para>Blocks it cannot reach (no way to any place beside them) are given up for the order's life.
/// Done when no block it may break and reach is left and none of the area is unloaded: it tells its owner how many it mined and how
/// many it left, walks back to where it was ordered (if it can) and the order ends. Interrupted
/// (self-defence, a slump, dry), it starts again by looking afresh; interrupted on its way back, it
/// goes on back.</para>
///
/// <para>Eidolons of one owner given the very same area work it as one job (README "Eidolon", working
/// together; <see cref="EidolonJobSystem.Mining"/>, <see cref="JobBoard{TUnit, TShared}"/>): each reserves
/// the 2 × 2 square it makes for or strikes in (its columns) before it sets off or strikes, and passes over
/// those another holds; none
/// starts a layer while one above is still worked by another, waiting at the area's rim
/// (<see cref="EidolonTeamwork.Rim"/>) as one does with nothing free to work; none breaks a block under
/// any eidolon's box at or below its feet (<see cref="EidolonMining.Undermines(double, int, double, MarkPos)"/>);
/// and the last to find nothing left tells the owner what each mined, and each walks back to where it was
/// ordered. Each saves its own order, with the ids of the others in its job (<c>mates</c>), which the
/// job, formed again on load, waits for (<see cref="EidolonTeamwork.ExpectSeconds"/>); and, once it has
/// been told the job is done, <c>done</c>, so it goes on back.</para>
/// </summary>
public sealed class MineOrder(MarkArea area, Vec3d? home, IReadOnlyList<long>? mates = null, bool done = false) : IEidolonOrder
{
    public const string OrderCode = "mine";

    /// <summary>How often, at most, it searches the area again while it works (the world may change
    /// under it); it searches at once when the layer it works is done.</summary>
    public const double RescanSeconds = 10;

    /// <summary>Places beside a square it searches a path to before it gives the square up.</summary>
    private const int PathTries = 3;

    /// <summary>Times it sets off again for a square it arrived out of reach of.</summary>
    private const int Approaches = 2;

    /// <summary>Within this of where it was ordered, it is back.</summary>
    private const double HomeSlack = 1.5;

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

    /// <summary>How often a waiting eidolon looks for a square freed or a layer finished.</summary>
    private const double WaitCheckSeconds = 1;

    /// <summary>Places on the rim it looks for ground at before it waits where it is.</summary>
    private const int RimLooks = 16;

    /// <summary>How far round a group (or itself) it looks for eidolons it must not undermine: its
    /// reach and their box's half width, with room.</summary>
    private const float OthersRange = 8;

    private const string SlamAnimation = "slam";

    private enum Phase { Seek, Walk, Strike, Return, Travel, Wait }

    private readonly HashSet<MarkPos> _unreachable = new();
    private readonly HashSet<MarkPos> _skip = new();
    private readonly HashSet<MarkPos> _left = new();
    private readonly Dictionary<MarkPos, bool> _mayBreak = new();
    private IReadOnlyList<(int X, int Z)> _unloaded = [];
    private double? _waitingSince;
    private bool _stalled;
    private int _maxTier = EidolonMining.SteelPickTier;
    private EidolonNavigator? _nav;
    private EntityLaborEidolon? _eidolon;
    private Phase _phase;
    private int? _layer;
    private double _rescanAt;
    private IReadOnlyList<MarkPos>? _square;
    private int _approaches;
    private PathCell? _stand;
    private bool _settled;
    private MineGroup? _group;
    private MineStrike _strike;
    private double _strikeStart;
    private bool _slammed;
    private bool _struck;
    private bool _done = done;
    private Item? _pick;
    private EidolonJobSystem? _jobs;
    private SharedJob<MineJobCells>? _job;
    private IReadOnlyList<long> _mates = mates ?? [];
    private bool _expectMates = true;
    private bool _finished;
    private (int X, int Z)? _rim;
    private double _waitCheckAt;
    private double _rimRetryAt;

    public string Code => OrderCode;

    public MarkArea Area => area;

    /// <summary>Blocks it broke.</summary>
    public int Mined { get; private set; }

    /// <summary>Strikes it struck (each breaking at least one block).</summary>
    public int Strikes { get; private set; }

    /// <summary>Blocks it gave up for want of a way to them.</summary>
    public int Unreachable => _unreachable.Count;

    /// <summary>The block columns of the area not loaded at its last search, which it has yet to mine.</summary>
    public int UnloadedColumns => _unloaded.Count;

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
        args.GetBool("done"));

    /// <summary>The report it made to its owner, in the server's language, if it made its job's (the
    /// last of its job to finish makes it; the others none).</summary>
    public string? Reported { get; private set; }

    /// <summary>The shared job it is in (README "Eidolon", working together), or null before it starts.</summary>
    public SharedJob<MineJobCells>? Job => _job;

    public void Start(EntityLaborEidolon eidolon)
    {
        _nav ??= new EidolonNavigator(eidolon);
        _eidolon = eidolon;
        _pick ??= eidolon.World.GetItem(new AssetLocation(EidolonMining.PickCode));
        _maxTier = EidolonMining.PickTier(_pick?.ToolTier);
        _jobs ??= EidolonJobSystem.Of(eidolon.Api);
        _group = null;
        _rescanAt = 0;
        _square = null;
        _rim = null;
        if (_done)
            GoHome(eidolon);
        else
        {
            Join(eidolon);
            _phase = Phase.Seek;
        }
    }

    public bool Continue(EntityLaborEidolon eidolon, float dt)
    {
        double now = eidolon.World.ElapsedMilliseconds / 1000.0;
        switch (_phase)
        {
            case Phase.Seek:
                return Seek(eidolon, now);
            case Phase.Walk:
                if (_nav!.Active)
                    return true;
                if (Next(eidolon, now) is { } group && Reserve(eidolon, group.Cells, now))
                {
                    BeginStrike(eidolon, group, now);
                    return true;
                }
                // Arrived short of the place (on a ledge's edge, its box over the block beside): step
                // onto it, straight, once, before counting the walk a miss. Aimed a little past it,
                // as the steer stops 0.2 short, so it ends within 0.15, its box over the place's own
                // columns. Only from close by and at most a block above or below it: the steer never
                // climbs, and must not walk it off a drop, so from anywhere else it walks a path again.
                double dx = 0, dz = 0, d = double.MaxValue;
                if (_stand is { } s)
                {
                    dx = s.X - eidolon.Pos.X;
                    dz = s.Z - eidolon.Pos.Z;
                    d = Math.Sqrt(dx * dx + dz * dz);
                }
                if (_stand is { } stand && !_settled && Math.Abs(Feet(eidolon) - stand.Y) <= SettleRise && d <= SettleWithin)
                {
                    _settled = true;
                    var aim = new Vec3d(stand.X, stand.Y, stand.Z);
                    if (d > 0.01)
                        aim.Add(dx / d * SettlePast, 0, dz / d * SettlePast);
                    if (d > 0.01 && _nav.Steer(aim, false, () => { }))
                        return true;
                }
                if (_square == null || ++_approaches > Approaches || !Approach(eidolon, _square))
                    GiveUp(eidolon);
                return true;
            case Phase.Strike:
                return Strike(eidolon, now);
            case Phase.Return:
                return _nav!.Active;
            case Phase.Travel:
                if (_nav!.Active)
                    return true;
                // there (or as far as it got): search again at once
                _phase = Phase.Seek;
                _rescanAt = 0;
                return true;
            case Phase.Wait:
                // Another member made the report: back to where it was ordered.
                if (_finished && _job is { Closed: true })
                {
                    Told(eidolon);
                    return GoHome(eidolon);
                }
                if (_nav!.Active || now < _waitCheckAt)
                    return true;
                _waitCheckAt = now + WaitCheckSeconds;
                if (_finished)
                {
                    if (Board?.TryClose(eidolon.EntityId, now) == true)
                        return Report(eidolon);
                    if (now < _rescanAt)
                        return true;
                }
                _phase = Phase.Seek;
                return Seek(eidolon, now);
        }
        return true;
    }

    public void Stop(EntityLaborEidolon eidolon, bool cancelled)
    {
        Halt(eidolon);
        // Its columns are freed at once; it leaves its job when done, and is away from it meanwhile
        // (the server's sweep takes it out if the order was replaced).
        if (_done)
            Board?.Leave(eidolon.EntityId);
        else
            Board?.Away(eidolon.EntityId);
        _jobs?.SetRim(eidolon.EntityId, null);
        _rim = null;
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
        _job = jobs.Mining.Join(id, id.ToString(CultureInfo.InvariantCulture), EidolonJobSystem.KeyOf(eidolon, OrderCode, area),
            _expectMates ? _mates : null, EidolonJobSystem.Now(eidolon.World) + EidolonTeamwork.ExpectSeconds);
        _expectMates = false;
        jobs.Track(jobs.Mining, eidolon, this);
        _finished = false;
        jobs.Mining.SetFinished(id, false);
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

    /// <summary>Reserves the 2 × 2 square <paramref name="cells"/> are in, its columns in the area (true
    /// with no jobs on this server).</summary>
    private bool Reserve(EntityLaborEidolon eidolon, IReadOnlyList<MarkPos> cells, double now) =>
        cells.Count == 0 || (Board?.TryReserve(eidolon.EntityId, SquareOf(cells[0]), now) ?? true);

    // The columns of the square a cell is in: the unit a miner reserves.
    private IEnumerable<(int X, int Z)> SquareOf(MarkPos cell) => EidolonMining.SquareColumns(area, EidolonMining.Square(area, cell.X, cell.Z));

    /// <summary>The other eidolons loaded within <see cref="OthersRange"/> of a point, where each stands
    /// and its feet' block: none of them may be undermined.</summary>
    private static List<(double X, int Feet, double Z)> Others(EntityLaborEidolon eidolon, Vec3d at) =>
        eidolon.World.GetEntitiesAround(at, OthersRange, OthersRange, e => e is EntityLaborEidolon && e.EntityId != eidolon.EntityId && e.Alive)
            .Select(e => (e.Pos.X, Feet(e), e.Pos.Z)).ToList();

    /// <summary>Where the others in its job that are at work stand: a square under one of them is
    /// left for now, as it moves on.</summary>
    private List<(double X, int Feet, double Z)> MatesAtWork(EntityLaborEidolon eidolon)
    {
        if (_job == null || _jobs == null)
            return [];
        return _job.Present.Where(id => id != eidolon.EntityId && !_job.IsFinished(id))
            .Select(id => _jobs.Member(id)).OfType<EntityLaborEidolon>()
            .Select(e => (e.Pos.X, Feet(e), e.Pos.Z)).ToList();
    }

    private bool Seek(EntityLaborEidolon eidolon, double now)
    {
        // Waiting for a part of the area to load: search again only now and then.
        if (_waitingSince != null && now < _rescanAt)
            return true;
        if (_layer == null || now >= _rescanAt || !_left.Any(c => c.Y == _layer))
        {
            eidolon.Orders?.SetStatus("seraphhorizons:eidolon-status-mine-seeking");
            Scan(eidolon);
            _rescanAt = now + RescanSeconds;
            Board?.SetLevel(eidolon.EntityId, _layer);
            SaveMates(eidolon);
        }
        if (_layer is not { } layer)
            return _unloaded.Count == 0 ? Finish(eidolon, now) : Travel(eidolon, now);
        _waitingSince = null;
        _stalled = false;
        Unfinish(eidolon);
        // Same layer together: another member still works a layer above its own.
        if (_job?.Level is { } level && level > layer)
            return Wait(eidolon, now);
        if (Next(eidolon, now) is { } group && Reserve(eidolon, group.Cells, now))
        {
            BeginStrike(eidolon, group, now);
            return true;
        }
        // The nearest square with a block its owner may break (the claim asked only now), not held by
        // another nor under a member at work.
        bool taken = false;
        var mates = MatesAtWork(eidolon);
        foreach (var (_, cells) in EidolonMining.Squares(area, _left, layer, eidolon.Pos.X, eidolon.Pos.Z))
        {
            var breakable = cells.Where(Left).ToList();
            if (breakable.Count == 0)
                continue;
            if (breakable.All(c => EidolonMining.Undermines(mates, c)) || !Reserve(eidolon, breakable, now))
            {
                taken = true;
                continue;
            }
            _square = breakable;
            _approaches = 0;
            if (!Approach(eidolon, _square))
                GiveUp(eidolon);
            return true;
        }
        if (taken)
            return Wait(eidolon, now);
        _layer = null;
        return true;
    }

    /// <summary>It has work again after it said it had none.</summary>
    private void Unfinish(EntityLaborEidolon eidolon)
    {
        if (!_finished)
            return;
        _finished = false;
        Board?.SetFinished(eidolon.EntityId, false);
    }

    /// <summary>Nothing it may work now (every square left held by another, a layer above still worked,
    /// or nothing left while the others finish): frees its columns and waits at the area's rim, out of
    /// the pit and out of the others' way, looking again every <see cref="WaitCheckSeconds"/>.</summary>
    private bool Wait(EntityLaborEidolon eidolon, double now)
    {
        Board?.Release(eidolon.EntityId);
        _square = null;
        _phase = Phase.Wait;
        _waitCheckAt = now + WaitCheckSeconds;
        string status = _finished ? "seraphhorizons:eidolon-status-mine-waiting-done" : "seraphhorizons:eidolon-status-mine-waiting";
        // set only when it changes: it waits here every second
        if (eidolon.WatchedAttributes.GetTreeAttribute(EntityBehaviorEidolonOrders.StatusKey)?.GetString("key") != status)
            eidolon.Orders?.SetStatus(status);
        if (_rim != null || _nav!.Active || now < _rimRetryAt)
            return true;
        if (!ToRim(eidolon))
            _rimRetryAt = now + RescanSeconds;
        return true;
    }

    /// <summary>Sets off for a place on the area's rim (<see cref="EidolonTeamwork.Rim"/>) with ground
    /// under it, away from the other eidolons and the places the others wait at; false when it finds
    /// no way to one.</summary>
    private bool ToRim(EntityLaborEidolon eidolon)
    {
        var blocks = eidolon.World.BlockAccessor;
        var space = new GameWideSpace(blocks, eidolon.CollisionBox);
        var pos = new BlockPos(eidolon.Pos.Dimension);
        var centre = new Vec3d((area.Min.X + area.Max.X + 1) / 2.0, (area.Min.Y + area.Max.Y) / 2.0, (area.Min.Z + area.Max.Z + 1) / 2.0);
        float range = area.LongestSide / 2f + EidolonTeamwork.RimGap + 4;
        var taken = eidolon.World.GetEntitiesAround(centre, range, Math.Max(range, area.SizeY + 8),
                e => e is EntityLaborEidolon && e.EntityId != eidolon.EntityId && e.Alive)
            .Select(e => (e.Pos.X, e.Pos.Z))
            .Concat(_jobs?.RimsBut(eidolon.EntityId).Select(r => ((double)r.X, (double)r.Z)) ?? []);
        int looked = 0, tries = 0;
        foreach (var (x, z) in EidolonTeamwork.Rim(area, eidolon.Pos.X, eidolon.Pos.Z, taken))
        {
            if (++looked > RimLooks)
                break;
            // the highest ground it may stand on there, from over the area's top to its bottom
            int? ground = null;
            for (int y = area.Max.Y + 3; y >= Math.Max(1, area.Min.Y); y--)
            {
                if (blocks.GetChunkAtBlockPos(pos.Set(x - 1, y, z - 1)) == null || blocks.GetChunkAtBlockPos(pos.Set(x, y, z)) == null)
                    break;
                if (space.Free(x, y, z) && !space.Free(x, y - 1, z))
                {
                    ground = y;
                    break;
                }
            }
            if (ground is not { } feet)
                continue;
            if (_nav!.GoTo(new Vec3d(x, feet, z), false, () => { }, () => { }, tolerance: 1))
            {
                _rim = (x, z);
                _jobs?.SetRim(eidolon.EntityId, _rim);
                return true;
            }
            if (++tries >= PathTries)
                break;
        }
        return false;
    }

    // Off the rim, back to work.
    private void LeaveRim(EntityLaborEidolon eidolon)
    {
        if (_rim == null)
            return;
        _rim = null;
        _jobs?.SetRim(eidolon.EntityId, null);
    }

    /// <summary>Finds the highest layer with a block left it may break and has not given up, and
    /// gathers that layer's and the next one down's (for upright strikes) into <see cref="_left"/>; and
    /// the columns of the area not loaded, which it has yet to mine. The land claim is left to
    /// <see cref="Left"/>.</summary>
    private void Scan(EntityLaborEidolon eidolon)
    {
        var blocks = eidolon.World.BlockAccessor;
        var pos = new BlockPos(eidolon.Pos.Dimension);
        var chunks = new Dictionary<(int, int), bool>();
        int probeY = Math.Clamp(area.Max.Y, 0, blocks.MapSizeY - 1);
        bool Loaded(int x, int z)
        {
            var key = (x >> 5, z >> 5);
            if (!chunks.TryGetValue(key, out bool loaded))
                chunks[key] = loaded = blocks.GetChunkAtBlockPos(pos.Set(x, probeY, z)) != null;
            return loaded;
        }
        var shared = _job?.Shared.Skip;
        var scan = EidolonMining.Scan(area, Loaded, c => Rule(eidolon, pos.Set(c.X, c.Y, c.Z), claims: false),
            c => _skip.Contains(c) || shared?.Contains(c) == true);
        _layer = scan.Layer;
        _left.Clear();
        _left.UnionWith(scan.Left);
        _unloaded = scan.Unloaded;
        _mayBreak.Clear();
    }

    /// <summary>With nothing left it may break in the loaded part of the area: walks toward the nearest
    /// part not loaded, at most <see cref="TravelBlocks"/> to ground that is loaded, or waits for it to
    /// load, searching again every <see cref="RescanSeconds"/>.</summary>
    private bool Travel(EntityLaborEidolon eidolon, double now)
    {
        Unfinish(eidolon);
        if (EidolonMining.NearestColumn(_unloaded, eidolon.Pos.X, eidolon.Pos.Z) is { } column && TravelTo(eidolon, column))
        {
            LeaveRim(eidolon);
            _phase = Phase.Travel;
            _waitingSince = null;
            _stalled = false;
            eidolon.Orders?.SetStatus("seraphhorizons:eidolon-status-mine-travelling");
            return true;
        }
        _waitingSince ??= now;
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

    /// <summary>What a block is to the order (<see cref="EidolonMining.Rule"/>), read from the world;
    /// the land claim, the dearest test, only for one it would otherwise break, and only when
    /// <paramref name="claims"/>.</summary>
    private MineRule Rule(EntityLaborEidolon eidolon, BlockPos pos, bool claims = true)
    {
        var world = eidolon.World;
        var blocks = world.BlockAccessor;
        var block = blocks.GetBlock(pos);
        bool liquid = block.IsLiquid() || blocks.GetBlock(pos, BlockLayersAccess.Fluid).IsLiquid();
        if (block.Id == 0 && !liquid)
            return MineRule.Air;
        bool beside = false;
        var near = new BlockPos(pos.dimension);
        foreach (var face in BlockFacing.ALLFACES)
        {
            near.Set(pos.X + face.Normali.X, pos.Y + face.Normali.Y, pos.Z + face.Normali.Z);
            if (blocks.GetBlock(near).IsLiquid() || blocks.GetBlock(near, BlockLayersAccess.Fluid).IsLiquid())
            {
                beside = true;
                break;
            }
        }
        int tier = block.GetRequiredMiningTier(world, pos);
        bool pickHas = _pick?.MiningSpeed?.ContainsKey(block.GetBlockMaterial(blocks, pos)) == true;
        // A block entity is left only on a player's thing (EidolonMining.LeavesBlockEntity), not on
        // natural ground that has one (Desire Paths' worn soil).
        bool leaves = false;
        if (block.EntityClass != null || blocks.GetBlockEntity(pos) != null)
        {
            var be = blocks.GetBlockEntity(pos);
            bool inventory = be is IBlockEntityContainer || be?.Behaviors?.Any(b => b is IBlockEntityContainer) == true;
            bool device = be?.Behaviors?.Any(b => b is BEBehaviorMPBase) == true;
            leaves = EidolonMining.LeavesBlockEntity(block.GetBlockMaterial(blocks, pos).ToString(), inventory, device);
        }
        var info = new MineBlock(false, liquid, beside, leaves, true,
            tier, EidolonMining.PickBreaks(tier, pickHas));
        var rule = EidolonMining.Rule(info, _maxTier);
        return claims && rule == MineRule.Mine && !MayBreak(eidolon, pos) ? MineRule.Claimed : rule;
    }

    /// <summary>Whether its owner may break blocks at <paramref name="pos"/> (land claims), as felling
    /// asks: their access when the server knows them, else only where nothing is claimed.</summary>
    private static bool MayBreak(EntityLaborEidolon eidolon, BlockPos pos)
    {
        var world = eidolon.World;
        if (eidolon.OwnerUid is { } uid && world.PlayerByUid(uid) is { } owner)
            return world.Claims.TestAccess(owner, pos, EnumBlockAccessFlags.BuildOrBreak) == EnumWorldAccessResponse.Granted;
        return world.Claims.Get(pos) is not { Length: > 0 };
    }

    /// <summary>Whether a cell is left to mine: found by the search, not mined since (by another
    /// member, say), and its owner may break it there, asked the first time (a block they may not is
    /// left for the order's life, by the whole job).</summary>
    private bool Left(MarkPos cell)
    {
        if (!_left.Contains(cell))
            return false;
        if (_eidolon != null && _eidolon.World.BlockAccessor.GetBlock(_probe.Set(cell.X, cell.Y, cell.Z)).Id == 0)
        {
            _left.Remove(cell);
            return false;
        }
        if (!_mayBreak.TryGetValue(cell, out bool may))
        {
            may = _mayBreak[cell] = _eidolon != null && MayBreak(_eidolon, new BlockPos(cell.X, cell.Y, cell.Z, _eidolon.Pos.Dimension));
            if (!may)
            {
                _left.Remove(cell);
                _skip.Add(cell);
                _job?.Shared.Skip.Add(cell);
            }
        }
        return may;
    }

    private readonly BlockPos _probe = new(0);

    private static int Feet(Entity eidolon) => (int)Math.Floor(eidolon.Pos.Y + 0.01);

    /// <summary>The group it strikes next from where it stands: of the cells left, those in squares no
    /// other eidolon holds and under no eidolon's box (<see cref="EidolonMining.Undermines(double, int, double, MarkPos)"/>).</summary>
    private MineGroup? Next(EntityLaborEidolon eidolon, double now)
    {
        if (_layer is not { } layer)
            return null;
        var others = Others(eidolon, eidolon.Pos.XYZ);
        var board = Board;
        long id = eidolon.EntityId;
        bool Takes(MarkPos c) => Left(c) && (board == null || SquareOf(c).All(u => board.Free(id, u, now))) && !EidolonMining.Undermines(others, c);
        return EidolonMining.Next(area, layer, Takes, eidolon.Pos.X, Feet(eidolon), eidolon.Pos.Z);
    }

    /// <summary>Sets off for a place from which it reaches <paramref name="cells"/>: false when it
    /// finds no way to any.</summary>
    private bool Approach(EntityLaborEidolon eidolon, IReadOnlyList<MarkPos> cells)
    {
        var space = new GameWideSpace(eidolon.World.BlockAccessor, eidolon.CollisionBox);
        int tries = 0;
        foreach (var stand in EidolonMining.Stands(space, cells, Left, eidolon.Pos.X, Feet(eidolon), eidolon.Pos.Z))
        {
            if (_nav!.GoTo(new Vec3d(stand.X, stand.Y, stand.Z), false, () => { }, () => { }, arriveWithin: ArriveWithin))
            {
                LeaveRim(eidolon);
                _phase = Phase.Walk;
                _stand = stand;
                _settled = false;
                eidolon.Orders?.SetStatus("seraphhorizons:eidolon-status-mine-walking");
                return true;
            }
            if (++tries >= PathTries)
                break;
        }
        return false;
    }

    /// <summary>Gives up the square it was making for: no way to a place that reaches it.</summary>
    private void GiveUp(EntityLaborEidolon eidolon)
    {
        _nav?.Stop();
        foreach (var cell in _square ?? [])
        {
            _left.Remove(cell);
            _skip.Add(cell);
            _unreachable.Add(cell);
            _job?.Shared.Unreachable.Add(cell);
        }
        _square = null;
        Board?.Release(eidolon.EntityId);
        _phase = Phase.Seek;
    }

    private void BeginStrike(EntityLaborEidolon eidolon, MineGroup group, double now)
    {
        _nav?.Stop();
        LeaveRim(eidolon);
        _phase = Phase.Strike;
        _group = group;
        _square = null;
        var blocks = eidolon.World.BlockAccessor;
        var pos = new BlockPos(eidolon.Pos.Dimension);
        _strike = EidolonMining.Timing(EidolonMining.StrikeSeconds(group.Cells.Select(c =>
        {
            var block = blocks.GetBlock(pos.Set(c.X, c.Y, c.Z));
            float? speed = _pick?.MiningSpeed is { } speeds && speeds.TryGetValue(block.GetBlockMaterial(blocks, pos), out float s) ? s : null;
            return EidolonMining.PlayerSeconds(block.GetResistance(blocks, pos), EidolonMining.PickSpeed(speed, GlobalConstants.ToolMiningSpeedModifier));
        })));
        _strikeStart = now;
        _slammed = false;
        _struck = false;
        Face(eidolon);
        eidolon.Orders?.SetStatus("seraphhorizons:eidolon-status-mine-mining");
    }

    private bool Strike(EntityLaborEidolon eidolon, double now)
    {
        Face(eidolon);
        double t = now - _strikeStart;
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
        return true;
    }

    /// <summary>Breaks what is still to break in the group, as a player's pick does with no player:
    /// the block's own drops fall in the world. A block that will not break is left, and one under an
    /// eidolon's box (any eidolon's, at or below its feet) is left for now.</summary>
    private void Break(EntityLaborEidolon eidolon)
    {
        if (_group == null)
            return;
        var world = eidolon.World;
        var blocks = world.BlockAccessor;
        int feet = Feet(eidolon), broke = 0;
        bool sounded = false;
        var others = Others(eidolon, new Vec3d(_group.CentreX, _group.CentreY, _group.CentreZ));
        foreach (var cell in _group.Cells)
        {
            if (EidolonMining.Undermines(others, cell))
                continue;
            _left.Remove(cell);
            var pos = new BlockPos(cell.X, cell.Y, cell.Z, eidolon.Pos.Dimension);
            if (!EidolonMining.InReach(eidolon.Pos.X, feet, eidolon.Pos.Z, cell) || Rule(eidolon, pos) != MineRule.Mine)
                continue;
            var block = blocks.GetBlock(pos);
            if (!sounded && block.Sounds?.GetBreakSound(EnumTool.Pickaxe).Location is { } sound)
            {
                world.PlaySoundAt(sound, cell.X + 0.5, cell.Y + 0.5, cell.Z + 0.5, null, true, 24);
                sounded = true;
            }
            blocks.BreakBlock(pos, null);
            if (blocks.GetBlock(pos).Id == block.Id)
            {
                _skip.Add(cell);
                _job?.Shared.Skip.Add(cell);
                continue;
            }
            broke++;
        }
        if (broke == 0)
            return;
        Mined += broke;
        Strikes++;
        _job?.Tally(eidolon.EntityId, broke);
        Board?.Struck(eidolon.EntityId, EidolonJobSystem.Now(world));
        // Last: a dry reservoir stops it at once (the order is cancelled and started again later).
        eidolon.SpendOil(EidolonJob.MiningStrike);
    }

    /// <summary>No block left it may break and reach: says so to its job, and if the job is done (every
    /// member at work has nothing left, none still to load) makes the report; else waits at the rim
    /// for the others.</summary>
    private bool Finish(EntityLaborEidolon eidolon, double now)
    {
        if (!_finished)
        {
            _finished = true;
            Board?.SetFinished(eidolon.EntityId, true);
        }
        if (Board is { } board && _job != null && !board.TryClose(eidolon.EntityId, now))
            return Wait(eidolon, now);
        return Report(eidolon);
    }

    /// <summary>Tells its owner what the job mined (each member's share, when there were more than
    /// one), how many it left and how many were out of reach, and walks back.</summary>
    private bool Report(EntityLaborEidolon eidolon)
    {
        var blocks = eidolon.World.BlockAccessor;
        var pos = new BlockPos(eidolon.Pos.Dimension);
        int left = 0, unreachable = 0;
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
            if (_unreachable.Contains(cell) || _job?.Shared.Unreachable.Contains(cell) == true)
                unreachable++;
            else
                left++;
        }
        var shares = _job?.Shares ?? [];
        if (shares.Count > 1)
        {
            Reported = Lang.Get("seraphhorizons:eidolon-mine-done-shared", shares.Count, _job!.Total,
                EidolonTeamwork.ShareText(shares, Lang.Get("seraphhorizons:eidolon-mine-share", "{0}", "{1}")), left, unreachable);
            TellShares(eidolon, shares, _job.Total, left, unreachable);
        }
        else
        {
            Reported = Lang.Get("seraphhorizons:eidolon-mine-done", Mined, left, unreachable);
            FellOrder.TellOwner(eidolon, "seraphhorizons:eidolon-mine-done", Mined, left, unreachable);
        }
        Told(eidolon);
        return GoHome(eidolon);
    }

    /// <summary>The job's report for more than one: how many, all they mined, each one's share
    /// (<c>eidolon-mine-share</c>, "40 by eidolon 1234", the eidolon by its entity id as the
    /// <c>/sh eidolon</c> command names it), what was left and out of reach.</summary>
    private static void TellShares(EntityLaborEidolon eidolon, IReadOnlyList<JobShare> shares, int total, int left, int unreachable)
    {
        if (eidolon.OwnerUid is not { } uid || eidolon.World.PlayerByUid(uid) is not IServerPlayer { ConnectionState: EnumClientState.Playing } owner)
            return;
        // the share's form with its placeholders kept, to be filled for each
        string each = Lang.GetL(owner.LanguageCode, "seraphhorizons:eidolon-mine-share", "{0}", "{1}");
        owner.SendMessage(GlobalConstants.GeneralChatGroup, Lang.GetL(owner.LanguageCode, "seraphhorizons:eidolon-mine-done-shared",
            shares.Count, total, EidolonTeamwork.ShareText(shares, each), left, unreachable), EnumChatType.Notification);
    }

    /// <summary>Its job is done and reported: it leaves it and saves that it is done, so it goes on
    /// back after a restart rather than mining again.</summary>
    private void Told(EntityLaborEidolon eidolon)
    {
        _done = true;
        _finished = false;
        Board?.Leave(eidolon.EntityId);
        LeaveRim(eidolon);
        Save(eidolon, args => args.SetBool("done", true));
    }

    /// <summary>Walks back to where it was ordered; false (the order over) when it is there or finds
    /// no way.</summary>
    private bool GoHome(EntityLaborEidolon eidolon)
    {
        _phase = Phase.Return;
        if (home == null || eidolon.Pos.XYZ.HorizontalSquareDistanceTo(home) <= HomeSlack * HomeSlack && Math.Abs(eidolon.Pos.Y - home.Y) < 1.5)
            return false;
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
