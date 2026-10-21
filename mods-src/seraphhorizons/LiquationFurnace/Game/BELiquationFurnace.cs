using System.Globalization;
using System.Text;
using SeraphHorizons.Mod.CrucibleFurnace;
using SeraphHorizons.Mod.CrucibleFurnace.Core;
using SeraphHorizons.Mod.LiquationFurnace.Core;
using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.Ore.Core;
using SeraphHorizons.Mod.Ore.Processing;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.LiquationFurnace;

/// <summary>
/// The liquation furnace (#929; README "Ore processing: the liquation furnace"): a sloping cast-iron hearth plate over a
/// fire, on the shared ore machine code (<see cref="BEOreMachine"/>), batch, loaded and emptied by hand, with no power,
/// no water and no chute (<see cref="LiquationFurnacing"/> has its rules). Built as its build file says (the iron
/// frame, then the brickwork in one fired brick type, the lining, and the fittings: the plate, the pot, the fire door
/// and the damper). Tin-lead bullion bars go on the plate from the front; fuel goes on the grate through the open fire
/// door at the back, where the damper is set too; lit with a firestarter or a torch, the fire burns its fuel and, with
/// the row's chimney drawing (the crucible furnace's row rule, <see cref="FurnaceRow"/>), the charge sweats: done, the
/// tin is in the pot and the lead on the plate, each taken out by hand from the front as ingots and bits.
/// <para>Its inventory: slot 0 (the shared infeed slot) the fuel on the grate, the rest the bars on the plate; all
/// drop when it is broken, with the cap.</para>
/// </summary>
public class BELiquationFurnace : BEFiredOreMachine
{
    private const int FirstChargeSlot = 1;
    private static readonly AssetLocation MetalSound = new("game", "sounds/block/ingot");

    private bool _lit, _doorShut, _sweating;
    private Damper _damper;
    private double _sweated, _fuelBurnt;
    private double _tinUnits, _leadUnits;
    private string _metal = "tin", _byProduct = "lead";
    private double _lastHours = double.NaN;
    private double _syncedSweated;

    public override OreMachine? Kind => OreMachine.LiquationFurnace;

    /// <summary>Nothing goes in by chute: the furnace is charged and stoked by hand (<see cref="InteractContent"/>).</summary>
    public override bool Takes(ItemStack stack) => false;

    protected override IEnumerable<(string Anchor, ItemStack Stack)> Products(ItemStack fed) => [];

    protected override IReadOnlyList<string> OutputAnchors => [];

    private LiquationFurnaceSystem Furnace => LiquationFurnaceSystem.Of(Api);
    private LiquationFurnaceConfig FurnaceConfig => Furnace.Config;
    public LiquationFurnaceRig? FurnaceRig => Api == null ? null : Furnace.Rig;
    protected override FiredRig? FiredRig => FurnaceRig;
    private OreRecovery Recovery => OreProcessingSystem.RecoveryFor(Api);

    public override bool Lit => _lit;
    public bool DoorShut => _doorShut;
    public Damper Damper => _damper;

    /// <summary>Whether the charge sweated in the last tick: the tin running.</summary>
    public bool Sweating => _sweating;

    /// <summary>Units of the charge sweated so far.</summary>
    public double SweatedUnits => _sweated;

    /// <summary>Tin in the pot and lead on the plate, in metal units (a fraction under a bit carried).</summary>
    public double TinUnits => _tinUnits;
    public double LeadUnits => _leadUnits;

    public int Fuel => InfeedSlot.StackSize;
    public int Capacity => FurnaceConfig.ChargeBars;

    public IEnumerable<ItemSlot> ChargeSlots => Enumerable.Range(FirstChargeSlot, Inventory.Count - FirstChargeSlot).Select(i => Inventory[i]);

    /// <summary>The bars on the plate.</summary>
    public int Bars => ChargeSlots.Sum(s => s.StackSize);

    public IEnumerable<LiquationBars> Charge => ChargeSlots
        .Select(s => (Ore: s.Itemstack is { } st ? LiquationFurnacing.OreOf(Recovery, st.Collectible.Code.ToString()) : null, s.StackSize,
            Whole: s.Itemstack is { } bar ? OreStreamAttributes.WholeShareOf(bar) : 0))
        .Where(b => b.Ore != null)
        .Select(b => new LiquationBars(b.Ore!, b.StackSize, b.Whole));

