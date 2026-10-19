using SeraphHorizons.Mod.Core;
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
/// be laid to take from the machine). With a chute or container there (<see cref="Attached"/>) the machine
/// keeps what it could not take and waits for it (a backed-up chute stops it); with nothing there it never
/// waits: tailings go on the heap (<see cref="Tailings"/>) and everything else is dropped on the ground in
/// front of the face (<see cref="Drop"/>, <see cref="Outlet"/>).</item>
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

    /// <summary>Whether a chute or container is attached beyond the anchor's face: a container of any kind
    /// (full or not), or a chute laid to take from the machine (one laid across the face is not). An outlet
    /// with one attached holds what it cannot push yet; one without drops it.</summary>
    public static bool Attached(IWorldAccessor world, BlockPos cell, BlockFacing face, ItemSlot probe) =>
        Target(world, cell, face) is { } target
        && (target is not BlockEntityItemFlow || target.Inventory.GetAutoPushIntoSlot(face.Opposite, probe) != null);

    /// <summary>Drops <paramref name="stack"/> on the ground just beyond <paramref name="cell"/>'s
    /// <paramref name="face"/> (<see cref="Outlet.DropOffset"/>), still, so it settles where it appears.</summary>
    public static void Drop(IWorldAccessor world, BlockPos cell, BlockFacing face, ItemStack stack)
    {
        var (x, y, z) = Outlet.DropOffset(face.Normali.X, face.Normali.Y, face.Normali.Z);
        world.SpawnItemEntity(stack, cell.ToVec3d().Add(x, y, z), new Vec3d());
    }

    private static BlockEntityContainer? Target(IWorldAccessor world, BlockPos cell, BlockFacing face)
    {
        var beyond = cell.AddCopy(face);
        return world.BlockAccessor.GetBlock(beyond).GetBlockEntity<BlockEntityContainer>(beyond);
    }

    /// <summary>Pushes up to <paramref name="max"/> of <paramref name="from"/> out of <paramref name="cell"/>'s
    /// <paramref name="face"/> into what is beyond; how many went. Into a chute only when its slot is
    /// empty, as chutes pass items on to each other. With <c>ChutesNoBacktrack</c> the item in a chute
    /// is tagged with the face's direction and the chute records the face it came in by (#1348).</summary>
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
            // Chutes never send an item back (#1348): the chute knows it came in by this face, and the
            // item goes on in the machine's direction (up in a screw), as a chute tags it for the next.
            if (target is BlockEntityItemFlow flow && ChutesNoBacktrack.Enabled && !into.Empty)
            {
                into.Itemstack.Attributes.SetInt("chuteDir",
                    ChuteEntry.Tag(face.Index, flow is Vintagestory.GameContent.Mechanics.BlockEntityArchimedesScrew, BlockFacing.UP.Index));
                ChutesNoBacktrack.Received(flow, face.Opposite);
            }
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
