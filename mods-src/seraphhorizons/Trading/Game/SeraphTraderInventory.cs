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

    /// <summary>Server: whose goods the selling cart (the sell slot) holds: the trading player when
    /// the window opened (<see cref="EntitySeraphTrader.OnTradeOpened"/>). Kept apart from vanilla's
    /// <c>tradingPlayerUID</c>, which the walk-away tick clears before it closes the inventory: only
    /// the owner may move stacks in the sell slot, sell from it, or get it back on closing.</summary>
    public string? OwnerUid { get; set; }

    /// <summary>Whether the selling cart holds anything.</summary>
    public bool CartHoldsGoods => Enumerable.Range(0, 4).Any(i => GetSellingCartSlot(i)?.Itemstack != null);

    private bool MayUseSellSlot(IPlayer? player) =>
        player != null && (Api?.Side != EnumAppSide.Server || player.PlayerUID == OwnerUid);

    public override object? ActivateSlot(int slotId, ItemSlot mouseSlot, ref ItemStackMoveOperation op)
    {
        // The sell slot moves stacks as any slot does (vanilla checks the trading player first, the
        // server the owner too); the shelves are read only and the carts are the server's
        // (EntitySeraphTrader.BuyUnit and SellUnit).
        return slotId == SellSlot && MayUseSellSlot(op.ActingPlayer) ? base.ActivateSlot(slotId, mouseSlot, ref op) : null;
    }

    /// <summary>Shift-click from the player's inventory: only the sell slot, only for the trading
    /// player (the owner on the server), and only goods the trader takes.</summary>
    public override WeightedSlot GetBestSuitedSlot(ItemSlot sourceSlot, ItemStackMoveOperation op, List<ItemSlot>? skipSlots = null)
    {
        var none = new WeightedSlot();
        var player = op?.ActingPlayer;
        if (PutLocked || sourceSlot?.Itemstack is not { } stack || sourceSlot.Inventory == this || player is null) return none;
        if (!MayUseSellSlot(player)) return none;
        if (TraderEntity?.WatchedAttributes.GetString("tradingPlayerUID") is { } uid && uid != player.PlayerUID) return none;
        if (stack.Collectible.Attributes?["currency"].Exists == true || !IsTraderInterestedIn(stack)) return none;
        var slot = this[SellSlot];
        if (skipSlots?.Contains(slot) == true || !slot.CanTakeFrom(sourceSlot)) return none;
        return new WeightedSlot { slot = slot, weight = GetSuitability(sourceSlot, slot, slot.Itemstack != null) };
    }

    private static readonly HarmonyLib.AccessTools.FieldRef<InventoryTrader, EntityTradingHumanoid> TraderField =
        HarmonyLib.AccessTools.FieldRefAccess<InventoryTrader, EntityTradingHumanoid>("traderEntity");

    private EntityTradingHumanoid? TraderEntity => TraderField(this);

    public override object Close(IPlayer player)
    {
        if (Api?.Side != EnumAppSide.Server) return base.Close(player);
        if (player != null && player.PlayerUID == OwnerUid)
        {
            // The owner closes: what is left goes back to them (what does not fit, vanilla drops).
            GiveBack(player);
            OwnerUid = null;
            return base.Close(player);
        }
        // Someone else closes (opened by vanilla's packet 1001): the carts are not theirs to clear or drop.
        var kept = new ItemStack?[8];
        for (int i = 0; i < 4; i++)
        {
            kept[i] = GetBuyingCartSlot(i).Itemstack;
            kept[4 + i] = GetSellingCartSlot(i).Itemstack;
            GetBuyingCartSlot(i).Itemstack = null;
            GetSellingCartSlot(i).Itemstack = null;
        }
        var result = base.Close(player!);
        for (int i = 0; i < 4; i++)
        {
            GetBuyingCartSlot(i).Itemstack = kept[i];
            GetSellingCartSlot(i).Itemstack = kept[4 + i];
        }
        return result;
    }

    /// <summary>Hands the selling cart to <paramref name="player"/> (as far as it fits).</summary>
    public void GiveBack(IPlayer player)
    {
        if (player.Entity is not { } entity) return;
        for (int i = 0; i < 4; i++)
        {
            var slot = GetSellingCartSlot(i);
            if (slot?.Itemstack is not { } stack) continue;
            entity.TryGiveItemStack(stack);
            slot.Itemstack = stack.StackSize > 0 ? stack : null;
            slot.MarkDirty();
        }
    }

    /// <summary>Server, a new trading player: goods a previous owner left in the selling cart go back
    /// to them if they are online, else on the ground by the trader; the cart is the new player's.</summary>
    public void TakeOwnership(IPlayer player, IWorldAccessor world, Vintagestory.API.MathTools.Vec3d at)
    {
        if (OwnerUid == player.PlayerUID) return;
        if (CartHoldsGoods)
        {
            if (OwnerUid != null && world.PlayerByUid(OwnerUid) is { Entity: not null } old) GiveBack(old);
            for (int i = 0; i < 4; i++)
            {
                var slot = GetSellingCartSlot(i);
                if (slot?.Itemstack is not { } stack) continue;
                world.SpawnItemEntity(stack, at);
                slot.Itemstack = null;
                slot.MarkDirty();
            }
        }
        OwnerUid = player.PlayerUID;
    }
}
