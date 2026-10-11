using System.Text;
using SeraphHorizons.Mod.Core;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.MandrelStation.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace SeraphHorizons.Mod.MandrelStation;

/// <summary>
/// The mandrel station's controller. Holds the fitted mandrel (its code), the hollow section on it as
/// its item stack, and the forging (<see cref="ForgeJob"/>: the blows struck and W, 0..1). The player
/// forges as on the anvil: each right-click with a hammer is a blow (the anvil's sound and sparks at
/// <c>strike.pos</c>, the hammer's durability paid; a hammer of a higher tool tier forges more a blow),
/// and at the blow that finishes the hollow two pipe sections of the
/// hollow's metal go into a container beyond the tip, or drop there. A blow on a bare mandrel takes a
/// hollow from a chest or hopper beside the stump, or, with right-click held after a hollow is finished,
/// another of the same from the player's hotbar. The mandrel, once fitted, comes out only by breaking. The server keeps the ghost cell stamped; the client
/// draws the station (<see cref="MandrelStationRenderer"/>, through <see cref="IMandrelStationView"/>).
/// <para>Its second job winds a spring (<see cref="Winding"/>): a hot steel rod goes on a fitted, bare
/// mandrel, and the player winds it by holding right-click with tongs, as on the quern; it winds only
/// while the rod is at working temperature (the anvil's rule), and the spring comes off at the rod's
/// temperature. Ctrl + right-click takes the rod back, straight, at any time (to reheat it).</para>
/// The rules are MandrelStation/Core's.
/// </summary>
public class BEMandrelStation : BlockEntity, IMandrelStationView
{
    private static readonly AssetLocation LatchSound = new("game", "sounds/effect/latch");
    private static readonly AssetLocation HollowSound = new("game", "sounds/block/chute");
    private static readonly AssetLocation BlowSound = new("game", "sounds/effect/anvilhit");
    private static readonly AssetLocation SectionSound = new("game", "sounds/block/chute");
    private static readonly AssetLocation RodSound = new("game", "sounds/block/ingot");
    // The server syncs the winding's W at least this often; the renderer follows the held clock between.
    private const double WindSyncStep = 0.02;
    // A client's own player holding right-click counts as winding for this long after its last step.
    private const long LocalHoldMs = 250;

    private string? _mandrel;
    private ItemStack? _hollow;
    private ForgeJob _job = ForgeJob.None;
    private ItemStack? _rod;
    private WindJob _wind = WindJob.None;
    private readonly WindHolds _holds = new();
    private bool _held;
    private string? _lastWinder;
    private bool _toldCold;
    private long _localHeldAt = long.MinValue / 2;
    private double? _serverTurnsPerRod;
    private long _lastBlowAt = long.MinValue / 2;
    private string? _lastFinished;
    private string? _lastClickBy;
    private long _lastClickAt = long.MinValue / 2;
    private int? _serverBlowsNeeded;
    private Dictionary<Int3, Cuboidf[]>? _cells;
    private Dictionary<Int3, Cuboidf[]>? _collision;
    private MandrelStationRenderer? _renderer;

    public Side Side { get; private set; } = Side.North;
    /// <summary>The fitted mandrel's code, or null.</summary>
    public string? Mandrel => _mandrel;
    /// <summary>The mandrel is fitted: the station works.</summary>
    public bool Complete => _mandrel != null;
    /// <summary>A hollow is on the mandrel.</summary>
    public bool HollowOn => _job.On && _hollow != null;
    public ItemStack? Hollow => _hollow;
    public ForgeJob Job => _job;
    /// <summary>A steel rod is on the mandrel, being wound into a spring.</summary>
    public bool RodOn => _wind.On && _rod != null;
    public ItemStack? Rod => _rod;
    public WindJob Wind => _wind;
    /// <summary>Work is on the mandrel: a hollow or a rod.</summary>
    public bool WorkOn => HollowOn || RodOn;
    /// <summary>The hollow on: 1 lead, 2 copper, 0 none (a rod is not a hollow).</summary>
    public int HollowClass => HollowOn ? _job.Class : 0;

