using System.Globalization;
using System.Text;
using SeraphHorizons.Mod.Ore.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.Ore.Processing;

/// <summary>
/// A liquation pan done in the fire, <c>seraphhorizons:liquationpan-smelted</c> (#724): the game's smelted
/// crucible (<see cref="BlockSmeltedContainer"/>), so its molten tin pours into any mold as the crucible's
/// does, frozen it is warmed again in a fire, and it is never set on the ground (the stack carries the
/// lead). The lead the tin left behind stays in the pan, decided as whole metal bits when it was done
/// (<see cref="Cupellation.Whole"/>): emptied, it is a pan with lead residue
/// (<see cref="BlockLiquationResidue"/>), or with no lead (an overheated charge) a fired pan again.
/// </summary>
public class BlockLiquationPanSmelted : BlockSmeltedContainer
{
    public const string ResidueKey = "liquationResidue";
    public const string ResidueUnitsKey = "liquationResidueUnits";

    /// <summary>The smelted pan for a yield: its main metal molten, its residue as whole bits drawn from
    /// <paramref name="rand"/>.</summary>
    public static ItemStack? Make(IWorldAccessor world, Block pan, LiquationYield yield, Random rand)
    {
        if (world.GetBlock(pan.CodeWithVariant("type", "smelted")) is not BlockSmeltedContainer { Id: > 0 } block) return null;
        if (world.GetItem(new AssetLocation("game", "ingot-" + yield.Metal)) is not { } ingot) return null;
        var stack = new ItemStack(block);
        block.SetContents(stack, new ItemStack(ingot), (int)Math.Round(yield.Units));
        var bits = stack.Attributes.GetOrAddTreeAttribute(ResidueKey);
        foreach (var m in yield.Residue)
            bits.SetInt(BlockCupelBead.BitCode(m.Metal), Cupellation.Whole(m.Units, Cupellation.UnitsPerItem, rand.NextDouble()));
        stack.Attributes.SetDouble(ResidueUnitsKey, yield.Residue.Sum(m => m.Units));
        return stack;
    }

    /// <summary>The metal bits a stack's residue breaks into, by item code.</summary>
    public static IEnumerable<(string Code, int Count)> Residue(ItemStack stack)
    {
        if (stack.Attributes.GetTreeAttribute(ResidueKey) is not { } tree) yield break;
        foreach (var (code, value) in tree)
            if (value is IntAttribute { value: > 0 } n)
                yield return (code, n.value);
    }

    public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handHandling)
    {
        var before = slot.Itemstack;
        base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handHandling);
        Emptied(slot, byEntity, before);
    }

    public override bool OnHeldInteractStep(float secondsUsed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel)
    {
        var before = slot.Itemstack;
        bool more = base.OnHeldInteractStep(secondsUsed, slot, byEntity, blockSel, entitySel);
        Emptied(slot, byEntity, before);
        return more;
    }

    /// <summary>The game swaps in the emptied pan (<c>emptiedBlockCode</c>, the fired pan) once the last
    /// unit is poured; with lead left in it, it is the pan with lead residue instead.</summary>
    private static void Emptied(ItemSlot slot, EntityAgent byEntity, ItemStack? before)
    {
        if (before == null || slot.Itemstack is not { } emptied || emptied == before) return;
        slot.Itemstack = EmptiedStack(byEntity.World, before, emptied);
        slot.MarkDirty();
    }

    /// <summary>What a smelted pan becomes once poured out: the pan with its residue, or the emptied
    /// stack the game made (the fired pan) when nothing is left in it; at the poured pan's heat.</summary>
    public static ItemStack EmptiedStack(IWorldAccessor world, ItemStack before, ItemStack emptied)
    {
        if (emptied.Collectible is not BlockLiquationPan pan) return emptied;
        var stack = emptied;
        if (Residue(before).Any() && world.GetBlock(pan.CodeWithVariant("type", "residue")) is { Id: > 0 } residue)
        {
            stack = new ItemStack(residue);
            stack.Attributes[ResidueKey] = before.Attributes.GetTreeAttribute(ResidueKey).Clone();
            stack.Attributes.SetDouble(ResidueUnitsKey, before.Attributes.GetDouble(ResidueUnitsKey));
        }
        stack.Collectible.SetTemperature(world, stack, before.Collectible.GetTemperature(world, before));
        return stack;
    }

    public override string GetHeldItemName(ItemStack itemStack)
    {
        var contents = GetContents(api.World, itemStack);
        string? metal = contents.Key?.Collectible?.Variant["metal"];
        string name = metal != null ? Lang.Get("material-" + metal) : contents.Key?.GetName() ?? "";
        return HasSolidifed(itemStack, contents.Key, api.World)
            ? Lang.Get("seraphhorizons:liquationpan-smelted-solid", name)
            : Lang.Get("seraphhorizons:liquationpan-smelted-molten", name);
    }

    public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
        if (inSlot.Itemstack is { } stack)
            BlockLiquationResidue.AppendResidue(stack, dsc, world);
    }
}
