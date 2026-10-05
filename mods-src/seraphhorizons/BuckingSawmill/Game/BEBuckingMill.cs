using System.Text;
using SeraphHorizons.Mod.BuckingSawmill.Core;
using SeraphHorizons.Mod.Core;
using SeraphHorizons.Mod.Woodworking;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.BuckingSawmill;

/// <summary>
/// The mill's controller. Holds the fitted parts (Immersive Woodworking's sawmill items), the
/// blade kit as its own stack (it wears), the loaded trunk as its whole Logging Expanded item
/// stack, the cut's progress and the saws' depth. The server cuts and winds the saws back up from
/// the power ghost's shaft angle, pulls trunks from a rack at the infeed side, and keeps the ghost
/// cells stamped; the client shows it. Every cell's collision and selection boxes, with the
/// loaded trunk's (<see cref="TrunkBox"/>), come from here.
/// </summary>
public class BEBuckingMill : BlockEntity, IMillVisualState
{
    public const string IwDomain = "immersivewoodworking";
    private static readonly AssetLocation BreakSound = new("game", "sounds/effect/toolbreak");

    private Parts _parts = new();
    // The fitted kit's stack; _parts.BladeMetal mirrors its metal.
    private ItemStack? _blade;
    private ItemStack? _trunk;
    // How the loaded trunk is shown, which picks the cells' boxes: read by collision lookups,
    // which can run off the main thread.
    private volatile TrunkClass _trunkClass;
    private float _progress;
    private float _clientProgress;
    // 0 at the top, 1 at the bed (Core/SawDepth.cs), and whether the saws are being wound up. The
    // server's are the ones that count.
    private float _depth;
    private bool _rising;
    // The client's estimate of _depth and _rising, advanced between syncs, the eased depth it shows,
    // and whether it has seen the cut finish before the server's sync clears the trunk.
    private float _clientDepthEstimate;
    private float _clientDepth;
    // The shown depth one client tick back, and when and how long that tick was: frames between
    // ticks draw the saws part way from one to the other.
    private float _clientDepthBefore;
    private long _clientDepthMs;
    private float _clientDepthSeconds;
    private bool _clientRising;
    private bool _clientCutDone;
    // The loaded trunk's stored logs, read when it was loaded (synced, so the client's progress
    // advances by the server's arithmetic).
    private int _storedLogs;
    private float _lastAngle;
    private bool _angleSeeded;
    private float _clientLastAngle;
    private bool _clientAngleSeeded;
    private float? _serverMinSpeed;
    private float? _serverRevolutions;
    private float? _serverRaiseRevolutions;
    private float? _serverBladeSpeed;
    // What the rack at the infeed end offers, as the server last found it (synced for the info).
    private RackState _rackState;
    // Players holding right-click on the mill (server side), by player UID: waiting to load a trunk
    // at the next top of the cycle, or winding the stopped saws up by hand. The server hears a
    // hold's start and its end (stop or cancel) from the client; the mill's own tick does the rest.
    private readonly Dictionary<string, Hold> _holds = [];
    private sealed record Hold(IPlayer Player, HoldKind Kind, long Since);
    private enum HoldKind { Load, Wind }
    // Every cell's boxes by trunk class (TrunkClass as the index), turned to the facing. Built
    // whole, then published; never changed after.
    private volatile IReadOnlyDictionary<Int3, Cuboidf[]>[]? _boxes;
    private MillRenderer? _renderer;

    public Side Side { get; private set; } = Side.North;
    public BlockFacing Facing => BlockFacing.FromCode(Side.Code());
    public bool Complete => _parts.Complete;
    public float Progress => _progress;
    /// <summary>The saws' depth, 0 at the top to 1 at the bed (server's value on the client).</summary>
    public float Depth => _depth;
    /// <summary>Whether the saws are being wound up (server's value on the client).</summary>
    public bool Rising => _rising;
    /// <summary>Complete and turning at <see cref="MinSpeed"/> or faster: the cycle runs.</summary>
    public bool Running => _parts.Complete && ShaftSpeed >= MinSpeed;
    /// <summary>The fitted blade kit's stack, or null.</summary>
    public ItemStack? BladeKit => _blade;
    /// <summary>What the rack at the infeed end offers, as the server last found it.</summary>
    public RackState RackState => _rackState;
    /// <summary>The configured slowest shaft speed that cuts. A client uses the server's value,
    /// which comes with the block entity's data, not its own config file's.</summary>
    public float MinSpeed => _serverMinSpeed ?? Config.MinSpeed;
    private float RevolutionsPerStoredLog => _serverRevolutions ?? Config.RevolutionsPerStoredLog;
    private float RaiseRevolutions => _serverRaiseRevolutions ?? Config.RaiseRevolutions;
    /// <summary>How fast the fitted blade kit cuts, a multiple of a copper kit's speed, from its tool
    /// tier (<see cref="BuckingSawmillSystem.BladeSpeed"/>); 1 without a kit. A client uses the
    /// server's figure.</summary>
    public float BladeSpeed => _serverBladeSpeed ?? (_blade == null || Api == null ? 1f : System.BladeSpeed(_blade));

    private BuckingSawmillSystem System => BuckingSawmillSystem.Of(Api);
    private MillConfig Config => System.Config;
    private Rig? Rig => System.Rig;

