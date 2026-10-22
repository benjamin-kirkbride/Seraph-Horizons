using System.Globalization;
using System.Text;
using SeraphHorizons.Mod.Arrastra.Core;
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

namespace SeraphHorizons.Mod.Arrastra;

/// <summary>
/// The arrastra's controller (#732; README "Arrastra"), on the shared ore machine (<see cref="BEOreMachine"/>:
/// the build, the power cell's load, the shaft's turns, the reservoir). A batch machine worked by hand
/// (#711 rule 2): no anchor but the axle and the draw gate, nothing automated feeds it or takes from it.
/// <list type="bullet">
/// <item>The pit is the infeed slot: the charge of crushed ore, one ore and grade, up to the config's
/// <c>charge</c> (2), tipped in a rock a right-click; once ground, the same slot holds the ground ore.</item>
/// <item>The water is the shared reservoir (20 L, a charge's worth), filled from a bucket by right-click
/// (the rocker's <see cref="Rocker.RockerWater"/>; no pipe). The charge grinds only with its water in the pit (10 L a rock),
/// spent when it is ground.</item>
/// <item>Grinding counts shaft turns (<see cref="ArrastraCharge"/>): a full charge in the config's
/// <c>chargeHours</c> (7.5) at the reference speed, a part charge its share; then every rock becomes its
/// ground ore at once, its stream lifted by <see cref="OreState.Ground"/> (#765: poor ore's × 0.4), no
/// metal lost.</item>
/// <item>The draw: a right-click with an empty hand gives the player what is in the pit (a charge not yet
/// ground comes back crushed, its turns lost).</item>
/// </list>
/// The charge and the paste are drawn by the renderer as the rig's <c>charge</c> and <c>paste</c> parts.
/// </summary>
public class BEArrastra : BEOreMachine
{
    public const string ClassName = "seraphhorizons.Arrastra";
    private static readonly AssetLocation TipSound = new("game", "sounds/block/gravel");
    private static readonly AssetLocation PourSound = new("game", "sounds/effect/water-pour");
    private static readonly AssetLocation GroundSound = new("game", "sounds/block/sand");

    private bool _ground;
    private double _grind;
    private int _shownPercent = -1;

    public override OreMachine? Kind => OreMachine.Arrastra;

    /// <summary>Oiled, its job a rock ground, drained a charge at a time as it is ground.</summary>
    protected override OilMachine? OilKind => OilMachine.Arrastra;

    /// <summary>Nothing automated feeds it: the charge is tipped in by hand (<see cref="InteractContent"/>).</summary>
    public override bool Takes(ItemStack stack) => false;

    protected override IEnumerable<(string Anchor, ItemStack Stack)> Products(ItemStack fed) => [];

    /// <summary>Emptied by hand, by the draw gate: no output anchor.</summary>
    protected override IReadOnlyList<string> OutputAnchors => [];

    /// <summary>Rock a charge holds (the config's <c>charge</c>).</summary>
    public int Capacity => Spec is { ChargeItems: > 0 } spec ? spec.ChargeItems : 2;

    public bool IsGround => _ground && !InfeedSlot.Empty;

    /// <summary>The pit as the batch's rules see it.</summary>
    public ArrastraCharge Charge
    {
        get
        {
            var stack = InfeedSlot.Itemstack;
            var rocks = stack == null ? [] : Enumerable.Repeat(stack.Collectible.Code.ToString(), stack.StackSize);
            return ArrastraCharge.Restore(Capacity, rocks, _ground, _grind, out _);
        }
    }

    public override bool HasWork => !InfeedSlot.Empty && !_ground;

    public override bool OutputRoom => true;

    /// <summary>The charge's turns ground, of a charge's (<see cref="ArrastraCharge.Progress"/>).</summary>
    public override double? WorkProgress => HasWork && Spec is { } spec ? Charge.Progress(spec.TurnsPerCharge) : null;

    /// <summary>It grinds a charge, not an item at a time (its own <see cref="Work"/>).</summary>
    protected override bool CountsTurns => false;

    /// <summary>The charge's water is in the pit; with nothing to grind it is not "dry".</summary>
    protected override bool Wet => !HasWork || Charge.CanGrind(Water.Litres, WaterPerItem);

    public override bool ShowsPart(string? requires) => requires switch
    {
        "charge" => Built && HasWork,
        "paste" => Built && IsGround,
        _ => base.ShowsPart(requires),
    };

    private const float GritInterval = 0.35f;
    private readonly ParticleTimer _grit = new(GritInterval);

    /// <summary>The default clock; while it grinds a wet charge, wet grit (the charge's colour) and flecks of water
    /// thrown up at one of the drag stones (the rig's <c>stone1</c>..<c>stone4</c>, carried round by the stones'
    /// part) now and then.</summary>
    public override RigInput FrameInput(float dt, bool far)
    {
        var input = base.FrameInput(dt, far);
        if (!far && Running && Rig is { } rig && _grit.Due(dt))
            SpawnGrit(rig, input);
        return input;
    }

