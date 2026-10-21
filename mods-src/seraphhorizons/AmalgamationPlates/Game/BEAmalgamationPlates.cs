using System.Globalization;
using System.Text;
using SeraphHorizons.Mod.AmalgamationPlates.Core;
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
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.AmalgamationPlates;

/// <summary>
/// The amalgamation plates (#917): a continuous ore machine on the shared code (<see cref="BEOreMachine"/>) with
/// no power: water and gravity. Crushed or ground ore comes down a vanilla chute into the distributor (the infeed,
/// the head cell's top) or in by hand; with a rock's water in the reservoir (5 L, from a ppex pipe on the head's
/// west face) it runs <c>machines.amalgamationplates.perHour</c> (8) an in-game hour (<see cref="WorkHours"/>)
/// over the plates (<see cref="PlateAmalgamation"/>): on gold and silver quartz the dressed plates catch 0.65 of
/// the catchable free metal as amalgam, which builds up on them (three states drawn) until scraped off by hand,
/// and the rock goes on amalgamated, by the outlet chute at the foot's end face for the concentrators, or
/// discarded beside it as sand in its rock (#694). The dressing is mercury poured on from a container: each
/// amalgam caught takes its mercury off it, and plates without it catch nothing. Nothing wears. While it runs,
/// the spray plays and the pulp leaves the distributor over the head of the plates, and drips off the bed's lip
/// into the trap at the foot.
/// </summary>
public class BEAmalgamationPlates : BEOreMachine
{
    public const string Output = "output";
    public const string MercuryCode = "em:mercuryportion";

    /// <summary>The rig's points: the distributor's holes, the spray's nozzles, the middle of the plates and the bed's
    /// foot, its lip, where the pulp falls into the trap.</summary>
    private const string FeedSpout = "feedSpout", Spray = "spray", AmalgamPoint = "amalgam", Lip = "lip";
    private const float SprayInterval = 0.25f, FeedInterval = 0.35f, LipInterval = 0.4f;
    // Across the plates' head, native: wide (x), hardly up or along.
    private static readonly Float3 HeadSpread = new(0.3f, 0.01f, 0.03f);
    private static readonly AssetLocation RunSound = new("game", "sounds/environment/smallsplash");
    private static readonly AssetLocation ScrapeSound = new("game", "sounds/block/sand");
    private static readonly AssetLocation PourSound = new("game", "sounds/effect/water-pour");

    private WorkHours _hours = WorkHours.None;
    private double _dressing;
    private readonly Dictionary<string, double> _amalgam = new(StringComparer.Ordinal);
    private int _amalgamState;
    private PlateVerdict? _last;
    private readonly ParticleTimer _spray = new(SprayInterval), _feed = new(FeedInterval), _lip = new(LipInterval);

    public override OreMachine? Kind => OreMachine.AmalgamationPlates;

    protected override IReadOnlyList<string> OutputAnchors => [Output];

    /// <summary>The outlet carries the plates' tailings on; with no chute there they are discarded as sand.</summary>
    protected override bool DiscardsAt(string anchor) => anchor == Output || base.DiscardsAt(anchor);

    /// <summary>The mercury on the plates, in portions.</summary>
    public double Dressing => _dressing;

    /// <summary>The amalgam on the plates, in metal units, by amalgam item code.</summary>
    public IReadOnlyDictionary<string, double> Amalgam => _amalgam;

    public double AmalgamUnits => _amalgam.Values.Sum();

    private AmalgamationPlatesConfig PlatesConfig => Api == null ? AmalgamationPlatesConfig.Defaults : AmalgamationPlatesSystem.Of(Api).Config;

    private OreRecovery Recovery => OreProcessingSystem.RecoveryFor(Api);

    private double DressingCapacity => PlateAmalgamation.DressingPortions(PlatesConfig);

    private double CapacityUnits => PlateAmalgamation.CapacityUnits(Recovery, PlatesConfig);

    public override bool Takes(ItemStack stack) => PlateAmalgamation.Takes(OreStreamAttributes.FormOf(stack.Collectible), OreStreamAttributes.Read(stack));

