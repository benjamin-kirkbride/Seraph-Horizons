using System.Globalization;
using System.Text;
using SeraphHorizons.Mod.CrucibleFurnace;
using SeraphHorizons.Mod.CrucibleFurnace.Core;
using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.Ore.Core;
using SeraphHorizons.Mod.Ore.Processing;
using SeraphHorizons.Mod.ParkesKettle.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.ParkesKettle;

/// <summary>
/// The Parkes kettle (#927; README "Ore processing: the Parkes kettle"): a desilverizing kettle on the shared ore
/// machine code (<see cref="BEOreMachine"/>), batch, loaded and emptied by hand, with a powered stirrer and its own
/// fire (<see cref="ParkesKettling"/> has its rules). Built as its build file says (the iron frame, the brickwork in
/// one fired brick type, the lining, the kettle with its doors, the stirrer); then coal goes on the grate at the fire
/// door and is lit with a firestarter or a torch, silver-lead bars and then zinc bits go into the kettle, and the
/// stirrer works the charge while the fire heats it, the row's chimney drawing (the crucible furnace's row rule,
/// <see cref="FurnaceRow"/>). Done, an empty hand skims the crust (rich lead with the charge's silver, and the zinc
/// back) and then ladles the desilverized lead out.
/// <para>Its state is in its own fields, not the inventory (which stays empty): breaking it gives back what is in
/// it.</para>
/// </summary>
public class BEParkesKettle : BEOreMachine
{
    private static readonly AssetLocation SplashSound = new("game", "sounds/effect/water-fill");
    private static readonly AssetLocation CoalSound = new("game", "sounds/block/charcoal");

    private string? _ore;
    private double _lead;
    private int _zinc;
    private double _coal;
    private bool _lit, _capped, _done, _skimmed;
    private Damper _damper;
    private double _progress;
    private int _rich, _leadOut, _zincBack;
    private double _silverCarry, _zincCarry;
    private double _lastHours = double.NaN;
    private double _syncedProgress;
    private FurnaceRow _row = new(0, 1, null, 0, null, RowProblem.NoChimney);

    public override OreMachine? Kind => OreMachine.ParkesKettle;

    /// <summary>Oiled (its stirrer's shaft), its job a charge stirred, by its share of a full charge.</summary>
    protected override OilMachine? OilKind => OilMachine.ParkesKettle;

    /// <summary>Nothing goes in by chute: the kettle is charged by hand (<see cref="InteractContent"/>).</summary>
    public override bool Takes(ItemStack stack) => false;

    protected override IEnumerable<(string Anchor, ItemStack Stack)> Products(ItemStack fed) => [];

    protected override IReadOnlyList<string> OutputAnchors => [];

    private ParkesKettleSystem Kettle => ParkesKettleSystem.Of(Api);
    private ParkesKettleConfig KettleConfig => Kettle.Config;
    public ParkesKettleRig? KettleRig => Api == null ? null : Kettle.Rig;

    /// <summary>The bullion's ore (<c>galena</c>), while lead is in.</summary>
    public string? Ore => _ore;
    /// <summary>Units of silver-lead in the bath.</summary>
    public double LeadUnits => _lead;
    public int ZincBits => _zinc;
    public double Coal => _coal;
    public bool Lit => _lit;
    public bool Capped => _capped;
    public Damper Damper => _damper;
    /// <summary>Shaft turns of the charge worked (heat-weighted).</summary>
    public double Progress => _progress;
    public int RichLead => _rich;
    public int LeadOut => _leadOut;
    public int ZincBack => _zincBack;
    public bool Skimmed => _skimmed;
    public FurnaceRow Row => _row;

    /// <summary>The units a full charge holds (ore-processing.json <c>machines.parkeskettle.charge</c>).</summary>
    public double ChargeUnits => Spec?.ChargeItems ?? KettleRig?.ChargeUnits ?? 0;

    /// <summary>Coal the fire burns an in-game hour with the damper open: the rig's coal a charge over the charge's
    /// hours (6 in 5, #927).</summary>
    public double CoalPerHour => KettleRig is { } rig && Spec is { ChargeHours: > 0 } spec ? rig.CoalPerCharge / spec.ChargeHours : 0;

