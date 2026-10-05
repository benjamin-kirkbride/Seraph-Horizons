using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.Rosser.Core;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent.Mechanics;

namespace SeraphHorizons.Mod.Rosser;

/// <summary>The ghost cell that takes the axle (<c>rosser-ghostpower-{side}</c>, side being the
/// rosser's): a mechanical power connector on the rig's power face only, a side face at the ring
/// station (native north), turned to the rosser's facing. The input shaft runs along native z, so
/// the renderer reads its angle with <see cref="MillMotion.NativeShaftAngle"/> about z.</summary>
public class BlockRosserGhostPower : BlockRosserGhost, IMechanicalPowerBlock
{
    /// <summary>The world face that takes the axle; north until the rig is known.</summary>
    public BlockFacing PowerFace { get; private set; } = BlockFacing.NORTH;

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);
        PowerFace = PowerFaceFor(RosserSystem.Of(api).Rig, Variant["side"]);
    }

    public static BlockFacing PowerFaceFor(RosserRig? rig, string? side)
    {
        if (rig == null || !Sides.TryParse(side, out var facing))
            return BlockFacing.NORTH;
        return BlockFacing.FromCode(Footprint.ToWorld(rig.PowerFace, facing).Code());
    }

    public bool HasMechPowerConnectorAt(IWorldAccessor world, BlockPos pos, BlockFacing face, BlockMPBase forBlock) =>
        face == PowerFace;

    public MechanicalNetwork? GetNetwork(IWorldAccessor world, BlockPos pos) =>
        world.BlockAccessor.GetBlockEntity(pos)?.GetBehavior<BEBehaviorMPBase>()?.Network;

    public void DidConnectAt(IWorldAccessor world, BlockPos pos, BlockFacing face)
    {
    }
}
