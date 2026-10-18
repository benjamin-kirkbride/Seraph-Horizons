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
public class BELiquationFurnace : BEOreMachine
{
    private const int FirstChargeSlot = 1;
    private static readonly AssetLocation MetalSound = new("game", "sounds/block/ingot");

    private bool _capped, _lit, _doorShut, _sweating;
    private Damper _damper;
    private double _sweated, _fuelBurnt;
    private double _tinUnits, _leadUnits;
    private string _metal = "tin", _byProduct = "lead";
    private double _lastHours = double.NaN;
    private double _syncedSweated;
    private FurnaceRow _row = new(0, 1, null, 0, null, RowProblem.NoChimney);

    public override OreMachine? Kind => OreMachine.LiquationFurnace;

    /// <summary>Nothing goes in by chute: the furnace is charged and stoked by hand (<see cref="InteractContent"/>).</summary>
    public override bool Takes(ItemStack stack) => false;

    protected override IEnumerable<(string Anchor, ItemStack Stack)> Products(ItemStack fed) => [];

    protected override IReadOnlyList<string> OutputAnchors => [];

    private LiquationFurnaceSystem Furnace => LiquationFurnaceSystem.Of(Api);
    private LiquationFurnaceConfig FurnaceConfig => Furnace.Config;
    public LiquationFurnaceRig? FurnaceRig => Api == null ? null : Furnace.Rig;
    private OreRecovery Recovery => OreProcessingSystem.RecoveryFor(Api);

    public bool Capped => _capped;
    public bool Lit => _lit;
    public bool DoorShut => _doorShut;
    public Damper Damper => _damper;
    public FurnaceRow Row => _row;

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
        .Select(s => (Ore: s.Itemstack is { } st ? LiquationFurnacing.OreOf(Recovery, st.Collectible.Code.ToString()) : null, s.StackSize))
        .Where(b => b.Ore != null)
        .Select(b => new LiquationBars(b.Ore!, b.StackSize));

    public double ChargeUnits => LiquationFurnacing.ChargeUnits(Charge);

    public bool ProductWaiting => LiquationFurnacing.Waiting(_tinUnits) || LiquationFurnacing.Waiting(_leadUnits);

    public LiquationPhase Phase => LiquationFurnacing.PhaseOf(Bars, ProductWaiting);

    /// <summary>The units of tin an hour at full pace (ore-processing.json <c>machines.liquationfurnace.perHour</c>).</summary>
    public double PerHour => Spec?.ItemsPerHour ?? 0;

    /// <summary>The share of its full pace the charge sweats at now.</summary>
    public double Pace => LiquationFurnacing.Pace(_lit, _row.Works, _damper, _doorShut, FurnaceConfig);

    /// <summary>The fired brick type of its brickwork (<c>red</c>), once the first is in.</summary>
    public string? BrickType =>
        Assembly?.Build.CodeIn(0, 0) is { } code && FurnaceRig is { } rig ? rig.Bricks.TypeOf(code) : null;

    /// <summary>The fire burns while lit, charge or none.</summary>
    public override bool HasWork => _lit;

    public override double JobFill => _lit ? 1 : 0;

    /// <summary>The fire door: shut (1) or slid up open (0, the rest pose).</summary>
    public override double PartDepth => _doorShut ? 1 : 0;

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        if (api.Side == EnumAppSide.Server)
            RegisterGameTickListener(OnRowTick, 1000);
    }

    // ---- The row and its chimney ----

    private void OnRowTick(float dt)
    {
        if (!Built)
            return;
        var row = FindRow();
        if (row != _row)
        {
            _row = row;
            MarkDirty(true);
        }
    }

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
    private BELiquationFurnace? Neighbour(int k)
    {
        if (FurnaceRig is not { } rig)
            return null;
        var w = Footprint.ToWorld(new Int3(k * rig.Width, 0, 0), Side);
        return Api.World.BlockAccessor.GetBlockEntity(Pos.AddCopy(w.X, w.Y, w.Z)) is BELiquationFurnace { Built: true } f && f.Side == Side ? f : null;
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

    public override bool Ignitable => Built;

    public override bool Burning => _lit;

    public override string? CannotIgnite() =>
        !Built ? Lang.Get("seraphhorizons:liquationfurnace-error-notbuilt")
        : LiquationFurnacing.CannotLight(_lit, Fuel) is var r and not LiquationFurnaceRefusal.None ? RefusalText(r)
        : null;

    public override void Ignite(IPlayer? byPlayer)
    {
        if (Api.Side != EnumAppSide.Server)
            return;
        if (CannotIgnite() is { } why)
        {
            if (byPlayer is IServerPlayer sp)
                sp.SendIngameError("liquationfurnace", why);
            return;
        }
        _lit = true;
        _fuelBurnt = 0;
        _lastHours = Api.World.Calendar.TotalHours;
        _row = FindRow();
        Api.World.PlaySoundAt(new AssetLocation("game", "sounds/torch-ignite"), Pos.X + 0.5, Pos.Y + 0.5, Pos.Z + 0.5, byPlayer, true, 16, 0.6f);
        if (byPlayer != null)
            Api.World.Logger.Audit("{0} lit the liquation furnace at {1} ({2} bars).", byPlayer.PlayerName, Pos, Bars);
        MarkDirty(true);
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

    private bool IsOutletTop(BlockSelection sel) =>
        FurnaceRig is { } rig && sel.Face == BlockFacing.UP && sel.Position.Equals(CellPos(rig.OutletCell));

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
                Itemstacks = ObjectCacheUtil.GetOrCreate(world.Api, "liquationfurnace-ignitestacks", () => BlockBehaviorCanIgnite.CanIgniteStacks(world.Api, true).ToArray()),
            });
        help.Add(new WorldInteraction { ActionLangCode = Key(_doorShut ? "opendoor" : "shutdoor"), MouseButton = EnumMouseButton.Right });
        help.Add(new WorldInteraction { ActionLangCode = Key("damper"), MouseButton = EnumMouseButton.Right });
        help.Add(_capped
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
            case LiquationRequires.OutletCap: return _capped;
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

    /// <summary>The frame and every stage's items, the cap, and the tin and lead not yet taken out. The fuel and the bars
    /// drop with the inventory.</summary>
    public override IEnumerable<ItemStack> AssemblyDrops()
    {
        foreach (var drop in base.AssemblyDrops())
            yield return drop;
        if (FurnaceRig is not { } rig)
            yield break;
        if (_capped && Api.World.GetItem(new AssetLocation(rig.CapCode)) is { Id: > 0 } plate)
            yield return new ItemStack(plate, rig.CapCount);
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
        tree.SetBool("capped", _capped);
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
        _capped = tree.GetBool("capped");
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
        int? Opt(string key) => tree.GetInt(key, int.MinValue) is var v && v != int.MinValue ? v : null;
        _row = new FurnaceRow(tree.GetInt("rowFirst"), Math.Max(1, tree.GetInt("rowCount", 1)), Opt("rowChimneyAt"),
            tree.GetInt("rowChimneyHeight"), Opt("rowOpenOutlet"), (RowProblem)tree.GetInt("rowProblem", (int)RowProblem.NoChimney));
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
            dsc.AppendLine(L(!_row.Works ? "nodraught-chimney" : "nodraught-damper"));
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
