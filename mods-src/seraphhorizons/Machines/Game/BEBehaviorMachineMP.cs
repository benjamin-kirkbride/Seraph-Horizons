using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent.Mechanics;

namespace SeraphHorizons.Mod.Machines;

/// <summary>What a machine's power cell asks of its owner (the controller or the machine's block
/// entity): the face its shaft comes in at and the load it puts on it
/// (<see cref="Core.MachineLoad.Resistance"/>).</summary>
public interface IMachinePower
{
    /// <summary>The world face the axle meets the power cell at.</summary>
    BlockFacing PowerFace { get; }

    /// <summary>The resistance on the shaft now.</summary>
    float Resistance { get; }
}

/// <summary>
/// The shared mechanical power consumer of the ore machines (#711), registered as
/// <c>seraphhorizons.MachineMP</c>: on the power cell's block entity, it finds its network only through
/// <see cref="IMachinePower.PowerFace"/> and puts <see cref="IMachinePower.Resistance"/> on it. The
/// power cell's block entity is the <see cref="IMachinePower"/>, or gives one as
/// <see cref="IMachinePowerCell.Power"/> (a ghost cell passing it on to its controller). A renderer
/// reads the shaft's angle as <c>Network.AngleRad * GearedRatio</c> and its speed as
/// <see cref="BEBehaviorMPBase.TrueSpeed"/>. Without an owner it is a token load facing up. The block
/// draws its own shape: nothing is handed to the mechanical power renderer
/// (<see cref="OnTesselation"/>).
/// </summary>
public class BEBehaviorMachineMP : BEBehaviorMPConsumer
{
    public const string ClassName = "seraphhorizons.MachineMP";

    public BEBehaviorMachineMP(BlockEntity blockentity) : base(blockentity)
    {
    }

    private IMachinePower? Owner => Blockentity as IMachinePower ?? (Blockentity as IMachinePowerCell)?.Power;

    public override void Initialize(ICoreAPI api, JsonObject properties)
    {
        OutFacingForNetworkDiscovery = Owner?.PowerFace ?? BlockFacing.UP;
        base.Initialize(api, properties);
    }

    /// <summary>The block's own shape is drawn. The game's consumer returns true here whenever it has a
    /// block, skipping that shape for the mechanical power renderer to draw <c>Shape</c> instead; with
    /// <c>mechPartShape: null</c> that is nothing, and a frame that is its own power cell (the shaking
    /// table, the amalgam pan) went unseen.</summary>
    public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tesselator) => false;

    public override float GetResistance() => Owner?.Resistance ?? Core.MachineLoad.Unassembled;

    /// <summary>The shaft's angle, radians, for a renderer: the network's times this node's gearing.</summary>
    public float ShaftAngle => (Network?.AngleRad ?? 0) * GearedRatio;
}

/// <summary>A ghost power cell's block entity: the machine whose load it carries.</summary>
public interface IMachinePowerCell
{
    IMachinePower? Power { get; }
}
