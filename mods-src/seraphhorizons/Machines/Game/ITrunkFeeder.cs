using SeraphHorizons.Mod.Machines.Core;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SeraphHorizons.Mod.Machines;

/// <summary>A machine that hands finished trunks on to the next one in line (the rosser to the
/// bucking mill), found the way a Trunk Storage Rack is: by a cell touching the taker's infeed
/// end. Implemented by the controller's block entity; its ghost cells lead to it through
/// <see cref="IMachineGhost"/>. Every member is asked on the server only, on the server's thread.
/// <para>The taker peeks first and checks its own rules (its bed empty, its saws at the top, the
/// trunk debranched and holding logs). Peeking changes nothing, so a taker that refuses after
/// peeking leaves the trunk where it is. It calls <see cref="TakeFinished"/> only once it will
/// load the trunk, in the same tick, right after a peek that gave one.</para></summary>
public interface ITrunkFeeder
{
    /// <summary>The feeder's <c>side</c> variant (its facing). A taker takes only from a feeder
    /// facing its own way, so the trunk keeps going the same way.</summary>
    Side Side { get; }

    /// <summary>Whether <paramref name="world"/> is one of the feeder's own ground-level cells at
    /// its outfeed end (the end its finished trunk leaves by), where a taker in line touches it.
    /// False for any other cell of the feeder.</summary>
    bool HasOutfeedCell(BlockPos world);

    /// <summary>Whether a trunk is in the feeder but not finished yet (waiting at the infeed or on
    /// its way through, powered or not): one is coming. False when the feeder is empty or its
    /// finished trunk is waiting to be taken.</summary>
    bool Busy { get; }

    /// <summary>The finished trunk waiting to be taken, without taking it; null when there is none.
    /// The caller does not change it.</summary>
    ItemStack? PeekFinished();

    /// <summary>Takes the finished trunk: the same trunk <see cref="PeekFinished"/> gives, now the
    /// caller's. The feeder clears its outfeed (and the trunk's boxes) and saves and syncs itself.
    /// Null when there is none, and then nothing changes.</summary>
    ItemStack? TakeFinished();
}

/// <summary>A ghost cell of a multiblock machine: it knows the controller's position.</summary>
public interface IMachineGhost
{
    BlockPos? Principal { get; }
}

public static class TrunkFeeders
{
    /// <summary>The feeder at <paramref name="pos"/>: the block entity there if it is one, or the
    /// controller of the ghost there; null otherwise.</summary>
    public static ITrunkFeeder? Find(IBlockAccessor blockAccessor, BlockPos pos) =>
        blockAccessor.GetBlockEntity(pos) switch
        {
            ITrunkFeeder feeder => feeder,
            IMachineGhost { Principal: { } principal } => blockAccessor.GetBlockEntity(principal) as ITrunkFeeder,
            _ => null,
        };
}