    /// <summary>Blows the hollow on takes with the base hammer (the copper one), as the server runs (0
    /// with none); a better hammer takes fewer.</summary>
    public int BlowsNeeded => _job.On ? _serverBlowsNeeded ?? Config.BlowsPerHollow(_job.Class) : 0;

    // IMandrelStationView
    public bool PartFitted(string? requires) => MandrelPart.Fitted(requires, _mandrel);
    public string? MandrelMetal => MandrelPart.MetalOf(_mandrel);
    public int JobClass => HollowOn ? _job.Class : RodOn ? Winding.SpringClass : 0;
    public double JobWork => RodOn ? _wind.Work : _job.Work;
    public int Blows => _job.Blows;
    public bool Held =>
        _held || Api is { Side: EnumAppSide.Client } && Api.World.ElapsedMilliseconds - _localHeldAt <= LocalHoldMs;
    public double TurnsPerRod => _serverTurnsPerRod ?? Config.TurnsPerRod;
    public float RodTemperature => _rod is { } rod && Api != null ? rod.Collectible.GetTemperature(Api.World, rod) : 0;

    private MandrelStationSystem System => MandrelStationSystem.Of(Api);
    private MandrelStationConfig Config => System.Config;
    private MandrelStationRig? Rig => System.Rig;

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
            RegisterGameTickListener(_ => EnsureGhosts(), 1000);
            RegisterGameTickListener(OnWindTick, 50);
        }
        else
        {
            RegisterGameTickListener(OnClientTick, 250);
            if (api is ICoreClientAPI capi && Rig is { } rig && Block is BlockMandrelStation)
                _renderer = new MandrelStationRenderer(capi, this, this, rig);
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

    private Block? GhostBlock() => Api.World.GetBlock(new AssetLocation(MandrelStationSystem.Domain, "mandrelstation-ghost"));

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
        if (Api.World.BlockAccessor.GetBlockEntity(pos) is BEMandrelStationGhost be)
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
                if (ba.GetBlockEntity(pos) is BEMandrelStationGhost be && !Pos.Equals(be.Principal))
                {
                    be.Principal = Pos.Copy();
                    be.MarkDirty(true);
                }
            }
            else if (current.Id == 0 || current is BlockMandrelStationGhost)
                StampGhost(expected, pos);
        }
    }

    public void RemoveGhosts()
    {
        if (Api.Side != EnumAppSide.Server)
            return;
        var ba = Api.World.BlockAccessor;
        foreach (var pos in GhostCells())
            if (ba.GetBlock(pos) is BlockMandrelStationGhost && ba.GetBlockEntity(pos) is BEMandrelStationGhost be && Pos.Equals(be.Principal))
                ba.SetBlock(0, pos);
    }

    // ---- Interaction ----

    /// <summary>
    /// Right-click on the station or its ghost. Ctrl takes back: in creative mode with no mandrel it
    /// fits an iron one with nothing taken; else a hollow not yet struck comes off, or a rod at any time
    /// (the mandrel is a part, not a consumable: it comes out only by breaking the station). With a rod
    /// on, any other click (Shift aside) starts winding it, held as on the quern, with tongs in either
    /// hand (<see cref="OnWorkStep"/>). A rod of iron, meteoric iron or steel is fitted as the mandrel on
    /// a bare station; on a fitted, bare mandrel a hot steel rod goes on to be wound (a cold one is
    /// refused); a lead or copper hollow section goes on a bare mandrel; a hammer strikes a blow (on a
    /// bare mandrel it takes a hollow from the infeed instead). Anything else is the item's own business.
    /// Decided and done on the server; the client says whether the click is the station's.
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
                FitCreativeMandrel(byPlayer);
            else if (RodOn)
                TakeRod(byPlayer);
            else
                TakeBack(byPlayer);
            return true;
        }
        if (RodOn && !controls.ShiftKey)
            return StartWinding(byPlayer);
        if (MandrelPart.IsMandrel(code))
        {
            if (server)
                OfferRod(slot!, byPlayer);
            return true;
        }
        if (Forging.ClassOfHollow(code) != 0 && !WorkOn)
            return !server || TryLoadHollow(slot!, byPlayer);
        if (Forging.IsHammer(code) && !controls.ShiftKey)
        {
            if (server)
                StrikeBy(byPlayer, slot!);
            return true;
        }
        return false;
    }

    /// <summary>Whether a click is the creative shortcut's: a player in creative mode, Ctrl without
    /// Shift, on a station with no mandrel.</summary>
    public bool CreativeShortcutApplies(IPlayer byPlayer)
    {
        var controls = byPlayer.Entity.Controls;
        return !Complete
               && CreativeUpgrades.Applies(byPlayer.WorldData?.CurrentGameMode == EnumGameMode.Creative, controls.CtrlKey, controls.ShiftKey);
    }

    /// <summary>The creative shortcut (server side): fits an iron mandrel with nothing taken.</summary>
    public bool FitCreativeMandrel(IPlayer? byPlayer)
    {
        if (Complete)
            return false;
        if (Api.World.GetItem(new AssetLocation(MandrelPart.Codes[0])) is not { } item)
        {
            Api.Logger.Warning("[seraphhorizons] Mandrel station: no {0} for the creative shortcut", MandrelPart.Codes[0]);
            return false;
        }
        return TryFitMandrel(new DummySlot(new ItemStack(item)), byPlayer, free: true);
    }

    /// <summary>Fits the rod in <paramref name="slot"/> as the mandrel (server side); false, with an
    /// error to the player, when one is already in. One item is taken from the slot unless the player
    /// is in creative mode or it is <paramref name="free"/>.</summary>
    public bool TryFitMandrel(ItemSlot slot, IPlayer? byPlayer, bool free = false)
    {
        string? code = slot.Itemstack?.Collectible?.Code?.ToString();
        switch (MandrelPart.CanFit(code, _mandrel))
        {
            case MandrelFitVerdict.Fits:
                break;
            case MandrelFitVerdict.AlreadyFitted:
                return Error(byPlayer, "error-mandrel-fitted");
            default:
                return false;
        }
        _mandrel = MandrelPart.Normalise(code);
        if (!free && byPlayer?.WorldData.CurrentGameMode != EnumGameMode.Creative)
        {
            slot.TakeOut(1);
            slot.MarkDirty();
        }
        Api.World.PlaySoundAt(LatchSound, Pos, -0.25, byPlayer);
        MarkDirty(true);
        return true;
    }

    /// <summary>Ctrl + right-click (server side): a hollow not yet struck comes off. A hollow being
    /// forged stays. The mandrel never comes out this way: the station has no consumable part, and
    /// breaking it gives the mandrel back.</summary>
    public bool TakeBack(IPlayer byPlayer)
    {
        if (!HollowOn)
            return false;
        if (!_job.Untouched)
            return Error(byPlayer, "error-busy");
        var hollow = _hollow!;
        ClearJob();
        Give(byPlayer, hollow);
        Api.World.PlaySoundAt(HollowSound, Pos, 0, byPlayer);
        MarkDirty(true);
        return true;
    }

    /// <summary>With right-click held after a hollow was finished (server side): puts another of the
    /// same item on from the first of the player's hotbar slots holding one
    /// (<see cref="Forging.RefillSlot"/>; not the backpack), as a right-click with it in hand would
    /// (<see cref="TryLoadHollow"/>). Returns whether one went on.</summary>
    public bool RefillFromHotbar(IPlayer byPlayer)
    {
        if (Api.Side != EnumAppSide.Server || WorkOn || !Complete
            || byPlayer.InventoryManager?.GetHotbarInventory() is not { } hotbar)
            return false;
        var codes = Enumerable.Range(0, hotbar.Count).Select(i => hotbar[i]?.Itemstack?.Collectible?.Code?.ToString()).ToList();
        int at = Forging.RefillSlot(codes, _lastFinished);
        return at >= 0 && hotbar[at] is { } slot && TryLoadHollow(slot, byPlayer);
    }

    /// <summary>The pipe section a hollow of class <paramref name="k"/> is forged into, or null when
    /// it does not exist in this game (it is UnifiedPipes' item).</summary>
    private Item? SectionItem(int k) =>
        Forging.SectionFor(k) is { } code && Api.World.GetItem(new AssetLocation(code)) is { Id: > 0, IsMissing: false } item ? item : null;

    /// <summary>Puts a hollow from <paramref name="slot"/> on the mandrel, if the station takes it now.</summary>
    public bool TryLoadHollow(ItemSlot slot, IPlayer? byPlayer)
    {
        string? code = slot.Itemstack?.Collectible?.Code?.ToString();
        switch (Forging.CanLoad(code, Complete, WorkOn))
        {
            case HollowLoadVerdict.Loads:
                break;
            case HollowLoadVerdict.NoMandrel:
                return Error(byPlayer, "error-no-mandrel");
            case HollowLoadVerdict.Occupied:
                return Error(byPlayer, "error-occupied");
            default:
                return false;
        }
        if (SectionItem(Forging.ClassOfHollow(code)) == null)
            return Error(byPlayer, "error-no-section");
        Load(slot.TakeOut(1));
        slot.MarkDirty();
        return true;
    }

    private void Load(ItemStack hollow)
    {
        _hollow = hollow;
        _job = new ForgeJob(Forging.ClassOfHollow(hollow.Collectible.Code.ToString()), 0, 0);
        var at = WorldPoint(Rig?.Strike ?? new Float3(0.5f, 0.75f, 1f));
        Api.World.PlaySoundAt(HollowSound, at.X, at.Y, at.Z);
        MarkDirty(true);
    }

    private void ClearJob()
    {
        _hollow = null;
        _job = ForgeJob.None;
    }

    private void Give(IPlayer byPlayer, ItemStack stack)
    {
        if (!byPlayer.InventoryManager.TryGiveItemstack(stack, true))
            Api.World.SpawnItemEntity(stack, Pos.ToVec3d().Add(0.5, 1.2, 0.5));
    }

    private bool Error(IPlayer? byPlayer, string key, params object[] args)
    {
        if (byPlayer is IServerPlayer sp)
            sp.SendIngameError("mandrelstation-" + key, Lang.GetL(sp.LanguageCode, MandrelStationSystem.Domain + ":mandrelstation-" + key, args));
        return false;
    }

    // ---- Winding ----

    /// <summary>The spring a rod is wound into, or null when it does not exist in this game (the
    /// <c>MandrelSprings</c> switch off).</summary>
    public Item? SpringItem() =>
        Api.World.GetItem(new AssetLocation(Winding.Spring)) is { Id: > 0, IsMissing: false } item ? item : null;

    /// <summary>A stack's temperature, and whether it is at working temperature (the anvil's rule:
    /// its <c>workableTemperature</c> attribute, else half its melting point).</summary>
    public (float Temperature, float Workable, bool Hot) Heat(ItemStack stack)
    {
        var obj = stack.Collectible;
        float t = obj.GetTemperature(Api.World, stack);
        float melt = obj.GetMeltingPoint(Api.World, null, new DummySlot(stack));
        float? attr = obj.Attributes?["workableTemperature"] is { Exists: true } w ? w.AsFloat(melt / 2) : null;
        float workable = Winding.WorkableTemperature(melt, attr);
        return (t, workable, melt > 0 && t >= workable);
    }

    /// <summary>A rod right-clicked on the station (server side): fitted as the mandrel on a bare
    /// station, put on to be wound when it is a hot steel rod on a fitted, bare mandrel
    /// (<see cref="Winding.ForRod"/>), else refused with a message.</summary>
    public bool OfferRod(ItemSlot slot, IPlayer? byPlayer)
    {
        var stack = slot.Itemstack;
        string? code = stack?.Collectible?.Code?.ToString();
        var heat = stack != null && Winding.IsRod(code) ? Heat(stack) : default;
        switch (Winding.ForRod(code, _mandrel, WorkOn, SpringItem() != null, heat.Hot))
        {
            case RodVerdict.Mandrel:
                return TryFitMandrel(slot, byPlayer);
            case RodVerdict.AlreadyFitted:
                return Error(byPlayer, "error-mandrel-fitted");
            case RodVerdict.Occupied:
                return Error(byPlayer, "error-occupied");
            case RodVerdict.TooCold:
                return Error(byPlayer, "error-rod-cold", (int)heat.Temperature, (int)heat.Workable);
            case RodVerdict.Winds:
                return TryLoadRod(slot, byPlayer);
            default:
                return false;
        }
    }

    /// <summary>Puts one steel rod from <paramref name="slot"/> on the mandrel to be wound (server side),
    /// its stack (and so its temperature) kept: no checks but that the station takes one now.</summary>
    public bool TryLoadRod(ItemSlot slot, IPlayer? byPlayer)
    {
        if (!Complete || WorkOn || slot.Itemstack is not { } stack || !Winding.IsRod(stack.Collectible?.Code?.ToString()))
            return false;
        if (SpringItem() == null)
            return Error(byPlayer, "error-mandrel-fitted");
        _rod = slot.TakeOut(1);
        slot.MarkDirty();
        _wind = WindJob.Start;
        _toldCold = false;
        var at = WorldPoint(Rig?.Strike ?? new Float3(0.5f, 0.75f, 1f));
        Api.World.PlaySoundAt(RodSound, at.X, at.Y, at.Z, null, true, 16, 0.6f);
        MarkDirty(true);
        return true;
    }

    /// <summary>Ctrl + right-click with a rod on (server side): the rod comes back straight, its stack and
    /// temperature as they are, whatever the winding so far (to reheat it, or give up).</summary>
    public bool TakeRod(IPlayer byPlayer)
    {
        if (!RodOn)
            return false;
        var rod = _rod!;
        ClearRod();
        Give(byPlayer, rod);
        Api.World.PlaySoundAt(RodSound, Pos, 0, byPlayer);
        MarkDirty(true);
        return true;
    }

    private void ClearRod()
    {
        _rod = null;
        _wind = WindJob.None;
        _holds.Clear();
        _held = false;
    }

    /// <summary>Whether <paramref name="player"/> holds tongs in either hand (the game's tongs, any
    /// metal: their tool is <c>Tongs</c>); a player in creative mode needs none.</summary>
    public static bool HasTongs(IPlayer player) =>
        player.WorldData?.CurrentGameMode == EnumGameMode.Creative || TongsSlot(player) != null;

    private static ItemSlot? TongsSlot(IPlayer player)
    {
        foreach (var slot in new[] { player.Entity?.LeftHandItemSlot, player.InventoryManager?.ActiveHotbarSlot })
            if (slot?.Itemstack?.Collectible?.Tool == EnumTool.Tongs)
                return slot;
        return null;
    }

    /// <summary>A click on a station with a rod on: starts winding it, if the player has tongs.</summary>
    private bool StartWinding(IPlayer byPlayer)
    {
        if (Api.Side == EnumAppSide.Client)
        {
            _localHeldAt = Api.World.ElapsedMilliseconds;
            return true;
        }
        if (!HasTongs(byPlayer))
            return Error(byPlayer, "error-no-tongs");
        _holds.Hold(byPlayer.PlayerUID, Api.World.ElapsedMilliseconds);
        _lastWinder = byPlayer.PlayerUID;
        UpdateHeld();
        return true;
    }

    /// <summary>
    /// Right-click still held on the station (both sides, every tick, as the quern's grinding): the server
    /// counts the player as winding while a rod is on and they hold tongs; the client marks its own player
    /// as winding for the renderer. Whether the player goes on: while a rod is on.
    /// </summary>
    public bool OnWorkStep(IPlayer byPlayer, float secondsUsed)
    {
        long now = Api.World.ElapsedMilliseconds;
        if (Api.Side == EnumAppSide.Client)
        {
            if (!RodOn)
                return false;
            _localHeldAt = now;
            return true;
        }
        if (!RodOn || !HasTongs(byPlayer))
        {
            Release(byPlayer);
            return false;
        }
        _holds.Hold(byPlayer.PlayerUID, now);
        _lastWinder = byPlayer.PlayerUID;
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

    // The server's "someone is winding", synced when it changes.
    private void UpdateHeld()
    {
        bool held = _holds.Any(Api.World.ElapsedMilliseconds) && RodOn && Complete;
        if (held == _held)
            return;
        _held = held;
        MarkDirty(false);
    }

    private void OnWindTick(float dt)
    {
        UpdateHeld();
        if (_held)
            WindBy(Winding.RadiansPerSecond * dt);
    }

    /// <summary>Winds the rod by <paramref name="radians"/> of the held clock (server side): W advances by
    /// their turns over <c>TurnsPerRod</c> while the rod is at working temperature (a cold rod stops, and the
    /// winder is told once), and at W = 1 the rod is used up and the spring comes off, at the rod's
    /// temperature, into a container beyond the tip or onto the ground there; the winder's tongs pay
    /// <c>TongsWearPerRod</c>. Returns the springs delivered.</summary>
    public int WindBy(double radians)
    {
        if (!RodOn || !Complete)
            return 0;
        var heat = Heat(_rod!);
        if (!heat.Hot)
        {
            if (!_toldCold && _lastWinder != null && Api.World.PlayerByUid(_lastWinder) is { } winder)
                Error(winder, "error-rod-cooled", (int)heat.Temperature, (int)heat.Workable);
            _toldCold = true;
            return 0;
        }
        _toldCold = false;
        var before = _wind;
        (_wind, bool finished) = _wind.Advance(radians, Config.TurnsPerRod, heat.Hot);
        if (finished)
        {
            var spring = SpringItem();
            var winder = _lastWinder != null ? Api.World.PlayerByUid(_lastWinder) : null;
            ClearRod();
            if (spring != null)
            {
                var stack = new ItemStack(spring, Winding.SpringsPerRod);
                spring.SetTemperature(Api.World, stack, heat.Temperature);
                Deliver(stack);
            }
            if (winder != null && Config.TongsWearPerRod > 0 && winder.WorldData?.CurrentGameMode != EnumGameMode.Creative
                && TongsSlot(winder) is { Itemstack: { } tongs } slot)
            {
                tongs.Collectible.DamageItem(Api.World, winder.Entity, slot, Config.TongsWearPerRod);
                slot.MarkDirty();
            }
            MarkDirty(true);
            return Winding.SpringsPerRod;
        }
        if (Math.Floor(before.Work / WindSyncStep) != Math.Floor(_wind.Work / WindSyncStep))
            MarkDirty(false);
        return 0;
    }

    // ---- Forging ----

    /// <summary>A right-click with a hammer (server side): on a bare mandrel, takes a hollow from the
    /// infeed, else, with right-click held since a hollow was finished (<see cref="Forging.Held"/>),
    /// puts another of the same on from the player's hotbar once a blow would be struck
    /// (<see cref="RefillFromHotbar"/>); on a hollow, strikes a blow, no faster than
    /// <see cref="Forging.BlowIntervalMs"/>.</summary>
    public bool StrikeBy(IPlayer byPlayer, ItemSlot hammer)
    {
        long now = Api.World.ElapsedMilliseconds;
        bool held = byPlayer.PlayerUID == _lastClickBy && Forging.Held(now - _lastClickAt);
        _lastClickBy = byPlayer.PlayerUID;
        _lastClickAt = now;
        if (!Complete)
            return Error(byPlayer, "error-no-mandrel");
        if (!HollowOn)
        {
            if (PullFromInfeed())
                return true;
            if (held && _lastFinished != null)
            {
                if (!Forging.Ready(now - _lastBlowAt))
                    return false;
                if (RefillFromHotbar(byPlayer))
                    return true;
            }
            return Error(byPlayer, "error-no-hollow");
        }
        if (!Forging.Ready(now - _lastBlowAt))
            return false;
        _lastBlowAt = now;
        Blow(byPlayer, hammer);
        return true;
    }

    /// <summary>
    /// One blow of the hammer on the hollow (server side): W advances by the hammer's tool tier over
    /// the base tier, over the hollow's blows (<see cref="Forging.WorkPerBlow"/>; no hammer, or one with
    /// no tier, is the base), the anvil's sound and sparks at <c>strike.pos</c>, the hammer in
    /// <paramref name="hammer"/> (if any) loses its wear a blow, and at the blow that brings W to 1 the
    /// hollow is used up and its two pipe sections come off. Returns the sections delivered.
    /// </summary>
    public int Blow(IPlayer? byPlayer = null, ItemSlot? hammer = null)
    {
        if (!HollowOn || !Complete)
            return 0;
        int tier = hammer?.Itemstack?.Collectible?.ToolTier ?? 0;
        (_job, bool finished) = _job.Strike(Config.BlowsPerHollow(_job.Class), tier, Config.BaseHammerTier);
        var at = WorldPoint(Rig?.Strike ?? new Float3(0.5f, 0.75f, 1f));
        Api.World.PlaySoundAt(BlowSound, at.X, at.Y, at.Z, null, true, 16, 0.8f);
        Sparks(at);
        if (hammer?.Itemstack is { } stack && Config.HammerWearPerBlow > 0
            && byPlayer?.WorldData.CurrentGameMode != EnumGameMode.Creative)
        {
            stack.Collectible.DamageItem(Api.World, byPlayer?.Entity, hammer, Config.HammerWearPerBlow);
            hammer.MarkDirty();
        }
        if (finished)
        {
            int k = _job.Class;
            _lastFinished = _hollow?.Collectible?.Code?.ToString();
            ClearJob();
            if (SectionItem(k) is { } section)
                Deliver(new ItemStack(section, Forging.SectionsPerHollow));
            MarkDirty(true);
            return Forging.SectionsPerHollow;
        }
        MarkDirty(false);
        return 0;
    }

    // Small sparks off the blow, the anvil's colour.
    private void Sparks(Vec3d at)
    {
        var props = new SimpleParticleProperties(
            3, 6, ColorUtil.ToRgba(255, 255, 233, 83),
            at.AddCopy(-0.1, 0, -0.1), at.AddCopy(0.1, 0.05, 0.1),
            new Vec3f(-1.2f, 0.6f, -1.2f), new Vec3f(1.2f, 2.2f, 1.2f),
            0.12f, 1f, 0.1f, 0.2f, EnumParticleModel.Cube)
        {
            VertexFlags = 128,
            WithTerrainCollision = true,
        };
        Api.World.SpawnParticles(props);
    }

    /// <summary>Puts <paramref name="stack"/> into a container just beyond the output face, else
    /// drops it there.</summary>
    public void Deliver(ItemStack stack)
    {
        if (Rig is not { } rig)
            return;
        var dummy = new DummySlot(stack);
        if (Api.World.BlockAccessor.GetBlockEntity(CellPos(rig.OutputNeighbour())) is Vintagestory.GameContent.BlockEntityContainer container)
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
        Api.World.PlaySoundAt(SectionSound, at.X, at.Y, at.Z);
    }

    /// <summary>A hollow a container at the infeed face holds that the station would take.</summary>
    private IEnumerable<(Vintagestory.GameContent.BlockEntityContainer Container, ItemSlot Slot)> InfeedHollows()
    {
        if (Rig is not { } rig)
            yield break;
        foreach (var local in rig.InfeedNeighbours())
        {
            if (Api.World.BlockAccessor.GetBlockEntity(CellPos(local)) is not Vintagestory.GameContent.BlockEntityContainer container)
                continue;
            foreach (var slot in container.Inventory)
            {
                int k = Forging.ClassOfHollow(slot.Itemstack?.Collectible?.Code?.ToString());
                if (k != 0 && SectionItem(k) != null)
                    yield return (container, slot);
            }
        }
    }

    /// <summary>A station with a mandrel and nothing on it takes one hollow from a container at the
    /// infeed face (when struck). Returns whether one went on.</summary>
    public bool PullFromInfeed()
    {
        if (Api.Side != EnumAppSide.Server || WorkOn || !Complete)
            return false;
        if (InfeedHollows().FirstOrDefault() is not ({ } container, { } slot))
            return false;
        Load(slot.TakeOut(1));
        slot.MarkDirty();
        container.MarkDirty(true);
        return true;
    }

    // The client's own: the client removes a broken station at once, and an update the server sent
    // before it heard of the break then brings the block entity back over air: drop it.
    private void OnClientTick(float dt)
    {
        if (Api.World.BlockAccessor.GetBlock(Pos) is not BlockMandrelStation)
        {
            DisposeRenderer();
            Api.World.BlockAccessor.RemoveBlockEntity(Pos);
        }
    }

    // ---- Breaking ----

    /// <summary>What breaking the frame gives besides the frame: the mandrel, and the hollow if it has
    /// not been struck yet, or the rod if it has not been wound yet (once work has begun, it is lost with
    /// the station).</summary>
    public IEnumerable<ItemStack> PartDrops()
    {
        if (_mandrel != null && Api.World.GetItem(new AssetLocation(_mandrel)) is { } item)
            yield return new ItemStack(item);
        if (HollowOn && _job.Untouched)
            yield return _hollow!.Clone();
        if (RodOn && _wind.Untouched)
            yield return _rod!.Clone();
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
        if (_mandrel != null)
            tree.SetString("mandrel", _mandrel);
        else
            tree.RemoveAttribute("mandrel");
        if (HollowOn)
            tree.SetItemstack("hollow", _hollow);
        else
            tree.RemoveAttribute("hollow");
        tree.SetInt("class", _job.Class);
        tree.SetInt("blows", _job.Blows);
        tree.SetDouble("work", _job.Work);
        if (RodOn)
            tree.SetItemstack("rod", _rod);
        else
            tree.RemoveAttribute("rod");
        tree.SetDouble("wound", _wind.Work);
        tree.SetBool("held", _held);
        if (Api?.Side == EnumAppSide.Server)
        {
            tree.SetInt("needed", Config.BlowsPerHollow(_job.Class));
            tree.SetDouble("turnsPerRod", Config.TurnsPerRod);
        }
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);
        _mandrel = MandrelPart.Restore(tree.GetString("mandrel"));
        _hollow = tree.GetItemstack("hollow");
        if (_hollow != null && !_hollow.ResolveBlockOrItem(worldForResolving))
            _hollow = null;
        _job = _hollow == null || _mandrel == null ? ForgeJob.None : ForgeJob.Restore(tree.GetInt("class"), tree.GetInt("blows"), tree.GetDouble("work"));
        if (!_job.On)
            _hollow = null;
        _rod = tree.GetItemstack("rod");
        if (_rod != null && (!_rod.ResolveBlockOrItem(worldForResolving) || _job.On || _mandrel == null))
            _rod = null;
        _wind = WindJob.Restore(_rod != null, tree.GetDouble("wound"));
        if (worldForResolving.Side == EnumAppSide.Client)
        {
            _serverBlowsNeeded = tree.TryGetInt("needed") is > 0 and var n ? n : null;
            _serverTurnsPerRod = tree.TryGetDouble("turnsPerRod") is > 0 and var t ? t : null;
            _held = tree.GetBool("held") && RodOn;
        }
    }

    public override void OnStoreCollectibleMappings(Dictionary<int, AssetLocation> blockIdMapping, Dictionary<int, AssetLocation> itemIdMapping)
    {
        base.OnStoreCollectibleMappings(blockIdMapping, itemIdMapping);
        _hollow?.Collectible.OnStoreCollectibleMappings(Api.World, new DummySlot(_hollow), blockIdMapping, itemIdMapping);
        _rod?.Collectible.OnStoreCollectibleMappings(Api.World, new DummySlot(_rod), blockIdMapping, itemIdMapping);
    }

    public override void OnLoadCollectibleMappings(IWorldAccessor worldForResolve, Dictionary<int, AssetLocation> oldBlockIdMapping,
                                                   Dictionary<int, AssetLocation> oldItemIdMapping, int schematicSeed, bool resolveImports)
    {
        base.OnLoadCollectibleMappings(worldForResolve, oldBlockIdMapping, oldItemIdMapping, schematicSeed, resolveImports);
        _hollow?.FixMapping(oldBlockIdMapping, oldItemIdMapping, worldForResolve);
        _rod?.FixMapping(oldBlockIdMapping, oldItemIdMapping, worldForResolve);
    }

    // ---- Info ----

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
    {
        base.GetBlockInfo(forPlayer, dsc);
        string L(string key, params object[] args) => Lang.Get(MandrelStationSystem.Domain + ":mandrelstation-" + key, args);
        if (!Complete)
            dsc.AppendLine(L("info-next"));
        else if (HollowOn)
            dsc.AppendLine(L("info-hollow", L("metal-" + Forging.MetalOf(_job.Class)), _job.Blows, (int)Math.Floor(_job.Work * 100 + 1e-6)));
        else if (RodOn)
        {
            var heat = Heat(_rod!);
            dsc.AppendLine(L(heat.Hot ? "info-rod" : "info-rod-cold", (int)Math.Floor(_wind.Work * 100 + 1e-6), (int)heat.Temperature, (int)heat.Workable));
        }
        else
            dsc.AppendLine(L("info-empty"));
    }
}
