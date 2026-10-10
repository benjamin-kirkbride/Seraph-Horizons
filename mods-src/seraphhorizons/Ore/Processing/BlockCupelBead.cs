using System.Globalization;
using System.Text;
using SeraphHorizons.Mod.Ore.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

namespace SeraphHorizons.Mod.Ore.Processing;

/// <summary>
/// A cupel done in the forge, <c>seraphhorizons:cupel-bead</c> (#722): the silver bead on the spent
/// cupel, which is litharge. It holds what it breaks into as whole items, decided when the cupel was
/// done (<see cref="Cupellation.Whole"/>). Broken with a hammer in the grid
/// (<c>recipes/grid/cupel.json</c>), its litharge is the recipe's output (its count set by
/// <see cref="ItemOreProduct.OnCreatedByCrafting"/>) and the bead's metal bits (silver, and copper
/// from tetrahedrite or freibergite) go to the player (<see cref="OnConsumedByCrafting"/>).
/// </summary>
public class BlockCupelBead : Block
{
    public const string LithargeKey = "cupelLitharge";
    public const string BitsKey = "cupelBits";
    public const string SilverUnitsKey = "cupelSilverUnits";

    /// <summary>The metal bit a metal of the bead comes out as (5 units, as a nugget).</summary>
    public static string BitCode(string metal) => "game:metalbit-" + metal;

    /// <summary>The bead for a yield, its whole items drawn from <paramref name="rand"/>; at least one
    /// litharge (a charge always holds lead).</summary>
    public static ItemStack? Make(IWorldAccessor world, Block cupel, CupelYield yield, Random rand)
    {
        if (world.GetBlock(cupel.CodeWithVariant("type", "bead")) is not { Id: > 0 } block) return null;
        var stack = new ItemStack(block);
        stack.Attributes.SetInt(LithargeKey, Math.Max(1, Cupellation.Whole(yield.LeadUnits, Cupellation.UnitsPerItem, rand.NextDouble())));
        var bits = stack.Attributes.GetOrAddTreeAttribute(BitsKey);
        foreach (var m in yield.Metals)
            bits.SetInt(BitCode(m.Metal), Cupellation.Whole(m.Units, Cupellation.UnitsPerItem, rand.NextDouble()));
        stack.Attributes.SetDouble(SilverUnitsKey, yield.UnitsOf("silver"));
        return stack;
    }

    public static int Litharge(ItemStack bead) => bead.Attributes.GetInt(LithargeKey);

    /// <summary>The metal bits the bead breaks into, by item code.</summary>
    public static IEnumerable<(string Code, int Count)> Bits(ItemStack bead)
    {
        if (bead.Attributes.GetTreeAttribute(BitsKey) is not { } tree) yield break;
        foreach (var (code, value) in tree)
            if (value is IntAttribute { value: > 0 } n)
                yield return (code, n.value);
    }

    public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
        if (inSlot.Itemstack is not { } bead) return;
        double silver = bead.Attributes.GetDouble(SilverUnitsKey);
        dsc.AppendLine(Lang.Get("seraphhorizons:cupel-bead-silver", silver.ToString("0.#", CultureInfo.InvariantCulture)));
        foreach (var (code, count) in Bits(bead))
        {
            var item = world.GetItem(new AssetLocation(code));
            dsc.AppendLine(Lang.Get("seraphhorizons:cupel-bead-gives", count, item != null ? new ItemStack(item).GetName() : code));
        }
        var litharge = world.GetItem(new AssetLocation(OreProducts.LithargeCode));
        dsc.AppendLine(Lang.Get("seraphhorizons:cupel-bead-gives", Litharge(bead), litharge != null ? new ItemStack(litharge).GetName() : "litharge"));
    }

    public override void OnConsumedByCrafting(ItemSlot[] allInputSlots, ItemSlot stackInSlot, IRecipeBase recipe,
        IRecipeIngredient fromIngredient, IPlayer byPlayer, int quantity)
    {
        var bead = stackInSlot.Itemstack?.Clone();
        base.OnConsumedByCrafting(allInputSlots, stackInSlot, recipe, fromIngredient, byPlayer, quantity);
        if (bead == null || byPlayer?.Entity?.World is not { } world) return;
        var give = Bits(bead).ToList();
        // Litharge past one stack (a cupel holding more than the output can) comes this way too.
        if (world.GetItem(new AssetLocation(OreProducts.LithargeCode)) is { } litharge && Litharge(bead) > litharge.MaxStackSize)
            give.Add((OreProducts.LithargeCode, Litharge(bead) - litharge.MaxStackSize));
        OreContainers.Give(byPlayer, give);
    }
}
