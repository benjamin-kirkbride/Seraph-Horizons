using System.Globalization;
using System.Text;
using SeraphHorizons.Mod.BuckingSawmill.Core;
using SeraphHorizons.Mod.Core;
using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.TrunkEntities;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace SeraphHorizons.Mod.BuckingSawmill;

/// <summary>
/// The mill's controller, on the shared machine base (<see cref="BEMachine"/>: its build from
/// <c>config/builds/buckingmill.json</c>, Immersive Woodworking's sawmill parts, with the blade kit its durable
/// wearing part; its cells and power cell; its oil (<see cref="OilMachine.BuckingMill"/>); its block info). It
/// keeps the loaded trunk as its whole Logging Expanded item stack, the cut's progress and the saws' depth.
/// The server cuts and winds the saws back up from the power cell's shaft angle, pulls trunks from a rack, a
/// feeder in line (the rosser, <see cref="ITrunkFeeder"/>) or the ground at the infeed side; the client
/// follows the saws between syncs and draws them (<see cref="MillRenderer"/>). Every cell's collision and
/// selection boxes, with the loaded trunk's (<see cref="TrunkBox"/>), come from here.
/// </summary>
public class BEBuckingMill : BEMachine
{
    public const string IwDomain = "immersivewoodworking";
    private static readonly AssetLocation BreakSound = new("game", "sounds/effect/toolbreak");
    private static readonly AssetLocation TrunkSound = new("game", "sounds/block/wood");

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
    private float _clientLastAngle;
    private bool _clientAngleSeeded;
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
    private volatile BoxTable[]? _boxes;
    private sealed record BoxTable(IReadOnlyDictionary<Int3, Cuboidf[]> Selection, IReadOnlyDictionary<Int3, Cuboidf[]> Collision);

    public BlockFacing Facing => BlockFacing.FromCode(Side.Code());
    public float Progress => _progress;
    /// <summary>The saws' depth, 0 at the top to 1 at the bed (server's value on the client).</summary>
    public float Depth => _depth;
    /// <summary>Whether the saws are being wound up (server's value on the client).</summary>
    public bool Rising => _rising;
    /// <summary>Assembled with its blade kit and turning at <see cref="BEMachine.MinSpeed"/> or faster: the cycle runs.</summary>
    public bool SawsRun => Ready && ShaftSpeed >= MinSpeed;
    /// <summary>What the rack at the infeed end offers, as the server last found it.</summary>
    public RackState RackState => _rackState;

    /// <summary>The fitted blade kit: its code and durability left; null without one (taken out, or worn out).</summary>
    public (string Code, double Left)? Blade => Assembly?.Wearing.Fitted(BladeKits.Part);

    /// <summary>The fitted blade kit as its item stack, with the durability it has left; null without one.</summary>
    public ItemStack? BladeKit => Blade is { } b && Api != null ? StackOf(new StageDrop(b.Code, 1, b.Left)) : null;

    private float RevolutionsPerStoredLog => _serverRevolutions ?? Config.RevolutionsPerStoredLog;
    private float RaiseRevolutions => _serverRaiseRevolutions ?? Config.RaiseRevolutions;
    /// <summary>How fast the fitted blade kit cuts, a multiple of a copper kit's speed, from its tool
    /// tier (<see cref="BuckingSawmillSystem.BladeSpeed"/>); 1 without a kit. A client uses the
    /// server's figure.</summary>
    public float BladeSpeed => _serverBladeSpeed ?? (BladeKit is { } kit ? System.BladeSpeed(kit) : 1f);

    private BuckingSawmillSystem System => BuckingSawmillSystem.Of(Api);
    private MillConfig Config => System.Config;
    private new Rig? Rig => System.Rig;

    // What the renderer draws
    public int SashCount => (ShowsPart("sash1") ? 1 : 0) + (ShowsPart("sash2") ? 1 : 0);
    public bool HasCrankshaft => Assembly?.Build.Fitted("crankshaft") == true;
    public bool HasLevers => Assembly?.Build.Fitted("levers") == true;
    public bool HasBladeKit => Blade != null;
    public string? BladeMetal => BladeKits.MetalOf(Blade?.Code);
    public ItemStack? Trunk => _trunk;
    public float ClientProgress => _trunk == null ? 0 : Math.Clamp(_clientProgress, 0, 1);
    public MillPhase Phase => SawDepth.Phase(SawsRun, _trunk != null, _rising);
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

    // ---- The machine base ----

    /// <summary>The blade kit's uses are each kit's own durability, which differs by metal.</summary>
    protected override MachineBuild? BuildSpec(IWorldAccessor world) =>
        base.BuildSpec(world)?.WithUses(BladeKits.Part, code =>
            world.GetItem(new AssetLocation(code)) is { } kit ? kit.GetMaxDurability(new ItemStack(kit)) : 0);

