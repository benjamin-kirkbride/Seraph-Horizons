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
/// grown tree it can reach is left (it tells its owner); with <c>once</c>, after one tree (the crew
/// order, #679, runs it so and reads <see cref="LastStump"/>).
///
/// <para>Without an axe in its hand (<see cref="EntityBehaviorEidolonAxe"/>), or once the axe
/// breaks, it stops and waits, says so and tells its owner once, and goes on when given another.
/// A tree it cannot reach (no way to any place beside it) is skipped for the order's life; so is a
/// young one, and one in a land claim its owner may not break in. Interrupted (self-defence, a slump,
/// dry), it starts again by looking afresh.</para>
/// </summary>
public sealed class FellOrder(MarkArea area, bool once) : IEidolonOrder
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

    private static readonly AssetLocation[] Chops =
        [new("game", "sounds/block/chop1"), new("game", "sounds/block/chop2"), new("game", "sounds/block/chop3")];

    private enum Phase { Seek, Walk, Swing, NoAxe }

    private readonly HashSet<BlockPos> _skip = new();
    private EidolonNavigator? _nav;
    private Phase _phase;
    private BlockPos? _tree;
    private int _approaches;
    private double _nextScan;
    private double _swingStart;
    private int _cuts;
    private bool _felled;
    private bool _toldNoAxe;

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
        args.GetBool("once"));

    public void Start(EntityLaborEidolon eidolon)
    {
        _nav ??= new EidolonNavigator(eidolon);
        _phase = Phase.Seek;
        _tree = null;
        _nextScan = 0;
    }

    public bool Continue(EntityLaborEidolon eidolon, float dt)
    {
        double now = eidolon.World.ElapsedMilliseconds / 1000.0;
        var hand = eidolon.GetBehavior<EntityBehaviorEidolonAxe>();
        if (hand?.Axe == null)
        {
            if (_phase != Phase.NoAxe)
            {
                Halt(eidolon);
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
        }

        switch (_phase)
        {
            case Phase.Seek:
                return Seek(eidolon, hand, now);
            case Phase.Walk:
                if (_tree == null || !StillATree(eidolon, _tree))
                {
                    Halt(eidolon);
                    _phase = Phase.Seek;
                    return true;
                }
                if (_nav!.Active)
                    return true;
                        if (InReach(eidolon, _tree))
                {
                    BeginSwing(eidolon, now);
                    return true;
                }
                if (++_approaches > Approaches || !Approach(eidolon, _tree))
                    GiveUp(eidolon, _tree);
                return true;
            case Phase.Swing:
                return Swing(eidolon, hand, now);
        }
        return true;
    }

    public void Stop(EntityLaborEidolon eidolon, bool cancelled)
    {
        Halt(eidolon);
        _phase = Phase.Seek;
    }

    private bool Seek(EntityLaborEidolon eidolon, EntityBehaviorEidolonAxe hand, double now)
    {
        if (now < _nextScan)
            return true;
        _nextScan = now + ScanSeconds;
        eidolon.Orders?.SetStatus("seraphhorizons:eidolon-status-fell-seeking");
        var axe = (ItemAxe)hand.Axe!.Collectible;
        if (Scan(eidolon, axe) is not { } stump)
        {
            if (!once)
                TellOwner(eidolon, Unreachable > 0 ? "seraphhorizons:eidolon-fell-done-unreachable" : "seraphhorizons:eidolon-fell-done",
                    Felled, Unreachable);
            return false;
        }
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

    /// <summary>The nearest grown wild tree's stump in the area it has not given up, or null.</summary>
    private BlockPos? Scan(EntityLaborEidolon eidolon, ItemAxe axe)
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
            if (!EidolonFelling.IsStump(block.Code.Path, FellingGroup(blocks.GetBlock(below)) == group) || _skip.Contains(pos))
                continue;
            stumps.Add(pos.Copy());
        }
        var config = EidolonSystem.Of(eidolon.Api)?.Config ?? EidolonConfig.Defaults;
        double ex = eidolon.Pos.X, ez = eidolon.Pos.Z;
        foreach (var stump in stumps.OrderBy(s => (s.X + 0.5 - ex) * (s.X + 0.5 - ex) + (s.Z + 0.5 - ez) * (s.Z + 0.5 - ez)).Take(TreesPerScan))
        {
            int wood = axe.FindTree(world, stump, out _, out _).Count(p => blocks.GetBlock(p).BlockMaterial == EnumBlockMaterial.Wood);
            if (EidolonFelling.Mature(wood, config.FellMinLogs) && MayFell(eidolon, stump))
                return stump;
            _skip.Add(stump);
        }
        return null;
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
            int? feet = null;
            foreach (int dy in (ReadOnlySpan<int>)[0, 1, -1, 2, -2])
            {
                int y = stump.Y + dy;
                if (space.Free(x, y, z) && !space.Free(x, y - 1, z))
                {
                    feet = y;
                    break;
                }
            }
            if (feet == null)
                continue;
            if (_nav!.GoTo(new Vec3d(x, feet.Value, z), false, () => { }, () => { }))
            {
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
        Unreachable++;
        _tree = null;
        _phase = Phase.Seek;
    }

    private void BeginSwing(EntityLaborEidolon eidolon, double now)
    {
        _nav?.Stop();
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
                _skip.Add(stump);
                break;
            }
            hand.Save();
            Felled++;
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
        _tree = null;
        _phase = Phase.Seek;
        _nextScan = now;
        return !(once && Felled > 0);
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

    private static void TellOwner(EntityLaborEidolon eidolon, string langKey, params object[] args)
    {
        if (eidolon.OwnerUid is { } uid && eidolon.World.PlayerByUid(uid) is IServerPlayer { ConnectionState: EnumClientState.Playing } owner)
            owner.SendMessage(GlobalConstants.GeneralChatGroup, Lang.GetL(owner.LanguageCode, langKey, args), EnumChatType.Notification);
    }
}
