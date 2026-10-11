using System.Globalization;
using SeraphHorizons.Mod.Eidolon.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.Eidolon;

/// <summary>
/// Fell the grown trees in a marked area (#677; README "Eidolon", felling; arguments the area's
/// corners <c>minX</c> ... <c>maxZ</c>, and <c>once</c>). It looks for the nearest grown wild tree whose
/// stump stands in the area (<see cref="Scan"/>, at most every <see cref="ScanSeconds"/>; a stump from
/// <see cref="EidolonFelling.AreaHeightSlack"/> below the lower corner to as far above the higher),
/// walks to a place beside it (<see cref="EidolonFelling.StandCorners"/>), faces it and swings
/// (<c>fell</c>) <see cref="EidolonFelling.Swings"/> times; the last cut fells it as a player's axe
/// would (<see cref="EidolonFeller.Fell"/>), replants it from what it carries
/// (<see cref="EidolonFeller.Replant"/>) and costs oil (<see cref="EidolonJob.TreeFelled"/>). Done when no
/// grown tree it can reach is left (it tells its owner); with <c>once</c>, after one tree since it was
/// last started, telling no one (the crew order, #679, runs one instance so, starting it again for
/// each tree, and reads <see cref="Felled"/>, <see cref="LastStump"/> and <see cref="Clear"/>).
///
/// <para>Without an axe in its hand (<see cref="EntityBehaviorEidolonAxe"/>), or once the axe
/// breaks, it stops and waits, says so and tells its owner once, and goes on when given another.
/// A tree it cannot reach (no way to any place beside it) is skipped for the order's life; so is a
/// young one, and one in a land claim its owner may not break in. Interrupted (self-defence, a slump,
/// dry), it starts again by looking afresh.</para>
///
/// <para>Eidolons of one owner given the very same area fell it as one job (README "Eidolon", working
/// together; <see cref="EidolonJobSystem.Felling"/>, <see cref="JobBoard{TUnit, TShared}"/>): each
/// reserves the stump it makes for before it sets off, and passes over those another holds; a tree
/// passed over for what it is (young, claimed, one that would not fall) is passed over by all
/// (<see cref="FellJobCells.Skip"/>); one with no tree free waits at the area's rim
/// (<see cref="RimWait"/>); and the last to find nothing left tells the owner what each felled, and
/// the order ends for each where it stands. Each saves the ids of the others in its job with its order
/// (<c>mates</c>), which the job, formed again on load, waits for
/// (<see cref="EidolonTeamwork.ExpectSeconds"/>). Not with <c>once</c>: the crew order fells alone.</para>
/// </summary>
public sealed class FellOrder(MarkArea area, bool once, IReadOnlyList<long>? mates = null, bool done = false) : IEidolonOrder
{
    public const string OrderCode = "fell";

    /// <summary>How often, at most, it searches the area for a tree.</summary>
    public const double ScanSeconds = 2;

    /// <summary>Trees it weighs (the axe's tree search) per scan, nearest first.</summary>
    private const int TreesPerScan = 24;

    /// <summary>Places beside a tree it searches a path to before it gives the tree up.</summary>
    private const int PathTries = 3;

    /// <summary>Times it sets off again for a tree it arrived out of reach of.</summary>
    private const int Approaches = 2;

    /// <summary>How often a waiting eidolon looks for a tree freed or its job done.</summary>
    private const double WaitCheckSeconds = 1;

    /// <summary>How often, at most, one with nothing left searches the area again while it waits for
    /// the others (the world may change), and how long it waits to look for a way to the rim again.</summary>
    private const double WaitScanSeconds = 10;

    private const string SharedDoneKey = "seraphhorizons:eidolon-fell-done-shared";
    private const string SharedDoneUnreachableKey = "seraphhorizons:eidolon-fell-done-shared-unreachable";
    private const string ShareKey = "seraphhorizons:eidolon-fell-share";

    private static readonly AssetLocation[] Chops =
        [new("game", "sounds/block/chop1"), new("game", "sounds/block/chop2"), new("game", "sounds/block/chop3")];

    private enum Phase { Seek, Walk, Swing, NoAxe, Wait }

