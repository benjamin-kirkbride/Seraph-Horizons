using System.Globalization;
using System.Text;
using SeraphHorizons.Mod.AcidParting.Core;
using SeraphHorizons.Mod.CrucibleFurnace;
using SeraphHorizons.Mod.CrucibleFurnace.Core;
using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.Ore.Core;
using SeraphHorizons.Mod.Ore.Processing;
using SeraphHorizons.Mod.Rocker;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.AcidParting;

/// <summary>
/// The acid parting vessel (#931; README "Ore processing: the acid parting vessel"): a stoneware vessel of nitric acid
/// in a sand bath over a small fire, under a hood, with a precipitating tub, on the shared ore machine code
/// (<see cref="BEOreMachine"/>): batch, loaded and emptied by hand, with no power and no chute
/// (<see cref="AcidPartingRules"/> has its rules). Built as its build file says (the iron frame, the brickwork in one
/// fired brick type, the lining, the sand bath and doors, the vessel and tub, the lead siphon), with a copper plate
/// hung in the tub (its wearing part). Then dore goes into the vessel with nitric acid enough for the charge, wash
/// water from a bucket; with fuel laid through the fire door and lit and the row's chimney drawing, it parts at ore
/// processing's rate: the silver, siphoned into the tub and thrown down on the copper, comes out there as silver bits
/// (drawn by hand at any time); the gold, as gold bits, is taken out of the vessel once the whole charge is parted.
/// <para>Its inventory: slot 0 (the shared infeed slot) the fire's fuel, slot 1 the dore before it dissolves, slots
/// <see cref="FirstGoldSlot"/> on the gold, slots <see cref="FirstSilverSlot"/> on the silver; all drop when it is
/// broken, with the cap. The acid and the water are lost with it.</para>
/// </summary>
public class BEAcidParting : BEOreMachine
{
    public const int DoreSlot = 1, FirstGoldSlot = 2, FirstSilverSlot = 10;
    private const int LastSlot = MaxOutputs * SlotsPerOutput;
    public const string CopperPart = "copper";
    private const string GoldKey = "gold", SilverKey = "silver";
    private static readonly AssetLocation PourSound = new("game", "sounds/effect/water-pour");

    private Damper _damper;
    private bool _capped, _lit, _started;
    private double _fireLeft, _chargeUnits, _parted, _acid;
    private double _lastHours = double.NaN;
    private string? _ore;
    private readonly UnitCarry _carry = new();
    private FurnaceRow _row = new(0, 1, null, 0, null, RowProblem.NoChimney);
    private double _syncedParted;

    public override OreMachine? Kind => OreMachine.AcidParting;

    /// <summary>Nothing goes in by chute: the vessel is charged by hand (<see cref="InteractContent"/>).</summary>
    public override bool Takes(ItemStack stack) => false;

    protected override IEnumerable<(string Anchor, ItemStack Stack)> Products(ItemStack fed) => [];

    protected override IReadOnlyList<string> OutputAnchors => [];

    private AcidPartingSystem VesselSystem => AcidPartingSystem.Of(Api);
    public AcidPartingConfig VesselConfig => VesselSystem.Config;
    public AcidPartingRig? VesselRig => Api == null ? null : VesselSystem.Rig;

    public Damper Damper => _damper;
    public bool Capped => _capped;
    public bool Lit => _lit;
    public FurnaceRow Row => _row;
    /// <summary>Litres of nitric acid in the vessel (weak acid kept from the last charge counted).</summary>
    public double Acid => _acid;
    /// <summary>Units of the charge parting (its bars' gold), once started.</summary>
    public double ChargeUnits => _chargeUnits;
    /// <summary>Units of the charge parted so far.</summary>
    public double Parted => _parted;
    /// <summary>In-game hours of fire left in the fuel burning now (the rest is in the fuel slot).</summary>
    public double FireLeft => _fireLeft;

    public ItemSlot FuelSlot => InfeedSlot;
    public ItemSlot DoreSlotItem => Inventory[DoreSlot];
    public IEnumerable<ItemSlot> GoldSlots => Enumerable.Range(FirstGoldSlot, FirstSilverSlot - FirstGoldSlot).Select(i => Inventory[i]);
    public IEnumerable<ItemSlot> SilverSlots => Enumerable.Range(FirstSilverSlot, LastSlot - FirstSilverSlot + 1).Select(i => Inventory[i]);

    public int Bars => DoreSlotItem.StackSize;
    public int GoldBits => GoldSlots.Sum(s => s.StackSize);
    public int SilverBits => SilverSlots.Sum(s => s.StackSize);

