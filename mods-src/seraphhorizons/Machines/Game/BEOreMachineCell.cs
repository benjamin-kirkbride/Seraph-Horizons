using System.Text;
using SeraphHorizons.Mod.Machines.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.Machines;

/// <summary>
/// A cell of an ore machine (#711): remembers its controller and takes its boxes from it. It is a
/// container so a vanilla chute laid into the machine's infeed face pushes into it: its one slot takes,
/// through the infeed face only, what the machine takes (<see cref="BEOreMachine.Takes"/>), and the
/// controller draws it in from there (<see cref="BEOreMachine.InfeedBuffer"/>). On the power cell it is the
/// <see cref="IMachinePower"/> of <see cref="BEBehaviorMachineMP"/>: its block's face, its controller's load.
/// </summary>
public class BEOreMachineCell : BlockEntityContainer, IMachinePower
{
    public const string ClassName = "seraphhorizons.OreMachineCell";

    private readonly InventoryGeneric _inventory;

    public BEOreMachineCell()
    {
        _inventory = new InventoryGeneric(1, null, null);
    }

    public BlockPos? Principal { get; set; }

    public override InventoryBase Inventory => _inventory;
    public override string InventoryClassName => "oremachinecell";

    /// <summary>The slot a chute fills, when this is the infeed cell.</summary>
    public ItemSlot Buffer => _inventory[0];

    public BEOreMachine? Controller =>
        Principal != null && Api != null ? Api.World.BlockAccessor.GetBlockEntity(Principal) as BEOreMachine : null;

    public BEOreMachine? Machine(IBlockAccessor blockAccessor) =>
        Principal is { } p ? blockAccessor.GetBlockEntity(p) as BEOreMachine : null;

    public BlockFacing PowerFace => (Block as BlockOreMachineCellPower)?.PowerFace ?? BlockFacing.UP;

    public float Resistance => Controller?.Resistance ?? MachineLoad.Unassembled;

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        _inventory.OnGetAutoPushIntoSlot = (face, from) =>
            Controller is { } machine && from?.Itemstack is { } stack && machine.InfeedBuffer(Pos, face) == this
            && machine.Takes(stack)
            && (Buffer.Empty || Buffer.Itemstack.Equals(api.World, stack, Vintagestory.API.Config.GlobalConstants.IgnoredStackAttributes))
                ? Buffer
                : null;
        _inventory.OnGetAutoPullFromSlot = _ => null;
    }

    // What it holds is the controller's to drop (BEOreMachine.RemoveCells): a cell is never broken by itself.
    public override void OnBlockBroken(IPlayer? byPlayer = null)
    {
    }

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
        // not the container's contents: the controller's info says what the machine holds
        Controller?.GetBlockInfo(forPlayer, dsc);
    }
}
