using HarmonyLib;
using SeraphHorizons.Mod.Trading.Economy.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.Trading.Economy;

/// <summary>A trader's offer for goods off its list: a buying slot that is not in the inventory,
/// made on demand by <see cref="EconomyPatches"/> for <c>GetBuyingConditionsSlot</c>.</summary>
public sealed class OffListSlot : ItemSlotTrade
{
    public Offer Offer { get; }

    public OffListSlot(InventoryBase inventory, Offer offer, ItemStack stack) : base(inventory, isBuyingSlot: true)
    {
        Offer = offer;
        var unit = stack.Clone();
        unit.StackSize = offer.UnitSize;
        // Stock never limits an off-list offer: the side budget does.
        SetTradeItem(new ResolvedTradeItem { Stack = unit, Price = offer.UnitPrice, Stock = 9999 });
    }
}

/// <summary>
/// The Harmony patches of the economy (#450, #451), on vanilla's <c>InventoryTrader</c> and its
/// selling-cart slot (the pack's traders trade through the pack's own window, which shows the side
/// budget itself, so vanilla's dialog is no longer patched). Every patch acts only on an inventory
/// whose trader is an <see cref="EntitySeraphTrader"/> with <see cref="EconomySystem.PricedAttr"/> set (but the
/// own-shelf rule, on any of ours), so vanilla's and other mods' traders are untouched and the
/// server's switch decides for its clients.
///
/// <list type="bullet">
/// <item><c>InventoryTrader.GetBuyingConditionsSlot</c> (postfix): where the list has no buying
/// slot for a stack, or the trader has the good on its own selling shelf, an
/// <see cref="OffListSlot"/> with the computed offer (on any of our traders, priced or not, a
/// listed slot for a good on its own shelf is dropped). That one method decides
/// everything about a sale in vanilla (<c>IsTraderInterestedIn</c>, so the selling cart's
/// <c>CanHold</c> and shift-click; <c>HasTraderEnoughDemand</c>; <c>GetTotalGain</c>; the deal), so
/// an off-list good is sold exactly like a listed one.</item>
/// <item><c>InventoryTrader.TryBuySell</c> (internal; prefix and postfix): the side budget. The
/// prefix totals what off-list goods earn; more than the side budget refuses the deal, else (server)
/// that much moves from the side budget into the money slot for vanilla's own payment to take, and
/// back if the deal fails (or throws: a finalizer). The postfix (server) records supply for everything sold and bought and
/// re-prices the region's traders.</item>
/// <item><c>ItemSlot.GetStackDescription</c> (postfix, selling-cart slots only, which is the trade
/// window's sell slot): the price breakdown and which budget pays.</item>
/// <item><c>ItemSlotBuying.CanHold</c> (postfix, client): why a refused good is refused, as an
/// in-game error.</item>
/// </list>
/// </summary>
public static class EconomyPatches
{
    public const string HarmonyId = "seraphhorizons.economy";

    private static readonly AccessTools.FieldRef<InventoryTrader, EntityTradingHumanoid> TraderField =
        AccessTools.FieldRefAccess<InventoryTrader, EntityTradingHumanoid>("traderEntity");
    [ThreadStatic] private static long s_lastRefusalMs;

    public static void Patch(Harmony harmony)
    {
        var inv = typeof(InventoryTrader);
        harmony.Patch(AccessTools.Method(inv, nameof(InventoryTrader.GetBuyingConditionsSlot)),
            postfix: new HarmonyMethod(typeof(EconomyPatches), nameof(BuyingConditionsPostfix)));
        harmony.Patch(AccessTools.Method(inv, "TryBuySell"),
            prefix: new HarmonyMethod(typeof(EconomyPatches), nameof(TryBuySellPrefix)),
            postfix: new HarmonyMethod(typeof(EconomyPatches), nameof(TryBuySellPostfix)),
            finalizer: new HarmonyMethod(typeof(EconomyPatches), nameof(TryBuySellFinalizer)));
        harmony.Patch(AccessTools.Method(typeof(ItemSlot), nameof(ItemSlot.GetStackDescription)),
            postfix: new HarmonyMethod(typeof(EconomyPatches), nameof(StackDescriptionPostfix)));
        harmony.Patch(AccessTools.Method(typeof(ItemSlotBuying), nameof(ItemSlotBuying.CanHold)),
            postfix: new HarmonyMethod(typeof(EconomyPatches), nameof(CanHoldPostfix)));
    }

    /// <summary>Our trader behind an inventory, when the economy prices for it.</summary>
    public static EntitySeraphTrader? PricedTrader(InventoryTrader? inventory) =>
        inventory != null && TraderField(inventory) is EntitySeraphTrader t && EconomySystem.IsPriced(t) ? t : null;

    private static string L(string key, params object[] args) => Lang.Get("seraphhorizons:" + key, args);

