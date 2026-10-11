using SeraphHorizons.Mod.Eidolon.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

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
/// <para>Blocks it cannot reach (no way to any place beside them) are given up for the order's life.
/// Done when no block it may break and reach is left: it tells its owner how many it mined and how
/// many it left, walks back to where it was ordered (if it can) and the order ends. Interrupted
/// (self-defence, a slump, dry), it starts again by looking afresh; interrupted on its way back, it
/// goes on back.</para>
/// </summary>
public sealed class MineOrder(MarkArea area, Vec3d? home) : IEidolonOrder
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

    private const string SlamAnimation = "slam";

    private enum Phase { Seek, Walk, Strike, Return }

    private readonly HashSet<MarkPos> _unreachable = new();
    private readonly HashSet<MarkPos> _skip = new();
    private readonly HashSet<MarkPos> _left = new();
    private EidolonNavigator? _nav;
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
    private bool _done;
    private Item? _pick;

    public string Code => OrderCode;

    public MarkArea Area => area;

    /// <summary>Blocks it broke.</summary>
    public int Mined { get; private set; }

    /// <summary>Strikes it struck (each breaking at least one block).</summary>
    public int Strikes { get; private set; }

    /// <summary>Blocks it gave up for want of a way to them.</summary>
    public int Unreachable => _unreachable.Count;

    /// <summary>Whether it still has blocks to mine: false once it has told its owner it is done
    /// (walking back is not work). For keeping its chunks loaded.</summary>
    public bool WorkLeft => !_done;

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
        args.HasAttribute("homeX") ? new Vec3d(args.GetDouble("homeX"), args.GetDouble("homeY"), args.GetDouble("homeZ")) : null);

    public void Start(EntityLaborEidolon eidolon)
    {
        _nav ??= new EidolonNavigator(eidolon);
        _pick ??= eidolon.World.GetItem(new AssetLocation(EidolonMining.PickCode));
        _group = null;
        _rescanAt = 0;
        _square = null;
        if (_done)
            GoHome(eidolon);
        else
            _phase = Phase.Seek;
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
                if (Next(eidolon) is { } group)
                {
                    BeginStrike(eidolon, group, now);
                    return true;
                }
                // Arrived short of the place (on a ledge's edge, its box over the block beside): step
                // onto it, straight, once, before counting the walk a miss. Aimed a little past it,
                // as the steer stops 0.2 short, so it ends within 0.15, its box over the place's own
                // columns.
                if (_stand is { } stand && !_settled)
                {
                    _settled = true;
                    double dx = stand.X - eidolon.Pos.X, dz = stand.Z - eidolon.Pos.Z, d = Math.Sqrt(dx * dx + dz * dz);
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
        }
        return true;
    }

    public void Stop(EntityLaborEidolon eidolon, bool cancelled)
    {
        Halt(eidolon);
        _phase = Phase.Seek;
    }

    private bool Seek(EntityLaborEidolon eidolon, double now)
    {
        if (_layer == null || now >= _rescanAt || !_left.Any(c => c.Y == _layer))
        {
            eidolon.Orders?.SetStatus("seraphhorizons:eidolon-status-mine-seeking");
            Scan(eidolon);
            _rescanAt = now + RescanSeconds;
        }
        if (_layer is not { } layer)
            return Finish(eidolon);
        if (Next(eidolon) is { } group)
        {
            BeginStrike(eidolon, group, now);
            return true;
        }
        if (EidolonMining.Nearest(area, _left, layer, eidolon.Pos.X, eidolon.Pos.Z) is not { } nearest)
        {
            _layer = null;
            return true;
        }
        _square = nearest.Cells;
        _approaches = 0;
        if (!Approach(eidolon, _square))
            GiveUp(eidolon);
        return true;
    }

    /// <summary>Finds the highest layer with a block left it may break and has not given up, and
    /// gathers that layer's and the next one down's (for upright strikes) into <see cref="_left"/>.</summary>
    private void Scan(EntityLaborEidolon eidolon)
    {
        _left.Clear();
        _layer = null;
        var pos = new BlockPos(eidolon.Pos.Dimension);
        for (int y = area.Max.Y; y >= Math.Max(1, area.Min.Y) && (_layer == null || y >= _layer - 1); y--)
        for (int x = area.Min.X; x <= area.Max.X; x++)
        for (int z = area.Min.Z; z <= area.Max.Z; z++)
        {
            var cell = new MarkPos(x, y, z);
            if (_skip.Contains(cell) || Rule(eidolon, pos.Set(x, y, z)) != MineRule.Mine)
                continue;
            _layer ??= y;
            _left.Add(cell);
        }
    }

    /// <summary>What a block is to the order (<see cref="EidolonMining.Rule"/>), read from the world;
    /// the land claim, the dearest test, only for one it would otherwise break.</summary>
    private MineRule Rule(EntityLaborEidolon eidolon, BlockPos pos)
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
        var info = new MineBlock(false, liquid, beside, block.EntityClass != null || blocks.GetBlockEntity(pos) != null, true,
            tier, EidolonMining.PickBreaks(tier, pickHas));
        var rule = EidolonMining.Rule(info);
        return rule == MineRule.Mine && !MayBreak(eidolon, pos) ? MineRule.Claimed : rule;
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

    private bool Left(MarkPos cell) => _left.Contains(cell);

    private static int Feet(EntityLaborEidolon eidolon) => (int)Math.Floor(eidolon.Pos.Y + 0.01);

    private MineGroup? Next(EntityLaborEidolon eidolon) =>
        _layer is { } layer ? EidolonMining.Next(area, layer, Left, eidolon.Pos.X, Feet(eidolon), eidolon.Pos.Z) : null;

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
        }
        _square = null;
        _phase = Phase.Seek;
    }

    private void BeginStrike(EntityLaborEidolon eidolon, MineGroup group, double now)
    {
        _nav?.Stop();
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
    /// the block's own drops fall in the world. A block that will not break is left.</summary>
    private void Break(EntityLaborEidolon eidolon)
    {
        if (_group == null)
            return;
        var world = eidolon.World;
        var blocks = world.BlockAccessor;
        int feet = Feet(eidolon), broke = 0;
        bool sounded = false;
        foreach (var cell in _group.Cells)
        {
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
                continue;
            }
            broke++;
        }
        if (broke == 0)
            return;
        Mined += broke;
        Strikes++;
        // Last: a dry reservoir stops it at once (the order is cancelled and started again later).
        eidolon.SpendOil(EidolonJob.MiningStrike);
    }

    /// <summary>No block left it may break and reach: tells its owner and walks back.</summary>
    private bool Finish(EntityLaborEidolon eidolon)
    {
        _done = true;
        var blocks = eidolon.World.BlockAccessor;
        var pos = new BlockPos(eidolon.Pos.Dimension);
        int left = 0, unreachable = 0;
        for (int y = Math.Max(1, area.Min.Y); y <= area.Max.Y; y++)
        for (int x = area.Min.X; x <= area.Max.X; x++)
        for (int z = area.Min.Z; z <= area.Max.Z; z++)
        {
            var block = blocks.GetBlock(pos.Set(x, y, z));
            if (block.Id == 0 || block.IsLiquid())
                continue;
            if (_unreachable.Contains(new MarkPos(x, y, z)))
                unreachable++;
            else
                left++;
        }
        FellOrder.TellOwner(eidolon, "seraphhorizons:eidolon-mine-done", Mined, left, unreachable);
        return GoHome(eidolon);
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
