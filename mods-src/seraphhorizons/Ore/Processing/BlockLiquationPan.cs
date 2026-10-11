using System.Globalization;
using SeraphHorizons.Mod.Ore.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.Ore.Processing;

/// <summary>
/// The fired clay liquation pan, <c>seraphhorizons:liquationpan-fired</c> (#724; README "Liquation"): a
/// smelting container like the game's crucible, heated in a firepit (its four cooking slots, as the
/// crucible's) or in crucibulum's forge, which takes any <see cref="BlockSmeltingContainer"/> as a
/// crucible. Both call its smelting with the charge's slots; the charge and what it gives are
/// <see cref="Liquation"/>'s:
/// <list type="bullet">
/// <item><see cref="CanSmelt"/>: a charge <see cref="Liquation.Check"/> accepts, with nothing in the
/// firepit's output slot. No air is needed, so the forge's blast gate only changes the heat.</item>
/// <item><see cref="GetMeltingPoint"/>: the tin point (240 °C); <see cref="GetMeltingDuration"/>: the
/// config's seconds per 100 units of charge.</item>
/// <item><see cref="DoSmelt"/>: the pan becomes a pan of molten tin (<see cref="BlockLiquationPanSmelted"/>,
/// poured like the crucible), holding the lead left in it as whole bits; a charge over the lead point
/// (327 °C) as it finishes gives its tin and no lead.</item>
/// </list>
/// Its dialog line is <see cref="OutputText"/> (<see cref="ContainerText"/>).
/// </summary>
public class BlockLiquationPan : BlockSmeltingContainer, IOreContainerText
{
    private OreRecovery? Recovery => api == null ? null : OreProcessingSystem.RecoveryFor(api);

    /// <summary>What one stack of the charge is to the pan.</summary>
    public static LiquationCharge ChargeOf(OreRecovery r, ItemStack stack) =>
        OreContainers.Roasted(r, stack) is var (ore, units) && Liquation.Liquates(r.Ore(ore))
            ? new LiquationCharge(ore, units)
            : LiquationCharge.Other;

    public static List<LiquationCharge> Charge(OreRecovery r, ISlotProvider provider) =>
        provider.Slots.Where(s => s?.Itemstack != null).Select(s => ChargeOf(r, s.Itemstack)).ToList();

    public override bool CanSmelt(IWorldAccessor world, ISlotProvider cookingSlotsProvider, ItemStack inputStack, ItemStack outputStack) =>
        outputStack == null && Recovery is { } r && Liquation.Check(r, Charge(r, cookingSlotsProvider)) == LiquationRefusal.None;

    public override float GetMeltingPoint(IWorldAccessor world, ISlotProvider cookingSlotsProvider, ItemSlot inputSlot) =>
        (float)(Recovery?.Liquation.TinPoint ?? 240);

    public override float GetMeltingDuration(IWorldAccessor world, ISlotProvider cookingSlotsProvider, ItemSlot inputSlot)
    {
        if (Recovery is not { } r) return float.MaxValue;
        double units = Charge(r, cookingSlotsProvider).Where(c => !c.Foreign).Sum(c => c.Units);
        return (float)Liquation.Seconds(r.Liquation, units);
    }

    public override void DoSmelt(IWorldAccessor world, ISlotProvider cookingSlotsProvider, ItemSlot inputSlot, ItemSlot outputSlot)
    {
        if (Recovery is not { } r) return;
        var charge = Charge(r, cookingSlotsProvider);
        if (Liquation.Check(r, charge) != LiquationRefusal.None) return;
        float temperature = OreContainers.ChargeTemperature(world, cookingSlotsProvider) ?? GetTemperature(world, inputSlot.Itemstack);
        var y = Liquation.Yield(r, charge, Liquation.Overheated(r.Liquation, temperature));
        if (BlockLiquationPanSmelted.Make(world, this, y, world.Rand) is not { } smelted) return;
        smelted.Collectible.SetTemperature(world, smelted, temperature);
        outputSlot.Itemstack = smelted;
        // A firepit's input slot can hold more than one pan: the rest stay.
        if (inputSlot.Itemstack is { StackSize: > 1 } pans) pans.StackSize--;
        else inputSlot.Itemstack = null;
        foreach (var slot in cookingSlotsProvider.Slots)
            slot.Itemstack = null;
        inputSlot.MarkDirty();
        outputSlot.MarkDirty();
    }

    /// <summary>What the pan will do with its charge, for the firepit's or the forge's dialog; null with
    /// nothing in it.</summary>
    public string? OutputText(IWorldAccessor world, ISlotProvider provider)
    {
        if (Recovery is not { } r) return null;
        var charge = Charge(r, provider);
        string F(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);
        switch (Liquation.Check(r, charge))
        {
            case LiquationRefusal.Empty: return null;
            case LiquationRefusal.Foreign: return Lang.Get("seraphhorizons:liquation-foreign");
            case LiquationRefusal.TooMuch: return Lang.Get("seraphhorizons:liquation-toomuch", F(r.Liquation.CapacityUnits));
        }
        var metal = Lang.Get("material-" + Liquation.Yield(r, charge, false).Metal);
        if (OreContainers.ChargeTemperature(world, provider) is { } t && Liquation.Overheated(r.Liquation, t))
            return Lang.Get("seraphhorizons:liquation-toohot", F(r.Liquation.LeadPoint), metal);
        var y = Liquation.Yield(r, charge, false);
        var residue = string.Join(", ", y.Residue.Select(m => Lang.Get("seraphhorizons:cupel-units", F(m.Units), Lang.Get("material-" + m.Metal))));
        return Lang.Get("seraphhorizons:liquation-willpart", F(y.Units), metal, residue, F(r.Liquation.LeadPoint));
    }
}
