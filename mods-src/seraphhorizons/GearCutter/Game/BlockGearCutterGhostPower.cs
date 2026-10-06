using SeraphHorizons.Mod.GearCutter.Core;
using SeraphHorizons.Mod.Machines.Core;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent.Mechanics;

namespace SeraphHorizons.Mod.GearCutter;

/// <summary>The ghost cell that takes the axle (<c>gearcutter-ghostpower-{side}</c>, side being the
/// cutter's): a mechanical power connector on the rig's power face only (native west, the back of
/// the column), turned to the cutter's facing. The entry shaft runs along native x, so the renderer
/// reads its angle with <see cref="MillMotion.NativeShaftAngle"/> about x.</summary>
public class BlockGearCutterGhostPower : BlockGearCutterGhost, IMechanicalPowerBlock
{
    /// <summary>The world face that takes the axle; west until the rig is known.</summary>
    public BlockFacing PowerFace { get; private set; } = BlockFacing.WEST;

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);
        PowerFace = PowerFaceFor(GearCutterSystem.Of(api).Rig, Variant["side"]);
    }

    public static BlockFacing PowerFaceFor(GearCutterRig? rig, string? side)
    {
        if (rig == null || !Sides.TryParse(side, out var facing))
            return BlockFacing.WEST;
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
