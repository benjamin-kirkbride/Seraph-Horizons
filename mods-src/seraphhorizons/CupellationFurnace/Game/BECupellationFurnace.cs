using System.Globalization;
using System.Text;
using SeraphHorizons.Mod.CrucibleFurnace;
using SeraphHorizons.Mod.CrucibleFurnace.Core;
using SeraphHorizons.Mod.CupellationFurnace.Core;
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

namespace SeraphHorizons.Mod.CupellationFurnace;

/// <summary>
/// The cupellation furnace (#740; README "Ore processing: the cupellation furnace"): an English test furnace on the
/// shared ore machine code (<see cref="BEOreMachine"/>), batch, charged and emptied by hand, with no power, no water
/// and no chute (<see cref="Cupelling"/> has its rules). Built as its build file says (the iron frame, the brickwork in
/// one fired brick type, the lining, the fittings), then the test, its wearing part, beaten in with bone meal; coal
/// goes in at the fire door and is lit with a firestarter or a torch; bars of base bullion (silver-lead, argentiferous
/// copper, cupriferous silver), rich lead and lead are charged through the working door. While the fire burns, the
/// row's chimney draws (the crucible furnace's row rule, <see cref="FurnaceRow"/>) and air arrives from a pipe at the
/// blast inlet (Steelmaking Expanded's engine air blower, <see cref="PpexGas"/>), the lead burns to litharge into the
/// pot at ore processing's rate, paced by the blast and the damper, and the silver stays as the cake.
/// <para>Its inventory: slot 0 (the shared infeed slot) the coal at the fire door, slots 1 to 4 the charge, 5 and 6 the
/// litharge pot, 7 the silver cake, 8 the copper beside it; all drop when it is broken, with the cap.</para>
/// </summary>
public class BECupellationFurnace : BEOreMachine
{
    private const int FirstCharge = 1, ChargeSlots = 4, FirstPot = 5, PotSlots = 2, SilverSlot = 7, CopperSlot = 8;
    private static readonly AssetLocation DamperSound = new("game", "sounds/effect/latch");
    private static readonly AssetLocation CoalSound = new("game", "sounds/block/charcoal");
    private static readonly AssetLocation MetalSound = new("game", "sounds/block/ingot");

    private Damper _damper = Damper.Open;
    private bool _capped, _lit, _running;
    private double _ember;
    private double _done;
    private double _air;
    private double _lastHours = double.NaN;
    private double _syncedDone;
    private FurnaceRow _row = new(0, 1, null, 0, null, RowProblem.NoChimney);

    public override OreMachine? Kind => OreMachine.CupellationFurnace;

    /// <summary>Nothing goes in by chute: the furnace is charged by hand (<see cref="InteractContent"/>).</summary>
    public override bool Takes(ItemStack stack) => false;

    protected override IEnumerable<(string Anchor, ItemStack Stack)> Products(ItemStack fed) => [];

    protected override IReadOnlyList<string> OutputAnchors => [];

    private CupellationFurnaceSystem Furnace => CupellationFurnaceSystem.Of(Api);
    private CupellationFurnaceConfig FurnaceConfig => Furnace.Config;
    public CupellationFurnaceRig? FurnaceRig => Api == null ? null : Furnace.Rig;
    private OreRecovery Recovery => OreProcessingSystem.RecoveryFor(Api);

    public Damper Damper => _damper;
    public bool Capped => _capped;
    public bool Lit => _lit;
    /// <summary>Whether the charge is being worked now: the litharge running.</summary>
    public bool Working => _running;
    /// <summary>Units of the charge's lead cupelled so far.</summary>
    public double Done => _done;
    /// <summary>The blast's share at the last tick, 0 to 1.</summary>
    public double Blast => _air;
    public FurnaceRow Row => _row;
    public int Coal => InfeedSlot.StackSize;

    public IEnumerable<ItemSlot> Charge => Enumerable.Range(FirstCharge, ChargeSlots).Select(i => Inventory[i]);
    public IEnumerable<ItemSlot> Pot => Enumerable.Range(FirstPot, PotSlots).Select(i => Inventory[i]);
    public ItemSlot SilverCake => Inventory[SilverSlot];
    public ItemSlot CopperBits => Inventory[CopperSlot];

    public int Litharge => Pot.Sum(s => s.StackSize);

    /// <summary>Whether the test is in with use left.</summary>
    public bool TestIn => Assembly?.Wearing.AllFitted == true;

