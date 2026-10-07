using System.Text;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.PressBrake.Core;
using SeraphHorizons.Mod.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.PressBrake;

/// <summary>
/// The press brake's controller. Holds the fitted parts (<see cref="PressBrakeParts"/>), the plate
/// on the bed as its item stack and the fold (<see cref="FoldJob"/>: W, 0..1), and who is working the
/// lever (<see cref="LeverHolds"/>). The player works it by holding right-click on it, as on the
/// quern: the server advances W while anyone holds, sounds the fold, and at W = 1 drops one angle
/// at the output face (or puts them in a container there); a lever worked on an empty bed
/// takes the next plate from a chest or hopper at the infeed face. The server keeps the ghost cell
/// stamped; the client draws the brake (<see cref="PressBrakeRenderer"/>, through
/// <see cref="IPressBrakeView"/>). The rules are PressBrake/Core's.
/// </summary>
public class BEPressBrake : BlockEntity, IPressBrakeView
{
    private static readonly AssetLocation LatchSound = new("game", "sounds/effect/latch");
    private static readonly AssetLocation PlateSound = new("game", "sounds/block/plate");
    private static readonly AssetLocation BendSound = new("game", "sounds/block/heavymetal-hit");
    private static readonly AssetLocation AngleSound = new("game", "sounds/block/chute");
    private static readonly AssetLocation[] CreakSounds =
        [.. Enumerable.Range(1, 4).Select(i => new AssetLocation("game", $"sounds/block/woodcreak_{i}"))];
    private const string PartKeyPrefix = "part-";
    // The server syncs W at least this often (plates); the renderer follows the lever between.
    private const double SyncStep = 0.02;
    // After a plate is done, the infeed waits this long, so the angle clears off the leaf (the
    // model eases the bar and screws back) before the next plate goes on.
    private const long ClearMs = 600;
    // A client's own player holding right-click counts as working for this long after its last step.
    private const long LocalHoldMs = 250;
    // A client keeps the lever worked on an empty bed this long, for the server to load a plate from
    // the infeed.
    private const long EmptyGraceMs = 1200;

    private PressBrakeParts _parts = new();
    private ItemStack? _plate;
    private FoldJob _job = FoldJob.None;
    private readonly LeverHolds _holds = new();
    private bool _held;
    private long _finishedAt = long.MinValue / 2;
    private long _localHeldAt = long.MinValue / 2;
    private long _clientEmptySince = long.MinValue / 2;
    private bool _clientPlateWasOn;
    private float _creakSeconds;
    private float? _serverTurnsLead;
    private float? _serverTurnsCopper;
    private Dictionary<Int3, Cuboidf[]>? _cells;
    private Dictionary<Int3, Cuboidf[]>? _collision;
    private PressBrakeRenderer? _renderer;

    public Side Side { get; private set; } = Side.North;
    public PressBrakeParts Parts => _parts;
    public bool Complete => _parts.Complete;
    /// <summary>A plate is on the bed.</summary>
    public bool PlateOn => _job.On && _plate != null;
    public ItemStack? Plate => _plate;
    public FoldJob Job => _job;
    /// <summary>Someone works the lever on a complete brake with a plate on: the fold runs.</summary>
    public bool Running => Folding.Running(_parts.Complete, PlateOn, Held);

    public double LeverTurnsPerPlate(int k) => k switch
    {
        1 => _serverTurnsLead ?? Config.LeverTurnsPerPlateLead,
        2 => _serverTurnsCopper ?? Config.LeverTurnsPerPlateCopper,
        _ => 0,
    };

    // IPressBrakeView
    public bool PartFitted(string? requires) => _parts.Fitted(requires);
    public string? ScrewMetal => PressBrakeParts.MetalOf(_parts.FittedIn(PressBrakeStage.Screws));
    public string? EdgeMetal => PressBrakeParts.MetalOf(_parts.FittedIn(PressBrakeStage.Edge));
    public int PlateClass => PlateOn ? _job.Class : 0;
    public double FoldWork => _job.Work;
    public bool Held =>
        _held || Api is { Side: EnumAppSide.Client } && Api.World.ElapsedMilliseconds - _localHeldAt <= LocalHoldMs;

