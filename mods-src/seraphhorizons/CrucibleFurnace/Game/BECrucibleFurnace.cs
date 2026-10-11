using System.Text;
using SeraphHorizons.Mod.Core;
using SeraphHorizons.Mod.CrucibleFurnace.Core;
using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Machines.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.CrucibleFurnace;

/// <summary>
/// The crucible furnace's controller (#796, README "Crucible furnace"), the front middle cell, the ash
/// mouth's. Holds the build (<see cref="FurnaceBuild"/>: every fitted item and the brick type), the two
/// pots (<see cref="FurnacePot"/>), the one coke bed and its fire (<see cref="HoleHeat"/>), the lid, the
/// ash-pit damper and the outlet's cap. The server ticks it each second: it finds the furnace's row and
/// chimney (<see cref="FurnaceRow"/>), sets the draught by the chimney's height, advances the fire by
/// the calendar's hours since the last tick (<see cref="FurnaceHeat"/>) and melts each pot whose
/// recipe's temperature the fire has reached. A player works it by right-clicks on any of its cells
/// (<see cref="OnInteract"/>) and lights it as a forge is lit (<see cref="BlockCrucibleFurnace"/>,
/// <see cref="IIgnitable"/>); the client draws it (<see cref="CrucibleFurnaceRenderer"/>).
/// </summary>
public class BECrucibleFurnace : BlockEntity
{
    private static readonly AssetLocation LidSound = new("game", "sounds/block/hatch");
    private static readonly AssetLocation DamperSound = new("game", "sounds/effect/latch");
    private static readonly AssetLocation CokeSound = new("game", "sounds/block/charcoal");
    private static readonly AssetLocation PotSound = new("game", "sounds/block/ceramicplace");
    private static readonly AssetLocation ChargeSound = new("game", "sounds/block/ingot");
    private static readonly AssetLocation CrackSound = new("game", "sounds/block/ceramicbreak");
    private static readonly AssetLocation IgniteSound = new("game", "sounds/torch-ignite");
    private static readonly AssetLocation CapSound = new("game", "sounds/block/ingot");

    /// <summary>Real milliseconds the lid takes to open or shut: its swing's boxes stand that long.</summary>
    public const long SwingMs = 1400;
    private const string BuildKeyPrefix = "build-";

    private FurnaceBuild? _build;
    private readonly FurnacePot?[] _pots = new FurnacePot?[2];
    private HoleHeat _heat = new(FurnaceHeat.Ambient, 0, false);
    private double _lastHours = double.NaN;
    private bool _lidOpen = true;
    private long _lidMovedMs = long.MinValue / 2;
    private Damper _damper = Damper.Open;
    private bool _capped;
    private FurnaceRow _row = new(0, 1, null, 0, null, RowProblem.NoChimney);
    private Draught? _draught;
    private Vec3d? _chimneyTop;
    private int? _serverCokeCapacity;
    private BoxTables? _boxes;
    private CrucibleFurnaceRenderer? _renderer;
    private float _particleSeconds;

    public Side Side { get; private set; } = Side.North;
    public FurnaceBuild? Build => _build;
    public bool Complete => _build?.Complete == true;
    public bool LiningIn => _build?.Has(FurnaceRequires.Lining) == true;
    public bool LidFitted => _build?.Has(FurnaceRequires.Lid) == true;
    /// <summary>The lid is fitted and shut.</summary>
    public bool LidClosed => LidFitted && !_lidOpen;
    public bool LidOpen => !LidClosed;
    /// <summary>The lid is still turning (both sides: from when each heard it move).</summary>
    public bool LidSwinging => LidFitted && Api != null && Api.World.ElapsedMilliseconds - _lidMovedMs < SwingMs;
    public Damper Damper => _damper;
    public bool Capped => _capped;
    public FurnacePot? Pot(int i) => i >= 0 && i < _pots.Length ? _pots[i] : null;
    public double Temperature => _heat.Temperature;
    public double Coke => _heat.Coke;
    public bool Lit => _heat.Lit;
    public FurnaceRow Row => _row;
    /// <summary>The row's draught, or null when it has none.</summary>
    public Draught? Draught => _draught;
    /// <summary>The top of the row's chimney, where it smokes; null with none.</summary>
    public Vec3d? ChimneyTop => _chimneyTop;
    public string? BrickType => _build?.BrickType;
    public int CokeCapacity => _serverCokeCapacity ?? Config.CokeCapacity;

