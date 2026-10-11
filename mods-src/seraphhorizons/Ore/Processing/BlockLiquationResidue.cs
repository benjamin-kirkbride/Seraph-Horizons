using System.Globalization;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace SeraphHorizons.Mod.Ore.Processing;

/// <summary>
/// A liquation pan with lead residue, <c>seraphhorizons:liquationpan-residue</c> (#724): the pan, its tin
/// poured, with the lead it held back set solid in it. Knocked out with a hammer in the grid
/// (<c>recipes/grid/liquationpan.json</c>), the pan is the recipe's output and the lead's metal bits
/// (<see cref="BlockLiquationPanSmelted.Residue"/>) go to the player (<see cref="OnConsumedByCrafting"/>).
/// </summary>
public class BlockLiquationResidue : Block
{
    /// <summary>The lead (or other residue) a stack holds and the bits it gives, for its tooltip.</summary>
    public static void AppendResidue(ItemStack stack, StringBuilder dsc, IWorldAccessor world)
    {
        var bits = BlockLiquationPanSmelted.Residue(stack).ToList();
        if (bits.Count == 0) return;
        double units = stack.Attributes.GetDouble(BlockLiquationPanSmelted.ResidueUnitsKey);
        dsc.AppendLine(Lang.Get("seraphhorizons:liquation-residue-units", units.ToString("0.#", CultureInfo.InvariantCulture)));
        foreach (var (code, count) in bits)
        {
            var item = world.GetItem(new AssetLocation(code));
            dsc.AppendLine(Lang.Get("seraphhorizons:cupel-bead-gives", count, item != null ? new ItemStack(item).GetName() : code));
        }
    }

    public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
        if (inSlot.Itemstack is { } stack)
            AppendResidue(stack, dsc, world);
    }

    public override void OnConsumedByCrafting(ItemSlot[] allInputSlots, ItemSlot stackInSlot, IRecipeBase recipe,
        IRecipeIngredient fromIngredient, IPlayer byPlayer, int quantity)
    {
        var pan = stackInSlot.Itemstack?.Clone();
        base.OnConsumedByCrafting(allInputSlots, stackInSlot, recipe, fromIngredient, byPlayer, quantity);
        if (pan != null)
            OreContainers.Give(byPlayer, BlockLiquationPanSmelted.Residue(pan));
    }
}