    /// <summary>The dressing is drawn while there is mercury or amalgam on the plates, one amalgam state at a time.</summary>
    public override bool ShowsPart(string? requires)
    {
        switch (requires)
        {
            case "dressing":
                return base.ShowsPart("plates") && (_dressing > 1e-9 || _amalgamState > 0);
            case "amalgam1":
            case "amalgam2":
            case "amalgam3":
                return base.ShowsPart("plates") && _amalgamState == requires[^1] - '0';
            default:
                return base.ShowsPart(requires);
        }
    }

    // ---- Running ----

    /// <summary>The in-game hours counted toward the next item.</summary>
    public override double? WorkProgress => _hours.Progress;

    protected override bool WorkUnpowered(float dt)
    {
        if (Spec is not { ItemsPerHour: > 0 } spec)
            return false;
        var calendar = Api.World.Calendar;
        double hours = dt * calendar.SpeedOfTime * calendar.CalendarSpeedMul / 3600.0;
        (_hours, int items) = _hours.Add(hours, spec.ItemsPerHour);
        bool changed = false;
        for (int i = 0; i < items; i++)
        {
            if (!WorkItem())
            {
                _hours = WorkHours.None;
                break;
            }
            changed = true;
        }
        return changed;
    }

    protected override IEnumerable<(string Anchor, ItemStack Stack)> Products(ItemStack fed)
    {
        if (OreStreamAttributes.Read(fed) is not { } stream)
        {
            yield return (Output, fed);
            yield break;
        }
        var pass = PlateAmalgamation.Pass(Recovery, stream, fed.StackSize, _dressing, AmalgamUnits, CapacityUnits);
        _last = pass.Verdict;
        if (pass.Verdict == PlateVerdict.Caught && pass.AmalgamCode != null)
        {
            _dressing = Math.Max(0, _dressing - pass.MercurySpent);
            _amalgam[pass.AmalgamCode] = _amalgam.GetValueOrDefault(pass.AmalgamCode) + pass.AmalgamUnits;
            _amalgamState = PlateAmalgamation.AmalgamState(AmalgamUnits, CapacityUnits);
        }
        var rest = fed.Clone();
        OreStreamAttributes.Write(rest, pass.Rest);
        yield return (Output, rest);
    }

    protected override void OnWorked(ItemStack fed)
    {
        var at = At(Spray);
        Api.World.PlaySoundAt(RunSound, at.X, at.Y, at.Z, null, true, 12, 0.3f);
    }

    /// <summary>The pulp's colour: the ore going over the plates'.</summary>
    private int Pulp() => MachineParticles.MaterialColour(InfeedSlot.Itemstack, 200, MachineParticles.Slurry, Api as ICoreClientAPI);

    /// <summary>A point of the rig in the world; the block's middle if the rig has none of that name.</summary>
    private Vec3d At(string point) => Rig?.Point(point) is { } p ? WorldPoint(p) : Pos.ToVec3d().Add(0.5, 0.5, 0.5);

    /// <summary>Nothing moves; while it runs, the spray falling over the head of the plates, the pulp (its ore's
    /// colour) leaving the distributor onto them and dripping off the lip into the trap.</summary>
    public override RigInput FrameInput(float dt, bool far)
    {
        var input = base.FrameInput(dt, far);
        if (far || !Running || Rig is not { } rig)
            return input;
        if (rig.Point(Spray) is { } spray && _spray.Due(dt))
            (MachineParticles.Splash() with { Count = 2, MaxCount = 4, MinVelocity = new Vec3f(-0.1f, -0.5f, -0.1f), MaxVelocity = new Vec3f(0.1f, -0.1f, 0.1f), Size = 0.12f })
                .Spawn(Api.World, WorldPoint(spray), MachineParticles.Around(HeadSpread, Side));
        if (rig.Point(FeedSpout) is { } feed && _feed.Due(dt))
            (MachineParticles.SlurryDrops(Pulp()) with { Count = 1, MaxCount = 2 })
                .Spawn(Api.World, WorldPoint(feed), MachineParticles.Around(HeadSpread, Side));
        if (rig.Point(Lip) is { } lip && _lip.Due(dt))
            (MachineParticles.Drip(Pulp()) with { Count = 2 })
                .Spawn(Api.World, WorldPoint(lip), MachineParticles.Around(new Float3(0.3f, 0.01f, 0.01f), Side));
        return input;
    }

