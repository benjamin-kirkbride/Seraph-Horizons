using SeraphHorizons.Mod.Trading.Economy;
using SeraphHorizons.Mod.Trading.Economy.Core;
using SeraphHorizons.Mod.Trading.Standing;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;

namespace SeraphHorizons.Mod.Trading.Glue;

/// <summary>
/// Standing in prices (#452 into #450): the economy's <see cref="IPriceModifier"/> for the player
/// trading. Vanilla lets one player at a time trade with a trader (<c>tradingPlayerUID</c>), so the
/// shelf is priced for that player: the server writes their two factors into the trader's watched
/// attributes (<see cref="Attr"/>: <c>uid</c>, <c>buy</c>, <c>sell</c>) and re-prices, and this
/// modifier, on both sides, reads them back. Nobody trading (or someone else), factor 1. The client
/// thus quotes off-list goods exactly as the server will pay them.
/// </summary>
public sealed class StandingPriceModifier(ICoreAPI api) : IPriceModifier
{
    public const string Attr = "seraphhorizons:standingprice";
    public const string TradingPlayerAttr = "tradingPlayerUID";

    public string Name => "standing";

    public double Factor(in PriceContext context)
    {
        if (api.World.GetEntityById(context.TraderId) is not EntitySeraphTrader trader) return 1;
        if (trader.WatchedAttributes[Attr] is not ITreeAttribute tree) return 1;
        string? uid = context.PlayerUid ?? trader.WatchedAttributes.GetString(TradingPlayerAttr);
        if (uid is null || tree.GetString("uid") != uid) return 1;
        // The trader buying from the player is the player selling.
        return context.TraderBuys ? tree.GetDouble("sell", 1) : tree.GetDouble("buy", 1);
    }
}

/// <summary>
/// Glue between the wave-2 trading features built in parallel (standing #452, economy #450/#451):
/// <list type="bullet">
/// <item>registers <see cref="StandingPriceModifier"/> with the economy on both sides, and on the
/// server keeps each trader's <see cref="StandingPriceModifier.Attr"/> on its trading player: set
/// when the dialog opens (<see cref="EntitySeraphTrader.TradeOpened"/>), checked every second
/// (a tier reached in a deal, the dialog closed or walked away from, which vanilla clears in
/// several places), re-pricing the shelf and sending it when it changes
/// (<see cref="TradingPlayerPriced"/> for anything else priced per player, the maps);</item>
/// <item><c>/sh trade simulate</c> ages standing's "traded recently" records by the simulated days
/// (<see cref="Standing.Core.StandingLedger.Age"/>), as it moves the restock clocks.</item>
/// </list>
/// </summary>
public class TradingGlueSystem : ModSystem
{
    private ICoreServerAPI? _sapi;
    private long _tick;

    /// <summary>Raised on the server when a trader's trading player (null: nobody) or their factors
    /// change, before the shelf is re-priced and sent: offers priced per player (the maps) follow.</summary>
    public static event Action<EntitySeraphTrader, IPlayer?>? TradingPlayerPriced;

    public override double ExecuteOrder() => 0.67;

    public override void StartClientSide(ICoreClientAPI api) => EconomySystem.Of(api)?.Modifiers.Add(new StandingPriceModifier(api));

    public override void StartServerSide(ICoreServerAPI api)
    {
        _sapi = api;
        if (EconomySystem.Of(api) is { } economy)
        {
            economy.Modifiers.Add(new StandingPriceModifier(api));
            economy.SimulatedDay += _ => StandingSystem.Of(api)?.Ledger.Age(1);
        }
        EntitySeraphTrader.TradeOpened += OnTradeOpened;
        _tick = api.Event.RegisterGameTickListener(_ => SyncAll(), 1000);
    }

    public override void Dispose()
    {
        EntitySeraphTrader.TradeOpened -= OnTradeOpened;
        if (_sapi != null && _tick != 0) _sapi.Event.UnregisterGameTickListener(_tick);
    }

    private void OnTradeOpened(EntitySeraphTrader trader, IPlayer player)
    {
        if (trader.Api == _sapi) Sync(trader);
    }

    private void SyncAll()
    {
        foreach (var e in _sapi!.World.LoadedEntities.Values)
            if (e is EntitySeraphTrader t && (t.WatchedAttributes.HasAttribute(StandingPriceModifier.Attr)
                                               || t.WatchedAttributes.HasAttribute(StandingPriceModifier.TradingPlayerAttr)))
                Sync(t);
    }

    /// <summary>Brings the trader's standing prices in line with its trading player; re-prices and
    /// sends the shelf if anything changed.</summary>
    public void Sync(EntitySeraphTrader trader)
    {
        var api = _sapi!;
        var attrs = trader.WatchedAttributes;
        string? uid = attrs.GetString(StandingPriceModifier.TradingPlayerAttr);
        var player = uid is null ? null : api.World.PlayerByUid(uid);
        var old = attrs[StandingPriceModifier.Attr] as ITreeAttribute;
        string? oldUid = old?.GetString("uid");
        double buy = 1, sell = 1;
        if (player != null && TradingSystem.Of(api)?.Standing is { Enabled: true } standing)
        {
            buy = standing.PriceFactorFor(player, trader, PriceSide.PlayerBuys);
            sell = standing.PriceFactorFor(player, trader, PriceSide.PlayerSells);
        }
        bool playerChanged = oldUid != (player?.PlayerUID);
        if (!playerChanged && old != null && old.GetDouble("buy", 1).Equals(buy) && old.GetDouble("sell", 1).Equals(sell)) return;
        if (player is null)
            attrs.RemoveAttribute(StandingPriceModifier.Attr);
        else
        {
            var tree = new TreeAttribute();
            tree.SetString("uid", player.PlayerUID);
            tree.SetDouble("buy", buy);
            tree.SetDouble("sell", sell);
            attrs[StandingPriceModifier.Attr] = tree;
        }
        attrs.MarkPathDirty(StandingPriceModifier.Attr);
        TradingPlayerPriced?.Invoke(trader, player);
        Reprice(api, trader);
    }

    /// <summary>The economy re-prices the shelf from its entries and sends it; without the economy
    /// pricing (both its switches off) the inventory is still sent, for what else changed.</summary>
    public static void Reprice(ICoreServerAPI api, EntitySeraphTrader trader)
    {
        if (EconomySystem.Of(api) is { } economy && (economy.EverythingHasAPrice || economy.RegionalSupply))
        {
            economy.Refresh(trader, broadcast: true);
            return;
        }
        var store = new TreeAttribute();
        trader.Inventory.ToTreeAttributes(store);
        trader.WatchedAttributes["traderInventory"] = store;
        trader.WatchedAttributes.MarkPathDirty("traderInventory");
        api.Network.BroadcastEntityPacket(trader.EntityId, 1234, store.ToBytes());
    }
}