    private void SpawnGrit(OreMachineRig rig, RigInput input)
    {
        int stones = rig.Parts.IndexOf("stones");
        if (stones < 0 || rig.Point($"stone{1 + Api.World.Rand.Next(4)}") is not { } stone)
            return;
        var at = WorldPoint(Mat4.Apply(rig.Parts.Matrices(input)[stones], stone));
        var round = MachineParticles.Around(0.12, 0.02, 0.12);
        (MachineParticles.SlurryDrops(MachineParticles.MaterialColour(InfeedSlot.Itemstack, 220, MachineParticles.Slurry, Api as ICoreClientAPI)) with
        {
            MinVelocity = new Vec3f(-0.2f, 0.15f, -0.2f), MaxVelocity = new Vec3f(0.2f, 0.4f, 0.2f), Life = 0.5f,
        }).Spawn(Api.World, at, round);
        if (Api.World.Rand.NextDouble() < 0.5)
            (MachineParticles.Splash(MachineParticles.Rgb(150, 135, 120, 100)) with { Count = 1, MaxCount = 2, Size = 0.15f }).Spawn(Api.World, at, round);
    }

    protected override bool Work(double radians)
    {
        if (Spec is not { TurnsPerCharge: > 0 } spec || !HasWork)
            return false;
        var charge = Charge;
        double water = charge.WaterNeeded(WaterPerItem);
        if (!charge.Work(radians, spec.TurnsPerCharge))
        {
            _grind = charge.Turns;
            int percent = (int)Math.Floor(100 * charge.Progress(spec.TurnsPerCharge));
            if (percent == _shownPercent)
                return false;
            _shownPercent = percent;
            return true;
        }
        SpendWater(water);
        DrainOil(InfeedSlot.StackSize);
        GrindCharge();
        _grind = 0;
        _shownPercent = -1;
        Api.World.PlaySoundAt(GroundSound, Pos.X + 0.5, Pos.Y + 0.5, Pos.Z + 0.5, null, true, 16, 0.8f);
        return true;
    }

    /// <summary>Turns the pit's crushed ore into its ground ore, one for one, keeping its stream (units,
    /// host rock, what was done to it) and lifting it by <see cref="OreState.Ground"/>.</summary>
    private void GrindCharge()
    {
        var crushed = InfeedSlot.Itemstack;
        _ground = true;
        if (crushed == null || ArrastraCharge.GroundCode(crushed.Collectible.Code.ToString()) is not { } code
            || Api.World.GetItem(new AssetLocation(code)) is not { Id: > 0, IsMissing: false } item)
            return;
        var ground = new ItemStack(item, crushed.StackSize) { Attributes = crushed.Attributes.Clone() };
        if (OreStreamAttributes.Read(crushed) is { } stream)
            OreStreamAttributes.Write(ground, OreProcessingSystem.RecoveryFor(Api).Lift(stream, OreState.Ground));
        InfeedSlot.Itemstack = ground;
        InfeedSlot.MarkDirty();
    }

    // ---- The hand's work: tipping in, watering, drawing out ----

    protected override bool? InteractContent(IPlayer byPlayer, ItemSlot slot, BlockSelection blockSel)
    {
        if (!Built)
            return null;
        var held = slot?.Itemstack;
        if (held == null)
            return InfeedSlot.Empty ? null : Draw(byPlayer);
        if (Rocker.RockerWater.Litres(held) > 0)
            return Pour(byPlayer, slot!);
        if (ArrastraCharge.IsCrushed(held.Collectible.Code.ToString()))
            return Tip(byPlayer, slot!);
        return null;
    }

    private bool Tip(IPlayer byPlayer, ItemSlot slot)
    {
        var held = slot.Itemstack;
        var verdict = Charge.CanLoad(held.Collectible.Code.ToString());
        if (verdict == LoadVerdict.Loads && !InfeedSlot.Empty
            && !InfeedSlot.Itemstack.Equals(Api.World, held, GlobalConstants.IgnoredStackAttributes))
            verdict = LoadVerdict.Mixed;
        if (Api.Side != EnumAppSide.Server)
            return true;
        if (verdict != LoadVerdict.Loads)
            return Error(byPlayer, "seraphhorizons:arrastra-error-" + verdict.ToString().ToLowerInvariant(), Capacity);
        var one = slot.TakeOut(1);
        slot.MarkDirty();
        if (InfeedSlot.Empty)
            InfeedSlot.Itemstack = one;
        else
            InfeedSlot.Itemstack!.StackSize += one.StackSize;
        InfeedSlot.MarkDirty();
        Api.World.PlaySoundAt(TipSound, Pos.X + 0.5, Pos.Y + 0.5, Pos.Z + 0.5, byPlayer, true, 16, 0.8f);
        MarkDirty(true);
        return true;
    }

