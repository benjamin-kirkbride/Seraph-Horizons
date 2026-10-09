using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.CrucibleFurnace;

/// <summary>
/// A melting pot of molten metal pulled from a hole (<c>meltingpot-smelted</c>): the game's smelted
/// crucible (<see cref="BlockSmeltedContainer"/>), so it pours into any <see cref="ILiquidMetalSink"/>
/// (ingot molds, tool molds, the gear blank molds) exactly as the crucible does. What it adds: the pot
/// keeps its heats when it is emptied and cracks after its last; and once the metal has frozen (the
/// game's own test: below 0.9 of its melting point, reached at the end of the pour window the pot was
/// given) a right-click breaks the pot, which is lost, and knocks the metal out as bits, a bit for every
/// 5 units.
/// </summary>
public class BlockMeltingPotSmelted : BlockSmeltedContainer
{
    private static readonly AssetLocation CrackSound = new("game", "sounds/block/ceramicbreak");

    public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handHandling)
    {
        if (slot.Itemstack is not { } stack)
            return;
        var contents = GetContents(byEntity.World, stack);
        if (contents.Key == null)
        {
            if (byEntity.World.Side == EnumAppSide.Server)
                Empty(slot, byEntity, stack);
            handHandling = EnumHandHandling.PreventDefault;
            return;
        }
        if (HasSolidifed(stack, contents.Key, byEntity.World))
        {
            if (byEntity.World.Side == EnumAppSide.Server)
                KnockOut(slot, byEntity, contents.Key, contents.Value);
            handHandling = EnumHandHandling.PreventDefault;
            return;
        }
        base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handHandling);
    }

    public override bool OnHeldInteractStep(float secondsUsed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel)
    {
        var before = slot.Itemstack;
        bool more = base.OnHeldInteractStep(secondsUsed, slot, byEntity, blockSel, entitySel);
        // The game swaps in the emptied pot (emptiedBlockCode) once the last unit is poured.
        if (before != null && slot.Itemstack != before && slot.Itemstack?.Collectible is BlockMeltingPot)
        {
            float temperature = before.Collectible.GetTemperature(byEntity.World, before);
            slot.Itemstack = null;
            Empty(slot, byEntity, before, temperature);
        }
        return more;
    }

    /// <summary>The pot, emptied: back to a fired pot with its heats, or cracked after its last.</summary>
    private void Empty(ItemSlot slot, EntityAgent byEntity, ItemStack pot, float? temperature = null)
    {
        int heats = PotStack.Heats(pot);
        var config = CrucibleFurnaceSystem.Of(byEntity.Api).Config;
        if (heats >= config.PotHeats)
        {
            slot.Itemstack = null;
            slot.MarkDirty();
            byEntity.World.PlaySoundAt(CrackSound, byEntity, null, true, 16);
            Tell(byEntity, "meltingpot-cracked");
            return;
        }
        var fired = new ItemStack(byEntity.World.GetBlock(new AssetLocation(CrucibleFurnaceSystem.FiredPot)));
        PotStack.SetHeats(fired, heats);
        if (temperature is { } t)
            fired.Collectible.SetTemperature(byEntity.World, fired, t);
        slot.Itemstack = fired;
        slot.MarkDirty();
    }

    /// <summary>The metal froze in the pot: the pot is broken, the metal knocked out as bits.</summary>
    private void KnockOut(ItemSlot slot, EntityAgent byEntity, ItemStack metal, int units)
    {
        slot.Itemstack = null;
        slot.MarkDirty();
        byEntity.World.PlaySoundAt(CrackSound, byEntity, null, true, 16);
        int bits = units / 5;
        string? metalCode = metal.Collectible.Variant["metal"];
        if (bits > 0 && metalCode != null
                     && byEntity.World.GetItem(new AssetLocation("game", "metalbit-" + metalCode)) is { } bit)
        {
            var stack = new ItemStack(bit, bits);
            if (!byEntity.TryGiveItemStack(stack))
                byEntity.World.SpawnItemEntity(stack, byEntity.Pos.XYZ);
        }
        Tell(byEntity, "meltingpot-frozen", bits);
    }

    private static void Tell(EntityAgent byEntity, string key, params object[] args)
    {
        if ((byEntity as EntityPlayer)?.Player is IServerPlayer sp)
            sp.SendIngameError(key, Lang.GetL(sp.LanguageCode, CrucibleFurnaceSystem.Domain + ":" + key, args));
    }

    public override string GetHeldItemName(ItemStack itemStack)
    {
        var contents = GetContents(api.World, itemStack);
        string? metal = contents.Key?.Collectible?.Variant["metal"];
        string name = metal != null ? Lang.Get("material-" + metal) : contents.Key?.GetName() ?? "";
        return HasSolidifed(itemStack, contents.Key, api.World)
            ? Lang.Get(CrucibleFurnaceSystem.Domain + ":meltingpot-smelted-solid", name)
            : Lang.Get(CrucibleFurnaceSystem.Domain + ":meltingpot-smelted-molten", name);
    }

    public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
        if (inSlot.Itemstack is not { } stack)
            return;
        var contents = GetContents(world, stack);
        if (contents.Key != null && HasSolidifed(stack, contents.Key, world))
            dsc.AppendLine(Lang.Get(CrucibleFurnaceSystem.Domain + ":meltingpot-frozen-info", contents.Value / 5));
        dsc.AppendLine(PotStack.HeatsLine(PotStack.Heats(stack), CrucibleFurnaceSystem.Of(world.Api).Config.PotHeats));
    }
}