    private PressBrakeSystem System => PressBrakeSystem.Of(Api);
    private PressBrakeConfig Config => System.Config;
    private PressBrakeRig? Rig => System.Rig;

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        Side = Sides.TryParse(Block.Variant["side"], out var side) ? side : Side.North;
        BuildBoxes();
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
            RegisterGameTickListener(OnClientTick, 100);
            if (api is ICoreClientAPI capi && Rig is { } rig && Block is BlockPressBrake)
                _renderer = new PressBrakeRenderer(capi, this, this, rig);
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

    /// <summary>Every ghost cell's world position.</summary>
    public IEnumerable<BlockPos> GhostCells() =>
        Rig is { } rig ? rig.GhostCells.Select(c => CellPos(c.Pos)) : [];

    public Cuboidf[]? CellBoxes(BlockPos cellPos) => Lookup(_cells, cellPos);

    /// <summary>A cell's selection boxes and the rig's collision-only lid.</summary>
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

    private Block? GhostBlock() => Api.World.GetBlock(new AssetLocation(PressBrakeSystem.Domain, "pressbrake-ghost"));

    public void PlaceGhosts()
    {
        if (Api.Side != EnumAppSide.Server || GhostBlock() is not { } ghost)
            return;
        foreach (var pos in GhostCells())
            StampGhost(ghost, pos);
    }

    private void StampGhost(Block ghost, BlockPos pos)
    {
        Api.World.BlockAccessor.SetBlock(ghost.BlockId, pos);
        if (Api.World.BlockAccessor.GetBlockEntity(pos) is BEPressBrakeGhost be)
        {
            be.Principal = Pos.Copy();
            be.MarkDirty(true);
        }
    }

    /// <summary>Restores the ghost cell if it went missing, and points it back here.</summary>
    public void EnsureGhosts()
    {
        if (Api.Side != EnumAppSide.Server || GhostBlock() is not { } expected)
            return;
        var ba = Api.World.BlockAccessor;
        foreach (var pos in GhostCells())
        {
            if (ba.GetChunkAtBlockPos(pos) == null)
                continue;
            var current = ba.GetBlock(pos);
            if (current.Id == expected.Id)
            {
                if (ba.GetBlockEntity(pos) is BEPressBrakeGhost be && !Pos.Equals(be.Principal))
                {
                    be.Principal = Pos.Copy();
                    be.MarkDirty(true);
                }
            }
            else if (current.Id == 0 || current is BlockPressBrakeGhost)
                StampGhost(expected, pos);
        }
    }

    public void RemoveGhosts()
    {
        if (Api.Side != EnumAppSide.Server)
            return;
        var ba = Api.World.BlockAccessor;
        foreach (var pos in GhostCells())
            if (ba.GetBlock(pos) is BlockPressBrakeGhost && ba.GetBlockEntity(pos) is BEPressBrakeGhost be && Pos.Equals(be.Principal))
                ba.SetBlock(0, pos);
    }

    // ---- Interaction ----

    /// <summary>
    /// Right-click on the brake or its ghost. Ctrl takes back: in creative mode on an incomplete
    /// brake it fits the next stage with nothing taken; else a plate still flat comes off the bed, or
    /// with no plate on the last part fitted comes out. A part in hand is fitted if it is the next
    /// stage's; a lead or copper plate goes on an empty bed. Anything else (an empty hand, a tool, a
    /// plate when one is already on) on a complete brake starts working the lever, held as on the
    /// quern (<see cref="OnWorkStep"/>); Shift lets a held block be placed against it instead.
    /// Decided and done on the server; the client says whether the click is the brake's.
    /// </summary>
    public bool OnInteract(IPlayer byPlayer)
    {
        var slot = byPlayer.InventoryManager.ActiveHotbarSlot;
        var controls = byPlayer.Entity.Controls;
        bool take = controls.CtrlKey && !controls.ShiftKey;
        string? code = slot?.Itemstack?.Collectible?.Code?.ToString();
        bool server = Api.Side == EnumAppSide.Server;
        if (take)
        {
            if (!server)
                return true;
            if (CreativeShortcutApplies(byPlayer))
                FitNextPart(byPlayer);
            else
                TakeBack(byPlayer);
            return true;
        }
        if (PressBrakeParts.IsPart(code))
        {
            if (server)
                TryFitPart(slot!, byPlayer);
            return true;
        }
        if (Folding.ClassOfPlate(code) != 0 && !PlateOn)
        {
            // a click loads it; held on, the steps that follow work the lever
            return !server || TryLoadPlate(slot!, byPlayer);
        }
        if (controls.ShiftKey || !_parts.Complete)
            return false;
        if (!server)
            return true;
        if (!PlateOn)
            PullFromInfeed(waitForClear: false);
        if (!PlateOn)
            return Error(byPlayer, "error-no-plate");
        return StartWork(byPlayer);
    }