    /// <summary>The fired brick type of its brickwork (<c>red</c>), once the first is in.</summary>
    public string? BrickType =>
        Assembly?.Build.CodeIn(0, 0) is { } code && FurnaceRig is { } rig ? rig.Bricks.TypeOf(code) : null;

    /// <summary>The charge as the rules see it.</summary>
    public List<FurnaceCharge> ChargeOf() => Charge.Where(s => !s.Empty).Select(s => ChargeOf(s.Itemstack!)).ToList();

    /// <summary>One stack of the charge as the rules see it.</summary>
    public static FurnaceCharge ChargeOf(ItemStack stack)
    {
        string code = stack.Collectible.Code.ToString();
        double units = stack.StackSize * Bullion.UnitsPerItem;
        if (code == Bullion.RichLeadCode) return FurnaceCharge.Rich(units);
        if (code == Cupelling.LeadIngot) return FurnaceCharge.Lead(units);
        if (Bullion.OreOf(code) is { } ore) return FurnaceCharge.Bar(ore, units);
        return FurnaceCharge.Other;
    }

    /// <summary>Whether a stack can go into the dish at all: bullion, rich lead or a lead ingot.</summary>
    public static bool IsChargeItem(ItemStack stack)
    {
        string code = stack.Collectible.Code.ToString();
        return code == Bullion.RichLeadCode || code == Cupelling.LeadIngot || Bullion.OreOf(code) != null;
    }

    public double ChargeUnits => ChargeOf().Sum(c => c.Units);

    public double ChargeLead => Cupelling.LeadUnits(Recovery, ChargeOf());

    /// <summary>Why the charge in it will not be worked now (its contents, the test, the pot), or none.</summary>
    public CupellingRefusal ChargeRefusal()
    {
        var charge = ChargeOf();
        var r = Cupelling.Check(Recovery, FurnaceConfig, charge);
        if (r != CupellingRefusal.None) return r;
        if (!TestIn) return CupellingRefusal.TestWorn;
        if (!SilverCake.Empty || !CopperBits.Empty) return CupellingRefusal.TakeOutFirst;
        int room = Pot.Sum(s => s.Empty ? OreProducts.LithargeStack : Math.Max(0, s.Itemstack!.Collectible.MaxStackSize - s.StackSize));
        return Cupelling.LithargeFor(Cupelling.LeadUnits(Recovery, charge)) > room ? CupellingRefusal.PotFull : CupellingRefusal.None;
    }

    public override bool HasWork => _lit && Charge.Any(s => !s.Empty);

    public override double JobFill => HasWork ? 1 : 0;

