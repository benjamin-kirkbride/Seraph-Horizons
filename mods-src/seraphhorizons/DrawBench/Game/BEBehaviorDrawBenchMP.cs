using SeraphHorizons.Mod.BuckingSawmill;
using SeraphHorizons.Mod.Machines;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent.Mechanics;

namespace SeraphHorizons.Mod.DrawBench;

/// <summary>The draw bench's load on its shaft, on the power ghost: a consumer whose network is
/// found through the power face only, as the mill's (<see cref="BEBehaviorMillMP"/>). Resistance is
/// the configured resistance of the metal on the bench (lead's when empty) once the bench is
/// complete, times the dry multiplier while its oil tank is dry (<c>MachineOil</c>), as the mill's
/// and the rosser's; a token 0.005 before.</summary>
public class BEBehaviorDrawBenchMP : BEBehaviorMPConsumer
{
    public BEBehaviorDrawBenchMP(BlockEntity blockentity) : base(blockentity)
    {
    }

    public override void Initialize(ICoreAPI api, JsonObject properties)
    {
        OutFacingForNetworkDiscovery = (Block as BlockDrawBenchGhostPower)?.PowerFace
                                       ?? BlockDrawBenchGhostPower.PowerFaceFor(DrawBenchSystem.Of(api).Rig, Block.Variant["side"]);
        base.Initialize(api, properties);
    }

    public override float GetResistance()
    {
        if ((Blockentity as BEDrawBenchGhost)?.Bench is not { Complete: true } bench)
            return BEBehaviorMillMP.IncompleteResistance;
        float resistance = DrawBenchSystem.Of(Api).Config.Resistance(bench.Job.Class);
        return bench.Oiling is { } oil ? Oil.Asked(oil, resistance, bench) : resistance;
    }
}
