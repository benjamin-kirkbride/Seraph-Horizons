using SeraphHorizons.Mod.BuckingSawmill.Core;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent.Mechanics;

namespace SeraphHorizons.Mod.BuckingSawmill;

/// <summary>The ghost cell that takes the axle (<c>buckingmill-ghostpower-{side}</c>, side being
/// the mill's): a mechanical power connector on the rig's power face only, turned to the mill's
/// facing.</summary>
public class BlockMillGhostPower : BlockMillGhost, IMechanicalPowerBlock
{
    /// <summary>The world face that takes the axle; west until the rig is known.</summary>
    public BlockFacing PowerFace { get; private set; } = BlockFacing.WEST;

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);
        PowerFace = PowerFaceFor(BuckingSawmillSystem.Of(api).Rig, Variant["side"]);
    }

    public static BlockFacing PowerFaceFor(Rig? rig, string? side)
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