    // IMillVisualState
    public int SashCount => _parts.Sashes;
    public bool HasCrankshaft => _parts.Crankshaft;
    public bool HasLevers => _parts.Levers;
    public bool HasBladeKit => _blade != null;
    public string? BladeMetal => _parts.BladeMetal;
    public ItemStack? Trunk => _trunk;
    public float ClientProgress => _trunk == null ? 0 : Math.Clamp(_clientProgress, 0, 1);
    public MillPhase Phase => SawDepth.Phase(Running, _trunk != null, _rising);
    public float ClientSawDepth
    {
        get
        {
            // The depth moves at the client's ticks (20 a second); read every frame, it would step.
            float t = _clientDepthSeconds > 0 && Api != null
                ? Math.Clamp((Api.World.ElapsedMilliseconds - _clientDepthMs) / (_clientDepthSeconds * 1000), 0, 1)
                : 1;
            return Math.Clamp(_clientDepthBefore + (_clientDepth - _clientDepthBefore) * t, 0, 1);
        }
    }
    /// <summary>The saws' depth as this side knows it: the client's shown depth, the server's own.</summary>
    public float SideDepth => Api?.Side == EnumAppSide.Client ? ClientSawDepth : _depth;
    public bool ClientRising => _clientRising;
    public float ShaftAngle => Power?.AngleRad ?? 0;
    public float ShaftSpeed => Power?.TrueSpeed ?? 0;

