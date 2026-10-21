using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Ore.Core;
using SeraphHorizons.Mod.Ore.Processing;
using SeraphHorizons.Mod.StampBattery.Core;
using SeraphHorizons.Mod.Machines.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SeraphHorizons.Mod.StampBattery;

/// <summary>
/// The stamp battery's controller (#728): the shared ore machine (<see cref="BEOreMachine"/>: its build,
/// shoes, power, water, chute in and out and hand feeding) with the battery's job, <see cref="Stamping"/>:
/// raw and crystallised ore and crushed ore not yet classified in; one rock a work item, out by the
/// <c>output</c> anchor as classified crushed ore carrying its stream (<see cref="OreStreamAttributes"/>).
/// </summary>
public class BEStampBattery : BEOreMachine
{
    public const string OutputAnchor = "output";

    private static readonly AssetLocation StampSound = new("game", "sounds/block/rock-hit-pickaxe");

    public override OreMachine? Kind => OreMachine.StampBattery;

    /// <summary>Oiled, its job a rock stamped (<see cref="BEOreMachine.WorkItem"/>).</summary>
    protected override OilMachine? OilKind => OilMachine.StampBattery;

    protected override IReadOnlyList<string> OutputAnchors => [OutputAnchor];

    public override bool Takes(ItemStack stack) =>
        Stamping.FeedOf(stack.Collectible?.Code?.ToString(), OreStreamAttributes.StateOf(stack)) != StampFeed.None;

    protected override IEnumerable<(string Anchor, ItemStack Stack)> Products(ItemStack fed)
    {
        string code = fed.Collectible.Code.ToString();
        if (FeedStream(fed) is not { } feed || Stamping.Stamp(code, feed) is not { } product)
            yield break;
        if (Api.World.GetItem(new AssetLocation(product.Code)) is not { IsMissing: false } item)
        {
            Api.Logger.Warning("[seraphhorizons] Stamp battery: {0} is not in the game; {1} is lost", product.Code, code);
            yield break;
        }
        var stack = new ItemStack(item, product.Count);
        OreStreamAttributes.Write(stack, product.Stream);
        yield return (OutputAnchor, stack);
    }

    /// <summary>A stamp's drop on the die, at the mortar (the middle stamp's).</summary>
    protected override void OnWorked(ItemStack fed)
    {
        if (Rig?.Point("mortar3") is not { } mortar)
            return;
        var at = WorldPoint(mortar);
        Api.World.PlaySoundAt(StampSound, at.X, at.Y, at.Z, null, true, 16, 0.5f);
    }

    // ---- The renderer's ----

    private const float TrickleInterval = 0.3f;
    private readonly ParticleTimer _trickle = new(TrickleInterval);
    private double _lastTravel;

    /// <summary>The default clock; while it stamps a rock, each drop (<see cref="Stamping.Landed"/>, as the rig draws
    /// the stamps) throws up water and grit out of the mortar over that stamp's die, and the pulp trickles out of the
    /// screen, the grit and pulp coloured by the rock.</summary>
    public override RigInput FrameInput(float dt, bool far)
    {
        var input = base.FrameInput(dt, far);
        int landed = Stamping.Landed(_lastTravel, input.Psi);
        _lastTravel = input.Psi;
        if (far || !Running || Rig is not { } rig)
            return input;
        var capi = Api as ICoreClientAPI;
        if (landed > 0 && rig.Point($"mortar{landed}") is { } mortar)
        {
            var at = WorldPoint(mortar);
            var round = MachineParticles.Around(0.06, 0.02, 0.06);
            (MachineParticles.Splash() with { MinVelocity = new Vec3f(-0.25f, 0.4f, -0.25f), MaxVelocity = new Vec3f(0.25f, 1.0f, 0.25f) })
                .Spawn(Api.World, at, round);
            (MachineParticles.Dust(MachineParticles.MaterialColour(InfeedSlot.Itemstack, 220, MachineParticles.Slurry, capi)) with
            {
                MinVelocity = new Vec3f(-0.2f, 0.3f, -0.2f), MaxVelocity = new Vec3f(0.2f, 0.8f, 0.2f),
            }).Spawn(Api.World, at, round);
        }
        if (_trickle.Due(dt) && rig.Point("screen") is { } screen)
            MachineParticles.SlurryDrops(MachineParticles.MaterialColour(InfeedSlot.Itemstack, 200, MachineParticles.Slurry, capi))
                .Toward(MachineParticles.WorldDirection(new Float3(0, -0.2f, -0.15f), Side), 0.05f)
                .Spawn(Api.World, WorldPoint(screen), MachineParticles.Around(new Float3(0.7f, 0.1f, 0.02f), Side));
        return input;
    }

    /// <summary>The help's feed: a few raw ores.</summary>
    protected override ItemStack[] FeedStacks(IWorldAccessor world) =>
        world.SearchItems(new AssetLocation("game:ore-*")).Where(i => Stamping.RawParts(i.Code?.ToString()) != null).Take(6)
            .Select(i => new ItemStack(i)).ToArray();

    /// <summary>A fed rock's stream: <see cref="OreStreamAttributes.Read"/>, with crystallised ore's units
    /// twice its grade's (its item holds twice, and its code says only the grade) unless the stack says.</summary>
    public static OreStream? FeedStream(ItemStack fed)
    {
        if (OreStreamAttributes.Read(fed) is not { } stream)
            return null;
        bool crystal = fed.Collectible.FirstCodePart() == "crystalizedore";
        if (crystal && !fed.Attributes.HasAttribute(OreStreamAttributes.Units))
            stream = stream with { Units = 2 * (OreProducts.RawUnits(stream.Ore, stream.Grade) ?? 0) };
        return stream;
    }
}
