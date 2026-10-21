using System.Globalization;
using System.Text;
using SeraphHorizons.Mod.CrucibleFurnace;
using SeraphHorizons.Mod.CrucibleFurnace.Core;
using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.Ore.Core;
using SeraphHorizons.Mod.Ore.Processing;
using SeraphHorizons.Mod.ReverberatoryRoaster.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.ReverberatoryRoaster;

/// <summary>
/// The reverberatory roaster (#736; README "Ore processing: the reverberatory roaster"): the mechanical furnace on the
/// shared ore machine code (<see cref="BEOreMachine"/>), continuous. A sulfide's concentrate comes in by a chute into
/// the hopper (or by hand) and lies on the hearth as its bed; the water-cooled rabbles, turned by the axle, plough it
/// to the discharge, and each concentrate comes out of the output chute as its roasted concentrate at the furnace's
/// 100 % (<see cref="ReverberatoryRoasting"/>); native sulfur rock is melted into sulfur powder at 0.65. It works only
/// lit, with coal in its bunker (by a second chute, the <c>fuel</c> anchor, or by hand), its row's chimney drawing and
/// its ash-mouth damper not shut; a coal every six concentrate, 2 L of water a concentrate for the rabbles. The rabbles'
/// load follows the bed. The flue's sulfur is lost.
/// <para>Its inventory: slot 0 the bed (the infeed), slots 1 to 4 the output's held products, <see cref="CoalSlot"/>
/// the coal bunker.</para>
/// </summary>
public class BEReverberatory : BEFiredOreMachine
{
    /// <summary>The coal bunker: the second output's first slot (the furnace has one output).</summary>
    public const int CoalSlot = 1 + SlotsPerOutput;
    public const string Fuel = "fuel";
    private static readonly AssetLocation DamperSound = new("game", "sounds/effect/latch");
    private static readonly AssetLocation RakeSound = new("game", "sounds/block/gravel");

    private Damper _damper = Damper.Open;
    private bool _lit;
    private double _coalLeft;
    private double _progress;
    private readonly UnitCarry _carry = new();

    public override OreMachine? Kind => OreMachine.Reverberatory;

    /// <summary>Oiled (its rabbles' shaft), its job an item roasted, by the item's weight.</summary>
    protected override OilMachine? OilKind => OilMachine.ReverberatoryRoaster;

    private ReverberatorySystem Furnace => ReverberatorySystem.Of(Api);
    private ReverberatoryConfig FurnaceConfig => Api == null ? ReverberatoryConfig.Defaults : Furnace.Config;
    public ReverberatoryRig? FurnaceRig => Api == null ? null : Furnace.Rig;
    protected override FiredRig? FiredRig => FurnaceRig;

    public Damper Damper => _damper;
    public override bool Lit => _lit;
    public ItemSlot Bunker => Inventory[CoalSlot];
    public int Coal => Bunker.StackSize;
    /// <summary>Shaft turns put into the item on the hearth's discharge so far.</summary>
    public double Progress => _progress;

    /// <summary>The share of its pace it roasts at now.</summary>
    public double Pace => ReverberatoryRoasting.Pace(_damper, Row.Works, _lit, FurnaceConfig.HalfDamperPace);

    private OreRecovery Recovery => OreProcessingSystem.RecoveryFor(Api);

    // ---- What it takes ----

    public ReverberatoryFeed FeedOf(ItemStack stack)
    {
        var item = stack.Collectible;
        string code = item.Code?.ToString() ?? "";
        if (code == ReverberatoryRoasting.NativeSulfurCode)
            return Api == null || Recovery.NativeSulfurMelt(OreMachine.Reverberatory) > 0 ? ReverberatoryFeed.NativeSulfur : ReverberatoryFeed.None;
        string? ore = item.Variant["ore"];
        if (ore == null || item.Attributes?["oreForm"].AsString(null) != "concentrate" || Api == null)
            return ReverberatoryFeed.None;
        OreSpec spec;
        try
        {
            spec = Recovery.Ore(ore);
        }
        catch (KeyNotFoundException)
        {
            return ReverberatoryFeed.None;
        }
        var feed = ReverberatoryRoasting.FeedOf(code, "concentrate", spec);
        return feed == ReverberatoryFeed.Concentrate && Api.World.GetItem(new AssetLocation(OreProducts.RoastedCode(ore))) is { Id: > 0 }
            ? feed : ReverberatoryFeed.None;
    }