    public double ChargeUnits => LiquationFurnacing.ChargeUnits(Charge);

    public bool ProductWaiting => LiquationFurnacing.Waiting(_tinUnits) || LiquationFurnacing.Waiting(_leadUnits);

    public LiquationPhase Phase => LiquationFurnacing.PhaseOf(Bars, ProductWaiting);

    /// <summary>The charge's units sweated, while it sweats; none while lit with nothing sweating.</summary>
    public override double? WorkProgress =>
        Phase != LiquationPhase.Charged || !_sweating ? null : ChargeUnits is var units && units > 0 ? _sweated / units : 1;

    /// <summary>The units of tin an hour at full pace (ore-processing.json <c>machines.liquationfurnace.perHour</c>).</summary>
    public double PerHour => Spec?.ItemsPerHour ?? 0;

    /// <summary>The share of its full pace the charge sweats at now.</summary>
    public double Pace => LiquationFurnacing.Pace(_lit, Row.Works, _damper, _doorShut, FurnaceConfig);

    /// <summary>The fire burns while lit, charge or none.</summary>
    public override bool HasWork => _lit;

    public override double JobFill => _lit ? 1 : 0;

    /// <summary>The fire door: shut (1) or slid up open (0, the rest pose).</summary>
    public override double PartDepth => _doorShut ? 1 : 0;

    // ---- The fire and the sweating ----

    protected override bool WorkUnpowered(float dt)
    {
        double now = Api.World.Calendar.TotalHours;
        double hours = double.IsNaN(_lastHours) ? 0 : now - _lastHours;
        _lastHours = now;
        if (hours <= 0)
            return false;
        bool changed = false;
        var (burnt, pieces, burning, lit) = LiquationFurnacing.Burn(_fuelBurnt, Fuel, hours, _damper, FurnaceConfig);
        _fuelBurnt = burnt;
        if (pieces > 0)
        {
            InfeedSlot.TakeOut(Math.Min(pieces, Fuel));
            InfeedSlot.MarkDirty();
            changed = true;
        }
        double pace = Pace;
        bool sweating = pace > 0 && Bars > 0 && !ProductWaiting;
        if (sweating)
        {
            var (sweated, done) = LiquationFurnacing.Advance(_sweated, burning, pace, PerHour, ChargeUnits);
            _sweated = sweated;
            if (done)
            {
                Finish();
                sweating = false;
                changed = true;
            }
            else if (_sweated - _syncedSweated >= Math.Max(1, ChargeUnits / 100))
            {
                _syncedSweated = _sweated;
                changed = true;
            }
        }
        if (sweating != _sweating)
        {
            _sweating = sweating;
            changed = true;
        }
        if (!lit)
        {
            _lit = false;
            _sweating = false;
            _fuelBurnt = 0;
            InfeedSlot.Itemstack = null;
            InfeedSlot.MarkDirty();
            changed = true;
        }
        return changed;
    }

    /// <summary>Ages a lit furnace by <paramref name="hours"/>, as if they had passed (tests).</summary>
    public void Age(double hours) => _lastHours = (double.IsNaN(_lastHours) ? Api.World.Calendar.TotalHours : _lastHours) - hours;

    /// <summary>The charge sweated through: its tin into the pot, its lead left on the plate (with any fraction the
    /// furnace carried from its last charge), the bars gone.</summary>
    private void Finish()
    {
        var y = LiquationFurnacing.Yield(Recovery, Charge.ToList());
        if (y.Metal.Length > 0) _metal = y.Metal;
        if (y.ByProduct.Length > 0) _byProduct = y.ByProduct;
        _tinUnits += y.MetalUnits;
        _leadUnits += y.ByProductUnits;
        foreach (var slot in ChargeSlots.Where(s => !s.Empty))
        {
            slot.Itemstack = null;
            slot.MarkDirty();
        }
        _sweated = 0;
        _syncedSweated = 0;
        MarkDirty(true);
    }

    // ---- Fire ----

