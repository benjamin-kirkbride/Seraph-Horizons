using SeraphHorizons.Mod.BuckingSawmill;
using SeraphHorizons.Mod.Machines;
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

    public override float GetResistance()
    {
        if ((Blockentity as BEGearCutterGhost)?.Cutter is not { Complete: true } cutter)
            return BEBehaviorMillMP.IncompleteResistance;
        float resistance = GearCutterSystem.Of(Api).Config.Resistance;
        // remembered for the block info's load line, never multiplied
        return cutter.Oiling is { } oil ? Oil.Asked(oil, resistance, cutter, dryLoad: false) : resistance;
    }
}