    private bool StartWork(IPlayer byPlayer)
    {
        _holds.Hold(byPlayer.PlayerUID, Api.World.ElapsedMilliseconds);
        UpdateHeld();
        return true;
    }

    /// <summary>
    /// Right-click still held on the brake (both sides, every tick, as the quern's grinding): the
    /// server counts the player as working the lever; the client marks its own player as working for
    /// the renderer. Whether the player goes on: while the brake is complete and has a plate on, and
    /// on the server just after a plate is done while the infeed has another for the next.
    /// </summary>
    public bool OnWorkStep(IPlayer byPlayer, float secondsUsed)
    {
        long now = Api.World.ElapsedMilliseconds;
        if (Api.Side == EnumAppSide.Client)
        {
            _localHeldAt = now;
            return _parts.Complete && (PlateOn || secondsUsed < 0.5f || now - _clientEmptySince <= EmptyGraceMs);
        }
        if (!_parts.Complete)
            return false;
        if (!PlateOn)
        {
            if (now - _finishedAt >= ClearMs)
                PullFromInfeed(waitForClear: true);
            if (!PlateOn && !(now - _finishedAt < ClearMs && InfeedHasPlate()))
            {
                Release(byPlayer);
                return false;
            }
        }
        _holds.Hold(byPlayer.PlayerUID, now);
        UpdateHeld();
        return true;
    }

    /// <summary>The player let go of the lever.</summary>
    public void Release(IPlayer byPlayer)
    {
        if (Api.Side == EnumAppSide.Client)
        {
            _localHeldAt = long.MinValue / 2;
            return;
        }
        _holds.Release(byPlayer.PlayerUID);
        UpdateHeld();
    }

    // The server's "someone is working the lever", synced when it changes.
    private void UpdateHeld()
    {
        bool held = _holds.Any(Api.World.ElapsedMilliseconds) && _parts.Complete && PlateOn;
        if (held == _held)
            return;
        _held = held;
        MarkDirty(false);
    }

    /// <summary>Whether a click is the creative shortcut's: a player in creative mode, Ctrl without
    /// Shift, on a brake with a stage still to fit.</summary>
    public bool CreativeShortcutApplies(IPlayer byPlayer)
    {
        var controls = byPlayer.Entity.Controls;
        return !_parts.Complete
               && CreativeUpgrades.Applies(byPlayer.WorldData?.CurrentGameMode == EnumGameMode.Creative, controls.CtrlKey, controls.ShiftKey);
    }

    /// <summary>The creative shortcut (server side): fits the next stage's first item with nothing
    /// taken from the player.</summary>
    public bool FitNextPart(IPlayer? byPlayer)
    {
        if (_parts.NextPart is not { } next)
            return false;
        if (Api.World.GetItem(new AssetLocation(next)) is not { } item)
        {
            Api.Logger.Warning("[seraphhorizons] Press brake: no {0} for the creative shortcut", next);
            return false;
        }
        return TryFitPart(new DummySlot(new ItemStack(item)), byPlayer, free: true);
    }

    /// <summary>Fits the part in <paramref name="slot"/> if it is the next stage's (server side);
    /// false, with an error to the player, when the rules say no. One item is taken from the slot
    /// unless the player is in creative mode or it is <paramref name="free"/>.</summary>
    public bool TryFitPart(ItemSlot slot, IPlayer? byPlayer, bool free = false)
    {
        string? code = slot.Itemstack?.Collectible?.Code?.ToString();
        if (code == null)
            return false;
        switch (_parts.CanFit(code, out _))
        {
            case PressBrakeFitVerdict.Fits:
                break;
            case PressBrakeFitVerdict.OutOfOrder:
                return Error(byPlayer, "error-order", StageName(_parts.Next));
            case PressBrakeFitVerdict.AlreadyFitted:
                return Error(byPlayer, "error-part-fitted");
            default:
                return Error(byPlayer, "error-not-a-part");
        }
        _parts.Fit(code);
        if (!free && byPlayer?.WorldData.CurrentGameMode != EnumGameMode.Creative)
        {
            slot.TakeOut(1);
            slot.MarkDirty();
        }
        Api.World.PlaySoundAt(Block.Sounds.Place, Pos, -0.25, byPlayer);
        MarkDirty(true);
        return true;
    }