    public override string? CannotIgnite() =>
        !Built ? Lang.Get("seraphhorizons:liquationfurnace-error-notbuilt")
        : LiquationFurnacing.CannotLight(_lit, Fuel) is var r and not LiquationFurnaceRefusal.None ? RefusalText(r)
        : null;

    protected override void Light(IPlayer? byPlayer)
    {
        _lit = true;
        _fuelBurnt = 0;
        _lastHours = Api.World.Calendar.TotalHours;
        Api.World.PlaySoundAt(new AssetLocation("game", "sounds/torch-ignite"), Pos.X + 0.5, Pos.Y + 0.5, Pos.Z + 0.5, byPlayer, true, 16, 0.6f);
        if (byPlayer != null)
            Api.World.Logger.Audit("{0} lit the liquation furnace at {1} ({2} bars).", byPlayer.PlayerName, Pos, Bars);
    }

    // ---- Interaction ----

    private static string RefusalText(LiquationFurnaceRefusal r, params object[] args) =>
        Lang.Get("seraphhorizons:liquationfurnace-error-" + r.ToString().ToLowerInvariant(), args);

    private bool Refuse(IPlayer byPlayer, LiquationFurnaceRefusal r, params object[] args)
    {
        if (Api.Side == EnumAppSide.Server && byPlayer is IServerPlayer sp)
            sp.SendIngameError("liquationfurnace-" + r.ToString().ToLowerInvariant(), RefusalText(r, args));
        return true;
    }

    /// <summary>Whether a click is on the back face (the fire door and the ash mouth), and if so whether on the ash
    /// mouth (under the midpoint between them, <see cref="LiquationFurnaceRig.IsAsh"/>).</summary>
    private (bool Back, bool Ash) BackFace(BlockSelection sel)
    {
        if (FurnaceRig is not { } rig || sel.Face == null)
            return (false, false);
        var world = CellFaces.ToWorld((CellFace)(int)rig.BackSide, Side);
        if (sel.Face.Code != world.Code())
            return (false, false);
        double y = sel.Position.Y - Pos.Y + (sel.HitPosition?.Y ?? 0.5);
        return (true, rig.IsAsh(y));
    }

    /// <summary>
    /// Once built: tin-lead bars charge the plate, fuel goes on the grate (through the open fire door), an iron plate
    /// caps the outlet; an empty hand on the back face opens or shuts the fire door, or on the ash mouth moves the
    /// damper a place; an empty hand elsewhere takes out the tin from the pot, then the lead off the plate, or on the
    /// outlet's top takes the cap off. Lighting is a firestarter's or a torch's (<see cref="Ignite"/>). Before it is
    /// built, the build's.
    /// </summary>
    protected override bool? InteractContent(IPlayer byPlayer, ItemSlot slot, BlockSelection blockSel)
    {
        if (!Built || FurnaceRig is not { } rig)
            return null;
        var stack = slot?.Itemstack;
        bool server = Api.Side == EnumAppSide.Server;
        if (stack == null)
        {
            var (back, ash) = BackFace(blockSel);
            if (back)
            {
                if (server)
                {
                    if (ash)
                        _damper = LiquationFurnacing.NextDamper(_damper);
                    else
                        _doorShut = !_doorShut;
                    Api.World.PlaySoundAt(new AssetLocation("game", "sounds/block/anvil1"), Pos, -0.25, byPlayer, true, 16, 0.3f);
                    MarkDirty(true);
                }
                return true;
            }
            if (Capped && IsOutletTop(blockSel))
            {
                Uncap(byPlayer);
                return true;
            }
            if (LiquationFurnacing.Waiting(_tinUnits))
            {
                if (server)
                    _tinUnits = TakeMetal(byPlayer, _metal, _tinUnits);
                return true;
            }
            if (LiquationFurnacing.Waiting(_leadUnits))
            {
                if (server)
                    _leadUnits = TakeMetal(byPlayer, _byProduct, _leadUnits);
                return true;
            }
            return null;
        }

        string code = stack.Collectible.Code.ToString();
        if (LiquationFurnacing.IsFuel(code))
        {
            var (take, why) = LiquationFurnacing.Stoke(_doorShut, InfeedSlot.Itemstack?.Collectible.Code.ToString(), Fuel, code, stack.StackSize, FurnaceConfig.GrateFuel);
            if (why != LiquationFurnaceRefusal.None)
                return Refuse(byPlayer, why);
            if (server)
            {
                MoveInto(slot!, InfeedSlot, take);
                Api.World.PlaySoundAt(new AssetLocation("game", "sounds/block/gravel"), Pos, -0.25, byPlayer);
                MarkDirty(true);
            }
            return true;
        }
        if (!Capped && code == rig.CapCode)
            return Cap(byPlayer, slot!);
        if (Bullion.OreOf(code) != null)
        {
            if (LiquationFurnacing.OreOf(Recovery, code) == null)
                return Refuse(byPlayer, LiquationFurnaceRefusal.NotTinLead);
            var (take, why) = LiquationFurnacing.Charge(Phase, Bars, stack.StackSize, Capacity);
            if (why != LiquationFurnaceRefusal.None)
                return Refuse(byPlayer, why, Capacity);
            if (server)
            {
                int moved = PutCharge(slot!, take);
                if (moved <= 0)
                    return Refuse(byPlayer, LiquationFurnaceRefusal.Full, Capacity);
                Api.World.PlaySoundAt(MetalSound, Pos.X + 0.5, Pos.Y + 0.5, Pos.Z + 0.5, byPlayer, true, 16, 0.5f);
                MarkDirty(true);
            }
            return true;
        }
        return null;
    }

