using System.Text;
using SeraphHorizons.Mod.BuckingSawmill;
using SeraphHorizons.Mod.Core;
using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.Rosser.Core;
using SeraphHorizons.Mod.Woodworking;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace SeraphHorizons.Mod.Rosser;

/// <summary>
/// The rosser's controller. Holds the fitted parts (<see cref="RosserParts"/>: every item's code,
/// and the scraper heads' wear), the trunk as its whole Logging Expanded item stack, its trip
/// (<see cref="RosserTrip"/>: travel, rate and what has dropped) and the drip's reservoir. The
/// server feeds the trunk from the power ghost's shaft angle, drops sticks and bark at the chute
/// as the trunk passes the limb breaker and the ring, debarks it at the end of the trip, pulls
/// trunks from a rack at the infeed end and pushes finished ones into a rack at the outfeed end
/// (or leaves them to the bucking mill in line, <see cref="ITrunkFeeder"/>), draws water from a
/// pipe, and keeps the ghost cells stamped; the client follows the trunk between syncs and shows
/// it. Every cell's collision and selection boxes, with the travelling trunk's
/// (<see cref="TrunkBox.BoundsOnPath"/>), come from here. The rules are Rosser/Core's.
/// </summary>
public class BERosser : BlockEntity, IRosserVisualState, ITrunkFeeder
{
    private static readonly AssetLocation BreakSound = new("game", "sounds/effect/toolbreak");
    private static readonly AssetLocation LatchSound = new("game", "sounds/effect/latch");
    private static readonly AssetLocation StickSound = new("game", "sounds/block/stickbreak");
    private static readonly AssetLocation TrunkSound = new("game", "sounds/block/planks");
    private const string PartKeyPrefix = "part-";
    // The server syncs the travel at least this often (blocks); the client follows it between.
    private const double SyncStep = 0.25;

    private RosserParts _parts = new();
    private ItemStack? _trunk;
    private RosserTrip _trip = RosserTrip.None;
    private RosserWater _water = RosserWater.Dry;
    private RosserRackState _rackState;
    private RosserOutfeedState _outfeedState;
    private float _lastAngle;
    private bool _angleSeeded;
    private bool _warnedNoDebark;
    // The client's estimate of T, advanced with the shaft between syncs, and the shown T one client
    // tick back with when and how long that tick was: frames between ticks draw part way.
    private double _clientTravel;
    private double _clientTravelBefore;
    private long _clientTravelMs;
    private float _clientTravelSeconds;
    private float _clientLastAngle;
    private bool _clientAngleSeeded;
    private float? _serverMinSpeed;
    private bool _serverWet;
    private double? _serverReservoir;
    // Every cell's boxes for the trunk at one quantised T, turned to the facing: built whole, then
    // published; collision lookups, which can run off the main thread, only read it.
    private volatile BoxTable? _boxes;
    private Dictionary<Int3, Cuboidf[]>? _frameBoxes;
    private RosserRenderer? _renderer;

    private sealed record BoxTable(TrunkClass Class, double Travel, (Float3 Min, Float3 Max) Trunk, IReadOnlyDictionary<Int3, Cuboidf[]> Cells);

    public Side Side { get; private set; } = Side.North;
    public BlockFacing Facing => BlockFacing.FromCode(Side.Code());
    public bool Complete => _parts.Complete;
    /// <summary>The fitted parts (read only by callers).</summary>
    public RosserParts Parts => _parts;
    /// <summary>The trip as the server knows it (the client's copy carries the server's last T).</summary>
    public RosserTrip Trip => _trip;
    public RosserWater Water => _water;
    public RosserRackState RackState => _rackState;
    public RosserOutfeedState OutfeedState => _outfeedState;
    /// <summary>The configured slowest shaft speed that feeds; a client uses the server's value.</summary>
    public float MinSpeed => _serverMinSpeed ?? Config.MinSpeed;

    private RosserSystem System => RosserSystem.Of(Api);
    private RosserConfig Config => System.Config;
    private RosserRig? Rig => System.Rig;
    private RosserPace? Pace => System.Pace;

    // IRosserVisualState
    public bool Fitted(string? requires) => _parts.Fitted(requires);
    public string? HeadMetal => _parts.HeadMetal;
    public ItemStack? Trunk => _trunk;
    public TrunkClass TrunkClass => (TrunkClass)_trip.Class;
    public double ClientTravel
    {
        get
        {
            // T moves at the client's ticks (20 a second); read every frame, it would step.
            float t = _clientTravelSeconds > 0 && Api != null
                ? Math.Clamp((Api.World.ElapsedMilliseconds - _clientTravelMs) / (_clientTravelSeconds * 1000), 0, 1)
                : 1;
            return RosserVisuals.Interpolate(_clientTravelBefore, _clientTravel, t);
        }
    }
    /// <summary>T as this side knows it: the client's estimate there, the server's own here.</summary>
    public double SideTravel => Api?.Side == EnumAppSide.Client ? _clientTravel : _trip.Travel;
    public RosserState State => Pace is { } pace ? (_trip with { Travel = SideTravel }).State(pace) : _trunk == null ? RosserState.Empty : RosserState.Waiting;
    public bool Running => RosserTrip.Running(_parts.Complete, ShaftSpeed, MinSpeed);
    public bool Wet => Api?.Side == EnumAppSide.Client ? _serverWet : _water.Wet(Config);
    public float ShaftAngle => Power?.AngleRad ?? 0;
    public float ShaftSpeed => Math.Abs(Power?.TrueSpeed ?? 0);

