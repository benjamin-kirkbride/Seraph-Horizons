using System.Text;
using SeraphHorizons.Mod.Core;
using SeraphHorizons.Mod.GearCutter.Core;
using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Machines.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.GearCutter;

/// <summary>
/// The gear cutter's controller (#480, #481). Holds the fitted parts (<see cref="GearCutterParts"/>:
/// every item's code and the cutter kit's durability), the blank on the arbor as its item stack and
/// the cut (<see cref="CutJob"/>: W, the teeth cut), and its MachineOil tank. The server cuts from
/// the power ghost's shaft angle, wears the kit by the tank's fill when a gear is done, drops the
/// gear at the output face or puts it in a container there, takes the next blank from a chest or
/// hopper at the infeed face, and keeps the ghost cells stamped; the client draws it
/// (<see cref="GearCutterRenderer"/>). The rules are GearCutter/Core's.
/// </summary>
public class BEGearCutter : BlockEntity
{
    private static readonly AssetLocation BreakSound = new("game", "sounds/effect/toolbreak");
    private static readonly AssetLocation LatchSound = new("game", "sounds/effect/latch");
    private static readonly AssetLocation GearSound = new("game", "sounds/block/ingot");
    private const string PartKeyPrefix = "part-";
    // The server syncs W at least this often (teeth); the renderer follows the shaft between.
    private const double SyncStep = 0.1;

    private GearCutterParts _parts = new();
    private ItemStack? _blank;
    private CutJob _job = CutJob.None;
    private OilState? _oil;
    private float _lastAngle;
    private bool _angleSeeded;
    private float _smokeSeconds;
    private float? _serverMinSpeed;
    private float? _serverTurnsPerTooth;
    private int? _serverWearPerGear;
    private Dictionary<Int3, Cuboidf[]>? _cells;
    private Dictionary<Int3, Cuboidf[]>? _collision;
    private GearCutterRenderer? _renderer;

    public Side Side { get; private set; } = Side.North;
    public GearCutterParts Parts => _parts;
    public bool Complete => _parts.Complete;
    public bool BlankOn => _job.On && _blank != null;
    public ItemStack? Blank => _blank;
    public CutJob Job => _job;
    /// <summary>The cutter's oil, or null without the <c>MachineOil</c> switch.</summary>
    public OilState? Oiling => _oil;
    /// <summary>The tank's fill, 0..1: 1 without a tank (MachineOil off), so the kit wears at its base rate.</summary>
    public double OilFill => _oil == null ? 1 : OilDrain.Fill(_oil.Tank);
    public float MinSpeed => _serverMinSpeed ?? Config.MinSpeed;
    public float TurnsPerTooth => _serverTurnsPerTooth ?? Config.TurnsPerTooth;
    private int WearPerGear => _serverWearPerGear ?? Config.CutterWearPerGear;
    public float ShaftAngle => Power?.AngleRad ?? 0;
    public float ShaftSpeed => Math.Abs(Power?.TrueSpeed ?? 0);
    /// <summary>Complete, a blank on and the shaft fast enough: the cut runs.</summary>
    public bool Running => GearCut.Running(_parts.Complete, BlankOn, ShaftSpeed, MinSpeed);

    private GearCutterSystem System => GearCutterSystem.Of(Api);
    private GearCutterConfig Config => System.Config;
    private GearCutterRig? Rig => System.Rig;

    /// <summary>Whether a rig part needing <paramref name="requires"/> is drawn: the parts' rule,
    /// and each blank while it is on the arbor.</summary>
    public bool Fitted(string? requires) => requires switch
    {
        GearCutterRequires.BlankSmall => BlankOn && _job.Class == 1,
        GearCutterRequires.BlankLarge => BlankOn && _job.Class == 2,
        _ => _parts.Fitted(requires),
    };

