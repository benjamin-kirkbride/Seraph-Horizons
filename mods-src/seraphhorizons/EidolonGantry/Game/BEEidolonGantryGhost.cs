using System.Text;
using SeraphHorizons.Mod.Machines;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace SeraphHorizons.Mod.EidolonGantry;

/// <summary>A ghost cell of the eidolon gantry: remembers the controller's position and takes its
/// cell's boxes from it.</summary>
public class BEEidolonGantryGhost : BlockEntity, IMachineGhost
{
    public BlockPos? Principal { get; set; }

    public BEEidolonGantry? Gantry =>
        Principal != null && Api != null ? Api.World.BlockAccessor.GetBlockEntity(Principal) as BEEidolonGantry : null;

    public Cuboidf[]? CellBoxes(IBlockAccessor blockAccessor) =>
        Principal is { } principal && blockAccessor.GetBlockEntity(principal) is BEEidolonGantry gantry ? gantry.CellBoxes(Pos) : null;

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
        Gantry?.GetBlockInfo(forPlayer, dsc);
    }
}