    /// <summary>The power ghost's mechanical power behavior, looked up afresh each time.</summary>
    public BEBehaviorRosserMP? Power =>
        Api != null && Rig is { } rig
            ? Api.World.BlockAccessor.GetBlockEntity(CellPos(rig.PowerCell))?.GetBehavior<BEBehaviorRosserMP>()
            : null;

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        Side = Sides.TryParse(Block.Variant["side"], out var side) ? side : Side.North;
        if (api.Side == EnumAppSide.Server)
            _parts = _parts.WithMetals(System.PartMetals, System.PartMetals == null);
        RebuildBoxes(force: true);
        if (api.Side == EnumAppSide.Server)
        {
            RegisterDelayedCallback(_ =>
            {
                if (Api?.World.BlockAccessor.GetBlockEntity(Pos) == this)
                    EnsureGhosts();
            }, 100);
            RegisterGameTickListener(OnTick, 50);
            RegisterGameTickListener(OnSlowTick, 1000);
        }
        else
        {
            _clientTravel = _clientTravelBefore = _trip.Travel;
            RegisterGameTickListener(OnClientTick, 50);
            if (api is ICoreClientAPI capi && Rig is { } rig && Block is BlockRosser)
                _renderer = new RosserRenderer(capi, this, rig);
        }
    }

    // ---- Footprint ----

    public enum CellKind { Plain, Power, Water }

    public BlockPos CellPos(Int3 local)
    {
        var w = Footprint.ToWorld(local, Side);
        return Pos.AddCopy(w.X, w.Y, w.Z);
    }

    /// <summary>Every ghost cell's world position, with which ghost it is.</summary>
    public IEnumerable<(BlockPos Pos, CellKind Kind)> GhostCells() =>
        Rig is { } rig
            ? rig.GhostCells.Select(c => (CellPos(c.Pos), c.Pos == rig.PowerCell ? CellKind.Power : c.Pos == rig.WaterCell ? CellKind.Water : CellKind.Plain))
            : [];

    /// <summary>A cell's collision and selection boxes, turned to the rosser's facing, with the
    /// trunk's part in that cell; an empty set for a hollow cell the trunk is not in; null when the
    /// cell is not the rosser's. Safe off the main thread: the table is built whole and only read here.</summary>
    public Cuboidf[]? CellBoxes(BlockPos cellPos)
    {
        if (_boxes is not { } table)
            return null;
        var local = Footprint.ToLocal(new Int3(cellPos.X - Pos.X, cellPos.Y - Pos.Y, cellPos.Z - Pos.Z), Side);
        return table.Cells.GetValueOrDefault(local);
    }

    /// <summary>Rebuilds the box table when the trunk's class or its quantised travel
    /// (<see cref="RosserTrip.BoxTravel"/>, 1/16 block) changed, on either side.</summary>
    private void RebuildBoxes(bool force = false)
    {
        if (Api == null || Rig is not { } rig || !Sides.TryParse(Block?.Variant["side"], out var side))
            return;
        var k = _trunk == null ? TrunkClass.None : (TrunkClass)_trip.Class;
        double travel = k == TrunkClass.None ? 0 : RosserTrip.BoxTravel(SideTravel);
        if (!force && _boxes is { } current && current.Class == k && current.Travel == travel)
            return;
        Cuboidf World(Box b)
        {
            var w = Footprint.ToWorld(b, side);
            return new Cuboidf(w.X1, w.Y1, w.Z1, w.X2, w.Y2, w.Z2);
        }
        var frame = _frameBoxes ??= rig.Cells.ToDictionary(c => c.Pos, c =>
            c.Hollow ? [] : c.Boxes.Count == 0 ? [Cuboidf.Default()] : c.Boxes.Select(World).ToArray());
        var cells = new Dictionary<Int3, Cuboidf[]>(frame);
        var bounds = TrunkBox.BoundsOnPath(rig.Path, k, (float)travel);
        if (k != TrunkClass.None)
            foreach (var (cell, box) in TrunkBox.CellBoxes(rig.Cells, bounds))
                cells[cell] = [.. frame[cell], World(box)];
        _boxes = new BoxTable(k, travel, bounds, cells);
    }

    /// <summary>Whether a click on cell <paramref name="cellPos"/> at <paramref name="hit"/> (the
    /// selection's hit point, relative to that cell) is on the trunk.</summary>
    public bool HitsTrunk(BlockPos cellPos, Vec3d? hit)
    {
        if (hit == null || _boxes is not { Class: not TrunkClass.None } table)
            return false;
        var local = Footprint.ToLocal(new Float3(
            (float)(cellPos.X - Pos.X + hit.X), (float)(cellPos.Y - Pos.Y + hit.Y), (float)(cellPos.Z - Pos.Z + hit.Z)), Side);
        return TrunkBox.Contains(table.Trunk, local);
    }

    private Block? GhostBlock(CellKind kind) => Api.World.GetBlock(new AssetLocation(RosserSystem.Domain, kind switch
    {
        CellKind.Power => "rosser-ghostpower-" + Side.Code(),
        CellKind.Water => "rosser-ghostwater-" + Side.Code(),
        _ => "rosser-ghost",
    }));

    public void PlaceGhosts()
    {
        if (Api.Side != EnumAppSide.Server)
            return;
        foreach (var (pos, kind) in GhostCells())
            if (GhostBlock(kind) is { } ghost)
                StampGhost(ghost, pos);
    }

    private void StampGhost(Block ghost, BlockPos pos)
    {
        Api.World.BlockAccessor.SetBlock(ghost.BlockId, pos);
        if (Api.World.BlockAccessor.GetBlockEntity(pos) is BERosserGhost be)
        {
            be.Principal = Pos.Copy();
            be.MarkDirty(true);
        }
    }

    /// <summary>Restores ghost cells that went missing, and points every ghost back here.</summary>
    public void EnsureGhosts()
    {
        if (Api.Side != EnumAppSide.Server)
            return;
        var ba = Api.World.BlockAccessor;
        foreach (var (pos, kind) in GhostCells())
        {
            if (ba.GetChunkAtBlockPos(pos) == null || GhostBlock(kind) is not { } expected)
                continue;
            var current = ba.GetBlock(pos);
            if (current.Id == expected.Id)
            {
                if (ba.GetBlockEntity(pos) is BERosserGhost be && !Pos.Equals(be.Principal))
                {
                    be.Principal = Pos.Copy();
                    be.MarkDirty(true);
                }
            }
            else if (current.Id == 0 || current is BlockRosserGhost)
                StampGhost(expected, pos);
        }
    }

    public void RemoveGhosts()
    {
        if (Api.Side != EnumAppSide.Server)
            return;
        var ba = Api.World.BlockAccessor;
        foreach (var (pos, _) in GhostCells())
            if (ba.GetBlock(pos) is BlockRosserGhost && ba.GetBlockEntity(pos) is BERosserGhost be && Pos.Equals(be.Principal))
                ba.SetBlock(0, pos);
    }

    // ---- Interaction ----

    /// <summary>Right-click on the rosser or any ghost (<paramref name="onTrunk"/>: on the trunk).
    /// Ctrl takes the waiting or delivered trunk back, else the unworn heads; a part in hand is
    /// fitted (as many as its stage still needs); a trunk in hand, or with an empty hand one from
    /// the hotbar or backpack, is loaded onto the infeed bed. Anything else held is the item's own
    /// business, except on the trunk, where the click is the rosser's and does nothing (a block
    /// would be placed inside the trunk). In creative mode, Ctrl on an unassembled rosser fits its
    /// next stage instead. Decided and done on the server; the client only says whether the click
    /// is the rosser's.</summary>
    public bool OnInteract(IPlayer byPlayer, bool onTrunk = false)
    {
        var slot = byPlayer.InventoryManager.ActiveHotbarSlot;
        var controls = byPlayer.Entity.Controls;
        bool take = controls.CtrlKey && !controls.ShiftKey;
        var held = slot?.Itemstack;
        bool part = RosserParts.StageOf(held?.Collectible?.Code?.ToString(), out _) != null;
        bool trunk = Trunks.IsTrunk(held);
        if (!take && !part && !trunk && held != null)
            return onTrunk;
        if (Api.Side != EnumAppSide.Server)
            return true;

        if (CreativeShortcutApplies(byPlayer))
            FitNextPart(byPlayer);
        else if (take)
            TakeBack(byPlayer);
        else if (part)
            TryFitPart(slot!, byPlayer);
        else if (trunk)
            TryLoadFromSlot(slot!, byPlayer);
        else
            TryLoadFromInventory(byPlayer);
        return true;
    }

    /// <summary>Whether the creative shortcut is on: with <c>UnifiedWoodworking</c>, whose creative
    /// shortcut on the woodworking stations this is, running (as the bucking mill's).</summary>
    public bool CreativeShortcut => Api?.ModLoader.GetModSystem<SeraphHorizonsSystem>()?.Woodworking is { Active: true };

    /// <summary>Whether a click is the creative shortcut's: a player in creative mode, Ctrl without
    /// Shift, on a rosser with a stage still to fit.</summary>
    public bool CreativeShortcutApplies(IPlayer byPlayer)
    {
        var controls = byPlayer.Entity.Controls;
        return !_parts.Complete && CreativeShortcut
               && CreativeUpgrades.Applies(byPlayer.WorldData?.CurrentGameMode == EnumGameMode.Creative, controls.CtrlKey, controls.ShiftKey);
    }

    /// <summary>The creative shortcut (server side): fits the next missing stage (in
    /// <see cref="RosserStage"/> order, metal parts and heads of <see cref="AssembledMachines.DefaultMetal"/>)
    /// with nothing taken from the player.</summary>
    public bool FitNextPart(IPlayer byPlayer)
    {
        if (_parts.NextPart(AssembledMachines.DefaultMetal) is not { } next)
            return false;
        if (Api.World.GetItem(new AssetLocation(next.Code)) is not { } item)
        {
            Api.Logger.Warning("[seraphhorizons] Rosser: no {0} for the creative shortcut", next.Code);
            return false;
        }
        return TryFitPart(new DummySlot(new ItemStack(item, next.Count)), byPlayer, free: true);
    }

    /// <summary>Fits as many of the part in <paramref name="slot"/> as its stage(s) still need
    /// (server side); false, with an error to the player, when the rules say no. The parts are taken
    /// from the slot unless the player is in creative mode (then a whole set goes in from a single
    /// item) or it is <paramref name="free"/> (then exactly the slot's stack goes in, one stage's set).</summary>
    public bool TryFitPart(ItemSlot slot, IPlayer? byPlayer, bool free = false)
    {
        var stack = slot.Itemstack;
        string? code = stack?.Collectible?.Code?.ToString();
        if (stack == null || RosserParts.StageOf(code, out var metal) is not { } stage)
            return false;
        bool consumes = !free && byPlayer?.WorldData.CurrentGameMode != EnumGameMode.Creative;
        // a free fit (the creative shortcut) is one stage's set, so the rods fill one roll set a
        // click; a player in creative fits as many as the stage(s) take from a single item
        int available = consumes || free ? stack.StackSize : 64;
        var check = _parts.CanFit(code, available);
        if (!check.Fitted)
        {
            Error(byPlayer, check.Verdict switch
            {
                RosserFitVerdict.AlreadyFitted => "error-part-fitted",
                RosserFitVerdict.NeedsRing => "error-needs-ring",
                RosserFitVerdict.WrongMetal => "error-wrong-metal",
                RosserFitVerdict.NeedsFullSet => "error-needs-full-set",
                _ => "error-not-a-part",
            });
            return false;
        }
        int capacity = stage == RosserStage.Heads && metal != null ? System.HeadCapacity(metal) : 1;
        var fit = _parts.Fit(code, available, capacity);
        if (consumes)
        {
            slot.TakeOut(fit.Taken);
            slot.MarkDirty();
        }
        Api.World.PlaySoundAt(Block.Sounds.Place, Pos, -0.25, byPlayer);
        MarkDirty(true);
        return true;
    }

    /// <summary>Ctrl + right click (server side): the waiting or delivered trunk, else the four
    /// unworn heads (<see cref="RosserTrip.TakeBack"/>).</summary>
    public bool TakeBack(IPlayer byPlayer)
    {
        switch (RosserTrip.TakeBack(State, _parts.HeadMetal != null, _parts.HeadsUnworn))
        {
            case RosserTakeBack.Trunk:
                var trunk = _trunk!;
                ClearTrunk();
                Give(byPlayer, trunk);
                Api.World.PlaySoundAt(TrunkSound, Pos, 0, byPlayer);
                MarkDirty(true);
                return true;
            case RosserTakeBack.InRolls:
                return Error(byPlayer, "error-trunk-in-rolls");
            case RosserTakeBack.Heads:
                var code = _parts.FittedIn(RosserStage.Heads)[0];
                if (Api.World.GetItem(new AssetLocation(code)) is not { } item || _parts.RemoveHeads() is not { } heads)
                    return false;
                Give(byPlayer, new ItemStack(item, heads.Count));
                MarkDirty(true);
                return true;
            case RosserTakeBack.HeadsWorn:
                return Error(byPlayer, "error-heads-worn");
            default:
                return false;
        }
    }

    public bool TryLoadFromSlot(ItemSlot slot, IPlayer? byPlayer)
    {
        if (slot.Itemstack is not { } stack || !CanLoad(stack, byPlayer))
            return false;
        Load(slot.TakeOut(1));
        slot.MarkDirty();
        Api.World.PlaySoundAt(TrunkSound, Pos, 0, byPlayer);
        return true;
    }

    /// <summary>Loads the first trunk in the player's hotbar, then backpack, that the rosser
    /// takes; with none, says why the first trunk found is refused, or that there is none.</summary>
    public bool TryLoadFromInventory(IPlayer byPlayer)
    {
        ItemSlot? first = null;
        foreach (var name in new[] { GlobalConstants.hotBarInvClassName, GlobalConstants.backpackInvClassName })
        {
            if (byPlayer.InventoryManager.GetOwnInventory(name) is not { } inventory)
                continue;
            foreach (var slot in inventory)
            {
                if (slot.Itemstack is not { } stack || !Trunks.IsTrunk(stack))
                    continue;
                if (!Trunks.IsDebarked(stack) && Trunks.StoredLogs(stack, Api.World) > 0)
                    return TryLoadFromSlot(slot, byPlayer);
                first ??= slot;
            }
        }
        return first != null ? TryLoadFromSlot(first, byPlayer) : Error(byPlayer, "error-no-trunk");
    }

    /// <summary>Whether <paramref name="trunk"/> can go on now (<see cref="RosserTrip.CanLoad"/>);
    /// tells the player why not.</summary>
    public bool CanLoad(ItemStack trunk, IPlayer? byPlayer)
    {
        if (!Trunks.IsTrunk(trunk) || Pace == null)
            return false;
        if (System.Logging == null)
            return Error(byPlayer, "error-no-logging");
        return RosserTrip.CanLoad(_parts.Complete, State, Trunks.IsDebarked(trunk), Trunks.StoredLogs(trunk, Api.World)) switch
        {
            RosserLoadVerdict.Loads => true,
            RosserLoadVerdict.Incomplete => Error(byPlayer, "error-incomplete"),
            RosserLoadVerdict.Occupied => Error(byPlayer, "error-occupied"),
            RosserLoadVerdict.AlreadyDebarked => Error(byPlayer, "error-already-debarked"),
            _ => Error(byPlayer, "error-empty-trunk"),
        };
    }

    /// <summary>Puts <paramref name="trunk"/> on the infeed bed (T = 0): its trip's rate is fixed
    /// now, by its class and the heads' metal.</summary>
    private void Load(ItemStack trunk)
    {
        _trunk = trunk;
        _trip = RosserTrip.Load(Pace!, TrunkBox.ClassOf(trunk.Block?.Variant["size"]), Trunks.StoredLogs(trunk, Api.World),
                                trunk.Attributes.GetInt(Trunks.BranchCountKey), System.HeadTier(_parts.HeadMetal));
        RebuildBoxes();
        // the trunk's weight on the treadle throws the feed dog in
        Api.World.PlaySoundAt(LatchSound, Pos, 0);
        MarkDirty(true);
    }

    private void ClearTrunk()
    {
        _trunk = null;
        _trip = RosserTrip.Taken;
        RebuildBoxes();
    }

    private void Give(IPlayer byPlayer, ItemStack stack)
    {
        if (!byPlayer.InventoryManager.TryGiveItemstack(stack, true))
            Api.World.SpawnItemEntity(stack, Pos.ToVec3d().Add(0.5, 0.6, 0.5));
    }

    private bool Error(IPlayer? byPlayer, string key, params object[] args)
    {
        if (byPlayer is IServerPlayer sp)
            sp.SendIngameError("rosser-" + key, Lang.GetL(sp.LanguageCode, RosserSystem.Domain + ":rosser-" + key, args));
        return false;
    }

    // ---- The trip ----

    // Feeds while complete and turning fast enough; nothing moves without a trunk or once delivered.
    private void OnTick(float dt)
    {
        var power = Power;
        float angle = power?.AngleRad ?? 0, speed = Math.Abs(power?.TrueSpeed ?? 0);
        bool feeds = Pace != null && System.Logging != null && RosserTrip.Running(_parts.Complete, speed, Config.MinSpeed)
                     && State is RosserState.Waiting or RosserState.Feeding;
        if (!feeds || !_angleSeeded)
        {
            _lastAngle = angle;
            _angleSeeded = true;
            return;
        }
        float advance = ShaftClock.AngleAdvance(_lastAngle, angle, speed, dt);
        _lastAngle = angle;
        Feed(advance);
    }

    /// <summary>Feeds the trunk by <paramref name="radians"/> of shaft rotation (server side; call
    /// only while running): drops the sticks and bark now due at the chute, and at the end of the
    /// trip debarks the trunk, wears the heads and hands it on. Returns what the step gave.</summary>
    public TripStep Feed(float radians)
    {
        if (Pace is not { } pace || _trunk == null)
            return TripStep.Nothing;
        var before = _trip;
        (_trip, var step) = _trip.Advance(pace, radians);
        if (step.Sticks > 0)
            DropSticks(step.Sticks);
        if (step.BarkLogs > 0)
            DropBark(step.BarkLogs);
        RebuildBoxes();
        if (step.Delivered)
            Deliver();
        else if (step.Sticks > 0 || step.BarkLogs > 0 || before.State(pace) != _trip.State(pace)
                 || Math.Floor(before.Travel / SyncStep) != Math.Floor(_trip.Travel / SyncStep))
            MarkDirty();
        return step;
    }

    /// <summary>The end of the trip: the trunk becomes the debarked trunk (its branch count gone),
    /// the heads wear by its stored logs (spent heads break and are gone), and it goes on to a rack
    /// if one with room touches the outfeed end.</summary>
    private void Deliver()
    {
        if (Trunks.Debark(_trunk, Api.World) is { } debarked)
            _trunk = debarked.Trunk;
        else if (!_warnedNoDebark)
        {
            _warnedNoDebark = true;
            Api.Logger.Warning("[seraphhorizons] Rosser: no debarked trunk for {0} at {1}; it leaves as it came", _trunk?.Collectible?.Code, Pos);
        }
        if (_parts.WearHeads(RosserParts.WearFor(_trip.Logs, Config.HeadWearPerStoredLog)))
            Api.World.PlaySoundAt(BreakSound, Pos.X + 0.5, Pos.Y + 0.5, Pos.Z + 0.5);
        // the last top roll drops and its weight throws the dog out
        Api.World.PlaySoundAt(LatchSound, Pos, 0);
        MarkDirty(true);
        PushToRack();
    }

    /// <summary>Where bark and sticks come out (world) and the push they get, out of the chute's side.</summary>
    private (Vec3d At, Vec3d Velocity) ChutePoint()
    {
        var chute = Rig!.Chute;
        var p = Footprint.ToWorld(chute.Pos, Side);
        var n = Footprint.ToWorld(chute.Side, Side).Normal();
        return (new Vec3d(Pos.X + p.X, Pos.Y + p.Y, Pos.Z + p.Z), new Vec3d(n.X * 0.08, 0.02, n.Z * 0.08));
    }

    private void Spawn(ItemStack stack)
    {
        var (at, velocity) = ChutePoint();
        int max = Math.Max(1, stack.Collectible.MaxStackSize);
        for (int left = stack.StackSize; left > 0; left -= max)
        {
            var part = stack.Clone();
            part.StackSize = Math.Min(left, max);
            Api.World.SpawnItemEntity(part, at, velocity);
        }
    }

    private void DropSticks(int count)
    {
        if (Api.World.GetItem(new AssetLocation("game", "stick")) is not { } stick)
            return;
        Spawn(new ItemStack(stick, count));
        var (at, _) = ChutePoint();
        Api.World.PlaySoundAt(StickSound, at.X, at.Y, at.Z, null, true, 16);
    }

    /// <summary>Immersive Woodworking's bark for <paramref name="logs"/> scraped logs of the
    /// trunk's wood, one roll each (<see cref="BarkDrops.Roll"/>): wet while the reservoir holds a
    /// log's water (<see cref="RosserWater.SpendForLog"/>), which raises the roll's chance
    /// multiplier and the count (<see cref="RosserBark.WetCount"/>) as the settings say.</summary>
    private void DropBark(int logs)
    {
        if (!System.BarkBound || _trunk == null)
            return;
        var config = Config;
        string? species = SawhorseWorks.Species(Trunks.Wood(_trunk, Api.World));
        var drops = new List<ItemStack>();
        for (int i = 0; i < logs; i++)
        {
            (_water, bool wet) = _water.SpendForLog(config);
            if (BarkDrops.Roll(Api.World, species, wet ? config.WetBarkMultiplier : config.DryBarkMultiplier) is not { } bark)
                continue;
            if (wet)
                bark.StackSize = RosserBark.WetCount(bark.StackSize, config.WetBarkCountMultiplier, Api.World.Rand.NextDouble);
            if (bark.StackSize <= 0)
                continue;
            if (drops.FirstOrDefault(d => d.Equals(Api.World, bark, GlobalConstants.IgnoredStackAttributes)) is { } same)
                same.StackSize += bark.StackSize;
            else
                drops.Add(bark);
        }
        foreach (var stack in drops)
            Spawn(stack);
    }

    // The client follows the trunk with the shaft between syncs, by the server's rate.
    private void OnClientTick(float dt)
    {
        // The client removes a broken rosser at once, and an update the server sent before it heard
        // of the break then brings the block entity back over air, renderer and all: drop it.
        if (Api.World.BlockAccessor.GetBlock(Pos) is not BlockRosser)
        {
            DisposeRenderer();
            Api.World.BlockAccessor.RemoveBlockEntity(Pos);
            return;
        }
        var power = Power;
        float angle = power?.AngleRad ?? 0, speed = Math.Abs(power?.TrueSpeed ?? 0);
        float advance = 0;
        if (_clientAngleSeeded && Pace is { } pace && RosserTrip.Running(_parts.Complete, speed, MinSpeed)
            && State is RosserState.Waiting or RosserState.Feeding)
            advance = ShaftClock.AngleAdvance(_clientLastAngle, angle, speed, dt);
        _clientLastAngle = angle;
        _clientAngleSeeded = true;
        _clientTravelBefore = ClientTravel;
        if (advance > 0 && Pace is { } p)
            _clientTravel = RosserVisuals.EstimateTravel(_clientTravel, _trip.Rate, advance, _trip.End(p));
        _clientTravelMs = Api.World.ElapsedMilliseconds;
        _clientTravelSeconds = dt;
        RebuildBoxes();
    }

    // ---- Racks, the mill, water ----

    // Once a second: the ghosts, the water, and the racks at both ends (and the in-line mill).
    private void OnSlowTick(float dt)
    {
        // A ghost can vanish without being broken (an explosion, another mod), the power ghost included.
        EnsureGhosts();
        DrawWater(dt);
        if (State == RosserState.Empty)
            PullFromRack();
        else if (State == RosserState.Delivered)
            PushToRack();
        var rack = CheckRack(out _);
        var outfeed = CheckOutfeed(out _);
        if (rack != _rackState || outfeed != _outfeedState)
        {
            (_rackState, _outfeedState) = (rack, outfeed);
            MarkDirty();
        }
    }

    /// <summary>Tops the reservoir up from a water pipe on the water face (server side).</summary>
    private void DrawWater(float seconds)
    {
        if (Rig is not { } rig)
            return;
        double wanted = _water.Wanted(Config, seconds);
        if (wanted <= 0)
            return;
        bool wasWet = _water.Wet(Config);
        double got = PpexWater.Draw(Api, CellPos(rig.WaterCell), BlockRosserGhostWater.WaterFaceFor(rig, Side.Code()), wanted);
        if (got <= 0)
            return;
        _water = _water.Fill(got, Config);
        if (_water.Wet(Config) != wasWet)
            MarkDirty();
    }

    /// <summary>What the racks at the infeed end offer (server side): the first with a trunk the
    /// rosser takes is <paramref name="ready"/>; otherwise the most telling reason it has none. A
    /// rack counts when its controller or filler cell is one of the ground cells just beyond the
    /// infeed end (<see cref="RosserRig.InfeedNeighbours"/>).</summary>
    public RosserRackState CheckRack(out BlockEntity? ready)
    {
        ready = null;
        if (!Config.AutoPullFromRack)
            return RosserRackState.Off;
        if (System.Logging is not { } logging)
            return RosserRackState.NoLogging;
        if (Rig is not { } rig)
            return RosserRackState.None;
        var best = RosserRackState.None;
        foreach (var local in rig.InfeedNeighbours())
        {
            if (logging.FindRack(Api.World.BlockAccessor, CellPos(local)) is not { } rack)
                continue;
            var top = logging.PeekTrunk(rack);
            bool isTrunk = Trunks.IsTrunk(top);
            var state = RosserTrip.RackOffer(isTrunk, isTrunk && Trunks.IsDebarked(top), isTrunk ? Trunks.StoredLogs(top!, Api.World) : 0);
            if (state == RosserRackState.Ready)
            {
                ready = rack;
                return state;
            }
            if (state > best)
                best = state;
        }
        return best;
    }

    /// <summary>An empty, running rosser takes the top trunk of a rack at its infeed end, unless it
    /// is already debarked or holds no logs (it waits there). Returns whether one went on.</summary>
    public bool PullFromRack()
    {
        if (State != RosserState.Empty || !Running || CheckRack(out var rack) != RosserRackState.Ready || System.Logging is not { } logging)
            return false;
        var trunk = logging.PopTrunk(rack!);
        if (trunk == null)
            return false;
        rack!.MarkDirty(true);
        if (!CanLoad(trunk, null))
        {
            logging.PushTrunk(rack, trunk);
            rack.MarkDirty(true);
            return false;
        }
        Load(trunk);
        _rackState = CheckRack(out _);
        return true;
    }

    /// <summary>Where the delivered trunk can go at the outfeed end (server side): a bucking mill in
    /// line takes it itself; else, while <c>AutoPushToRack</c> is on, a rack touching the outfeed
    /// end with room is <paramref name="rack"/>.</summary>
    public RosserOutfeedState CheckOutfeed(out BlockEntity? rack)
    {
        rack = null;
        if (Rig is not { } rig)
            return RosserOutfeedState.None;
        if (MillInLine())
            return RosserOutfeedState.Mill;
        // a rack with room is no use when nothing pushes into it
        if (!Config.AutoPushToRack)
            return RosserOutfeedState.Off;
        if (System.Logging is not { } logging)
            return RosserOutfeedState.None;
        var best = RosserOutfeedState.None;
        foreach (var local in rig.OutfeedNeighbours())
        {
            if (logging.FindRack(Api.World.BlockAccessor, CellPos(local)) is not { } found)
                continue;
            if (RosserTrip.RackHasRoom(logging.TrunkCount(found)))
            {
                rack = found;
                return RosserOutfeedState.Rack;
            }
            best = RosserOutfeedState.RackFull;
        }
        return best;
    }

    /// <summary>A delivered trunk goes onto a rack with room at the outfeed end while the shaft
    /// turns, heads or no heads (a full rack leaves it on the bed; Logging Expanded's rack drops a fifth trunk
    /// silently, so the count is checked first). Then an empty rosser takes the next trunk.</summary>
    public bool PushToRack()
    {
        // only a new trip needs heads: a trunk debarked by the heads' last points still leaves
        if (State != RosserState.Delivered || ShaftSpeed < MinSpeed || !Config.AutoPushToRack || _trunk == null
            || CheckOutfeed(out var rack) != RosserOutfeedState.Rack || System.Logging is not { } logging)
            return false;
        logging.PushTrunk(rack!, _trunk.Clone());
        rack!.MarkDirty(true);
        ClearTrunk();
        MarkDirty(true);
        _outfeedState = CheckOutfeed(out _);
        PullFromRack();
        return true;
    }

    /// <summary>Whether a bucking mill facing the same way has this controller's cell among the
    /// cells just beyond its infeed end: it takes the delivered trunk itself (<see cref="ITrunkFeeder"/>).
    /// Not while the mills' <c>AutoPullFromRack</c> is off: then no mill takes it.</summary>
    public bool MillInLine()
    {
        var mills = BuckingSawmillSystem.Of(Api);
        if (Rig is not { } rig || mills.Rig is not { } millRig || !mills.Config.AutoPullFromRack)
            return false;
        var ba = Api.World.BlockAccessor;
        foreach (var local in rig.OutfeedNeighbours())
        {
            var be = ba.GetBlockEntity(CellPos(local));
            var mill = be as BEBuckingMill ?? (be as BEMillGhost)?.Mill;
            if (mill != null && mill.Side == Side && millRig.InfeedNeighbours().Any(n => mill.CellPos(n).Equals(Pos)))
                return true;
        }
        return false;
    }

    // ---- ITrunkFeeder (the bucking mill in line) ----

    /// <summary>Only the controller's cell: the outfeed end's cell on the trunk's line.</summary>
    public bool HasOutfeedCell(BlockPos world) =>
        Footprint.ToLocal(new Int3(world.X - Pos.X, world.Y - Pos.Y, world.Z - Pos.Z), Side) == Int3.Zero;

    public bool Busy => State is RosserState.Waiting or RosserState.Feeding;

    public ItemStack? PeekFinished() => State == RosserState.Delivered ? _trunk : null;

    public ItemStack? TakeFinished()
    {
        if (State != RosserState.Delivered || _trunk is not { } trunk)
            return null;
        ClearTrunk();
        MarkDirty(true);
        _outfeedState = CheckOutfeed(out _);
        return trunk;
    }

    // ---- Breaking ----

    /// <summary>What breaking the frame gives besides the frame: every fitted part (the heads only
    /// while unworn), and the trunk as far as it got (<see cref="RosserTrip.Broken"/>), so breaking
    /// and running it again never gives its sticks or bark twice.</summary>
    public IEnumerable<ItemStack> PartDrops()
    {
        foreach (var (code, count) in _parts.Returns())
            if (Api.World.GetItem(new AssetLocation(code)) is { } item)
                for (int left = count; left > 0; left -= Math.Max(1, item.MaxStackSize))
                    yield return new ItemStack(item, Math.Min(left, Math.Max(1, item.MaxStackSize)));
        if (_trunk == null)
            yield break;
        var broken = Pace is { } pace ? _trip.Broken(pace) : RosserBrokenTrunk.AsLoaded;
        var trunk = broken switch
        {
            RosserBrokenTrunk.Debranched => Trunks.Debranch(_trunk, Api.World),
            RosserBrokenTrunk.Debarked when !Trunks.IsDebarked(_trunk) => Trunks.Debark(_trunk, Api.World)?.Trunk,
            _ => null,
        };
        yield return trunk ?? _trunk.Clone();
    }

    public override void OnBlockRemoved()
    {
        base.OnBlockRemoved();
        RemoveGhosts();
        DisposeRenderer();
    }

    public override void OnBlockUnloaded()
    {
        base.OnBlockUnloaded();
        DisposeRenderer();
    }

    private void DisposeRenderer()
    {
        _renderer?.Dispose();
        _renderer = null;
    }

    // ---- Saving and syncing ----

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        foreach (var (requires, codes) in _parts.Snapshot())
            tree.SetString(PartKeyPrefix + requires, string.Join(",", codes));
        tree.SetInt("headsLeft", _parts.HeadsLeft);
        tree.SetInt("headsCapacity", _parts.HeadsCapacity);
        if (_trunk != null)
            tree.SetItemstack("trunk", _trunk);
        else
            tree.RemoveAttribute("trunk");
        tree.SetInt("logs", _trip.Logs);
        tree.SetInt("branches", _trip.Branches);
        tree.SetDouble("travel", _trip.Travel);
        tree.SetDouble("rate", _trip.Rate);
        tree.SetInt("sticksDone", _trip.SticksDone);
        tree.SetInt("barkDone", _trip.BarkDone);
        tree.SetDouble("water", _water.Litres);
        if (Api?.Side == EnumAppSide.Server)
        {
            tree.SetFloat("minSpeed", Config.MinSpeed);
            tree.SetBool("wet", _water.Wet(Config));
            tree.SetDouble("reservoir", Config.ReservoirLitres);
            tree.SetInt("rackState", (int)_rackState);
            tree.SetInt("outfeedState", (int)_outfeedState);
        }
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);
        var system = RosserSystem.Of(worldForResolving.Api);
        var fitted = RosserRequires.Stages.Select(RosserRequires.Name).ToDictionary(n => n, n =>
            (IReadOnlyList<string>)(tree.GetString(PartKeyPrefix + n) ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries));
        // What went in passed the metal rule of its day; what goes in next follows today's.
        _parts = RosserParts.Restore(fitted, tree.GetInt("headsLeft"), tree.GetInt("headsCapacity"), anyMetal: true);
        if (worldForResolving.Side == EnumAppSide.Server)
            _parts = _parts.WithMetals(system.PartMetals, system.PartMetals == null);

        var oldTrunk = _trunk;
        int oldClass = _trip.Class;
        _trunk = tree.GetItemstack("trunk");
        if (_trunk != null && !_trunk.ResolveBlockOrItem(worldForResolving))
            _trunk = null;
        var k = _trunk == null ? TrunkClass.None : TrunkBox.ClassOf(_trunk.Block?.Variant["size"]);
        int logs = tree.GetInt("logs"), branches = tree.GetInt("branches");
        double travel = tree.GetDouble("travel"), rate = tree.GetDouble("rate");
        int? sticksDone = tree.TryGetInt("sticksDone"), barkDone = tree.TryGetInt("barkDone");
        _trip = system.Pace is { } pace
            ? RosserTrip.Restore(pace, k, logs, branches, travel, rate, sticksDone, barkDone, system.HeadTier(_parts.HeadMetal))
            : k == TrunkClass.None ? RosserTrip.None : new RosserTrip((int)k, logs, branches, travel, rate, sticksDone ?? 0, barkDone ?? 0);
        _water = RosserWater.Restore(tree.GetDouble("water"), system.Config);

        if (worldForResolving.Side == EnumAppSide.Client)
        {
            _serverMinSpeed = tree.TryGetFloat("minSpeed");
            _serverWet = tree.GetBool("wet");
            _serverReservoir = tree.TryGetDouble("reservoir");
            _rackState = (RosserRackState)tree.GetInt("rackState");
            _outfeedState = (RosserOutfeedState)tree.GetInt("outfeedState");
            // A new trunk (or none) starts the estimate at the server's T; otherwise a sync that
            // parts from the estimate resets it, and small differences are left to the shaft.
            bool sameTrunk = _trunk != null && oldTrunk != null && oldClass == _trip.Class;
            if (!sameTrunk)
                _clientTravel = _clientTravelBefore = _trip.Travel;
            else
                _clientTravel = RosserVisuals.Reconcile(_clientTravel, _trip.Travel);
        }
        RebuildBoxes();
    }

    public override void OnStoreCollectibleMappings(Dictionary<int, AssetLocation> blockIdMapping, Dictionary<int, AssetLocation> itemIdMapping)
    {
        base.OnStoreCollectibleMappings(blockIdMapping, itemIdMapping);
        _trunk?.Collectible.OnStoreCollectibleMappings(Api.World, new DummySlot(_trunk), blockIdMapping, itemIdMapping);
    }

    public override void OnLoadCollectibleMappings(IWorldAccessor worldForResolve, Dictionary<int, AssetLocation> oldBlockIdMapping,
                                                   Dictionary<int, AssetLocation> oldItemIdMapping, int schematicSeed, bool resolveImports)
    {
        base.OnLoadCollectibleMappings(worldForResolve, oldBlockIdMapping, oldItemIdMapping, schematicSeed, resolveImports);
        _trunk?.FixMapping(oldBlockIdMapping, oldItemIdMapping, worldForResolve);
    }

    // ---- Info ----

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
    {
        base.GetBlockInfo(forPlayer, dsc);
        string L(string key, params object[] args) => Lang.Get(RosserSystem.Domain + ":rosser-" + key, args);
        if (!_parts.Complete)
        {
            dsc.AppendLine(L(_parts.Has(RosserStage.Ring) && _parts.Missing().All(m => m.Stage == RosserStage.Heads) ? "info-noheads" : "info-incomplete"));
            foreach (var (stage, _, count) in _parts.Missing())
            {
                string name = L("info-stage-" + RosserRequires.Name(stage));
                dsc.AppendLine("• " + (count > 1 ? L("info-count", count, name) : name));
            }
        }
        if (_parts.HeadMetal is { } metal)
        {
            dsc.AppendLine(L("info-heads", Lang.Get("material-" + metal), _parts.HeadsLeft, _parts.HeadsCapacity));
            if (Pace is { } pace)
                dsc.AppendLine(L("info-head-speed", pace.HeadSpeed(System.HeadTier(metal)).ToString("0.##")));
        }
        if (_parts.Complete || _trunk != null)
        {
            var state = State;
            if (_trunk != null)
                dsc.AppendLine(L("info-trunk", _trunk.GetName(), _trip.Logs, _trip.Branches));
            switch (state)
            {
                case RosserState.Empty:
                    dsc.AppendLine(L("info-empty"));
                    break;
                case RosserState.Waiting:
                    dsc.AppendLine(L(Running ? "info-waiting" : "info-waiting-stopped"));
                    break;
                case RosserState.Feeding:
                    double end = Pace is { } p ? _trip.End(p) : 0;
                    int percent = end > 0 ? (int)(Math.Clamp(SideTravel / end, 0, 1) * 100) : 0;
                    dsc.AppendLine(Running ? L("info-feeding", percent) : L("info-stalled", percent));
                    break;
                case RosserState.Delivered:
                    dsc.AppendLine(L("info-delivered"));
                    if (_outfeedState != RosserOutfeedState.Unknown)
                        dsc.AppendLine(L("info-outfeed-" + _outfeedState.ToString().ToLowerInvariant()));
                    break;
            }
            if (state == RosserState.Empty && _rackState != RosserRackState.Unknown)
                dsc.AppendLine(L("info-rack-" + _rackState.ToString().ToLowerInvariant()));
        }
        double reservoir = _serverReservoir ?? Config.ReservoirLitres;
        dsc.AppendLine(Wet ? L("info-water", _water.Litres.ToString("0"), reservoir.ToString("0")) : L("info-dry"));
        float speed = ShaftSpeed;
        dsc.AppendLine(speed < MinSpeed ? L("info-nopower") : L("info-speed", speed.ToString("0.00")));
    }
}
