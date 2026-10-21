using SeraphHorizons.Mod.BallMill.Core;
using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.Ore.Core;
using SeraphHorizons.Mod.Ore.Processing;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SeraphHorizons.Mod.BallMill;

/// <summary>
/// The ball mill (#915): a continuous ore machine on the shared code (<see cref="BEOreMachine"/>). Crushed ore,
/// and the shaking table's middlings merged into the same chute line, come in by a chute into the feed box (the
/// infeed, high on the west end) or by hand; each item grinds to one ground ore of its grade
/// (<see cref="BallMillGrind"/>: ground and screened, its locked shares freed, nothing lost) at
/// <c>machines.ballmill.perHour</c> (6 an hour at speed 0.6), 25 L of water a rock from its reservoir, into the
/// output chute low on its back. Its load is steady (1.1 once built, working or idle). No part wears.
/// While it grinds, the pulp slops out of the output chute's mouth, coloured by the ore going in.
/// </summary>
public class BEBallMill : BEOreMachine
{
    private static readonly AssetLocation GrindSound = new("game", "sounds/block/rock-hit-pickaxe");
    private const float PulpInterval = 0.3f;

    private readonly ParticleTimer _pulp = new(PulpInterval);

    public override OreMachine? Kind => OreMachine.BallMill;

    /// <summary>Oiled, its job a rock ground (<see cref="BEOreMachine.WorkItem"/>).</summary>
    protected override OilMachine? OilKind => OilMachine.BallMill;

    /// <summary>Crushed or ground ore whose ground ore item is in the game.</summary>
    public override bool Takes(ItemStack stack)
    {
        var stream = OreStreamAttributes.Read(stack);
        return BallMillGrind.Takes(OreStreamAttributes.FormOf(stack.Collectible), stream)
               && (Api == null || Api.World.GetItem(new AssetLocation(OreProducts.GroundCode(stream!.Ore, stream.Grade))) is { Id: > 0 });
    }

    protected override IReadOnlyList<string> OutputAnchors => ["output"];

    protected override IEnumerable<(string Anchor, ItemStack Stack)> Products(ItemStack fed)
    {
        var product = BallMillGrind.Grind(OreProcessingSystem.RecoveryFor(Api), OreStreamAttributes.FormOf(fed.Collectible), OreStreamAttributes.Read(fed));
        if (product == null || Api.World.GetItem(new AssetLocation(product.Code)) is not { Id: > 0 } item)
            yield break;
        var ground = new ItemStack(item, Math.Max(1, fed.StackSize));
        OreStreamAttributes.Write(ground, product.Stream);
        yield return ("output", ground);
    }

    protected override void OnWorked(ItemStack fed)
    {
        var at = WorldPoint(new Float3(0.5f, 1.25f, 1f));
        Api.World.PlaySoundAt(GrindSound, at.X, at.Y, at.Z, null, true, 16, 0.4f);
    }

    /// <summary>The default clock's θ; while it grinds, the pulp slopping out at the rig's <c>output</c> point.</summary>
    public override RigInput FrameInput(float dt, bool far)
    {
        var input = base.FrameInput(dt, far);
        if (!far && Running && Rig?.Point("output") is { } output && _pulp.Due(dt))
        {
            int colour = MachineParticles.MaterialColour(InfeedSlot.Itemstack, 200, MachineParticles.Slurry, Api as ICoreClientAPI);
            // out of the chute's mouth (native south) and falling
            (MachineParticles.SlurryDrops(colour) with { Count = 1, MaxCount = 3 })
                .Toward(MachineParticles.WorldDirection(new Float3(0, -0.1f, 0.35f), Side), 0.08f)
                .Spawn(Api.World, WorldPoint(output), MachineParticles.Around(new Float3(0.08f, 0.02f, 0.02f), Side));
        }
        return input;
    }

    protected override ItemStack[] FeedStacks(IWorldAccessor world) =>
        world.SearchItems(new AssetLocation(OreProducts.Domain, "crushedore-*")).Take(6).Select(i => new ItemStack(i)).ToArray();
}