    /// <summary>A stage's name as the block info and the errors show it.</summary>
    public static string StageName(PressBrakeStage? stage) =>
        stage is { } s ? Lang.Get(PressBrakeSystem.Domain + ":pressbrake-info-stage-" + PressBrakeRequires.Name(s)) : "";

    /// <summary>Ctrl + right-click (server side): a plate still flat comes off the bed; with no plate
    /// on, the last part fitted comes out (the edges, then the screws). A plate being folded stays.</summary>
    public bool TakeBack(IPlayer byPlayer)
    {
        if (PlateOn)
        {
            if (!_job.Untouched)
                return Error(byPlayer, "error-busy");
            var plate = _plate!;
            ClearJob();
            Give(byPlayer, plate);
            Api.World.PlaySoundAt(PlateSound, Pos, 0, byPlayer);
            MarkDirty(true);
            return true;
        }
        if (_parts.RemoveLast() is not { } code || Api.World.GetItem(new AssetLocation(code)) is not { } item)
            return false;
        Give(byPlayer, new ItemStack(item));
        Api.World.PlaySoundAt(LatchSound, Pos, 0, byPlayer);
        MarkDirty(true);
        return true;
    }

    /// <summary>The angle a plate of class <paramref name="k"/> is folded into, or null when it does
    /// not exist in this game (it is UnifiedPipes' item).</summary>
    private Item? AngleItem(int k) =>
        Folding.AngleFor(k) is { } code && Api.World.GetItem(new AssetLocation(code)) is { Id: > 0, IsMissing: false } item ? item : null;

    /// <summary>Puts a plate from <paramref name="slot"/> on the bed, if the brake takes it now.</summary>
    public bool TryLoadPlate(ItemSlot slot, IPlayer? byPlayer)
    {
        string? code = slot.Itemstack?.Collectible?.Code?.ToString();
        switch (Folding.CanLoad(code, _parts.Complete, PlateOn))
        {
            case FoldLoadVerdict.Loads:
                break;
            case FoldLoadVerdict.Incomplete:
                return Error(byPlayer, "error-incomplete");
            case FoldLoadVerdict.Occupied:
                return Error(byPlayer, "error-occupied");
            default:
                return false;
        }
        if (AngleItem(Folding.ClassOfPlate(code)) == null)
            return Error(byPlayer, "error-no-angle");
        Load(slot.TakeOut(1));
        slot.MarkDirty();
        return true;
    }

    private void Load(ItemStack plate)
    {
        _plate = plate;
        _job = new FoldJob(Folding.ClassOfPlate(plate.Collectible.Code.ToString()), 0);
        var at = WorldPoint(Rig?.Plate ?? new Float3(0.5f, 0.5f, 0.5f));
        Api.World.PlaySoundAt(PlateSound, at.X, at.Y, at.Z);
        MarkDirty(true);
    }

    private void ClearJob()
    {
        _plate = null;
        _job = FoldJob.None;
    }

    private void Give(IPlayer byPlayer, ItemStack stack)
    {
        if (!byPlayer.InventoryManager.TryGiveItemstack(stack, true))
            Api.World.SpawnItemEntity(stack, Pos.ToVec3d().Add(0.5, 1.2, 0.5));
    }

    private bool Error(IPlayer? byPlayer, string key, params object[] args)
    {
        if (byPlayer is IServerPlayer sp)
            sp.SendIngameError("pressbrake-" + key, Lang.GetL(sp.LanguageCode, PressBrakeSystem.Domain + ":pressbrake-" + key, args));
        return false;
    }

    // ---- The fold ----

    private void OnTick(float dt)
    {
        UpdateHeld();
        if (_held)
            Fold(Folding.LeverRadiansPerSecond * dt);
    }

