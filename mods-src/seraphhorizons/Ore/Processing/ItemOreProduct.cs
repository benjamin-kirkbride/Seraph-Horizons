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

    public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
        double units = Attributes?["metalUnits"].AsDouble(0) ?? 0;
        if (units > 0 && Metal is { } metal)
            dsc.AppendLine(Lang.Get("seraphhorizons:oreproduct-units", units.ToString("0.#"), Lang.Get("material-" + metal)));
        string form = Form;
        // A sulfide's concentrate does not smelt: it roasts in the firepit into roasted concentrate
        // (the server sets it).
        if (form == "concentrate" && CombustibleProps?.SmeltedStack?.Code?.Path.StartsWith("roastedconcentrate-") == true)
            form = "concentrate-sulfide";
        if (form.Length > 0)
            dsc.AppendLine(Lang.Get("seraphhorizons:oreproduct-" + form + "-info"));
    }
}