    private double WeightOf(ItemStack stack) =>
        ReverberatoryRoasting.Weight(FeedOf(stack), Recovery.ConcentrateUnits, Recovery.NativeSulfurUnits);

    public override bool Takes(ItemStack stack) => FeedOf(stack) != ReverberatoryFeed.None;

    public override int InfeedCapacity(ItemStack stack) =>
        Math.Min(stack.Collectible.MaxStackSize, ReverberatoryRoasting.Capacity(FurnaceConfig.HearthCharge, Api == null ? 1 : WeightOf(stack)));

    public override double JobFill =>
        InfeedSlot.Empty ? 0 : ReverberatoryRoasting.Fill(InfeedSlot.StackSize, WeightOf(InfeedSlot.Itemstack), FurnaceConfig.HearthCharge);

    public override bool HasWork => !InfeedSlot.Empty && Pace > 0;

    protected override IReadOnlyList<string> OutputAnchors => ["output"];

    protected override IReadOnlyList<string> SideFeeds => [Fuel];

    public override bool SideFeedTakes(string anchor, ItemStack stack) =>
        anchor == Fuel && ReverberatoryRoasting.IsFuel(stack.Collectible.Code?.ToString());

    protected override (ItemSlot Slot, int Capacity)? SideFeedSlot(string anchor) =>
        anchor == Fuel ? (Bunker, Bunker.Empty ? 64 : Bunker.Itemstack.Collectible.MaxStackSize) : null;

    // the default Work is replaced: an item's turns, water and coal go by its weight
    protected override IEnumerable<(string Anchor, ItemStack Stack)> Products(ItemStack fed) => Made(fed);

    protected override bool Wet => InfeedSlot.Empty || Water.Holds(WaterPerItem * WeightOf(InfeedSlot.Itemstack));

    /// <summary>The turns put into the item at the discharge, of the turns its weight takes.</summary>
    public override double? WorkProgress =>
        InfeedSlot.Empty || Spec is not { TurnsPerItem: > 0 } spec ? null
            : _progress / Math.Max(1e-9, spec.TurnsPerItem * WeightOf(InfeedSlot.Itemstack));

    // ---- Work ----

    /// <summary>The shaft's turns go into the item at the discharge at the furnace's pace; each item takes the
    /// machine's turns a concentrate × its weight, its water and its coal, and gives its products at the output.
    /// Short of coal, the fire goes out.</summary>
    protected override bool Work(double radians)
    {
        if (Spec is not { TurnsPerItem: > 0 } spec)
            return false;
        _progress += radians / (2 * Math.PI) * Pace;
        bool changed = false;
        while (!InfeedSlot.Empty && _lit)
        {
            double weight = WeightOf(InfeedSlot.Itemstack);
            if (_progress < spec.TurnsPerItem * weight - 1e-9)
                break;
            if (!OutputRoom || !Water.Holds(WaterPerItem * weight))
                break;
            var (take, left) = ReverberatoryRoasting.Coal(_coalLeft, weight, FurnaceConfig.ConcentratePerCoal);
            if (take > Coal)
            {
                GoOut();
                return true;
            }
            if (take > 0)
            {
                Bunker.TakeOut(take);
                Bunker.MarkDirty();
            }
            _coalLeft = left;
            SpendWater(WaterPerItem * weight);
            DrainOil(weight);
            _progress -= spec.TurnsPerItem * weight;
            var fed = InfeedSlot.TakeOut(1);
            InfeedSlot.MarkDirty();
            foreach (var (anchor, product) in Made(fed))
                Deliver(anchor, product);
            OnWorked(fed);
            changed = true;
        }
        if (InfeedSlot.Empty)
            _progress = 0;
        return changed;
    }

