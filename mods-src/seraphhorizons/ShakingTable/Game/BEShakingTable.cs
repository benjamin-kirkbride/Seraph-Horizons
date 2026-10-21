using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.Ore;
using SeraphHorizons.Mod.Ore.Core;
using SeraphHorizons.Mod.Ore.Processing;
using SeraphHorizons.Mod.ShakingTable.Core;
using SeraphHorizons.Mod.Sluice;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace SeraphHorizons.Mod.ShakingTable;

/// <summary>
/// The shaking table (#921): the third concentrator, a continuous ore machine on the shared code
/// (<see cref="BEOreMachine"/>), its axle at the controller. Crushed or ground ore (and rich gravel and bog iron,
/// as the sluice and jig take them), fresh or the jig's tailings, comes in by a vanilla chute onto the feed box
/// (the infeed, the top of the head end's middle cell) or by hand; with an item's water in its reservoir (20 L a
/// rock from a ppex pipe down onto the water line) it washes <c>machines.table.perHour</c> (7 an hour at shaft
/// speed 0.6, in proportion to speed), each through <see cref="ShakingTabling"/> at r = 0.70. Three outlets: the
/// concentrate off the far end, whole items carried over from rock to rock (<see cref="UnitCarry"/>); the
/// middlings off the far end's lower corner, the fed item carrying its locked units, for the ball mill; the
/// tailings off the low side, the fed item carrying the rest, on to the vanner or discarded beside it as sand in
/// its rock (#694). Its load is steady. Nothing in it wears out.
/// </summary>
public class BEShakingTable : BEOreMachine
{
    private static readonly AssetLocation WashSound = new("game", "sounds/block/sand");

    private const float FlowInterval = 0.25f;

    private readonly UnitCarry _carry = new();
    private readonly ParticleTimer _flow = new(FlowInterval);
    private double _lastTravel;

    public override OreMachine? Kind => OreMachine.Table;

    /// <summary>Oiled, its job a rock washed (<see cref="BEOreMachine.WorkItem"/>).</summary>
    protected override OilMachine? OilKind => OilMachine.ShakingTable;

    protected override IReadOnlyList<string> OutputAnchors =>
        [ShakingTabling.Concentrate, ShakingTabling.Middlings, ShakingTabling.Tailings];

    public override bool Takes(ItemStack stack) => ShakingTableSystem.TakesFeed(stack);

    protected override IEnumerable<(string Anchor, ItemStack Stack)> Products(ItemStack fed)
    {
        if (SluiceSystem.StreamOf(fed) is not { } stream)
        {
            yield return (ShakingTabling.Tailings, fed);
            yield break;
        }
        string code = fed.Collectible.Code.ToString();
        var outlets = ShakingTabling.Wash(OreProcessingSystem.RecoveryFor(Api), stream, () => PlacerTable.For(Api, code));
        foreach (var product in outlets.Concentrate)
        {
            int n = _carry.Add(product.Code, product.Units * fed.StackSize, product.UnitsPerItem);
            if (n > 0 && ProductStack(product.Code, n) is { } stack)
            {
                OreStreamAttributes.MarkWhole(stack, outlets.Whole);
                yield return (ShakingTabling.Concentrate, stack);
            }
        }
        if (outlets.Middlings is { } middlings)
        {
            var m = fed.Clone();
            OreStreamAttributes.Write(m, middlings);
            yield return (ShakingTabling.Middlings, m);
        }
        var tailings = fed.Clone();
        OreStreamAttributes.Write(tailings, outlets.Tailings);
        yield return (ShakingTabling.Tailings, tailings);
    }

    /// <summary>A product's stack: the item or block of its code.</summary>
    private ItemStack? ProductStack(string code, int count)
    {
        var loc = new AssetLocation(code);
        CollectibleObject? c = (CollectibleObject?)Api.World.GetItem(loc) ?? Api.World.GetBlock(loc);
        if (c is not { Id: > 0, IsMissing: false })
        {
            Api.World.Logger.Warning("[seraphhorizons] Shaking table: {0} is not in the game; {1} of it are lost", code, count);
            return null;
        }
        return new ItemStack(c, count);
    }

    /// <summary>The wash, over the middle of the deck (the rig's <c>deck</c>).</summary>
    protected override void OnWorked(ItemStack fed)
    {
        if (Rig?.Point("deck") is not { } deck)
            return;
        var at = WorldPoint(deck);
        Api.World.PlaySoundAt(WashSound, at.X, at.Y, at.Z, null, true, 12, 0.35f);
    }

    // ---- The renderer's ----

    /// <summary>The default clock; while it washes a rock, the pulp spills over the feed box's weir and a sheet of wash
    /// water runs off the low side (steady), and at each bump (<see cref="ShakingTabling.Bumped"/>, the end of the push)
    /// water and grains splash off the far end's lip; pulp and grains are the rock's colour.</summary>
    public override RigInput FrameInput(float dt, bool far)
    {
        var input = base.FrameInput(dt, far);
        bool bumped = ShakingTabling.Bumped(_lastTravel, input.Psi);
        _lastTravel = input.Psi;
        if (far || !Running || Rig is not { } rig)
            return input;
        var capi = Api as ICoreClientAPI;
        if (_flow.Due(dt))
        {
            if (rig.Point("feed") is { } feed)
                MachineParticles.SlurryDrops(MachineParticles.MaterialColour(InfeedSlot.Itemstack, 200, MachineParticles.Slurry, capi))
                    .Toward(MachineParticles.WorldDirection(new Float3(0.2f, -0.1f, 0), Side), 0.05f)
                    .Spawn(Api.World, WorldPoint(feed), MachineParticles.Around(new Float3(0.02f, 0.02f, 0.25f), Side));
            if (rig.Point("tailings") is { } tailings)
                (MachineParticles.Drip(MachineParticles.Water) with { Count = 2, Size = 0.15f })
                    .Toward(MachineParticles.WorldDirection(new Float3(0.15f, -0.3f, 0), Side), 0.05f)
                    .Spawn(Api.World, WorldPoint(tailings), MachineParticles.Around(new Float3(0.02f, 0.02f, 0.8f), Side));
        }
        if (bumped && rig.Point("concentrate") is { } lip)
        {
            var at = WorldPoint(lip);
            var across = MachineParticles.Around(new Float3(0.4f, 0.02f, 0.03f), Side);
            MachineParticles.Splash().Toward(MachineParticles.WorldDirection(new Float3(0, 0.25f, 0.3f), Side), 0.15f)
                .Spawn(Api.World, at, across);
            (MachineParticles.Dust(MachineParticles.MaterialColour(InfeedSlot.Itemstack, 230, MachineParticles.Slurry, capi)) with { Count = 1, MaxCount = 2 })
                .Toward(MachineParticles.WorldDirection(new Float3(0, 0.1f, 0.3f), Side), 0.1f)
                .Spawn(Api.World, at, across);
        }
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