    public VesselPhase Phase => AcidPartingRules.PhaseOf(Bars, _chargeUnits, _parted, _started);

    /// <summary>The share of its full draught it burns and parts at now.</summary>
    public double Draught => AcidPartingRules.Draught(_damper, _row.Works, VesselConfig);

    /// <summary>Silver units the tub's copper can still throw down; 0 with none hung.</summary>
    public double CopperLeft => Assembly?.Wearing.Fitted(CopperPart)?.Left ?? 0;

    /// <summary>The fired brick type of its brickwork (<c>red</c>), once the first is in.</summary>
    public string? BrickType =>
        Assembly?.Build.CodeIn(0, 0) is { } code && VesselRig is { } rig ? rig.Bricks.TypeOf(code) : null;

    public override bool HasWork => Phase == VesselPhase.Parting || Phase == VesselPhase.Charged && AcidPartingRules.AcidReady(Bars, _acid, VesselConfig);

    public override double JobFill => HasWork ? 1 : 0;

    /// <summary>The charge's units parted, once it has started.</summary>
    public override double? WorkProgress =>
        Phase != VesselPhase.Parting ? null : _parted / Math.Max(1e-6, _chargeUnits);

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        if (api.Side == EnumAppSide.Server)
            RegisterGameTickListener(OnVesselTick, 1000);
    }

    // ---- The row and its chimney (the crucible furnace's rule) ----

    /// <summary>The chimney standing on the outlet, counted as the crucible furnace counts its own.</summary>
    public ChimneyColumn ChimneyOnOutlet()
    {
        if (VesselRig is not { } rig)
            return default;
        var ba = Api.World.BlockAccessor;
        var pos = CellPos(rig.ChimneyBase);
        return ChimneyColumn.Measure(n => ba.GetBlock(pos.UpCopy(n))?.Code?.ToString(), CrucibleFurnaceSystem.Of(Api).Config);
    }

    private BEAcidParting? Neighbour(int k)
    {
        if (VesselRig is not { } rig)
            return null;
        var w = Footprint.ToWorld(new Int3(k * rig.Width, 0, 0), Side);
        return Api.World.BlockAccessor.GetBlockEntity(Pos.AddCopy(w.X, w.Y, w.Z)) is BEAcidParting { Built: true } v && v.Side == Side ? v : null;
    }

    /// <summary>Its row: vessels side by side along their width, one chimney on any outlet, the others capped.</summary>
    public FurnaceRow FindRow()
    {
        if (VesselRig is not { } rig || !Built)
            return new FurnaceRow(0, 1, null, 0, null, RowProblem.NoChimney);
        RowMember? At(int k) => k == 0
            ? new RowMember(_capped, ChimneyOnOutlet())
            : Neighbour(k) is { } other ? new RowMember(other.Capped, other.ChimneyOnOutlet()) : null;
        return FurnaceRow.Find(At, rig.RowMax, rig.MinChimney);
    }

    // ---- The fire and the parting ----

    private void OnVesselTick(float dt)
    {
        double now = Api.World.Calendar.TotalHours;
        double hours = double.IsNaN(_lastHours) ? 0 : Math.Max(0, now - _lastHours);
        _lastHours = now;
        if (!Built)
            return;
        bool dirty = false;
        var row = FindRow();
        if (row != _row)
        {
            _row = row;
            dirty = true;
        }
        double fired = Burn(hours * Draught, ref dirty);
        dirty |= PartFor(fired);
        if (dirty)
            MarkDirty(true);
    }

    /// <summary>Ages the vessel by <paramref name="hours"/>, as if they had passed (tests).</summary>
    public void Age(double hours) => _lastHours = (double.IsNaN(_lastHours) ? Api.World.Calendar.TotalHours : _lastHours) - hours;

    /// <summary>Burns <paramref name="want"/> full-draught hours of the fire, taking fuel from the fire door's slot as
    /// each burns out; the hours it burned (less when the fuel ran out and the fire went out).</summary>
    private double Burn(double want, ref bool dirty)
    {
        if (!_lit || want <= 0)
            return 0;
        double burned = 0;
        while (want > 1e-12)
        {
            if (_fireLeft <= 1e-12 && !TakeFuel())
            {
                _lit = false;
                _fireLeft = 0;
                dirty = true;
                break;
            }
            double b = Math.Min(want, _fireLeft);
            _fireLeft -= b;
            want -= b;
            burned += b;
        }
        return burned;
    }

    private bool TakeFuel()
    {
        if (FuelSlot.Empty || FuelSlot.Itemstack.Collectible.CombustibleProps is not { } props)
            return false;
        FuelSlot.TakeOut(1);
        FuelSlot.MarkDirty();
        _fireLeft += AcidPartingRules.FuelHours(props.BurnDuration, VesselConfig);
        return true;
    }

    /// <summary>
    /// Parts for <paramref name="hours"/> of full-draught fire: a charged vessel with acid enough, wash water and copper
    /// in the tub starts (its bars dissolve into the charge); a parting one parts at ore processing's rate, as far as
    /// its water and the tub's copper allow, spending acid, water and copper, the gold into the vessel and the silver
    /// into the tub as bits (their fractions carried to the next).
    /// </summary>
    private bool PartFor(double hours)
    {
        if (hours <= 0 || Assembly is not { Wearing.AllFitted: true } assembly || Spec is not { } spec)
            return false;
        var c = VesselConfig;
        bool changed = false;
        if (Phase == VesselPhase.Charged)
        {
            if (!AcidPartingRules.AcidReady(Bars, _acid, c) || !Water.Holds(WaterPerItem)
                || Bullion.OreOf(DoreSlotItem.Itemstack!.Collectible.Code.ToString()) is not { } ore)
                return false;
            _ore = ore;
            _chargeUnits = Bars * Bullion.UnitsPerItem;
            _parted = 0;
            _started = true;
            DoreSlotItem.Itemstack = null;
            DoreSlotItem.MarkDirty();
            changed = true;
        }
        if (Phase != VesselPhase.Parting || _ore == null)
            return changed;
        var recovery = OreProcessingSystem.RecoveryFor(Api);
        var spec2 = recovery.Ore(_ore);
        var yields = recovery.Smelted(spec2, 1, PartingStation.AcidParting);
        double mainPerUnit = yields.Where(m => m.Metal == spec2.Metal).Sum(m => m.Units);
        var byProducts = yields.Where(m => m.Metal != spec2.Metal).ToList();
        double silverPerUnit = byProducts.Sum(m => m.Units);
        var step = AcidPartingRules.Part(hours, 1, spec.ItemsPerHour, _chargeUnits - _parted, Water.Litres, WaterPerItem,
            silverPerUnit > 0 ? CopperLeft : double.MaxValue, silverPerUnit, AcidPartingRules.AcidPerUnit(c, Bullion.UnitsPerItem));
        if (step.Units <= 0)
            return changed;
        _parted = Math.Min(_chargeUnits, _parted + step.Units);
        if (_chargeUnits - _parted < 1e-6)
            _parted = _chargeUnits;
        SpendWater(Math.Min(step.Water, Water.Litres));
        _acid = Math.Max(0, _acid - step.Acid);
        if (step.Silver > 0)
            assembly.Wearing.Wear(CopperPart, step.Silver);
        Store(GoldSlots, spec2.Metal, _carry.Add(GoldKey, step.Units * mainPerUnit, AcidPartingRules.UnitsPerBit));
        if (byProducts.Count > 0)
            Store(SilverSlots, byProducts[0].Metal, _carry.Add(SilverKey, step.Units * silverPerUnit, AcidPartingRules.UnitsPerBit));
        if (Phase == VesselPhase.Parted || _parted - _syncedParted >= _chargeUnits / 50)
        {
            _syncedParted = _parted;
            changed = true;
        }
        return changed;
    }

    /// <summary>Puts <paramref name="count"/> bits of <paramref name="metal"/> into <paramref name="slots"/>; what does not
    /// fit is dropped at the vessel.</summary>
    private void Store(IEnumerable<ItemSlot> slots, string metal, int count)
    {
        if (count <= 0 || Api.World.GetItem(new AssetLocation("game", "metalbit-" + metal)) is not { Id: > 0 } bit)
            return;
        var from = new DummySlot(new ItemStack(bit, count));
        foreach (var slot in slots)
        {
            if (from.Empty)
                break;
            var op = new ItemStackMoveOperation(Api.World, EnumMouseButton.Left, 0, EnumMergePriority.AutoMerge, from.StackSize);
            from.TryPutInto(slot, ref op);
            slot.MarkDirty();
        }
        if (!from.Empty && VesselRig is { } rig)
            Api.World.SpawnItemEntity(from.Itemstack, WorldPoint(rig.Vessel));
    }

    // ---- Fire ----

    public override bool Ignitable => Built;

    public override bool Burning => _lit;

    public override string? CannotIgnite() =>
        !Built ? Lang.Get("seraphhorizons:acidparting-error-notbuilt")
        : _lit ? RefusalText(VesselRefusal.Burning)
        : FuelSlot.Empty ? RefusalText(VesselRefusal.NoFuel)
        : null;

    public override void Ignite(IPlayer? byPlayer)
    {
        if (Api.Side != EnumAppSide.Server)
            return;
        if (CannotIgnite() is { } why)
        {
            if (byPlayer is IServerPlayer sp)
                sp.SendIngameError("acidparting", why);
            return;
        }
        _lit = TakeFuel();
        _lastHours = Api.World.Calendar.TotalHours;
        _row = FindRow();
        Api.World.PlaySoundAt(new AssetLocation("game", "sounds/torch-ignite"), Pos.X + 0.5, Pos.Y + 0.5, Pos.Z + 0.5, byPlayer, true, 16, 0.6f);
        MarkDirty(true);
    }

    // ---- Interaction ----

    private static string RefusalText(VesselRefusal r, params object[] args) =>
        Lang.Get("seraphhorizons:acidparting-error-" + r.ToString().ToLowerInvariant(), args);

    private bool Refuse(IPlayer byPlayer, VesselRefusal r, params object[] args)
    {
        if (Api.Side == EnumAppSide.Server && byPlayer is IServerPlayer sp)
            sp.SendIngameError("acidparting-" + r.ToString().ToLowerInvariant(), RefusalText(r, args));
        return true;
    }

    private Int3 LocalCell(BlockPos cell) => Footprint.ToLocal(new Int3(cell.X - Pos.X, cell.Y - Pos.Y, cell.Z - Pos.Z), Side);

    private bool IsOutletTop(BlockSelection sel) =>
        VesselRig is { } rig && sel.Face == BlockFacing.UP && LocalCell(sel.Position) == rig.OutletCell;

    /// <summary>Whether the click is at the tub rather than the vessel: in the hood's cell, nearer the tub's mouth.</summary>
    private bool AtTub(BlockSelection sel) =>
        VesselRig is { } rig && LocalCell(sel.Position) == rig.HoodCell && sel.HitPosition != null
        && rig.NearerTub(MachinePoints.NativeHit(Pos, sel.Position, sel.HitPosition, Side));

    /// <summary>Whether the click is on the controller's front, nearer the ash mouth than the fire door.</summary>
    private bool AtAsh(BlockSelection sel) =>
        VesselRig is { } rig && LocalCell(sel.Position) == Int3.Zero && sel.HitPosition != null
        && rig.NearerAsh(MachinePoints.NativeHit(Pos, sel.Position, sel.HitPosition, Side));

    /// <summary>Dore: bullion that acid parting takes (gold quartz's).</summary>
    public bool IsDore(ItemStack stack) =>
        Bullion.OreOf(stack.Collectible.Code.ToString()) is { } ore
        && OreProcessingSystem.RecoveryFor(Api) is var r && Bullion.TakenBy(r, r.Ore(ore), PartingStation.AcidParting);

    /// <summary>A fuel the sand bath's fire burns.</summary>
    public bool IsFuel(ItemStack stack) =>
        stack.Collectible.CombustibleProps is { } p && AcidPartingRules.IsFuel(p.BurnTemperature, p.BurnDuration, VesselConfig);

    /// <summary>Nitric acid in a held liquid container, litres.</summary>
    private static double AcidLitres(ItemStack held)
    {
        if (held.Collectible is not ILiquidSource source || source.GetContent(held) is not { } content
            || content.Collectible.Code.ToString() != AcidPartingRules.Acid)
            return 0;
        double perLitre = BlockLiquidContainerBase.GetContainableProps(content)?.ItemsPerLitre ?? 100;
        return perLitre > 0 ? content.StackSize / perLitre : 0;
    }

    /// <summary>
    /// Once built: dore charges the vessel; a container of nitric acid pours acid into it, one of water the wash water;
    /// fuel goes in at the fire door; an iron plate caps the outlet. An empty hand at the tub draws the silver, at the
    /// vessel takes the parted gold out, on the ash mouth moves the damper, on the outlet's top takes the cap off. A
    /// copper plate renews the tub's copper (the build's wearing part). Lighting is a firestarter's or a torch's
    /// (<see cref="Ignite"/>). Before it is built, the build's.
    /// </summary>
    protected override bool? InteractContent(IPlayer byPlayer, ItemSlot slot, BlockSelection blockSel)
    {
        if (!Built || VesselRig is not { } rig)
            return null;
        var stack = slot?.Itemstack;
        bool server = Api.Side == EnumAppSide.Server;
        var c = VesselConfig;
        if (stack == null)
        {
            if (_capped && IsOutletTop(blockSel))
            {
                if (server && Api.World.GetItem(new AssetLocation(rig.CapCode)) is { Id: > 0 } plate)
                {
                    _capped = false;
                    Give(byPlayer, new ItemStack(plate, rig.CapCount));
                    MarkDirty(true);
                }
                return true;
            }
            if (AtAsh(blockSel))
            {
                if (server)
                {
                    _damper = AcidPartingRules.Next(_damper);
                    Api.World.PlaySoundAt(new AssetLocation("game", "sounds/block/meteoriciron-hit-pickaxe"), Pos, -0.25, byPlayer);
                    MarkDirty(true);
                }
                return true;
            }
            if (AtTub(blockSel) || Phase != VesselPhase.Parted && SilverBits > 0)
            {
                if (SilverBits == 0)
                    return Refuse(byPlayer, VesselRefusal.NoSilver);
                if (server)
                    TakeAll(byPlayer, SilverSlots);
                return true;
            }
            if (Phase != VesselPhase.Parted)
                return Refuse(byPlayer, Phase == VesselPhase.Parting ? VesselRefusal.Parting : VesselRefusal.NotParted);
            if (server)
            {
                TakeAll(byPlayer, GoldSlots);
                _started = false;
                _chargeUnits = 0;
                _parted = 0;
                _syncedParted = 0;
                _ore = null;
                MarkDirty(true);
            }
            return true;
        }

        string code = stack.Collectible.Code.ToString();
        if (!_capped && code == rig.CapCode && IsOutletTop(blockSel))
        {
            if (stack.StackSize < rig.CapCount)
                return false;
            if (server)
            {
                slot!.TakeOut(rig.CapCount);
                slot.MarkDirty();
                _capped = true;
                Api.World.PlaySoundAt(new AssetLocation("game", "sounds/block/anvil1"), Pos, -0.25, byPlayer);
                MarkDirty(true);
            }
            return true;
        }
        if (IsDore(stack))
        {
            if (DoreSlotItem.Itemstack is { } inside && !inside.Equals(Api.World, stack, GlobalConstants.IgnoredStackAttributes))
                return Refuse(byPlayer, VesselRefusal.Full, c.ChargeBars);
            var (take, why) = AcidPartingRules.Charge(Phase, Bars, stack.StackSize, c);
            if (why != VesselRefusal.None)
                return Refuse(byPlayer, why, c.ChargeBars);
            if (server)
            {
                var op = new ItemStackMoveOperation(Api.World, EnumMouseButton.Right, 0, EnumMergePriority.AutoMerge, take);
                slot!.TryPutInto(DoreSlotItem, ref op);
                slot.MarkDirty();
                DoreSlotItem.MarkDirty();
                Api.World.PlaySoundAt(new AssetLocation("game", "sounds/block/ingot"), Pos, -0.25, byPlayer);
                MarkDirty(true);
            }
            return true;
        }
        if (stack.Collectible is ILiquidSource)
        {
            double acid = AcidLitres(stack), water = RockerWater.Litres(stack);
            if (acid > 0)
            {
                var (litres, why) = AcidPartingRules.PourAcid(Phase, _acid, acid, c);
                if (why != VesselRefusal.None)
                    return Refuse(byPlayer, why, AcidPartingRules.AcidCapacity(c));
                if (server)
                {
                    int portions = (int)Math.Floor(litres * 100 + 1e-6);
                    int took = RockerWater.Take(byPlayer, slot!, portions);
                    _acid = Math.Min(AcidPartingRules.AcidCapacity(c), _acid + took / 100.0);
                    Api.World.PlaySoundAt(PourSound, Pos, -0.25, byPlayer);
                    MarkDirty(true);
                }
                return true;
            }
            if (water > 0)
            {
                double room = ReservoirCapacity - Water.Litres;
                if (room <= 1e-6)
                    return Refuse(byPlayer, VesselRefusal.WaterFull);
                if (server)
                {
                    double perLitre = RockerWater.PortionsPerLitre(stack);
                    int took = RockerWater.Take(byPlayer, slot!, (int)Math.Floor(Math.Min(room, water) * perLitre + 1e-6));
                    FillWater(took / perLitre);
                    Api.World.PlaySoundAt(PourSound, Pos, -0.25, byPlayer);
                    MarkDirty(true);
                }
                return true;
            }
            if (RockerWater.IsContainer(stack) && (stack.Collectible as ILiquidSource)?.GetContent(stack) is { })
                return Refuse(byPlayer, VesselRefusal.WrongLiquid);
            return null;
        }
        if (IsFuel(stack))
        {
            int room = c.FuelCapacity - FuelSlot.StackSize;
            if (room <= 0 || !FuelSlot.Empty && !FuelSlot.Itemstack.Equals(Api.World, stack, GlobalConstants.IgnoredStackAttributes))
                return Refuse(byPlayer, VesselRefusal.FuelFull, c.FuelCapacity);
            if (server)
            {
                var op = new ItemStackMoveOperation(Api.World, EnumMouseButton.Right, 0, EnumMergePriority.AutoMerge, Math.Min(room, stack.StackSize));
                slot!.TryPutInto(FuelSlot, ref op);
                slot.MarkDirty();
                FuelSlot.MarkDirty();
                Api.World.PlaySoundAt(new AssetLocation("game", "sounds/block/charcoal"), Pos, -0.25, byPlayer);
                MarkDirty(true);
            }
            return true;
        }
        return null;
    }

    /// <summary>The bullion items the vessel takes, for its help.</summary>
    private ItemStack[] DoreStacks(IWorldAccessor world) =>
        world.SearchItems(new AssetLocation(OreProducts.Domain, "bullion-*"))
            .Where(i => i is { Id: > 0 } && IsDore(new ItemStack(i))).Select(i => new ItemStack(i)).ToArray();

    private void TakeAll(IPlayer byPlayer, IEnumerable<ItemSlot> slots)
    {
        foreach (var s in slots.Where(s => !s.Empty))
        {
            Give(byPlayer, s.TakeOutWhole());
            s.MarkDirty();
        }
        Api.World.PlaySoundAt(new AssetLocation("game", "sounds/block/gravel"), Pos.X + 0.5, Pos.Y + 1.5, Pos.Z + 0.5, byPlayer, true, 16, 0.5f);
        MarkDirty(true);
    }

    private void Give(IPlayer byPlayer, ItemStack? stack)
    {
        if (stack == null || stack.StackSize <= 0)
            return;
        if (!byPlayer.InventoryManager.TryGiveItemstack(stack, true))
            Api.World.SpawnItemEntity(stack, Pos.ToVec3d().Add(0.5, 1.2, 0.5));
    }

    public override WorldInteraction[] InteractionHelp(IPlayer forPlayer)
    {
        var help = new List<WorldInteraction>(base.InteractionHelp(forPlayer));
        if (!Built || VesselRig is not { } rig)
            return help.ToArray();
        var world = Api.World;
        ItemStack[] Of(params string[] codes) => codes
            .Select(c => (CollectibleObject?)world.GetItem(new AssetLocation(c)) ?? world.GetBlock(new AssetLocation(c)))
            .OfType<CollectibleObject>().Where(c => c.Id > 0).Select(c => new ItemStack(c)).ToArray();
        string Key(string k) => "seraphhorizons:acidparting-help-" + k;
        switch (Phase)
        {
            case VesselPhase.Empty or VesselPhase.Charged:
                help.Add(new WorldInteraction { ActionLangCode = Key("charge"), MouseButton = EnumMouseButton.Right, Itemstacks = DoreStacks(world) });
                help.Add(new WorldInteraction { ActionLangCode = Key("acid"), MouseButton = EnumMouseButton.Right, Itemstacks = Of("game:woodbucket") });
                break;
            case VesselPhase.Parted:
                help.Add(new WorldInteraction { ActionLangCode = Key("gold"), MouseButton = EnumMouseButton.Right });
                break;
        }
        help.Add(new WorldInteraction { ActionLangCode = Key("water"), MouseButton = EnumMouseButton.Right, Itemstacks = Of("game:woodbucket") });
        help.Add(new WorldInteraction { ActionLangCode = Key("fuel"), MouseButton = EnumMouseButton.Right, Itemstacks = Of("game:charcoal", "game:firewood") });
        if (!_lit && !FuelSlot.Empty)
            help.Add(new WorldInteraction
            {
                ActionLangCode = Key("light"), MouseButton = EnumMouseButton.Right,
                Itemstacks = ObjectCacheUtil.GetOrCreate(world.Api, "acidparting-ignitestacks", () => BlockBehaviorCanIgnite.CanIgniteStacks(world.Api, true).ToArray()),
            });
        if (SilverBits > 0)
            help.Add(new WorldInteraction { ActionLangCode = Key("silver"), MouseButton = EnumMouseButton.Right });
        help.Add(new WorldInteraction { ActionLangCode = Key("damper"), MouseButton = EnumMouseButton.Right });
        help.Add(_capped
            ? new WorldInteraction { ActionLangCode = Key("uncap"), MouseButton = EnumMouseButton.Right }
            : new WorldInteraction { ActionLangCode = Key("cap"), MouseButton = EnumMouseButton.Right, Itemstacks = Of(rig.CapCode) });
        return help.ToArray();
    }

    // ---- Drawing ----

    /// <summary>
    /// The stages as built and the tub's copper (the wearing part's); the damper with the sand bath; then, once built,
    /// the contents by the vessel's state: the fire laid (<c>fuel</c>) or burning (<c>fire</c>); the dore (<c>charge</c>)
    /// and the acid over it; while parting, the <c>solution</c> over the <c>gold</c>, the tub's <c>liquor</c> and its
    /// <c>silver</c>; parted, the gold with the <c>wash</c> water over it while there is any; the silver sludge while
    /// there are bits to draw; the cap.
    /// </summary>
    public override bool ShowsPart(string? requires)
    {
        switch (requires)
        {
            case VesselRequires.DamperOpen or VesselRequires.DamperHalf or VesselRequires.DamperShut:
                return base.ShowsPart("sandbath") && VesselRequires.Of(_damper) == requires;
            case VesselRequires.OutletCap:
                return _capped;
        }
        if (requires is null || !VesselRequires.Extra.Contains(requires) || requires == VesselRequires.Copper)
            return base.ShowsPart(requires);
        if (!Built)
            return false;
        var phase = Phase;
        return requires switch
        {
            VesselRequires.Fuel => !_lit && !FuelSlot.Empty,
            VesselRequires.Fire => _lit,
            VesselRequires.Charge => phase == VesselPhase.Charged,
            VesselRequires.Acid => phase is VesselPhase.Charged or VesselPhase.Empty && _acid > 0,
            VesselRequires.Solution => phase == VesselPhase.Parting,
            VesselRequires.Gold => phase is VesselPhase.Parting or VesselPhase.Parted,
            VesselRequires.Wash => phase == VesselPhase.Parted && Water.Litres > 0,
            VesselRequires.Liquor => phase == VesselPhase.Parting,
            VesselRequires.Silver => phase == VesselPhase.Parting || SilverBits > 0,
            _ => false,
        };
    }

    public override string TextureKey => BrickType ?? "";

    /// <summary>The block's textures, the brickwork's <c>brick</c> code in the vessel's brick type: its base with its
    /// overlays, composed into the block atlas as the crucible furnace's and the stall roaster's are.</summary>
    public override ITexPositionSource PartTextures(ICoreClientAPI capi)
    {
        var inner = capi.Tesselator.GetTextureSource(Block);
        if (VesselRig is not { } rig || (rig.Bricks.Get(BrickType) ?? rig.Bricks.Get(rig.Bricks.Default)) is not { } brick)
            return inner;
        var composite = new CompositeTexture(new AssetLocation(brick.Base))
        {
            BlendedOverlays = brick.Overlays.Select(o => new BlendedOverlayTexture { Base = new AssetLocation(o) }).ToArray(),
        };
        return capi.BlockTextureAtlas.GetOrInsertTexture(composite, out _, out var pos) && pos != null
            ? new CodeTextureSource(inner, rig.Bricks.TextureCode, pos)
            : inner;
    }

    private sealed class CodeTextureSource(ITexPositionSource inner, string code, TextureAtlasPosition pos) : ITexPositionSource
    {
        public Size2i AtlasSize => inner.AtlasSize!;
        public TextureAtlasPosition this[string textureCode] => textureCode == code ? pos : inner[textureCode]!;
    }

    // ---- Breaking ----

    /// <summary>The frame and every stage's items, the copper's with use left, and the cap. The fuel, the dore and the
    /// bits drop with the inventory; a charge parting is lost with its acid and water.</summary>
    public override IEnumerable<ItemStack> AssemblyDrops()
    {
        foreach (var drop in base.AssemblyDrops())
            yield return drop;
        if (VesselRig is { } rig && _capped && Api.World.GetItem(new AssetLocation(rig.CapCode)) is { Id: > 0 } plate)
            yield return new ItemStack(plate, rig.CapCount);
    }

    // ---- Saving and syncing ----

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetInt("damper", (int)_damper);
        tree.SetBool("capped", _capped);
        tree.SetBool("lit", _lit);
        tree.SetBool("started", _started);
        tree.SetDouble("fireLeft", _fireLeft);
        tree.SetDouble("chargeUnits", _chargeUnits);
        tree.SetDouble("parted", _parted);
        tree.SetDouble("acid", _acid);
        tree.SetDouble("lastHours", _lastHours);
        if (_ore != null)
            tree.SetString("ore", _ore);
        else
            tree.RemoveAttribute("ore");
        tree.SetDouble("carryGold", _carry.HeldFor(GoldKey));
        tree.SetDouble("carrySilver", _carry.HeldFor(SilverKey));
        tree.SetInt("rowFirst", _row.First);
        tree.SetInt("rowCount", _row.Count);
        tree.SetInt("rowChimneyAt", _row.ChimneyAt ?? int.MinValue);
        tree.SetInt("rowChimneyHeight", _row.ChimneyHeight);
        tree.SetInt("rowOpenOutlet", _row.OpenOutlet ?? int.MinValue);
        tree.SetInt("rowProblem", (int)_row.Problem);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);
        _damper = (Damper)Math.Clamp(tree.GetInt("damper"), 0, 2);
        _capped = tree.GetBool("capped");
        _lit = tree.GetBool("lit");
        _started = tree.GetBool("started");
        _fireLeft = Math.Max(0, tree.GetDouble("fireLeft"));
        _chargeUnits = Math.Max(0, tree.GetDouble("chargeUnits"));
        _parted = Math.Clamp(tree.GetDouble("parted"), 0, _chargeUnits);
        _acid = Math.Max(0, tree.GetDouble("acid"));
        _lastHours = tree.GetDouble("lastHours", double.NaN);
        _ore = tree.GetString("ore");
        _carry.Restore([new(GoldKey, tree.GetDouble("carryGold")), new(SilverKey, tree.GetDouble("carrySilver"))]);
        int? Opt(string key) => tree.GetInt(key, int.MinValue) is var v && v != int.MinValue ? v : null;
        _row = new FurnaceRow(tree.GetInt("rowFirst"), Math.Max(1, tree.GetInt("rowCount", 1)), Opt("rowChimneyAt"),
            tree.GetInt("rowChimneyHeight"), Opt("rowOpenOutlet"), (RowProblem)tree.GetInt("rowProblem", (int)RowProblem.NoChimney));
    }

    // ---- Info ----

    protected override void AppendContents(StringBuilder dsc)
    {
        if (!Built || VesselRig is not { } rig)
            return;
        var c = VesselConfig;
        string L(string key, params object[] args) => Lang.Get("seraphhorizons:acidparting-info-" + key, args);
        string N(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);
        switch (Phase)
        {
            case VesselPhase.Empty:
                dsc.AppendLine(L("empty", c.ChargeBars));
                break;
            case VesselPhase.Charged:
                dsc.AppendLine(L("charged", Bars, c.ChargeBars, N(AcidPartingRules.AcidFor(Bars, c))));
                break;
            case VesselPhase.Parting:
                dsc.AppendLine(L("parting", (int)Math.Floor(100 * _parted / Math.Max(1e-6, _chargeUnits))));
                break;
            case VesselPhase.Parted:
                dsc.AppendLine(L("parted", GoldBits));
                break;
        }
        dsc.AppendLine(L("acid", N(_acid), N(AcidPartingRules.AcidCapacity(c))));
        if (SilverBits > 0)
            dsc.AppendLine(L("silver", SilverBits));
        dsc.AppendLine(_lit ? L("fire", N(_fireLeft), FuelSlot.StackSize) : L("nofire", FuelSlot.StackSize));
        dsc.AppendLine(L("damper-" + _damper.ToString().ToLowerInvariant()));
        dsc.AppendLine(_row.Problem switch
        {
            RowProblem.None => L("row", _row.Count, _row.ChimneyHeight),
            RowProblem.TooManyFurnaces => L("row-toomany", rig.RowMax),
            RowProblem.OutletOpen => L("row-open"),
            RowProblem.ChimneyTooShort => L("row-short", _row.ChimneyHeight, rig.MinChimney),
            RowProblem.NoChimneyTop => L("row-notop"),
            RowProblem.ChimneyTopBuried => L("row-buried"),
            _ => L(_capped && _row.Count == 1 ? "row-capped" : "row-none", rig.MinChimney),
        });
    }
}