    /// <summary>What one item gives: its roasted concentrate's whole items, or native sulfur's sulfur powder, through
    /// the furnace's carry (nothing lost to rounding).</summary>
    private IEnumerable<(string Anchor, ItemStack Stack)> Made(ItemStack fed)
    {
        var world = Api.World;
        var recovery = Recovery;
        switch (FeedOf(fed))
        {
            case ReverberatoryFeed.Concentrate:
            {
                string ore = fed.Collectible.Variant["ore"]!;
                if (world.GetItem(new AssetLocation(OreProducts.RoastedCode(ore))) is not { Id: > 0 } roasted)
                    yield break;
                bool whole = OreStreamAttributes.IsWhole(fed);
                double units = ItemOreProduct.UnitsOf(fed.Collectible) is > 0 and var u ? u : recovery.ConcentrateUnits;
                var (outUnits, carryWhole) = ReverberatoryRoasting.Roast(recovery, recovery.Ore(ore), units, whole);
                double per = ItemOreProduct.UnitsOf(roasted) is > 0 and var r ? r : recovery.ConcentrateUnits;
                int count = _carry.Add(roasted.Code + (carryWhole ? "|whole" : ""), outUnits, per);
                if (count <= 0)
                    yield break;
                var stack = new ItemStack(roasted, count);
                OreStreamAttributes.MarkWhole(stack, carryWhole);
                yield return ("output", stack);
                break;
            }
            case ReverberatoryFeed.NativeSulfur:
            {
                if (world.GetItem(new AssetLocation(ReverberatoryRoasting.SulfurPowderCode)) is not { Id: > 0 } powder)
                    yield break;
                int count = _carry.Add(powder.Code.ToString(), ReverberatoryRoasting.Melt(recovery), recovery.SulfurUnitsPerPowder);
                if (count > 0)
                    yield return ("output", new ItemStack(powder, count));
                break;
            }
        }
    }

    protected override void OnWorked(ItemStack fed)
    {
        var at = WorldPoint(new Float3(1.0625f, 0.875f, 2.5f));
        Api.World.PlaySoundAt(RakeSound, at.X, at.Y, at.Z, null, true, 16, 0.3f);
    }

    protected override ItemStack[] FeedStacks(IWorldAccessor world) =>
        world.SearchItems(new AssetLocation(OreProducts.Domain, "concentrate-galena")).Take(1)
            .Concat(world.SearchItems(new AssetLocation(OreProducts.Domain, "concentrate-chalcopyrite")).Take(1))
            .Concat(world.SearchItems(new AssetLocation(ReverberatoryRoasting.NativeSulfurCode)).Take(1))
            .Select(i => new ItemStack(i)).ToArray();

    // ---- The row and its chimney ----

    /// <summary>The furnace back to back with this one (sharing its west wall, built, facing the other way), or null.</summary>
    public BEReverberatory? Partner()
    {
        if (FurnaceRig is not { } rig)
            return null;
        var n = Side.Normal();
        var opposite = Sides.FromNormal(-n.X, -n.Z);
        return Api.World.BlockAccessor.GetBlockEntity(CellPos(rig.Partner)) is BEReverberatory { Built: true } other && other.Side == opposite
            ? other : null;
    }

    /// <summary>Its row: itself and the furnace back to back with it, one chimney on either outlet, the other capped.</summary>
    protected override BEFiredOreMachine? RowMemberAt(int k) => k == -1 ? Partner() : null;

    // ---- Fire ----

    public override string? CannotIgnite() =>
        !Built ? Lang.Get("seraphhorizons:reverberatory-error-notbuilt")
        : _lit ? Lang.Get("seraphhorizons:reverberatory-error-lit")
        : Bunker.Empty ? Lang.Get("seraphhorizons:reverberatory-error-nocoal")
        : null;

    protected override void Light(IPlayer? byPlayer)
    {
        _lit = true;
        var at = WorldPoint(new Float3(0.5f, 1.0625f, 0f));
        Api.World.PlaySoundAt(new AssetLocation("game", "sounds/torch-ignite"), at.X, at.Y, at.Z, byPlayer, true, 16, 0.6f);
        if (byPlayer != null)
            Api.World.Logger.Audit("{0} lit the reverberatory roaster at {1}.", byPlayer.PlayerName, Pos);
    }

    /// <summary>The fire goes out: its coal ran short.</summary>
    private void GoOut()
    {
        _lit = false;
        MarkDirty(true);
    }

    // ---- Interaction ----

    private bool IsAshMouth(BlockSelection sel) =>
        FurnaceRig is { } rig && sel.Position.Equals(Pos) && sel.Face.Code == Footprint.ToWorld(rig.AshSide, Side).Code();

