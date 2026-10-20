using System.Reflection;
using HarmonyLib;
using SeraphHorizons.Mod.Ore.Core;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.Ore.Processing;

/// <summary>
/// The whole route through the crucible (README "Ore processing: base bullion"): the vanner marks a by-product ore's
/// concentrate off the whole route (<c>oreWhole</c>, the reverberatory carrying it onto the roasted concentrate), and
/// the ore's final step is the parting machine that takes its bullion. The game's crucible keeps nothing of its
/// charge but the smelted stack, so a prefix on <c>BlockSmeltingContainer.DoSmelt</c> (the firepit's crucible and
/// crucibulum's forge both call it) reads the share of the charge's units that are marked
/// (<see cref="Bullion.WholeShare"/>), and a postfix puts it on the molten bullion the crucible now holds
/// (<see cref="Bullion.WholeShareAttribute"/>). The ingot mold keeps the poured stack's attributes, so every bar
/// cast from it carries the share; a mold refuses a pour of another share into a bar already begun (the game's own
/// test). Nothing else smelts to bullion: the bloomery refuses it (<see cref="BloomeryBullion"/>).
/// </summary>
public static class BullionWhole
{
    public static MethodInfo? Target() =>
        AccessTools.DeclaredMethod(typeof(BlockSmeltingContainer), nameof(BlockSmeltingContainer.DoSmelt),
            [typeof(IWorldAccessor), typeof(ISlotProvider), typeof(ItemSlot), typeof(ItemSlot)]);

    /// <summary>Patches the crucible's smelting; false (nothing patched) when it is not as expected.</summary>
    public static bool Patch(Harmony harmony)
    {
        if (Target() is not { } target)
            return false;
        harmony.Patch(target, prefix: new HarmonyMethod(typeof(BullionWhole), nameof(Prefix)),
            postfix: new HarmonyMethod(typeof(BullionWhole), nameof(Postfix)));
        return true;
    }

    /// <summary>The share of a crucible's charge off the whole route: each stack that smelts to bullion counted by the
    /// bullion it makes (its stack size × the smelted stack's size ÷ the ratio, as the game counts it), the marked
    /// ones' over all. 0 for a charge that does not make bullion or holds nothing marked.</summary>
    public static double ShareOf(IWorldAccessor world, IEnumerable<ItemStack?> charge)
    {
        var parts = new List<(double, bool)>();
        foreach (var stack in charge)
        {
            if (stack == null) continue;
            var props = stack.Collectible.GetCombustibleProperties(world, stack, null);
            if (props?.SmeltedStack?.ResolvedItemstack?.Collectible?.Code is not { } code || Bullion.OreOf(code.ToString()) == null
                || !(props.SmeltedRatio > 0))
                return 0;
            parts.Add((stack.StackSize * (double)props.SmeltedStack.StackSize / props.SmeltedRatio, OreStreamAttributes.IsWhole(stack)));
        }
        return Bullion.WholeShare(parts);
    }

    private static void Prefix(BlockSmeltingContainer __instance, IWorldAccessor world, ISlotProvider cookingSlotsProvider, out double __state)
    {
        __state = 0;
        try
        {
            __state = ShareOf(world, __instance.GetIngredients(world, cookingSlotsProvider));
        }
        catch (Exception e)
        {
            world.Logger.Warning("[seraphhorizons] Ore processing: reading a crucible's whole-route share failed: {0}", e.Message);
        }
    }

    private static void Postfix(IWorldAccessor world, ItemSlot outputSlot, double __state)
    {
        if (!(__state > 0) || outputSlot?.Itemstack is not { Collectible: BlockSmeltedContainer smelted } stack)
            return;
        var (metal, units) = smelted.GetContents(world, stack);
        if (metal?.Collectible?.Code is not { } code || Bullion.OreOf(code.ToString()) == null)
            return;
        OreStreamAttributes.SetWholeShare(metal, __state);
        smelted.SetContents(stack, metal, units);
        outputSlot.MarkDirty();
    }
}