    private readonly HashSet<BlockPos> _skip = new();
    private readonly HashSet<MarkPos> _unreachable = new();
    private readonly RimWait _rim = new(area, EidolonFelling.AreaHeightSlack);
    private EidolonNavigator? _nav;
    private Phase _phase;
    private BlockPos? _tree;
    private int _approaches;
    private double _nextScan;
    private double _swingStart;
    private int _cuts;
    private bool _felled;
    private bool _toldNoAxe;
    private int _felledAtStart;
    private EidolonJobSystem? _jobs;
    private SharedJob<FellJobCells>? _job;
    private IReadOnlyList<long> _mates = mates ?? [];
    private bool _expectMates = true;
    private bool _finished;
    private bool _done = done;
    private double _waitCheckAt;

    public string Code => OrderCode;

    public MarkArea Area => area;

    /// <summary>Trees felled by this order.</summary>
    public int Felled { get; private set; }

    /// <summary>Trees it replanted.</summary>
    public int Replanted { get; private set; }

    /// <summary>Trees it gave up for want of a way to them.</summary>
    public int Unreachable { get; private set; }

    /// <summary>Where the last tree it felled stood (its stump), or null.</summary>
    public BlockPos? LastStump { get; private set; }

    /// <summary>Whether its last search found no grown tree it may fell and can reach (until it is
    /// started again): what tells the crew order an area is clear from a tree that would not fall.</summary>
    public bool Clear { get; private set; }

    /// <summary>Whether it waits for an axe (none in its hand, or it broke).</summary>
    public bool WaitsForAxe => _phase == Phase.NoAxe;

    // Done is no order at all; waiting for an axe waits on its player.
    public bool HasWorkLeft => !WaitsForAxe;

    public IEnumerable<MarkArea> WorkAreas => [area];

    public static ITreeAttribute Args(MarkArea area, bool once = false)
    {
        var tree = new TreeAttribute();
        tree.SetInt("minX", area.Min.X);
        tree.SetInt("minY", area.Min.Y);
        tree.SetInt("minZ", area.Min.Z);
        tree.SetInt("maxX", area.Max.X);
        tree.SetInt("maxY", area.Max.Y);
        tree.SetInt("maxZ", area.Max.Z);
        tree.SetBool("once", once);
        return tree;
    }

