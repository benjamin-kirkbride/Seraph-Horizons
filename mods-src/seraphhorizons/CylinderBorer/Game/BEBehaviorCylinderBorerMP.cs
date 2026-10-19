using SeraphHorizons.Mod.BuckingSawmill;
using SeraphHorizons.Mod.Machines;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent.Mechanics;

namespace SeraphHorizons.Mod.CylinderBorer;

/// <summary>The cylinder borer's load on its shaft, on the power ghost: a consumer whose network is
/// found through the power face only, as the mill's (<see cref="Machines.BEBehaviorMachineMP"/>). Resistance is
/// the configured resistance once the borer is complete, oiled or dry: as the gear cutter's, the
/// borer's oil spares its cutter head, never its load. A token 0.005 before.</summary>
public class BEBehaviorCylinderBorerMP : BEBehaviorMPConsumer
{
    public BEBehaviorCylinderBorerMP(BlockEntity blockentity) : base(blockentity)
    {
    }

    public override void Initialize(ICoreAPI api, JsonObject properties)
    {
        OutFacingForNetworkDiscovery = (Block as BlockCylinderBorerGhostPower)?.PowerFace
                                       ?? BlockCylinderBorerGhostPower.PowerFaceFor(CylinderBorerSystem.Of(api).Rig, Block.Variant["side"]);
        base.Initialize(api, properties);
    }

    public override float GetResistance()
    {
        if ((Blockentity as BECylinderBorerGhost)?.Borer is not { Complete: true } borer)
            return Machines.Core.MachineLoad.Unassembled;
        float resistance = CylinderBorerSystem.Of(Api).Config.Resistance;
        // remembered for the block info's load line, never multiplied
        return borer.Oiling is { } oil ? Oil.Asked(oil, resistance, borer, dryLoad: false) : resistance;
    }
}
