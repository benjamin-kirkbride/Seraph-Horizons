using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace SeraphHorizons.Mod.Trading.Visitors;

/// <summary>
/// The inn flag (<c>seraphhorizons:innflag</c>, #456): raising it (placing it) makes an inn of the
/// building around it, which <see cref="InnSystem"/> then checks once a day; taking it down ends the
/// inn (and any visit). The player who places it owns the inn: their standing calls the visitors.
/// Right-clicking it says what the inn still lacks. A flag placed without a player (a command, a
/// schematic) still makes an inn, without an owner.
/// </summary>
public class BlockInnFlag : Block
{
    public const string ClassName = "SeraphHorizons.InnFlag";

    public override void OnBlockPlaced(IWorldAccessor world, BlockPos blockPos, ItemStack? byItemStack = null)
    {
        base.OnBlockPlaced(world, blockPos, byItemStack);
        if (world.Side == EnumAppSide.Server) InnSystem.Of(world.Api)?.Raised(blockPos, null);
    }

    public override bool DoPlaceBlock(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ItemStack byItemStack)
    {
        bool placed = base.DoPlaceBlock(world, byPlayer, blockSel, byItemStack);
        if (placed && world.Side == EnumAppSide.Server && byPlayer is IServerPlayer player)
            InnSystem.Of(world.Api)?.Raised(blockSel.Position, player);
        return placed;
    }

    public override void OnBlockRemoved(IWorldAccessor world, BlockPos pos)
    {
        if (world.Side == EnumAppSide.Server) InnSystem.Of(world.Api)?.Lowered(pos);
        base.OnBlockRemoved(world, pos);
    }

    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        if (world.Side == EnumAppSide.Server && byPlayer is IServerPlayer player)
            InnSystem.Of(world.Api)?.Tell(player, blockSel.Position);
        return true;
    }
}