    /// <summary>Gives the player <paramref name="units"/> of <paramref name="metal"/> as whole ingots and bits; the
    /// units left (under a bit), carried in the furnace.</summary>
    private double TakeMetal(IPlayer byPlayer, string metal, double units)
    {
        var (ingots, bits, carried) = LiquationFurnacing.Whole(units);
        if (ingots > 0 && Api.World.GetItem(new AssetLocation("game", "ingot-" + metal)) is { Id: > 0 } ingot)
            Give(byPlayer, new ItemStack(ingot, ingots));
        else
            bits += (int)Math.Round(ingots * LiquationFurnacing.UnitsPerIngot / LiquationFurnacing.UnitsPerBit);
        if (bits > 0 && Api.World.GetItem(new AssetLocation("game", "metalbit-" + metal)) is { Id: > 0 } bit)
            Give(byPlayer, new ItemStack(bit, bits));
        Api.World.PlaySoundAt(MetalSound, Pos.X + 0.5, Pos.Y + 0.5, Pos.Z + 0.5, byPlayer, true, 16, 0.7f);
        MarkDirty(true);
        return carried;
    }

    private void MoveInto(ItemSlot from, ItemSlot into, int count)
    {
        var op = new ItemStackMoveOperation(Api.World, EnumMouseButton.Right, 0, EnumMergePriority.AutoMerge, count);
        from.TryPutInto(into, ref op);
        from.MarkDirty();
        into.MarkDirty();
    }

    /// <summary>Puts <paramref name="count"/> of the held bars on the plate, merging with their kind; the number moved.</summary>
    private int PutCharge(ItemSlot from, int count)
    {
        int moved = 0;
        foreach (var target in ChargeSlots)
        {
            if (moved >= count || from.Empty)
                break;
            if (!target.Empty && !OreStreamAttributes.SameBar(Api.World, target.Itemstack, from.Itemstack!))
                continue;
            int before = from.StackSize;
            MoveInto(from, target, count - moved);
            moved += before - from.StackSize;
        }
        return moved;
    }

