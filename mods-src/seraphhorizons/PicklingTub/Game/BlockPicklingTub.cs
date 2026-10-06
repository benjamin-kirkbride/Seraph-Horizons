using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace SeraphHorizons.Mod.PicklingTub;

/// <summary>The pickling tub's block: right-clicks go to its block entity (<see cref="BEPicklingTub"/>).</summary>
public class BlockPicklingTub : Block
{
    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is BEPicklingTub tub && tub.OnInteract(byPlayer))
            return true;
        return base.OnBlockInteractStart(world, byPlayer, blockSel);
    }

    public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer) =>
    [
        new WorldInteraction { ActionLangCode = "seraphhorizons:picklingtub-help-pour", MouseButton = EnumMouseButton.Right },
        new WorldInteraction { ActionLangCode = "seraphhorizons:picklingtub-help-gears", MouseButton = EnumMouseButton.Right },
        new WorldInteraction { ActionLangCode = "seraphhorizons:picklingtub-help-take", MouseButton = EnumMouseButton.Right },
        .. base.GetPlacedBlockInteractionHelp(world, selection, forPlayer),
    ];
}