    public static FellOrder From(ITreeAttribute args) => new(
        new MarkArea(new MarkPos(args.GetInt("minX"), args.GetInt("minY"), args.GetInt("minZ")),
            new MarkPos(args.GetInt("maxX"), args.GetInt("maxY"), args.GetInt("maxZ"))),
        args.GetBool("once"),
        (args.GetString("mates") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(m => long.TryParse(m, NumberStyles.Integer, CultureInfo.InvariantCulture, out long id) ? id : 0).Where(id => id != 0).ToList(),
        args.GetBool("done"));

    /// <summary>The report it made to its owner, in the server's language, if it made its job's (the
    /// last of its job to finish makes it; the others none). Not with <c>once</c>.</summary>
    public string? Reported { get; private set; }

    /// <summary>The shared job it is in (README "Eidolon", working together), or null before it starts
    /// and with <c>once</c>.</summary>
    public SharedJob<FellJobCells>? Job => _job;

    public void Start(EntityLaborEidolon eidolon)
    {
        _nav ??= new EidolonNavigator(eidolon);
        if (!once)
            _jobs ??= EidolonJobSystem.Of(eidolon.Api);
        _phase = Phase.Seek;
        _tree = null;
        _nextScan = 0;
        _felledAtStart = Felled;
        Clear = false;
        _rim.Leave(eidolon, _jobs);
        if (!_done)
            Join(eidolon);
    }

    public bool Continue(EntityLaborEidolon eidolon, float dt)
    {
        // its job reported (while it was away, or before a restart): the order ends, no report of its own
        if (_done)
            return false;
        double now = eidolon.World.ElapsedMilliseconds / 1000.0;
        var hand = eidolon.GetBehavior<EntityBehaviorEidolonAxe>();
        if (hand?.Axe == null)
        {
            if (_phase != Phase.NoAxe)
            {
                Halt(eidolon);
                // waiting on its player: its tree is freed, and it holds up no report
                Board?.Away(eidolon.EntityId);
                _rim.Leave(eidolon, _jobs);
                _phase = Phase.NoAxe;
                eidolon.Orders?.SetStatus("seraphhorizons:eidolon-status-fell-noaxe");
                if (!_toldNoAxe)
                    TellOwner(eidolon, "seraphhorizons:eidolon-fell-noaxe-told");
                _toldNoAxe = true;
            }
            return true;
        }
        if (_phase == Phase.NoAxe)
        {
            _phase = Phase.Seek;
            _toldNoAxe = false;
            _nextScan = 0;
            Join(eidolon);
            if (_done)
                return false;
        }

        switch (_phase)
        {
            case Phase.Seek:
                return Seek(eidolon, hand, now);
            case Phase.Walk:
                if (_tree == null || !StillATree(eidolon, _tree))
                {
                    Halt(eidolon);
                    Board?.Release(eidolon.EntityId);
                    _phase = Phase.Seek;
                    return true;
                }
                if (_nav!.Active)
                    return true;
                if (InReach(eidolon, _tree))
                {
                    // its reservation may have lapsed on a long walk, and another taken the tree
                    if (Reserve(eidolon, _tree, now))
                        BeginSwing(eidolon, now);
                    else
                    {
                        _tree = null;
                        _phase = Phase.Seek;
                    }
                    return true;
                }
                if (++_approaches > Approaches || !Approach(eidolon, _tree))
                    GiveUp(eidolon, _tree);
                return true;
            case Phase.Swing:
                return Swing(eidolon, hand, now);
            case Phase.Wait:
                // Another member made the report: the order ends where it stands.
                if (_finished && _job is { Closed: true })
                {
                    Told(eidolon);
                    return false;
                }
                if (_nav!.Active || now < _waitCheckAt)
                    return true;
                _waitCheckAt = now + WaitCheckSeconds;
                if (_finished && Board?.TryClose(eidolon.EntityId, now) == true)
                {
                    _jobs!.SaveDone(_job!);
                    return Report(eidolon);
                }
                return Seek(eidolon, hand, now);
        }
        return true;
    }

    public void Stop(EntityLaborEidolon eidolon, bool cancelled)
    {
        Halt(eidolon);
        // Its tree is freed at once; it leaves its job when done, and is away from it meanwhile (the
        // server's sweep takes it out if the order was replaced).
        if (_done)
            Board?.Leave(eidolon.EntityId);
        else
            Board?.Away(eidolon.EntityId);
        _rim.Leave(eidolon, _jobs);
        _phase = Phase.Seek;
    }

    private JobBoard<MarkPos, FellJobCells>? Board => _jobs?.Felling;

    /// <summary>Joins (or rejoins) its job: the one of its owner's eidolons over the very same area, or
    /// a new one (not with <c>once</c>). The first time, it brings the ids of the others it worked with
    /// (saved with the order), for the job to wait for while they load.</summary>
    private void Join(EntityLaborEidolon eidolon)
    {
        if (_jobs is not { } jobs)
            return;
        long id = eidolon.EntityId;
        // A job closed while it was away (no axe, dry, unloaded) is rejoined, closed: the order ends with
        // no report of its own. A new order (a player's, no mates saved) to the same area makes a new job.
        _job = jobs.Felling.Join(id, id.ToString(CultureInfo.InvariantCulture), EidolonJobSystem.KeyOf(eidolon, OrderCode, area),
            _expectMates ? _mates : null, EidolonJobSystem.Now(eidolon.World) + EidolonTeamwork.ExpectSeconds,
            rejoinClosed: !_expectMates || _mates.Count > 0);
        _expectMates = false;
        if (_job.Closed)
        {
            Told(eidolon);
            return;
        }
        jobs.Track(jobs.Felling, eidolon, this);
        _finished = false;
        jobs.Felling.SetFinished(id, false);
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
        if (eidolon.Orders is not { } orders || !ReferenceEquals(orders.Current, this) || orders.OrderArgs is not { } args)
            return;
        args.SetString("mates", string.Join(",", mates.Select(m => m.ToString(CultureInfo.InvariantCulture))));
        eidolon.WatchedAttributes.MarkPathDirty(EntityBehaviorEidolonOrders.OrderKey);
    }

    /// <summary>Reserves the tree whose stump is <paramref name="stump"/> (true with no job: alone, or
    /// with <c>once</c>).</summary>
    private bool Reserve(EntityLaborEidolon eidolon, BlockPos stump, double now) =>
        Board?.TryReserve(eidolon.EntityId, [Unit(stump)], now) ?? true;

    private static MarkPos Unit(BlockPos stump) => new(stump.X, stump.Y, stump.Z);

    /// <summary>Passes over a tree for what it is (young, claimed, would not fall), it and the whole job.</summary>
    private void Skip(BlockPos stump)
    {
        _skip.Add(stump.Copy());
        _job?.Shared.Skip.Add(Unit(stump));
    }

    private bool Seek(EntityLaborEidolon eidolon, EntityBehaviorEidolonAxe hand, double now)
    {
        if (now < _nextScan)
            return true;
        _nextScan = now + ScanSeconds;
        // a waiting one keeps its waiting status while it looks
        if (_phase != Phase.Wait)
            eidolon.Orders?.SetStatus("seraphhorizons:eidolon-status-fell-seeking");
        var axe = (ItemAxe)hand.Axe!.Collectible;
        var (found, taken) = Scan(eidolon, axe, now);
        SaveMates(eidolon);
        if (found is not { } stump)
        {
            // trees left, but held by others: not done, as one may come free; wait at the rim
            if (taken)
            {
                Unfinish(eidolon);
                return Wait(eidolon, now);
            }
            Clear = true;
            if (once)
                return false;
            return Finish(eidolon, now);
        }
        Unfinish(eidolon);
        _tree = stump;
        _approaches = 0;
        if (InReach(eidolon, stump))
        {
            BeginSwing(eidolon, now);
            return true;
        }
        if (!Approach(eidolon, stump))
        {
            GiveUp(eidolon, stump);
            _nextScan = now;
            return true;
        }
        return true;
    }

    /// <summary>The nearest grown wild tree's stump in the area it has not given up and no other
    /// eidolon holds, reserved (or null), and whether it passed over one another holds.</summary>
    private (BlockPos? Stump, bool Taken) Scan(EntityLaborEidolon eidolon, ItemAxe axe, double now)
    {
        var world = eidolon.World;
        var blocks = world.BlockAccessor;
        int dim = eidolon.Pos.Dimension;
        var stumps = new List<BlockPos>();
        var pos = new BlockPos(dim);
        var below = new BlockPos(dim);
        int lowY = area.Min.Y - EidolonFelling.AreaHeightSlack, highY = area.Max.Y + EidolonFelling.AreaHeightSlack;
        for (int x = area.Min.X; x <= area.Max.X; x++)
        for (int z = area.Min.Z; z <= area.Max.Z; z++)
        for (int y = Math.Max(1, lowY); y <= highY; y++)
        {
            pos.Set(x, y, z);
            var block = blocks.GetBlock(pos);
            if (block.Id == 0 || FellingGroup(block) is not { } group
                || !EidolonFelling.IsWildLog(block.Code.Path, true, block.BlockMaterial == EnumBlockMaterial.Wood))
                continue;
            below.Set(x, y - 1, z);
            if (!EidolonFelling.IsStump(block.Code.Path, FellingGroup(blocks.GetBlock(below)) == group) || _skip.Contains(pos)
                || _job?.Shared.Skip.Contains(Unit(pos)) == true)
                continue;
            stumps.Add(pos.Copy());
        }
        var config = EidolonSystem.Of(eidolon.Api)?.Config ?? EidolonConfig.Defaults;
        double ex = eidolon.Pos.X, ez = eidolon.Pos.Z;
        var board = Board;
        long id = eidolon.EntityId;
        // trees another eidolon holds are passed over before the weighing, which they do not count to
        int held = stumps.RemoveAll(s => board != null && !board.Free(id, Unit(s), now));
        foreach (var stump in stumps.OrderBy(s => (s.X + 0.5 - ex) * (s.X + 0.5 - ex) + (s.Z + 0.5 - ez) * (s.Z + 0.5 - ez)).Take(TreesPerScan))
        {
            int wood = axe.FindTree(world, stump, out _, out _).Count(p => blocks.GetBlock(p).BlockMaterial == EnumBlockMaterial.Wood);
            if (!EidolonFelling.Mature(wood, config.FellMinLogs) || !MayFell(eidolon, stump))
            {
                Skip(stump);
                continue;
            }
            if (Reserve(eidolon, stump, now))
                return (stump, held > 0);
            held++;
        }
        return (null, held > 0);
    }

    private static string? FellingGroup(Block block) => block.Attributes?["treeFellingGroupCode"].AsString();

    /// <summary>Whether its owner may break blocks at <paramref name="pos"/> (land claims): their
    /// access when the server knows them, else only where nothing is claimed.</summary>
    private static bool MayFell(EntityLaborEidolon eidolon, BlockPos pos)
    {
        var world = eidolon.World;
        if (eidolon.OwnerUid is { } uid && world.PlayerByUid(uid) is { } owner)
            return world.Claims.TestAccess(owner, pos, EnumBlockAccessFlags.BuildOrBreak) == EnumWorldAccessResponse.Granted;
        return world.Claims.Get(pos) is not { Length: > 0 };
    }

    private static bool StillATree(EntityLaborEidolon eidolon, BlockPos stump) => FellingGroup(eidolon.World.BlockAccessor.GetBlock(stump)) != null;

    private static bool InReach(EntityLaborEidolon eidolon, BlockPos stump) =>
        EidolonFelling.InReach(eidolon.Pos.X, eidolon.Pos.Z, stump.X, stump.Z) && Math.Abs(eidolon.Pos.Y - stump.Y) <= 2;

    /// <summary>Sets off for a place beside the tree: false when it finds no way to any.</summary>
    private bool Approach(EntityLaborEidolon eidolon, BlockPos stump)
    {
        var space = new GameWideSpace(eidolon.World.BlockAccessor, eidolon.CollisionBox);
        int tries = 0;
        foreach (var (x, z) in EidolonFelling.StandCorners(stump.X, stump.Z, eidolon.Pos.X, eidolon.Pos.Z))
        {
            if (WidePath.Feet(space, x, stump.Y, z, [0, 1, -1, 2, -2]) is not { } feet)
                continue;
            if (_nav!.GoTo(new Vec3d(x, feet, z), false, () => { }, () => { }))
            {
                _rim.Leave(eidolon, _jobs);
                _phase = Phase.Walk;
                eidolon.Orders?.SetStatus("seraphhorizons:eidolon-status-fell-walking");
                return true;
            }
            if (++tries >= PathTries)
                break;
        }
        return false;
    }

    private void GiveUp(EntityLaborEidolon eidolon, BlockPos stump)
    {
        Halt(eidolon);
        _skip.Add(stump);
        if (_unreachable.Add(Unit(stump)))
            Unreachable++;
        _job?.Shared.Unreachable.Add(Unit(stump));
        Board?.Release(eidolon.EntityId);
        _tree = null;
        _phase = Phase.Seek;
    }

    private void BeginSwing(EntityLaborEidolon eidolon, double now)
    {
        _nav?.Stop();
        _rim.Leave(eidolon, _jobs);
        _phase = Phase.Swing;
        _swingStart = now;
        _cuts = 0;
        _felled = false;
        Face(eidolon);
        eidolon.AnimManager.StartAnimation("fell");
        eidolon.Orders?.SetStatus("seraphhorizons:eidolon-status-fell-felling");
    }

    private bool Swing(EntityLaborEidolon eidolon, EntityBehaviorEidolonAxe hand, double now)
    {
        Face(eidolon);
        double t = now - _swingStart;
        while (!_felled && _cuts < EidolonFelling.Swings && t >= EidolonFelling.CutAt(_cuts))
        {
            _cuts++;
            var stump = _tree!;
            Board?.Struck(eidolon.EntityId, now);
            eidolon.World.PlaySoundAt(Chops[eidolon.World.Rand.Next(Chops.Length)], stump.X + 0.5, stump.Y + 1, stump.Z + 0.5, null, true, 24);
            if (_cuts < EidolonFelling.Swings)
                continue;
            _felled = true;
            if (!StillATree(eidolon, stump))
                break;
            var block = eidolon.World.BlockAccessor.GetBlock(stump);
            string? wood = block.Variant?["wood"];
            if (!EidolonFeller.Fell(eidolon, hand.Slot, stump))
            {
                Skip(stump);
                break;
            }
            hand.Save();
            Felled++;
            _job?.Tally(eidolon.EntityId, 1);
            LastStump = stump.Copy();
            if (wood != null && EidolonFeller.Replant(eidolon, stump, wood))
                Replanted++;
            if (hand.Axe == null)
                TellOwner(eidolon, "seraphhorizons:eidolon-fell-axe-broke");
            // Last: a dry reservoir stops it at once (the order is cancelled and started again later).
            eidolon.SpendOil(EidolonJob.TreeFelled);
        }
        if (t < EidolonFelling.Swings * EidolonFelling.SwingSeconds)
            return true;
        eidolon.AnimManager.StopAnimation("fell");
        Board?.Release(eidolon.EntityId);
        _tree = null;
        _phase = Phase.Seek;
        _nextScan = now;
        return !(once && Felled > _felledAtStart);
    }

    /// <summary>It has a tree again after it said it had none.</summary>
    private void Unfinish(EntityLaborEidolon eidolon)
    {
        if (!_finished)
            return;
        _finished = false;
        Board?.SetFinished(eidolon.EntityId, false);
    }

    /// <summary>No tree it may take now (every one left held by another, or nothing left while the
    /// others finish): frees its tree and waits at the area's rim, out of the others' way
    /// (<see cref="RimWait"/>), looking again every <see cref="WaitCheckSeconds"/>.</summary>
    private bool Wait(EntityLaborEidolon eidolon, double now)
    {
        Board?.Release(eidolon.EntityId);
        _tree = null;
        _phase = Phase.Wait;
        _waitCheckAt = now + WaitCheckSeconds;
        _rim.Wait(eidolon, _nav!, _jobs,
            _finished ? "seraphhorizons:eidolon-status-fell-waiting-done" : "seraphhorizons:eidolon-status-fell-waiting",
            "seraphhorizons:eidolon-status-fell-waiting-aside", AsideClear(eidolon.EntityId, now), now, WaitScanSeconds);
        return true;
    }

    /// <summary>Where it may step aside to when it cannot reach the rim: a path node further than a
    /// feller's farthest stand (<see cref="EidolonFelling.StandFar"/>, and a block more) from every tree
    /// another holds.</summary>
    private System.Func<int, int, bool> AsideClear(long id, double now)
    {
        var held = Board?.HeldByOthers(id, now) ?? [];
        const double clear = EidolonFelling.StandFar + 1;
        return (x, z) => held.All(s => (s.X + 0.5 - x) * (s.X + 0.5 - x) + (s.Z + 0.5 - z) * (s.Z + 0.5 - z) > clear * clear);
    }

    /// <summary>No grown tree left it may fell and reach: says so to its job, and if the job is done
    /// (every member at work has nothing left, none still to load) makes the report; else waits at the
    /// rim for the others, searching again every <see cref="WaitScanSeconds"/>.</summary>
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
            {
                _nextScan = now + WaitScanSeconds;
                return Wait(eidolon, now);
            }
            // every member's order saved done, so none fells and reports again after a restart
            _jobs!.SaveDone(_job);
        }
        return Report(eidolon);
    }

    /// <summary>Tells its owner what the job felled (each member's share, when there were more than
    /// one) and how many were out of reach; the order ends where it stands (false).</summary>
    private bool Report(EntityLaborEidolon eidolon)
    {
        var shares = _job?.Shares ?? [];
        if (shares.Count > 1)
        {
            // the trees some member gave up that still stand: another may have felled one
            var blocks = eidolon.World.BlockAccessor;
            var pos = new BlockPos(eidolon.Pos.Dimension);
            int unreachable = _job!.Shared.Unreachable.Count(c => FellingGroup(blocks.GetBlock(pos.Set(c.X, c.Y, c.Z))) != null);
            string key = unreachable > 0 ? SharedDoneUnreachableKey : SharedDoneKey;
            Reported = EidolonJobSystem.ShareReport(key, ShareKey, shares, _job.Total, unreachable);
            EidolonJobSystem.TellShares(eidolon, key, ShareKey, shares, _job.Total, unreachable);
        }
        else
        {
            string key = Unreachable > 0 ? "seraphhorizons:eidolon-fell-done-unreachable" : "seraphhorizons:eidolon-fell-done";
            Reported = Lang.Get(key, Felled, Unreachable);
            TellOwner(eidolon, key, Felled, Unreachable);
        }
        Told(eidolon);
        return false;
    }

    /// <summary>Its job is done and reported: it leaves it.</summary>
    private void Told(EntityLaborEidolon eidolon)
    {
        _done = true;
        _finished = false;
        Board?.Leave(eidolon.EntityId);
        _rim.Leave(eidolon, _jobs);
    }

    private void Face(EntityLaborEidolon eidolon)
    {
        if (_tree == null)
            return;
        float yaw = EidolonFelling.Yaw(eidolon.Pos.X, eidolon.Pos.Z, _tree.X + 0.5, _tree.Z + 0.5);
        eidolon.Pos.Yaw = yaw;
        eidolon.BodyYaw = yaw;
    }

    private void Halt(EntityLaborEidolon eidolon)
    {
        _nav?.Stop();
        eidolon.AnimManager.StopAnimation("fell");
    }

    internal static void TellOwner(EntityLaborEidolon eidolon, string langKey, params object[] args)
    {
        if (eidolon.OwnerUid is { } uid && eidolon.World.PlayerByUid(uid) is IServerPlayer { ConnectionState: EnumClientState.Playing } owner)
            owner.SendMessage(GlobalConstants.GeneralChatGroup, Lang.GetL(owner.LanguageCode, langKey, args), EnumChatType.Notification);
    }
}
