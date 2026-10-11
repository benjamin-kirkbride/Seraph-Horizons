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

    private const string SlamAnimation = "slam";

    private enum Phase { Seek, Walk, Strike, Return, Travel }

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
        args.HasAttribute("homeX") ? new Vec3d(args.GetDouble("homeX"), args.GetDouble("homeY"), args.GetDouble("homeZ")) : null);

    public void Start(EntityLaborEidolon eidolon)
    {
        _nav ??= new EidolonNavigator(eidolon);
        _eidolon = eidolon;
        _pick ??= eidolon.World.GetItem(new AssetLocation(EidolonMining.PickCode));
        _maxTier = EidolonMining.PickTier(_pick?.ToolTier);
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
        // Waiting for a part of the area to load: search again only now and then.
        if (_waitingSince != null && now < _rescanAt)
            return true;
        if (_layer == null || now >= _rescanAt || !_left.Any(c => c.Y == _layer))
        {
            eidolon.Orders?.SetStatus("seraphhorizons:eidolon-status-mine-seeking");
            Scan(eidolon);
            _rescanAt = now + RescanSeconds;
        }
        if (_layer is not { } layer)
            return _unloaded.Count == 0 ? Finish(eidolon) : Travel(eidolon, now);
        _waitingSince = null;
        _stalled = false;
        if (Next(eidolon) is { } group)
        {
            BeginStrike(eidolon, group, now);
            return true;
        }
        // The nearest square with a block its owner may break (the claim asked only now).
        foreach (var (_, cells) in EidolonMining.Squares(area, _left, layer, eidolon.Pos.X, eidolon.Pos.Z))
        {
            var breakable = cells.Where(Left).ToList();
            if (breakable.Count == 0)
                continue;
            _square = breakable;
            _approaches = 0;
            if (!Approach(eidolon, _square))
                GiveUp(eidolon);
            return true;
        }
        _layer = null;
        return true;
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
        var scan = EidolonMining.Scan(area, Loaded, c => Rule(eidolon, pos.Set(c.X, c.Y, c.Z), claims: false), _skip.Contains);
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
        if (EidolonMining.NearestColumn(_unloaded, eidolon.Pos.X, eidolon.Pos.Z) is { } column && TravelTo(eidolon, column))
        {
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
        var info = new MineBlock(false, liquid, beside, block.EntityClass != null || blocks.GetBlockEntity(pos) != null, true,
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

    /// <summary>Whether a cell is left to mine: found by the search, and its owner may break it there,
    /// asked the first time (a block they may not is left for the order's life).</summary>
    private bool Left(MarkPos cell)
    {
        if (!_left.Contains(cell))
            return false;
        if (!_mayBreak.TryGetValue(cell, out bool may))
        {
            may = _mayBreak[cell] = _eidolon != null && MayBreak(_eidolon, new BlockPos(cell.X, cell.Y, cell.Z, _eidolon.Pos.Dimension));
            if (!may)
            {
                _left.Remove(cell);
                _skip.Add(cell);
            }
        }
        return may;
    }

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
        for (int y = Math.Max(EidolonMining.BottomY, area.Min.Y); y <= area.Max.Y; y++)
        for (int x = area.Min.X; x <= area.Max.X; x++)
        for (int z = area.Min.Z; z <= area.Max.Z; z++)
        {
            if (blocks.GetChunkAtBlockPos(pos.Set(x, y, z)) == null)
                continue;
            var block = blocks.GetBlock(pos);
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
