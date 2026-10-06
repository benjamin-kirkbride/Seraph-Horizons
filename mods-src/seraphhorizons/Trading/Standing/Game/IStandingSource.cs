using SeraphHorizons.Mod.Trading.Standing.Core;
using Vintagestory.API.Common;

namespace SeraphHorizons.Mod.Trading.Standing;

/// <summary>Which way goods go in a deal, for a price factor.</summary>
public enum PriceSide
{
    /// <summary>The player buys the trader's goods (the trader's selling slots).</summary>
    PlayerBuys,
    /// <summary>The player sells goods to the trader (its buying slots).</summary>
    PlayerSells,
}

/// <summary>
/// What the trading features ask of standing (#452, #463), set on <see cref="TradingSystem.Standing"/>
/// by <see cref="StandingSystem"/> (server) when <c>TraderStanding</c> is on; <see cref="NoStanding"/>
/// otherwise. Everything a trader reads (tier, unlocks, price factor) is the effective standing:
/// max(personal, company) plus spillover from nearby traders of the same type.
/// </summary>
public interface IStandingSource
{
    bool Enabled { get; }

    /// <summary>The trader's standing id (<see cref="TraderIds"/>): its camp cell, else its entity.</summary>
    string TraderIdOf(EntitySeraphTrader trader);

    /// <summary>The player's tier index at the trader (0 = the first tier).</summary>
    int TierFor(IPlayer player, EntitySeraphTrader trader);

    /// <summary>What the player's tier at the trader unlocks.</summary>
    TierUnlocks UnlocksFor(IPlayer player, EntitySeraphTrader trader);

    /// <summary>The factor standing puts on a price for this player (pricing multiplies by it).</summary>
    double PriceFactorFor(IPlayer player, EntitySeraphTrader trader, PriceSide side);

    /// <summary>The wallet tier (index into the list's <c>wallet</c>) the trader restocks to: the best
    /// tier among players who traded with it recently.</summary>
    int WalletTierFor(EntitySeraphTrader trader);

    /// <summary>A deal went through at the trader (the game's trade packet).</summary>
    void OnDeal(IPlayer player, EntitySeraphTrader trader, int gearsPaid, int gearsReceived);

    /// <summary>The player opened the trader's trade dialog (server).</summary>
    void OnTradeOpened(IPlayer player, EntitySeraphTrader trader);

    // Orders and deliveries (wave 3, #456, #457) report here; trader ids from TraderIdOf, as the
    // traders need not be loaded then, and player uids, as the player may be offline.

    void OnOrderDone(string playerUid, string traderId);

    /// <summary>On time (<paramref name="bothEnds"/>): standing at the sender and the receiver; late,
    /// at the receiver only.</summary>
    void OnDeliveryDone(string playerUid, string fromTraderId, string toTraderId, bool bothEnds);

    /// <summary>Standing with the sender goes, from the player who took the job and their company.</summary>
    void OnDeliveryFailed(string playerUid, string fromTraderId);

    void OnOrderAbandoned(string playerUid, string traderId);
}

/// <summary>Standing off: everyone is a stranger everywhere.</summary>
public sealed class NoStanding : IStandingSource
{
    public static readonly NoStanding Instance = new();
    public bool Enabled => false;
    public string TraderIdOf(EntitySeraphTrader trader) => TraderIds.Entity(trader.EntityId);
    public int TierFor(IPlayer player, EntitySeraphTrader trader) => 0;
    public TierUnlocks UnlocksFor(IPlayer player, EntitySeraphTrader trader) => new();
    public double PriceFactorFor(IPlayer player, EntitySeraphTrader trader, PriceSide side) => 1;
    public int WalletTierFor(EntitySeraphTrader trader) => 0;
    public void OnDeal(IPlayer player, EntitySeraphTrader trader, int gearsPaid, int gearsReceived) { }
    public void OnTradeOpened(IPlayer player, EntitySeraphTrader trader) { }
    public void OnOrderDone(string playerUid, string traderId) { }
    public void OnDeliveryDone(string playerUid, string fromTraderId, string toTraderId, bool bothEnds) { }
    public void OnDeliveryFailed(string playerUid, string fromTraderId) { }
    public void OnOrderAbandoned(string playerUid, string traderId) { }
}
