using SeraphHorizons.Mod.CylinderBorer.Core;
using SeraphHorizons.Mod.Machines.Core;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent.Mechanics;

namespace SeraphHorizons.Mod.CylinderBorer;

/// <summary>The ghost cell that takes the axle (<c>cylinderborer-ghostpower-{side}</c>, side being
/// the borer's): a mechanical power connector on the rig's power face only (native south, the drive
/// end's face, the vanilla axle coming in along the machine), turned to the borer's facing. The entry
/// shaft runs along native z, so the renderer reads its angle with
/// <see cref="MillMotion.NativeShaftAngle"/> about z.</summary>
public class BlockCylinderBorerGhostPower : BlockCylinderBorerGhost, IMechanicalPowerBlock
{
    /// <summary>The world face that takes the axle; south until the rig is known.</summary>
    public BlockFacing PowerFace { get; private set; } = BlockFacing.SOUTH;

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);
        PowerFace = PowerFaceFor(CylinderBorerSystem.Of(api).Rig, Variant["side"]);
    }

    public static BlockFacing PowerFaceFor(CylinderBorerRig? rig, string? side)
    {
        if (rig == null || !Sides.TryParse(side, out var facing))
            return BlockFacing.SOUTH;
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