    // ---- Selling anything ----

    public static void BuyingConditionsPostfix(InventoryTrader __instance, ItemStack forStack, ref ItemSlotTrade? __result)
    {
        if (forStack?.Collectible is null || TraderField(__instance) is not EntitySeraphTrader trader) return;
        // A listed good the trader has on its own selling shelf is never bought at its list's price:
        // it is bought back off-market instead, or, without the economy's side budget, not at all.
        bool ownShelf = EconomySystem.OnOwnShelf(trader, forStack.Collectible);
        if (__result != null && !ownShelf) return;
        __result = null;
        if (!EconomySystem.IsPriced(trader)) return;
        if (EconomySystem.Of(trader.Api) is not { } economy) return;
        var offer = economy.QuoteOffList(trader, forStack);
        if (!offer.Accepted || !forStack.Collectible.IsReasonablyFresh(trader.World, forStack)) return;
        __result = new OffListSlot(__instance, offer, forStack);
    }

    /// <summary>One line of a deal: what, how many items, at what per item, from which budget.</summary>
    public readonly record struct DealLine(CollectibleObject Collectible, int Items, double PricePerItem, bool TraderBuys);

    public sealed class DealState
    {
        public required EntitySeraphTrader Trader { get; init; }
        public int SideGain { get; init; }
        public int Moved { get; set; }
        public List<DealLine> Lines { get; } = [];
    }

    /// <summary>What the selling cart earns, split by budget, and every line of the deal.</summary>
    public static (int Main, int Side, List<DealLine> Lines) Cart(InventoryTrader inventory)
    {
        var parts = new List<(int, int, Budget)>();
        var lines = new List<DealLine>();
        for (int i = 0; i < 4; i++)
        {
            var stack = inventory.GetSellingCartSlot(i)?.Itemstack;
            if (stack is null) continue;
            var condition = inventory.GetBuyingConditionsSlot(stack);
            if (condition?.TradeItem?.Stack is not { } unit) continue;
            int units = stack.StackSize / Math.Max(1, unit.StackSize);
            if (units <= 0) continue;
            parts.Add((condition.TradeItem.Price, units, condition is OffListSlot off ? off.Offer.Budget : Budget.Main));
            lines.Add(new DealLine(stack.Collectible, units * unit.StackSize, condition.TradeItem.Price / (double)unit.StackSize, true));
        }
        for (int i = 0; i < 4; i++)
        {
            var slot = inventory.GetBuyingCartSlot(i);
            if (slot?.Itemstack is null || slot.TradeItem?.Stack is not { } unit) continue;
            lines.Add(new DealLine(slot.Itemstack.Collectible, slot.Itemstack.StackSize, slot.TradeItem.Price / (double)Math.Max(1, unit.StackSize), false));
        }
        var (main, side) = SideBudget.Split(parts);
        return (main, side, lines);
    }

    public static bool TryBuySellPrefix(InventoryTrader __instance, IPlayer buyingPlayer, ref EnumTransactionResult __result, out DealState? __state)
    {
        __state = null;
        if (PricedTrader(__instance) is not { } trader) return true;
        var (_, sideGain, lines) = Cart(__instance);
        int budget = EconomySystem.SideBudgetOf(trader);
        if (sideGain > budget)
        {
            __result = EnumTransactionResult.TraderNotEnoughAssets;
            if (buyingPlayer is IServerPlayer sp)
                sp.SendIngameError("seraphhorizons-sidebudget", L("trading-economy-sidebudget-short", sideGain, budget));
            return false;
        }
        var state = new DealState { Trader = trader, SideGain = sideGain };
        state.Lines.AddRange(lines);
        if (trader.Api.Side == EnumAppSide.Server && sideGain > 0)
        {
            __instance.GiveToTrader(sideGain);
            EconomySystem.SetSideBudget(trader, budget - sideGain);
            state.Moved = sideGain;
        }
        __state = state;
        return true;
    }

    /// <summary>A deal that threw (another mod's hook, a map's): the side budget's share moved into
    /// the money slot goes back, as for a deal that failed. The exception goes on.</summary>
    public static void TryBuySellFinalizer(InventoryTrader __instance, Exception? __exception, DealState? __state)
    {
        if (__exception is null || __state is not { Moved: > 0 } state) return;
        __instance.DeductFromTrader(state.Moved);
        EconomySystem.SetSideBudget(state.Trader, EconomySystem.SideBudgetOf(state.Trader) + state.Moved);
        state.Moved = 0;
    }

    public static void TryBuySellPostfix(InventoryTrader __instance, EnumTransactionResult __result, DealState? __state)
    {
        if (__state is null || __state.Trader.Api.Side != EnumAppSide.Server) return;
        var trader = __state.Trader;
        if (__result != EnumTransactionResult.Success)
        {
            if (__state.Moved > 0)
            {
                __instance.DeductFromTrader(__state.Moved);
                EconomySystem.SetSideBudget(trader, EconomySystem.SideBudgetOf(trader) + __state.Moved);
            }
            return;
        }
        RecordSupply(trader, __state.Lines);
    }