    /// <summary>The power ghost's mechanical power behavior, looked up afresh each time.</summary>
    public BEBehaviorGearCutterMP? Power =>
        Api != null && Rig is { } rig
            ? Api.World.BlockAccessor.GetBlockEntity(CellPos(rig.PowerCell))?.GetBehavior<BEBehaviorGearCutterMP>()
            : null;

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        Side = Sides.TryParse(Block.Variant["side"], out var side) ? side : Side.North;
        BuildBoxes();
        if (api.Side == EnumAppSide.Server)
        {
            _oil ??= Oil.NewOwn(api, OilMachine.GearCutter);
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
            RegisterGameTickListener(OnClientTick, 200);
            if (api is ICoreClientAPI capi && Rig is { } rig && Block is BlockGearCutter)
                _renderer = new GearCutterRenderer(capi, this, rig);
        }
    }

    // ---- Footprint ----

    public BlockPos CellPos(Int3 local)
    {
        var w = Footprint.ToWorld(local, Side);
        return Pos.AddCopy(w.X, w.Y, w.Z);
    }

    /// <summary>A native-frame point as a world position.</summary>
    public Vec3d WorldPoint(Float3 native)
    {
        var w = Footprint.ToWorld(native, Side);
        return new Vec3d(Pos.X + w.X, Pos.Y + w.Y, Pos.Z + w.Z);
    }

    /// <summary>Every ghost cell's world position, and whether it is the power ghost.</summary>
    public IEnumerable<(BlockPos Pos, bool Power)> GhostCells() =>
        Rig is { } rig ? rig.GhostCells.Select(c => (CellPos(c.Pos), c.Pos == rig.PowerCell)) : [];

    public Cuboidf[]? CellBoxes(BlockPos cellPos) => Lookup(_cells, cellPos);

    /// <summary>A cell's selection boxes and, on a column's top cell, the rig's collision-only lid.</summary>
    public Cuboidf[]? CollisionBoxes(BlockPos cellPos) => Lookup(_collision, cellPos);

    private Cuboidf[]? Lookup(Dictionary<Int3, Cuboidf[]>? table, BlockPos cellPos) =>
        table?.GetValueOrDefault(Footprint.ToLocal(new Int3(cellPos.X - Pos.X, cellPos.Y - Pos.Y, cellPos.Z - Pos.Z), Side));

    // The boxes never change: built once, then only read (collision lookups can run off the main thread).
    private void BuildBoxes()
    {
        if (Rig is not { } rig || !Sides.TryParse(Block?.Variant["side"], out var side))
            return;
        Cuboidf World(Box b)
        {
            var w = Footprint.ToWorld(b, side);
            return new Cuboidf(w.X1, w.Y1, w.Z1, w.X2, w.Y2, w.Z2);
        }
        var cells = rig.Cells.ToDictionary(c => c.Pos, c => c.Boxes.Count == 0 ? [Cuboidf.Default()] : c.Boxes.Select(World).ToArray());
        var collision = new Dictionary<Int3, Cuboidf[]>(cells);
        foreach (var c in rig.Cells)
            if (c.LidBox is { } lid)
                collision[c.Pos] = [.. cells[c.Pos], World(lid)];
        _cells = cells;
        _collision = collision;
    }

    private Block? GhostBlock(bool power) =>
        Api.World.GetBlock(new AssetLocation(GearCutterSystem.Domain, power ? "gearcutter-ghostpower-" + Side.Code() : "gearcutter-ghost"));

    public void PlaceGhosts()
    {
        if (Api.Side != EnumAppSide.Server)
            return;
        foreach (var (pos, power) in GhostCells())
            if (GhostBlock(power) is { } ghost)
                StampGhost(ghost, pos);
    }

    private void StampGhost(Block ghost, BlockPos pos)
    {
        Api.World.BlockAccessor.SetBlock(ghost.BlockId, pos);
        if (Api.World.BlockAccessor.GetBlockEntity(pos) is BEGearCutterGhost be)
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
        foreach (var (pos, power) in GhostCells())
        {
            if (ba.GetChunkAtBlockPos(pos) == null || GhostBlock(power) is not { } expected)
                continue;
            var current = ba.GetBlock(pos);
            if (current.Id == expected.Id)
            {
                if (ba.GetBlockEntity(pos) is BEGearCutterGhost be && !Pos.Equals(be.Principal))
                {
                    be.Principal = Pos.Copy();
                    be.MarkDirty(true);
                }
            }
            else if (current.Id == 0 || current is BlockGearCutterGhost)
                StampGhost(expected, pos);
        }
    }

    public void RemoveGhosts()
    {
        if (Api.Side != EnumAppSide.Server)
            return;
        var ba = Api.World.BlockAccessor;
        foreach (var (pos, _) in GhostCells())
            if (ba.GetBlock(pos) is BlockGearCutterGhost && ba.GetBlockEntity(pos) is BEGearCutterGhost be && Pos.Equals(be.Principal))
                ba.SetBlock(0, pos);
    }

    // ---- Interaction ----

    /// <summary>
    /// Right-click on the cutter or any ghost. Holding oil, it pours (MachineOil). In creative mode,
    /// Ctrl on an incomplete cutter fits its next stage with nothing taken. Ctrl takes back the
    /// cutter kit, else the blank on the arbor, else the master. A part in hand is fitted if it is
    /// the next stage's; a blank goes on the arbor. Anything else held is the item's own business.
    /// Decided and done on the server; the client only says whether the click is the cutter's.
    /// </summary>
    public bool OnInteract(IPlayer byPlayer)
    {
        if (Oil.Interact(this, _oil, byPlayer) is { } oiled)
            return oiled;
        var slot = byPlayer.InventoryManager.ActiveHotbarSlot;
        var controls = byPlayer.Entity.Controls;
        bool take = controls.CtrlKey && !controls.ShiftKey;
        string? code = slot?.Itemstack?.Collectible?.Code?.ToString();
        bool part = GearCutterParts.IsPart(code);
        bool blank = GearCut.ClassOfBlank(code) != 0;
        if (!take && !part && !blank)
            return false;
        if (Api.Side != EnumAppSide.Server)
            return true;
        if (CreativeShortcutApplies(byPlayer))
            FitNextPart(byPlayer);
        else if (take)
            TakeBack(byPlayer);
        else if (part)
            TryFitPart(slot!, byPlayer);
        else
            TryLoadBlank(slot!, byPlayer);
        return true;
    }

    /// <summary>Whether a click is the creative shortcut's: a player in creative mode, Ctrl without
    /// Shift, on a cutter with a stage still to fit.</summary>
    public bool CreativeShortcutApplies(IPlayer byPlayer)
    {
        var controls = byPlayer.Entity.Controls;
        return !_parts.Complete
               && CreativeUpgrades.Applies(byPlayer.WorldData?.CurrentGameMode == EnumGameMode.Creative, controls.CtrlKey, controls.ShiftKey);
    }

    /// <summary>The creative shortcut (server side): fits the next stage's first item (the small
    /// master, a new kit) with nothing taken from the player.</summary>
    public bool FitNextPart(IPlayer? byPlayer)
    {
        if (_parts.NextPart is not { } next)
            return false;
        if (Api.World.GetItem(new AssetLocation(next)) is not { } item)
        {
            Api.Logger.Warning("[seraphhorizons] Gear cutter: no {0} for the creative shortcut", next);
            return false;
        }
        return TryFitPart(new DummySlot(new ItemStack(item)), byPlayer, free: true);
    }

    /// <summary>Fits the part in <paramref name="slot"/> if it is the next stage's (server side);
    /// false, with an error to the player, when the rules say no. One item is taken from the slot
    /// unless the player is in creative mode or it is <paramref name="free"/>.</summary>
    public bool TryFitPart(ItemSlot slot, IPlayer? byPlayer, bool free = false)
    {
        var stack = slot.Itemstack;
        string? code = stack?.Collectible?.Code?.ToString();
        if (stack == null)
            return false;
        int left = 0, capacity = 0;
        if (GearCutterParts.StagesOf(code).Contains(GearCutterStage.Cutter))
        {
            capacity = Math.Max(1, stack.Collectible.GetMaxDurability(stack));
            left = stack.Collectible.GetRemainingDurability(stack);
        }
        var verdict = _parts.CanFit(code, out _, capacity > 0 ? left : 1);
        if (verdict != GearCutterFitVerdict.Fits)
        {
            switch (verdict)
            {
                case GearCutterFitVerdict.OutOfOrder:
                    Error(byPlayer, "error-order", StageName(_parts.Next));
                    break;
                case GearCutterFitVerdict.AlreadyFitted:
                    Error(byPlayer, "error-part-fitted");
                    break;
                case GearCutterFitVerdict.KitSpent:
                    Error(byPlayer, "error-kit-spent");
                    break;
                default:
                    Error(byPlayer, "error-not-a-part");
                    break;
            }
            return false;
        }
        _parts.Fit(code, left, capacity);
        bool consumes = !free && byPlayer?.WorldData.CurrentGameMode != EnumGameMode.Creative;
        if (consumes)
        {
            slot.TakeOut(1);
            slot.MarkDirty();
        }
        Api.World.PlaySoundAt(Block.Sounds.Place, Pos, -0.25, byPlayer);
        MarkDirty(true);
        PullFromInfeed();
        return true;
    }

    /// <summary>A stage's name as the block info and the errors show it.</summary>
    public static string StageName(GearCutterStage? stage) =>
        stage is { } s ? Lang.Get(GearCutterSystem.Domain + ":gearcutter-info-stage-" + GearCutterRequires.Name(s)) : "";

    /// <summary>Ctrl + right-click (server side): the kit (with what durability it has left), else
    /// the blank on the arbor (its cut lost), else the master.</summary>
    public bool TakeBack(IPlayer byPlayer)
    {
        switch (_parts.TakeBack(BlankOn))
        {
            case GearCutterTakeBack.Kit:
                if (_parts.RemoveKit() is not { } kit || KitStack(kit.Left) is not { } stack)
                    return false;
                Give(byPlayer, stack);
                break;
            case GearCutterTakeBack.Blank:
                var blank = _blank!;
                ClearBlank();
                Give(byPlayer, blank);
                break;
            case GearCutterTakeBack.Master:
                if (_parts.RemoveMaster() is not { } code || Api.World.GetItem(new AssetLocation(code)) is not { } item)
                    return false;
                Give(byPlayer, new ItemStack(item));
                break;
            default:
                return false;
        }
        Api.World.PlaySoundAt(LatchSound, Pos, 0, byPlayer);
        MarkDirty(true);
        return true;
    }

    /// <summary>A cutter kit with <paramref name="left"/> durability.</summary>
    private ItemStack? KitStack(int left)
    {
        if (Api.World.GetItem(new AssetLocation(GearCutterParts.KitCode)) is not { } item)
            return null;
        var stack = new ItemStack(item);
        if (left < item.GetMaxDurability(stack))
            stack.Attributes.SetInt("durability", Math.Max(1, left));
        return stack;
    }

    /// <summary>Puts a blank from <paramref name="slot"/> on the arbor, if the cutter takes it now.</summary>
    public bool TryLoadBlank(ItemSlot slot, IPlayer? byPlayer)
    {
        string? code = slot.Itemstack?.Collectible?.Code?.ToString();
        switch (GearCut.CanLoad(code, _parts.Complete, BlankOn, _parts.Master))
        {
            case GearCutterBlankVerdict.Loads:
                break;
            case GearCutterBlankVerdict.Incomplete:
                return Error(byPlayer, _parts.Next == GearCutterStage.Cutter && _parts.Has(GearCutterStage.Master) ? "error-no-kit" : "error-incomplete");
            case GearCutterBlankVerdict.Occupied:
                return Error(byPlayer, "error-occupied");
            case GearCutterBlankVerdict.WrongSize:
                return Error(byPlayer, _parts.Master == 1 ? "error-wrong-size-small" : "error-wrong-size-large");
            default:
                return false;
        }
        Load(slot.TakeOut(1));
        slot.MarkDirty();
        return true;
    }

    private void Load(ItemStack blank)
    {
        _blank = blank;
        _job = new CutJob(GearCut.ClassOfBlank(blank.Collectible.Code.ToString()), 0);
        Api.World.PlaySoundAt(LatchSound, Pos, 0);
        MarkDirty(true);
    }

    private void ClearBlank()
    {
        _blank = null;
        _job = CutJob.None;
    }

    private void Give(IPlayer byPlayer, ItemStack stack)
    {
        if (!byPlayer.InventoryManager.TryGiveItemstack(stack, true))
            Api.World.SpawnItemEntity(stack, Pos.ToVec3d().Add(0.5, 1.2, 0.5));
    }

    private bool Error(IPlayer? byPlayer, string key, params object[] args)
    {
        if (byPlayer is IServerPlayer sp)
            sp.SendIngameError("gearcutter-" + key, Lang.GetL(sp.LanguageCode, GearCutterSystem.Domain + ":gearcutter-" + key, args));
        return false;
    }

    // ---- The cut ----

    private void OnTick(float dt)
    {
        var power = Power;
        float angle = power?.AngleRad ?? 0, speed = Math.Abs(power?.TrueSpeed ?? 0);
        if (!GearCut.Running(_parts.Complete, BlankOn, speed, Config.MinSpeed) || !_angleSeeded)
        {
            _lastAngle = angle;
            _angleSeeded = true;
            return;
        }
        float advance = ShaftClock.AngleAdvance(_lastAngle, angle, speed, dt);
        _lastAngle = angle;
        Cut(advance);
    }

    /// <summary>Cuts by <paramref name="radians"/> of shaft rotation (server side; call only while
    /// running). At the end of the gear the kit wears by the oil's fill, the tank drains, the gear
    /// goes out and the next blank comes in. Returns whether a gear was finished.</summary>
    public bool Cut(double radians)
    {
        if (!BlankOn || !_parts.Complete)
            return false;
        var before = _job;
        (_job, bool finished) = _job.Advance(radians, Config.TurnsPerTooth);
        if (finished)
        {
            Finish();
            return true;
        }
        if (Math.Floor(before.Work / SyncStep) != Math.Floor(_job.Work / SyncStep))
            MarkDirty();
        return false;
    }

    /// <summary>The gear is cut: the kit wears by the fill the tank has now (#481), then the tank
    /// drains (double for a large gear); a spent kit breaks with the tool-break sound. The gear goes
    /// into a container at the output face, else drops there; the next blank comes from the infeed.</summary>
    private void Finish()
    {
        int k = _job.Class;
        int wear = GearCutterWear.WearFor(Config.CutterWearPerGear, GearCut.Teeth(k), OilFill, _parts.KitLeft);
        Oil.Drain(_oil, Oil.DrainPerJob(Api, OilMachine.GearCutter) * (k == 2 ? 2 : 1));
        if (_parts.WearKit(wear))
            Api.World.PlaySoundAt(BreakSound, Pos.X + 0.5, Pos.Y + 0.5, Pos.Z + 0.5);
        ClearBlank();
        if (GearCut.GearFor(k) is { } code && Api.World.GetItem(new AssetLocation(code)) is { } gear)
            Deliver(new ItemStack(gear));
        MarkDirty(true);
        PullFromInfeed();
    }

    /// <summary>Puts <paramref name="stack"/> into a container just beyond the output face, else
    /// drops it there.</summary>
    public void Deliver(ItemStack stack)
    {
        if (Rig is not { } rig)
            return;
        var dummy = new DummySlot(stack);
        if (Api.World.BlockAccessor.GetBlockEntity(CellPos(rig.OutputNeighbour())) is BlockEntityContainer container)
        {
            foreach (var slot in container.Inventory)
            {
                if (dummy.Empty)
                    break;
                if (slot.CanHold(dummy))
                    dummy.TryPutInto(Api.World, slot, dummy.StackSize);
            }
            container.MarkDirty(true);
        }
        if (dummy.Empty)
            return;
        var at = WorldPoint(rig.OutputDrop());
        var n = Footprint.ToWorld(rig.OutputSide, Side).Normal();
        Api.World.SpawnItemEntity(dummy.Itemstack, at, new Vec3d(n.X * 0.05, 0.02, n.Z * 0.05));
        Api.World.PlaySoundAt(GearSound, at.X, at.Y, at.Z);
    }

    /// <summary>A complete cutter with an empty arbor and its shaft turning takes one blank of its
    /// master's size from a container at the infeed face. Returns whether one went on.</summary>
    public bool PullFromInfeed()
    {
        if (Api.Side != EnumAppSide.Server || BlankOn || !_parts.Complete || ShaftSpeed < MinSpeed || Rig is not { } rig)
            return false;
        foreach (var local in rig.InfeedNeighbours())
        {
            if (Api.World.BlockAccessor.GetBlockEntity(CellPos(local)) is not BlockEntityContainer container)
                continue;
            foreach (var slot in container.Inventory)
            {
                if (GearCut.CanLoad(slot.Itemstack?.Collectible?.Code?.ToString(), true, false, _parts.Master) != GearCutterBlankVerdict.Loads)
                    continue;
                Load(slot.TakeOut(1));
                slot.MarkDirty();
                container.MarkDirty(true);
                return true;
            }
        }
        return false;
    }

    // Once a second: the ghosts, and the infeed.
    private void OnSlowTick(float dt)
    {
        // A ghost can vanish without being broken (an explosion, another mod), the power ghost included.
        EnsureGhosts();
        PullFromInfeed();
    }

    // The client's own: the client removes a broken cutter at once, and an update the server sent
    // before it heard of the break then brings the block entity back over air: drop it. A dry
    // cutter smokes while it cuts.
    private void OnClientTick(float dt)
    {
        if (Api.World.BlockAccessor.GetBlock(Pos) is not BlockGearCutter)
        {
            DisposeRenderer();
            Api.World.BlockAccessor.RemoveBlockEntity(Pos);
            return;
        }
        if ((_smokeSeconds += dt) >= 0.4f)
        {
            _smokeSeconds = 0;
            if (_oil is { Dry: true } && Running && Api is ICoreClientAPI capi && Rig is { } rig)
                Oil.Smoke(capi, WorldPoint(rig.Chips));
        }
    }

    // ---- Breaking ----

    /// <summary>What breaking the frame gives besides the frame: every fitted part (the kit with its
    /// durability) and the blank on the arbor.</summary>
    public IEnumerable<ItemStack> PartDrops()
    {
        foreach (var drop in _parts.Returns())
        {
            if (drop.Durability is { } left)
            {
                if (KitStack(left) is { } kit)
                    yield return kit;
            }
            else if (Api.World.GetItem(new AssetLocation(drop.Code)) is { } item)
                yield return new ItemStack(item);
        }
        if (BlankOn)
            yield return _blank!.Clone();
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
        foreach (var stage in GearCutterRequires.Stages)
            tree.RemoveAttribute(PartKeyPrefix + GearCutterRequires.Name(stage));
        foreach (var (requires, code) in _parts.Snapshot())
            tree.SetString(PartKeyPrefix + requires, code);
        tree.SetInt("kitLeft", _parts.KitLeft);
        tree.SetInt("kitCapacity", _parts.KitCapacity);
        if (BlankOn)
            tree.SetItemstack("blank", _blank);
        else
            tree.RemoveAttribute("blank");
        tree.SetInt("class", _job.Class);
        tree.SetDouble("work", _job.Work);
        if (Api?.Side == EnumAppSide.Server)
        {
            tree.SetFloat("minSpeed", Config.MinSpeed);
            tree.SetFloat("turnsPerTooth", Config.TurnsPerTooth);
            tree.SetInt("wearPerGear", Config.CutterWearPerGear);
        }
        if (_oil != null)
            Oil.Write(tree, _oil);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);
        var fitted = new Dictionary<string, string>();
        foreach (var stage in GearCutterRequires.Stages)
            if (tree.GetString(PartKeyPrefix + GearCutterRequires.Name(stage)) is { Length: > 0 } code)
                fitted[GearCutterRequires.Name(stage)] = code;
        _parts = GearCutterParts.Restore(fitted, tree.GetInt("kitLeft"), tree.GetInt("kitCapacity"));
        _blank = tree.GetItemstack("blank");
        if (_blank != null && !_blank.ResolveBlockOrItem(worldForResolving))
            _blank = null;
        _job = _blank == null ? CutJob.None : CutJob.Restore(tree.GetInt("class"), tree.GetDouble("work"));
        if (!_job.On)
            _blank = null;
        _oil = Oil.LoadOwn(tree, worldForResolving, OilMachine.GearCutter);
        if (worldForResolving.Side == EnumAppSide.Client)
        {
            _serverMinSpeed = tree.TryGetFloat("minSpeed");
            _serverTurnsPerTooth = tree.TryGetFloat("turnsPerTooth");
            _serverWearPerGear = tree.TryGetInt("wearPerGear");
        }
    }

    public override void OnStoreCollectibleMappings(Dictionary<int, AssetLocation> blockIdMapping, Dictionary<int, AssetLocation> itemIdMapping)
    {
        base.OnStoreCollectibleMappings(blockIdMapping, itemIdMapping);
        _blank?.Collectible.OnStoreCollectibleMappings(Api.World, new DummySlot(_blank), blockIdMapping, itemIdMapping);
    }

    public override void OnLoadCollectibleMappings(IWorldAccessor worldForResolve, Dictionary<int, AssetLocation> oldBlockIdMapping,
                                                   Dictionary<int, AssetLocation> oldItemIdMapping, int schematicSeed, bool resolveImports)
    {
        base.OnLoadCollectibleMappings(worldForResolve, oldBlockIdMapping, oldItemIdMapping, schematicSeed, resolveImports);
        _blank?.FixMapping(oldBlockIdMapping, oldItemIdMapping, worldForResolve);
    }

    // ---- Info ----

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
    {
        base.GetBlockInfo(forPlayer, dsc);
        string L(string key, params object[] args) => Lang.Get(GearCutterSystem.Domain + ":gearcutter-" + key, args);
        if (_parts.Next is { } next)
        {
            dsc.AppendLine(L("info-next", StageName(next)));
            var after = GearCutterRequires.Stages.Where(s => s > next && !_parts.Has(s)).ToList();
            if (after.Count > 0)
                dsc.AppendLine(L("info-then", string.Join(", ", after.Select(s => StageName(s)))));
        }
        if (_parts.Master is var k and > 0)
            dsc.AppendLine(L(k == 1 ? "info-master-small" : "info-master-large"));
        int teeth = GearCut.Teeth(_parts.Master is > 0 and var m ? m : 1);
        double fill = OilFill;
        if (_parts.Has(GearCutterStage.Cutter))
        {
            dsc.AppendLine(L("info-kit", _parts.KitLeft, _parts.KitCapacity,
                GearCutterWear.GearsLeft(WearPerGear, teeth, fill, _parts.KitLeft)));
        }
        if (BlankOn)
        {
            string done = Math.Floor(_job.Work).ToString("0");
            dsc.AppendLine(Running ? L("info-cutting", done, _job.End) : L("info-stopped", done, _job.End));
        }
        else if (_parts.Complete)
            dsc.AppendLine(L("info-empty"));
        Oil.Info(_oil, dsc, dryLoad: false);
        if (_oil != null)
            dsc.AppendLine(fill <= 0 ? L("info-wear-dry") : L("info-wear", GearCutterWear.Multiplier(fill).ToString("0.#")));
        float speed = ShaftSpeed;
        dsc.AppendLine(speed < MinSpeed ? L("info-nopower") : L("info-speed", speed.ToString("0.00")));
    }
}
