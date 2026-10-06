using System.Text;
using SeraphHorizons.Mod.Machines;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace SeraphHorizons.Mod.Rosser;

/// <summary>A ghost cell of the rosser: remembers the controller's position (so the bucking mill
/// finds the rosser through it, <see cref="IMachineGhost"/>), and takes its cell's boxes from it,
/// asked afresh each time since the travelling trunk changes them. The power ghost carries the
/// mechanical power behavior too.</summary>
public class BERosserGhost : BlockEntity, IMachineGhost
{
    public BlockPos? Principal { get; set; }

    public BERosser? Rosser =>
        Principal != null && Api != null ? Api.World.BlockAccessor.GetBlockEntity(Principal) as BERosser : null;

    public Cuboidf[]? CellBoxes(IBlockAccessor blockAccessor) =>
        Principal is { } principal && blockAccessor.GetBlockEntity(principal) is BERosser rosser ? rosser.CellBoxes(Pos) : null;

    public Cuboidf[]? CollisionBoxes(IBlockAccessor blockAccessor) =>
        Principal is { } principal && blockAccessor.GetBlockEntity(principal) is BERosser rosser ? rosser.CollisionBoxes(Pos) : null;

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
        Rosser?.GetBlockInfo(forPlayer, dsc);
    }
}
