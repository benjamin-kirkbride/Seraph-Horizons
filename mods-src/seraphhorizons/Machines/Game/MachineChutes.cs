using SeraphHorizons.Mod.Machines.Core;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.Machines;

/// <summary>
/// Chutes in and out of a continuous machine (#711), at its rig's anchors, with vanilla's chute
/// (<c>BlockEntityItemFlow</c>) or any container:
/// <list type="bullet">
/// <item><b>Out.</b> <see cref="TryPush"/> puts a product through an anchor's face into the container
/// beyond it, as a chute pushes into the next (<c>GetAutoPushIntoSlot</c> of the face it comes in at;
/// a chute takes only into an empty slot and only from a face it pulls from or accepts from, so it must
/// be laid to take from the machine). Nothing there takes it: the machine keeps it (its output is
/// blocked) or, for tailings, discards it (<see cref="Tailings"/>).</item>
/// <item><b>In.</b> A chute pushes into the block entity beyond its push face when that is a
/// <see cref="BlockEntityContainer"/>; the machine's infeed cell's block entity exposes an inventory
/// whose <see cref="Infeed"/> callbacks take only through the infeed face, only what the machine
/// takes, up to its own room. A hand feeding into the chute's mouth (the stamp battery) is the
/// machine's own right-click on that cell.</item>
/// </list>
/// Batch machines have no chute anchor (<see cref="FeedMode.Batch"/>): loaded and emptied by hand.
/// </summary>
public static class MachineChutes
{
    /// <summary>The world block of anchor <paramref name="anchor"/> on a machine with its controller at
    /// <paramref name="controller"/> facing <paramref name="facing"/>, and its world face.</summary>
    public static (BlockPos Cell, BlockFacing Face) World(BlockPos controller, Side facing, MachineAnchor anchor)
    {
        var (cell, face) = anchor.ToWorld(facing);
        return (controller.AddCopy(cell.X, cell.Y, cell.Z), BlockFacing.FromCode(face.Code()));
    }

    /// <summary>Whether something beyond the anchor's face takes items (a chute laid to take from the
    /// machine, or a container): an outlet chuted on.</summary>
    public static bool ChutedOn(IWorldAccessor world, BlockPos cell, BlockFacing face, ItemSlot probe) =>
        Target(world, cell, face) is { } target && target.Inventory.GetAutoPushIntoSlot(face.Opposite, probe) != null;

    private static BlockEntityContainer? Target(IWorldAccessor world, BlockPos cell, BlockFacing face)
    {
        var beyond = cell.AddCopy(face);
        return world.BlockAccessor.GetBlock(beyond).GetBlockEntity<BlockEntityContainer>(beyond);
    }

    /// <summary>Pushes up to <paramref name="max"/> of <paramref name="from"/> out of <paramref name="cell"/>'s
    /// <paramref name="face"/> into what is beyond; how many went. Into a chute only when its slot is
    /// empty, as chutes pass items on to each other.</summary>
    public static int TryPush(IWorldAccessor world, BlockPos cell, BlockFacing face, ItemSlot from, int max = int.MaxValue)
    {
        if (from.Empty || max <= 0 || Target(world, cell, face) is not { } target)
            return 0;
        var into = target.Inventory.GetAutoPushIntoSlot(face.Opposite, from);
        if (into == null || target is BlockEntityItemFlow && !into.Empty)
            return 0;
        // a chute's own travel counters are its, not ours
        from.Itemstack.Attributes.RemoveAttribute("chuteQHTravelled");
        from.Itemstack.Attributes.RemoveAttribute("chuteDir");
        var op = new ItemStackMoveOperation(world, EnumMouseButton.Left, 0, EnumMergePriority.DirectMerge, Math.Min(max, from.StackSize));
        int moved = from.TryPutInto(into, ref op);
        if (moved > 0)
        {
            from.MarkDirty();
            into.MarkDirty();
            target.MarkDirty();
        }
        return moved;
    }

    /// <summary>Pushes a stack out (a product made now, not held in a slot): how many went.</summary>
    public static int TryPush(IWorldAccessor world, BlockPos cell, BlockFacing face, ItemStack stack)
    {
        var slot = new DummySlot(stack);
        return TryPush(world, cell, face, slot);
    }

    /// <summary>
    /// Wires an infeed inventory: chutes and hoppers push into <paramref name="slot"/> only through
    /// <paramref name="face"/> and only stacks <paramref name="takes"/> accepts; nothing is pulled out of
    /// it. Set on the inventory the infeed cell's block entity gives as its <c>Inventory</c>.
    /// </summary>
    public static void Infeed(InventoryGeneric inventory, System.Func<BlockFacing> face, ItemSlot slot, System.Func<ItemStack, bool> takes)
    {
        inventory.OnGetAutoPushIntoSlot = (at, from) =>
            at == face() && from?.Itemstack is { } stack && takes(stack) && (slot.Empty || slot.Itemstack.Equals(inventory.Api?.World, stack, Vintagestory.API.Config.GlobalConstants.IgnoredStackAttributes))
                ? slot
                : null;
        inventory.OnGetAutoPullFromSlot = _ => null;
    }
}