    private CrucibleFurnaceSystem System => CrucibleFurnaceSystem.Of(Api);
    private CrucibleFurnaceConfig Config => System.Config;
    private CrucibleFurnaceRig? Rig => System.Rig;

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        Side = Sides.TryParse(Block.Variant["side"], out var side) ? side : Side.North;
        if (Rig is { } rig)
            _build ??= new FurnaceBuild(rig.Stages, rig.Bricks);
        BuildBoxes();
        if (api.Side == EnumAppSide.Server)
        {
            RegisterDelayedCallback(_ =>
            {
                if (Api?.World.BlockAccessor.GetBlockEntity(Pos) == this)
                    EnsureGhosts();
            }, 100);
            RegisterGameTickListener(_ => Update(), 1000);
        }
        else
        {
            RegisterGameTickListener(OnClientTick, 100);
            if (api is ICoreClientAPI capi && Rig is { } r && Block is BlockCrucibleFurnace)
                _renderer = new CrucibleFurnaceRenderer(capi, this, r);
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

    /// <summary>A world position's cell in the native frame.</summary>
    public Int3 LocalCell(BlockPos cellPos) => Footprint.ToLocal(new Int3(cellPos.X - Pos.X, cellPos.Y - Pos.Y, cellPos.Z - Pos.Z), Side);

    public IEnumerable<BlockPos> GhostCells() => Rig is { } rig ? rig.GhostCells.Select(c => CellPos(c.Pos)) : [];

    /// <summary>The cells under the hollow ones: the top of the hole's rows, where a click is the
    /// hole's (the lid, the pots, the fire).</summary>
    private bool IsHoleCell(Int3 cell) => Rig is { } rig && rig.HollowCells.Any(h => h.Pos == cell + new Int3(0, 1, 0));

    private bool IsHollow(Int3 cell) => Rig is { } rig && rig.HollowCells.Any(h => h.Pos == cell);

    // The boxes: built once per facing, then only read (collision lookups can run off the main thread).
    private sealed record BoxTables(
        Dictionary<Int3, Cuboidf[]> Built, Dictionary<Int3, Cuboidf[]> BuiltCollision,
        Dictionary<Int3, Cuboidf[]> Frame, Dictionary<Int3, Cuboidf[]> FrameCollision,
        Dictionary<Int3, Cuboidf[]> LidOpen, Dictionary<Int3, Cuboidf[]> LidSwept);

    private void BuildBoxes()
    {
        if (Rig is not { } rig || !Sides.TryParse(Block?.Variant["side"], out var side))
            return;
        Cuboidf World(Box b)
        {
            var w = Footprint.ToWorld(b, side);
            return new Cuboidf(w.X1, w.Y1, w.Z1, w.X2, w.Y2, w.Z2);
        }
        var built = rig.Cells.ToDictionary(c => c.Pos, c => c.Hollow ? [] : c.Boxes.Count == 0 ? [Cuboidf.Default()] : c.Boxes.Select(World).ToArray());
        var builtCollision = new Dictionary<Int3, Cuboidf[]>(built);
        foreach (var c in rig.Cells)
            if (!c.Hollow && c.LidBox is { } lid)
                builtCollision[c.Pos] = [.. built[c.Pos], World(lid)];
        // the iron frame alone: every brickwork cell is clicked whole, but only the ground row (the grate
        // and its bearers, the binding's feet) is solid to walk into
        var frame = rig.Cells.ToDictionary(c => c.Pos, c => c.Hollow ? [] : new[] { Cuboidf.Default() });
        var frameCollision = rig.Cells.ToDictionary(c => c.Pos, c => c.Hollow || c.Pos.Y > 0 ? [] : new[] { Cuboidf.Default() });
        var hollow = rig.HollowCells.Select(c => c.Pos).ToList();
        Dictionary<Int3, Cuboidf[]> Lid(Box? box) =>
            box is { } b ? CrucibleFurnaceRig.ByCell(b, hollow).ToDictionary(kv => kv.Key, kv => new[] { World(kv.Value) }) : [];
        var lidBoxes = System.Lid;
        _boxes = new BoxTables(built, builtCollision, frame, frameCollision, Lid(lidBoxes?.Open), Lid(lidBoxes?.Swept));
    }

    /// <summary>A cell's selection boxes by the furnace's state: the iron frame's cells whole before the
    /// brickwork, the rig's after; over the hole, the lid's while it stands open or swings; null when
    /// the cell is not the furnace's.</summary>
    public Cuboidf[]? CellBoxes(BlockPos cellPos) => Boxes(cellPos, collision: false);

    /// <summary>A cell's collision boxes: as <see cref="CellBoxes"/>, the iron frame's ground row only
    /// before the brickwork, and the rig's decks on the top cells after it.</summary>
    public Cuboidf[]? CollisionBoxes(BlockPos cellPos) => Boxes(cellPos, collision: true);

    private Cuboidf[]? Boxes(BlockPos cellPos, bool collision)
    {
        if (_boxes is not { } t)
            return null;
        var local = LocalCell(cellPos);
        if (!t.Built.ContainsKey(local))
            return null;
        if (t.Built[local].Length == 0 && IsHollow(local))
        {
            if (!LidFitted)
                return [];
            var table = LidSwinging ? t.LidSwept : _lidOpen ? t.LidOpen : null;
            return table?.GetValueOrDefault(local) ?? [];
        }
        bool brick = _build?.Has(FurnaceRequires.Brickwork) == true;
        return (brick ? collision ? t.BuiltCollision : t.Built : collision ? t.FrameCollision : t.Frame).GetValueOrDefault(local);
    }

    // ---- Ghosts ----

    private Block? GhostBlock => Api.World.GetBlock(new AssetLocation(CrucibleFurnaceSystem.Domain, "cruciblefurnace-ghost"));

    public void PlaceGhosts()
    {
        if (Api.Side != EnumAppSide.Server || GhostBlock is not { } ghost)
            return;
        foreach (var pos in GhostCells())
            StampGhost(ghost, pos);
    }

    private void StampGhost(Block ghost, BlockPos pos)
    {
        Api.World.BlockAccessor.SetBlock(ghost.BlockId, pos);
        if (Api.World.BlockAccessor.GetBlockEntity(pos) is BECrucibleFurnaceGhost be)
        {
            be.Principal = Pos.Copy();
            be.MarkDirty(true);
        }
    }

    /// <summary>Restores ghost cells that went missing, and points every ghost back here.</summary>
    public void EnsureGhosts()
    {
        if (Api.Side != EnumAppSide.Server || GhostBlock is not { } expected)
            return;
        var ba = Api.World.BlockAccessor;
        foreach (var pos in GhostCells())
        {
            if (ba.GetChunkAtBlockPos(pos) == null)
                continue;
            var current = ba.GetBlock(pos);
            if (current.Id == expected.Id)
            {
                if (ba.GetBlockEntity(pos) is BECrucibleFurnaceGhost be && !Pos.Equals(be.Principal))
                {
                    be.Principal = Pos.Copy();
                    be.MarkDirty(true);
                }
            }
            else if (current.Id == 0)
                StampGhost(expected, pos);
        }
    }

    public void RemoveGhosts()
    {
        if (Api.Side != EnumAppSide.Server)
            return;
        var ba = Api.World.BlockAccessor;
        foreach (var pos in GhostCells())
            if (ba.GetBlock(pos) is BlockCrucibleFurnaceGhost && ba.GetBlockEntity(pos) is BECrucibleFurnaceGhost be && Pos.Equals(be.Principal))
                ba.SetBlock(0, pos);
    }

    // ---- The fire ----

    /// <summary>Runs the furnace up to now (server side): row, draught, fire, melts. Public for the tests.</summary>
    public void Update()
    {
        if (Api == null)
            return;
        double now = Api.World.Calendar.TotalHours;
        if (double.IsNaN(_lastHours))
            _lastHours = now;
        double hours = Math.Max(0, now - _lastHours);
        _lastHours = now;

        var row = FindRow();
        bool rowChanged = row != _row;
        _row = row;
        var draught = row.Works ? FurnaceHeat.DraughtOf(row.ChimneyHeight, Config) : null;
        rowChanged |= draught != _draught;
        _draught = draught;
        var top = ChimneyTopOf(row);
        rowChanged |= !Equals(top, _chimneyTop);
        _chimneyTop = top;

        var before = _heat;
        _heat = FurnaceHeat.Advance(_heat, hours, draught, LidClosed, _damper, Config);
        bool changed = rowChanged || (int)before.Temperature != (int)_heat.Temperature || before.Lit != _heat.Lit
                       || Math.Ceiling(before.Coke - 1e-9) != Math.Ceiling(_heat.Coke - 1e-9);
        foreach (var pot in _pots)
            changed |= pot?.TryMelt(System.Recipes, _heat.Temperature) == true;
        if (changed)
            MarkDirty();
    }

    /// <summary>Moves the fire's clock <paramref name="hours"/> back, as if that much time had passed
    /// since the last tick (the tests' clock); the next <see cref="Update"/> runs it.</summary>
    public void Age(double hours) => _lastHours = (double.IsNaN(_lastHours) ? Api.World.Calendar.TotalHours : _lastHours) - hours;

    /// <summary>Sets the fire as it is (the tests').</summary>
    public void SetFire(double temperature, double coke, bool lit)
    {
        _heat = new HoleHeat(temperature, coke, lit && coke > 0);
        MarkDirty();
    }

    /// <summary>The chimney blocks standing on this furnace's outlet, counted from it up.</summary>
    public int ChimneyOnOutlet()
    {
        if (Rig is not { } rig)
            return 0;
        var ba = Api.World.BlockAccessor;
        var pos = CellPos(rig.ChimneyBase);
        int n = 0;
        while (n < FurnaceRow.ChimneyScan && Config.IsChimneyBlock(ba.GetBlock(pos.UpCopy(n))?.Code?.ToString()))
            n++;
        return n;
    }

    /// <summary>The controller of the furnace <paramref name="k"/> widths along the row (native +x), or null.</summary>
    public BECrucibleFurnace? Neighbour(int k)
    {
        if (Rig is not { } rig)
            return null;
        var pos = CellPos(new Int3(k * rig.Width, 0, 0));
        return Api.World.BlockAccessor.GetBlockEntity(pos) is BECrucibleFurnace other && other.Side == Side
               && Api.World.BlockAccessor.GetBlock(pos) is BlockCrucibleFurnace ? other : null;
    }

    /// <summary>This furnace's row: its neighbours of the same facing side by side, each with its
    /// lining in (an unlined furnace has no flue yet, and ends the row).</summary>
    public FurnaceRow FindRow()
    {
        if (Rig == null)
            return new FurnaceRow(0, 1, null, 0, null, RowProblem.NoChimney);
        RowMember? At(int k)
        {
            if (k == 0)
                return new RowMember(_capped, ChimneyOnOutlet());
            return Neighbour(k) is { LiningIn: true } other ? new RowMember(other.Capped, other.ChimneyOnOutlet()) : null;
        }
        return FurnaceRow.Find(At, Config.MaxFurnacesInRow, Config.MinChimneyHeight);
    }

    private Vec3d? ChimneyTopOf(FurnaceRow row)
    {
        if (row.ChimneyAt is not { } c || Rig is not { } rig || (c == 0 ? this : Neighbour(c)) is not { } owner)
            return null;
        var b = owner.CellPos(rig.ChimneyBase);
        return new Vec3d(b.X + 0.5, b.Y + row.ChimneyHeight, b.Z + 0.5);
    }

    // ---- Interaction ----

    /// <summary>
    /// A right-click on any of the furnace's cells. In creative mode, Ctrl fits the next stage with
    /// nothing taken. An iron plate on the outlet's top caps it (once the lining is in) and an empty
    /// hand there takes the plate back. While the furnace is being built a stage's material goes in, a
    /// stack a click. Once it is built: an empty hand on the front works the ash-pit damper; on the
    /// hole (the top cells of its two rows) it opens the lid, pulls the nearer pot, or with no pot
    /// there shuts the lid, and on the open lid it shuts it (Shift: the lid, wherever); a fired pot
    /// goes into the nearer place, coke onto the fire (Shift: into the nearer pot), and a charge into
    /// the nearer pot. A firestarter or a torch lights it (<see cref="IIgnitable"/>). Decided and done
    /// on the server; the client only says whether the click is the furnace's.
    /// </summary>
    public bool OnInteract(IPlayer byPlayer, BlockSelection sel)
    {
        if (Rig is not { } rig || _build is not { } build)
            return false;
        var slot = byPlayer.InventoryManager.ActiveHotbarSlot;
        var held = slot?.Itemstack;
        string? code = held?.Collectible?.Code?.ToString();
        var controls = byPlayer.Entity.Controls;
        bool server = Api.Side == EnumAppSide.Server;
        var cell = LocalCell(sel.Position);
        var hit = sel.HitPosition ?? new Vec3d(0.5, 0.5, 0.5);
        var native = MachinePoints.NativeHit(Pos, sel.Position, hit, Side);

        if (CreativeShortcutApplies(byPlayer))
        {
            if (server)
                FillNextStage(byPlayer);
            return true;
        }
        // the outlet's cap
        if (cell == rig.OutletCell && sel.Face == BlockFacing.UP && LiningIn)
        {
            if (!_capped && rig.OutletCap.Matches(code))
            {
                if (server)
                    Cap(byPlayer, slot!);
                return true;
            }
            if (_capped && held == null)
            {
                if (server)
                    Uncap(byPlayer);
                return true;
            }
        }
        if (!build.Complete)
        {
            var fit = build.CanFit(code, held?.StackSize ?? 0);
            if (fit.Verdict is not (BuildVerdict.NotAMaterial or BuildVerdict.Built))
            {
                if (server)
                    TryFit(slot!, byPlayer);
                return true;
            }
            bool furnaceItem = code == CrucibleFurnaceSystem.FiredPot || code == "game:coke" || code != null && System.Recipes.IsIngredient(code);
            if (furnaceItem && server)
                Error(byPlayer, "notbuilt", StageName(build.Next));
            return furnaceItem;
        }
        // a firestarter or a torch lights the coke (IIgnitable)
        if (held != null && (held.Collectible is ItemFirestarter || held.Block?.HasBehavior<BlockBehaviorCanIgnite>() == true))
            return false;
        int pot = rig.NearerPot(native);
        if (held == null)
        {
            bool front = cell == Int3.Zero && sel.Face == BlockFacing.FromCode(Footprint.ToWorld(rig.AshSide, Side).Code());
            bool hole = IsHoleCell(cell);
            bool lid = IsHollow(cell);
            if (!front && !hole && !lid && !controls.ShiftKey)
                return false;
            if (!server)
                return true;
            if (front && !controls.ShiftKey)
                CycleDamper(byPlayer);
            else if (lid || controls.ShiftKey || LidClosed)
                ToggleLid(byPlayer);
            else if (_pots[pot] != null)
                Pull(byPlayer, slot!, pot);
            else
                ToggleLid(byPlayer);
            return true;
        }
        bool isPot = code == CrucibleFurnaceSystem.FiredPot;
        bool coke = code == "game:coke" && !controls.ShiftKey;
        if (!isPot && !coke && !System.Recipes.IsIngredient(code!))
        {
            if (IsHoleCell(cell) && server)
                Error(byPlayer, "notaningredient");
            return IsHoleCell(cell);
        }
        if (!server)
            return true;
        if (LidClosed)
            return Error(byPlayer, "lidclosed");
        if (isPot)
            PutPot(byPlayer, slot!, pot);
        else if (coke)
            AddCoke(byPlayer, slot!);
        else
            AddCharge(byPlayer, slot!, pot);
        return true;
    }

    /// <summary>Whether a click is the creative shortcut's: a player in creative mode, Ctrl without
    /// Shift, on a furnace with a stage still to fit.</summary>
    public bool CreativeShortcutApplies(IPlayer byPlayer)
    {
        var controls = byPlayer.Entity.Controls;
        return _build is { Complete: false }
               && CreativeUpgrades.Applies(byPlayer.WorldData?.CurrentGameMode == EnumGameMode.Creative, controls.CtrlKey, controls.ShiftKey);
    }

    /// <summary>The creative shortcut (server side): fills the next stage with nothing taken.</summary>
    public bool FillNextStage(IPlayer? byPlayer)
    {
        if (_build is not { } build || !build.FillNext())
            return false;
        StageFitted(byPlayer);
        return true;
    }

    /// <summary>Fits what the next stage takes of the stack in <paramref name="slot"/> (server side);
    /// false, with an error to the player, when the rules say no. The fitted items are taken from the
    /// slot unless the player is in creative mode.</summary>
    public bool TryFit(ItemSlot slot, IPlayer? byPlayer)
    {
        if (_build is not { } build || slot.Itemstack is not { } stack)
            return false;
        string code = stack.Collectible.Code.ToString();
        int? stage = build.Next;
        var fit = build.Fit(code, stack.StackSize);
        switch (fit.Verdict)
        {
            case BuildVerdict.Fits:
                break;
            case BuildVerdict.OutOfOrder:
                return Error(byPlayer, "error-order", StageName(build.Next));
            case BuildVerdict.Enough:
                return Error(byPlayer, "error-enough", StageName(build.Next));
            case BuildVerdict.OtherBrick:
                return Error(byPlayer, "error-otherbrick", BrickName(build.BrickType));
            case BuildVerdict.UnknownBrick:
                return Error(byPlayer, "error-unknownbrick");
            default:
                return Error(byPlayer, "error-notamaterial");
        }
        if (byPlayer?.WorldData.CurrentGameMode != EnumGameMode.Creative)
        {
            slot.TakeOut(fit.Count);
            slot.MarkDirty();
        }
        Api.World.PlaySoundAt(Block.Sounds.Place, Pos, -0.25, byPlayer);
        if (stage is { } s && build.StageComplete(s))
            StageFitted(byPlayer);
        else
            MarkDirty(true);
        return true;
    }

    /// <summary>A stage is complete: with the last (the lid and damper) the lid goes in open, and the
    /// damper open.</summary>
    private void StageFitted(IPlayer? byPlayer)
    {
        if (Complete)
        {
            _lidOpen = true;
            _damper = Damper.Open;
        }
        MarkDirty(true);
    }

    /// <summary>A stage's name as the block info and the errors show it.</summary>
    public string StageName(int? stage) =>
        stage is { } s && _build != null ? Lang.Get(CrucibleFurnaceSystem.Domain + ":cruciblefurnace-stage-" + _build.Stages[s].Requires) : "";

    public static string BrickName(string? type) =>
        type == null ? "" : Lang.Get(CrucibleFurnaceSystem.Domain + ":cruciblefurnace-brick-" + type);

    private void Cap(IPlayer byPlayer, ItemSlot slot)
    {
        _capped = true;
        if (byPlayer.WorldData.CurrentGameMode != EnumGameMode.Creative)
        {
            slot.TakeOut(1);
            slot.MarkDirty();
        }
        var at = WorldPoint(new Float3(Rig!.OutletCell.X + 0.5f, Rig.OutletCell.Y + 1, Rig.OutletCell.Z + 0.5f));
        Api.World.PlaySoundAt(CapSound, at.X, at.Y, at.Z, byPlayer);
        MarkDirty(true);
    }

    private void Uncap(IPlayer byPlayer)
    {
        _capped = false;
        if (CrucibleFurnaceSystem.Resolve(Api.World, Rig!.OutletCap.Code) is { } plate)
            Give(byPlayer, new ItemStack(plate, Rig.OutletCap.Quantity));
        var at = WorldPoint(new Float3(Rig.OutletCell.X + 0.5f, Rig.OutletCell.Y + 1, Rig.OutletCell.Z + 0.5f));
        Api.World.PlaySoundAt(CapSound, at.X, at.Y, at.Z, byPlayer);
        MarkDirty(true);
    }

    /// <summary>Whether the player can touch the furnace's hot parts: it is not too hot to touch, or
    /// they hold tongs (any <c>heatResistant</c> item) in the off hand.</summary>
    private bool CanTouch(IPlayer byPlayer) =>
        _heat.Temperature <= GlobalConstants.TooHotToTouchTemperature
        || byPlayer.Entity.LeftHandItemSlot?.Itemstack?.ItemAttributes?.IsTrue("heatResistant") == true;

    /// <summary>Opens or shuts the lid (server side), with tongs while it is too hot to touch.</summary>
    public bool ToggleLid(IPlayer byPlayer)
    {
        if (!LidFitted)
            return false;
        if (!CanTouch(byPlayer))
            return Error(byPlayer, "lidhot");
        _lidOpen = !_lidOpen;
        _lidMovedMs = Api.World.ElapsedMilliseconds;
        var at = WorldPoint(Rig!.Mouth);
        Api.World.PlaySoundAt(LidSound, at.X, at.Y, at.Z, byPlayer);
        MarkDirty(true);
        return true;
    }

    /// <summary>Moves the ash-pit damper to its next place: open, half, shut, open (server side).</summary>
    public void CycleDamper(IPlayer? byPlayer)
    {
        if (!LidFitted)
            return;
        _damper = _damper switch { Damper.Open => Damper.Half, Damper.Half => Damper.Shut, _ => Damper.Open };
        var at = WorldPoint(Rig!.Ash);
        Api.World.PlaySoundAt(DamperSound, at.X, at.Y, at.Z, byPlayer);
        MarkDirty(true);
    }

    private void PutPot(IPlayer byPlayer, ItemSlot slot, int i)
    {
        if (_pots[i] != null)
        {
            Error(byPlayer, "haspot");
            return;
        }
        var stack = slot.Itemstack!;
        _pots[i] = new FurnacePot { Heats = PotStack.Heats(stack), Charge = PotStack.Charge(stack) };
        slot.TakeOut(1);
        slot.MarkDirty();
        var at = WorldPoint(Rig!.PotPoints[i]);
        Api.World.PlaySoundAt(PotSound, at.X, at.Y, at.Z, byPlayer);
        MarkDirty(true);
    }

    private void AddCoke(IPlayer byPlayer, ItemSlot slot)
    {
        int room = Config.CokeCapacity - (int)Math.Ceiling(_heat.Coke - 1e-9);
        if (room <= 0)
        {
            Error(byPlayer, "cokefull", Config.CokeCapacity);
            return;
        }
        int n = Math.Min(room, slot.StackSize);
        _heat = _heat with { Coke = _heat.Coke + n };
        slot.TakeOut(n);
        slot.MarkDirty();
        var at = WorldPoint(Rig!.Mouth);
        Api.World.PlaySoundAt(CokeSound, at.X, at.Y, at.Z, byPlayer);
        MarkDirty();
    }

    private void AddCharge(IPlayer byPlayer, ItemSlot slot, int i)
    {
        if (_pots[i] is not { } pot)
        {
            Error(byPlayer, "nopot");
            return;
        }
        if (pot.Melted != null)
        {
            Error(byPlayer, "molten");
            return;
        }
        string code = slot.Itemstack!.Collectible.Code.ToString();
        var add = pot.Add(System.Recipes, code, slot.StackSize, Config.PotCapacityUnits);
        if (add.Count <= 0)
        {
            Error(byPlayer, "refused-" + add.Refusal.ToString().ToLowerInvariant(), Config.PotCapacityUnits);
            return;
        }
        slot.TakeOut(add.Count);
        slot.MarkDirty();
        var at = WorldPoint(Rig!.PotPoints[i]);
        Api.World.PlaySoundAt(ChargeSound, at.X, at.Y, at.Z, byPlayer);
        MarkDirty();
    }

    /// <summary>Takes pot <paramref name="i"/> out into the player's empty hand (server side), with
    /// tongs while it is too hot to touch.</summary>
    public bool Pull(IPlayer byPlayer, ItemSlot hand, int i)
    {
        if (_pots[i] is not { } pot)
            return false;
        if (!CanTouch(byPlayer))
            return Error(byPlayer, "needtongs");
        double t = _heat.Temperature;
        var world = Api.World;
        var recipe = System.Recipes.Get(pot.Melted);
        _pots[i] = null;
        MarkDirty(true);
        var at = WorldPoint(Rig!.PotPoints[i]);

        if (recipe == null)
        {
            // unmelted: the pot comes back with its charge
            var back = new ItemStack(world.GetBlock(new AssetLocation(CrucibleFurnaceSystem.FiredPot)));
            PotStack.SetHeats(back, pot.Heats);
            PotStack.SetCharge(back, pot.Charge);
            Hot(back, t);
            hand.Itemstack = back;
            hand.MarkDirty();
            return true;
        }
        int heats = pot.Heats + 1;
        int units = pot.MeltedUnits;
        if (recipe.Pourable && CrucibleFurnaceSystem.Resolve(world, recipe.Output) is { } ingot)
        {
            var metal = new ItemStack(ingot);
            double meltingPoint = metal.Collectible.GetMeltingPoint(world, null, new DummySlot(metal));
            if (t < PourWindow.SolidifiesAt(meltingPoint))
            {
                // frozen in the furnace: the pot is lost, the metal knocked out
                world.PlaySoundAt(CrackSound, at.X, at.Y, at.Z, null);
                Give(byPlayer, Bits(metal, units / Stainless.UnitsPerLump));
                Error(byPlayer, "frozenhole", units / Stainless.UnitsPerLump);
                return true;
            }
            var smelted = new ItemStack(world.GetBlock(new AssetLocation(CrucibleFurnaceSystem.SmeltedPot)));
            (smelted.Collectible as BlockSmeltedContainer)?.SetContents(smelted, metal, PotRecipes.Products(recipe, units).Units);
            PotStack.SetHeats(smelted, heats);
            Hot(smelted, t);
            double hoursPerSecond = world.Calendar.SpeedOfTime * world.Calendar.CalendarSpeedMul / 3600.0;
            double speed = PourWindow.CooldownPerHour(t, meltingPoint, Config.PourWindowSeconds, hoursPerSecond);
            (smelted.Attributes["temperature"] as ITreeAttribute)?.SetFloat("cooldownSpeed", (float)speed);
            hand.Itemstack = smelted;
            hand.MarkDirty();
            return true;
        }
        // a ferroalloy: the cake is broken out of the pot, which goes to the hand first
        var products = PotRecipes.Products(recipe, units);
        if (heats >= Config.PotHeats)
        {
            world.PlaySoundAt(CrackSound, at.X, at.Y, at.Z, null);
            Error(byPlayer, "potcracked");
        }
        else
        {
            var empty = new ItemStack(world.GetBlock(new AssetLocation(CrucibleFurnaceSystem.FiredPot)));
            PotStack.SetHeats(empty, heats);
            hand.Itemstack = Hot(empty, t);
            hand.MarkDirty();
        }
        if (products.Lumps > 0 && CrucibleFurnaceSystem.Resolve(world, recipe.Output) is { } lump)
            Give(byPlayer, Hot(new ItemStack(lump, products.Lumps), t));
        if (products.Byproducts > 0 && recipe.Byproduct != null && CrucibleFurnaceSystem.Resolve(world, recipe.Byproduct) is { } slag)
            Give(byPlayer, Hot(new ItemStack(slag, products.Byproducts), t));
        return true;
    }

    private ItemStack Hot(ItemStack stack, double t)
    {
        if (t > FurnaceHeat.Ambient + 1)
            stack.Collectible.SetTemperature(Api.World, stack, (float)t);
        return stack;
    }

    private ItemStack? Bits(ItemStack metal, int count)
    {
        string? m = metal.Collectible.Variant["metal"];
        return count > 0 && m != null && Api.World.GetItem(new AssetLocation("game", "metalbit-" + m)) is { } bit
            ? new ItemStack(bit, count)
            : null;
    }

    private void Give(IPlayer byPlayer, ItemStack? stack)
    {
        if (stack == null)
            return;
        if (!byPlayer.InventoryManager.TryGiveItemstack(stack, true))
            Api.World.SpawnItemEntity(stack, WorldPoint(Rig?.Mouth ?? new Float3(0.5f, 2, 1)).Add(0, 0.2, 0));
    }

    private bool Error(IPlayer? byPlayer, string key, params object[] args)
    {
        if (!key.StartsWith("error-", StringComparison.Ordinal))
            key = "error-" + key;
        if (byPlayer is IServerPlayer sp)
            sp.SendIngameError("cruciblefurnace-" + key, Lang.GetL(sp.LanguageCode, CrucibleFurnaceSystem.Domain + ":cruciblefurnace-" + key, args));
        return false;
    }

    // ---- Lighting ----

    /// <summary>Whether a firestarter or torch can light the furnace now; else the error key.</summary>
    public string? CannotLight()
    {
        if (!Complete)
            return "notbuilt";
        if (_heat.Lit)
            return "lit";
        if (LidClosed)
            return "lidclosed";
        if (_heat.Coke <= 0)
            return "nocoke";
        if (_pots.Any(p => p != null && !p.ChargeMakesSomething(System.Recipes)))
            return "badcharge";
        return null;
    }

    /// <summary>Lights the coke (server side); the reason it cannot, to the player, else.</summary>
    public bool TryLight(IPlayer? byPlayer)
    {
        if (CannotLight() is { } why)
            return Error(byPlayer, why, StageName(_build?.Next));
        Update();
        _heat = _heat with { Lit = true };
        var at = WorldPoint(Rig!.Mouth);
        Api.World.PlaySoundAt(IgniteSound, at.X, at.Y, at.Z, null);
        MarkDirty();
        return true;
    }

    // ---- Breaking ----

    /// <summary>What breaking the furnace gives besides its frame: every fitted item, the outlet's cap,
    /// the unburnt coke, each pot with its charge; a melted charge as its products (molten metal as
    /// bits), the pot lost.</summary>
    public List<ItemStack> ContentDrops()
    {
        var drops = new List<ItemStack>();
        var world = Api.World;
        foreach (var drop in _build?.Returns() ?? [])
            if (CrucibleFurnaceSystem.Resolve(world, drop.Code) is { } item)
                drops.Add(new ItemStack(item, drop.Count));
        if (_capped && Rig is { } rig && CrucibleFurnaceSystem.Resolve(world, rig.OutletCap.Code) is { } plate)
            drops.Add(new ItemStack(plate, rig.OutletCap.Quantity));
        int coke = (int)Math.Floor(_heat.Coke + 1e-9);
        if (coke > 0 && world.GetItem(new AssetLocation("game", "coke")) is { } cokeItem)
            drops.Add(new ItemStack(cokeItem, coke));
        foreach (var pot in _pots)
        {
            if (pot == null)
                continue;
            if (System.Recipes.Get(pot.Melted) is { } recipe)
            {
                var products = PotRecipes.Products(recipe, pot.MeltedUnits);
                if (CrucibleFurnaceSystem.Resolve(world, recipe.Output) is { } output)
                {
                    var stack = recipe.Pourable ? Bits(new ItemStack(output), products.Units / Stainless.UnitsPerLump)
                        : products.Lumps > 0 ? new ItemStack(output, products.Lumps) : null;
                    if (stack != null)
                        drops.Add(stack);
                }
                if (products.Byproducts > 0 && recipe.Byproduct != null && CrucibleFurnaceSystem.Resolve(world, recipe.Byproduct) is { } slag)
                    drops.Add(new ItemStack(slag, products.Byproducts));
                continue;
            }
            var back = new ItemStack(world.GetBlock(new AssetLocation(CrucibleFurnaceSystem.FiredPot)));
            PotStack.SetHeats(back, pot.Heats);
            PotStack.SetCharge(back, pot.Charge);
            drops.Add(back);
        }
        return drops;
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

    // ---- Client ----

    // The client's own: the client removes a broken furnace at once, and an update the server sent
    // before it heard of the break then brings the block entity back over air: drop it. A lit furnace
    // flames at the hole while its lid is open and smokes at its row's chimney.
    private void OnClientTick(float dt)
    {
        if (Api.World.BlockAccessor.GetBlock(Pos) is not BlockCrucibleFurnace)
        {
            DisposeRenderer();
            Api.World.BlockAccessor.RemoveBlockEntity(Pos);
            return;
        }
        if (Api is not ICoreClientAPI capi || Rig is not { } rig || !_heat.Lit)
            return;
        if ((_particleSeconds += dt) < 0.3f)
            return;
        _particleSeconds = 0;
        if (LidOpen)
        {
            var mouth = WorldPoint(rig.Mouth);
            var flames = new SimpleParticleProperties(2, 4, ColorUtil.ToRgba(220, 255, 160, 40), mouth.AddCopy(-0.6, -0.2, -0.3),
                mouth.AddCopy(0.6, 0, 0.3), new Vec3f(-0.05f, 0.3f, -0.05f), new Vec3f(0.05f, 0.8f, 0.05f), 0.5f, -0.05f, 0.4f, 0.9f, EnumParticleModel.Quad)
            {
                VertexFlags = 200,
                OpacityEvolve = EvolvingNatFloat.create(EnumTransformFunction.LINEAR, -200),
            };
            capi.World.SpawnParticles(flames);
        }
        if (_chimneyTop is { } top && _draught != null)
        {
            var smoke = new SimpleParticleProperties(1, 2, ColorUtil.ToRgba(80, 60, 60, 60), top.AddCopy(-0.2, 0, -0.2), top.AddCopy(0.2, 0.2, 0.2),
                new Vec3f(-0.1f, 0.3f, -0.1f), new Vec3f(0.1f, 0.6f, 0.1f), 3, -0.02f, 0.5f, 1.5f, EnumParticleModel.Quad)
            {
                SizeEvolve = EvolvingNatFloat.create(EnumTransformFunction.LINEAR, 1.5f),
                OpacityEvolve = EvolvingNatFloat.create(EnumTransformFunction.LINEAR, -40),
            };
            capi.World.SpawnParticles(smoke);
        }
    }

    // ---- Info ----

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
    {
        base.GetBlockInfo(forPlayer, dsc);
        if (_build is not { } build || Rig is not { } rig)
            return;
        string L(string key, params object[] args) => Lang.Get(CrucibleFurnaceSystem.Domain + ":cruciblefurnace-info-" + key, args);
        string ItemName(string code) => CrucibleFurnaceSystem.Resolve(Api.World, code) is { } c ? new ItemStack(c).GetName() : code;

        // the build
        if (build.Next is { } next)
        {
            int done = next - 1;
            dsc.AppendLine(L("stage", done < 0 ? Lang.Get(CrucibleFurnaceSystem.Domain + ":cruciblefurnace-stage-frame") : StageName(done)));
            var missing = build.Stages[next].Materials.Select((m, i) => (m, i)).Where(p => build.Missing(next, p.i) > 0)
                .Select(p => $"{build.Missing(next, p.i)} {(build.ItemOf(next, p.i) is { } item ? ItemName(item) : Lang.Get(CrucibleFurnaceSystem.Domain + ":cruciblefurnace-material-anybrick"))}");
            dsc.AppendLine(L("next", StageName(next), string.Join(", ", missing)));
        }
        if (build.BrickType is { } brick)
            dsc.AppendLine(L("brick", BrickName(brick)));
        if (!build.Complete)
        {
            if (LiningIn)
                AppendRow(dsc, L);
            return;
        }
        dsc.AppendLine(L(LidClosed ? "lidclosed" : "lidopen"));
        dsc.AppendLine(L("damper-" + _damper.ToString().ToLowerInvariant()));
        int coke = (int)Math.Ceiling(_heat.Coke - 1e-9);
        dsc.AppendLine(L("temperature", (int)Math.Round(_heat.Temperature)));
        dsc.AppendLine(coke == 0 ? L("nocoke") : L(_heat.Lit ? "cokeburning" : "coke", coke, CokeCapacity));
        for (int i = 0; i < _pots.Length; i++)
            AppendPot(dsc, L, i);
        AppendRow(dsc, L);
    }

    private void AppendPot(StringBuilder dsc, System.Func<string, object[], string> L, int i)
    {
        string Line(string key, params object[] args) => L(key, args);
        string name = Line("pot" + (i + 1));
        if (_pots[i] is not { } pot)
        {
            dsc.AppendLine(Line("nopot", name));
            return;
        }
        var world = Api.World;
        var recipes = System.Recipes;
        string heats = PotStack.HeatsLine(pot.Heats, Config.PotHeats);
        if (recipes.Get(pot.Melted) is { } melted)
        {
            var output = CrucibleFurnaceSystem.Resolve(world, melted.Output);
            string metal = output == null ? melted.Output
                : output.Variant["metal"] is { } m ? Lang.Get("material-" + m) : new ItemStack(output).GetName();
            dsc.AppendLine(melted.Pourable ? Line("molten", name, PotRecipes.Products(melted, pot.MeltedUnits).Units, metal, heats)
                : Line("ready", name, PotRecipes.Products(melted, pot.MeltedUnits).Lumps, metal, heats));
            return;
        }
        if (pot.Charge.Count == 0)
        {
            dsc.AppendLine(Line("empty", name, Config.PotCapacityUnits, heats));
            return;
        }
        dsc.AppendLine(Line("charge", name, PotStack.Describe(world, pot.Charge), pot.ChargeUnits, Config.PotCapacityUnits, heats));
        var match = recipes.Match(pot.Charge);
        if (match.Recipe is { } r)
        {
            int needs = (int)r.Temperature;
            dsc.AppendLine(Line(_heat.Temperature >= needs ? "makes-hot" : "makes", Lang.Get(CrucibleFurnaceSystem.Domain + ":cruciblefurnace-recipe-" + r.Code),
                needs, needs - (int)Math.Round(_heat.Temperature)));
        }
        else if (match.Closest is { } closest)
        {
            dsc.AppendLine(Line(match.TooSmall ? "toosmall" : "wrongratio", Lang.Get(CrucibleFurnaceSystem.Domain + ":cruciblefurnace-recipe-" + closest.Code)));
            foreach (var (ingredient, share) in match.Shares)
                dsc.AppendLine(Line("share", Lang.Get(CrucibleFurnaceSystem.Domain + ":cruciblefurnace-ingredient-" + ingredient.Name),
                    (int)Math.Round(share * 100), (int)Math.Round(ingredient.Min * 100), (int)Math.Round(ingredient.Max * 100)));
        }
    }

    /// <summary>A member of the row, as the block info names it: this furnace, or so many to the left
    /// or right of it, seen from its front (native +x is the viewer's left).</summary>
    private static string Where(int k)
    {
        string L(string key, params object[] args) => Lang.Get(CrucibleFurnaceSystem.Domain + ":cruciblefurnace-row-" + key, args);
        return k == 0 ? L("this") : k > 0 ? L("left", k) : L("right", -k);
    }

    private void AppendRow(StringBuilder dsc, System.Func<string, object[], string> L)
    {
        string Line(string key, params object[] args) => L(key, args);
        var row = _row;
        var c = Config;
        string chimney = row.ChimneyAt is { } at ? Where(at) : "";
        string members = row.Count > 1 ? Line("row", row.Count, chimney) : "";
        dsc.AppendLine(row.Problem switch
        {
            RowProblem.TooManyFurnaces => Line("draught-toomany", c.MaxFurnacesInRow),
            RowProblem.NoChimney => Line(_capped && row.Count == 1 ? "draught-capped" : "draught-none", c.MinChimneyHeight),
            RowProblem.OutletOpen => Line("draught-open", Where(row.OpenOutlet ?? 0), chimney),
            RowProblem.ChimneyTooShort => Line("draught-short", row.ChimneyHeight, c.MinChimneyHeight),
            _ => _draught is { } d
                ? Line(d.Height > c.MaxChimneyHeight ? "draught-max" : "draught", d.Height, (int)Math.Round(FurnaceHeat.HeatingRate(d, _damper, c)), (int)Math.Round(d.MaxTemperature), c.MaxChimneyHeight)
                : Line("draught-none", c.MinChimneyHeight),
        });
        if (members.Length > 0 && row.Problem is RowProblem.None or RowProblem.ChimneyTooShort)
            dsc.AppendLine(members);
    }

    // ---- Saving and syncing ----

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        foreach (var key in tree.Select(e => e.Key).Where(k => k.StartsWith(BuildKeyPrefix, StringComparison.Ordinal)).ToList())
            tree.RemoveAttribute(key);
        if (_build != null)
        {
            foreach (var (key, n) in _build.Snapshot())
                tree.SetInt(BuildKeyPrefix + key, n);
            if (_build.BrickType != null)
                tree.SetString("brickType", _build.BrickType);
            else
                tree.RemoveAttribute("brickType");
        }
        for (int i = 0; i < _pots.Length; i++)
        {
            string k = "pot" + i;
            if (_pots[i] is not { } pot)
            {
                tree.RemoveAttribute(k);
                continue;
            }
            var p = new TreeAttribute();
            p.SetInt("heats", pot.Heats);
            var charge = new TreeAttribute();
            foreach (var item in pot.Charge)
                charge.SetInt(item.Code, item.Count);
            p["charge"] = charge;
            if (pot.Melted != null)
                p.SetString("melted", pot.Melted);
            p.SetInt("meltedUnits", pot.MeltedUnits);
            tree[k] = p;
        }
        tree.SetDouble("temperature", _heat.Temperature);
        tree.SetDouble("coke", _heat.Coke);
        tree.SetBool("lit", _heat.Lit);
        tree.SetDouble("lastHours", _lastHours);
        tree.SetBool("lidOpen", _lidOpen);
        tree.SetInt("damper", (int)_damper);
        tree.SetBool("capped", _capped);
        tree.SetInt("rowFirst", _row.First);
        tree.SetInt("rowCount", _row.Count);
        tree.SetInt("rowChimneyAt", _row.ChimneyAt ?? int.MinValue);
        tree.SetInt("rowChimneyHeight", _row.ChimneyHeight);
        tree.SetInt("rowOpenOutlet", _row.OpenOutlet ?? int.MinValue);
        tree.SetInt("rowProblem", (int)_row.Problem);
        if (_draught is { } d)
        {
            tree.SetDouble("draughtRate", d.HeatingPerHour);
            tree.SetDouble("draughtMax", d.MaxTemperature);
            tree.SetInt("draughtHeight", d.Height);
        }
        else
            tree.RemoveAttribute("draughtRate");
        if (_chimneyTop is { } top)
        {
            tree.SetDouble("chimneyX", top.X);
            tree.SetDouble("chimneyY", top.Y);
            tree.SetDouble("chimneyZ", top.Z);
        }
        else
            tree.RemoveAttribute("chimneyX");
        if (Api?.Side == EnumAppSide.Server)
            tree.SetInt("cokeCapacity", Config.CokeCapacity);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);
        if (CrucibleFurnaceSystem.Of(worldForResolving.Api).Rig is { } rig)
        {
            var counts = new Dictionary<string, int>();
            foreach (var e in tree)
                if (e.Key.StartsWith(BuildKeyPrefix, StringComparison.Ordinal) && e.Value is IntAttribute n)
                    counts[e.Key[BuildKeyPrefix.Length..]] = n.value;
            _build = FurnaceBuild.Restore(rig.Stages, rig.Bricks, counts, tree.GetString("brickType"));
        }
        for (int i = 0; i < _pots.Length; i++)
        {
            if (tree["pot" + i] is not ITreeAttribute p)
            {
                _pots[i] = null;
                continue;
            }
            _pots[i] = new FurnacePot
            {
                Heats = p.GetInt("heats"),
                Charge = p["charge"] is ITreeAttribute charge
                    ? charge.Select(e => new ChargeItem(e.Key, (e.Value as IntAttribute)?.value ?? 0)).Where(c => c.Count > 0).ToList()
                    : [],
                Melted = p.GetString("melted"),
                MeltedUnits = p.GetInt("meltedUnits"),
            };
        }
        _heat = new HoleHeat(tree.GetDouble("temperature", FurnaceHeat.Ambient), tree.GetDouble("coke"), tree.GetBool("lit"));
        _lastHours = tree.GetDouble("lastHours", double.NaN);
        bool lidOpen = tree.GetBool("lidOpen", true);
        if (lidOpen != _lidOpen && Api != null)
            _lidMovedMs = Api.World.ElapsedMilliseconds;
        _lidOpen = lidOpen;
        _damper = (Damper)Math.Clamp(tree.GetInt("damper"), 0, 2);
        _capped = tree.GetBool("capped");
        int? Opt(string key) => tree.GetInt(key, int.MinValue) is var v && v != int.MinValue ? v : null;
        _row = new FurnaceRow(tree.GetInt("rowFirst"), Math.Max(1, tree.GetInt("rowCount", 1)), Opt("rowChimneyAt"), tree.GetInt("rowChimneyHeight"),
            Opt("rowOpenOutlet"), (RowProblem)tree.GetInt("rowProblem", (int)RowProblem.NoChimney));
        _draught = tree.HasAttribute("draughtRate")
            ? new Draught(tree.GetDouble("draughtRate"), tree.GetDouble("draughtMax"), tree.GetInt("draughtHeight"))
            : null;
        _chimneyTop = tree.HasAttribute("chimneyX") ? new Vec3d(tree.GetDouble("chimneyX"), tree.GetDouble("chimneyY"), tree.GetDouble("chimneyZ")) : null;
        if (worldForResolving.Side == EnumAppSide.Client)
            _serverCokeCapacity = tree.TryGetInt("cokeCapacity");
    }
}
