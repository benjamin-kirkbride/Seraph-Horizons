using System.Text;
using SeraphHorizons.Mod.Ore.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace SeraphHorizons.Mod.Ore.Processing;

/// <summary>
/// Ore processing's own items (#686): crushed ore (<c>game:crushed-{ore}-{grain}</c>), ground ore,
/// concentrate, roasted concentrate, amalgam (<c>seraphhorizons:{form}-{ore}</c>) and litharge. The
/// form is the item type's <c>oreForm</c> attribute; the name is the form's with the ore's
/// (vanilla's <c>ore-{ore}</c>), and the text says the metal it holds (<c>metalUnits</c>) and what
/// the form is for. Smelting is set on the server by <see cref="OreProcessingSystem"/>.
/// </summary>
public class ItemOreProduct : Item
{
    public string Form => Attributes?["oreForm"].AsString("") ?? "";

    public string? Ore => Variant["ore"];

    /// <summary>The metal the item holds: the ore's metal group, lead for litharge.</summary>
    public string? Metal => Form == "litharge" ? "lead" : Ore is { } ore ? OreMetals.MetalOf(ore) : null;

    public override string GetHeldItemName(ItemStack itemStack)
    {
        string form = Form;
        if (form.Length == 0)
            return base.GetHeldItemName(itemStack);
        if (Ore is not { } ore)
            return Lang.Get("seraphhorizons:oreproduct-" + form);
        string key = form == "crushed" ? $"seraphhorizons:oreproduct-crushed-{Variant["grain"]}" : "seraphhorizons:oreproduct-" + form;
        return Lang.Get(key, Lang.Get("ore-" + ore));
    }

    /// <summary>Litharge out of a broken cupel (<see cref="BlockCupelBead"/>): as many as the cupel
    /// holds, up to a stack (the rest goes to the player with the bead's bits).</summary>
    public override void OnCreatedByCrafting(ItemSlot[] allInputSlots, ItemSlot outputSlot, IRecipeBase byRecipe)
    {
        base.OnCreatedByCrafting(allInputSlots, outputSlot, byRecipe);
        if (Form != "litharge" || outputSlot.Itemstack is not { } output) return;
        foreach (var slot in allInputSlots)
            if (slot.Itemstack is { Collectible: BlockCupelBead } bead)
                output.StackSize = Math.Clamp(BlockCupelBead.Litharge(bead), 1, MaxStackSize);
    }

    public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
        double units = Attributes?["metalUnits"].AsDouble(0) ?? 0;
        if (units > 0 && Metal is { } metal)
            dsc.AppendLine(Lang.Get("seraphhorizons:oreproduct-units", units.ToString("0.#"), Lang.Get("material-" + metal)));
        string form = Form;
        // A sulfide's concentrate has no smelting (the server sets it): it is roasted first.
        if (form == "concentrate" && CombustibleProps?.SmeltedStack == null)
            form = "concentrate-sulfide";
        if (form.Length > 0)
            dsc.AppendLine(Lang.Get("seraphhorizons:oreproduct-" + form + "-info"));
    }
}