    protected override ItemStack[] FeedStacks(IWorldAccessor world) =>
        new[] { "quartz_nativegold", "quartz_nativesilver" }
            .SelectMany(ore => OreProducts.Grades.Select(g => world.GetItem(new AssetLocation(OreProducts.Domain, $"crushedore-{ore}-{g}"))))
            .OfType<Item>().Where(i => i.Id > 0).Take(8).Select(i => new ItemStack(i)).ToArray();

    // ---- Interaction ----

    /// <summary>
    /// A container of mercury dresses fitted plates (up to <see cref="AmalgamationPlatesConfig.DressingLitres"/>);
    /// an empty hand scrapes the whole amalgam items off them, the rest left for the next. Anything else goes on
    /// to the build and the infeed.
    /// </summary>
    protected override bool? InteractContent(IPlayer byPlayer, ItemSlot slot, BlockSelection blockSel)
    {
        var held = slot?.Itemstack;
        if (held?.Collectible is ILiquidSource source && source.GetContent(held) is { } content)
        {
            string mercury = Api.Side == EnumAppSide.Server ? Recovery.Retort.Mercury ?? MercuryCode : MercuryCode;
            if (content.Collectible?.Code?.ToString() != mercury)
                return null;
            return Api.Side != EnumAppSide.Server || Dress(slot!, byPlayer);
        }
        if (held == null && !byPlayer.Entity.Controls.ShiftKey && (_amalgamState > 0 || AmalgamUnits > 0))
            return Api.Side != EnumAppSide.Server || Scrape(byPlayer);
        return null;
    }

    private bool Dress(ItemSlot slot, IPlayer byPlayer)
    {
        if (!base.ShowsPart("plates"))
            return Refuse(byPlayer, "noplates");
        int room = (int)Math.Floor(DressingCapacity - _dressing + 1e-6);
        if (room <= 0)
            return Refuse(byPlayer, "dressed");
        var held = slot.Itemstack!;
        var source = (ILiquidSource)held.Collectible;
        int fit = Math.Min(room, source.GetContent(held)!.StackSize);
        int taken = held.StackSize > 1 && source is BlockLiquidContainerBase container
            ? container.SplitStackAndPerformAction(byPlayer.Entity, slot, one => container.TryTakeContent(one, fit)?.StackSize ?? 0)
            : source.TryTakeContent(held, fit)?.StackSize ?? 0;
        slot.MarkDirty();
        if (taken <= 0)
            return true;
        _dressing = Math.Min(DressingCapacity, _dressing + taken);
        if (_last is PlateVerdict.Undressed)
            _last = null;
        Api.World.PlaySoundAt(PourSound, Pos, -0.25, byPlayer);
        MarkDirty(true);
        return true;
    }

    private bool Scrape(IPlayer byPlayer)
    {
        double per = Recovery.ConcentrateUnits;
        bool any = false;
        foreach (var code in _amalgam.Keys.ToList())
        {
            var (items, left) = PlateAmalgamation.Scrape(_amalgam[code], per);
            if (items <= 0)
                continue;
            if (Api.World.GetItem(new AssetLocation(code)) is not { Id: > 0 } item)
            {
                Api.World.Logger.Warning("[seraphhorizons] Amalgamation plates: {0} is not in the game; the amalgam stays on the plates", code);
                continue;
            }
            var stack = new ItemStack(item, items);
            // what the player has no room for drops beside the plates
            if (!byPlayer.InventoryManager.TryGiveItemstack(stack, true) || stack.StackSize > 0)
                Api.World.SpawnItemEntity(stack, At(AmalgamPoint));
            if (left > 1e-9) _amalgam[code] = left;
            else _amalgam.Remove(code);
            any = true;
            Api.World.Logger.Audit("{0} scraped {1}x{2} off the amalgamation plates at {3}.", byPlayer.PlayerName, items, code, Pos);
        }
        if (!any)
            return Refuse(byPlayer, "nothingtoscrape");
        _amalgamState = PlateAmalgamation.AmalgamState(AmalgamUnits, CapacityUnits);
        if (_last is PlateVerdict.Full)
            _last = null;
        var at = At(AmalgamPoint);
        Api.World.PlaySoundAt(ScrapeSound, at.X, at.Y, at.Z, byPlayer, true, 12, 0.6f);
        MarkDirty(true);
        return true;
    }

