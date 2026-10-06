using SeraphHorizons.Mod.BuckingSawmill;
using SeraphHorizons.Mod.Machines;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent.Mechanics;

namespace SeraphHorizons.Mod.Rosser;

/// <summary>The rosser's load on its shaft, on the power ghost: a consumer whose network is found
/// through the power face only, as the mill's (<see cref="BEBehaviorMillMP"/>). Resistance is the
/// configured <c>Resistance</c> once the rosser is assembled (times the dry multiplier while
/// its oil tank is dry, <c>MachineOil</c>), a token 0.005 before.</summary>
public class BEBehaviorRosserMP : BEBehaviorMPConsumer
{
    public BEBehaviorRosserMP(BlockEntity blockentity) : base(blockentity)
    {
    }

    public override void Initialize(ICoreAPI api, JsonObject properties)
    {
        OutFacingForNetworkDiscovery = (Block as BlockRosserGhostPower)?.PowerFace
                                       ?? BlockRosserGhostPower.PowerFaceFor(RosserSystem.Of(api).Rig, Block.Variant["side"]);
        base.Initialize(api, properties);
    }

    public override float GetResistance() =>
        (Blockentity as BERosserGhost)?.Rosser is { Complete: true } rosser
            ? rosser.Oiling is { } oil ? Oil.Asked(oil, RosserSystem.Of(Api).Config.Resistance, rosser) : RosserSystem.Of(Api).Config.Resistance
            : BEBehaviorMillMP.IncompleteResistance;
}
