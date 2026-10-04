using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace SeraphHorizons.Mod.BuckingSawmill;

/// <summary>A ghost cell of the mill: remembers the controller's position, and takes its cell's
/// boxes from it. The power ghost carries the mechanical power behavior too.</summary>
public class BEMillGhost : BlockEntity
{
    private volatile Cuboidf[]? _boxes;

    public BlockPos? Principal { get; set; }

    public BEBuckingMill? Mill =>
        Principal != null && Api != null ? Api.World.BlockAccessor.GetBlockEntity(Principal) as BEBuckingMill : null;

    public Cuboidf[]? CellBoxes(IBlockAccessor blockAccessor)
    {
        if (_boxes != null)
            return _boxes;
        if (Principal == null || blockAccessor.GetBlockEntity(Principal) is not BEBuckingMill mill)
            return null;
        return _boxes = mill.CellBoxes(Pos);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);
        int x = tree.GetInt("cx", -1), y = tree.GetInt("cy", -1), z = tree.GetInt("cz", -1);
        var principal = x == -1 && y == -1 && z == -1 ? null : new BlockPos(x, y, z);
        if (principal != Principal)
            _boxes = null;
        Principal = principal;
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