    public KettlePhase Phase => ParkesKettling.PhaseOf(_lead, _zinc, _done, KettleConfig);

    /// <summary>The share of its heat the fire gives the bath now.</summary>
    public double Heat => ParkesKettling.Heat(_lit, _row.Works, _damper, KettleConfig);

    /// <summary>Shaft turns this charge takes.</summary>
    public double TurnsNeeded => ParkesKettling.TurnsFor(_lead, ChargeUnits, Spec?.TurnsPerCharge ?? 0);

    /// <summary>The charge's turns stirred, of <see cref="TurnsNeeded"/>, while it stirs.</summary>
    public override double? WorkProgress =>
        Phase != KettlePhase.Stirring ? null : TurnsNeeded is var needed && needed > 0 ? _progress / needed : 1;

    /// <summary>Units in the bath: the charge's, or once done the lead still to ladle out and the crust's.</summary>
    public double BathUnits => _done ? (_leadOut + (_skimmed ? 0 : _rich)) * Bullion.UnitsPerItem : _lead;

    /// <summary>The fired brick type of its brickwork (<c>red</c>), once the first is in.</summary>
    public string? BrickType =>
        Assembly?.Build.CodeIn(0, 0) is { } code && KettleRig is { } rig ? rig.Bricks.TypeOf(code) : null;

    public override bool HasWork => Phase == KettlePhase.Stirring && Heat > 0;

