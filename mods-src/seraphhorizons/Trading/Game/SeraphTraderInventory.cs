using SeraphHorizons.Mod.Trading.Deliveries;
using SeraphHorizons.Mod.Trading.Economy;
using SeraphHorizons.Mod.Trading.Economy.Core;
using SeraphHorizons.Mod.Trading.Window;
using SeraphHorizons.Mod.Trading.Window.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.Trading;

/// <summary>
/// The pack's traders' inventory: vanilla's <see cref="InventoryTrader"/> (the same 41 slots, saved
/// and synced the same way, so the economy's patches and every list keep working), as the pack's
/// trade window uses it. The window has no carts: a shelf slot is never moved into the buying cart
/// by a click (<see cref="ActivateSlot"/> ignores the shelves and the buying cart), and the four
/// slots of the selling cart (<see cref="SellSlot"/> to <see cref="SellSlot"/> + 3) are the window's
/// sell slots, where the trading player puts goods to sell from (<see cref="ItemSlotSell"/>: anything
/// but money and delivery packages, whether the trader buys it or not). When the window closes, what
/// is left there goes back to the player, or at their feet, instead of onto the ground by the trader
/// (<see cref="Close"/>).
/// </summary>
public sealed class SeraphTraderInventory : InventoryTrader
{
    /// <summary>The window's first sell slot: vanilla's first selling cart slot.</summary>
    public const int SellSlot = 36;

    /// <summary>How many sell slots the window has: the whole selling cart.</summary>
    public const int SellSlots = 4;

    public SeraphTraderInventory(string className, string instanceId, ICoreAPI? api) : base(className, instanceId, api!) { }

    protected override ItemSlot NewSlot(int slotId) =>
        slotId >= SellSlot && slotId < SellSlot + SellSlots ? new ItemSlotSell(this) : base.NewSlot(slotId);

    public static bool IsSellSlot(int slotId) => slotId >= SellSlot && slotId < SellSlot + SellSlots;

    /// <summary>The sell slots, in order.</summary>
    public IEnumerable<ItemSlot> SellSlotList => Enumerable.Range(SellSlot, SellSlots).Select(i => this[i]);

    /// <summary>Server: whose goods the selling cart (the sell slots) holds: the trading player when
    /// the window opened (<see cref="EntitySeraphTrader.OnTradeOpened"/>). Kept apart from vanilla's
    /// <c>tradingPlayerUID</c>, which the walk-away tick clears before it closes the inventory: only
    /// the owner may move stacks in the sell slots, sell from them, or get them back on closing.</summary>
    public string? OwnerUid { get; set; }

    /// <summary>Whether the selling cart holds anything.</summary>
    public bool CartHoldsGoods => Enumerable.Range(0, 4).Any(i => GetSellingCartSlot(i)?.Itemstack != null);

    private bool MayUseSellSlot(IPlayer? player) =>
        player != null && (Api?.Side != EnumAppSide.Server || player.PlayerUID == OwnerUid);

    /// <summary>What may go in a sell slot: anything but money and delivery packages (which stay where
    /// they are, never sold by accident).</summary>
    public static bool MaySell(ItemStack? stack) =>
        stack?.Collectible is { } c && c is not ItemPackage && c.Attributes?["currency"].Exists != true;

    /// <summary>The trade window this player has open on a pack trader, if any: on the server only
    /// its owner's.</summary>
    public static SeraphTraderInventory? OpenFor(IPlayer? player)
    {
        if (player?.InventoryManager is not { } manager) return null;
        foreach (var inv in manager.OpenedInventories)
            if (inv is SeraphTraderInventory own && own.MayUseSellSlot(player)) return own;
        return null;
    }

    public override object? ActivateSlot(int slotId, ItemSlot mouseSlot, ref ItemStackMoveOperation op)
    {
        // The sell slots move stacks as any slot does (vanilla checks the trading player first, the
        // server the owner too; a shift-click out of them never lands in the creative black hole);
        // the shelves are read only and the carts are the server's (EntitySeraphTrader.BuyUnit and SellLot).
        if (!IsSellSlot(slotId) || !MayUseSellSlot(op.ActingPlayer)) return null;
        using var _ = op.ShiftDown ? ShiftClick.KeepOutOfBlackHole() : null;
        return base.ActivateSlot(slotId, mouseSlot, ref op);
    }

    /// <summary>Shift-click from the player's inventory: the first free sell slot, for the trading
    /// player (the owner on the server), anything but money and packages.</summary>
    public override WeightedSlot GetBestSuitedSlot(ItemSlot sourceSlot, ItemStackMoveOperation op, List<ItemSlot>? skipSlots = null)
    {
        var none = new WeightedSlot();
        var player = op?.ActingPlayer;
        if (PutLocked || sourceSlot?.Itemstack is not { } stack || sourceSlot.Inventory == this || player is null) return none;
        if (!MayUseSellSlot(player) || !MaySell(stack)) return none;
        if (TraderEntity?.WatchedAttributes.GetString("tradingPlayerUID") is { } uid && uid != player.PlayerUID) return none;
        foreach (var slot in SellSlotList)
            if (slot.Empty && skipSlots?.Contains(slot) != true && slot.CanHold(sourceSlot))
                return new WeightedSlot { slot = slot, weight = 100 };
        return none;
    }