    public override WorldInteraction[] InteractionHelp(IPlayer forPlayer)
    {
        if (!Built || FurnaceRig is not { } rig)
            return base.InteractionHelp(forPlayer);
        var world = Api.World;
        ItemStack[] Of(IEnumerable<string> codes) => codes
            .Select(c => (CollectibleObject?)world.GetItem(new AssetLocation(c)) ?? world.GetBlock(new AssetLocation(c)))
            .OfType<CollectibleObject>().Where(c => c.Id > 0).Select(c => new ItemStack(c)).ToArray();
        string Key(string k) => "seraphhorizons:liquationfurnace-help-" + k;
        var help = new List<WorldInteraction>();
        switch (Phase)
        {
            case LiquationPhase.Empty or LiquationPhase.Charged when Bars < Capacity:
                help.Add(new WorldInteraction
                {
                    ActionLangCode = Key("charge"), MouseButton = EnumMouseButton.Right,
                    Itemstacks = Of(Recovery.Ores.Where(o => Bullion.TakenBy(Recovery, o, LiquationFurnacing.Station)).Select(Bullion.Code)),
                });
                break;
            case LiquationPhase.Sweated:
                help.Add(new WorldInteraction { ActionLangCode = Key(LiquationFurnacing.Waiting(_tinUnits) ? "tin" : "lead"), MouseButton = EnumMouseButton.Right });
                break;
        }
        if (!_doorShut && Fuel < FurnaceConfig.GrateFuel)
            help.Add(new WorldInteraction { ActionLangCode = Key("stoke"), MouseButton = EnumMouseButton.Right, Itemstacks = Of(LiquationFurnacing.Fuels) });
        if (!_lit && Fuel > 0)
            help.Add(new WorldInteraction
            {
                ActionLangCode = Key("light"), MouseButton = EnumMouseButton.Right,
                Itemstacks = FurnaceFiring.IgniteStacks(world.Api),
            });
        help.Add(new WorldInteraction { ActionLangCode = Key(_doorShut ? "opendoor" : "shutdoor"), MouseButton = EnumMouseButton.Right });
        help.Add(new WorldInteraction { ActionLangCode = Key("damper"), MouseButton = EnumMouseButton.Right });
        help.Add(Capped
            ? new WorldInteraction { ActionLangCode = Key("uncap"), MouseButton = EnumMouseButton.Right }
            : new WorldInteraction { ActionLangCode = Key("cap"), MouseButton = EnumMouseButton.Right, Itemstacks = Of([rig.CapCode]) });
        return help.ToArray();
    }

    // ---- Drawing ----

    /// <summary>
    /// The stages as built; then by the furnace's state: the cakes while bars are on the plate, the tin running while
    /// they sweat, the tin in the pot and the lead on the plate once sweated, the fuel's layers (burning while lit), the
    /// damper's place and the cap.
    /// </summary>
    public override bool ShowsPart(string? requires)
    {
        if (requires is null or LiquationRequires.Brickwork or LiquationRequires.Lining or LiquationRequires.Fittings)
            return base.ShowsPart(requires);
        if (!Built || FurnaceRig is not { } rig)
            return false;
        switch (requires)
        {
            case LiquationRequires.Cakes: return Bars > 0;
            case LiquationRequires.TinRun: return _sweating;
            case LiquationRequires.Tin: return LiquationFurnacing.Waiting(_tinUnits);
            case LiquationRequires.Lead: return LiquationFurnacing.Waiting(_leadUnits);
            case LiquationRequires.OutletCap: return Capped;
        }
        foreach (var d in Enum.GetValues<Damper>())
            if (requires == LiquationRequires.DamperPart(d))
                return _damper == d;
        int layers = LiquationFurnacing.FuelLayers(Fuel, FurnaceConfig.GrateFuel, rig.FuelLayers);
        if (requires.StartsWith("fire", StringComparison.Ordinal))
            return _lit && Layer(requires, "fire") <= layers;
        if (requires.StartsWith("fuel", StringComparison.Ordinal))
            return !_lit && Layer(requires, "fuel") <= layers;
        return false;
    }

