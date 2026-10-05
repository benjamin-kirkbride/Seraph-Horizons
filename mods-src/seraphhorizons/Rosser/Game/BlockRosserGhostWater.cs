using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.Rosser.Core;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SeraphHorizons.Mod.Rosser;

/// <summary>The ghost cell a Pipes and Power Expanded pipe feeds the drip through
/// (<c>rosser-ghostwater-{side}</c>, side being the rosser's). Its water face (the rig's
/// <c>waterFace</c>, turned to the rosser's facing) carries a block attached to it: ppex keeps a
/// pipe's connector into a neighbour that does, and breaks a pipe with nothing to hold it, and the
/// ghosts are otherwise not solid. It holds no ppex type: the controller draws the water across
/// this face (<see cref="PpexWater"/>).</summary>
public class BlockRosserGhostWater : BlockRosserGhost
{
    /// <summary>The world face a pipe connects to; south until the rig is known.</summary>
    public BlockFacing WaterFace { get; private set; } = BlockFacing.SOUTH;

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);
        WaterFace = WaterFaceFor(RosserSystem.Of(api).Rig, Variant["side"]);
    }

    public static BlockFacing WaterFaceFor(RosserRig? rig, string? side)
    {
        if (rig == null || !Sides.TryParse(side, out var facing))
            return BlockFacing.SOUTH;
        return BlockFacing.FromCode(Footprint.ToWorld(rig.WaterFace, facing).Code());
    }

    public override bool CanAttachBlockAt(IBlockAccessor blockAccessor, Block block, BlockPos pos, BlockFacing blockFace, Cuboidi? attachmentArea = null) =>
        blockFace == WaterFace || base.CanAttachBlockAt(blockAccessor, block, pos, blockFace, attachmentArea);
}