    /// <summary>A shift-click on a stack in the player's hotbar or backpack while the window is open
    /// (<see cref="ShiftClick"/>): the whole stack into the first free sell slot, or, for money, a
    /// package, or with every sell slot taken, nothing moves.</summary>
    public int ShiftIn(ItemSlot source, ref ItemStackMoveOperation op)
    {
        if (GetBestSuitedSlot(source, op).slot is not { } target) return 0;
        int before = source.StackSize;
        op.RequestedQuantity = before;
        source.TryPutInto(target, ref op);
        int moved = before - source.StackSize;
        if (moved > 0)
        {
            source.MarkDirty();
            target.MarkDirty();
        }
        return moved;
    }

    private static readonly HarmonyLib.AccessTools.FieldRef<InventoryTrader, EntityTradingHumanoid> TraderField =
        HarmonyLib.AccessTools.FieldRefAccess<InventoryTrader, EntityTradingHumanoid>("traderEntity");

    private EntityTradingHumanoid? TraderEntity => TraderField(this);

    // ---- Selling ----

    /// <summary>Why a stack in a sell slot does not sell: null when the trader buys it.</summary>
    public string? RefusalOf(ItemStack stack)
    {
        if (!MaySell(stack)) return "trading-economy-refused-currency";
        var condition = GetBuyingConditionsSlot(stack);
        if (condition?.TradeItem?.Stack is null) return "trading-window-pays-none";
        if (condition is not OffListSlot && condition.TradeItem.Stock <= 0) return "trading-window-selected-nodemand";
        return null;
    }

    /// <summary>The sell slots' goods the trader buys, for the pooled sale (<see cref="SellPool"/>):
    /// each with its offer, its budget and its demand (a listed good's buying slot's stock).</summary>
    public List<SellLine> SellLines()
    {
        var lines = new List<SellLine>();
        foreach (int id in Enumerable.Range(SellSlot, SellSlots))
        {
            if (this[id].Itemstack is not { } stack || !MaySell(stack)) continue;
            var condition = GetBuyingConditionsSlot(stack);
            if (condition?.TradeItem is not { Stack: { } unit } item) continue;
            bool off = condition is OffListSlot;
            lines.Add(new SellLine(id, stack.StackSize, Math.Max(1, unit.StackSize), item.Price, off ? Budget.Side : Budget.Main,
                off ? "off:" + stack.Collectible.Code : "slot:" + GetSlotId(condition), off ? null : item.Stock));
        }
        return lines;
    }

    // ---- Closing and giving back ----

    public override object Close(IPlayer player)
    {
        if (Api?.Side != EnumAppSide.Server) return base.Close(player);
        if (player != null && player.PlayerUID == OwnerUid)
        {
            // The owner closes: what is left goes back to them, or at their feet.
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

    /// <summary>Hands the selling cart to <paramref name="player"/>: into their inventory, what does
    /// not fit at their feet. Nothing is lost (in creative not into its black hole either).</summary>
    public void GiveBack(IPlayer player)
    {
        if (player.Entity is null) return;
        for (int i = 0; i < 4; i++)
        {
            var slot = GetSellingCartSlot(i);
            if (slot?.Itemstack is not { } stack) continue;
            slot.Itemstack = null;
            slot.MarkDirty();
            GiveOrDrop(player, stack);
        }
    }

    /// <summary>Gives <paramref name="stack"/> to the player's own inventory (never the creative black
    /// hole), the rest dropped at their feet.</summary>
    public static void GiveOrDrop(IPlayer player, ItemStack stack)
    {
        if (player.Entity is not { } entity || stack.StackSize <= 0) return;
        using (ShiftClick.KeepOutOfBlackHole())
        {
            int max = Math.Max(1, stack.Collectible.MaxStackSize);
            while (stack.StackSize > 0)
            {
                var part = stack.Clone();
                part.StackSize = Math.Min(max, stack.StackSize);
                stack.StackSize -= part.StackSize;
                entity.TryGiveItemStack(part);
                if (part.StackSize > 0) entity.World.SpawnItemEntity(part, entity.Pos.XYZ);
            }
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

/// <summary>
/// One of the trade window's four sell slots (vanilla's selling cart slots, which take only what
/// the trader buys): it takes anything but money and delivery packages, so a good the trader does
/// not buy can go in and the window says so; a bag only empty, as vanilla's.
/// </summary>
public sealed class ItemSlotSell(InventoryBase inventory) : ItemSlotSurvival(inventory)
{
    public override bool CanHold(ItemSlot sourceSlot)
    {
        if (!base.CanHold(sourceSlot) || !SeraphTraderInventory.MaySell(sourceSlot.Itemstack)) return false;
        var bag = sourceSlot.Itemstack!.Collectible.GetCollectibleInterface<IHeldBag>();
        return bag is null || bag.IsEmpty(sourceSlot.Itemstack);
    }
}
