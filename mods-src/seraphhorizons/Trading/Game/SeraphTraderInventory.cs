using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.Trading;

/// <summary>
/// The pack's traders' inventory: vanilla's <see cref="InventoryTrader"/> (the same 41 slots, saved
/// and synced the same way, so the economy's patches and every list keep working), as the pack's
/// trade window uses it. The window has no carts: a shelf slot is never moved into the buying cart
/// by a click (<see cref="ActivateSlot"/> ignores the shelves and the carts), and the first slot of
/// the selling cart, <see cref="SellSlot"/>, is the window's sell slot, where the trading player puts
/// a stack to sell from. When the window closes, what is left there goes back to the player instead
/// of onto the ground (<see cref="Close"/>).
/// </summary>
public sealed class SeraphTraderInventory : InventoryTrader
{
    /// <summary>The window's sell slot: vanilla's first selling cart slot.</summary>
    public const int SellSlot = 36;

    public SeraphTraderInventory(string className, string instanceId, ICoreAPI? api) : base(className, instanceId, api!) { }

    public override object? ActivateSlot(int slotId, ItemSlot mouseSlot, ref ItemStackMoveOperation op)
    {
        // The sell slot moves stacks as any slot does (vanilla checks the trading player first);
        // the shelves are read only and the carts are the server's
        // (EntitySeraphTrader.BuyUnit and SellUnit).
        return slotId == SellSlot ? base.ActivateSlot(slotId, mouseSlot, ref op) : null;
    }

    public override object Close(IPlayer player)
    {
        if (Api?.Side == EnumAppSide.Server && player?.Entity is { } entity)
            for (int i = 0; i < 4; i++)
            {
                var slot = GetSellingCartSlot(i);
                if (slot?.Itemstack is not { } stack) continue;
                if (!entity.TryGiveItemStack(stack)) continue;
                slot.Itemstack = stack.StackSize > 0 ? stack : null;
                slot.MarkDirty();
            }
        return base.Close(player);
    }
}
