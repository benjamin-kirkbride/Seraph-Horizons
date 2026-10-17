using System.Text;
using SeraphHorizons.Mod.Core;
using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.Riddle.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace SeraphHorizons.Mod.Riddle;

/// <summary>
/// The riddle's block entity (#714). Holds the fitted riddle (<see cref="RiddleParts"/>), the charge on
/// the mesh as its item stack and the riddling (<see cref="RiddleJob"/>: W, 0..1), the fines in the box
/// and the oversize left on the mesh, and who is riddling (<see cref="RiddleHolds"/>). The player works
/// it by holding right-click on it, as on the quern: the server advances W while anyone holds, and at
/// W = 1 the charge's fines are in the box, classified (<see cref="RiddleOre"/>), and its oversize on the
/// mesh as raw ore, back to spalling. Both stay until the player takes them out by hand: no automated
/// input or output. The client draws it (<see cref="RiddleRenderer"/>, through <see cref="IRiddleView"/>).
/// The rules are Riddle/Core's.
/// </summary>
public class BERiddle : BlockEntity, IRiddleView
{
    private static readonly AssetLocation LoadSound = new("game", "sounds/block/gravel");
    private static readonly AssetLocation ShakeSound = new("game", "sounds/player/panning");
    private static readonly AssetLocation FinesSound = new("game", "sounds/block/sand");
    private static readonly AssetLocation OversizeSound = new("game", "sounds/block/rock-hit-pickaxe");
    // The server syncs W at least this often (charges); the renderer follows the clock between.
    private const double SyncStep = 0.02;
    // A client's own player holding right-click counts as riddling for this long after its last step.
    private const long LocalHoldMs = 250;
    // The panning sound is heard every few shakes, not each.
    private const int ShakesPerSound = 3;

    private RiddleParts _parts = new();
    private ItemStack? _charge;
    private RiddleJob _job = RiddleJob.None;
    private ItemStack? _fines;
    private ItemStack? _oversize;
    private readonly RiddleHolds _holds = new();
    private bool _held;
    private long _localHeldAt = long.MinValue / 2;
    private float? _serverShakes;
    private Cuboidf[]? _boxes;
    private Cuboidf[]? _collision;
    private RiddleRenderer? _renderer;

    public Side Side { get; private set; } = Side.North;
    public RiddleParts Parts => _parts;
    public bool Complete => _parts.Complete;
    /// <summary>A charge is on the mesh being riddled (or riddled, at W 1, until it is delivered).</summary>
    public bool Riddling => _job.On && _charge != null;
    public ItemStack? Charge => _charge;
    public RiddleJob Job => _job;
    /// <summary>The fines in the box, classified, or null.</summary>
    public ItemStack? Fines => _fines;
    /// <summary>The oversize left on the mesh, raw ore, or null.</summary>
    public ItemStack? Oversize => _oversize;
    /// <summary>Someone holds right-click on a complete riddle with a charge on: the riddling runs.</summary>
    public bool Running => Core.Riddling.Running(_parts.Complete, Riddling, _job.Done, Held);

    // IRiddleView
    public bool RiddleFitted => _parts.Complete;
    public bool ChargeOn => Riddling || _oversize != null;
    public double RiddleWork => Riddling ? _job.Work : 1;
    public bool BoxHoldsFines => Riddling || _fines != null;
    public double ShakesPerCharge => _serverShakes ?? Config.ShakesPerCharge;
    public bool Held =>
        _held || Api is { Side: EnumAppSide.Client } && Api.World.ElapsedMilliseconds - _localHeldAt <= LocalHoldMs;

    /// <summary>The fines' texture (<c>ore</c>): the crushed ore's own (its ore's nugget), the charge's, else the box's.</summary>
    public AssetLocation? OreTexture => ((_charge ?? _fines)?.Collectible as Item)?.FirstTexture?.Base;

    /// <summary>The oversize's texture (<c>rock</c>): its host rock's, the oversize's, else the rock the station
    /// would give it; null for the block's default (granite).</summary>
    public AssetLocation? RockTexture =>
        (_oversize?.Collectible.Variant["rock"] ?? RiddleOre.RockUnder(Api?.World, Pos)) is { } rock
            ? new AssetLocation("game", $"block/stone/rock/{rock}1")
            : null;

