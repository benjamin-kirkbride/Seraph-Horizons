using System.Text;
using SeraphHorizons.Mod.CrucibleFurnace.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

namespace SeraphHorizons.Mod.CrucibleFurnace;

/// <summary>What a melting pot stack carries: the heats it has been through and, when it was pulled
/// before its charge melted, the charge.</summary>
public static class PotStack
{
    public const string HeatsKey = "seraphhorizonsPotHeats";
    public const string ChargeKey = "seraphhorizonsPotCharge";

    public static int Heats(ItemStack? stack) => stack?.Attributes.GetInt(HeatsKey) ?? 0;

    public static void SetHeats(ItemStack stack, int heats)
    {
        if (heats > 0)
            stack.Attributes.SetInt(HeatsKey, heats);
        else
            stack.Attributes.RemoveAttribute(HeatsKey);
    }

    public static List<ChargeItem> Charge(ItemStack? stack)
    {
        if (stack?.Attributes[ChargeKey] is not ITreeAttribute tree)
            return [];
        return tree.Select(e => new ChargeItem(e.Key, (e.Value as IntAttribute)?.value ?? 0)).Where(i => i.Count > 0).ToList();
    }

    public static void SetCharge(ItemStack stack, IReadOnlyList<ChargeItem> charge)
    {
        stack.Attributes.RemoveAttribute(ChargeKey);
        if (charge.Count == 0)
            return;
        var tree = new TreeAttribute();
        foreach (var item in charge)
            tree.SetInt(item.Code, item.Count);
        stack.Attributes[ChargeKey] = tree;
    }

    /// <summary>The charge as a line: "12x Iron bits, 1x Iron ingot".</summary>
    public static string Describe(IWorldAccessor world, IEnumerable<ChargeItem> charge) =>
        string.Join(", ", charge.Select(i =>
            $"{i.Count}x {(CrucibleFurnaceSystem.Resolve(world, i.Code) is { } c ? new ItemStack(c).GetName() : i.Code)}"));

    /// <summary>The heats line of a pot's tooltip and block info.</summary>
    public static string HeatsLine(int heats, int of) =>
        Lang.Get(CrucibleFurnaceSystem.Domain + ":meltingpot-heats", heats, of);
}

/// <summary>
/// The fired melting pot (<c>meltingpot-fired</c>): goes into a melting hole, where it is charged and
/// fired. Its tooltip gives its heats and any charge it carries (a pot pulled before its charge
/// melted keeps it, and takes it back into a hole).
/// </summary>
public class BlockMeltingPot : Block
{
    public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
        if (api == null)
            return;
        var config = CrucibleFurnaceSystem.Of(api).Config;
        dsc.AppendLine(PotStack.HeatsLine(PotStack.Heats(inSlot.Itemstack), config.PotHeats));
        var charge = PotStack.Charge(inSlot.Itemstack);
        if (charge.Count > 0)
            dsc.AppendLine(Lang.Get(CrucibleFurnaceSystem.Domain + ":meltingpot-charge", PotStack.Describe(world, charge)));
    }
}