    /// <summary>Folds by <paramref name="radians"/> of the lever clock (server side): W advances by
    /// their turns over the plate's lever turns, the bend is heard at the middle of the fold, and at
    /// W = 1 the plate is used up and its angle comes off. Returns the angles delivered.</summary>
    public int Fold(double radians)
    {
        if (!PlateOn || !_parts.Complete)
            return 0;
        var before = _job;
        (_job, bool finished) = _job.Advance(radians, Config.LeverTurnsPerPlate(_job.Class));
        if (Rig is { } rig && FoldJob.Crossed(rig.FoldMoments, before.Work, _job.Work) > 0)
        {
            var at = WorldPoint(rig.Edge);
            Api.World.PlaySoundAt(BendSound, at.X, at.Y, at.Z, null, true, 16, 0.6f);
        }
        if (finished)
        {
            int k = _job.Class;
            ClearJob();
            _finishedAt = Api.World.ElapsedMilliseconds;
            if (AngleItem(k) is { } angle)
                Deliver(new ItemStack(angle, Folding.AnglesPerPlate));
            UpdateHeld();
            MarkDirty(true);
            return Folding.AnglesPerPlate;
        }
        if (Math.Floor(before.Work / SyncStep) != Math.Floor(_job.Work / SyncStep))
            MarkDirty(false);
        return 0;
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
        Api.World.PlaySoundAt(AngleSound, at.X, at.Y, at.Z);
    }

    /// <summary>A plate a container at the infeed face holds that the brake would take.</summary>
    private IEnumerable<(BlockEntityContainer Container, ItemSlot Slot)> InfeedPlates()
    {
        if (Rig is not { } rig)
            yield break;
        foreach (var local in rig.InfeedNeighbours())
        {
            if (Api.World.BlockAccessor.GetBlockEntity(CellPos(local)) is not BlockEntityContainer container)
                continue;
            foreach (var slot in container.Inventory)
            {
                string? code = slot.Itemstack?.Collectible?.Code?.ToString();
                if (Folding.ClassOfPlate(code) != 0 && AngleItem(Folding.ClassOfPlate(code)) != null)
                    yield return (container, slot);
            }
        }
    }

    private bool InfeedHasPlate() => InfeedPlates().Any();

    /// <summary>A complete brake with nothing on its bed takes one plate from a container at the
    /// infeed face; it does so when the lever is worked on it, and, <paramref name="waitForClear"/>,
    /// only a moment after the last plate was done. Returns whether one went on.</summary>
    public bool PullFromInfeed(bool waitForClear = true)
    {
        if (Api.Side != EnumAppSide.Server || PlateOn || !_parts.Complete)
            return false;
        if (waitForClear && Api.World.ElapsedMilliseconds - _finishedAt < ClearMs)
            return false;
        if (InfeedPlates().FirstOrDefault() is not ({ } container, { } slot))
            return false;
        Load(slot.TakeOut(1));
        slot.MarkDirty();
        container.MarkDirty(true);
        return true;
    }

    // Once a second: the ghost.
    private void OnSlowTick(float dt) => EnsureGhosts();

    // The client's own: the client removes a broken brake at once, and an update the server sent
    // before it heard of the break then brings the block entity back over air: drop it. A worked
    // brake creaks.
    private void OnClientTick(float dt)
    {
        if (Api.World.BlockAccessor.GetBlock(Pos) is not BlockPressBrake)
        {
            DisposeRenderer();
            Api.World.BlockAccessor.RemoveBlockEntity(Pos);
            return;
        }
        if (_clientPlateWasOn && !PlateOn)
            _clientEmptySince = Api.World.ElapsedMilliseconds;
        _clientPlateWasOn = PlateOn;
        if (Api is not ICoreClientAPI capi || Rig is not { } rig || !Running)
        {
            _creakSeconds = 0.3f;
            return;
        }
        if ((_creakSeconds -= dt) <= 0)
        {
            _creakSeconds = 1.4f + (float)capi.World.Rand.NextDouble() * 0.6f;
            var at = WorldPoint(rig.Plate);
            capi.World.PlaySoundAt(CreakSounds[capi.World.Rand.Next(CreakSounds.Length)], at.X, at.Y, at.Z, null, true, 12, 0.5f);
        }
    }