    private bool Pour(IPlayer byPlayer, ItemSlot slot)
    {
        if (Api.Side != EnumAppSide.Server)
            return true;
        double capacity = ReservoirCapacity;
        var verdict = Charge.CanWater(Water.Litres, capacity);
        if (verdict != WaterVerdict.Pours)
            return Error(byPlayer, verdict == WaterVerdict.Full ? "seraphhorizons:arrastra-error-waterfull" : "seraphhorizons:arrastra-error-ground");
        double perLitre = Rocker.RockerWater.PortionsPerLitre(slot.Itemstack);
        double litres = Math.Min(Rocker.RockerWater.Litres(slot.Itemstack), capacity - Water.Litres);
        int portions = (int)Math.Floor(litres * perLitre + 1e-6);
        int taken = Rocker.RockerWater.Take(byPlayer, slot, portions);
        if (taken <= 0)
            return true;
        FillWater(taken / perLitre);
        Api.World.PlaySoundAt(PourSound, Pos.X + 0.5, Pos.Y + 0.5, Pos.Z + 0.5, byPlayer, true, 16, 0.8f);
        MarkDirty(true);
        return true;
    }

    private bool Draw(IPlayer byPlayer)
    {
        if (Api.Side != EnumAppSide.Server)
            return true;
        var drawn = InfeedSlot.TakeOutWhole();
        InfeedSlot.MarkDirty();
        _ground = false;
        _grind = 0;
        _shownPercent = -1;
        if (!byPlayer.InventoryManager.TryGiveItemstack(drawn, true))
            Api.World.SpawnItemEntity(drawn, Pos.ToVec3d().Add(0.5, 0.6, 0.5));
        Api.World.PlaySoundAt(TipSound, Pos.X + 0.5, Pos.Y + 0.5, Pos.Z + 0.5, byPlayer, true, 16, 0.8f);
        Api.World.Logger.Audit("{0} drew {1}x{2} out of the arrastra at {3}.", byPlayer.PlayerName, drawn.StackSize, drawn.Collectible.Code, Pos);
        MarkDirty(true);
        return true;
    }

    private bool Error(IPlayer byPlayer, string langCode, params object[] args)
    {
        if (byPlayer is IServerPlayer sp)
            sp.SendIngameError("arrastra", Lang.Get(langCode, args));
        return true;
    }

    public override WorldInteraction[] InteractionHelp(IPlayer forPlayer)
    {
        var help = base.InteractionHelp(forPlayer).ToList();
        if (!Built)
            return help.ToArray();
        var world = Api.World;
        if (!IsGround && InfeedSlot.StackSize < Capacity)
        {
            var crushed = InfeedSlot.Empty
                ? world.SearchItems(new AssetLocation("seraphhorizons:crushedore-*-poor")).Take(8).Select(i => new ItemStack(i)).ToArray()
                : [InfeedSlot.Itemstack.Clone()];
            if (crushed.Length > 0)
                help.Add(new WorldInteraction { ActionLangCode = "seraphhorizons:arrastra-help-tip", MouseButton = EnumMouseButton.Right, Itemstacks = crushed });
        }
        if (!IsGround && Water.Litres + 1e-9 < ReservoirCapacity && world.GetBlock(new AssetLocation("game:woodbucket")) is { Id: > 0 } bucket)
            help.Add(new WorldInteraction { ActionLangCode = "seraphhorizons:arrastra-help-water", MouseButton = EnumMouseButton.Right, Itemstacks = [new ItemStack(bucket)] });
        if (!InfeedSlot.Empty)
            help.Add(new WorldInteraction { ActionLangCode = "seraphhorizons:arrastra-help-draw", MouseButton = EnumMouseButton.Right });
        return help.ToArray();
    }

    // ---- Info and saving ----

    protected override void AppendContents(StringBuilder dsc)
    {
        if (!Built)
            return;
        if (InfeedSlot.Empty)
            dsc.AppendLine(Lang.Get("seraphhorizons:arrastra-info-empty", Capacity));
        else if (IsGround)
            dsc.AppendLine(Lang.Get("seraphhorizons:arrastra-info-ground", InfeedSlot.StackSize));
        else
        {
            double progress = Spec is { } spec ? Charge.Progress(spec.TurnsPerCharge) : 0;
            dsc.AppendLine(Lang.Get("seraphhorizons:arrastra-info-charge", InfeedSlot.StackSize, Capacity,
                Math.Floor(100 * progress).ToString(CultureInfo.InvariantCulture)));
            dsc.AppendLine(InfeedSlot.Itemstack.GetName());
        }
    }

    /// <summary>For a wrench: the shaft turns ground into the charge, of the turns it takes.</summary>
    protected override void AppendDetail(StringBuilder dsc)
    {
        if (Built && HasWork && Spec is { TurnsPerCharge: > 0 } spec && Charge is var charge)
            dsc.AppendLine(Lang.Get("seraphhorizons:arrastra-info-turns", charge.Turns.ToString("0.##", CultureInfo.InvariantCulture),
                ArrastraCharge.TurnsFor(charge.Count, Capacity, spec.TurnsPerCharge).ToString("0.##", CultureInfo.InvariantCulture)));
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetBool("ground", _ground);
        tree.SetDouble("grind", _grind);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);
        _ground = tree.GetBool("ground");
        double grind = tree.GetDouble("grind");
        _grind = double.IsFinite(grind) ? Math.Max(0, grind) : 0;
    }
}
