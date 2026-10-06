using SeraphHorizons.Mod.BuckingSawmill;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent.Mechanics;

namespace SeraphHorizons.Mod.GearCutter;

/// <summary>The gear cutter's load on its shaft, on the power ghost: a consumer whose network is
/// found through the power face only, as the mill's (<see cref="BEBehaviorMillMP"/>). Resistance is
/// the configured <c>Resistance</c> once the cutter is complete, oiled or dry (#481: its oil wears
/// the cutter kit, never the load), a token 0.005 before.</summary>
public class BEBehaviorGearCutterMP : BEBehaviorMPConsumer
{
    public BEBehaviorGearCutterMP(BlockEntity blockentity) : base(blockentity)
    {
    }

    public override void Initialize(ICoreAPI api, JsonObject properties)
    {
        OutFacingForNetworkDiscovery = (Block as BlockGearCutterGhostPower)?.PowerFace
                                       ?? BlockGearCutterGhostPower.PowerFaceFor(GearCutterSystem.Of(api).Rig, Block.Variant["side"]);
        base.Initialize(api, properties);
    }

    public override float GetResistance() =>
        (Blockentity as BEGearCutterGhost)?.Cutter is { Complete: true }
            ? GearCutterSystem.Of(Api).Config.Resistance
            : BEBehaviorMillMP.IncompleteResistance;
}
