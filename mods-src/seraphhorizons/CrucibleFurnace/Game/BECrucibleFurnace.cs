using System.Text;
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
/// mouth's, on the shared machine base (<see cref="BEMachine"/>, docs/modeling/machine-base.md): its build is
/// <c>config/builds/cruciblefurnace.json</c> (the brickwork in one fired brick type, the lining, the lid and
/// damper), its cells the shared ones, its flue the fired machines' (<see cref="FurnaceFiring"/>: the outlet's
/// cap and the row). It holds the two pots (<see cref="FurnacePot"/>), the one coke bed and its fire
/// (<see cref="HoleHeat"/>), the lid and the ash-pit damper. The server ticks it each second: it finds the
/// furnace's row and chimney (<see cref="FurnaceRow"/>), sets the draught by the chimney's height, advances the
/// fire by the calendar's hours since the last tick (<see cref="FurnaceHeat"/>) and with it the pots, each
/// melting while the fire is at its recipe's temperature, and a molten one freezing when the fire has stayed
/// too long below its solid point (<see cref="FurnaceMelt"/>). A player works it by right-clicks on any of its
/// cells (<see cref="InteractContent"/>, <see cref="InteractWork"/>) and lights it as a forge is lit
/// (<see cref="MachineFire"/>); the client draws it with its own renderer (<see cref="CrucibleFurnaceRenderer"/>:
/// the pots' metals and the fire's glow), its cells' boxes changing with the build and the lid.
/// </summary>
public class BECrucibleFurnace : BEMachine, IFiredMachine
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

    private readonly FurnacePot?[] _pots = new FurnacePot?[2];
    private readonly FurnaceFiring _firing = new(CapSound);
    private HoleHeat _heat = new(FurnaceHeat.Ambient, 0, false);
    private double _lastHours = double.NaN;
    private bool _lidOpen = true;
    private long _lidMovedMs = long.MinValue / 2;
    private Damper _damper = Damper.Open;
    private Draught? _draught;
    private Vec3d? _chimneyTop;
    private int? _serverCokeCapacity;
    private BoxTables? _boxes;
    private float _particleSeconds;

    public FurnaceFiring Firing => _firing;
    public bool LiningIn => Assembly?.Build.Fitted(FurnaceRequires.Lining) == true;
    public bool LidFitted => Assembly?.Build.Fitted(FurnaceRequires.Lid) == true;
    /// <summary>The lid is fitted and shut.</summary>
    public bool LidClosed => LidFitted && !_lidOpen;
    public bool LidOpen => !LidClosed;
    /// <summary>The lid is still turning (both sides: from when each heard it move).</summary>
    public bool LidSwinging => LidFitted && Api != null && Api.World.ElapsedMilliseconds - _lidMovedMs < SwingMs;
    public Damper Damper => _damper;
    public bool Capped => _firing.Capped;
    public FurnacePot? Pot(int i) => i >= 0 && i < _pots.Length ? _pots[i] : null;
    public double Temperature => _heat.Temperature;
    public double Coke => _heat.Coke;
    public bool Lit => _heat.Lit;
    public FurnaceRow Row => _firing.Row;
    /// <summary>The row's draught, or null when it has none.</summary>
    public Draught? Draught => _draught;
    /// <summary>The top of the row's chimney, where it smokes; null with none.</summary>
    public Vec3d? ChimneyTop => _chimneyTop;
    /// <summary>The fired brick type of its brickwork (<c>red</c>), set by its first brick; null before.</summary>
    public string? BrickType =>
        Assembly?.Build.CodeIn(0, 0) is { } code && Rig is { } rig ? rig.Bricks.TypeOf(code) : null;
    public int CokeCapacity => _serverCokeCapacity ?? Config.CokeCapacity;

    private CrucibleFurnaceSystem System => CrucibleFurnaceSystem.Of(Api);
    private CrucibleFurnaceConfig Config => System.Config;
    private new CrucibleFurnaceRig? Rig => base.Rig as CrucibleFurnaceRig;

    /// <summary>Lit, the furnace works: its state is the fire's.</summary>
    public override bool HasWork => _heat.Lit;

    protected override int ClientTickMs => 100;

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        BuildBoxes();
        if (api.Side == EnumAppSide.Server)
            RegisterGameTickListener(_ => Update(), 1000);
    }

    /// <summary>The furnace's own renderer: the stages, the pots in their metals, the coke, the fire's glow, the
    /// lid's swing.</summary>
    protected override IRenderer CreateRenderer(ICoreClientAPI capi, IMachineRig rig) =>
        new CrucibleFurnaceRenderer(capi, this, (CrucibleFurnaceRig)rig);

    /// <summary>A world position's cell in the native frame.</summary>
    public Int3 LocalCell(BlockPos cellPos) => LocalOf(cellPos);

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
        if (Rig is not { } rig)
            return;
        var side = Side;
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
    public override Cuboidf[]? CellBoxes(BlockPos cellPos) => Boxes(cellPos, collision: false);

    /// <summary>A cell's collision boxes: as <see cref="CellBoxes"/>, the iron frame's ground row only
    /// before the brickwork, and the rig's decks on the top cells after it.</summary>
    public override Cuboidf[]? CollisionBoxes(BlockPos cellPos) => Boxes(cellPos, collision: true);

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
        bool brick = Assembly?.Build.Fitted(FurnaceRequires.Brickwork) == true;
        return (brick ? collision ? t.BuiltCollision : t.Built : collision ? t.FrameCollision : t.Frame).GetValueOrDefault(local);
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
        bool rowChanged = _firing.SetRow(row);
        var draught = row.Works ? FurnaceHeat.DraughtOf(row.ChimneyHeight, Config) : null;
        rowChanged |= draught != _draught;
        _draught = draught;
        var top = ChimneyTopOf(row);
        rowChanged |= !Equals(top, _chimneyTop);
        _chimneyTop = top;

        var before = _heat;
        var shares = _pots.Select(p => p == null ? -1 : (int)(FurnaceMelt.Share(p) * 100)).ToArray();
        _heat = FurnaceMelt.Run(_heat, hours, draught, LidClosed, _damper, _pots, System.Recipes, SolidPointOf, Config, out bool potsChanged);
        bool changed = rowChanged || potsChanged || (int)before.Temperature != (int)_heat.Temperature || before.Lit != _heat.Lit
                       || Math.Ceiling(before.Coke - 1e-9) != Math.Ceiling(_heat.Coke - 1e-9)
                       || _pots.Select(p => p == null ? -1 : (int)(FurnaceMelt.Share(p) * 100)).Where((s, i) => s != shares[i]).Any();
        if (changed)
            MarkDirty();
    }

    private readonly Dictionary<string, double?> _solidPoints = [];

    /// <summary>A pourable recipe's solid point: 0.9 of its output's melting point, as the game sets a
    /// smelted container's metal solid; null when the output has none.</summary>
    public double? SolidPointOf(string recipeCode)
    {
        if (_solidPoints.TryGetValue(recipeCode, out var known))
            return known;
        double? point = null;
        if (System.Recipes.Get(recipeCode) is { Pourable: true } recipe && CrucibleFurnaceSystem.Resolve(Api.World, recipe.Output) is { } output)
        {
            var metal = new ItemStack(output);
            float mp = metal.Collectible.GetMeltingPoint(Api.World, null, new DummySlot(metal));
            if (mp > 0)
                point = PourWindow.SolidifiesAt(mp);
        }
        return _solidPoints[recipeCode] = point;
    }

    /// <summary>Takes the heat of <paramref name="units"/> charged at <paramref name="itemTemperature"/>
    /// off the fire (<see cref="FurnaceMelt.ChargeChill"/>), lit or not.</summary>
    private void Chill(int units, double itemTemperature)
    {
        Update();
        _heat = _heat with { Temperature = FurnaceMelt.ChargeChill(_heat.Temperature, units, itemTemperature, Config) };
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

    /// <summary>The chimney standing on this furnace's outlet: its shaft, counted from the outlet up,
    /// and its top.</summary>
    public ChimneyColumn ChimneyOnOutlet() => Rig is { } rig ? FurnaceFiring.ChimneyOn(this, rig.Flue) : default;

    /// <summary>The controller of the furnace <paramref name="k"/> widths along the row (native +x), or null.</summary>
    public BECrucibleFurnace? Neighbour(int k) => Rig is { } rig ? FurnaceFiring.Beside<BECrucibleFurnace>(this, rig.Flue, k) : null;

    /// <summary>This furnace's row: its neighbours of the same facing side by side, each with its
    /// lining in (an unlined furnace has no flue yet, and ends the row).</summary>
    public FurnaceRow FindRow() =>
        Rig is { } rig
            ? _firing.Find(this, rig.Flue, k => Neighbour(k) is { LiningIn: true } other ? other : null, Config.MaxFurnacesInRow, Config.MinChimneyHeight)
            : FurnaceRow.Alone;

    private Vec3d? ChimneyTopOf(FurnaceRow row)
    {
        if (row.ChimneyAt is not { } c || Rig is not { } rig || (c == 0 ? this : Neighbour(c)) is not { } owner)
            return null;
        // over the chimney top, which stands on the shaft: its pots rise to 12 voxels
        var b = owner.CellPos(rig.ChimneyBase);
        return new Vec3d(b.X + 0.5, b.Y + row.ChimneyHeight + 0.75, b.Z + 0.5);
    }

    // ---- Interaction ----

    /// <summary>
    /// The outlet's cap, before the build's clicks: an iron plate on the outlet's top caps it (once the lining is
    /// in), and an empty hand there takes the plate back.
    /// </summary>
    protected override bool? InteractContent(IPlayer byPlayer, ItemSlot slot, BlockSelection sel)
    {
        if (Rig is not { } rig || !LiningIn || sel.Face != BlockFacing.UP || LocalCell(sel.Position) != rig.OutletCell)
            return null;
        var held = slot?.Itemstack;
        if (!Capped && held?.Collectible?.Code?.ToString() == rig.Flue.CapCode)
            return _firing.TryCap(this, rig.Flue, byPlayer, slot!);
        if (Capped && held == null)
        {
            _firing.Uncap(this, rig.Flue, byPlayer, stack => Give(byPlayer, stack));
            return true;
        }
        return null;
    }

    /// <summary>
    /// A right-click that is not the build's. Before it is built, a pot, coke or a charge is refused (the next
    /// stage first). Once it is built: an empty hand on the front works the ash-pit damper; on the hole (the top
    /// cells of its two rows) it opens the lid, pulls the nearer pot, or with no pot there shuts the lid, and on
    /// the open lid it shuts it (Shift: the lid, wherever); a fired pot goes into the nearer place, coke onto the
    /// fire (Shift: into the nearer pot), and a charge into the nearer pot. A firestarter or a torch lights it
    /// (<see cref="MachineFire"/>). Decided and done on the server; the client only says whether the click is the
    /// furnace's.
    /// </summary>
    protected override bool InteractWork(IPlayer byPlayer, ItemSlot slot, BlockSelection sel)
    {
        if (Rig is not { } rig || Assembly is not { } assembly)
            return false;
        var held = slot.Itemstack;
        string? code = held?.Collectible?.Code?.ToString();
        var controls = byPlayer.Entity.Controls;
        bool server = Api.Side == EnumAppSide.Server;
        var cell = LocalCell(sel.Position);
        var hit = sel.HitPosition ?? new Vec3d(0.5, 0.5, 0.5);
        var native = MachinePoints.NativeHit(Pos, sel.Position, hit, Side);

        if (!assembly.Built)
        {
            bool furnaceItem = code == CrucibleFurnaceSystem.FiredPot || code == "game:coke" || code != null && System.Recipes.IsIngredient(code);
            if (furnaceItem && server)
                Refuse(byPlayer, "notbuilt", NextStageName());
            return furnaceItem;
        }
        // a firestarter or a torch lights the coke (MachineFire)
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
                Pull(byPlayer, slot, pot);
            else
                ToggleLid(byPlayer);
            return true;
        }
        bool isPot = code == CrucibleFurnaceSystem.FiredPot;
        bool coke = code == "game:coke" && !controls.ShiftKey;
        if (!isPot && !coke && !System.Recipes.IsIngredient(code!))
        {
            if (IsHoleCell(cell) && server)
                Refuse(byPlayer, "notaningredient");
            return IsHoleCell(cell);
        }
        if (!server)
            return true;
        if (LidClosed)
            return Refuse(byPlayer, "lidclosed");
        if (isPot)
            PutPot(byPlayer, slot, pot);
        else if (coke)
            AddCoke(byPlayer, slot);
        else
            AddCharge(byPlayer, slot, pot);
        return true;
    }

    /// <summary>The last stage in: the lid goes in open, and the damper open.</summary>
    protected override void OnAssemblyChanged()
    {
        if (Built)
        {
            _lidOpen = true;
            _damper = Damper.Open;
        }
    }

    /// <summary>The next stage's name, as the errors show it.</summary>
    private string NextStageName() =>
        Assembly?.Build is { Next: { } next } build ? MachineText.StageName(Machine, build.Stages[next]) : "";

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
            return Refuse(byPlayer, "lidhot");
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
            Refuse(byPlayer, "haspot");
            return;
        }
        var stack = slot.Itemstack!;
        var pot = new FurnacePot { Heats = PotStack.Heats(stack), Charge = PotStack.Charge(stack) };
        // a pot charged outside goes in with its charge's heat to take from the fire
        if (pot.ChargeUnits > 0)
            Chill(pot.ChargeUnits, stack.Collectible.GetTemperature(Api.World, stack));
        _pots[i] = pot;
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
            Refuse(byPlayer, "cokefull", Config.CokeCapacity);
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
            Refuse(byPlayer, "nopot");
            return;
        }
        if (pot.Frozen)
        {
            Refuse(byPlayer, "frozen");
            return;
        }
        if (!pot.TakesCharge(System.Recipes))
        {
            Refuse(byPlayer, "molten");
            return;
        }
        var stack = slot.Itemstack!;
        string code = stack.Collectible.Code.ToString();
        double itemTemperature = stack.Collectible.GetTemperature(Api.World, stack);
        var add = pot.Add(System.Recipes, code, slot.StackSize, Config.PotCapacityUnits);
        if (add.Count <= 0)
        {
            Refuse(byPlayer, "refused-" + add.Refusal.ToString().ToLowerInvariant(), Config.PotCapacityUnits);
            return;
        }
        Chill(add.Count * PotRecipes.UnitsOf(code), itemTemperature);
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
            return Refuse(byPlayer, "needtongs");
        double t = _heat.Temperature;
        var world = Api.World;
        var recipe = System.Recipes.Get(pot.Melted);
        _pots[i] = null;
        MarkDirty(true);
        var at = WorldPoint(Rig!.PotPoints[i]);

        if (recipe == null)
        {
            // unmelted: the pot comes back with its charge, its progress lost (the charge cools)
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
            if (pot.Frozen || t < PourWindow.SolidifiesAt(meltingPoint))
            {
                // frozen in the furnace: the pot is lost, the metal knocked out
                world.PlaySoundAt(CrackSound, at.X, at.Y, at.Z, null);
                Give(byPlayer, Bits(metal, units / Stainless.UnitsPerLump));
                Refuse(byPlayer, "frozenhole", units / Stainless.UnitsPerLump);
                return true;
            }
            // what had not dissolved (or put the bath off every ratio) goes to the slag
            if (pot.Charge.Count > 0)
                Refuse(byPlayer, "additionslost", PotStack.Describe(world, pot.Charge), Lang.Get("material-" + (ingot.Variant["metal"] ?? "")));
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
            Refuse(byPlayer, "potcracked");
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

    private bool Refuse(IPlayer? byPlayer, string key, params object[] args)
    {
        if (!key.StartsWith("error-", StringComparison.Ordinal))
            key = "error-" + key;
        if (byPlayer is IServerPlayer sp)
            sp.SendIngameError("cruciblefurnace-" + key, Lang.GetL(sp.LanguageCode, CrucibleFurnaceSystem.Domain + ":cruciblefurnace-" + key, args));
        return false;
    }

    // ---- Lighting ----

    /// <summary>Lit with a firestarter or a torch on any of its cells, as the forge is: once it is built, when it can be.</summary>
    public override bool Ignitable => true;

    public override bool Burning => _heat.Lit;

    public override string? CannotIgnite() =>
        CannotLight() is { } why ? Lang.Get(CrucibleFurnaceSystem.Domain + ":cruciblefurnace-error-" + why, NextStageName()) : null;

    public override void Ignite(IPlayer? byPlayer) => TryLight(byPlayer);

    /// <summary>Whether a firestarter or torch can light the furnace now; else the error key.</summary>
    public string? CannotLight()
    {
        if (!Built)
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
            return Refuse(byPlayer, why, NextStageName());
        Update();
        _heat = _heat with { Lit = true };
        var at = WorldPoint(Rig!.Mouth);
        Api.World.PlaySoundAt(IgniteSound, at.X, at.Y, at.Z, null);
        MarkDirty();
        RefreshState();
        return true;
    }

    // ---- Breaking ----

    /// <summary>What breaking the furnace gives besides its frame: every fitted item, the outlet's cap,
    /// the unburnt coke, each pot with its charge; a melted charge as its products (molten metal as
    /// bits), the pot lost.</summary>
    public override IEnumerable<ItemStack> AssemblyDrops()
    {
        var world = Api.World;
        foreach (var drop in base.AssemblyDrops())
            yield return drop;
        if (Rig is { } rig && _firing.Drop(world, rig.Flue) is { } cap)
            yield return cap;
        int coke = (int)Math.Floor(_heat.Coke + 1e-9);
        if (coke > 0 && world.GetItem(new AssetLocation("game", "coke")) is { } cokeItem)
            yield return new ItemStack(cokeItem, coke);
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
                        yield return stack;
                }
                if (products.Byproducts > 0 && recipe.Byproduct != null && CrucibleFurnaceSystem.Resolve(world, recipe.Byproduct) is { } slag)
                    yield return new ItemStack(slag, products.Byproducts);
                continue;
            }
            var back = new ItemStack(world.GetBlock(new AssetLocation(CrucibleFurnaceSystem.FiredPot)));
            PotStack.SetHeats(back, pot.Heats);
            PotStack.SetCharge(back, pot.Charge);
            yield return back;
        }
    }

    // ---- Help ----

    /// <summary>The build's help, then the cap's once the lining is in.</summary>
    public override WorldInteraction[] InteractionHelp(IPlayer forPlayer)
    {
        var help = base.InteractionHelp(forPlayer).ToList();
        if (!Built && LiningIn && CapHelp() is { } cap)
            help.Add(cap);
        return help.ToArray();
    }

    private WorldInteraction? CapHelp()
    {
        if (Rig is not { } rig || FurnaceFiring.CapStack(Api.World, rig.Flue) is not { } plate)
            return null;
        return Capped
            ? new WorldInteraction { ActionLangCode = HelpKey("uncap"), MouseButton = EnumMouseButton.Right }
            : new WorldInteraction { ActionLangCode = HelpKey("cap"), MouseButton = EnumMouseButton.Right, Itemstacks = [plate] };
    }

    private static string HelpKey(string action) => CrucibleFurnaceSystem.Domain + ":blockhelp-cruciblefurnace-" + action;

    protected override void WorkHelp(IPlayer? forPlayer, List<WorldInteraction> help)
    {
        var world = Api.World;
        ItemStack[] Stacks(string code) => CrucibleFurnaceSystem.Resolve(world, code) is { } c ? [new ItemStack(c)] : [];
        if (CapHelp() is { } cap)
            help.Add(cap);
        help.Add(new WorldInteraction { ActionLangCode = HelpKey("lid"), MouseButton = EnumMouseButton.Right });
        help.Add(new WorldInteraction { ActionLangCode = HelpKey("damper"), MouseButton = EnumMouseButton.Right });
        help.Add(new WorldInteraction { ActionLangCode = HelpKey("pot"), MouseButton = EnumMouseButton.Right, Itemstacks = Stacks(CrucibleFurnaceSystem.FiredPot) });
        help.Add(new WorldInteraction { ActionLangCode = HelpKey("coke"), MouseButton = EnumMouseButton.Right, Itemstacks = Stacks("game:coke") });
        help.Add(new WorldInteraction { ActionLangCode = HelpKey("light"), MouseButton = EnumMouseButton.Right, Itemstacks = FurnaceFiring.IgniteStacks(world.Api) });
        help.Add(new WorldInteraction { ActionLangCode = HelpKey("pull"), MouseButton = EnumMouseButton.Right });
    }

    // ---- Client ----

    // A lit furnace flames at the hole while its lid is open and smokes at its row's chimney.
    protected override void OnClientTick(float dt)
    {
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

    /// <summary>After the shared lines (the build, the state): the brick, then once it is built the lid, the
    /// damper, the fire, the coke, each pot, and the row's draught.</summary>
    protected override void AppendContents(StringBuilder dsc)
    {
        if (Rig is null)
            return;
        string L(string key, params object[] args) => Lang.Get(CrucibleFurnaceSystem.Domain + ":cruciblefurnace-info-" + key, args);
        if (BrickType is { } brick)
            dsc.AppendLine(L("brick", BrickName(brick)));
        if (!Built)
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
        var c = Config;
        double fire = _heat.Temperature;
        string heats = PotStack.HeatsLine(pot.Heats, c.PotHeats);
        string RecipeName(PotRecipe r) => Lang.Get(CrucibleFurnaceSystem.Domain + ":cruciblefurnace-recipe-" + r.Code);
        string Percent() => ((int)Math.Floor(FurnaceMelt.Share(pot) * 100)).ToString();
        string Hours(double h) => h.ToString("0.0", global::System.Globalization.CultureInfo.InvariantCulture);
        void Shares(PotMatch match)
        {
            foreach (var (ingredient, share) in match.Shares)
                dsc.AppendLine(Line("share", Lang.Get(CrucibleFurnaceSystem.Domain + ":cruciblefurnace-ingredient-" + ingredient.Name),
                    (int)Math.Round(share * 100), (int)Math.Round(ingredient.Min * 100), (int)Math.Round(ingredient.Max * 100)));
        }
        // how the charge (or the additions) is getting on: melting, or paused below the recipe's heat
        void Progress(PotRecipe r, string melting, string paused)
        {
            int needs = (int)r.Temperature;
            if (FurnaceMelt.HoursLeft(pot, recipes, fire, c) is { } left)
                dsc.AppendLine(Line(melting, RecipeName(r), needs, Percent(), Hours(left)));
            else
                dsc.AppendLine(Line(paused, RecipeName(r), needs, Percent(), needs - (int)Math.Round(fire)));
        }

        if (recipes.Get(pot.Melted) is { } melted)
        {
            var output = CrucibleFurnaceSystem.Resolve(world, melted.Output);
            string metal = output == null ? melted.Output
                : output.Variant["metal"] is { } m ? Lang.Get("material-" + m) : new ItemStack(output).GetName();
            int units = PotRecipes.Products(melted, pot.MeltedUnits).Units;
            if (pot.Frozen)
            {
                dsc.AppendLine(Line("frozen", name, units, metal, heats));
                return;
            }
            dsc.AppendLine(melted.Pourable ? Line("molten", name, units, metal, heats)
                : Line("ready", name, PotRecipes.Products(melted, pot.MeltedUnits).Lumps, metal, heats));
            if (!melted.Pourable)
                return;
            if (pot.Charge.Count > 0)
            {
                string added = PotStack.Describe(world, pot.Charge);
                var match = recipes.Match(pot.Mix(recipes));
                if (match.Recipe is { } r)
                {
                    dsc.AppendLine(Line("added", added, pot.ChargeUnits, RecipeName(r)));
                    Progress(r, "dissolving", "dissolving-paused");
                }
                else
                {
                    dsc.AppendLine(Line("offratio", match.Closest is { } closest ? RecipeName(closest) : metal, units, metal, added));
                    Shares(match);
                }
            }
            if (SolidPointOf(melted.Code) is { } solid && fire < solid)
                dsc.AppendLine(Line("belowsolid", (int)Math.Round(solid), (int)Math.Ceiling(FurnaceMelt.FreezesIn(pot, c) * 60)));
            return;
        }
        if (pot.Charge.Count == 0)
        {
            dsc.AppendLine(Line("empty", name, c.PotCapacityUnits, heats));
            return;
        }
        dsc.AppendLine(Line("charge", name, PotStack.Describe(world, pot.Charge), pot.ChargeUnits, c.PotCapacityUnits, heats));
        var charge = recipes.Match(pot.Charge);
        if (charge.Recipe is { } recipe)
            Progress(recipe, "makes-hot", "makes");
        else if (charge.Closest is { } closest)
        {
            dsc.AppendLine(Line(charge.TooSmall ? "toosmall" : "wrongratio", RecipeName(closest)));
            Shares(charge);
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
        var row = Row;
        var c = Config;
        string chimney = row.ChimneyAt is { } at ? Where(at) : "";
        string members = row.Count > 1 ? Line("row", row.Count, chimney) : "";
        dsc.AppendLine(row.Problem switch
        {
            RowProblem.TooManyFurnaces => Line("draught-toomany", c.MaxFurnacesInRow),
            RowProblem.NoChimney => Line(Capped && row.Count == 1 ? "draught-capped" : "draught-none", c.MinChimneyHeight),
            RowProblem.OutletOpen => Line("draught-open", Where(row.OpenOutlet ?? 0), chimney),
            RowProblem.ChimneyTooShort => Line("draught-short", row.ChimneyHeight, c.MinChimneyHeight),
            RowProblem.NoChimneyTop => Line("draught-notop", row.ChimneyHeight),
            RowProblem.ChimneyTopBuried => Line("draught-buried", row.ChimneyHeight),
            _ => _draught is { } d
                ? Line(d.Height > c.MaxChimneyHeight ? "draught-max" : "draught", d.Height, (int)Math.Round(FurnaceHeat.HeatingRate(d, _damper, c)), (int)Math.Round(d.MaxTemperature), c.MaxChimneyHeight)
                : Line("draught-none", c.MinChimneyHeight),
        });
        if (members.Length > 0 && row.Problem is RowProblem.None or RowProblem.ChimneyTooShort or RowProblem.NoChimneyTop or RowProblem.ChimneyTopBuried)
            dsc.AppendLine(members);
    }

    /// <summary>For a wrench: the fire's exact heat and coke.</summary>
    protected override void AppendDetail(StringBuilder dsc)
    {
        if (!Built)
            return;
        string Num(double x, string format) => x.ToString(format, global::System.Globalization.CultureInfo.InvariantCulture);
        dsc.AppendLine(Lang.Get(CrucibleFurnaceSystem.Domain + ":cruciblefurnace-detail-fire", Num(_heat.Temperature, "0.0"), Num(_heat.Coke, "0.00")));
    }

    public static string BrickName(string? type) =>
        type == null ? "" : Lang.Get(CrucibleFurnaceSystem.Domain + ":cruciblefurnace-brick-" + type);

    // ---- Saving and syncing ----

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
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
            p.SetDouble("progress", pot.Progress);
            p.SetDouble("belowSolidHours", pot.BelowSolidHours);
            p.SetBool("frozen", pot.Frozen);
            tree[k] = p;
        }
        tree.SetDouble("temperature", _heat.Temperature);
        tree.SetDouble("coke", _heat.Coke);
        tree.SetBool("lit", _heat.Lit);
        tree.SetDouble("lastHours", _lastHours);
        tree.SetBool("lidOpen", _lidOpen);
        tree.SetInt("damper", (int)_damper);
        _firing.Write(tree);
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
                // a save from before melting took time: a melted pot stays melted, a charge starts at none
                Progress = p.GetDouble("progress"),
                BelowSolidHours = p.GetDouble("belowSolidHours"),
                Frozen = p.GetBool("frozen"),
            };
        }
        _heat = new HoleHeat(tree.GetDouble("temperature", FurnaceHeat.Ambient), tree.GetDouble("coke"), tree.GetBool("lit"));
        _lastHours = tree.GetDouble("lastHours", double.NaN);
        bool lidOpen = tree.GetBool("lidOpen", true);
        if (lidOpen != _lidOpen && Api != null)
            _lidMovedMs = Api.World.ElapsedMilliseconds;
        _lidOpen = lidOpen;
        _damper = (Damper)Math.Clamp(tree.GetInt("damper"), 0, 2);
        _firing.Read(tree);
        _draught = tree.HasAttribute("draughtRate")
            ? new Draught(tree.GetDouble("draughtRate"), tree.GetDouble("draughtMax"), tree.GetInt("draughtHeight"))
            : null;
        _chimneyTop = tree.HasAttribute("chimneyX") ? new Vec3d(tree.GetDouble("chimneyX"), tree.GetDouble("chimneyY"), tree.GetDouble("chimneyZ")) : null;
        if (worldForResolving.Side == EnumAppSide.Client)
            _serverCokeCapacity = tree.TryGetInt("cokeCapacity");
    }
}
