using SeraphHorizons.Mod.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace SeraphHorizons.Mod.Woodworking;

/// <summary>
/// A splitting block's tier, on Immersive Woodworking's chopping block entity (added to its
/// blocktype by <see cref="SplittingBlock"/>). It comes from the stack the block is placed from
/// and is saved with the block entity, both under <see cref="SplittingBlockTiers.AttributeKey"/>;
/// a block or stack without it is primitive. Immersive Woodworking's block entity calls its base
/// first in <c>OnBlockPlaced</c>, <c>ToTreeAttributes</c> and <c>FromTreeAttributes</c>, so this
/// runs there and its key survives next to Immersive Woodworking's own.
///
/// It also holds the upgrade holds in progress on its side (<see cref="SplittingBlockUpgrades"/>):
/// each side has its own block entity, so a hold started on the client and the server's copy of
/// it never meet.
/// </summary>
public class BEBehaviorSplittingBlockTier(BlockEntity blockentity) : BlockEntityBehavior(blockentity)
{
    public const string Name = "seraphhorizons.SplittingBlockTier";

    public SplittingBlockTier Tier { get; private set; }

    /// <summary>Upgrade holds in progress on this side, by player uid.</summary>
    internal Dictionary<string, SplittingBlockUpgrade> Holds { get; } = [];

    /// <summary>The behavior of the block entity at a position, if it is a splitting block.</summary>
    public static BEBehaviorSplittingBlockTier? At(IWorldAccessor world, Vintagestory.API.MathTools.BlockPos? pos) =>
        pos == null ? null : world.BlockAccessor.GetBlockEntity(pos)?.GetBehavior<BEBehaviorSplittingBlockTier>();

    /// <summary>The tier a stack names.</summary>
    public static SplittingBlockTier Of(ItemStack? stack) =>
        SplittingBlockTiers.Parse(stack?.Attributes?.GetString(SplittingBlockTiers.AttributeKey));

    /// <summary>Server: the block becomes <paramref name="tier"/>, and clients redraw it.</summary>
    public void SetTier(SplittingBlockTier tier)
    {
        Tier = tier;
        Blockentity.MarkDirty(true);
    }

    public override void OnBlockPlaced(ItemStack? byItemStack = null) => Tier = Of(byItemStack);

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetString(SplittingBlockTiers.AttributeKey, Tier.Name());
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);
        var tier = SplittingBlockTiers.Parse(tree.GetString(SplittingBlockTiers.AttributeKey));
        // An upgrade changes the look and nothing Immersive Woodworking redraws for.
        if (tier != Tier && Blockentity.Api is ICoreClientAPI capi)
            capi.World.BlockAccessor.MarkBlockDirty(Blockentity.Pos);
        Tier = tier;
    }
}