    /// <summary>The stirrer's load follows how full the kettle is (#927).</summary>
    public override double JobFill => ParkesKettling.Fill(BathUnits, ChargeUnits);

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        if (api.Side == EnumAppSide.Server)
            RegisterGameTickListener(OnFireTick, 1000);
    }

    // ---- The row and its chimney ----

    /// <summary>The chimney standing on the outlet, counted as the crucible furnace counts its own.</summary>
    public ChimneyColumn ChimneyOnOutlet()
    {
        if (KettleRig is not { } rig)
            return default;
        var ba = Api.World.BlockAccessor;
        var pos = CellPos(rig.ChimneyBase);
        return ChimneyColumn.Measure(n => ba.GetBlock(pos.UpCopy(n))?.Code?.ToString(), CrucibleFurnaceSystem.Of(Api).Config);
    }

    /// <summary>The kettle at offset <paramref name="k"/> along its row (built, facing the same way), or null.</summary>
    private BEParkesKettle? Neighbour(int k)
    {
        if (KettleRig is not { } rig)
            return null;
        var w = Footprint.ToWorld(new Int3(k * rig.Width, 0, 0), Side);
        return Api.World.BlockAccessor.GetBlockEntity(Pos.AddCopy(w.X, w.Y, w.Z)) is BEParkesKettle { Built: true } s && s.Side == Side ? s : null;
    }

    /// <summary>Its row: kettles side by side along their width, one chimney on any outlet, the others capped.</summary>
    public FurnaceRow FindRow()
    {
        if (KettleRig is not { } rig || !Built)
            return new FurnaceRow(0, 1, null, 0, null, RowProblem.NoChimney);
        RowMember? At(int k) => k == 0
            ? new RowMember(_capped, ChimneyOnOutlet())
            : Neighbour(k) is { } other ? new RowMember(other.Capped, other.ChimneyOnOutlet()) : null;
        return FurnaceRow.Find(At, rig.RowMax, rig.MinChimney);
    }

    // ---- The fire ----

    private void OnFireTick(float dt)
    {
        if (!Built)
            return;
        bool dirty = false;
        var row = FindRow();
        if (row != _row)
        {
            _row = row;
            dirty = true;
        }
        double now = Api.World.Calendar.TotalHours;
        double hours = double.IsNaN(_lastHours) ? 0 : now - _lastHours;
        _lastHours = now;
        if (_lit)
        {
            int layers = Layers;
            var (coal, burntOut) = ParkesKettling.Burn(_coal, hours, CoalPerHour, ParkesKettling.BurnShare(_row.Works, _damper, KettleConfig));
            _coal = coal;
            if (burntOut)
            {
                _coal = 0;
                _lit = false;
                dirty = true;
            }
            dirty |= layers != Layers;
        }
        if (dirty)
            MarkDirty(true);
    }

    private int Layers => KettleRig is { } rig ? ParkesKettling.FuelLayers(_coal, rig.FuelPerLayer, rig.FuelLayers) : 0;

    /// <summary>Ages a burning fire by <paramref name="hours"/>, as if they had passed (tests).</summary>
    public void Age(double hours) => _lastHours = (double.IsNaN(_lastHours) ? Api.World.Calendar.TotalHours : _lastHours) - hours;

    public override bool Ignitable => Built;

    public override bool Burning => _lit;

    public override string? CannotIgnite() =>
        !Built ? Lang.Get("seraphhorizons:parkeskettle-error-notbuilt")
        : ParkesKettling.CannotLight(_lit, _coal) is var r and not KettleRefusal.None ? RefusalText(r)
        : null;

    public override void Ignite(IPlayer? byPlayer)
    {
        if (Api.Side != EnumAppSide.Server)
            return;
        if (CannotIgnite() is { } why)
        {
            if (byPlayer is IServerPlayer sp)
                sp.SendIngameError("parkeskettle", why);
            return;
        }
        _lit = true;
        _lastHours = Api.World.Calendar.TotalHours;
        _row = FindRow();
        Api.World.PlaySoundAt(new AssetLocation("game", "sounds/torch-ignite"), Pos.X + 0.5, Pos.Y + 0.5, Pos.Z + 0.5, byPlayer, true, 16, 0.6f);
        MarkDirty(true);
    }

    // ---- The stirring ----

    /// <summary>The stirrer works the charge: a turn of the shaft is a turn of the paddles (the mitre pair, ratio 1),
    /// counted at the fire's heat; the charge done at its turns.</summary>
    protected override bool Work(double radians)
    {
        double needed = TurnsNeeded;
        if (!(needed > 0) || Phase != KettlePhase.Stirring)
            return false;
        _progress += radians / (2 * Math.PI) * Heat;
        if (_progress >= needed - 1e-9)
        {
            Finish();
            return true;
        }
        if (_progress - _syncedProgress >= needed / 100)
        {
            _syncedProgress = _progress;
            return true;
        }
        return false;
    }

    /// <summary>Stirs the charge through as if its turns had run (tests).</summary>
    public void StirThrough() => Work(2 * Math.PI * (TurnsNeeded - _progress) / Math.Max(1e-6, Heat) + 1e-6);

    /// <summary>The charge done: its silver whole into rich lead (the kettle's figure), the rest of the lead
    /// desilverized, the zinc back at the rule's share.</summary>
    private void Finish()
    {
        var recovery = OreProcessingSystem.RecoveryFor(Api);
        double silver = _ore is { } ore
            ? recovery.Smelted(recovery.Ore(ore), _lead, PartingStation.ParkesKettle).Where(m => m.Metal == "silver").Sum(m => m.Units)
            : 0;
        var y = ParkesKettling.Finish(_lead, silver, _silverCarry, recovery.RichLeadSilver, _zinc, _zincCarry, recovery.ZincReturn);
        _rich = y.RichLead;
        _leadOut = y.Lead;
        _zincBack = y.ZincBits;
        _silverCarry = y.SilverCarry;
        _zincCarry = y.ZincCarry;
        DrainOil(ParkesKettling.Fill(_lead, ChargeUnits));
        _lead = 0;
        _zinc = 0;
        _progress = 0;
        _syncedProgress = 0;
        _done = true;
        _skimmed = false;
        MarkDirty(true);
    }

    // ---- Interaction ----

    private static string RefusalText(KettleRefusal r, params object[] args) =>
        Lang.Get("seraphhorizons:parkeskettle-error-" + r.ToString().ToLowerInvariant(), args);

    private bool Refuse(IPlayer byPlayer, KettleRefusal r, params object[] args)
    {
        if (Api.Side == EnumAppSide.Server && byPlayer is IServerPlayer sp)
            sp.SendIngameError("parkeskettle-" + r.ToString().ToLowerInvariant(), RefusalText(r, args));
        return true;
    }

    private bool IsOutletTop(BlockSelection sel) =>
        KettleRig is { } rig && sel.Face == BlockFacing.UP && sel.Position.Equals(CellPos(rig.OutletCell));

    /// <summary>The ore of a bullion bar, or null for anything else.</summary>
    private static string? BullionOre(ItemStack stack) =>
        stack.Collectible.Code is { } code && code.ToString() != Bullion.RichLeadCode ? Bullion.OreOf(code.ToString()) : null;

    /// <summary>
    /// Once built: coal, coke or charcoal goes on the grate (up to its 8); silver-lead bars into the kettle (up to a
    /// charge, all of one ore), then zinc bits (as many as the lead takes, which starts the stirring); an iron plate
    /// caps the outlet. An empty hand skims the crust of a charge done, then ladles its lead out, or on the outlet's
    /// top takes the cap off; with sneak, it moves the damper (open, half, shut). Lighting is a firestarter's or a
    /// torch's (<see cref="Ignite"/>). Before it is built, the build's.
    /// </summary>
    protected override bool? InteractContent(IPlayer byPlayer, ItemSlot slot, BlockSelection blockSel)
    {
        if (!Built || KettleRig is not { } rig)
            return null;
        var stack = slot?.Itemstack;
        bool server = Api.Side == EnumAppSide.Server;
        if (stack == null)
        {
            if (byPlayer.Entity.Controls.ShiftKey)
            {
                if (server)
                {
                    _damper = (Damper)(((int)_damper + 1) % 3);
                    Api.World.PlaySoundAt(new AssetLocation("game", "sounds/block/metaldoor-open"), Pos, -0.25, byPlayer);
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
            if (Phase != KettlePhase.Done)
                return null;
            if (server)
                TakeOut(byPlayer);
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
        if (ParkesKettling.IsFuel(code))
        {
            var (take, why) = ParkesKettling.AddFuel(_coal, stack.StackSize, rig.Grate);
            if (why != KettleRefusal.None)
                return Refuse(byPlayer, why, rig.Grate);
            if (server)
            {
                slot!.TakeOut(take);
                slot.MarkDirty();
                _coal += take;
                Api.World.PlaySoundAt(CoalSound, Pos, -0.25, byPlayer);
                MarkDirty(true);
            }
            return true;
        }
        if (BullionOre(stack) is { } ore)
        {
            var recovery = OreProcessingSystem.RecoveryFor(Api);
            if (!Bullion.TakenBy(recovery, recovery.Ore(ore), PartingStation.ParkesKettle))
                return Refuse(byPlayer, KettleRefusal.NotTaken, stack.GetName());
            if (_ore != null && _ore != ore && _lead > 0)
                return Refuse(byPlayer, KettleRefusal.OtherBullion, stack.GetName());
            var (take, why) = ParkesKettling.AddLead(Phase, _lead, stack.StackSize, Bullion.UnitsPerItem, ChargeUnits);
            if (why != KettleRefusal.None)
                return Refuse(byPlayer, why, (int)(ChargeUnits / Bullion.UnitsPerItem));
            if (server)
            {
                slot!.TakeOut(take);
                slot.MarkDirty();
                _ore = ore;
                _lead += take * Bullion.UnitsPerItem;
                Api.World.PlaySoundAt(SplashSound, Pos, -0.25, byPlayer);
                MarkDirty(true);
            }
            return true;
        }
        if (code == ParkesKettling.ZincBit)
        {
            var (take, why) = ParkesKettling.AddZinc(Phase, _lead, _zinc, stack.StackSize, KettleConfig);
            if (why != KettleRefusal.None)
                return Refuse(byPlayer, why);
            if (server)
            {
                slot!.TakeOut(take);
                slot.MarkDirty();
                _zinc += take;
                if (Phase == KettlePhase.Stirring)
                {
                    _progress = 0;
                    _syncedProgress = 0;
                    if (byPlayer != null)
                        Api.World.Logger.Audit("{0} charged the Parkes kettle at {1} ({2} units of {3} silver-lead).", byPlayer.PlayerName, Pos, _lead, _ore);
                }
                Api.World.PlaySoundAt(SplashSound, Pos, -0.25, byPlayer);
                MarkDirty(true);
            }
            return true;
        }
        return null;
    }

    /// <summary>A charge done: the crust first (the rich lead and the zinc back), then the lead, a ladling at a time.</summary>
    private void TakeOut(IPlayer byPlayer)
    {
        var world = Api.World;
        if (!_skimmed)
        {
            if (_rich > 0 && world.GetItem(new AssetLocation(Bullion.RichLeadCode)) is { Id: > 0 } rich)
                Give(byPlayer, new ItemStack(rich, _rich));
            if (_zincBack > 0 && world.GetItem(new AssetLocation(ParkesKettling.ZincBit)) is { Id: > 0 } zinc)
                Give(byPlayer, new ItemStack(zinc, _zincBack));
            _rich = 0;
            _zincBack = 0;
            _skimmed = true;
        }
        else if (_leadOut > 0 && world.GetItem(new AssetLocation(ParkesKettling.Lead)) is { Id: > 0 } lead)
        {
            int n = Math.Min(_leadOut, KettleConfig.LadleIngots);
            Give(byPlayer, new ItemStack(lead, n));
            _leadOut -= n;
        }
        else
            _leadOut = 0;
        if (_skimmed && _leadOut <= 0)
        {
            _done = false;
            _skimmed = false;
            _ore = null;
        }
        world.PlaySoundAt(SplashSound, Pos, -0.25, byPlayer);
        MarkDirty(true);
    }

    private void Give(IPlayer byPlayer, ItemStack? stack)
    {
        if (stack == null || stack.StackSize <= 0)
            return;
        if (!byPlayer.InventoryManager.TryGiveItemstack(stack, true))
            Api.World.SpawnItemEntity(stack, Pos.ToVec3d().Add(0.5, 2.2, 0.5));
    }

    public override WorldInteraction[] InteractionHelp(IPlayer forPlayer)
    {
        if (!Built || KettleRig is not { } rig)
            return base.InteractionHelp(forPlayer);
        var world = Api.World;
        ItemStack[] Of(params string[] codes) => codes
            .Select(c => (CollectibleObject?)world.GetItem(new AssetLocation(c)) ?? world.GetBlock(new AssetLocation(c)))
            .OfType<CollectibleObject>().Where(c => c.Id > 0).Select(c => new ItemStack(c)).ToArray();
        var help = new List<WorldInteraction>();
        string Key(string k) => "seraphhorizons:parkeskettle-help-" + k;
        if (_coal < rig.Grate - 1e-9)
            help.Add(new WorldInteraction { ActionLangCode = Key("fuel"), MouseButton = EnumMouseButton.Right, Itemstacks = Of([.. ParkesKettling.Fuels]) });
        if (!_lit && _coal >= 1)
            help.Add(new WorldInteraction
            {
                ActionLangCode = Key("light"), MouseButton = EnumMouseButton.Right,
                Itemstacks = ObjectCacheUtil.GetOrCreate(world.Api, "parkeskettle-ignitestacks", () => BlockBehaviorCanIgnite.CanIgniteStacks(world.Api, true).ToArray()),
            });
        switch (Phase)
        {
            case KettlePhase.Empty or KettlePhase.Charging:
                if (_lead < ChargeUnits)
                    help.Add(new WorldInteraction { ActionLangCode = Key("lead"), MouseButton = EnumMouseButton.Right, Itemstacks = Of("seraphhorizons:bullion-galena-lead") });
                if (Phase == KettlePhase.Charging)
                    help.Add(new WorldInteraction { ActionLangCode = Key("zinc"), MouseButton = EnumMouseButton.Right, Itemstacks = Of(ParkesKettling.ZincBit) });
                break;
            case KettlePhase.Done:
                help.Add(new WorldInteraction { ActionLangCode = Key(_skimmed ? "ladle" : "skim"), MouseButton = EnumMouseButton.Right });
                break;
        }
        help.Add(new WorldInteraction { ActionLangCode = Key("damper"), MouseButton = EnumMouseButton.Right, HotKeyCode = "shift" });
        help.Add(_capped
            ? new WorldInteraction { ActionLangCode = Key("uncap"), MouseButton = EnumMouseButton.Right }
            : new WorldInteraction { ActionLangCode = Key("cap"), MouseButton = EnumMouseButton.Right, Itemstacks = Of(rig.CapCode) });
        return help.ToArray();
    }

    // ---- Drawing ----

    /// <summary>
    /// The stages as built; then, once the kettle is in, its damper's place, the bath's levels
    /// (<c>ceil(fill × levels)</c>), the zinc stirred in (the first half of the charge) and the crust risen (the
    /// second half, and done until skimmed), the grate's coal by layers (burning while lit) and the cap.
    /// </summary>
    public override bool ShowsPart(string? requires)
    {
        if (requires is null or KettleRequires.Brickwork or KettleRequires.Lining or KettleRequires.Kettle or KettleRequires.Stirrer)
            return base.ShowsPart(requires);
        if (requires == KettleRequires.OutletCap)
            return _capped;
        if (KettleRig is not { } rig || !base.ShowsPart(KettleRequires.Kettle))
            return false;
        bool secondHalf = Phase == KettlePhase.Stirring && _progress >= TurnsNeeded / 2;
        switch (requires)
        {
            case KettleRequires.DamperOpen: return _damper == Damper.Open;
            case KettleRequires.DamperHalf: return _damper == Damper.Half;
            case KettleRequires.DamperShut: return _damper == Damper.Shut;
            case KettleRequires.Zinc: return _zinc > 0 && !_done && !secondHalf;
            case KettleRequires.Crust: return secondHalf || _done && !_skimmed;
        }
        if (requires.StartsWith("lead", StringComparison.Ordinal))
            return Index(requires, "lead") <= ParkesKettling.Levels(BathUnits, ChargeUnits, rig.LeadLevels);
        if (requires.StartsWith("fuel", StringComparison.Ordinal))
            return !_lit && Index(requires, "fuel") <= Layers;
        if (requires.StartsWith("fire", StringComparison.Ordinal))
            return _lit && Index(requires, "fire") <= Layers;
        return false;
    }

    private static int Index(string requires, string prefix) =>
        int.TryParse(requires.AsSpan(prefix.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : int.MaxValue;

    public override string TextureKey => BrickType ?? "";

    /// <summary>The block's textures, the brickwork's <c>brick</c> code in the setting's brick type: its base with its
    /// overlays, composed into the block atlas as the crucible furnace's are.</summary>
    public override ITexPositionSource PartTextures(ICoreClientAPI capi)
    {
        var inner = capi.Tesselator.GetTextureSource(Block);
        if (KettleRig is not { } rig || (rig.Bricks.Get(BrickType) ?? rig.Bricks.Get(rig.Bricks.Default)) is not { } brick)
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

    /// <summary>The frame and every stage's items, the cap, and what is in it: the silver-lead and zinc of a charge
    /// (whole bars and bits), the coal on the grate, and a charge done's crust and lead.</summary>
    public override IEnumerable<ItemStack> AssemblyDrops()
    {
        foreach (var drop in base.AssemblyDrops())
            yield return drop;
        if (KettleRig is not { } rig)
            yield break;
        var world = Api.World;
        ItemStack? Of(string code, int n) =>
            n > 0 && world.GetItem(new AssetLocation(code)) is { Id: > 0 } item ? new ItemStack(item, n) : null;
        var drops = new[]
        {
            _capped ? Of(rig.CapCode, rig.CapCount) : null,
            _ore != null ? Of(Bullion.Code(OreProcessingSystem.RecoveryFor(Api).Ore(_ore)), (int)Math.Floor(_lead / Bullion.UnitsPerItem + 1e-9)) : null,
            Of(ParkesKettling.ZincBit, _zinc + _zincBack),
            Of(ParkesKettling.Fuels[0], (int)Math.Floor(_coal + 1e-9)),
            Of(Bullion.RichLeadCode, _rich),
            Of(ParkesKettling.Lead, _leadOut),
        };
        foreach (var d in drops)
            if (d != null)
                yield return d;
    }

    // ---- Saving and syncing ----

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        if (_ore != null)
            tree.SetString("ore", _ore);
        else
            tree.RemoveAttribute("ore");
        tree.SetDouble("lead", _lead);
        tree.SetInt("zinc", _zinc);
        tree.SetDouble("coal", _coal);
        tree.SetBool("lit", _lit);
        tree.SetBool("capped", _capped);
        tree.SetBool("done", _done);
        tree.SetBool("skimmed", _skimmed);
        tree.SetInt("damper", (int)_damper);
        tree.SetDouble("progress", _progress);
        tree.SetInt("rich", _rich);
        tree.SetInt("leadOut", _leadOut);
        tree.SetInt("zincBack", _zincBack);
        tree.SetDouble("silverCarry", _silverCarry);
        tree.SetDouble("zincCarry", _zincCarry);
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
        _ore = tree.GetString("ore");
        _lead = Math.Max(0, tree.GetDouble("lead"));
        _zinc = Math.Max(0, tree.GetInt("zinc"));
        _coal = Math.Max(0, tree.GetDouble("coal"));
        _lit = tree.GetBool("lit");
        _capped = tree.GetBool("capped");
        _done = tree.GetBool("done");
        _skimmed = tree.GetBool("skimmed");
        _damper = (Damper)Math.Clamp(tree.GetInt("damper"), 0, 2);
        _progress = Math.Max(0, tree.GetDouble("progress"));
        _rich = Math.Max(0, tree.GetInt("rich"));
        _leadOut = Math.Max(0, tree.GetInt("leadOut"));
        _zincBack = Math.Max(0, tree.GetInt("zincBack"));
        _silverCarry = Math.Max(0, tree.GetDouble("silverCarry"));
        _zincCarry = Math.Max(0, tree.GetDouble("zincCarry"));
        _lastHours = tree.GetDouble("lastHours", double.NaN);
        int? Opt(string key) => tree.GetInt(key, int.MinValue) is var v && v != int.MinValue ? v : null;
        _row = new FurnaceRow(tree.GetInt("rowFirst"), Math.Max(1, tree.GetInt("rowCount", 1)), Opt("rowChimneyAt"),
            tree.GetInt("rowChimneyHeight"), Opt("rowOpenOutlet"), (RowProblem)tree.GetInt("rowProblem", (int)RowProblem.NoChimney));
    }

    // ---- Info ----

    protected override void AppendContents(StringBuilder dsc)
    {
        if (!Built || KettleRig is not { } rig)
            return;
        string L(string key, params object[] args) => Lang.Get("seraphhorizons:parkeskettle-info-" + key, args);
        string N(double v) => v.ToString("0", CultureInfo.InvariantCulture);
        dsc.AppendLine(_lit
            ? L("fire-lit", _coal.ToString("0.0", CultureInfo.InvariantCulture), rig.Grate)
            : _coal > 0 ? L("fire-laid", N(_coal), rig.Grate) : L("fire-empty", rig.Grate));
        dsc.AppendLine(L("damper-" + _damper.ToString().ToLowerInvariant()));
        switch (Phase)
        {
            case KettlePhase.Empty:
                dsc.AppendLine(L("empty", N(ChargeUnits)));
                break;
            case KettlePhase.Charging:
                dsc.AppendLine(L("charging", N(_lead), N(ChargeUnits), _zinc, ParkesKettling.ZincBitsFor(_lead, KettleConfig)));
                break;
            case KettlePhase.Stirring:
                double needed = TurnsNeeded;
                dsc.AppendLine(L("stirring", N(_lead), (int)Math.Floor(100 * (needed > 0 ? _progress / needed : 1))));
                if (Heat <= 0)
                    dsc.AppendLine(L(!_lit ? "cold" : !_row.Works ? "nodraught" : "banked"));
                else if (Heat < 1)
                    dsc.AppendLine(L("halfheat", (int)Math.Round(100 * Heat)));
                break;
            case KettlePhase.Done:
                dsc.AppendLine(_skimmed ? L("ladle", _leadOut) : L("done", _rich, _zincBack, _leadOut));
                break;
        }
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
