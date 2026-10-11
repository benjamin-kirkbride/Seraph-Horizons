using System.Text;
using SeraphHorizons.Mod.Core;
using SeraphHorizons.Mod.CylinderBorer.Core;
using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Machines.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.CylinderBorer;

/// <summary>
/// The cylinder borer's controller (#639). Holds the fitted parts (<see cref="CylinderBorerParts"/>:
/// every item's code and the cutter head's durability), the casting in the cradle as its item stack
/// and the bore (<see cref="BoreJob"/>: W, the head's travel through it), and its MachineOil tank.
/// The server bores from the power ghost's shaft angle, wears the head by the tank's fill when a
/// cylinder is done, drops the bored cylinder at the output face or puts it in a container there,
/// takes the next casting from a chest or hopper at the infeed face, and keeps the ghost cells
/// stamped; the client draws it (<see cref="CylinderBorerRenderer"/>, through
/// <see cref="ICylinderBorerView"/>). The rules are CylinderBorer/Core's.
/// </summary>
public class BECylinderBorer : BlockEntity, ICylinderBorerView
{
    private static readonly AssetLocation BreakSound = new("game", "sounds/effect/toolbreak");
    private static readonly AssetLocation LatchSound = new("game", "sounds/effect/latch");
    private static readonly AssetLocation CylinderSound = new("game", "sounds/block/ingot");
    private static readonly AssetLocation BoreSound = new("game", "sounds/effect/gears");
    private const string PartKeyPrefix = "part-";
    // The server syncs W at least this often (blocks of travel); the renderer follows the shaft between.
    private const double SyncStep = 1.0 / 32;
    // After a cylinder is done the infeed waits this long, so the head eases back out of the empty
    // cradle (the model's presence) before the next casting goes in.
    private const long ClearMs = 600;

    private CylinderBorerParts _parts = new();
    private ItemStack? _blank;
    private BoreJob _job = BoreJob.None;
    private OilState? _oil;
    private float _lastAngle;
    private bool _angleSeeded;
    private long _finishedAt = long.MinValue / 2;
    private float _smokeSeconds;
    private float _soundSeconds;
    private float? _serverMinSpeed;
    private float? _serverTurnsIron;
    private float? _serverTurnsSteel;
    private int? _serverWearPerCylinder;
    private Dictionary<Int3, Cuboidf[]>? _cells;
    private Dictionary<Int3, Cuboidf[]>? _collision;
    private Dictionary<Int3, Cuboidf[]>? _cellsLoaded;
    private Dictionary<Int3, Cuboidf[]>? _collisionLoaded;
    private CylinderBorerRenderer? _renderer;

    public Side Side { get; private set; } = Side.North;
    public CylinderBorerParts Parts => _parts;
    public bool Complete => _parts.Complete;
    public bool BlankOn => _job.On && _blank != null;
    public ItemStack? Blank => _blank;
    public BoreJob Job => _job;
    /// <summary>The borer's oil, or null without the <c>MachineOil</c> switch.</summary>
    public OilState? Oiling => _oil;
    /// <summary>The tank's fill, 0..1: 1 without a tank (MachineOil off), so the head wears at its base rate.</summary>
    public double OilFill => _oil == null ? 1 : OilDrain.Fill(_oil.Tank);
    public float MinSpeed => _serverMinSpeed ?? Config.MinSpeed;
    public double TurnsPerBlock(int k) => k switch
    {
        1 => _serverTurnsIron ?? Config.TurnsPerBlockIron,
        2 => _serverTurnsSteel ?? Config.TurnsPerBlockSteel,
        _ => 0,
    };
    private int WearPerCylinder => _serverWearPerCylinder ?? Config.HeadWearPerCylinder;
    public float ShaftAngle => Power?.AngleRad ?? 0;
    public float ShaftSpeed => Math.Abs(Power?.TrueSpeed ?? 0);
    /// <summary>Complete, a casting in and the shaft fast enough: the bore runs.</summary>
    public bool Running => Boring.Running(_parts.Complete, BlankOn, ShaftSpeed, MinSpeed);

    // ICylinderBorerView
    public bool PartFitted(string? requires) => _parts.Fitted(requires);
    public int JobClass => BlankOn ? _job.Class : 0;
    public double JobWork => _job.Work;