    private static int Layer(string requires, string prefix) =>
        int.TryParse(requires.AsSpan(prefix.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : int.MaxValue;

    // ---- Client ----

    private readonly ParticleTimer _drip = new(0.5f);

    /// <summary>The fire's (<see cref="BEFiredOreMachine"/>); while the charge sweats, the metal it gives (the tin) drips
    /// off the spout's lip into the pot, in its colour, not glowing (it runs far under the game's glow).</summary>
    protected override void OnClientTick(float dt)
    {
        base.OnClientTick(dt);
        if (Api is not ICoreClientAPI capi || Rig is not { } rig || !_lit || !_sweating || !_drip.Due(dt) || rig.Point("spout") is not { } spout)
            return;
        (MachineParticles.Drip(MachineParticles.MetalColour(_metal, 230, MachineParticles.Molten)) with { Life = 0.45f })
            .Spawn(capi.World, WorldPoint(spout), MachineParticles.Around(0.01));
    }

    // ---- Breaking ----

    /// <summary>The frame and every stage's items, the cap, and the tin and lead not yet taken out. The fuel and the bars
    /// drop with the inventory.</summary>
    public override IEnumerable<ItemStack> AssemblyDrops()
    {
        foreach (var drop in base.AssemblyDrops())
            yield return drop;
        foreach (var (metal, units) in new[] { (_metal, _tinUnits), (_byProduct, _leadUnits) })
        {
            var (ingots, bits, _) = LiquationFurnacing.Whole(units);
            if (ingots > 0 && Api.World.GetItem(new AssetLocation("game", "ingot-" + metal)) is { Id: > 0 } ingot)
                yield return new ItemStack(ingot, ingots);
            if (bits > 0 && Api.World.GetItem(new AssetLocation("game", "metalbit-" + metal)) is { Id: > 0 } bit)
                yield return new ItemStack(bit, bits);
        }
    }

    // ---- Saving and syncing ----

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetBool("lit", _lit);
        tree.SetBool("doorShut", _doorShut);
        tree.SetBool("sweating", _sweating);
        tree.SetInt("damper", (int)_damper);
        tree.SetDouble("sweated", _sweated);
        tree.SetDouble("fuelBurnt", _fuelBurnt);
        tree.SetDouble("tinUnits", _tinUnits);
        tree.SetDouble("leadUnits", _leadUnits);
        tree.SetString("metal", _metal);
        tree.SetString("byProduct", _byProduct);
        tree.SetDouble("lastHours", _lastHours);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);
        _lit = tree.GetBool("lit");
        _doorShut = tree.GetBool("doorShut");
        _sweating = tree.GetBool("sweating");
        _damper = (Damper)Math.Clamp(tree.GetInt("damper"), 0, 2);
        _sweated = Math.Max(0, tree.GetDouble("sweated"));
        _fuelBurnt = Math.Max(0, tree.GetDouble("fuelBurnt"));
        _tinUnits = Math.Max(0, tree.GetDouble("tinUnits"));
        _leadUnits = Math.Max(0, tree.GetDouble("leadUnits"));
        _metal = tree.GetString("metal", "tin");
        _byProduct = tree.GetString("byProduct", "lead");
        _lastHours = tree.GetDouble("lastHours", double.NaN);
    }

    // ---- Info ----

    protected override void AppendContents(StringBuilder dsc)
    {
        if (!Built || FurnaceRig is not { } rig)
            return;
        string L(string key, params object[] args) => Lang.Get("seraphhorizons:liquationfurnace-info-" + key, args);
        string F(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);
        switch (Phase)
        {
            case LiquationPhase.Empty:
                dsc.AppendLine(L("empty", Capacity));
                break;
            case LiquationPhase.Charged:
                dsc.AppendLine(L("charged", Bars, Capacity));
                if (_sweating)
                {
                    double units = ChargeUnits;
                    dsc.AppendLine(L("sweating", (int)Math.Floor(100 * (units > 0 ? _sweated / units : 1)),
                        F(Math.Max(0, (units - _sweated) / Math.Max(1e-6, PerHour * Pace)))));
                }
                break;
            case LiquationPhase.Sweated:
                dsc.AppendLine(L("sweated", F(Math.Floor(_tinUnits)), Lang.Get("material-" + _metal), F(Math.Floor(_leadUnits)), Lang.Get("material-" + _byProduct)));
                break;
        }
        dsc.AppendLine(_lit ? L("lit", Fuel, FurnaceConfig.GrateFuel) : Fuel > 0 ? L("laid", Fuel, FurnaceConfig.GrateFuel) : L("nofuel"));
        dsc.AppendLine(L("door-" + (_doorShut ? "shut" : "open")) + " " + L("damper-" + _damper.ToString().ToLowerInvariant()));
        if (_lit && Bars > 0 && !ProductWaiting && Pace <= 0)
            dsc.AppendLine(L(!Row.Works ? "nodraught-chimney" : "nodraught-damper"));
        dsc.AppendLine(RowLine());
    }
}
