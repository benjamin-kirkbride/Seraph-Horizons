using System.Text;
using SeraphHorizons.Mod.Ore.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace SeraphHorizons.Mod.Ore.Processing;

/// <summary>
/// Ore processing's own items (#686, #847): the rock forms by ore and grade, crushed ore, ground ore
/// and roasted rock (<c>seraphhorizons:{crushedore,groundore,roastedore,stallroastedore}-{ore}-{grade}</c>), and
/// concentrate, roasted concentrate, amalgam, sponge (<c>seraphhorizons:{form}-{ore}</c>) and
/// litharge. The form is the item type's <c>oreForm</c> attribute; the name is the form's with the
/// ore's (vanilla's <c>ore-{ore}</c>) and a rock form's grade, and the text says the metal it holds
/// (<c>metalUnits</c>) and one terse status line (<see cref="StatusText"/>); how the form is worked is
/// in its handbook section (<see cref="OreHandbook"/>). Smelting is set on the server by
/// <see cref="OreProcessingSystem"/>.
///
/// A sulfide's concentrate cooks in the firepit into its roasted concentrate (#720): its smelting
/// points there, and <see cref="DoSmelt"/> gives the firepit's share of it (<see cref="RoastShare"/>),
/// the fraction carried over to the next item (<see cref="OreProcessingSystem.RoastCarry"/>).
/// </summary>
public class ItemOreProduct : Item
{
    public string Form => Attributes?["oreForm"].AsString("") ?? "";

    public string? Ore => Variant["ore"];

    /// <summary>The share of its units a roast keeps (the firepit's, 85 %), set on the server for a
    /// sulfide's concentrate (<see cref="OreProcessingItems"/>); 0 for every other item, which smelts
    /// as the game smelts it.</summary>
    public double RoastShare { get; internal set; }

    /// <summary>The units of metal the item holds (its <c>metalUnits</c>).</summary>
    public static double UnitsOf(CollectibleObject item) => item.Attributes?["metalUnits"].AsDouble(0) ?? 0;

    /// <summary>
    /// The game's smelting, but for a sulfide's concentrate roasting: one item in, and out only the
    /// roasted items its <see cref="RoastShare"/> of units makes with what this firepit held over for
    /// that output, so a run of 20 gives 17 and nothing rounds away. Only the firepit calls this on its
    /// own (the crucible refuses an item that needs no container), on the server.
    /// </summary>
    public override void DoSmelt(IWorldAccessor world, ISlotProvider cookingSlotsProvider, ItemSlot inputSlot, ItemSlot outputSlot)
    {
        if (!(RoastShare > 0) || world.Api is not { Side: EnumAppSide.Server } api)
        {
            base.DoSmelt(world, cookingSlotsProvider, inputSlot, outputSlot);
            return;
        }
        if (inputSlot.Itemstack is not { } input || !CanSmelt(world, cookingSlotsProvider, input, outputSlot.Itemstack)
            || CombustibleProps?.SmeltedStack?.ResolvedItemstack is not { } roasted)
            return;
        double unitsOut = UnitsOf(roasted.Collectible);
        string key = ((cookingSlotsProvider as InventoryBase)?.InventoryID ?? "elsewhere") + "|" + roasted.Collectible.Code;
        int count = unitsOut > 0
            ? OreRoasting.Roast(OreProcessingSystem.Of(api).RoastCarry, key, UnitsOf(this), RoastShare, unitsOut)
            : 1;
        if (count > 0)
        {
            var made = roasted.Clone();
            made.StackSize = count;
            if (outputSlot.Itemstack == null)
                outputSlot.Itemstack = made;
            else
                // CanSmelt made sure the output takes the item (a run never makes two at once).
                outputSlot.Itemstack.StackSize += count;
        }
        input.StackSize -= 1;
        if (input.StackSize <= 0)
            inputSlot.Itemstack = null;
        inputSlot.MarkDirty();
        outputSlot.MarkDirty();
    }