    private static bool Refuse(IPlayer byPlayer, string why)
    {
        if (byPlayer is IServerPlayer sp)
            sp.SendIngameError("amalgamationplates-" + why, Lang.Get("seraphhorizons:amalgamationplates-error-" + why));
        return true;
    }

    public override WorldInteraction[] InteractionHelp(IPlayer forPlayer)
    {
        var help = base.InteractionHelp(forPlayer).ToList();
        if (Assembly?.Shows("plates") == true)
        {
            if (Api.World.GetBlock(new AssetLocation("game:woodbucket")) is { Id: > 0 } bucket)
                help.Add(new WorldInteraction { ActionLangCode = "seraphhorizons:amalgamationplates-help-dress", MouseButton = EnumMouseButton.Right, Itemstacks = [new ItemStack(bucket)] });
            if (_amalgamState > 0)
                help.Add(new WorldInteraction { ActionLangCode = "seraphhorizons:amalgamationplates-help-scrape", MouseButton = EnumMouseButton.Right, RequireFreeHand = true });
        }
        return help.ToArray();
    }

    // ---- Breaking ----

    /// <summary>Broken, the plates give the whole amalgam items on them; their mercury is lost.</summary>
    public override void OnBlockRemoved()
    {
        if (Api?.Side == EnumAppSide.Server)
            foreach (var (code, units) in _amalgam)
            {
                var (items, _) = PlateAmalgamation.Scrape(units, Recovery.ConcentrateUnits);
                if (items > 0 && Api.World.GetItem(new AssetLocation(code)) is { Id: > 0 } item)
                    Api.World.SpawnItemEntity(new ItemStack(item, items), At(AmalgamPoint));
            }
        base.OnBlockRemoved();
    }

    // ---- Info ----

    protected override void AppendContents(StringBuilder dsc)
    {
        base.AppendContents(dsc);
        if (Assembly?.Shows("plates") != true)
            return;
        dsc.AppendLine(Lang.Get("seraphhorizons:amalgamationplates-info-dressing",
            Litres(_dressing / PlateAmalgamation.PortionsPerLitre), Litres(DressingCapacity / PlateAmalgamation.PortionsPerLitre)));
        if (AmalgamUnits > 1e-9)
            dsc.AppendLine(Lang.Get("seraphhorizons:amalgamationplates-info-amalgam", Litres(AmalgamUnits), Litres(CapacityUnits)));
        if (_dressing <= 1e-9)
            dsc.AppendLine(Lang.Get("seraphhorizons:amalgamationplates-info-undressed"));
        else if (_last == PlateVerdict.Undressed)
            dsc.AppendLine(Lang.Get("seraphhorizons:amalgamationplates-info-low"));
        if (_last == PlateVerdict.Full)
            dsc.AppendLine(Lang.Get("seraphhorizons:amalgamationplates-info-full"));
    }

    private static string Litres(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    // ---- Saving ----

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetDouble("hours", _hours.Items);
        tree.SetDouble("dressing", _dressing);
        var amalgam = new TreeAttribute();
        foreach (var (code, units) in _amalgam)
            amalgam.SetDouble(code, units);
        tree["amalgam"] = amalgam;
        tree.SetInt("amalgamState", _amalgamState);
        if (_last is { } last) tree.SetInt("last", (int)last);
        else tree.RemoveAttribute("last");
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);
        _hours = WorkHours.Restore(tree.GetDouble("hours"));
        double dressing = tree.GetDouble("dressing");
        _dressing = double.IsFinite(dressing) ? Math.Max(0, dressing) : 0;
        _amalgam.Clear();
        if (tree["amalgam"] is ITreeAttribute amalgam)
            foreach (var (code, value) in amalgam)
                if (value is DoubleAttribute d && double.IsFinite(d.value) && d.value > 1e-9)
                    _amalgam[code] = d.value;
        _amalgamState = Math.Clamp(tree.GetInt("amalgamState"), 0, 3);
        _last = tree.HasAttribute("last") ? (PlateVerdict)tree.GetInt("last") : null;
    }
}
