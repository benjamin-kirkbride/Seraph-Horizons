namespace SeraphHorizons.Mod.Core;

/// <summary>
/// Big piles (<c>BigPiles</c>): a ground pile (the game's <c>Stacking</c> ground storage layout)
/// holds <see cref="StacksPerPile"/> of the item's inventory stacks, and still looks as full as it
/// did when full, and as full as its share of that when not. Game-independent, so tests/ runs it
/// without the game; <c>BigPiles.cs</c> reads and writes the game's numbers.
///
/// What the game's <c>BlockEntityGroundStorage</c> (1.22.7) does with a pile's numbers:
/// <list type="bullet">
/// <item>it holds <c>StackingCapacity</c> items;</item>
/// <item>it draws <c>CuboidsPerModel * ceil(items / ItemsPerModel)</c> elements of the pile's shape
/// (<c>ItemModelCount</c>), so the full pile's element count is
/// <c>CuboidsPerModel * ceil(capacity / ItemsPerModel)</c>;</item>
/// <item>its collision box is the props' box with its top times
/// <c>(int)ceil(CbScaleYByLayer * (float)items)</c> when <c>CbScaleYByLayer</c> is set;</item>
/// <item>a burning pile's flames sit <c>ItemModelCount * ItemsPerModel / StackingCapacity</c>
/// blocks up (integer division: 0, or 1 when full);</item>
/// <item>the obsolete <c>Layers</c> is <c>(int)(items * ModelItemsToStackSizeRatio)</c>.</item>
/// </list>
/// So a pile <c>k</c> times bigger keeps every one of them at the same fill by a <c>k</c> times
/// bigger <c>ItemsPerModel</c> (the full pile's model count kept exactly, <see cref="ItemsPerModel"/>),
/// a <c>k</c> times smaller <c>CbScaleYByLayer</c> (the full pile's layer count kept exactly in the
/// game's float arithmetic, <see cref="CbScale"/>) and a <c>k</c> times smaller ratio.
/// </summary>
public static class BigPileRules
{
    /// <summary>Inventory stacks one pile holds.</summary>
    public const int StacksPerPile = 64;

    /// <summary>A pile's numbers, as <c>GroundStorageProperties</c> names them.</summary>
    public readonly record struct Pile(
        int StackingCapacity,
        int ItemsPerModel,
        int CuboidsPerModel,
        float CbScaleYByLayer,
        float ModelItemsToStackSizeRatio,
        int TransferQuantity,
        int BulkTransferQuantity);

    /// <summary>The capacity of a big pile of an item stacking to <paramref name="maxStackSize"/>:
    /// <see cref="StacksPerPile"/> stacks, never less than <paramref name="capacity"/> (a pile that
    /// already holds more keeps its capacity, so no pile in a world is left over full).</summary>
    public static int Capacity(int capacity, int maxStackSize) =>
        Math.Max(Math.Max(1, capacity), StacksPerPile * Math.Max(1, maxStackSize));

    /// <summary>Models a pile of <paramref name="items"/> draws: <c>ceil(items / itemsPerModel)</c>,
    /// as <c>BlockEntityGroundStorage.ItemModelCount</c> has it.</summary>
    public static int Models(int items, int itemsPerModel) =>
        (int)Math.Ceiling(items / (float)Math.Max(1, itemsPerModel));

    /// <summary>Layers a pile of <paramref name="items"/> scales its collision box's top by, in the
    /// game's arithmetic: <c>(int)Math.Ceiling(cbScaleYByLayer * (float)items)</c>, float times float.</summary>
    public static int Layers(float cbScaleYByLayer, int items)
    {
        float product = cbScaleYByLayer * (float)items;
        return (int)Math.Ceiling(product);
    }

    /// <summary>The items each model stands for in a pile of <paramref name="newCapacity"/>, so the
    /// full pile draws the same number of models as the full pile of <paramref name="oldCapacity"/>
    /// at <paramref name="oldItemsPerModel"/>: the smallest that does, <c>ceil(new / models)</c>
    /// with <c>models</c> the old full pile's. Exact whenever <c>models</c> divides the new capacity
    /// (every pile in the pack: 64 stacks of a power of two); otherwise the full pile may draw one
    /// model fewer, where no whole number of items per model gives it exactly.</summary>
    public static int ItemsPerModel(int oldCapacity, int oldItemsPerModel, int newCapacity)
    {
        int models = Models(Math.Max(1, oldCapacity), oldItemsPerModel);
        return Math.Max(1, (int)Math.Ceiling(newCapacity / (double)models));
    }

    /// <summary>The collision box scale of a pile of <paramref name="newCapacity"/>: the old scale
    /// times <c>old / new</c>, nudged by a float step either way until the full pile's layer count
    /// (<see cref="Layers"/>) is exactly the old full pile's, so a full pile is as tall as before.
    /// 0 stays 0 (no scaling).</summary>
    public static float CbScale(float oldScale, int oldCapacity, int newCapacity)
    {
        if (oldScale == 0f || oldCapacity == newCapacity) return oldScale;
        int want = Layers(oldScale, oldCapacity);
        float scale = (float)(oldScale * (double)oldCapacity / newCapacity);
        for (int i = 0; i < 64 && Layers(scale, newCapacity) > want; i++) scale = MathF.BitDecrement(scale);
        for (int i = 0; i < 64 && Layers(scale, newCapacity) < want; i++) scale = MathF.BitIncrement(scale);
        return scale;
    }

    /// <summary>The pile of an item stacking to <paramref name="maxStackSize"/> made big: capacity
    /// <see cref="Capacity"/>, the drawing and collision numbers scaled to it, a plain click's
    /// quantity kept, and a Ctrl click's a whole stack (never fewer than it moved before). A pile
    /// whose capacity does not change comes back as it is.</summary>
    public static Pile Big(Pile old, int maxStackSize)
    {
        int capacity = Capacity(old.StackingCapacity, maxStackSize);
        if (capacity == old.StackingCapacity) return old;
        return old with
        {
            StackingCapacity = capacity,
            ItemsPerModel = ItemsPerModel(old.StackingCapacity, old.ItemsPerModel, capacity),
            CbScaleYByLayer = CbScale(old.CbScaleYByLayer, Math.Max(1, old.StackingCapacity), capacity),
            ModelItemsToStackSizeRatio = (float)(old.ModelItemsToStackSizeRatio * (double)Math.Max(1, old.StackingCapacity) / capacity),
            BulkTransferQuantity = Math.Max(old.BulkTransferQuantity, Math.Max(1, maxStackSize)),
        };
    }
}
