using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.Ore;
using SeraphHorizons.Mod.Ore.Core;
using SeraphHorizons.Mod.Ore.Processing;
using SeraphHorizons.Mod.Sluice;
using SeraphHorizons.Mod.Vanner.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace SeraphHorizons.Mod.Vanner;

/// <summary>
/// The vanner (#923): the last of the concentrators, a continuous ore machine on the shared code
/// (<see cref="BEOreMachine"/>), driven by an axle onto its countershaft at the back. Crushed or ground ore
/// (the shaking table's tailings), and placer gravel or bog iron carried down the line, come in by a vanilla
/// chute into the feed box (the infeed, the top of the middle west cell) or by hand; with an item's water in
/// its reservoir (15 L a rock, a ppex pipe down onto the front east cell) it vans
/// <c>machines.vanner.perHour</c> (3) an in-game hour at speed 0.6, in proportion to speed, each through
/// <see cref="Vanning.Wash"/> at r = 0.80. The concentrate leaves by the chute under the head, whole items
/// carried over from rock to rock (<see cref="UnitCarry"/>; a whole-route concentrate marked so,
/// <see cref="OreStreamAttributes.MarkWhole"/>); the rock goes on as tailings, the same item carrying the
/// rest, by the chute off the foot, or is discarded beside it as sand in its rock (#694). The leather belt
/// wears a use a rock and is renewed by right-click.
/// </summary>
public class BEVanner : BEOreMachine
{
    public const string Concentrate = "concentrate";

    private static readonly AssetLocation WashSound = new("game", "sounds/block/sand");

    private const float TrickleInterval = 0.3f;

    private readonly UnitCarry _carry = new();
    private readonly ParticleTimer _trickle = new(TrickleInterval);
    private double _lastTheta;

    public override OreMachine? Kind => OreMachine.Vanner;

    /// <summary>Oiled, its job a rock vanned (<see cref="BEOreMachine.WorkItem"/>).</summary>
    protected override OilMachine? OilKind => OilMachine.Vanner;

    protected override IReadOnlyList<string> OutputAnchors => [Concentrate, Tailings];

    /// <summary>What the concentrators wash: crushed or ground ore, rich gravel and bog iron
    /// (<see cref="SluiceSystem.StreamOf"/>). Raw ore needs breaking first; roasted rock and concentrate are
    /// past the concentrators.</summary>
    public override bool Takes(ItemStack stack) => SluiceSystem.TakesFeed(stack);

    protected override IEnumerable<(string Anchor, ItemStack Stack)> Products(ItemStack fed)
    {
        if (SluiceSystem.StreamOf(fed) is not { } stream)
        {
            yield return (Tailings, fed);
            yield break;
        }
        string code = fed.Collectible.Code.ToString();
        var wash = Vanning.Wash(OreProcessingSystem.RecoveryFor(Api), stream, () => PlacerTable.For(Api, code));
        foreach (var product in wash.Products)
        {
            bool whole = wash.Whole;
            int n = _carry.Add(Vanning.CarryKey(product.Code, whole), product.Units * fed.StackSize, product.UnitsPerItem);
            if (n > 0 && ProductStack(product.Code, n) is { } stack)
            {
                OreStreamAttributes.MarkWhole(stack, whole);
                yield return (Concentrate, stack);
            }
        }
        var tailings = fed.Clone();
        OreStreamAttributes.Write(tailings, wash.Tailings);
        yield return (Tailings, tailings);
    }

    /// <summary>A product's stack: the item or block of its code.</summary>
    private ItemStack? ProductStack(string code, int count)
    {
        var loc = new AssetLocation(code);
        CollectibleObject? c = (CollectibleObject?)Api.World.GetItem(loc) ?? Api.World.GetBlock(loc);
        if (c is not { Id: > 0, IsMissing: false })
        {
            Api.World.Logger.Warning("[seraphhorizons] Vanner: {0} is not in the game; {1} of it are lost", code, count);
            return null;
        }
        return new ItemStack(c, count);
    }

    /// <summary>The wash, where the concentrate drops off the head into its box (the rig's <c>headBox</c>).</summary>
    protected override void OnWorked(ItemStack fed)
    {
        if (Rig?.Point("headBox") is not { } head)
            return;
        var at = WorldPoint(head);
        Api.World.PlaySoundAt(WashSound, at.X, at.Y, at.Z, null, true, 12, 0.3f);
    }

    // ---- The renderer's ----

    /// <summary>The default clock; while it vans a rock, a steady trickle of wash water and now and then a grain of
    /// concentrate (the rock's colour) off the head roller into its box (<c>headBox</c>), and at each end of the shake
    /// (<see cref="Vanning.ShakeTurned"/>) tailings slurry dripping off the foot roller (<c>foot</c>).</summary>
    public override RigInput FrameInput(float dt, bool far)
    {
        var input = base.FrameInput(dt, far);
        bool turned = Vanning.ShakeTurned(_lastTheta, input.Theta);
        _lastTheta = input.Theta;
        if (far || !Running || Rig is not { } rig)
            return input;
        var capi = Api as ICoreClientAPI;
        var across = MachineParticles.Around(new Float3(0.3f, 0.02f, 0.02f), Side);
        if (_trickle.Due(dt) && rig.Point("headBox") is { } head)
        {
            var at = WorldPoint(head);
            (MachineParticles.Drip(MachineParticles.Water) with { Count = 2 }).Spawn(Api.World, at, across);
            if (Api.World.Rand.NextDouble() < 0.4)
                MachineParticles.Drip(MachineParticles.MaterialColour(InfeedSlot.Itemstack, 230, MachineParticles.Slurry, capi))
                    .Spawn(Api.World, at, across);
        }
        if (turned && rig.Point("foot") is { } foot)
            MachineParticles.SlurryDrops(MachineParticles.MaterialColour(InfeedSlot.Itemstack, 200, MachineParticles.Slurry, capi))
                .Spawn(Api.World, WorldPoint(foot), across);
        return input;
    }

    protected override ItemStack[] FeedStacks(IWorldAccessor world) => SluiceSystem.FeedExamples(world);

    // ---- Saving ----

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        var carry = new TreeAttribute();
        foreach (var (key, units) in _carry.Held)
            carry.SetDouble(key, units);
        tree["carry"] = carry;
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);
        var held = new List<KeyValuePair<string, double>>();
        if (tree["carry"] is ITreeAttribute carry)
            foreach (var (key, value) in carry)
                if (value is DoubleAttribute d)
                    held.Add(new(key, d.value));
        _carry.Restore(held);
    }
}
