using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent.Mechanics;

namespace BuckingSawmill;

/// <summary>The mill's load on its shaft, on the power ghost: a consumer whose network is found
/// through the power face only, as Immersive Woodworking's BEBehaviorSawmillMP does (without its
/// cap on the network's speed). Resistance is the configured <c>Resistance</c> once the mill is
/// assembled, a token 0.005 before.</summary>
public class BEBehaviorMillMP : BEBehaviorMPConsumer
{
    public const float IncompleteResistance = 0.005f;

    public BEBehaviorMillMP(BlockEntity blockentity) : base(blockentity)
    {
    }

    public override void Initialize(ICoreAPI api, JsonObject properties)
    {
        OutFacingForNetworkDiscovery = (Block as BlockMillGhostPower)?.PowerFace
                                       ?? BlockMillGhostPower.PowerFaceFor(BuckingSawmillSystem.Of(api).Rig, Block.Variant["side"]);
        base.Initialize(api, properties);
    }

    public override float GetResistance() =>
        (Blockentity as BEMillGhost)?.Mill is { Complete: true }
            ? BuckingSawmillSystem.Of(Api).Config.Resistance
            : IncompleteResistance;
}
