using System.Globalization;
using SeraphHorizons.Mod.Ore.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.Ore.Processing;

/// <summary>
/// The fired bone-ash cupel, <c>seraphhorizons:cupel-fired</c> (#722; README "Cupellation"): a
/// smelting container like the game's crucible, set in crucibulum's forge, which takes it as a crucible
/// (<see cref="CupelForge"/>) and calls its smelting. The charge and what it gives are
/// <see cref="Cupellation"/>'s; here they are read off the stacks:
/// <list type="bullet">
/// <item><see cref="CanSmelt"/>: a charge <see cref="Cupellation.Check"/> accepts, in a forge whose
/// blast gate is not shut. Anywhere else (a firepit) it refuses: cupellation wants the forge's blast.</item>
/// <item><see cref="GetMeltingPoint"/>: the config's heat (950 °C), which the forge's crucible bonus
/// reaches through the gate open, half or a quarter; <see cref="GetMeltingDuration"/>: the config's
/// seconds per 100 units of charge, divided by the gate's air.</item>
/// <item><see cref="DoSmelt"/>: the charge and the cupel become one "cupel with silver bead"
/// (<see cref="BlockCupelBead"/>), holding the whole items it breaks into.</item>
/// </list>
/// What the forge's dialog says it will make is <see cref="OutputText"/> (<see cref="ContainerText"/>, a
/// postfix on the game's <c>BlockSmeltingContainer.GetOutputText</c>, which is not virtual).
/// </summary>
public class BlockCupel : BlockSmeltingContainer, IOreContainerText
{
    private OreRecovery? Recovery => api == null ? null : OreProcessingSystem.RecoveryFor(api);

    /// <summary>What one stack of the charge is to the cupel.</summary>
    public static CupelCharge ChargeOf(OreRecovery r, ItemStack stack)
    {
        var code = stack.Collectible.Code;
        if (OreContainers.Roasted(r, stack) is var (ore, units))
            return Cupellation.Cupels(r.Ore(ore)) ? new CupelCharge(ore, units) : CupelCharge.Other;
        // Litharge is lead spent in a cupel already; it smelts back to lead, not into another cupel.
        if (code.ToString() == OreProducts.LithargeCode)
            return CupelCharge.Other;
        var props = stack.Collectible.CombustibleProps;
        if (props?.SmeltedStack?.ResolvedItemstack?.Collectible?.Code?.ToString() == "game:ingot-lead" && props.SmeltedRatio > 0)
            return CupelCharge.Lead(stack.StackSize * 100.0 * props.SmeltedStack.StackSize / props.SmeltedRatio);
        return CupelCharge.Other;
    }

    public static List<CupelCharge> Charge(OreRecovery r, ISlotProvider provider) =>
        provider.Slots.Where(s => s?.Itemstack != null).Select(s => ChargeOf(r, s.Itemstack)).ToList();

    public override bool CanSmelt(IWorldAccessor world, ISlotProvider cookingSlotsProvider, ItemStack inputStack, ItemStack outputStack) =>
        Recovery is { } r && CupelForge.Air(cookingSlotsProvider) is > 0
        && Cupellation.Check(r, Charge(r, cookingSlotsProvider)) == CupelRefusal.None;

    public override float GetMeltingPoint(IWorldAccessor world, ISlotProvider cookingSlotsProvider, ItemSlot inputSlot) =>
        (float)(Recovery?.Cupel.MeltingPoint ?? 950);

    public override float GetMeltingDuration(IWorldAccessor world, ISlotProvider cookingSlotsProvider, ItemSlot inputSlot)
    {
        if (Recovery is not { } r) return float.MaxValue;
        double units = Charge(r, cookingSlotsProvider).Where(c => !c.Foreign).Sum(c => c.Units);
        return Cupellation.Seconds(r.Cupel, units, CupelForge.Air(cookingSlotsProvider) ?? 0) is { } s ? (float)s : float.MaxValue;
    }

    public override void DoSmelt(IWorldAccessor world, ISlotProvider cookingSlotsProvider, ItemSlot inputSlot, ItemSlot outputSlot)
    {
        if (Recovery is not { } r) return;
        var charge = Charge(r, cookingSlotsProvider);
        if (Cupellation.Check(r, charge) != CupelRefusal.None) return;
        if (BlockCupelBead.Make(world, this, Cupellation.Yield(r, charge), world.Rand) is not { } bead) return;
        bead.Collectible.SetTemperature(world, bead, GetTemperature(world, inputSlot.Itemstack));
        outputSlot.Itemstack = bead;
        inputSlot.Itemstack = null;
        foreach (var slot in cookingSlotsProvider.Slots)
            slot.Itemstack = null;
    }

    /// <summary>What the cupel will do with its charge where it is, for the forge's (or the firepit's)
    /// dialog; null with nothing in it.</summary>
    public string? OutputText(IWorldAccessor world, ISlotProvider provider)
    {
        if (Recovery is not { } r) return null;
        var charge = Charge(r, provider);
        string F(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);
        switch (Cupellation.Check(r, charge))
        {
            case CupelRefusal.Empty: return null;
            case CupelRefusal.Foreign: return Lang.Get("seraphhorizons:cupel-foreign");
            case CupelRefusal.NothingToPart: return Lang.Get("seraphhorizons:cupel-nothingtopart");
            case CupelRefusal.TooMuch: return Lang.Get("seraphhorizons:cupel-toomuch", F(r.Cupel.CapacityUnits));
            case CupelRefusal.TooLittleLead: return Lang.Get("seraphhorizons:cupel-toolittlelead", F(r.Cupel.LeadPerOreUnit));
        }
        switch (CupelForge.Air(provider))
        {
            case null: return Lang.Get("seraphhorizons:cupel-notforge");
            case <= 0: return Lang.Get("seraphhorizons:cupel-gateshut");
        }
        var y = Cupellation.Yield(r, charge);
        var metals = string.Join(", ", y.Metals.Select(m => Lang.Get("seraphhorizons:cupel-units", F(m.Units), Lang.Get("material-" + m.Metal))));
        return Lang.Get("seraphhorizons:cupel-willpart", metals, F(y.LeadUnits));
    }
}