    /// <summary>
    /// Once built: coal goes into the bunker; an iron plate caps the outlet, and an empty hand on the outlet's top takes
    /// it off; an empty hand on the ash mouth moves the damper (open, half, shut). Lighting is a firestarter's or a
    /// torch's (<see cref="Ignite"/>). Concentrate and native sulfur go into the hopper by the shared infeed; before it
    /// is built, the build's.
    /// </summary>
    protected override bool? InteractContent(IPlayer byPlayer, ItemSlot slot, BlockSelection blockSel)
    {
        if (!Built || FurnaceRig is not { } rig)
            return null;
        var stack = slot?.Itemstack;
        bool server = Api.Side == EnumAppSide.Server;
        if (stack == null)
        {
            if (Capped && IsOutletTop(blockSel))
            {
                Uncap(byPlayer);
                return true;
            }
            if (IsAshMouth(blockSel))
            {
                if (server)
                {
                    _damper = _damper switch { Damper.Open => Damper.Half, Damper.Half => Damper.Shut, _ => Damper.Open };
                    var at = WorldPoint(new Float3(0.5f, 0.2344f, 0f));
                    Api.World.PlaySoundAt(DamperSound, at.X, at.Y, at.Z, byPlayer);
                    MarkDirty(true);
                }
                return true;
            }
            return null;
        }
        string code = stack.Collectible.Code.ToString();
        if (!Capped && code == rig.CapCode && IsOutletTop(blockSel))
            return Cap(byPlayer, slot!);
        if (ReverberatoryRoasting.IsFuel(code))
        {
            int room = Bunker.Empty ? stack.Collectible.MaxStackSize
                : Bunker.Itemstack.Equals(Api.World, stack, GlobalConstants.IgnoredStackAttributes) ? Bunker.Itemstack.Collectible.MaxStackSize - Bunker.StackSize : 0;
            if (room <= 0)
            {
                if (server && byPlayer is IServerPlayer sp)
                    sp.SendIngameError("reverberatory-bunkerfull", Lang.Get("seraphhorizons:reverberatory-error-bunkerfull"));
                return true;
            }
            if (server)
            {
                var op = new ItemStackMoveOperation(Api.World, EnumMouseButton.Right, 0, EnumMergePriority.AutoMerge, Math.Min(room, stack.StackSize));
                slot!.TryPutInto(Bunker, ref op);
                slot.MarkDirty();
                Bunker.MarkDirty();
                MarkDirty(true);
            }
            return true;
        }
        return null;
    }

    public override WorldInteraction[] InteractionHelp(IPlayer forPlayer)
    {
        var help = base.InteractionHelp(forPlayer).ToList();
        if (!Built || FurnaceRig is not { } rig)
            return help.ToArray();
        var world = Api.World;
        ItemStack[] Of(IEnumerable<string> codes) => codes
            .Select(c => (CollectibleObject?)world.GetItem(new AssetLocation(c)) ?? world.GetBlock(new AssetLocation(c)))
            .OfType<CollectibleObject>().Where(c => c.Id > 0).Select(c => new ItemStack(c)).ToArray();
        string Key(string k) => "seraphhorizons:reverberatory-help-" + k;
        help.Add(new WorldInteraction { ActionLangCode = Key("coal"), MouseButton = EnumMouseButton.Right, Itemstacks = Of(ReverberatoryRoasting.Fuels) });
        if (!_lit)
            help.Add(new WorldInteraction
            {
                ActionLangCode = Key("light"), MouseButton = EnumMouseButton.Right,
                Itemstacks = FurnaceFiring.IgniteStacks(world.Api),
            });
        help.Add(new WorldInteraction { ActionLangCode = Key("damper"), MouseButton = EnumMouseButton.Right });
        help.Add(Capped
            ? new WorldInteraction { ActionLangCode = Key("uncap"), MouseButton = EnumMouseButton.Right }
            : new WorldInteraction { ActionLangCode = Key("cap"), MouseButton = EnumMouseButton.Right, Itemstacks = Of([rig.CapCode]) });
        return help.ToArray();
    }

    // ---- Drawing ----

    /// <summary>The stages as built; then by the furnace's state: the damper in its place once the doors are in, the bed
    /// with anything on the hearth, the fire while lit, the cap.</summary>
    public override bool ShowsPart(string? requires)
    {
        switch (requires)
        {
            case ReverberatoryRequires.Charge: return Built && !InfeedSlot.Empty;
            case ReverberatoryRequires.Fire: return Built && _lit;
            case ReverberatoryRequires.OutletCap: return Built && Capped;
        }
        if (requires != null && requires.StartsWith("damper", StringComparison.Ordinal))
            return base.ShowsPart(ReverberatoryRequires.Doors) && requires == ReverberatoryRequires.DamperPart(_damper);
        return base.ShowsPart(requires);
    }