    private CylinderBorerSystem System => CylinderBorerSystem.Of(Api);
    private CylinderBorerConfig Config => System.Config;
    private CylinderBorerRig? Rig => System.Rig;

    /// <summary>The power ghost's mechanical power behavior, looked up afresh each time.</summary>
    public BEBehaviorCylinderBorerMP? Power =>
        Api != null && Rig is { } rig
            ? Api.World.BlockAccessor.GetBlockEntity(CellPos(rig.PowerCell))?.GetBehavior<BEBehaviorCylinderBorerMP>()
            : null;

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        Side = Sides.TryParse(Block.Variant["side"], out var side) ? side : Side.North;
        BuildBoxes();
        if (api.Side == EnumAppSide.Server)
        {
            _oil ??= Oil.NewOwn(api, OilMachine.CylinderBorer);
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
            if (api is ICoreClientAPI capi && Rig is { } rig && Block is BlockCylinderBorer)
                _renderer = new CylinderBorerRenderer(capi, this, this, rig);
        }
    }

    // ---- Footprint ----

    public BlockPos CellPos(Int3 local)
    {
        var w = Footprint.ToWorld(local, Side);
        return Pos.AddCopy(w.X, w.Y, w.Z);
    }

    /// <summary>A native-frame point as a world position.</summary>
    public Vec3d WorldPoint(Float3 native) =>
        MachinePoints.WorldPoint(Pos, native, Side);

    /// <summary>Every ghost cell's world position, and whether it is the power ghost.</summary>
    public IEnumerable<(BlockPos Pos, bool Power)> GhostCells() =>
        Rig is { } rig ? rig.GhostCells.Select(c => (CellPos(c.Pos), c.Pos == rig.PowerCell)) : [];

    /// <summary>A cell's selection boxes, the casting's part of the cell among them while one is in the cradle.</summary>
    public Cuboidf[]? CellBoxes(BlockPos cellPos) => Lookup(BlankOn ? _cellsLoaded : _cells, cellPos);

    /// <summary>A cell's selection boxes and, on a column's top cell, the rig's collision-only lid.</summary>
    public Cuboidf[]? CollisionBoxes(BlockPos cellPos) => Lookup(BlankOn ? _collisionLoaded : _collision, cellPos);

    private Cuboidf[]? Lookup(Dictionary<Int3, Cuboidf[]>? table, BlockPos cellPos) =>
        table?.GetValueOrDefault(Footprint.ToLocal(new Int3(cellPos.X - Pos.X, cellPos.Y - Pos.Y, cellPos.Z - Pos.Z), Side));

    // The boxes never change: built once, then only read (collision lookups can run off the main
    // thread). The loaded tables add the casting's box, cut into the cells it crosses.
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
        var casting = rig.CastingByCell();
        var cellsLoaded = new Dictionary<Int3, Cuboidf[]>(cells);
        var collisionLoaded = new Dictionary<Int3, Cuboidf[]>(collision);
        foreach (var (cell, box) in casting)
        {
            cellsLoaded[cell] = [.. cells[cell], World(box)];
            collisionLoaded[cell] = [.. collision[cell], World(box)];
        }
        _cells = cells;
        _collision = collision;
        _cellsLoaded = cellsLoaded;
        _collisionLoaded = collisionLoaded;
    }

    private Block? GhostBlock(bool power) =>
        Api.World.GetBlock(new AssetLocation(CylinderBorerSystem.Domain, power ? "cylinderborer-ghostpower-" + Side.Code() : "cylinderborer-ghost"));

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
        if (Api.World.BlockAccessor.GetBlockEntity(pos) is BECylinderBorerGhost be)
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
                if (ba.GetBlockEntity(pos) is BECylinderBorerGhost be && !Pos.Equals(be.Principal))
                {
                    be.Principal = Pos.Copy();
                    be.MarkDirty(true);
                }
            }
            else if (current.Id == 0 || current is BlockCylinderBorerGhost)
                StampGhost(expected, pos);
        }
    }

    public void RemoveGhosts()
    {
        if (Api.Side != EnumAppSide.Server)
            return;
        var ba = Api.World.BlockAccessor;
        foreach (var (pos, _) in GhostCells())
            if (ba.GetBlock(pos) is BlockCylinderBorerGhost && ba.GetBlockEntity(pos) is BECylinderBorerGhost be && Pos.Equals(be.Principal))
                ba.SetBlock(0, pos);
    }

    // ---- Interaction ----

    /// <summary>
    /// Right-click on the borer or any ghost. Holding oil, it pours (MachineOil). In creative mode,
    /// Ctrl on an incomplete borer fits its next stage with nothing taken. Ctrl takes back the cutter
    /// head, unworn by it, else the casting in the cradle. A part in hand is fitted if it is the next
    /// stage's; an iron or steel cylinder casting goes in the cradle (a bored cylinder, what comes off,
    /// never does). Anything else held is the item's own business. Decided and done on the server;
    /// the client only says whether the click is the borer's.
    /// </summary>
    public bool OnInteract(IPlayer byPlayer)
    {
        if (Oil.Interact(this, _oil, byPlayer) is { } oiled)
            return oiled;
        var slot = byPlayer.InventoryManager.ActiveHotbarSlot;
        var controls = byPlayer.Entity.Controls;
        bool take = controls.CtrlKey && !controls.ShiftKey;
        string? code = slot?.Itemstack?.Collectible?.Code?.ToString();
        bool part = CylinderBorerParts.IsPart(code);
        bool blank = Boring.ClassOfBlank(code) != 0;
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
    /// Shift, on a borer with a stage still to fit.</summary>
    public bool CreativeShortcutApplies(IPlayer byPlayer)
    {
        var controls = byPlayer.Entity.Controls;
        return !_parts.Complete
               && CreativeUpgrades.Applies(byPlayer.WorldData?.CurrentGameMode == EnumGameMode.Creative, controls.CtrlKey, controls.ShiftKey);
    }

    /// <summary>The creative shortcut (server side): fits the next stage's item (a new cutter head
    /// last) with nothing taken from the player.</summary>
    public bool FitNextPart(IPlayer? byPlayer)
    {
        if (_parts.NextPart is not { } next)
            return false;
        if (PartStack(Api.World, next) is not { } stack)
        {
            Api.Logger.Warning("[seraphhorizons] Cylinder borer: no {0} for the creative shortcut", next);
            return false;
        }
        return TryFitPart(new DummySlot(stack), byPlayer, free: true);
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
        if (CylinderBorerParts.StagesOf(code).Contains(CylinderBorerStage.CutterHead))
        {
            capacity = Math.Max(1, stack.Collectible.GetMaxDurability(stack));
            left = stack.Collectible.GetRemainingDurability(stack);
        }
        var verdict = _parts.CanFit(code, out _, capacity > 0 ? left : 1);
        if (verdict != CylinderBorerFitVerdict.Fits)
        {
            switch (verdict)
            {
                case CylinderBorerFitVerdict.OutOfOrder:
                    Error(byPlayer, "error-order", StageName(_parts.Next));
                    break;
                case CylinderBorerFitVerdict.AlreadyFitted:
                    Error(byPlayer, "error-part-fitted");
                    break;
                case CylinderBorerFitVerdict.HeadSpent:
                    Error(byPlayer, "error-head-spent");
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
    public static string StageName(CylinderBorerStage? stage) =>
        stage is { } s ? Lang.Get(CylinderBorerSystem.Domain + ":cylinderborer-info-stage-" + CylinderBorerRequires.Name(s)) : "";

    /// <summary>Ctrl + right-click (server side): the cutter head, with the durability it has left
    /// (taking it back wears it no further), else the casting in the cradle, its bore lost.</summary>
    public bool TakeBack(IPlayer byPlayer)
    {
        switch (_parts.TakeBack(BlankOn))
        {
            case CylinderBorerTakeBack.Head:
                if (_parts.RemoveHead() is not { } head || HeadStack(head.Left) is not { } stack)
                    return false;
                Give(byPlayer, stack);
                break;
            case CylinderBorerTakeBack.Blank:
                var blank = _blank!;
                ClearJob();
                Give(byPlayer, blank);
                break;
            default:
                return false;
        }
        Api.World.PlaySoundAt(LatchSound, Pos, 0, byPlayer);
        MarkDirty(true);
        return true;
    }

    /// <summary>A cutter head with <paramref name="left"/> durability.</summary>
    private ItemStack? HeadStack(int left)
    {
        if (Api.World.GetItem(new AssetLocation(CylinderBorerParts.HeadCode)) is not { } item)
            return null;
        var stack = new ItemStack(item);
        if (left < item.GetMaxDurability(stack))
            stack.Attributes.SetInt("durability", Math.Max(1, left));
        return stack;
    }

    /// <summary>The bored cylinder a casting of class <paramref name="k"/> becomes, or null when it
    /// does not exist in this game.</summary>
    private Item? CylinderItem(int k) =>
        Boring.CylinderFor(k) is { } code && Api.World.GetItem(new AssetLocation(code)) is { Id: > 0, IsMissing: false } item ? item : null;

    /// <summary>Puts a casting from <paramref name="slot"/> in the cradle, if the borer takes it now.</summary>
    public bool TryLoadBlank(ItemSlot slot, IPlayer? byPlayer)
    {
        string? code = slot.Itemstack?.Collectible?.Code?.ToString();
        switch (Boring.CanLoad(code, _parts.Complete, BlankOn))
        {
            case BoreLoadVerdict.Loads:
                break;
            case BoreLoadVerdict.Incomplete:
                return Error(byPlayer, _parts.Next == CylinderBorerStage.CutterHead && _parts.Has(CylinderBorerStage.Feed)
                    ? "error-no-head" : "error-incomplete");
            case BoreLoadVerdict.Occupied:
                return Error(byPlayer, "error-occupied");
            default:
                return false;
        }
        if (CylinderItem(Boring.ClassOfBlank(code)) == null)
            return Error(byPlayer, "error-no-cylinder");
        Load(slot.TakeOut(1));
        slot.MarkDirty();
        return true;
    }

    private void Load(ItemStack blank)
    {
        _blank = blank;
        _job = new BoreJob(Boring.ClassOfBlank(blank.Collectible.Code.ToString()), 0);
        Api.World.PlaySoundAt(LatchSound, Pos, 0);
        MarkDirty(true);
    }

    private void ClearJob()
    {
        _blank = null;
        _job = BoreJob.None;
    }

    private void Give(IPlayer byPlayer, ItemStack stack)
    {
        if (!byPlayer.InventoryManager.TryGiveItemstack(stack, true))
            Api.World.SpawnItemEntity(stack, Pos.ToVec3d().Add(0.5, 1.2, 0.5));
    }

    private bool Error(IPlayer? byPlayer, string key, params object[] args)
    {
        if (byPlayer is IServerPlayer sp)
            sp.SendIngameError("cylinderborer-" + key, Lang.GetL(sp.LanguageCode, CylinderBorerSystem.Domain + ":cylinderborer-" + key, args));
        return false;
    }

    // ---- The bore ----

    private void OnTick(float dt)
    {
        var power = Power;
        float angle = power?.AngleRad ?? 0, speed = Math.Abs(power?.TrueSpeed ?? 0);
        if (!Boring.Running(_parts.Complete, BlankOn, speed, Config.MinSpeed) || !_angleSeeded)
        {
            _lastAngle = angle;
            _angleSeeded = true;
            return;
        }
        float advance = ShaftClock.AngleAdvance(_lastAngle, angle, speed, dt);
        _lastAngle = angle;
        Bore(advance);
    }

    /// <summary>Bores by <paramref name="radians"/> of shaft rotation (server side; call only while
    /// running). At the end of the traverse the head wears by the oil's fill, the tank drains, the
    /// cylinder goes out and, a moment later, the next casting comes in. Returns whether a cylinder
    /// was finished.</summary>
    public bool Bore(double radians)
    {
        if (!BlankOn || !_parts.Complete)
            return false;
        var before = _job;
        (_job, bool finished) = _job.Advance(radians, Config.TurnsPerBlock(_job.Class));
        if (finished)
        {
            Finish();
            return true;
        }
        if (Math.Floor(before.Work / SyncStep) != Math.Floor(_job.Work / SyncStep))
            MarkDirty();
        return false;
    }

    /// <summary>The cylinder is bored: the head wears by the fill the tank has now, then the tank
    /// drains a cylinder's oil; a spent head breaks with the tool-break sound and the borer stops
    /// until a new one goes in. The bored cylinder goes into a container at the output face, else
    /// drops there; the infeed waits a moment before the next casting.</summary>
    private void Finish()
    {
        int k = _job.Class;
        int wear = CutterHeadWear.WearFor(Config.HeadWearPerCylinder, OilFill, _parts.HeadLeft);
        Oil.Drain(_oil, Oil.DrainPerJob(Api, OilMachine.CylinderBorer));
        if (_parts.WearHead(wear))
            Api.World.PlaySoundAt(BreakSound, Pos.X + 0.5, Pos.Y + 0.5, Pos.Z + 0.5);
        ClearJob();
        if (CylinderItem(k) is { } cylinder)
            Deliver(new ItemStack(cylinder));
        _finishedAt = Api.World.ElapsedMilliseconds;
        MarkDirty(true);
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
        Api.World.PlaySoundAt(CylinderSound, at.X, at.Y, at.Z);
    }

    /// <summary>A complete borer with its cradle empty and its shaft turning takes one casting from a
    /// container at the infeed face, a moment after the last cylinder was done. Returns whether one
    /// went in.</summary>
    public bool PullFromInfeed()
    {
        if (Api.Side != EnumAppSide.Server || BlankOn || !_parts.Complete || ShaftSpeed < MinSpeed || Rig is not { } rig)
            return false;
        if (Api.World.ElapsedMilliseconds - _finishedAt < ClearMs)
            return false;
        foreach (var local in rig.InfeedNeighbours())
        {
            if (Api.World.BlockAccessor.GetBlockEntity(CellPos(local)) is not BlockEntityContainer container)
                continue;
            foreach (var slot in container.Inventory)
            {
                string? code = slot.Itemstack?.Collectible?.Code?.ToString();
                if (Boring.CanLoad(code, true, false) != BoreLoadVerdict.Loads || CylinderItem(Boring.ClassOfBlank(code)) == null)
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

    // The client's own: the client removes a broken borer at once, and an update the server sent
    // before it heard of the break then brings the block entity back over air: drop it. A dry borer
    // smokes at the bore while it cuts, and a boring borer is heard.
    private void OnClientTick(float dt)
    {
        if (Api.World.BlockAccessor.GetBlock(Pos) is not BlockCylinderBorer)
        {
            DisposeRenderer();
            Api.World.BlockAccessor.RemoveBlockEntity(Pos);
            return;
        }
        if (Api is not ICoreClientAPI capi || Rig is not { } rig)
            return;
        bool running = Running;
        if ((_smokeSeconds += dt) >= 0.4f)
        {
            _smokeSeconds = 0;
            if (_oil is { Dry: true } && running)
                Oil.Smoke(capi, WorldPoint(rig.Chips));
        }
        if (!running)
        {
            _soundSeconds = 0;
            return;
        }
        if ((_soundSeconds -= dt) <= 0)
        {
            _soundSeconds = 2.4f;
            var at = WorldPoint(rig.Chips);
            capi.World.PlaySoundAt(BoreSound, at.X, at.Y, at.Z, null, true, 16, 0.4f);
        }
    }

    // ---- Breaking ----

    /// <summary>What breaking the frame gives besides the frame: every fitted part (the cutter head
    /// with its durability) and the casting in the cradle, its bore lost.</summary>
    public IEnumerable<ItemStack> PartDrops()
    {
        foreach (var drop in _parts.Returns())
        {
            if (drop.Durability is { } left)
            {
                if (HeadStack(left) is { } head)
                    yield return head;
            }
            else if (PartStack(Api.World, drop.Code) is { } stack)
                yield return stack;
        }
        if (BlankOn)
            yield return _blank!.Clone();
    }

    /// <summary>One of a part, by its code: an item, or a block (the bushings' metal parts are one);
    /// null when the game has neither.</summary>
    public static ItemStack? PartStack(IWorldAccessor world, string code)
    {
        var loc = new AssetLocation(code);
        if (world.GetItem(loc) is { } item)
            return new ItemStack(item);
        return world.GetBlock(loc) is { Id: > 0 } block ? new ItemStack(block) : null;
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
        foreach (var stage in CylinderBorerRequires.Stages)
            tree.RemoveAttribute(PartKeyPrefix + CylinderBorerRequires.Name(stage));
        foreach (var (requires, code) in _parts.Snapshot())
            tree.SetString(PartKeyPrefix + requires, code);
        tree.SetInt("headLeft", _parts.HeadLeft);
        tree.SetInt("headCapacity", _parts.HeadCapacity);
        if (BlankOn)
            tree.SetItemstack("blank", _blank);
        else
            tree.RemoveAttribute("blank");
        tree.SetInt("class", _job.Class);
        tree.SetDouble("work", _job.Work);
        if (Api?.Side == EnumAppSide.Server)
        {
            tree.SetFloat("minSpeed", Config.MinSpeed);
            tree.SetFloat("turnsPerBlockIron", Config.TurnsPerBlockIron);
            tree.SetFloat("turnsPerBlockSteel", Config.TurnsPerBlockSteel);
            tree.SetInt("wearPerCylinder", Config.HeadWearPerCylinder);
        }
        if (_oil != null)
            Oil.Write(tree, _oil);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);
        var fitted = new Dictionary<string, string>();
        foreach (var stage in CylinderBorerRequires.Stages)
            if (tree.GetString(PartKeyPrefix + CylinderBorerRequires.Name(stage)) is { Length: > 0 } code)
                fitted[CylinderBorerRequires.Name(stage)] = code;
        _parts = CylinderBorerParts.Restore(fitted, tree.GetInt("headLeft"), tree.GetInt("headCapacity"));
        _blank = tree.GetItemstack("blank");
        if (_blank != null && !_blank.ResolveBlockOrItem(worldForResolving))
            _blank = null;
        _job = _blank == null ? BoreJob.None : BoreJob.Restore(tree.GetInt("class"), tree.GetDouble("work"));
        if (!_job.On)
            _blank = null;
        _oil = Oil.LoadOwn(tree, worldForResolving, OilMachine.CylinderBorer);
        if (worldForResolving.Side == EnumAppSide.Client)
        {
            _serverMinSpeed = tree.TryGetFloat("minSpeed");
            _serverTurnsIron = tree.TryGetFloat("turnsPerBlockIron");
            _serverTurnsSteel = tree.TryGetFloat("turnsPerBlockSteel");
            _serverWearPerCylinder = tree.TryGetInt("wearPerCylinder");
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
        string L(string key, params object[] args) => Lang.Get(CylinderBorerSystem.Domain + ":cylinderborer-" + key, args);
        if (_parts.Next is { } next)
        {
            dsc.AppendLine(L("info-next", StageName(next)));
            var after = CylinderBorerRequires.Stages.Where(s => s > next && !_parts.Has(s)).ToList();
            if (after.Count > 0)
                dsc.AppendLine(L("info-then", string.Join(", ", after.Select(s => StageName(s)))));
        }
        double fill = OilFill;
        if (_parts.Has(CylinderBorerStage.CutterHead))
            dsc.AppendLine(L("info-head", _parts.HeadLeft, _parts.HeadCapacity,
                CutterHeadWear.CylindersLeft(WearPerCylinder, fill, _parts.HeadLeft)));
        if (BlankOn)
        {
            string metal = L("metal-" + Boring.MetalOf(_job.Class));
            string done = (100 * _job.Work / Boring.Travel).ToString("0");
            dsc.AppendLine(Running ? L("info-boring", metal, done) : L("info-stopped", metal, done));
        }
        else if (_parts.Complete)
            dsc.AppendLine(L("info-empty"));
        Oil.Info(_oil, dsc, dryLoad: false);
        if (_oil != null)
            dsc.AppendLine(fill <= 0 ? L("info-wear-dry") : L("info-wear", CutterHeadWear.Multiplier(fill).ToString("0.#")));
        float speed = ShaftSpeed;
        dsc.AppendLine(speed < MinSpeed ? L("info-nopower") : L("info-speed", speed.ToString("0.00")));
    }
}
