using SeraphHorizons.Mod.Springs.Core;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.Springs;

/// <summary>
/// An untapped spring's outflow (README "Springs"), on the server: what the game's source block
/// does in <c>SpreadAndUpdateLiquidLevels</c>, from a solid block. The cell under it gets falling
/// water (<c>{liquid}-d-6</c>) when the game's <c>CanSpreadIntoBlock</c> allows it, as if the
/// spring's cell were open water at level 7; otherwise every side it allows gets the level-6 block
/// the game would spread there (<c>GetLessLiquidBlockId</c>). Each block put is told to look again
/// (its <c>OnNeighbourBlockChange</c>), so the game's own spreading carries it on. The water stays
/// because <see cref="SpringsSystem"/>'s postfix on <c>GetMaxNeighbourLiquidLevel</c> counts a
/// pouring spring as a source; a tapped or broken spring is no source, and its water drains as any
/// water without one.
/// </summary>
public static class SpringPouring
{
    /// <summary>Pours from the spring at <paramref name="pos"/>.</summary>
    public static void Pour(IWorldAccessor world, BlockPos pos, BlockWaterSpring spring)
    {
        var source = spring.Source;
        var behavior = source?.GetBehavior<BlockBehaviorFiniteSpreadingLiquid>();
        if (source == null || behavior == null || spring.Falling == null)
            return;
        var ba = world.BlockAccessor;
        var open = world.GetBlock(0);
        var below = pos.DownCopy();
        var belowFluid = ba.GetBlock(below, BlockLayersAccess.Fluid);
        bool belowOpen = ba.GetMostSolidBlock(below).GetLiquidBarrierHeightOnSide(BlockFacing.UP, below) < 1f;
        bool canDown = belowOpen && CanPourInto(world, behavior, source, open, pos, below, BlockFacing.DOWN, spring);
        bool belowSource = spring.Pours(belowFluid) && belowFluid.LiquidLevel == SpringCodes.SourceLevel;
        foreach (int face in SpringPour.Faces(belowOpen, canDown, belowSource,
                     f => CanPourInto(world, behavior, source, open, pos, pos.AddCopy(BlockFacing.ALLFACES[f]), BlockFacing.ALLFACES[f], spring)))
        {
            var facing = BlockFacing.ALLFACES[face];
            var at = pos.AddCopy(facing);
            // Already its water at the poured level or higher, in whatever flow the game has given
            // it: left alone (the game turns its flow; putting it back would fight that).
            var there = ba.GetBlock(at, BlockLayersAccess.Fluid);
            if (spring.Pours(there) && there.LiquidLevel >= SpringCodes.PouredLevel)
                continue;
            int id = face == SpringPour.Down
                ? spring.Falling.Id
                : behavior.GetLessLiquidBlockId(world, at, source);
            if (id == 0)
                continue;
            ba.SetBlock(id, at, BlockLayersAccess.Fluid);
            world.GetBlock(id).OnNeighbourBlockChange(world, at, pos);
        }
    }

    // The game's check, as from open water at level 7 in the spring's cell, and only into its own
    // liquid, air or what the game lets water replace: never over another liquid (lava, salt water).
    private static bool CanPourInto(IWorldAccessor world, BlockBehaviorFiniteSpreadingLiquid behavior, Block source, Block open,
        BlockPos pos, BlockPos at, BlockFacing facing, BlockWaterSpring spring)
    {
        var fluid = world.BlockAccessor.GetBlock(at, BlockLayersAccess.Fluid);
        if (fluid.Id != 0 && !spring.Pours(fluid))
            return false;
        return behavior.CanSpreadIntoBlock(source, open, pos, at, facing, world);
    }

    /// <summary>Tells the water round <paramref name="pos"/> that the spring there changed (tapped,
    /// untapped, broken), so the game's flowing water looks again: it drains without a source.</summary>
    public static void Notify(IWorldAccessor world, BlockPos pos, BlockWaterSpring spring)
    {
        var ba = world.BlockAccessor;
        foreach (var facing in BlockFacing.ALLFACES)
        {
            var at = pos.AddCopy(facing);
            var fluid = ba.GetBlock(at, BlockLayersAccess.Fluid);
            if (fluid.Id != 0 && spring.Pours(fluid))
                fluid.OnNeighbourBlockChange(world, at, pos);
        }
    }
}
