namespace SeraphHorizons.Mod.Trading.Window.Core;

/// <summary>Why the server turns a request from the trade window away before looking at it.</summary>
public enum GuardRefusal
{
    None,
    /// <summary>The trader is dead or gone.</summary>
    Gone,
    /// <summary>Somebody else is trading with it, or nobody: the window is not open for this player.</summary>
    NotTrading,
    /// <summary>The player walked away (the window closes then, as vanilla's).</summary>
    TooFar,
}

/// <summary>
/// The server's check on every trade window request: the trader is alive, the player is its trading
/// player (vanilla's <c>tradingPlayerUID</c>, one player at a time), and the player stands next to
/// it. Vanilla opens the trade at a squared distance of 7 and closes it past 5 (its own tick), so a
/// request from past 7 is from a window that is closing.
/// </summary>
public static class TradeGuard
{
    /// <summary>Vanilla's reach to open a trade, squared blocks (<c>EntityTradingHumanoid.Dialog_DialogTriggers</c>).</summary>
    public const double MaxDistanceSq = 7;

    public static GuardRefusal Check(bool traderAlive, string? tradingPlayerUid, string playerUid, double distanceSq)
    {
        if (!traderAlive) return GuardRefusal.Gone;
        if (string.IsNullOrEmpty(tradingPlayerUid) || tradingPlayerUid != playerUid) return GuardRefusal.NotTrading;
        if (distanceSq > MaxDistanceSq) return GuardRefusal.TooFar;
        return GuardRefusal.None;
    }

    /// <summary>What says the player's bags have no room for what they would buy.</summary>
    public const string NoRoomKey = "trading-window-noroom";

    /// <summary>
    /// Whether <paramref name="quantity"/> items fit in the player's bags, given what each slot can
    /// still take of them (<paramref name="room"/>: an empty slot that holds them, its stack limit; a
    /// slot whose stack they merge with, what it lacks to its limit; any other, 0). Partial room in
    /// several slots adds up, as the game spreads a stack it gives. Checked before a buy, so a trade
    /// that would not fit is refused before any gears move rather than dropped at the player's feet.
    /// </summary>
    public static bool Fits(int quantity, IEnumerable<int> room)
    {
        if (quantity <= 0) return true;
        long left = quantity;
        foreach (int r in room)
            if ((left -= Math.Max(0, r)) <= 0) return true;
        return false;
    }

    /// <summary>The lang key (mod domain) that says so.</summary>
    public static string Key(GuardRefusal refusal) => refusal switch
    {
        GuardRefusal.Gone => "trading-window-gone",
        GuardRefusal.NotTrading => "trading-window-nottrading",
        GuardRefusal.TooFar => "trading-window-toofar",
        _ => "",
    };
}