    /// <summary>The power ghost's mechanical power behavior, looked up afresh each time.</summary>
    public BEBehaviorMillMP? Power =>
        Api != null && Rig is { } rig
            ? Api.World.BlockAccessor.GetBlockEntity(CellPos(rig.PowerCell))?.GetBehavior<BEBehaviorMillMP>()
            : null;

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        Side = Sides.TryParse(Block.Variant["side"], out var side) ? side : Side.North;
        _boxes = BuildBoxes();
        if (api.Side == EnumAppSide.Server)
        {
            RegisterDelayedCallback(_ =>
            {
                if (Api?.World.BlockAccessor.GetBlockEntity(Pos) == this)
                    EnsureGhosts();
            }, 100);
            RegisterGameTickListener(OnCutTick, 50);
            RegisterGameTickListener(OnRackTick, 1000);
        }
        else
        {
            _clientProgress = _progress;
            _clientDepthEstimate = _clientDepth = _clientDepthBefore = _depth;
            _clientRising = _rising;
            RegisterGameTickListener(OnClientTick, 50);
            if (api is ICoreClientAPI capi && Rig is { } rig && Block is BlockBuckingMill)
                _renderer = new MillRenderer(capi, this, rig);
        }
    }

    // ---- Footprint ----

    public BlockPos CellPos(Int3 local)
    {
        var w = Footprint.ToWorld(local, Side);
        return Pos.AddCopy(w.X, w.Y, w.Z);
    }

    /// <summary>Every ghost cell's world position, with whether it is the power cell.</summary>
    public IEnumerable<(BlockPos Pos, bool Power)> GhostCells() =>
        Rig is { } rig ? rig.GhostCells.Select(c => (CellPos(c.Pos), c.Pos == rig.PowerCell)) : [];

    /// <summary>A cell's collision and selection boxes, turned to the mill's facing, with the
    /// loaded trunk's part in that cell; null when the cell is not the mill's. Safe off the main
    /// thread: the tables are built whole in <see cref="Initialize"/> and only read here.</summary>
    public Cuboidf[]? CellBoxes(BlockPos cellPos)
    {
        var tables = _boxes ??= BuildBoxes();
        if (tables == null)
            return null;
        var local = Footprint.ToLocal(new Int3(cellPos.X - Pos.X, cellPos.Y - Pos.Y, cellPos.Z - Pos.Z), Side);
        return tables[(int)_trunkClass].GetValueOrDefault(local);
    }

    /// <summary>Every cell's boxes, once per trunk class; null before the rig is known.</summary>
    private IReadOnlyDictionary<Int3, Cuboidf[]>[]? BuildBoxes()
    {
        if (Api == null || Rig is not { } rig || !Sides.TryParse(Block?.Variant["side"], out var side))
            return null;
        Cuboidf World(Box b)
        {
            var w = Footprint.ToWorld(b, side);
            return new Cuboidf(w.X1, w.Y1, w.Z1, w.X2, w.Y2, w.Z2);
        }
        var tables = new IReadOnlyDictionary<Int3, Cuboidf[]>[3];
        foreach (var trunk in new[] { TrunkClass.None, TrunkClass.Thin, TrunkClass.Thick })
        {
            var trunkBoxes = trunk == TrunkClass.None || rig.TrunkBed is not { } bed
                ? new Dictionary<Int3, Box>()
                : TrunkBox.CellBoxes(rig.Cells.Select(c => c.Pos), TrunkBox.Bounds(bed, trunk));
            tables[(int)trunk] = rig.Cells.ToDictionary(c => c.Pos, c =>
            {
                var own = c.Boxes.Count == 0 ? [Cuboidf.Default()] : c.Boxes.Select(World);
                return (trunkBoxes.TryGetValue(c.Pos, out var t) ? own.Append(World(t)) : own).ToArray();
            });
        }
        return tables;
    }

    /// <summary>Whether a click on cell <paramref name="cellPos"/> at <paramref name="hit"/> (the
    /// selection's hit point, relative to that cell) is on the loaded trunk.</summary>
    public bool HitsTrunk(BlockPos cellPos, Vec3d? hit)
    {
        if (hit == null || _trunkClass == TrunkClass.None || Rig?.TrunkBed is not { } bed)
            return false;
        var local = Footprint.ToLocal(new Float3(
            (float)(cellPos.X - Pos.X + hit.X), (float)(cellPos.Y - Pos.Y + hit.Y), (float)(cellPos.Z - Pos.Z + hit.Z)), Side);
        return TrunkBox.Contains(TrunkBox.Bounds(bed, _trunkClass), local);
    }

    private Block? GhostBlock(bool power) =>
        Api.World.GetBlock(new AssetLocation(BuckingSawmillSystem.Domain, power ? "buckingmill-ghostpower-" + Side.Code() : "buckingmill-ghost"));

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
        if (Api.World.BlockAccessor.GetBlockEntity(pos) is BEMillGhost be)
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
                if (ba.GetBlockEntity(pos) is BEMillGhost be && !Pos.Equals(be.Principal))
                {
                    be.Principal = Pos.Copy();
                    be.MarkDirty(true);
                }
            }
            else if (current.Id == 0 || current is BlockMillGhost)
                StampGhost(expected, pos);
        }
    }

    public void RemoveGhosts()
    {
        if (Api.Side != EnumAppSide.Server)
            return;
        var ba = Api.World.BlockAccessor;
        foreach (var (pos, _) in GhostCells())
            if (ba.GetBlock(pos) is BlockMillGhost && ba.GetBlockEntity(pos) is BEMillGhost be && Pos.Equals(be.Principal))
                ba.SetBlock(0, pos);
    }

    // ---- Interaction ----

    /// <summary>Right-click on the mill or any ghost (<paramref name="onTrunk"/>: on the loaded
    /// trunk). Ctrl takes the trunk back, else the blade kit; a part in hand is fitted; a trunk in
    /// hand, or with an empty hand one from the hotbar or backpack, is loaded. Anything else held is
    /// the item's own business, except on the trunk, where the click is the mill's and does nothing
    /// (a block would be placed inside the trunk). In creative mode, Ctrl on an unassembled mill fits
    /// its next part instead (<see cref="CreativeShortcut"/>). Decided and done on the server; the
    /// client only says whether the click is the mill's.</summary>
    public bool OnInteract(IPlayer byPlayer, bool onTrunk = false)
    {
        var slot = byPlayer.InventoryManager.ActiveHotbarSlot;
        var controls = byPlayer.Entity.Controls;
        bool take = controls.CtrlKey && !controls.ShiftKey;
        var path = slot?.Itemstack?.Collectible?.Code?.Path;
        bool part = slot?.Itemstack?.Collectible?.Code?.Domain == IwDomain && Parts.KindOf(path, out _) != PartKind.None;
        bool trunk = Trunks.IsTrunk(slot?.Itemstack);
        if (!take && !part && !trunk && slot?.Itemstack != null)
            return onTrunk;
        if (Api.Side != EnumAppSide.Server)
            return true;

        if (CreativeShortcutApplies(byPlayer))
            FitNextPart(byPlayer);
        else if (take)
        {
            if (!TryTakeTrunk(byPlayer))
                TryTakeBladeKit(byPlayer);
        }
        else if (part)
            TryFitPart(slot!, byPlayer);
        else if (!trunk && Feeding.WindsUp(Running, _depth))
            StartHold(byPlayer, HoldKind.Wind);
        else if (trunk ? !TryLoadFromSlot(slot!, byPlayer) : !TryLoadFromInventory(byPlayer))
        {
            // Not now: if only because the saws are not up yet, it goes on at the next top while
            // the button is held.
            if (WaitsForTop(byPlayer))
                StartHold(byPlayer, HoldKind.Load);
        }
        return true;
    }

    /// <summary>Whether a trunk refused only because the saws are not at the top can wait for them:
    /// the mill is running, its bed is empty and the player has a trunk for it.</summary>
    private bool WaitsForTop(IPlayer byPlayer) =>
        Running && _trunk == null && !SawDepth.AtTop(_depth) && System.Logging != null && FindTrunk(byPlayer) != null;

    private void StartHold(IPlayer byPlayer, HoldKind kind) =>
        _holds[byPlayer.PlayerUID] = new Hold(byPlayer, kind, Api.World.ElapsedMilliseconds);

    /// <summary>Right-click held on the mill (both sides): whether the hold goes on. The client keeps
    /// it while there is something to wait for, a trunk to load or saws to wind up; the server, while
    /// it has the hold.</summary>
    public bool OnInteractStep(IPlayer byPlayer)
    {
        if (Api.Side == EnumAppSide.Server)
            return _holds.ContainsKey(byPlayer.PlayerUID);
        var held = byPlayer.InventoryManager.ActiveHotbarSlot?.Itemstack;
        if (held == null && Feeding.KeepsWinding(Running, SideDepth))
            return true;
        return _trunk == null && Running && (held == null || Trunks.IsTrunk(held));
    }

    /// <summary>Right-click let go, or the hold cancelled (both sides): the server forgets it.</summary>
    public void OnInteractEnd(IPlayer byPlayer)
    {
        if (Api.Side == EnumAppSide.Server)
            _holds.Remove(byPlayer.PlayerUID);
    }

    /// <summary>Runs the holds each tick (server side): winds stopped saws up by hand, and loads a
    /// waiting player's trunk when the saws are at, or pass, the top (<paramref name="passedTop"/>).
    /// Returns whether a trunk went on.</summary>
    private bool RunHolds(float dt, bool passedTop)
    {
        if (_holds.Count == 0)
            return false;
        long now = Api.World.ElapsedMilliseconds;
        foreach (var (uid, hold) in _holds.ToList())
            if (hold.Player.Entity is not { Alive: true } || now - hold.Since > Feeding.HoldLapseSeconds * 1000)
                _holds.Remove(uid);
        foreach (var hold in _holds.Values.Where(h => h.Kind == HoldKind.Wind).ToList())
        {
            if (!Feeding.KeepsWinding(Running, _depth))
            {
                _holds.Remove(hold.Player.PlayerUID);
                continue;
            }
            WindUp(dt);
            if (_depth <= 0)
                _holds.Remove(hold.Player.PlayerUID);
            break;   // one winder is enough
        }
        if (_trunk != null || !Feeding.CanTakeTrunk(_depth, passedTop))
            return false;
        foreach (var hold in _holds.Values.Where(h => h.Kind == HoldKind.Load).ToList())
        {
            if (FindTrunk(hold.Player) is not { } slot || !TryLoadFromSlot(slot, null, passedTop))
                continue;
            _holds.Remove(hold.Player.PlayerUID);
            return true;
        }
        return false;
    }

    /// <summary>Winds the stopped saws up by hand for <paramref name="dt"/> seconds (server side):
    /// the whole travel in <see cref="Feeding.HandWindSeconds"/>. They are being raised, so a mill
    /// that starts turning again finishes the rise and comes down onto its trunk where the cut is.</summary>
    public void WindUp(float dt)
    {
        float before = _depth;
        _depth = Feeding.Wind(_depth, dt);
        _rising = _depth > 0;
        if (_depth <= 0 || (int)(before * 20) != (int)(_depth * 20))
            MarkDirty();
    }

    /// <summary>Whether the creative shortcut is on: with <c>UnifiedWoodworking</c>, whose creative
    /// shortcut on the woodworking stations this is (<see cref="CreativeUpgrades"/>), running.</summary>
    public bool CreativeShortcut => Api?.ModLoader.GetModSystem<SeraphHorizonsSystem>()?.Woodworking is { Active: true };

    /// <summary>Whether a click is the creative shortcut's: a player in creative mode, Ctrl without
    /// Shift (<see cref="CreativeUpgrades.Applies"/>), on a mill with a part still to fit. On an
    /// assembled mill Ctrl takes the trunk or the blade kit back, as it does in survival.</summary>
    public bool CreativeShortcutApplies(IPlayer byPlayer)
    {
        var controls = byPlayer.Entity.Controls;
        return !_parts.Complete && CreativeShortcut
               && CreativeUpgrades.Applies(byPlayer.WorldData?.CurrentGameMode == EnumGameMode.Creative, controls.CtrlKey, controls.ShiftKey);
    }

    /// <summary>The creative shortcut (server side): fits the next missing part (sashes, crankshaft,
    /// levers, then a blade kit of <see cref="AssembledMachines.DefaultMetal"/>), with nothing taken
    /// from the player, whatever they hold.</summary>
    public bool FitNextPart(IPlayer byPlayer)
    {
        if (_parts.NextPart(AssembledMachines.DefaultMetal) is not { } path)
            return false;
        var item = Api.World.GetItem(new AssetLocation(IwDomain, path))
                   ?? (Parts.KindOf(path, out _) == PartKind.BladeKit ? FirstBladeKit() : null);
        if (item == null)
        {
            Api.Logger.Warning("[seraphhorizons] Bucking sawmill: no {0}:{1} for the creative shortcut", IwDomain, path);
            return false;
        }
        return TryFitPart(new DummySlot(new ItemStack(item)), byPlayer, free: true);
    }

    private Item? FirstBladeKit() =>
        Api.World.Items.FirstOrDefault(i => i?.Code is { Domain: IwDomain } c && Parts.KindOf(c.Path, out _) == PartKind.BladeKit);

    /// <summary>Fits the part in <paramref name="slot"/> (server side); false, with an error to
    /// the player, when the rules say no. The part is taken from the slot unless the player is in
    /// creative mode or it is <paramref name="free"/>.</summary>
    public bool TryFitPart(ItemSlot slot, IPlayer byPlayer, bool free = false)
    {
        var stack = slot.Itemstack;
        if (stack?.Collectible?.Code is not { Domain: IwDomain } code)
            return false;
        var verdict = _parts.Fit(code.Path);
        if (verdict != FitVerdict.Fits)
        {
            Error(byPlayer, verdict switch
            {
                FitVerdict.AlreadyFitted => "error-part-fitted",
                FitVerdict.NeedsSashes => "error-need-sashes",
                FitVerdict.BladeFitted => "error-blade-fitted",
                _ => "error-not-a-part",
            });
            return false;
        }
        var fitted = stack.Clone();
        fitted.StackSize = 1;
        if (Parts.KindOf(code.Path, out _) == PartKind.BladeKit)
            _blade = fitted;
        if (!free && byPlayer.WorldData.CurrentGameMode != EnumGameMode.Creative)
        {
            slot.TakeOut(1);
            slot.MarkDirty();
        }
        Api.World.PlaySoundAt(Block.Sounds.Place, Pos, -0.25, byPlayer);
        MarkDirty(true);
        return true;
    }

    /// <summary>Gives the trunk back if it is still recoverable (server side).</summary>
    public bool TryTakeTrunk(IPlayer byPlayer)
    {
        if (_trunk == null)
            return false;
        if (!Cutting.Recoverable(_progress))
        {
            Error(byPlayer, "error-trunk-cut");
            return true;
        }
        var trunk = _trunk;
        ClearTrunk();
        Give(byPlayer, trunk);
        Api.World.PlaySoundAt(new AssetLocation("game", "sounds/block/wood"), Pos, 0, byPlayer);
        MarkDirty(true);
        return true;
    }

    /// <summary>Gives back the blade kit (server side).</summary>
    public bool TryTakeBladeKit(IPlayer byPlayer)
    {
        if (_blade is not { } blade)
            return false;
        _blade = null;
        _parts.RemoveBladeKit();
        Give(byPlayer, blade);
        MarkDirty(true);
        return true;
    }

    public bool TryLoadFromSlot(ItemSlot slot, IPlayer? byPlayer, bool passedTop = false)
    {
        if (slot.Itemstack is not { } stack || !CanLoad(stack, byPlayer, passedTop))
            return false;
        Load(slot.TakeOutWhole());
        slot.MarkDirty();
        Api.World.PlaySoundAt(new AssetLocation("game", "sounds/block/wood"), Pos, 0, byPlayer);
        return true;
    }

    /// <summary>Loads the first trunk in the player's hotbar, then backpack, as Logging Expanded's
    /// workstations find one (branched trunks are skipped while Logging Expanded requires them
    /// debranched).</summary>
    public bool TryLoadFromInventory(IPlayer byPlayer)
    {
        if (FindTrunk(byPlayer, branchedToo: true) is { } slot)
            return TryLoadFromSlot(slot, byPlayer);
        return Error(byPlayer, "error-no-trunk");
    }

    /// <summary>The trunk a player's click loads: the one in hand, else the first in the hotbar,
    /// then backpack, that the mill takes, as Logging Expanded's workstations find one (with
    /// <paramref name="branchedToo"/>, a branched one when there is no other, for Logging Expanded's
    /// message).</summary>
    private ItemSlot? FindTrunk(IPlayer byPlayer, bool branchedToo = false)
    {
        var hand = byPlayer.InventoryManager.ActiveHotbarSlot;
        if (hand?.Itemstack is { } held)
            return Trunks.IsTrunk(held) && (branchedToo || !BranchedAndRefused(held)) ? hand : null;
        ItemSlot? branched = null;
        foreach (var name in new[] { GlobalConstants.hotBarInvClassName, GlobalConstants.backpackInvClassName })
        {
            if (byPlayer.InventoryManager.GetOwnInventory(name) is not { } inventory)
                continue;
            foreach (var slot in inventory)
            {
                if (slot.Itemstack is not { } stack || !Trunks.IsTrunk(stack))
                    continue;
                if (!BranchedAndRefused(stack))
                    return slot;
                branched ??= slot;
            }
        }
        return branchedToo ? branched : null;
    }

    private bool BranchedAndRefused(ItemStack trunk) =>
        Trunks.IsBranched(trunk) && (System.Logging?.RequireBranchRemoval ?? true);

    /// <summary>Whether <paramref name="trunk"/> can go in now (or this tick, the saws having
    /// <paramref name="passedTop"/>); tells the player why not.</summary>
    public bool CanLoad(ItemStack trunk, IPlayer? byPlayer, bool passedTop = false)
    {
        if (!Trunks.IsTrunk(trunk))
            return false;
        if (System.Logging == null)
            return Error(byPlayer, "error-no-logging");
        if (!_parts.Complete)
            return Error(byPlayer, "error-incomplete");
        if (_trunk != null)
            return Error(byPlayer, "error-bed-full");
        if (BranchedAndRefused(trunk))
        {
            // Logging Expanded's own message, as its sawhorses give it.
            if (byPlayer is IServerPlayer sp)
                sp.SendIngameError("", Lang.GetL(sp.LanguageCode, "loggingmod:treetrunk-branches-first"));
            return false;
        }
        if (!Feeding.CanTakeTrunk(_depth, passedTop))
            return Error(byPlayer, Running ? "error-saws-not-up" : "error-saws-down");
        if (Trunks.StoredLogs(trunk, Api.World) <= 0)
            return Error(byPlayer, "error-empty-trunk");
        return true;
    }

    private void Load(ItemStack trunk)
    {
        _trunk = trunk;
        _trunkClass = TrunkBox.ClassOf(trunk.Block?.Variant["size"]);
        _storedLogs = Trunks.StoredLogs(trunk, Api.World);
        // Going down, the saws drop onto the trunk at once; going up, they finish the rise first.
        var cycle = SawDepth.Load(new SawCycle(_depth, _rising), TouchDepth(trunk));
        (_depth, _rising, _progress) = (cycle.Depth, cycle.Rising, cycle.Progress);
        MarkDirty(true);
    }

    /// <summary>Where the saws come to rest on <paramref name="trunk"/>.</summary>
    private float TouchDepth(ItemStack trunk) =>
        SawDepth.Touch(Rig?.Saw ?? SawTravel.Default, Rig?.TrunkBed, trunk.Block?.Variant["size"]);

    /// <summary>Clears the bed; the saws carry on from where they are, at the empty rate.</summary>
    private void ClearTrunk()
    {
        _trunk = null;
        _trunkClass = TrunkClass.None;
        _storedLogs = 0;
        _progress = 0;
    }

    private void Give(IPlayer byPlayer, ItemStack stack)
    {
        if (!byPlayer.InventoryManager.TryGiveItemstack(stack, true))
            Api.World.SpawnItemEntity(stack, Pos.ToVec3d().Add(0.5, 0.6, 0.5));
    }

    private bool Error(IPlayer? byPlayer, string key, params object[] args)
    {
        if (byPlayer is IServerPlayer sp)
            sp.SendIngameError("buckingsawmill-" + key, Lang.GetL(sp.LanguageCode, BuckingSawmillSystem.Domain + ":buckingmill-" + key, args));
        return false;
    }

    // ---- Rack ----

    /// <summary>An assembled, empty mill with its saws at the top of their cycle, turning fast
    /// enough, takes the next trunk from a Trunk Storage Rack touching its infeed end at ground
    /// level, unless that trunk is branched (it waits for the player to debranch it). Tried every
    /// tick the saws are at (or pass) the top, and once a second besides; the rack is looked up
    /// every time.</summary>
    private void OnRackTick(float dt)
    {
        // A ghost can vanish without being broken (an explosion, another mod), the power ghost included.
        EnsureGhosts();
        PullFromRack();
        var state = CheckRack(out _);
        if (state != _rackState)
        {
            _rackState = state;
            MarkDirty();
        }
    }

    public bool PullFromRack(bool passedTop = false)
    {
        if (_trunk != null || !Feeding.CanTakeTrunk(_depth, passedTop) || !_parts.Complete || ShaftSpeed < Config.MinSpeed
            || CheckRack(out var rack) != RackState.Ready || System.Logging is not { } logging
            || logging.PopTrunk(rack!) is not { } trunk)
            return false;
        rack!.MarkDirty(true);
        Load(trunk);
        _rackState = CheckRack(out _);
        return true;
    }

    /// <summary>What the racks at the infeed end offer (server side): the first that has a trunk the
    /// mill takes is <paramref name="ready"/>; otherwise the most telling reason it has none. A rack
    /// counts when its controller or filler cell is one of the ground cells just outside the infeed
    /// end (<see cref="Rig.InfeedNeighbours"/>), however it is turned.</summary>
    public RackState CheckRack(out BlockEntity? ready)
    {
        ready = null;
        if (!Config.AutoPullFromRack)
            return RackState.Off;
        if (System.Logging is not { } logging)
            return RackState.NoLogging;
        if (Rig is not { } rig)
            return RackState.None;
        var best = RackState.None;
        foreach (var local in rig.InfeedNeighbours())
        {
            if (FindRack(logging, CellPos(local)) is not { } rack)
                continue;
            var state = logging.PeekTrunk(rack) is not { } top || !Trunks.IsTrunk(top) ? RackState.Empty
                : BranchedAndRefused(top) ? RackState.Branched
                : Trunks.StoredLogs(top, Api.World) <= 0 ? RackState.NoLogs
                : RackState.Ready;
            if (state == RackState.Ready)
            {
                ready = rack;
                return state;
            }
            if (state > best)
                best = state;
        }
        return best;
    }

    /// <summary>The rack whose controller or filler cell is at <paramref name="pos"/>.</summary>
    private BlockEntity? FindRack(LoggingBridge logging, BlockPos pos)
    {
        var ba = Api.World.BlockAccessor;
        if (ba.GetBlock(pos) is BlockMultiblock filler)
            pos = pos.AddCopy(filler.OffsetInv);
        var be = ba.GetBlockEntity(pos);
        return logging.IsRack(be) ? be : null;
    }

    // ---- Cutting ----

    // Runs the cycle while assembled and turning fast enough: the saws sink (cutting the loaded
    // trunk, if any), are wound back up, and sink again.
    private void OnCutTick(float dt)
    {
        var power = Power;
        float angle = power?.AngleRad ?? 0, speed = power?.TrueSpeed ?? 0;
        bool work = _trunk == null || System.Logging != null;
        if (!work || !_parts.Complete || speed < Config.MinSpeed || !_angleSeeded)
        {
            _lastAngle = angle;
            _angleSeeded = true;
            RunHolds(dt, false);
            return;
        }
        float advance = Cutting.AngleAdvance(_lastAngle, angle, speed, dt);
        _lastAngle = angle;
        Turn(advance, dt);
    }

    /// <summary>Turns the cycle by <paramref name="radians"/> and feeds it (server side): a trunk
    /// waiting in a player's hands, else the rack's, goes on if the saws are at the top or passed
    /// it during the turn, however far the shaft turned in one tick. Returns whether a trunk went on.</summary>
    public bool Turn(float radians, float dt = 0.05f)
    {
        bool passedTop = Advance(radians);
        if (RunHolds(dt, passedTop))
            return true;
        return _trunk == null && Feeding.CanTakeTrunk(_depth, passedTop) && PullFromRack(passedTop);
    }

    /// <summary>Turns the cycle by <paramref name="radians"/> of shaft rotation (server side);
    /// finishes the cut when the trunk is through. Returns whether the saws passed the top.</summary>
    public bool Advance(float radians)
    {
        if (radians <= 0)
            return false;
        float beforeDepth = _depth, beforeProgress = _progress;
        bool beforeRising = _rising;
        float? touch = _trunk != null ? TouchDepth(_trunk) : null;
        var step = SawDepth.Advance(new SawCycle(_depth, _rising, _progress), radians, touch, _storedLogs,
                                    Cutting.CutRevolutions(Config.RevolutionsPerStoredLog, BladeSpeed), Config.RaiseRevolutions);
        (_depth, _rising, _progress) = (step.Cycle.Depth, step.Cycle.Rising, step.Cycle.Progress);
        if (step.CutFinished)
            FinishCut();
        else if (_rising != beforeRising || (int)(beforeDepth * 20) != (int)(_depth * 20)
                 || (int)(beforeProgress * 20) != (int)(_progress * 20) || Cutting.Recoverable(beforeProgress) != Cutting.Recoverable(_progress))
            MarkDirty();
        return step.PassedTop;
    }

    private void FinishCut()
    {
        var trunk = _trunk!;
        int stored = _storedLogs;
        var log = Trunks.Wood(trunk, Api.World) is { } wood && System.Logging?.PlacedLogCode(wood) is { } code
            ? Api.World.GetBlock(code)
            : null;
        ClearTrunk();
        var (spawnAt, velocity) = OutputPoint();
        if (log == null || log.Id == 0)
        {
            Api.Logger.Warning("[seraphhorizons] Bucking sawmill: No log for {0} at {1}; the trunk is given back", trunk.Collectible.Code, Pos);
            Api.World.SpawnItemEntity(trunk, spawnAt, velocity);
            MarkDirty(true);
            return;
        }
        foreach (int size in Cutting.SplitStacks(Cutting.LogYield(stored, Config.LogsPerStoredLog), log.MaxStackSize))
            Api.World.SpawnItemEntity(new ItemStack(log, size), spawnAt, velocity);
        WearBlade(Cutting.BladeWear(stored, Config.BladeWearPerStoredLog));
        MarkDirty(true);
    }

    private (Vec3d At, Vec3d Velocity) OutputPoint()
    {
        var rig = Rig!;
        var p = Footprint.ToWorld(rig.OutputPos, Side);
        var n = Footprint.ToWorld(rig.OutputSide, Side).Normal();
        return (new Vec3d(Pos.X + p.X, Pos.Y + p.Y, Pos.Z + p.Z), new Vec3d(n.X * 0.05, 0, n.Z * 0.05));
    }

    /// <summary>Wears the blade kit by <paramref name="wear"/>; one worn to 0 breaks, and the mill
    /// stops until a new one is fitted.</summary>
    private void WearBlade(int wear)
    {
        if (wear <= 0 || _blade is not { } blade || blade.Collectible.GetMaxDurability(blade) <= 1)
            return;
        int left = blade.Collectible.GetRemainingDurability(blade) - wear;
        if (left > 0)
        {
            blade.Collectible.SetDurability(blade, left);
            return;
        }
        _blade = null;
        _parts.RemoveBladeKit();
        Api.World.PlaySoundAt(BreakSound, Pos.X + 0.5, Pos.Y + 0.5, Pos.Z + 0.5);
    }

    // The client turns its own copies of the cycle with the shaft between syncs, by the same
    // arithmetic. The shown depth follows those local steps directly and eases toward the estimate
    // when a sync moves it, so a new trunk's drop is quick rather than a snap.
    private void OnClientTick(float dt)
    {
        // The client removes a broken mill at once, and an update the server sent before it heard
        // of the break then brings the block entity back over air, renderer and all: drop it.
        if (Api.World.BlockAccessor.GetBlock(Pos) is not BlockBuckingMill)
        {
            DisposeRenderer();
            Api.World.BlockAccessor.RemoveBlockEntity(Pos);
            return;
        }
        var power = Power;
        float angle = power?.AngleRad ?? 0, speed = power?.TrueSpeed ?? 0;
        float advance = 0;
        if (_parts.Complete && speed >= MinSpeed && _clientAngleSeeded)
            advance = Cutting.AngleAdvance(_clientLastAngle, angle, speed, dt);
        _clientLastAngle = angle;
        _clientAngleSeeded = true;

        float before = _clientDepthEstimate;
        float? touch = _trunk != null && !_clientCutDone ? TouchDepth(_trunk) : null;
        var step = SawDepth.Advance(new SawCycle(_clientDepthEstimate, _clientRising, _clientProgress), advance, touch, _storedLogs,
                                    Cutting.CutRevolutions(RevolutionsPerStoredLog, BladeSpeed), RaiseRevolutions);
        (_clientDepthEstimate, _clientRising, _clientProgress) = (step.Cycle.Depth, step.Cycle.Rising, step.Cycle.Progress);
        if (step.CutFinished)
        {
            _clientCutDone = true;
            _clientProgress = 1;
        }
        float stepped = advance > 0 ? _clientDepthEstimate - before : 0;
        // a big jump (a trunk dropped onto, the bed reached) is eased, small steps are followed
        if (Math.Abs(stepped) > 0.05f)
            stepped = 0;
        _clientDepthBefore = ClientSawDepth;
        _clientDepth = SawDepth.Ease(Math.Clamp(_clientDepth + stepped, 0, 1), _clientDepthEstimate, dt);
        _clientDepthMs = Api.World.ElapsedMilliseconds;
        _clientDepthSeconds = dt;
    }

    // ---- Breaking ----

    /// <summary>What breaking the frame gives besides the frame: every fitted part, the blade kit,
    /// and the trunk while it is recoverable.</summary>
    public IEnumerable<ItemStack> PartDrops()
    {
        Item? Part(string path) => Api.World.GetItem(new AssetLocation(IwDomain, path));
        for (int i = 0; i < _parts.Sashes; i++)
            if (Part(Parts.SashPath) is { } sash)
                yield return new ItemStack(sash);
        if (_parts.Crankshaft && Part(Parts.CrankshaftPath) is { } crankshaft)
            yield return new ItemStack(crankshaft);
        if (_parts.Levers && Part(Parts.LeversPath) is { } levers)
            yield return new ItemStack(levers);
        if (_blade != null)
            yield return _blade.Clone();
        if (_trunk != null && Cutting.Recoverable(_progress))
            yield return _trunk.Clone();
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
        tree.SetInt("sashes", _parts.Sashes);
        tree.SetBool("crankshaft", _parts.Crankshaft);
        tree.SetBool("levers", _parts.Levers);
        if (_blade != null)
            tree.SetItemstack("blade", _blade);
        else
            tree.RemoveAttribute("blade");
        if (_trunk != null)
            tree.SetItemstack("trunk", _trunk);
        else
            tree.RemoveAttribute("trunk");
        tree.SetInt("storedLogs", _storedLogs);
        tree.SetFloat("progress", _progress);
        tree.SetFloat("depth", _depth);
        tree.SetBool("rising", _rising);
        if (Api?.Side == EnumAppSide.Server)
        {
            tree.SetFloat("minSpeed", Config.MinSpeed);
            tree.SetFloat("revolutionsPerStoredLog", Config.RevolutionsPerStoredLog);
            tree.SetFloat("raiseRevolutions", Config.RaiseRevolutions);
            tree.SetFloat("bladeSpeed", BladeSpeed);
            tree.SetInt("rackState", (int)_rackState);
        }
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);
        _blade = tree.GetItemstack("blade");
        string? metal = null;
        if (_blade != null && !(_blade.ResolveBlockOrItem(worldForResolving)
                                && Parts.KindOf(_blade.Collectible.Code.Path, out metal) == PartKind.BladeKit))
            _blade = null;
        _parts = new Parts(tree.GetInt("sashes"), tree.GetBool("crankshaft"), tree.GetBool("levers"), _blade == null ? null : metal);
        if (!_parts.BladeKit)
            _blade = null;
        _trunk = tree.GetItemstack("trunk");
        if (_trunk != null && !_trunk.ResolveBlockOrItem(worldForResolving))
            _trunk = null;
        _trunkClass = _trunk == null ? TrunkClass.None : TrunkBox.ClassOf(_trunk.Block?.Variant["size"]);
        _storedLogs = tree.GetInt("storedLogs");
        _progress = tree.GetFloat("progress");
        _depth = Math.Clamp(tree.GetFloat("depth"), 0, 1);
        _rising = tree.GetBool("rising");
        if (worldForResolving.Side == EnumAppSide.Client)
        {
            _serverMinSpeed = tree.TryGetFloat("minSpeed");
            _serverRevolutions = tree.TryGetFloat("revolutionsPerStoredLog");
            _serverRaiseRevolutions = tree.TryGetFloat("raiseRevolutions");
            _serverBladeSpeed = tree.TryGetFloat("bladeSpeed");
            _rackState = (RackState)tree.GetInt("rackState");
        }
        // A sync that disagrees with the client's own estimate (a new trunk, a finished cut, a
        // change of direction, or drift) resets it to the server's; the shown depth eases to it.
        if (_trunk == null)
            _clientCutDone = false;
        if (_trunk == null || Math.Abs(_clientProgress - _progress) > 0.05f)
            _clientProgress = _progress;
        if (_clientRising != _rising || Math.Abs(_clientDepthEstimate - _depth) > 0.05f)
        {
            _clientDepthEstimate = _depth;
            _clientRising = _rising;
        }
    }

    public override void OnStoreCollectibleMappings(Dictionary<int, AssetLocation> blockIdMapping, Dictionary<int, AssetLocation> itemIdMapping)
    {
        base.OnStoreCollectibleMappings(blockIdMapping, itemIdMapping);
        foreach (var stack in new[] { _blade, _trunk })
            stack?.Collectible.OnStoreCollectibleMappings(Api.World, new DummySlot(stack), blockIdMapping, itemIdMapping);
    }

    public override void OnLoadCollectibleMappings(IWorldAccessor worldForResolve, Dictionary<int, AssetLocation> oldBlockIdMapping,
                                                   Dictionary<int, AssetLocation> oldItemIdMapping, int schematicSeed, bool resolveImports)
    {
        base.OnLoadCollectibleMappings(worldForResolve, oldBlockIdMapping, oldItemIdMapping, schematicSeed, resolveImports);
        foreach (var stack in new[] { _blade, _trunk })
            stack?.FixMapping(oldBlockIdMapping, oldItemIdMapping, worldForResolve);
    }

    // ---- Info ----

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
    {
        base.GetBlockInfo(forPlayer, dsc);
        string L(string key, params object[] args) => Lang.Get(BuckingSawmillSystem.Domain + ":buckingmill-" + key, args);
        if (!_parts.Complete)
        {
            dsc.AppendLine(L("info-incomplete"));
            foreach (var group in _parts.Missing().GroupBy(p => p))
            {
                string name = group.Key.EndsWith('*')
                    ? L("info-bladekit")
                    : Lang.Get(IwDomain + ":item-" + group.Key);
                dsc.AppendLine("• " + (group.Count() > 1 ? L("info-count", group.Count(), name) : name));
            }
        }
        if (_blade is { } blade)
        {
            int max = blade.Collectible.GetMaxDurability(blade);
            dsc.AppendLine(max > 1
                ? L("info-blade-durability", blade.GetName(), blade.Collectible.GetRemainingDurability(blade), max)
                : blade.GetName());
            dsc.AppendLine(L("info-blade-speed", BladeSpeed.ToString("0.##")));
        }
        // How far through the travel the saws are: the client's eased depth, the server's own on the server.
        float depth = SideDepth;
        if (_parts.Sashes > 0 && Feeding.WindsUp(Running, depth))
            dsc.AppendLine(L("info-windup"));
        if (!_parts.Complete)
            return;
        switch (Phase)
        {
            case MillPhase.Cutting:
                dsc.AppendLine(L("info-trunk", _trunk!.GetName(), _storedLogs));
                dsc.AppendLine(L("info-progress", (int)(ClientProgress * 100)));
                if (!Cutting.Recoverable(_progress))
                    dsc.AppendLine(L("info-committed"));
                break;
            case MillPhase.Raising:
                dsc.AppendLine(L("info-raising", (int)((1 - depth) * 100)));
                break;
            case MillPhase.Sinking:
                dsc.AppendLine(L("info-sinking", (int)(depth * 100)));
                break;
            default:
                dsc.AppendLine(L("info-stopped"));
                if (_trunk != null)
                    dsc.AppendLine(L("info-trunk", _trunk.GetName(), _storedLogs));
                break;
        }
        if (_trunk == null && SawDepth.AtTop(depth))
            dsc.AppendLine(L("info-attop"));
        else if (_trunk == null && Running)
            dsc.AppendLine(L("info-hold-to-load"));
        // Why the rack's trunks are, or are not, coming.
        if (_trunk == null && _rackState != RackState.Unknown)
            dsc.AppendLine(L("info-rack-" + _rackState.ToString().ToLowerInvariant()));
        float speed = ShaftSpeed;
        dsc.AppendLine(speed < MinSpeed ? L("info-nopower") : L("info-speed", speed.ToString("0.00")));
    }
}
