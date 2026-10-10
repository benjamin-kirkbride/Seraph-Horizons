using SeraphHorizons.Mod.BuckingSawmill;
using SeraphHorizons.Mod.Eidolon.Core;
using SeraphHorizons.Mod.Rosser;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SeraphHorizons.Mod.Eidolon;

/// <summary>
/// A machine's infeed as a hauler sees it: the machine (its controller), the ground cells just
/// beyond its infeed end where it takes a trunk entity lying (<c>InfeedNeighbours</c> of its rig), the
/// way out of the machine from them, and which trunks it takes at all (<c>Takes</c>: the rosser none
/// already debarked, the mill none branched while Logging Expanded requires debranching).
/// </summary>
public sealed record MachineInfeed(BlockPos Machine, string Kind, IReadOnlyList<BlockPos> Cells, int OutwardX, int OutwardZ, System.Func<ItemStack, bool> Takes)
{
    /// <summary>The cells as marks, for <see cref="HaulPlan.Drop"/>.</summary>
    public IReadOnlyList<MarkPos> CellMarks => Cells.Select(c => new MarkPos(c.X, c.Y, c.Z)).ToList();
}

/// <summary>Finds the machine infeed a hauler delivers to from any cell of the machine (the
/// controller or a ghost): a rosser (<see cref="BERosser"/>) or a bucking mill
/// (<see cref="BEBuckingMill"/>). The crew order (#679) uses the same.</summary>
public static class MachineInfeeds
{
    public const string RosserKind = "rosser";
    public const string MillKind = "mill";

    /// <summary>The machine whose cell is at <paramref name="pos"/>, or null.</summary>
    public static MachineInfeed? Find(IWorldAccessor world, BlockPos pos)
    {
        var be = world.BlockAccessor.GetBlockEntity(pos);
        if ((be as BERosser ?? (be as BERosserGhost)?.Rosser) is { } rosser && rosser.InfeedCells() is { } r)
            return new MachineInfeed(rosser.Pos.Copy(), RosserKind, r.Cells, r.OutwardX, r.OutwardZ, rosser.Takes);
        if ((be as BEBuckingMill ?? (be as BEMillGhost)?.Mill) is { } mill && mill.InfeedCells() is { } m)
            return new MachineInfeed(mill.Pos.Copy(), MillKind, m.Cells, m.OutwardX, m.OutwardZ, mill.Takes);
        return null;
    }
}
