using SeraphHorizons.Mod.BuckingSawmill.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace SeraphHorizons.Mod.BuckingSawmill;

/// <summary>
/// A Logging Expanded tree trunk as an item stack (<c>loggingmod:treetrunk-{wood}-{size}-{branches}-{side}</c>).
/// Its logs are an inventory saved in the stack's attributes: <c>slots</c> → <c>"0"</c> holds a stack
/// of the log block whose size is the log count, e.g. <c>game:log-placed-oak-ud</c> ×12.
/// </summary>
public static class Trunks
{
    public static bool IsTrunk(ItemStack? stack) =>
        stack?.Block?.Code is { Domain: LoggingBridge.ModId } code && code.Path.StartsWith("treetrunk", StringComparison.Ordinal);

    /// <summary>Branched as Logging Expanded judges it: the <c>branches</c> variant is <c>yes</c>, or
    /// branches are counted in the stack.</summary>
    public static bool IsBranched(ItemStack stack) =>
        stack.Block?.Variant["branches"] == "yes" || stack.Attributes.GetInt("branchCount") > 0;

    /// <summary>The stored log stack, resolved; null when there is none.</summary>
    public static ItemStack? StoredLogStack(ItemStack trunk, IWorldAccessor world)
    {
        if (trunk.Attributes["slots"] is not ITreeAttribute slots || slots.GetItemstack("0") is not { } logs)
            return null;
        return logs.ResolveBlockOrItem(world) ? logs : null;
    }

    public static int StoredLogs(ItemStack trunk, IWorldAccessor world) => StoredLogStack(trunk, world)?.StackSize ?? 0;

    /// <summary>The Logging Expanded block the mill shows for a loaded trunk: its wood, no
    /// branches, and the size of its class's model (<see cref="TrunkBox.DisplaySize"/>), so every
    /// thin trunk looks like a 1×1×4 and every thick one like a 2×2×5, whatever its own size. The
    /// stack is not changed. The trunk's own block if that one does not exist.</summary>
    public static Block? ShownBlock(IWorldAccessor world, ItemStack? trunk)
    {
        if (trunk?.Block is not { } block)
            return null;
        if (TrunkBox.DisplaySize(TrunkBox.ClassOf(block.Variant["size"])) is not { } size)
            return block;
        return world.GetBlock(block.CodeWithVariants(["size", "branches"], [size, "no"])) is { Id: > 0 } shown ? shown : block;
    }

    /// <summary>The trunk's wood, from its stored logs as Logging Expanded reads it, else its own
    /// <c>wood</c> variant.</summary>
    public static string? Wood(ItemStack trunk, IWorldAccessor world) =>
        Cutting.WoodOfStoredLog(StoredLogStack(trunk, world)?.Collectible?.Code?.Path) ?? trunk.Block?.Variant["wood"];
}