    /// <summary>The charge's lead cupelled, once the blast has started on it.</summary>
    public override double? WorkProgress =>
        ChargeUnits <= 0 || !(_done > 0 || _running) ? null : ChargeLead is var lead && lead > 0 ? _done / lead : 1;

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        if (api.Side == EnumAppSide.Server)
            RegisterGameTickListener(OnFurnaceTick, 500);
    }

    // ---- The row and its chimney ----

    /// <summary>The chimney standing on the outlet, counted as the crucible furnace counts its own.</summary>
    public ChimneyColumn ChimneyOnOutlet()
    {
        if (FurnaceRig is not { } rig)
            return default;
        var ba = Api.World.BlockAccessor;
        var pos = CellPos(rig.ChimneyBase);
        return ChimneyColumn.Measure(n => ba.GetBlock(pos.UpCopy(n))?.Code?.ToString(), CrucibleFurnaceSystem.Of(Api).Config);
    }

    /// <summary>The furnace at offset <paramref name="k"/> along its row (built, facing the same way), or null.</summary>
    private BECupellationFurnace? Neighbour(int k)
    {
        if (FurnaceRig is not { } rig)
            return null;
        var w = Footprint.ToWorld(new Int3(k * rig.Width, 0, 0), Side);
        return Api.World.BlockAccessor.GetBlockEntity(Pos.AddCopy(w.X, w.Y, w.Z)) is BECupellationFurnace { Built: true } f && f.Side == Side ? f : null;
    }

    /// <summary>Its row: furnaces side by side along their width, one chimney on any outlet, the others capped.</summary>
    public FurnaceRow FindRow()
    {
        if (FurnaceRig is not { } rig || !Built)
            return new FurnaceRow(0, 1, null, 0, null, RowProblem.NoChimney);
        RowMember? At(int k) => k == 0
            ? new RowMember(_capped, ChimneyOnOutlet())
            : Neighbour(k) is { } other ? new RowMember(other.Capped, other.ChimneyOnOutlet()) : null;
        return FurnaceRow.Find(At, rig.RowMax, rig.MinChimney);
    }

    // ---- The fire, the blast and the work ----

    private void OnFurnaceTick(float dt)
    {
        if (!Built || FurnaceRig is not { } rig)
        {
            _lastHours = double.NaN;
            return;
        }
        bool dirty = false;
        var row = FindRow();
        if (row != _row)
        {
            _row = row;
            dirty = true;
        }
        double now = Api.World.Calendar.TotalHours;
        double hours = double.IsNaN(_lastHours) ? 0 : Math.Max(0, now - _lastHours);
        _lastHours = now;

        if (_lit)
        {
            var (ember, taken, lit) = Cupelling.Burn(_ember, Coal, hours, FurnaceConfig.CoalPerHour,
                Cupelling.BurnShare(_damper, _row.Works, FurnaceConfig));
            _ember = ember;
            if (taken > 0)
            {
                InfeedSlot.TakeOut(taken);
                InfeedSlot.MarkDirty();
                dirty = true;
            }
            if (!lit)
            {
                _lit = false;
                _ember = 0;
                dirty = true;
            }
        }

        // The blast draws only while the furnace holds a hot charge it can work (the owner's ruling).
        bool hot = _lit && ChargeRefusal() == CupellingRefusal.None;
        double air = 0;
        if (hot && TestBlast is { } given)
            air = Math.Clamp(given, 0, 1);
        else if (hot && FurnaceRig is { } r && Rig?.Anchor("blast") is { } a)
        {
            var (cell, face) = MachineChutes.World(Pos, Side, a);
            double wanted = r.BlastRate * dt;
            air = Cupelling.BlastShare(PpexGas.Draw(Api, cell, face, r.BlastGas, wanted, r.BlastPressure), wanted);
        }
        if (Math.Abs(air - _air) > 0.05 || (air > 0) != (_air > 0))
            dirty = true;
        _air = air;

        double pace = air * Cupelling.Draught(_damper, _row.Works, FurnaceConfig);
        bool running = hot && pace > 0;
        if (running != _running)
        {
            _running = running;
            dirty = true;
        }
        if (running && Spec is { ItemsPerHour: > 0 } spec)
        {
            double lead = ChargeLead;
            var (done, finished) = Cupelling.Advance(_done, hours, pace, spec.ItemsPerHour, lead);
            _done = done;
            if (finished)
            {
                Finish();
                dirty = true;
            }
            else if (lead > 0 && _done - _syncedDone >= lead / 100)
            {
                _syncedDone = _done;
                dirty = true;
            }
        }
        if (dirty)
            MarkDirty(true);
    }

    /// <summary>The blast's share given in place of the pipe's (tests: the pack's world has no engine air blower to
    /// hand); null, the pipe's.</summary>
    public double? TestBlast { get; set; }

    /// <summary>Ages the furnace by <paramref name="hours"/>, as if they had passed (tests).</summary>
    public void Age(double hours) => _lastHours = (double.IsNaN(_lastHours) ? Api.World.Calendar.TotalHours : _lastHours) - hours;

    /// <summary>
    /// The charge done: its lead to litharge in the pot, its silver to the cake and its copper beside it, each as
    /// whole items of 5 units, a fraction one more at that chance (<see cref="Cupellation.Whole"/>); the dish
    /// emptied; the test worn a charge.
    /// </summary>
    private void Finish()
    {
        var yield = Cupelling.Yield(Recovery, ChargeOf());
        var rand = Api.World.Rand;
        int litharge = Math.Min(Cupelling.LithargeFor(yield.LeadUnits), Cupellation.Whole(yield.LeadUnits, Cupelling.UnitsPerItem, rand.NextDouble()));
        if (litharge > 0 && Api.World.GetItem(new AssetLocation(OreProducts.LithargeCode)) is { Id: > 0 } lith)
            PutInto(Pot, new ItemStack(lith, litharge));
        Put(SilverCake, "game:metalbit-silver", Cupellation.Whole(yield.UnitsOf("silver"), Cupelling.UnitsPerItem, rand.NextDouble()));
        Put(CopperBits, "game:metalbit-copper", Cupellation.Whole(yield.UnitsOf("copper"), Cupelling.UnitsPerItem, rand.NextDouble()));
        foreach (var slot in Charge)
        {
            slot.Itemstack = null;
            slot.MarkDirty();
        }
        Assembly?.Wearing.WearAll(1);
        _done = 0;
        _syncedDone = 0;
        _running = false;
        Api.World.PlaySoundAt(MetalSound, Pos.X + 0.5, Pos.Y + 0.5, Pos.Z + 0.5, null, true, 16, 0.6f);
    }

    private void Put(ItemSlot slot, string code, int count)
    {
        if (count <= 0 || Api.World.GetItem(new AssetLocation(code)) is not { Id: > 0 } item)
            return;
        if (slot.Empty)
            slot.Itemstack = new ItemStack(item, count);
        else
            slot.Itemstack!.StackSize += count;
        slot.MarkDirty();
    }

    private void PutInto(IEnumerable<ItemSlot> slots, ItemStack stack)
    {
        foreach (var slot in slots)
        {
            if (stack.StackSize <= 0)
                break;
            if (slot.Empty)
            {
                int n = Math.Min(stack.StackSize, stack.Collectible.MaxStackSize);
                slot.Itemstack = new ItemStack(stack.Collectible, n);
                stack.StackSize -= n;
            }
            else if (slot.Itemstack!.Collectible == stack.Collectible)
            {
                int n = Math.Min(stack.StackSize, stack.Collectible.MaxStackSize - slot.StackSize);
                slot.Itemstack.StackSize += n;
                stack.StackSize -= n;
            }
            slot.MarkDirty();
        }
        if (stack.StackSize > 0)
            Api.World.SpawnItemEntity(stack, Pos.ToVec3d().Add(0.5, 0.6, 0.5));
    }

    // ---- Fire ----

    public override bool Ignitable => Built;

    public override bool Burning => _lit;

    public override string? CannotIgnite() =>
        !Built ? Lang.Get("seraphhorizons:cupellationfurnace-error-notbuilt")
        : _lit ? Lang.Get("seraphhorizons:cupellationfurnace-error-lit")
        : InfeedSlot.Empty ? Lang.Get("seraphhorizons:cupellationfurnace-error-nofuel")
        : null;

    public override void Ignite(IPlayer? byPlayer)
    {
        if (Api.Side != EnumAppSide.Server)
            return;
        if (CannotIgnite() is { } why)
        {
            if (byPlayer is IServerPlayer sp)
                sp.SendIngameError("cupellationfurnace", why);
            return;
        }
        _lit = true;
        _ember = 0;
        _lastHours = Api.World.Calendar.TotalHours;
        _row = FindRow();
        Api.World.PlaySoundAt(new AssetLocation("game", "sounds/torch-ignite"), Pos.X + 0.5, Pos.Y + 0.5, Pos.Z + 0.5, byPlayer, true, 16, 0.6f);
        if (byPlayer != null)
            Api.World.Logger.Audit("{0} lit the cupellation furnace at {1}.", byPlayer.PlayerName, Pos);
        MarkDirty(true);
    }

    // ---- Interaction ----

    private static string RefusalText(CupellingRefusal r, params object[] args) =>
        Lang.Get("seraphhorizons:cupellationfurnace-error-" + r.ToString().ToLowerInvariant(), args);

    private bool Refuse(IPlayer byPlayer, CupellingRefusal r, params object[] args)
    {
        if (Api.Side == EnumAppSide.Server && byPlayer is IServerPlayer sp)
            sp.SendIngameError("cupellationfurnace-" + r.ToString().ToLowerInvariant(), RefusalText(r, args));
        return true;
    }

    private bool Say(IPlayer byPlayer, string key, params object[] args)
    {
        if (Api.Side == EnumAppSide.Server && byPlayer is IServerPlayer sp)
            sp.SendIngameError("cupellationfurnace-" + key, Lang.Get("seraphhorizons:cupellationfurnace-error-" + key, args));
        return true;
    }

    private bool IsOutletTop(BlockSelection sel) =>
        FurnaceRig is { } rig && sel.Face == BlockFacing.UP && sel.Position.Equals(CellPos(rig.OutletCell));

    /// <summary>
    /// Once built: coal, coke or charcoal goes in at the fire door, bars and lead into the dish, an iron plate caps
    /// the outlet; with sneak, an empty hand works the ash-pit damper; an empty hand takes the silver cake and the
    /// copper out, then the litharge, or on the outlet's top takes the cap off, or takes back a charge not yet begun.
    /// Bone meal relines a spent test (the shared wearing parts). Lighting is a firestarter's or a torch's
    /// (<see cref="Ignite"/>). Before it is built, the build's.
    /// </summary>
    protected override bool? InteractContent(IPlayer byPlayer, ItemSlot slot, BlockSelection blockSel)
    {
        if (!Built || FurnaceRig is not { } rig)
            return null;
        var stack = slot?.Itemstack;
        bool server = Api.Side == EnumAppSide.Server;
        if (stack == null)
        {
            if (byPlayer.Entity.Controls.ShiftKey)
            {
                if (server)
                {
                    _damper = _damper switch { Damper.Open => Damper.Half, Damper.Half => Damper.Shut, _ => Damper.Open };
                    Api.World.PlaySoundAt(DamperSound, Pos.X + 0.5, Pos.Y + 0.3, Pos.Z + 0.5, byPlayer);
                    MarkDirty(true);
                }
                return true;
            }
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
            foreach (var out_ in new[] { SilverCake, CopperBits }.Concat(Pot))
                if (!out_.Empty)
                {
                    if (server)
                    {
                        Give(byPlayer, out_.TakeOutWhole());
                        out_.MarkDirty();
                        Api.World.PlaySoundAt(MetalSound, Pos.X + 0.5, Pos.Y + 0.5, Pos.Z + 0.5, byPlayer, true, 16, 0.5f);
                        MarkDirty(true);
                    }
                    return true;
                }
            if (Charge.LastOrDefault(s => !s.Empty) is { } last)
            {
                if (_done > 0)
                    return Refuse(byPlayer, CupellingRefusal.Working);
                if (server)
                {
                    Give(byPlayer, last.TakeOutWhole());
                    last.MarkDirty();
                    MarkDirty(true);
                }
                return true;
            }
            return null;
        }

        string code = stack.Collectible.Code.ToString();
        if (Cupelling.Fuels.Contains(code))
        {
            if (!InfeedSlot.Empty && !InfeedSlot.Itemstack!.Equals(Api.World, stack, GlobalConstants.IgnoredStackAttributes))
                return Say(byPlayer, "otherfuel", InfeedSlot.Itemstack.GetName());
            int room = InfeedSlot.Empty ? stack.Collectible.MaxStackSize : stack.Collectible.MaxStackSize - InfeedSlot.StackSize;
            if (room <= 0)
                return Say(byPlayer, "fuelfull");
            if (server)
            {
                MoveInto(slot!, InfeedSlot, Math.Min(room, stack.StackSize));
                Api.World.PlaySoundAt(CoalSound, Pos.X + 0.5, Pos.Y + 0.3, Pos.Z + 0.5, byPlayer, true, 16, 0.6f);
                MarkDirty(true);
            }
            return true;
        }
        if (!_capped && code == rig.CapCode)
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
        if (IsChargeItem(stack))
        {
            if (!TestIn)
                return Refuse(byPlayer, CupellingRefusal.TestWorn);
            if (_done > 0)
                return Refuse(byPlayer, CupellingRefusal.Working);
            if (!SilverCake.Empty || !CopperBits.Empty)
                return Refuse(byPlayer, CupellingRefusal.TakeOutFirst);
            var one = ChargeOf(new ItemStack(stack.Collectible, 1));
            if (one.Foreign || one.Ore is { } ore && !Cupelling.Takes(Recovery, Recovery.Ore(ore)))
                return Refuse(byPlayer, CupellingRefusal.Foreign);
            double held = ChargeUnits;
            int fits = (int)Math.Floor((FurnaceConfig.ChargeUnits - held + 1e-9) / Bullion.UnitsPerItem);
            int take = Math.Min(fits, stack.StackSize);
            if (take <= 0)
                return Refuse(byPlayer, CupellingRefusal.TooMuch, FurnaceConfig.ChargeUnits);
            if (server)
            {
                int moved = PutCharge(slot!, take);
                if (moved <= 0)
                    return Refuse(byPlayer, CupellingRefusal.TooMuch, FurnaceConfig.ChargeUnits);
                Api.World.PlaySoundAt(MetalSound, Pos.X + 0.5, Pos.Y + 0.5, Pos.Z + 0.5, byPlayer, true, 16, 0.5f);
                MarkDirty(true);
            }
            return true;
        }
        return null;
    }

    private void MoveInto(ItemSlot from, ItemSlot into, int count)
    {
        var op = new ItemStackMoveOperation(Api.World, EnumMouseButton.Right, 0, EnumMergePriority.AutoMerge, count);
        from.TryPutInto(into, ref op);
        from.MarkDirty();
        into.MarkDirty();
    }

    /// <summary>Puts <paramref name="count"/> of the held bars into the dish, merging with their kind; the number moved.</summary>
    private int PutCharge(ItemSlot from, int count)
    {
        int moved = 0;
        foreach (var target in Charge)
        {
            if (moved >= count || from.Empty)
                break;
            if (!target.Empty && !target.Itemstack.Equals(Api.World, from.Itemstack, GlobalConstants.IgnoredStackAttributes))
                continue;
            int before = from.StackSize;
            MoveInto(from, target, count - moved);
            moved += before - from.StackSize;
        }
        return moved;
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
        if (!Built || FurnaceRig is not { } rig)
            return base.InteractionHelp(forPlayer);
        var world = Api.World;
        ItemStack[] Of(params string[] codes) => codes
            .Select(c => (CollectibleObject?)world.GetItem(new AssetLocation(c)) ?? world.GetBlock(new AssetLocation(c)))
            .OfType<CollectibleObject>().Where(c => c.Id > 0).Select(c => new ItemStack(c)).ToArray();
        var help = new List<WorldInteraction>(base.InteractionHelp(forPlayer));
        string Key(string k) => "seraphhorizons:cupellationfurnace-help-" + k;
        help.Add(new WorldInteraction { ActionLangCode = Key("fuel"), MouseButton = EnumMouseButton.Right, Itemstacks = Of([.. Cupelling.Fuels]) });
        if (!_lit && !InfeedSlot.Empty)
            help.Add(new WorldInteraction
            {
                ActionLangCode = Key("light"), MouseButton = EnumMouseButton.Right,
                Itemstacks = ObjectCacheUtil.GetOrCreate(world.Api, "cupellationfurnace-ignitestacks", () => BlockBehaviorCanIgnite.CanIgniteStacks(world.Api, true).ToArray()),
            });
        if (_done <= 0 && TestIn)
            help.Add(new WorldInteraction
            {
                ActionLangCode = Key("charge"), MouseButton = EnumMouseButton.Right,
                Itemstacks = Of("seraphhorizons:bullion-galena-lead", "seraphhorizons:bullion-galena_nativesilver-lead", Bullion.RichLeadCode, Cupelling.LeadIngot),
            });
        if (!SilverCake.Empty || !CopperBits.Empty || Litharge > 0)
            help.Add(new WorldInteraction { ActionLangCode = Key("takeout"), MouseButton = EnumMouseButton.Right });
        help.Add(new WorldInteraction { ActionLangCode = Key("damper"), MouseButton = EnumMouseButton.Right, HotKeyCode = "shift" });
        help.Add(_capped
            ? new WorldInteraction { ActionLangCode = Key("uncap"), MouseButton = EnumMouseButton.Right }
            : new WorldInteraction { ActionLangCode = Key("cap"), MouseButton = EnumMouseButton.Right, Itemstacks = Of(rig.CapCode) });
        return help.ToArray();
    }

    // ---- Drawing ----

    /// <summary>The stages as built and the test while it is in; then by the furnace's state: the coal on the grate
    /// (<c>coal</c>), burning (<c>fire</c>), the charge's lead in the dish (<c>lead</c>, the test in), the litharge
    /// running off while it works (<c>stream</c>), the cake (<c>silver</c>), the litharge in the pot, the damper's
    /// place and the cap.</summary>
    public override bool ShowsPart(string? requires)
    {
        if (requires is null or CupellationRequires.Brickwork or CupellationRequires.Lining or CupellationRequires.Fittings or CupellationRequires.Test)
            return base.ShowsPart(requires);
        if (!Built)
            return false;
        switch (requires)
        {
            case CupellationRequires.Coal: return !InfeedSlot.Empty || _lit;
            case CupellationRequires.Fire: return _lit;
            case CupellationRequires.Lead: return TestIn && Charge.Any(s => !s.Empty);
            case CupellationRequires.Stream: return TestIn && _running;
            case CupellationRequires.Silver: return TestIn && !SilverCake.Empty;
            case CupellationRequires.Litharge: return Litharge > 0;
            case CupellationRequires.OutletCap: return _capped;
        }
        return requires == CupellationRequires.DamperPart(_damper);
    }

    public override string TextureKey => BrickType ?? "";

    /// <summary>The block's textures, the brickwork's <c>brick</c> code in the furnace's brick type: its base with its
    /// overlays, composed into the block atlas as the crucible furnace's are.</summary>
    public override ITexPositionSource PartTextures(ICoreClientAPI capi)
    {
        var inner = capi.Tesselator.GetTextureSource(Block);
        if (FurnaceRig is not { } rig || (rig.Bricks.Get(BrickType) ?? rig.Bricks.Get(rig.Bricks.Default)) is not { } brick)
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

    /// <summary>The frame and every stage's items, the test with use left, and the cap. The coal, the charge and what
    /// it gave drop with the inventory.</summary>
    public override IEnumerable<ItemStack> AssemblyDrops()
    {
        foreach (var drop in base.AssemblyDrops())
            yield return drop;
        if (_capped && FurnaceRig is { } rig && Api.World.GetItem(new AssetLocation(rig.CapCode)) is { Id: > 0 } plate)
            yield return new ItemStack(plate, rig.CapCount);
    }

    // ---- Saving and syncing ----

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetInt("damper", (int)_damper);
        tree.SetBool("capped", _capped);
        tree.SetBool("lit", _lit);
        tree.SetBool("running", _running);
        tree.SetDouble("ember", _ember);
        tree.SetDouble("done", _done);
        tree.SetDouble("air", _air);
        tree.SetDouble("lastHours", _lastHours);
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
        _running = tree.GetBool("running");
        _ember = Math.Clamp(tree.GetDouble("ember"), 0, 1);
        _done = Math.Max(0, tree.GetDouble("done"));
        _air = Math.Clamp(tree.GetDouble("air"), 0, 1);
        _lastHours = tree.GetDouble("lastHours", double.NaN);
        int? Opt(string key) => tree.GetInt(key, int.MinValue) is var v && v != int.MinValue ? v : null;
        _row = new FurnaceRow(tree.GetInt("rowFirst"), Math.Max(1, tree.GetInt("rowCount", 1)), Opt("rowChimneyAt"),
            tree.GetInt("rowChimneyHeight"), Opt("rowOpenOutlet"), (RowProblem)tree.GetInt("rowProblem", (int)RowProblem.NoChimney));
    }

    // ---- Info ----

    protected override void AppendContents(StringBuilder dsc)
    {
        if (!Built || FurnaceRig is not { } rig)
            return;
        string L(string key, params object[] args) => Lang.Get("seraphhorizons:cupellationfurnace-info-" + key, args);
        dsc.AppendLine(L("damper-" + _damper.ToString().ToLowerInvariant()));
        dsc.AppendLine(_lit ? L("fire-lit", Coal) : Coal > 0 ? L("fire-out", Coal) : L("fire-nofuel"));
        double units = ChargeUnits;
        if (units > 0)
        {
            double lead = ChargeLead;
            dsc.AppendLine(L("charge", units.ToString("0", CultureInfo.InvariantCulture), FurnaceConfig.ChargeUnits.ToString("0", CultureInfo.InvariantCulture)));
            if (_done > 0 || _running)
                dsc.AppendLine(L("working", (int)Math.Floor(100 * (lead > 0 ? _done / lead : 1)), (int)Math.Round(100 * _air)));
            else if (ChargeRefusal() is var r and not CupellingRefusal.None)
                dsc.AppendLine(RefusalText(r, FurnaceConfig.ChargeUnits));
            else if (_lit && _air <= 0)
                dsc.AppendLine(L("noblast", rig.BlastPressure.ToString("0.#", CultureInfo.InvariantCulture)));
        }
        else
            dsc.AppendLine(L("empty", FurnaceConfig.ChargeUnits.ToString("0", CultureInfo.InvariantCulture)));
        if (!SilverCake.Empty)
            dsc.AppendLine(L("cake", SilverCake.StackSize, SilverCake.Itemstack!.GetName()));
        if (!CopperBits.Empty)
            dsc.AppendLine(L("cake", CopperBits.StackSize, CopperBits.Itemstack!.GetName()));
        if (Litharge > 0)
            dsc.AppendLine(L("pot", Litharge));
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