    private RiddleSystem System => RiddleSystem.Of(Api);
    private RiddleConfig Config => System.Config;
    private RiddleRig? Rig => System.Rig;

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        Side = Sides.TryParse(Block.Variant["side"], out var side) ? side : Side.North;
        BuildBoxes();
        if (api.Side == EnumAppSide.Server)
            RegisterGameTickListener(OnTick, 50);
        else
        {
            RegisterGameTickListener(OnClientTick, 100);
            if (api is ICoreClientAPI capi && Rig is { } rig && Block is BlockRiddle)
                _renderer = new RiddleRenderer(capi, this, this, rig);
        }
    }

    /// <summary>A native-frame point as a world position.</summary>
    public Vec3d WorldPoint(Float3 native) => MachinePoints.WorldPoint(Pos, native, Side);

    public Cuboidf[]? CellBoxes => _boxes;

    /// <summary>The cell's selection boxes and the rig's collision-only lid.</summary>
    public Cuboidf[]? CollisionBoxes => _collision;

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
        var cell = rig.Cells[0];
        var boxes = cell.Boxes.Count == 0 ? [Cuboidf.Default()] : cell.Boxes.Select(World).ToArray();
        _boxes = boxes;
        _collision = cell.LidBox is { } lid ? [.. boxes, World(lid)] : boxes;
    }

    // ---- Interaction ----

    /// <summary>
    /// Right-click on the riddle. Ctrl: in creative mode on a frame with no riddle it fits the riddle with
    /// nothing taken; with a charge on that nothing has been done to, the charge comes back. The riddle never
    /// comes out: only breaking the station returns it. Nails and strips in hand fit the riddle (two). Crushed
    /// ore in hand, with the riddle empty and the box too, goes on as a charge (a whole charge, of one kind), else is
    /// refused saying why. An empty hand takes out the oversize first, then the fines; with a charge on, the
    /// click starts riddling, held as on the quern (<see cref="OnWorkStep"/>). Shift lets a held block be
    /// placed against it instead. Decided and done on the server; the client says whether the click is the
    /// riddle's.
    /// </summary>
    public bool OnInteract(IPlayer byPlayer)
    {
        var slot = byPlayer.InventoryManager.ActiveHotbarSlot;
        var controls = byPlayer.Entity.Controls;
        var held = slot?.Itemstack;
        string? code = held?.Collectible?.Code?.ToString();
        bool server = Api.Side == EnumAppSide.Server;
        bool ctrl = controls.CtrlKey && !controls.ShiftKey;
        if (ctrl && (Riddling || CreativeShortcutApplies(byPlayer)))
        {
            if (!server)
                return true;
            if (CreativeShortcutApplies(byPlayer))
                FitRiddle(new DummySlot(NewStack(RiddleParts.MeshCodes[0], RiddleParts.Needed)), byPlayer, free: true);
            else
                TakeCharge(byPlayer);
            return true;
        }
        if (controls.ShiftKey)
            return false;
        if (RiddleParts.IsPart(code))
        {
            if (server)
                FitRiddle(slot!, byPlayer);
            return true;
        }
        // crushed ore in hand goes on as a charge (or is refused, saying why), unless a charge is being riddled
        if (Core.Riddling.IsCrushedOre(code) && !Riddling)
            return !server || TryLoad(slot!, byPlayer);
        // what it holds comes out to an empty hand
        if (held == null && _oversize != null)
            return !server || TakeOversize(byPlayer);
        if (held == null && !Riddling && _fines != null)
            return !server || TakeFines(byPlayer);
        if (!_parts.Complete)
            return false;
        if (!server)
            return true;
        if (!Riddling)
            return Error(byPlayer, _oversize != null || _fines != null ? "error-empty-hand" : "error-no-charge");
        return StartWork(byPlayer);
    }

    private bool StartWork(IPlayer byPlayer)
    {
        _holds.Hold(byPlayer.PlayerUID, Api.World.ElapsedMilliseconds);
        UpdateHeld();
        return true;
    }

    /// <summary>
    /// Right-click still held on the riddle (both sides, every tick, as the quern's grinding): the server
    /// counts the player as riddling; the client marks its own player as riddling for the renderer.
    /// Whether the player goes on: while a charge is on and not yet riddled.
    /// </summary>
    public bool OnWorkStep(IPlayer byPlayer, float secondsUsed)
    {
        long now = Api.World.ElapsedMilliseconds;
        bool going = _parts.Complete && Riddling && !_job.Done;
        if (Api.Side == EnumAppSide.Client)
        {
            _localHeldAt = going ? now : long.MinValue / 2;
            return going || secondsUsed < 0.5f && !ChargeOn;
        }
        if (!going)
        {
            Release(byPlayer);
            return false;
        }
        _holds.Hold(byPlayer.PlayerUID, now);
        UpdateHeld();
        return true;
    }

    /// <summary>The player let go.</summary>
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

    // The server's "someone is riddling", synced when it changes.
    private void UpdateHeld()
    {
        bool held = _holds.Any(Api.World.ElapsedMilliseconds) && _parts.Complete && Riddling && !_job.Done;
        if (held == _held)
            return;
        _held = held;
        MarkDirty(false);
    }

    /// <summary>Whether a click is the creative shortcut's: a player in creative mode, Ctrl without Shift,
    /// on a frame with no riddle.</summary>
    public bool CreativeShortcutApplies(IPlayer byPlayer)
    {
        var controls = byPlayer.Entity.Controls;
        return !_parts.Complete
               && CreativeUpgrades.Applies(byPlayer.WorldData?.CurrentGameMode == EnumGameMode.Creative, controls.CtrlKey, controls.ShiftKey);
    }

    private ItemStack? NewStack(string code, int count) =>
        Api.World.GetItem(new AssetLocation(code)) is { IsMissing: false } item ? new ItemStack(item, count) : null;

    /// <summary>Fits the riddle from <paramref name="slot"/> (server side): its nails and strips, two of
    /// them, taken unless the player is in creative mode or it is <paramref name="free"/>; false, with an
    /// error to the player, when the rules say no.</summary>
    public bool FitRiddle(ItemSlot slot, IPlayer? byPlayer, bool free = false)
    {
        string? code = slot.Itemstack?.Collectible?.Code?.ToString();
        if (code == null)
            return false;
        switch (_parts.CanFit(code, slot.StackSize))
        {
            case RiddleFitVerdict.Fits:
                break;
            case RiddleFitVerdict.AlreadyFitted:
                return Error(byPlayer, "error-part-fitted");
            case RiddleFitVerdict.TooFew:
                return Error(byPlayer, "error-too-few-parts", RiddleParts.Needed);
            default:
                return Error(byPlayer, "error-not-a-part");
        }
        _parts.Fit(code);
        if (!free && byPlayer?.WorldData.CurrentGameMode != EnumGameMode.Creative)
        {
            slot.TakeOut(RiddleParts.Needed);
            slot.MarkDirty();
        }
        Api.World.PlaySoundAt(Block.Sounds.Place, Pos, -0.25, byPlayer);
        MarkDirty(true);
        return true;
    }

    /// <summary>Puts a charge from <paramref name="slot"/> on the riddle (server side), if it takes one now:
    /// <see cref="RiddleConfig.LumpsPerCharge"/> crushed ore of the held kind.</summary>
    public bool TryLoad(ItemSlot slot, IPlayer? byPlayer)
    {
        var stack = slot.Itemstack;
        string? code = stack?.Collectible?.Code?.ToString();
        int lumps = Config.LumpsPerCharge;
        switch (Core.Riddling.CanLoad(code, stack != null && RiddleOre.IsClassified(stack), slot.StackSize, lumps,
                                      _parts.Complete, ChargeOn, _fines != null))
        {
            case RiddleLoadVerdict.Loads:
                break;
            case RiddleLoadVerdict.AlreadyClassified:
                return Error(byPlayer, "error-classified");
            case RiddleLoadVerdict.Incomplete:
                return Error(byPlayer, "error-incomplete");
            case RiddleLoadVerdict.Occupied:
                return Error(byPlayer, "error-occupied");
            case RiddleLoadVerdict.BoxFull:
                return Error(byPlayer, "error-box-full");
            case RiddleLoadVerdict.TooFew:
                return Error(byPlayer, "error-too-few", lumps);
            default:
                return false;
        }
        Load(slot.TakeOut(lumps));
        slot.MarkDirty();
        return true;
    }

    private void Load(ItemStack charge)
    {
        _charge = charge;
        _job = RiddleJob.Start;
        var at = WorldPoint(Rig?.Charge ?? new Float3(0.5f, 0.45f, 0.5f));
        Api.World.PlaySoundAt(LoadSound, at.X, at.Y, at.Z);
        MarkDirty(true);
    }

    /// <summary>Ctrl + right-click (server side): a charge nothing has been done to comes back; one being
    /// riddled stays.</summary>
    public bool TakeCharge(IPlayer byPlayer)
    {
        if (!Riddling)
            return false;
        if (!_job.Untouched)
            return Error(byPlayer, "error-busy");
        var charge = _charge!;
        _charge = null;
        _job = RiddleJob.None;
        Give(byPlayer, charge);
        Api.World.PlaySoundAt(LoadSound, Pos, 0, byPlayer);
        MarkDirty(true);
        return true;
    }

    /// <summary>Takes the oversize off the mesh (server side), to the player.</summary>
    public bool TakeOversize(IPlayer byPlayer)
    {
        if (_oversize is not { } oversize)
            return false;
        _oversize = null;
        Give(byPlayer, oversize);
        var at = WorldPoint(Rig?.Charge ?? new Float3(0.5f, 0.45f, 0.5f));
        Api.World.PlaySoundAt(OversizeSound, at.X, at.Y, at.Z, null, true, 16, 0.5f);
        MarkDirty(true);
        return true;
    }

    /// <summary>Takes the fines out of the box (server side), to the player.</summary>
    public bool TakeFines(IPlayer byPlayer)
    {
        if (_fines is not { } fines || Riddling)
            return false;
        _fines = null;
        Give(byPlayer, fines);
        var at = WorldPoint(Rig?.Fines ?? new Float3(0.5f, 0.15f, 0.5f));
        Api.World.PlaySoundAt(FinesSound, at.X, at.Y, at.Z);
        MarkDirty(true);
        return true;
    }

    private void Give(IPlayer byPlayer, ItemStack stack)
    {
        if (!byPlayer.InventoryManager.TryGiveItemstack(stack, true))
            Api.World.SpawnItemEntity(stack, Pos.ToVec3d().Add(0.5, 0.8, 0.5));
    }

    private bool Error(IPlayer? byPlayer, string key, params object[] args)
    {
        if (byPlayer is IServerPlayer sp)
            sp.SendIngameError("riddle-" + key, Lang.GetL(sp.LanguageCode, RiddleSystem.Domain + ":riddle-" + key, args));
        return false;
    }

    // ---- The riddling ----

    private void OnTick(float dt)
    {
        UpdateHeld();
        if (_held)
            Shake(Core.Riddling.ShakeRadiansPerSecond * dt);
    }

    /// <summary>
    /// Riddles by <paramref name="radians"/> of the clock (server side): W advances by their shakes over the
    /// charge's, and at W = 1 the charge is split (<see cref="Core.Riddling.Split"/>): its fines into the box,
    /// classified, its oversize onto the mesh as raw ore. Returns whether this step finished the charge.
    /// </summary>
    public bool Shake(double radians)
    {
        if (!Riddling || !_parts.Complete)
            return false;
        var before = _job;
        double shakes = Config.ShakesPerCharge;
        (_job, bool finished) = _job.Advance(radians, shakes);
        if (RiddleJob.ShakesCrossed(before.Work, _job.Work, shakes) > 0
            && (int)Math.Floor(_job.Work * shakes + 1e-9) % ShakesPerSound == 1)
        {
            var at = WorldPoint(Rig?.Charge ?? new Float3(0.5f, 0.45f, 0.5f));
            Api.World.PlaySoundAt(ShakeSound, at.X, at.Y, at.Z, null, true, 16, 0.7f);
        }
        if (finished)
        {
            Deliver();
            UpdateHeld();
            MarkDirty(true);
            return true;
        }
        if (Math.Floor(before.Work / SyncStep) != Math.Floor(_job.Work / SyncStep))
            MarkDirty(false);
        return false;
    }

    // The charge riddled: its fines into the box (classified), its oversize onto the mesh (raw ore).
    private void Deliver()
    {
        var charge = _charge!;
        var (fines, oversize) = Core.Riddling.Split(charge.StackSize, Config.OversizePerCharge);
        _charge = null;
        _job = RiddleJob.None;
        var rock = RiddleOre.RockUnder(Api.World, Pos);
        var oversizeStack = oversize > 0 ? RiddleOre.Oversize(Api.World, charge, oversize, rock) : null;
        if (oversize > 0 && oversizeStack == null)
        {
            // no raw ore of it in this world: the oversize stays the crushed ore, unclassified, rather than lost
            oversizeStack = charge.Clone();
            oversizeStack.StackSize = oversize;
        }
        if (fines > 0)
        {
            var through = charge.Clone();
            through.StackSize = fines;
            RiddleOre.MarkClassified(through);
            _fines = through;
        }
        _oversize = oversizeStack;
        Api.World.Logger.Audit("Riddle at {0} riddled {1}x{2}: {3} fines, {4} oversize ({5}).", Pos, charge.StackSize,
            charge.Collectible.Code, fines, oversize, oversizeStack?.Collectible.Code);
    }

    // The client's own: the client removes a broken riddle at once, and an update the server sent before it
    // heard of the break then brings the block entity back over air: drop it.
    private void OnClientTick(float dt)
    {
        if (Api.World.BlockAccessor.GetBlock(Pos) is not BlockRiddle)
        {
            DisposeRenderer();
            Api.World.BlockAccessor.RemoveBlockEntity(Pos);
        }
    }

    // ---- Breaking ----

    /// <summary>What breaking the station gives besides the frame: the riddle's nails and strips, the
    /// charge (whole: nothing is lost by breaking), the oversize and the fines.</summary>
    public IEnumerable<ItemStack> PartDrops()
    {
        if (_parts.Returns() is { } r && NewStack(r.Code, r.Count) is { } mesh)
            yield return mesh;
        if (_charge != null)
            yield return _charge.Clone();
        if (_oversize != null)
            yield return _oversize.Clone();
        if (_fines != null)
            yield return _fines.Clone();
    }

    public override void OnBlockRemoved()
    {
        base.OnBlockRemoved();
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
        if (_parts.Fitted is { } mesh)
            tree.SetString("riddle", mesh);
        else
            tree.RemoveAttribute("riddle");
        SetStack(tree, "charge", Riddling ? _charge : null);
        SetStack(tree, "fines", _fines);
        SetStack(tree, "oversize", _oversize);
        tree.SetDouble("work", _job.Work);
        tree.SetBool("held", _held);
        if (Api?.Side == EnumAppSide.Server)
            tree.SetFloat("shakes", Config.ShakesPerCharge);
    }

    private static void SetStack(ITreeAttribute tree, string key, ItemStack? stack)
    {
        if (stack != null)
            tree.SetItemstack(key, stack);
        else
            tree.RemoveAttribute(key);
    }

    private static ItemStack? GetStack(ITreeAttribute tree, string key, IWorldAccessor world)
    {
        var stack = tree.GetItemstack(key);
        return stack != null && stack.ResolveBlockOrItem(world) ? stack : null;
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);
        _parts = RiddleParts.Restore(tree.GetString("riddle"));
        _charge = GetStack(tree, "charge", worldForResolving);
        _job = RiddleJob.Restore(_charge != null, tree.GetDouble("work"));
        _fines = GetStack(tree, "fines", worldForResolving);
        _oversize = GetStack(tree, "oversize", worldForResolving);
        if (worldForResolving.Side == EnumAppSide.Client)
        {
            // who is riddling is the server's to say; a loaded world starts with no one at it
            _held = tree.GetBool("held");
            _serverShakes = tree.TryGetFloat("shakes");
        }
    }

    public override void OnStoreCollectibleMappings(Dictionary<int, AssetLocation> blockIdMapping, Dictionary<int, AssetLocation> itemIdMapping)
    {
        base.OnStoreCollectibleMappings(blockIdMapping, itemIdMapping);
        foreach (var stack in new[] { _charge, _fines, _oversize })
            stack?.Collectible.OnStoreCollectibleMappings(Api.World, new DummySlot(stack), blockIdMapping, itemIdMapping);
    }

    public override void OnLoadCollectibleMappings(IWorldAccessor worldForResolve, Dictionary<int, AssetLocation> oldBlockIdMapping,
                                                   Dictionary<int, AssetLocation> oldItemIdMapping, int schematicSeed, bool resolveImports)
    {
        base.OnLoadCollectibleMappings(worldForResolve, oldBlockIdMapping, oldItemIdMapping, schematicSeed, resolveImports);
        foreach (var stack in new[] { _charge, _fines, _oversize })
            stack?.FixMapping(oldBlockIdMapping, oldItemIdMapping, worldForResolve);
    }

    // ---- Info ----

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
    {
        base.GetBlockInfo(forPlayer, dsc);
        string L(string key, params object[] args) => Lang.Get(RiddleSystem.Domain + ":riddle-" + key, args);
        if (!_parts.Complete)
        {
            dsc.AppendLine(L("info-next"));
            return;
        }
        if (Riddling)
        {
            string name = _charge!.GetName();
            int percent = (int)Math.Floor(_job.Work * 100 + 1e-9);
            dsc.AppendLine(Running ? L("info-riddling", name, percent) : L("info-charge", name, percent));
        }
        if (_oversize != null)
            dsc.AppendLine(L("info-oversize", _oversize.StackSize, _oversize.GetName()));
        if (_fines != null)
            dsc.AppendLine(L("info-fines", _fines.StackSize, _fines.GetName()));
        if (!ChargeOn && _fines == null)
            dsc.AppendLine(L("info-empty", Config.LumpsPerCharge));
    }
}
