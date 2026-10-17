using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.Arrastra;

/// <summary>
/// The arrastra's water, poured by hand from the liquid container the player holds (a bucket, or any
/// container the game's buckets are: <see cref="ILiquidSource"/>; the owner's ruling on #732, no pipe).
/// Fresh water only: the game's water and Hydrate or Diedrate's fresh waters (found by name).
/// </summary>
public static class ArrastraWater
{
    public const string PlainWater = "game:waterportion";
    public const string HydrateFreshPrefix = "hydrateordiedrate:waterportion-fresh";

    /// <summary>Whether a liquid's code is fresh water.</summary>
    public static bool IsWater(string? code) =>
        code == PlainWater || code != null && code.StartsWith(HydrateFreshPrefix, StringComparison.Ordinal);

    /// <summary>The water in <paramref name="held"/>, litres; 0 when it holds none or something else.</summary>
    public static double Litres(ItemStack? held)
    {
        if (held?.Collectible is not ILiquidSource source || source.GetContent(held) is not { } content
            || !IsWater(content.Collectible.Code.ToString()))
            return 0;
        double perLitre = PortionsPerLitre(held);
        return perLitre > 0 ? content.StackSize / perLitre : 0;
    }

    /// <summary>Portions of water a litre is in <paramref name="held"/>; 100 by default.</summary>
    public static double PortionsPerLitre(ItemStack? held) =>
        held?.Collectible is ILiquidSource source && source.GetContent(held) is { } content
            ? BlockLiquidContainerBase.GetContainableProps(content)?.ItemsPerLitre ?? 100
            : 100;

    /// <summary>Takes up to <paramref name="portions"/> of water out of the held container; returns how many.</summary>
    public static int Take(IPlayer player, ItemSlot slot, int portions)
    {
        if (portions <= 0 || slot.Itemstack is not { Collectible: ILiquidSource source } held)
            return 0;
        int taken;
        if (held.StackSize > 1 && source is BlockLiquidContainerBase container)
            taken = container.SplitStackAndPerformAction(player.Entity, slot, one => container.TryTakeContent(one, portions)?.StackSize ?? 0);
        else
            taken = source.TryTakeContent(held, portions)?.StackSize ?? 0;
        if (taken > 0)
            slot.MarkDirty();
        return taken;
    }
}