    // ---- Client ----

    private readonly ParticleTimer _doorFume = new(0.75f);
    private int _lastBed = -1;

    /// <summary>The fire's (<see cref="BEFiredOreMachine"/>); while the rabbles roast the bed, yellowish sulfur smoke
    /// seeps out round a work door now and then; and as an item is roasted (the bed one less, lit) its hot calcine
    /// falls glowing at the discharge tunnel's mouth with a puff of the smoke.</summary>
    protected override void OnClientTick(float dt)
    {
        base.OnClientTick(dt);
        int bed = InfeedSlot.StackSize, before = _lastBed;
        _lastBed = bed;
        if (Api is not ICoreClientAPI capi || Rig is not { } rig || !Built)
            return;
        var outward = MachineParticles.WorldDirection(new Float3(1, 0, 0), Side);
        if (Running && _doorFume.Due(dt) && rig.Point(capi.World.Rand.Next(2) == 0 ? "workDoor1" : "workDoor2") is { } door)
            MachineParticles.Fume(MachineParticles.FumeSulfur).Toward(new Vec3f(outward.X * 0.1f, 0.15f, outward.Z * 0.1f), 0.05f)
                .Spawn(capi.World, WorldPoint(new Float3(door.X + 0.16f, door.Y, door.Z)), MachineParticles.Around(new Float3(0.02f, 0.15f, 0.2f), Side));
        if (_lit && before > 0 && bed < before && rig.Point("output") is { } mouth)
        {
            var at = WorldPoint(new Float3(mouth.X + 0.05f, mouth.Y, mouth.Z));
            (MachineParticles.Dust(MachineParticles.Rgb(255, 230, 90, 30)) with { Count = 4, MaxCount = 6, VertexFlags = 160 })
                .Toward(new Vec3f(outward.X * 0.4f, 0.1f, outward.Z * 0.4f), 0.2f)
                .Spawn(capi.World, at, MachineParticles.Around(0.1));
            MachineParticles.Fume(MachineParticles.FumeSulfur).Spawn(capi.World, at, MachineParticles.Around(0.1));
        }
    }

    // ---- Saving and syncing ----

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetInt("damper", (int)_damper);
        tree.SetBool("lit", _lit);
        tree.SetDouble("coalLeft", _coalLeft);
        tree.SetDouble("progress", _progress);
        var carry = new TreeAttribute();
        foreach (var (key, units) in _carry.Held)
            carry.SetDouble(key, units);
        tree["carry"] = carry;
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);
        _damper = (Damper)Math.Clamp(tree.GetInt("damper"), 0, 2);
        _lit = tree.GetBool("lit");
        _coalLeft = Math.Max(0, tree.GetDouble("coalLeft"));
        _progress = Math.Max(0, tree.GetDouble("progress"));
        if (tree["carry"] is ITreeAttribute carry)
            _carry.Restore(carry.Where(kv => kv.Value is DoubleAttribute).Select(kv => new KeyValuePair<string, double>(kv.Key, ((DoubleAttribute)kv.Value).value)));
    }

    // ---- Info ----

    protected override void AppendContents(StringBuilder dsc)
    {
        if (!Built || FurnaceRig is not { } rig)
        {
            base.AppendContents(dsc);
            return;
        }
        string L(string key, params object[] args) => Lang.Get("seraphhorizons:reverberatory-info-" + key, args);
        dsc.AppendLine(InfeedSlot.Empty ? L("bed-empty")
            : L("bed", InfeedSlot.StackSize, InfeedSlot.Itemstack.GetName(), (int)Math.Round(100 * JobFill)));
        dsc.AppendLine(Bunker.Empty ? L("coal-empty") : L("coal", Coal, Bunker.Itemstack.GetName()));
        dsc.AppendLine(_lit ? L("lit") : L("out"));
        dsc.AppendLine(L("damper-" + _damper.ToString().ToLowerInvariant()));
        foreach (var held in OutputSlots("output").Where(s => !s.Empty))
            dsc.AppendLine(Lang.Get("seraphhorizons:machine-info-held", held.StackSize, held.Itemstack!.GetName()));
        dsc.AppendLine(RowLine());
        if (_lit && Row.Works && _damper == Damper.Half)
            dsc.AppendLine(L("halfpace", (int)Math.Round(100 * Pace)));
    }
}
