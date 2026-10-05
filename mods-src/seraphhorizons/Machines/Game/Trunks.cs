using SeraphHorizons.Mod.Core;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.Rosser;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace SeraphHorizons.Mod.Machines;

/// <summary>
/// A Logging Expanded tree trunk as an item stack (<c>loggingmod:treetrunk-{wood}-{size}-{branches}-{side}</c>).
/// Its logs are an inventory saved in the stack's attributes: <c>slots</c> → <c>"0"</c> holds a stack
/// of the log block whose size is the log count, e.g. <c>game:log-placed-oak-ud</c> ×12. Shared by the
/// bucking mill and the rosser.
/// </summary>
public static class Trunks
{
    public static bool IsTrunk(ItemStack? stack) =>
        stack?.Block?.Code is { Domain: LoggingBridge.ModId } code && code.Path.StartsWith("treetrunk", StringComparison.Ordinal);

    /// <summary>Branched as Logging Expanded judges it: the <c>branches</c> variant is <c>yes</c>, or
    /// branches are counted in the stack.</summary>
    public static bool IsBranched(ItemStack stack) =>
        TrunkVariants.IsBranched(stack.Block?.Variant[TrunkVariants.Group], stack.Attributes.GetInt(BranchCountKey));

    /// <summary>Logging Expanded's branch count in a trunk's stack (and its block entity).</summary>
    public const string BranchCountKey = "branchCount";

    /// <summary>A debarked trunk (the Rosser switch's <c>debarked</c> state of <c>branches</c>).</summary>
    public static bool IsDebarked(ItemStack? stack) => TrunkVariants.IsDebarked(stack?.Block?.Variant[TrunkVariants.Group]);

    /// <summary>What <see cref="Debark"/> made: the debarked trunk, how many branches the trunk had
    /// (its <c>branchCount</c>; 0 for a clean or debarked one) and how many logs it holds.</summary>
    public sealed record Debarking(ItemStack Trunk, int Branches, int Logs);

    /// <summary>The debarked trunk of <paramref name="trunk"/>'s wood, size and side, branchy or not:
    /// a new stack with the same attributes (its logs, resin and char state) less its branch count,
    /// its stored log stack marked (<see cref="DebarkedTrunks.Mark"/>) so a sawhorse knows the logs.
    /// <paramref name="trunk"/> is not changed. Null when it is not a trunk or there is no debarked
    /// trunk (the Rosser switch off). A trunk already debarked gives a copy of itself.</summary>
    public static Debarking? Debark(ItemStack? trunk, IWorldAccessor world)
    {
        if (!IsTrunk(trunk) || trunk!.Block is not { } block)
            return null;
        var debarked = IsDebarked(trunk) ? block
            : TrunkVariants.DebarkedPath(block.Code.Path) is { } path ? world.GetBlock(new AssetLocation(block.Code.Domain, path))
            : null;
        if (debarked is not { Id: > 0 })
            return null;
        var stack = new ItemStack(debarked) { Attributes = trunk.Attributes.Clone() };
        int branches = stack.Attributes.GetInt(BranchCountKey);
        stack.Attributes.RemoveAttribute(BranchCountKey);
        if (stack.Attributes["slots"] is ITreeAttribute slots && slots.GetItemstack("0") is { } logs)
            DebarkedTrunks.Mark(logs);
        return new Debarking(stack, branches, StoredLogs(stack, world));
    }

    /// <summary>The trunk with its branches gone: the clean (<c>no</c>) trunk of the same wood, size
    /// and side, or the same block if it is debarked, with the same attributes less its branch
    /// count. For a trunk broken out of a machine mid-trip. <paramref name="trunk"/> is not changed.
    /// Null when it is not a trunk or the clean trunk does not exist.</summary>
    public static ItemStack? Debranch(ItemStack? trunk, IWorldAccessor world)
    {
        if (!IsTrunk(trunk) || trunk!.Block is not { } block)
            return null;
        var clean = IsDebarked(trunk) ? block
            : TrunkCode.Parse(block.Code.Path)?.WithBranches(TrunkVariants.Clean).Path is { } path
                ? world.GetBlock(new AssetLocation(block.Code.Domain, path))
                : null;
        if (clean is not { Id: > 0 })
            return null;
        var stack = new ItemStack(clean) { Attributes = trunk.Attributes.Clone() };
        stack.Attributes.RemoveAttribute(BranchCountKey);
        return stack;
    }

    /// <summary>The stored log stack, resolved; null when there is none.</summary>
    public static ItemStack? StoredLogStack(ItemStack trunk, IWorldAccessor world)
    {
        if (trunk.Attributes["slots"] is not ITreeAttribute slots || slots.GetItemstack("0") is not { } logs)
            return null;
        return logs.ResolveBlockOrItem(world) ? logs : null;
    }

    public static int StoredLogs(ItemStack trunk, IWorldAccessor world) => StoredLogStack(trunk, world)?.StackSize ?? 0;

    /// <summary>The Logging Expanded block the mill shows for a loaded trunk: its wood, no
    /// branches (debarked if it is), and the size of its class's model (<see cref="TrunkBox.DisplaySize"/>),
    /// so every thin trunk looks like a 1×1×4 and every thick one like a 2×2×5, whatever its own
    /// size. The stack is not changed. The trunk's own block if that one does not exist.</summary>
    public static Block? ShownBlock(IWorldAccessor world, ItemStack? trunk)
    {
        if (trunk?.Block is not { } block)
            return null;
        if (TrunkBox.DisplaySize(TrunkBox.ClassOf(block.Variant["size"])) is not { } size)
            return block;
        return world.GetBlock(block.CodeWithVariants(["size", TrunkVariants.Group],
            [size, IsDebarked(trunk) ? TrunkVariants.Debarked : TrunkVariants.Clean])) is { Id: > 0 } shown ? shown : block;
    }

    /// <summary>The trunk's wood, from its stored logs as Logging Expanded reads it, else its own
    /// <c>wood</c> variant.</summary>
    public static string? Wood(ItemStack trunk, IWorldAccessor world) =>
        ShaftClock.WoodOfStoredLog(StoredLogStack(trunk, world)?.Collectible?.Code?.Path) ?? trunk.Block?.Variant["wood"];
}