    protected override OilMachine? OilKind => OilMachine.BuckingMill;
    protected override float ConfigMinSpeed => Config.MinSpeed;
    protected override int TickMs => 50;
    protected override int ClientTickMs => 50;

    /// <summary>Whole with its blade kit in: until then it is the frame, puts the token load on its shaft and
    /// takes the creative shortcut (the woodworking stations', with <c>UnifiedWoodworking</c>: a steel kit last).</summary>
    public override bool Complete => Ready;
    protected override bool LoadsShaft => Ready;
    protected override bool CreativeFills => !Ready && WoodworkingShortcut;
    protected override double ShaftLoad => Config.Resistance;
    protected override Float3? SmokePoint => new Float3(0.5f, 1.2f, 0.5f);

    /// <summary>The cycle runs with the bed empty too (the saws sink and are wound up); with a trunk on it only
    /// while Logging Expanded is as the mill needs it.</summary>
    public override bool HasWork => _trunk == null || System.Logging != null;
    public override double? WorkProgress => _trunk != null ? _progress : null;

    /// <summary>Whether the creative shortcut is on: with <c>UnifiedWoodworking</c>, whose creative
    /// shortcut on the woodworking stations this is (<see cref="CreativeUpgrades"/>), running.</summary>
    public bool WoodworkingShortcut => Api?.ModLoader.GetModSystem<SeraphHorizonsSystem>()?.Woodworking is { Active: true };

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        _boxes = BuildBoxes();
        if (api.Side == EnumAppSide.Server)
            RegisterGameTickListener(OnRackTick, 1000);
        else
        {
            _clientProgress = _progress;
            _clientDepthEstimate = _clientDepth = _clientDepthBefore = _depth;
            _clientRising = _rising;
        }
    }

    protected override IRenderer CreateRenderer(ICoreClientAPI capi, IMachineRig rig) => new MillRenderer(capi, this, (Rig)rig);

    // ---- Footprint ----

    /// <summary>A cell's selection boxes, turned to the mill's facing, with the loaded trunk's part
    /// in that cell; null when the cell is not the mill's. Safe off the main thread: the tables are
    /// built whole in <see cref="Initialize"/> and only read here.</summary>
    public override Cuboidf[]? CellBoxes(BlockPos cellPos) => Table(cellPos, collision: false);

    /// <summary>A cell's collision boxes: its <see cref="CellBoxes"/> and, on a column's top cell,
    /// the rig's collision-only lid, so nothing falls into the mill from above.</summary>
    public override Cuboidf[]? CollisionBoxes(BlockPos cellPos) => Table(cellPos, collision: true);

    private Cuboidf[]? Table(BlockPos cellPos, bool collision)
    {
        var tables = _boxes ??= BuildBoxes();
        if (tables == null)
            return null;
        var table = tables[(int)_trunkClass];
        return (collision ? table.Collision : table.Selection).GetValueOrDefault(LocalOf(cellPos));
    }

    /// <summary>Every cell's boxes, once per trunk class; null before the rig is known.</summary>
    private BoxTable[]? BuildBoxes()
    {
        if (Api == null || Rig is not { } rig || !Sides.TryParse(Block?.Variant["side"], out var side))
            return null;
        Cuboidf World(Box b)
        {
            var w = Footprint.ToWorld(b, side);
            return new Cuboidf(w.X1, w.Y1, w.Z1, w.X2, w.Y2, w.Z2);
        }
        var tables = new BoxTable[3];
        foreach (var trunk in new[] { TrunkClass.None, TrunkClass.Thin, TrunkClass.Thick })
        {
            var trunkBoxes = trunk == TrunkClass.None || rig.TrunkBed is not { } bed
                ? new Dictionary<Int3, Box>()
                : TrunkBox.CellBoxes(rig.Cells.Select(c => c.Pos), TrunkBox.Bounds(bed, trunk));
            var selection = rig.Cells.ToDictionary(c => c.Pos, c =>
            {
                var own = c.Boxes.Count == 0 ? [Cuboidf.Default()] : c.Boxes.Select(World);
                return (trunkBoxes.TryGetValue(c.Pos, out var t) ? own.Append(World(t)) : own).ToArray();
            });
            var collision = rig.Cells.ToDictionary(c => c.Pos, c =>
                c.LidBox is { } lid ? [.. selection[c.Pos], World(lid)] : selection[c.Pos]);
            tables[(int)trunk] = new BoxTable(selection, collision);
        }
        return tables;
    }

    /// <summary>Whether a click on cell <paramref name="cellPos"/> at <paramref name="hit"/> (the
    /// selection's hit point, relative to that cell) is on the loaded trunk.</summary>
    public bool HitsTrunk(BlockPos cellPos, Vec3d? hit)
    {
        if (hit == null || _trunkClass == TrunkClass.None || Rig?.TrunkBed is not { } bed)
            return false;
        var local = MachinePoints.NativeHit(Pos, cellPos, hit, Side);
        return TrunkBox.Contains(TrunkBox.Bounds(bed, _trunkClass), local);
    }

    // ---- Interaction ----

    /// <summary>Before the build's: Ctrl takes the trunk back (into Carry On's hands with trunk entities), else
    /// the blade kit, with what durability it has left (the creative shortcut comes first while a part is still
    /// to fit); a blade kit is fitted once both sashes are in, before or after the crankshaft and levers.</summary>
    protected override bool? InteractContent(IPlayer byPlayer, ItemSlot slot, BlockSelection blockSel)
    {
        if (CreativeShortcut(byPlayer))
            return null;
        var controls = byPlayer.Entity.Controls;
        if (controls.CtrlKey && !controls.ShiftKey)
        {
            if (Api.Side == EnumAppSide.Server && !TryTakeTrunk(byPlayer))
                TryTakeBladeKit(byPlayer);
            return true;
        }
        if (BladeKits.MetalOf(slot?.Itemstack?.Collectible?.Code?.ToString()) == null)
            return null;
        if (Api.Side == EnumAppSide.Server)
            TryFitBlade(slot!, byPlayer);
        return true;
    }

    /// <summary>After the build's: with an empty hand the trunk carried in Carry On's hands (with trunk entities),
    /// else a trunk in hand or one from the hotbar or backpack, is loaded; a stopped mill's saws, if they are down,
    /// are wound up by holding right-click with an empty hand. Anything else held is the item's own business,
    /// except on the trunk, where the click is the mill's and does nothing (a block would be placed inside the
    /// trunk).</summary>
    protected override bool InteractWork(IPlayer byPlayer, ItemSlot slot, BlockSelection blockSel)
    {
        bool trunk = Trunks.IsTrunk(slot.Itemstack);
        if (!trunk && slot.Itemstack != null)
            return HitsTrunk(blockSel.Position, blockSel.HitPosition);
        if (Api.Side != EnumAppSide.Server)
            return true;
        if (!trunk && Feeding.WindsUp(SawsRun, _depth))
            StartHold(byPlayer, HoldKind.Wind);
        else if (trunk ? !TryLoadFromSlot(slot, byPlayer) : !TryLoadFromInventory(byPlayer))
        {
            // Not now: if only because the saws are not up yet, it goes on at the next top while
            // the button is held.
            if (WaitsForTop(byPlayer))
                StartHold(byPlayer, HoldKind.Load);
        }
        return true;
    }

    /// <summary>Fits what <paramref name="slot"/> holds (server side), as a right-click holding it does: a blade
    /// kit by the mill's rule, any other part into the build. The part is taken from the slot unless the player
    /// is in creative mode or it is <paramref name="free"/>. False, with an error to the player, when it does
    /// not go in.</summary>
    public bool TryFitPart(ItemSlot slot, IPlayer byPlayer, bool free = false)
    {
        if (BladeKits.MetalOf(slot.Itemstack?.Collectible?.Code?.ToString()) != null)
            return TryFitBlade(slot, byPlayer, free);
        if (free)
            slot = new DummySlot(slot.Itemstack?.Clone());
        bool fitted = TryFit(slot, byPlayer) == true;
        RefreshState();
        return fitted;
    }

    /// <summary>Fits the blade kit in <paramref name="slot"/> (server side): one kit, a blade in each saw, so it
    /// needs both sashes; it goes in with the durability it has left. False, with an error to the player, when
    /// one is already in or the sashes are not.</summary>
    public bool TryFitBlade(ItemSlot slot, IPlayer? byPlayer, bool free = false)
    {
        if (Assembly is not { } assembly || slot.Itemstack is not { } stack || stack.Collectible?.Code?.ToString() is not { } code)
            return false;
        double? uses = stack.Collectible.GetMaxDurability(stack) > 0 ? stack.Collectible.GetRemainingDurability(stack) : null;
        switch (BladeKits.Fit(assembly, code, uses))
        {
            case BladeVerdict.Fits:
                break;
            case BladeVerdict.BladeFitted:
                return Refuse(byPlayer, "error-blade-fitted");
            case BladeVerdict.NeedsSashes:
                return Refuse(byPlayer, "error-need-sashes");
            default:
                return Refuse(byPlayer, "error-not-a-part");
        }
        if (!free && byPlayer?.WorldData.CurrentGameMode != EnumGameMode.Creative)
        {
            slot.TakeOut(1);
            slot.MarkDirty();
        }
        Api.World.PlaySoundAt(Block.Sounds.Place, Pos, -0.25, byPlayer);
        MarkDirty(true);
        RefreshState();
        return true;
    }

    /// <summary>The creative shortcut (server side): fits the next stage (every part still missing), then a
    /// steel blade kit, with nothing taken from the player, whatever they hold.</summary>
    public bool FitNextPart(IPlayer byPlayer)
    {
        if (Assembly is not { } assembly || Ready || !assembly.FillNext())
            return false;
        Api.World.PlaySoundAt(Block.Sounds.Place, Pos, -0.25, byPlayer);
        MarkDirty(true);
        RefreshState();
        return true;
    }

    /// <summary>Whether a trunk refused only because the saws are not at the top can wait for them:
    /// the mill is running, its bed is empty and the player has a trunk for it.</summary>
    private bool WaitsForTop(IPlayer byPlayer) =>
        SawsRun && _trunk == null && !SawDepth.AtTop(_depth) && System.Logging != null
        && (TrunkStations.Hands(Api) ? TrunkStations.Carried(Api, byPlayer) != null : FindTrunk(byPlayer) != null);

    private void StartHold(IPlayer byPlayer, HoldKind kind) =>
        _holds[byPlayer.PlayerUID] = new Hold(byPlayer, kind, Api.World.ElapsedMilliseconds);

    /// <summary>Right-click held on the mill (both sides): whether the hold goes on. The client keeps
    /// it while there is something to wait for, a trunk to load or saws to wind up; the server, while
    /// it has the hold.</summary>
    public override bool OnInteractStep(IPlayer byPlayer, float secondsUsed)
    {
        if (Api.Side == EnumAppSide.Server)
            return _holds.ContainsKey(byPlayer.PlayerUID);
        var held = byPlayer.InventoryManager.ActiveHotbarSlot?.Itemstack;
        if (held == null && Feeding.KeepsWinding(SawsRun, SideDepth))
            return true;
        return _trunk == null && SawsRun && (held == null || Trunks.IsTrunk(held));
    }

    /// <summary>Right-click let go, or the hold cancelled (both sides): the server forgets it.</summary>
    public override void OnInteractStop(IPlayer byPlayer)
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
            if (!Feeding.KeepsWinding(SawsRun, _depth))
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
            if (TrunkStations.Hands(Api) ? !TryLoadFromHands(hold.Player, null, passedTop)
                : FindTrunk(hold.Player) is not { } slot || !TryLoadFromSlot(slot, null, passedTop))
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

    /// <summary>Gives the trunk back if it is still recoverable (server side).</summary>
    public bool TryTakeTrunk(IPlayer byPlayer)
    {
        if (_trunk == null)
            return false;
        if (!Cutting.Recoverable(_progress))
        {
            Refuse(byPlayer, "error-trunk-cut");
            return true;
        }
        var trunk = _trunk;
        if (!GiveTrunk(byPlayer, trunk))
            return true;   // hands full: it stays
        ClearTrunk();
        Api.World.PlaySoundAt(TrunkSound, Pos, 0, byPlayer);
        MarkDirty(true);
        return true;
    }

    /// <summary>Gives back the blade kit, with the durability it has left (server side).</summary>
    public bool TryTakeBladeKit(IPlayer byPlayer)
    {
        if (Assembly?.Take(BladeKits.Part) is not { } blade)
            return false;
        if (StackOf(new StageDrop(blade.Code, 1, blade.Left)) is { } stack)
            Give(byPlayer, stack);
        MarkDirty(true);
        RefreshState();
        return true;
    }

    public bool TryLoadFromSlot(ItemSlot slot, IPlayer? byPlayer, bool passedTop = false)
    {
        if (slot.Itemstack is not { } stack || !CanLoad(stack, byPlayer, passedTop))
            return false;
        Load(slot.TakeOutWhole());
        slot.MarkDirty();
        Api.World.PlaySoundAt(TrunkSound, Pos, 0, byPlayer);
        return true;
    }

    /// <summary>Loads the trunk the player carries in Carry On's hands, while trunks go through
    /// hands (<see cref="TrunkStations.Hands"/>); else the first trunk in the player's hotbar, then
    /// backpack, as Logging Expanded's workstations find one (branched trunks are skipped while
    /// Logging Expanded requires them debranched).</summary>
    public bool TryLoadFromInventory(IPlayer byPlayer)
    {
        if (TrunkStations.Hands(Api))
            return TryLoadFromHands(byPlayer, byPlayer);
        if (FindTrunk(byPlayer, branchedToo: true) is { } slot)
            return TryLoadFromSlot(slot, byPlayer);
        return Refuse(byPlayer, "error-no-trunk");
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

    /// <summary>Loads the trunk <paramref name="player"/> carries in Carry On's hands, if the mill
    /// takes it now (or this tick, the saws having <paramref name="passedTop"/>); why not goes to
    /// <paramref name="errorsTo"/> (server side).</summary>
    public bool TryLoadFromHands(IPlayer player, IPlayer? errorsTo, bool passedTop = false)
    {
        if (TrunkStations.Carried(Api, player) is not { } carried)
            return Refuse(errorsTo, "error-no-trunk");
        if (!CanLoad(carried, errorsTo, passedTop) || player is not IServerPlayer sp || TrunkCarry.Take(sp) is not { } trunk)
            return false;
        Load(trunk);
        Api.World.PlaySoundAt(TrunkSound, Pos, 0, player);
        return true;
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
            return Refuse(byPlayer, "error-no-logging");
        if (!Ready)
            return Refuse(byPlayer, "error-incomplete");
        if (_trunk != null)
            return Refuse(byPlayer, "error-bed-full");
        if (BranchedAndRefused(trunk))
        {
            // Logging Expanded's own message, as its sawhorses give it.
            if (byPlayer is IServerPlayer sp)
                sp.SendIngameError("", Lang.GetL(sp.LanguageCode, "loggingmod:treetrunk-branches-first"));
            return false;
        }
        if (!Feeding.CanTakeTrunk(_depth, passedTop))
            return Refuse(byPlayer, SawsRun ? "error-saws-not-up" : "error-saws-down");
        if (Trunks.StoredLogs(trunk, Api.World) <= 0)
            return Refuse(byPlayer, "error-empty-trunk");
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
        RefreshState();
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

    /// <summary>Gives a trunk taken off the mill to the player (server side): into Carry On's hands
    /// while trunks go through hands, false with the hands-full error when they are full; with
    /// trunk entities but no Carry On, a trunk entity beyond the infeed end; else to the inventory
    /// as any item.</summary>
    private bool GiveTrunk(IPlayer byPlayer, ItemStack trunk)
    {
        if (TrunkStations.Hands(Api))
            return byPlayer is IServerPlayer sp && TrunkStations.GiveToHands(sp, trunk);
        if (TrunkEntitySystem.Of(Api).Enabled && Rig is { } rig)
        {
            var outward = Footprint.ToWorld(rig.InfeedSide, Side).Normal();
            if (TrunkStations.DropBeyond(Api.World, trunk, rig.InfeedNeighbours().Select(CellPos).ToList(), outward.X, outward.Z) != null)
                return true;
        }
        Give(byPlayer, trunk);
        return true;
    }

    private void Give(IPlayer byPlayer, ItemStack stack)
    {
        if (!byPlayer.InventoryManager.TryGiveItemstack(stack, true))
            Api.World.SpawnItemEntity(stack, Pos.ToVec3d().Add(0.5, 0.6, 0.5));
    }

    /// <summary>The mill's own message (<c>seraphhorizons:buckingmill-{key}</c>) to the player; false.</summary>
    private bool Refuse(IPlayer? byPlayer, string key, params object[] args)
    {
        if (byPlayer is IServerPlayer sp)
            sp.SendIngameError("buckingsawmill-" + key, Lang.GetL(sp.LanguageCode, BuckingSawmillSystem.Domain + ":buckingmill-" + key, args));
        return false;
    }

    /// <summary>The help: the base's (the parts still to fit, a blade kit once worn out), a blade kit once both
    /// sashes are in, a trunk to load, winding the saws up, and Ctrl for the trunk or the blade kit.</summary>
    public override WorldInteraction[] InteractionHelp(IPlayer forPlayer)
    {
        var help = base.InteractionHelp(forPlayer).ToList();
        string Key(string action) => BuckingSawmillSystem.Domain + ":blockhelp-buckingmill-" + action;
        if (!Built && Blade == null && SashCount == 2 && Stacks(Api.World, [BladeKits.Prefix + "*"], 1) is { Length: > 0 } kits)
            help.Add(new WorldInteraction { ActionLangCode = Key("fitblades"), MouseButton = EnumMouseButton.Right, Itemstacks = kits });
        if (!Built)
            AddCommonHelp(help, Key);
        return help.ToArray();
    }

    protected override void WorkHelp(IPlayer? forPlayer, List<WorldInteraction> help)
    {
        string Key(string action) => BuckingSawmillSystem.Domain + ":blockhelp-buckingmill-" + action;
        if (Ready && _trunk == null && System.TrunkStacks() is { Length: > 0 } trunks)
            help.Add(new WorldInteraction { ActionLangCode = Key("loadtrunk"), MouseButton = EnumMouseButton.Right, Itemstacks = trunks });
        AddCommonHelp(help, Key);
    }

    private void AddCommonHelp(List<WorldInteraction> help, System.Func<string, string> key)
    {
        // a stopped mill's saws, if they are down, are wound up by hand
        if (SashCount > 0 && Feeding.WindsUp(SawsRun, SideDepth))
            help.Add(new WorldInteraction { ActionLangCode = key("windup"), MouseButton = EnumMouseButton.Right, RequireFreeHand = true });
        if (_trunk != null && Cutting.Recoverable(_progress))
            help.Add(new WorldInteraction { ActionLangCode = key("taketrunk"), MouseButton = EnumMouseButton.Right, HotKeyCode = "ctrl" });
        else if (_trunk == null && Blade != null)
            help.Add(new WorldInteraction { ActionLangCode = key("removeblade"), MouseButton = EnumMouseButton.Right, HotKeyCode = "ctrl" });
    }

    // ---- Rack ----

    /// <summary>An assembled, empty mill with its saws at the top of their cycle, turning fast
    /// enough, takes the next trunk from a Trunk Storage Rack touching its infeed end at ground
    /// level, or the finished trunk of a feeder in line there (the rosser), or failing both a trunk
    /// entity lying in those cells, unless that trunk is branched (it waits for the player to
    /// debranch it). Tried every tick the saws are at (or pass) the top, and once a second besides;
    /// the rack or feeder is looked up every time.</summary>
    private void OnRackTick(float dt)
    {
        PullFromRack();
        var state = CheckRack(out _);
        if (state != _rackState)
        {
            _rackState = state;
            MarkDirty();
        }
    }

    /// <summary>Takes the next trunk at the infeed end when the mill can (see <see cref="OnRackTick"/>):
    /// a rack's or feeder's first, else a trunk entity lying in the infeed cells
    /// (<see cref="PullFromGround"/>). Returns whether one went on.</summary>
    public bool PullFromRack(bool passedTop = false)
    {
        if (_trunk != null || !Feeding.CanTakeTrunk(_depth, passedTop) || !Ready || ShaftSpeed < Config.MinSpeed
            || System.Logging is not { } logging)
            return false;
        if (!Feeding.Pulls(CheckRack(out var ready)))
            return PullFromGround(passedTop);
        var trunk = ready switch
        {
            ITrunkFeeder feeder => feeder.TakeFinished(),
            BlockEntity rack => logging.PopTrunk(rack),
            _ => null,
        };
        if (trunk == null)
            return false;
        // A feeder saves and syncs itself.
        if (ready is BlockEntity rackEntity and not ITrunkFeeder)
            rackEntity.MarkDirty(true);
        Load(trunk);
        _rackState = CheckRack(out _);
        return true;
    }

    /// <summary>A trunk entity lying in the infeed cells (<see cref="Rig.InfeedNeighbours"/>) goes
    /// on under a rack trunk's rules: while <c>AutoPullFromRack</c> is on, not branched while
    /// Logging Expanded requires debranching, holding logs, and with the mill able to take a trunk
    /// now (<see cref="CanLoad"/>). Server side; returns whether one went on.</summary>
    public bool PullFromGround(bool passedTop = false)
    {
        if (_trunk != null || !Config.AutoPullFromRack || Rig is not { } rig || !TrunkEntitySystem.Of(Api).Enabled)
            return false;
        var cells = rig.InfeedNeighbours().Select(CellPos).ToList();
        if (TrunkStations.FindInCells(Api.World, cells, t => CanLoad(t, null, passedTop)) is not { } entity
            || TrunkStations.TakeEntity(entity) is not { } trunk)
            return false;
        Load(trunk);
        Api.World.PlaySoundAt(TrunkSound, Pos, 0);
        return true;
    }

    // ---- Hauling (the eidolon's haul order, Eidolon/Game/HaulOrder.cs) ----

    /// <summary>The ground cells just beyond the infeed end, where a trunk entity lying is taken
    /// (<see cref="PullFromGround"/>), and the way out of the machine from them; null without a rig.</summary>
    public (List<BlockPos> Cells, int OutwardX, int OutwardZ)? InfeedCells()
    {
        if (Rig is not { } rig)
            return null;
        var outward = Footprint.ToWorld(rig.InfeedSide, Side).Normal();
        return (rig.InfeedNeighbours().Select(CellPos).ToList(), outward.X, outward.Z);
    }

    /// <summary>Whether it takes <paramref name="trunk"/> at all, whatever it is doing now: a trunk
    /// with logs, not branched while Logging Expanded requires debranching.</summary>
    public bool Takes(ItemStack trunk) =>
        Trunks.IsTrunk(trunk) && !BranchedAndRefused(trunk) && Api != null && Trunks.StoredLogs(trunk, Api.World) > 0;

    /// <summary>What the racks, or a feeder in line, at the infeed end offer (server side): the
    /// first that has a trunk the mill takes is <paramref name="ready"/> (the rack's block entity or
    /// the <see cref="ITrunkFeeder"/>); otherwise the most telling reason it has none. A rack counts
    /// when its controller or filler cell is one of the ground cells just outside the infeed end
    /// (<see cref="Rig.InfeedNeighbours"/>), however it is turned. A feeder counts when one of its
    /// outfeed cells is one of them (its controller's or a cell's) and it faces the mill's way.
    /// AutoPullFromRack covers both.</summary>
    public RackState CheckRack(out object? ready)
    {
        ready = null;
        if (!Config.AutoPullFromRack)
            return RackState.Off;
        if (System.Logging is not { } logging)
            return RackState.NoLogging;
        if (Rig is not { } rig)
            return RackState.None;
        var best = RackState.None;
        var blockAccessor = Api.World.BlockAccessor;
        foreach (var local in rig.InfeedNeighbours())
        {
            var cell = CellPos(local);
            RackState state;
            object source;
            if (TrunkFeeders.Find(blockAccessor, cell) is { } feeder && feeder.Side == Side && feeder.HasOutfeedCell(cell))
            {
                var finished = feeder.PeekFinished();
                bool isTrunk = Trunks.IsTrunk(finished);
                state = Feeding.FeederOffer(isTrunk, feeder.Busy, isTrunk && BranchedAndRefused(finished!),
                                            isTrunk ? Trunks.StoredLogs(finished!, Api.World) : 0);
                source = feeder;
            }
            else if (logging.FindRack(blockAccessor, cell) is { } rack)
            {
                var top = logging.PeekTrunk(rack);
                bool isTrunk = Trunks.IsTrunk(top);
                bool branched = isTrunk && BranchedAndRefused(top!);
                state = Feeding.RackOffer(isTrunk, branched, isTrunk && !branched ? Trunks.StoredLogs(top!, Api.World) : 0);
                source = rack;
            }
            else
                continue;
            if (Feeding.Pulls(state))
            {
                ready = source;
                return state;
            }
            best = Feeding.MoreTelling(best, state);
        }
        return best;
    }

    // ---- Cutting ----

    /// <summary>Stopped (unbuilt, worn out, too slow, or a trunk Logging Expanded cannot cut), the holds still
    /// run: a hand winds the saws up, and holds lapse. Turning, they run with the cycle (<see cref="Work"/>).</summary>
    protected override bool Tick(float dt)
    {
        if (!(Ready && HasWork && MachineRun.Turning(ShaftSpeed, ConfigMinSpeed)))
            RunHolds(dt, false);
        return false;
    }

    // The cycle syncs itself as it goes (every 5% of the travel or the cut), so the base's full sync is not asked for.
    protected override bool Work(double radians)
    {
        Turn((float)radians, TickMs / 1000f);
        return false;
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
        // A debarked trunk (the Rosser switch's) cuts into its wood's debarked log.
        var log = Trunks.Wood(trunk, Api.World) is { } wood && System.Logging is { } logging
                  && (Trunks.IsDebarked(trunk) ? logging.DebarkedLogCode(wood) : logging.PlacedLogCode(wood)) is { } code
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
        // a stored log's oil, rounded up a trunk (OilDrain.PerTrunk), as jobs of the setting's drain
        if (Oiling is { } oil && oil.DrainPerJob(Api) is > 0 and var perLog)
            DrainOil(OilDrain.PerTrunk(stored, perLog) / perLog);
        MarkDirty(true);
    }

    private (Vec3d At, Vec3d Velocity) OutputPoint()
    {
        var rig = Rig!;
        var n = Footprint.ToWorld(rig.OutputSide, Side).Normal();
        return (MachinePoints.WorldPoint(Pos, rig.OutputPos, Side), new Vec3d(n.X * 0.05, 0, n.Z * 0.05));
    }

    /// <summary>Wears the blade kit by <paramref name="wear"/>; one worn to 0 breaks, and the mill
    /// stops until a new one is fitted.</summary>
    private void WearBlade(int wear)
    {
        if (wear <= 0 || Blade is not { } blade
            || Assembly!.Wearing.Parts.First(p => p.Id == BladeKits.Part).UsesFor(blade.Code) <= 1)
            return;
        if (Assembly.Wearing.Wear(BladeKits.Part, wear))
            Api.World.PlaySoundAt(BreakSound, Pos.X + 0.5, Pos.Y + 0.5, Pos.Z + 0.5);
    }

    // The client turns its own copies of the cycle with the shaft between syncs, by the same
    // arithmetic. The shown depth follows those local steps directly and eases toward the estimate
    // when a sync moves it, so a new trunk's drop is quick rather than a snap.
    protected override void OnClientTick(float dt)
    {
        float angle = ShaftAngle, speed = ShaftSpeed;
        float advance = 0;
        if (Ready && speed >= MinSpeed && _clientAngleSeeded)
            advance = ShaftClock.AngleAdvance(_clientLastAngle, angle, speed, dt);
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

    /// <summary>What breaking the frame gives besides the frame: every fitted part, the blade kit with what it
    /// has left, and the trunk while it is recoverable.</summary>
    public override IEnumerable<ItemStack> AssemblyDrops()
    {
        foreach (var stack in base.AssemblyDrops())
            yield return stack;
        if (_trunk != null && Cutting.Recoverable(_progress))
            yield return _trunk.Clone();
    }

    // ---- Saving and syncing ----

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
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
            tree.SetFloat("revolutionsPerStoredLog", Config.RevolutionsPerStoredLog);
            tree.SetFloat("raiseRevolutions", Config.RaiseRevolutions);
            tree.SetFloat("bladeSpeed", BladeSpeed);
            tree.SetInt("rackState", (int)_rackState);
        }
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);
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
        _trunk?.Collectible.OnStoreCollectibleMappings(Api.World, new DummySlot(_trunk), blockIdMapping, itemIdMapping);
    }

    public override void OnLoadCollectibleMappings(IWorldAccessor worldForResolve, Dictionary<int, AssetLocation> oldBlockIdMapping,
                                                   Dictionary<int, AssetLocation> oldItemIdMapping, int schematicSeed, bool resolveImports)
    {
        base.OnLoadCollectibleMappings(worldForResolve, oldBlockIdMapping, oldItemIdMapping, schematicSeed, resolveImports);
        _trunk?.FixMapping(oldBlockIdMapping, oldItemIdMapping, worldForResolve);
    }

    // ---- Info ----

    private static string L(string key, params object[] args) => Lang.Get(BuckingSawmillSystem.Domain + ":buckingmill-" + key, args);

    /// <summary>The mill's own lines: the blade kit while the parts are still going in (the wearing line says it
    /// once they are all in), winding the saws up, where the saws are and the trunk on the bed, and why the
    /// rack's trunks are, or are not, coming.</summary>
    protected override void AppendContents(StringBuilder dsc)
    {
        if (!Built)
        {
            if (BladeKit is { } blade)
                dsc.AppendLine(L("info-blade", blade.GetName(), Math.Round(100 * Assembly!.Wearing.ShareLeft(BladeKits.Part)).ToString(CultureInfo.InvariantCulture)));
            else
                dsc.AppendLine(L("info-needs-blade"));
        }
        // How far through the travel the saws are: the client's eased depth, the server's own on the server.
        float depth = SideDepth;
        if (SashCount > 0 && Feeding.WindsUp(SawsRun, depth))
            dsc.AppendLine(L("info-windup"));
        if (!Ready)
            return;
        switch (Phase)
        {
            case MillPhase.Cutting:
                dsc.AppendLine(L("info-trunk", _trunk!.GetName(), _storedLogs));
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
                if (_trunk != null)
                    dsc.AppendLine(L("info-trunk", _trunk.GetName(), _storedLogs));
                break;
        }
        if (_trunk == null && SawDepth.AtTop(depth))
            dsc.AppendLine(L("info-attop"));
        else if (_trunk == null && SawsRun)
            dsc.AppendLine(L("info-hold-to-load"));
        // Why the rack's trunks are, or are not, coming.
        if (_trunk == null && _rackState != RackState.Unknown)
            dsc.AppendLine(L("info-rack-" + _rackState.ToString().ToLowerInvariant()));
    }

    /// <summary>With a wrench: how fast the blade kit cuts, and the cut in shaft turns.</summary>
    protected override void AppendDetail(StringBuilder dsc)
    {
        if (Blade != null)
            dsc.AppendLine(L("info-blade-speed", BladeSpeed.ToString("0.##", CultureInfo.InvariantCulture)));
        if (_trunk != null && _storedLogs > 0)
        {
            double turns = _storedLogs * Cutting.CutRevolutions(RevolutionsPerStoredLog, BladeSpeed);
            dsc.AppendLine(L("info-cut-turns", (ClientProgress * turns).ToString("0.#", CultureInfo.InvariantCulture),
                turns.ToString("0.#", CultureInfo.InvariantCulture)));
        }
    }
}