    /// <summary>The metal the item holds: the ore's metal group, lead for litharge.</summary>
    public string? Metal => Form is "litharge" or "richlead" ? "lead" : Form == "bullion" ? Variant["metal"] : Ore is { } ore ? OreMetals.MetalOf(ore) : null;

    public override string GetHeldItemName(ItemStack itemStack)
    {
        string form = Form;
        if (form.Length == 0)
            return base.GetHeldItemName(itemStack);
        if (Ore is not { } ore)
            return Lang.Get("seraphhorizons:oreproduct-" + form);
        // Bullion is named by its kind (silver-lead, tin-lead, doré ...), with the ore it came from.
        if (form == "bullion")
            return Lang.Get("seraphhorizons:oreproduct-bullion-" + ore, Lang.Get("ore-" + ore));
        return Variant["grade"] is { } grade
            ? Lang.Get("seraphhorizons:oreproduct-" + form, Lang.Get("ore-" + ore), Lang.Get("seraphhorizons:oreprocessing-grade-" + grade))
            : Lang.Get("seraphhorizons:oreproduct-" + form, Lang.Get("ore-" + ore));
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

    // One terse status line under the units: what the item does in a furnace, or where it
    // goes next. How each form is worked is in its handbook page's section and the ore processing guide
    // (OreHandbook), never here: the handbook shows this same text.
    public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
        double units = Attributes?["metalUnits"].AsDouble(0) ?? 0;
        if (units > 0 && Metal is { } metal)
            dsc.AppendLine(Lang.Get("seraphhorizons:oreproduct-units", units.ToString("0.#"), Lang.Get("material-" + metal)));
        if (StatusText() is { } status)
            dsc.AppendLine(status);
    }

    /// <summary>The form's status line; null for an item with no form.</summary>
    public string? StatusText() => Form switch
    {
        "" => null,
        "crushed" or "ground" or "roastedrock" or "stallroastedrock" or "concentrate" => SmeltText(this),
        "roastedconcentrate" or "sponge" => Lang.Get("seraphhorizons:oreproduct-smeltswhole"),
        "amalgam" => Lang.Get("seraphhorizons:oreproduct-retortfirst"),
        "litharge" => Lang.Get("seraphhorizons:oreproduct-smeltstolead"),
        // Bullion names the machine that parts its kind (the type's partedBy, by ore); rich lead the
        // cupellation furnace.
        "bullion" or "richlead" => Lang.Get("seraphhorizons:oreproduct-partin-" + (Attributes?["partedBy"].AsString(null) ?? "cupellation")),
        _ => null,
    };

    /// <summary>
    /// What an ore item's smelting (as the server set it) does: <c>Smelts at {share} %</c>, a sulfide's
    /// <c>Roast first</c> (it roasts in the firepit, no container), <c>Smelts whole</c> for a concentrate
    /// that smelts at all of its units, or <c>Does not smelt</c> (a sulfide's ground ore).
    /// </summary>
    public static string SmeltText(CollectibleObject item)
    {
        var props = item.CombustibleProps;
        if (props?.SmeltedStack == null)
            return Lang.Get("seraphhorizons:oreproduct-nosmelt");
        if (!props.RequiresContainer)
            return Lang.Get("seraphhorizons:oreproduct-roastfirst", OreRoasting.MeltingPoint);
        double units = UnitsOf(item);
        // the smelted units a piece gives, over the units it holds
        double share = units > 0 ? 100.0 * props.SmeltedStack.StackSize / props.SmeltedRatio / units : 0;
        if (item is ItemOreProduct { Form: "concentrate" } && share >= 0.9995)
            return Lang.Get("seraphhorizons:oreproduct-smeltswhole");
        return Lang.Get("seraphhorizons:oreproduct-smeltsat", (share * 100).ToString("0.#"));
    }
}