    /// <summary>Server: regional supply for a deal that went through (vanilla's, or the trade window's
    /// pooled sale), and the region's traders re-priced.</summary>
    public static void RecordSupply(EntitySeraphTrader trader, IEnumerable<DealLine> lines)
    {
        if (EconomySystem.Of(trader.Api) is not { RegionalSupply: true } economy) return;
        string region = EconomySystem.RegionOf(trader);
        foreach (var line in lines)
        {
            string code = line.Collectible.Code.ToString();
            double weight = economy.SupplyWeight(trader.Api, line.Collectible, line.PricePerItem);
            if (line.TraderBuys) economy.Supply.Sold(region, code, line.Items, weight);
            else economy.Supply.Bought(region, code, line.Items, weight);
            SeraphHorizons.Mod.Admin.AdminLogs.Trade?.Write("supply", $"{region} {code} {(line.TraderBuys ? "sold to" : "bought from")} {trader.TraderType} x{line.Items}: level {economy.Supply.Level(region, code):0.###}");
        }
        // The broadcast that follows the deal (vanilla's packet 1234) carries the new prices to the
        // trading player; the region's other loaded traders are sent theirs.
        foreach (var other in economy.LoadedTraders())
            if (EconomySystem.RegionOf(other) == region)
                economy.Refresh(other, broadcast: other != trader);
    }

    // ---- Showing it ----

    /// <summary>The breakdown of what the trader pays for a stack in the selling cart, and which
    /// budget it comes from.</summary>
    public static string? Describe(InventoryTrader inventory, ItemStack stack)
    {
        if (PricedTrader(inventory) is not { } trader) return null;
        var condition = inventory.GetBuyingConditionsSlot(stack);
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        if (condition is OffListSlot off)
        {
            var o = off.Offer;
            string line = L("trading-economy-offer-offlist", o.UnitPrice, o.UnitSize,
                o.Base.ToString("0.##", ci), o.Spread.ToString("0.##", ci), o.Fit.ToString("0.##", ci), o.Supply.ToString("0.##", ci));
            if (o.OwnShelf) line = L("trading-economy-offer-ownshelf", o.UnitPrice, o.UnitSize, o.Base.ToString("0.##", ci), o.Fit.ToString("0.##", ci), o.Supply.ToString("0.##", ci));
            if (Math.Abs(o.Modifiers - 1) > 1e-3) line += " " + L("trading-economy-offer-modifiers", o.Modifiers.ToString("0.##", ci));
            return o.Budget == Budget.Main ? line + "\n" + L("trading-economy-offer-mainwallet")
                : line + "\n" + L("trading-economy-offer-sidebudget", EconomySystem.SideBudgetOf(trader));
        }
        if (condition?.TradeItem is { } listed)
        {
            double supply = EconomySystem.SupplyFactor(trader, stack.Collectible.Code.ToString());
            return L("trading-economy-offer-listed", listed.Price, listed.Stack?.StackSize ?? 1, supply.ToString("0.##", ci));
        }
        var economy = EconomySystem.Of(trader.Api);
        return economy is null ? null : RefusalText(economy.QuoteOffList(trader, stack).Refusal);
    }

    public static string? RefusalText(Refusal refusal) =>
        refusal == Refusal.None ? null : L(SeraphHorizons.Mod.Trading.Window.Core.TradeWindowModel.RefusalKey(refusal));

    public static void StackDescriptionPostfix(ItemSlot __instance, ref string __result)
    {
        if (__instance is not ItemSlotBuying || __instance.Itemstack is null || __instance.Inventory is not InventoryTrader inv) return;
        if (Describe(inv, __instance.Itemstack) is { } text) __result = text + "\n\n" + __result;
    }

    public static void CanHoldPostfix(ItemSlotBuying __instance, ItemSlot itemstackFromSourceSlot, bool __result)
    {
        if (__result || itemstackFromSourceSlot?.Itemstack is not { } stack || __instance.Inventory is not InventoryTrader inv) return;
        if (inv.Api is not ICoreClientAPI capi || PricedTrader(inv) is not { } trader || EconomySystem.Of(capi) is not { } economy) return;
        if (inv.GetBuyingConditionsSlot(stack) != null) return;
        if (RefusalText(economy.QuoteOffList(trader, stack).Refusal) is not { } text) return;
        // CanHold is asked on every hover and shift-click: say it once in a while.
        long now = Environment.TickCount64;
        if (now - s_lastRefusalMs < 2000) return;
        s_lastRefusalMs = now;
        capi.TriggerIngameError(__instance, "seraphhorizons-refused", text);
    }
}
