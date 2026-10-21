using SeraphHorizons.Mod.JawBreaker.Core;
using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.Ore.Core;
using SeraphHorizons.Mod.Ore.Processing;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SeraphHorizons.Mod.JawBreaker;

/// <summary>
/// The jaw breaker (#912): a continuous ore machine on the shared code (<see cref="BEOreMachine"/>). Raw ore
/// comes in by a chute onto the grizzly's head (the infeed, the top of its west front column) or by hand,
/// up to a charge (<see cref="JawBreakerConfig.ChargeRocks"/>); it breaks each into its crushed ore, one
/// for one, at <c>machines.jawbreaker.perHour</c> (24 an hour at speed 0.6), into the output chute low on
/// its front face. Its load follows the charge in its mouth (<see cref="JawBreaking.Fill"/>). The jaw
/// faces wear a use a rock and are renewed whole by right-click.
/// </summary>
public class BEJawBreaker : BEOreMachine
{
    private static readonly AssetLocation BiteSound = new("game", "sounds/block/rock-break-pickaxe");

    public override OreMachine? Kind => OreMachine.JawBreaker;

    /// <summary>Oiled, its job a rock broken (<see cref="BEOreMachine.WorkItem"/>).</summary>
    protected override OilMachine? OilKind => OilMachine.JawBreaker;

    private int ChargeRocks => Api == null ? JawBreakerConfig.Defaults.ChargeRocks : JawBreakerSystem.Of(Api).Config.ChargeRocks;

    public override bool Takes(ItemStack stack) =>
        stack.Collectible is ItemGradedOre ore && JawBreaking.Takes(ore.FirstCodePart(), ore.Variant["grade"])
        && (Api?.Side != EnumAppSide.Server || ore.CrushingProps?.CrushedStack?.ResolvedItemstack != null);

    public override int InfeedCapacity(ItemStack stack) => ChargeRocks;

    public override double JobFill => JawBreaking.Fill(InfeedSlot.StackSize, ChargeRocks);

    protected override IReadOnlyList<string> OutputAnchors => ["output"];

    protected override IEnumerable<(string Anchor, ItemStack Stack)> Products(ItemStack fed)
    {
        if (fed.Collectible.CrushingProps?.CrushedStack?.ResolvedItemstack is not { } crushedOf)
            yield break;
        var crushed = crushedOf.Clone();
        crushed.StackSize = Math.Max(1, crushed.StackSize) * Math.Max(1, fed.StackSize);
        if (OreStreamAttributes.Read(fed) is { } stream)
            OreStreamAttributes.Write(crushed, JawBreaking.Crush(stream));
        yield return ("output", crushed);
    }

    protected override void OnWorked(ItemStack fed)
    {
        if (Rig?.Point("jaws") is not { } jaws)
            return;
        var at = WorldPoint(jaws);
        Api.World.PlaySoundAt(BiteSound, at.X, at.Y, at.Z, null, true, 16, 0.5f);
    }

    // ---- The renderer's ----

    private double _lastTheta;

    /// <summary>The default clock; on each bite (<see cref="JawBreaking.Bit"/>) while it breaks a rock, rock dust and
    /// chips thrown up out of the jaws' mouth and dust falling from the discharge, coloured by the rock.</summary>
    public override RigInput FrameInput(float dt, bool far)
    {
        var input = base.FrameInput(dt, far);
        bool bit = JawBreaking.Bit(_lastTheta, input.Theta);
        _lastTheta = input.Theta;
        if (bit && !far && Running && Rig is { } rig)
            SpawnBite(rig);
        return input;
    }

    private void SpawnBite(OreMachineRig rig)
    {
        var rock = InfeedSlot.Itemstack;
        var capi = Api as ICoreClientAPI;
        int dust = MachineParticles.MaterialColour(rock, 200, MachineParticles.RockDust, capi);
        if (rig.Point("jaws") is { } jaws)
        {
            var at = WorldPoint(jaws);
            var across = MachineParticles.Around(new Float3(0.3f, 0.02f, 0.1f), Side);
            (MachineParticles.Dust(dust) with { Count = 3, MinVelocity = new Vec3f(-0.2f, 0.3f, -0.2f), MaxVelocity = new Vec3f(0.2f, 0.9f, 0.2f) })
                .Spawn(Api.World, at, across);
            (MachineParticles.Chips(MachineParticles.MaterialColour(rock, 255, MachineParticles.RockDust, capi)) with
            {
                Count = 1, MaxCount = 3, MinVelocity = new Vec3f(-0.6f, 0.6f, -0.6f), MaxVelocity = new Vec3f(0.6f, 1.4f, 0.6f),
            }).Spawn(Api.World, at, across);
        }
        if (rig.Point("discharge") is { } discharge)
            (MachineParticles.Dust(dust) with { MinVelocity = new Vec3f(-0.05f, -0.5f, -0.05f), MaxVelocity = new Vec3f(0.05f, -0.2f, 0.05f) })
                .Spawn(Api.World, WorldPoint(discharge), MachineParticles.Around(new Float3(0.25f, 0.02f, 0.04f), Side));
    }

    protected override ItemStack[] FeedStacks(IWorldAccessor world) =>
        world.SearchItems(new AssetLocation("game:ore-*")).Where(i => i is ItemGradedOre { Spalls: true }).Take(6)
            .Select(i => new ItemStack(i)).ToArray();
}
