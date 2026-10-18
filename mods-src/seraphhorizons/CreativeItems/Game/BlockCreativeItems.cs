using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace SeraphHorizons.Mod.CreativeItems;

/// <summary>The source's and the sink's block: right-click opens the block's dialog, for a player
/// in creative mode (<see cref="CreativeItemBlocks.CanUse"/>).</summary>
public class BlockCreativeItems : Block
{
    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is not ICreativeItemsBlockEntity be
            || byPlayer.WorldData.CurrentGameMode != EnumGameMode.Creative)
            return base.OnBlockInteractStart(world, byPlayer, blockSel);
        if (world.Api is ICoreClientAPI && CreativeItemBlocks.CanUse(world, byPlayer, blockSel.Position))
            be.ToggleDialog();
        return true;
    }

    public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer) =>
    [
        new() { ActionLangCode = "seraphhorizons:blockhelp-" + Code.Path + "-open", MouseButton = EnumMouseButton.Right },
    ];
}

/// <summary>What the block asks of either block entity.</summary>
public interface ICreativeItemsBlockEntity
{
    /// <summary>Opens the block's dialog, or closes it if open (client side).</summary>
    void ToggleDialog();
}
