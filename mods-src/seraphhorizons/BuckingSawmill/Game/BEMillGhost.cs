using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace SeraphHorizons.Mod.BuckingSawmill;

/// <summary>A ghost cell of the mill: remembers the controller's position, and takes its cell's
/// boxes from it, asked afresh each time since a loaded trunk changes them. The power ghost carries
/// the mechanical power behavior too.</summary>
public class BEMillGhost : BlockEntity
{
    public BlockPos? Principal { get; set; }

    public BEBuckingMill? Mill =>
        Principal != null && Api != null ? Api.World.BlockAccessor.GetBlockEntity(Principal) as BEBuckingMill : null;

    public Cuboidf[]? CellBoxes(IBlockAccessor blockAccessor) =>
        Principal is { } principal && blockAccessor.GetBlockEntity(principal) is BEBuckingMill mill ? mill.CellBoxes(Pos) : null;

    public Cuboidf[]? CollisionBoxes(IBlockAccessor blockAccessor) =>
        Principal is { } principal && blockAccessor.GetBlockEntity(principal) is BEBuckingMill mill ? mill.CollisionBoxes(Pos) : null;

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);
        int x = tree.GetInt("cx", -1), y = tree.GetInt("cy", -1), z = tree.GetInt("cz", -1);
        Principal = x == -1 && y == -1 && z == -1 ? null : new BlockPos(x, y, z);
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetInt("cx", Principal?.X ?? -1);
        tree.SetInt("cy", Principal?.Y ?? -1);
        tree.SetInt("cz", Principal?.Z ?? -1);
    }

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
    {
        base.GetBlockInfo(forPlayer, dsc);
        Mill?.GetBlockInfo(forPlayer, dsc);
    }
}