    // ---- Breaking ----

    /// <summary>What breaking the frame gives besides the frame: every fitted part, and the plate if
    /// it is still flat (once folding has begun, it is lost with the brake).</summary>
    public IEnumerable<ItemStack> PartDrops()
    {
        foreach (var code in _parts.Returns())
            if (Api.World.GetItem(new AssetLocation(code)) is { } item)
                yield return new ItemStack(item);
        if (PlateOn && _job.Untouched)
            yield return _plate!.Clone();
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
        foreach (var stage in PressBrakeRequires.Stages)
            tree.RemoveAttribute(PartKeyPrefix + PressBrakeRequires.Name(stage));
        foreach (var (requires, code) in _parts.Snapshot())
            tree.SetString(PartKeyPrefix + requires, code);
        if (PlateOn)
            tree.SetItemstack("plate", _plate);
        else
            tree.RemoveAttribute("plate");
        tree.SetInt("class", _job.Class);
        tree.SetDouble("work", _job.Work);
        tree.SetBool("held", _held);
        if (Api?.Side == EnumAppSide.Server)
        {
            tree.SetFloat("leverTurnsLead", Config.LeverTurnsPerPlateLead);
            tree.SetFloat("leverTurnsCopper", Config.LeverTurnsPerPlateCopper);
        }
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);
        var fitted = new Dictionary<string, string>();
        foreach (var stage in PressBrakeRequires.Stages)
            if (tree.GetString(PartKeyPrefix + PressBrakeRequires.Name(stage)) is { Length: > 0 } code)
                fitted[PressBrakeRequires.Name(stage)] = code;
        _parts = PressBrakeParts.Restore(fitted);
        _plate = tree.GetItemstack("plate");
        if (_plate != null && !_plate.ResolveBlockOrItem(worldForResolving))
            _plate = null;
        _job = _plate == null ? FoldJob.None : FoldJob.Restore(tree.GetInt("class"), tree.GetDouble("work"));
        if (!_job.On)
            _plate = null;
        if (worldForResolving.Side == EnumAppSide.Client)
        {
            // who works the lever is the server's to say; a loaded world starts with no one at it
            _held = tree.GetBool("held");
            _serverTurnsLead = tree.TryGetFloat("leverTurnsLead");
            _serverTurnsCopper = tree.TryGetFloat("leverTurnsCopper");
        }
    }

    public override void OnStoreCollectibleMappings(Dictionary<int, AssetLocation> blockIdMapping, Dictionary<int, AssetLocation> itemIdMapping)
    {
        base.OnStoreCollectibleMappings(blockIdMapping, itemIdMapping);
        _plate?.Collectible.OnStoreCollectibleMappings(Api.World, new DummySlot(_plate), blockIdMapping, itemIdMapping);
    }

    public override void OnLoadCollectibleMappings(IWorldAccessor worldForResolve, Dictionary<int, AssetLocation> oldBlockIdMapping,
                                                   Dictionary<int, AssetLocation> oldItemIdMapping, int schematicSeed, bool resolveImports)
    {
        base.OnLoadCollectibleMappings(worldForResolve, oldBlockIdMapping, oldItemIdMapping, schematicSeed, resolveImports);
        _plate?.FixMapping(oldBlockIdMapping, oldItemIdMapping, worldForResolve);
    }

    // ---- Info ----

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
    {
        base.GetBlockInfo(forPlayer, dsc);
        string L(string key, params object[] args) => Lang.Get(PressBrakeSystem.Domain + ":pressbrake-" + key, args);
        if (_parts.Next is { } next)
        {
            dsc.AppendLine(L("info-next", StageName(next)));
            var after = PressBrakeRequires.Stages.Where(s => s > next && !_parts.Has(s)).ToList();
            if (after.Count > 0)
                dsc.AppendLine(L("info-then", string.Join(", ", after.Select(s => StageName(s)))));
        }
        if (PlateOn)
        {
            string metal = L("metal-" + Folding.MetalOf(_job.Class));
            int percent = (int)Math.Floor(_job.Work * 100 + 1e-9);
            dsc.AppendLine(Running ? L("info-folding", metal, percent) : L("info-plate", metal, percent));
        }
        else if (_parts.Complete)
            dsc.AppendLine(L("info-empty"));
    }
}
