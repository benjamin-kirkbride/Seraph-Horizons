using System.Globalization;
using SeraphHorizons.Mod.Trading.Commands;
using SeraphHorizons.Mod.Trading.Economy.Core;
using SeraphHorizons.Mod.Trading.Orders.Core;
using SeraphHorizons.Mod.Trading.Values;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace SeraphHorizons.Mod.Trading.Orders;

/// <summary>
/// Order admin commands (#453). Players take and hand in orders in the trade window's Orders tab
/// (<c>Trading/Window</c>); these are the admin's.
/// <list type="bullet">
/// <item><c>/sh trade orders [trader|player]</c> (controlserver): open orders, all, a trader's (id
/// or <c>near</c>) or a player's; <c>orders create &lt;trader&gt; &lt;item&gt; &lt;qty&gt; &lt;days&gt;</c>
/// puts one on offer; <c>orders complete|cancel &lt;id&gt;</c>.</item>
/// </list>
/// The game parses a command's arguments before its subcommands, so the admin node reads its
/// subcommand from the first argument (as <c>/sh trade standing</c> does).
/// </summary>
public static class OrderCommands
{
    private static string L(string key, params object[] args) => Lang.Get("seraphhorizons:" + key, args);

    public static void Register(ICoreServerAPI api, OrdersSystem system)
    {
        var parsers = api.ChatCommands.Parsers;
        var sh = api.ChatCommands.GetOrCreate("sh");
        if (string.IsNullOrEmpty(sh.Description)) sh.WithDescription("Seraph Horizons commands").RequiresPrivilege(Privilege.controlserver);
        var trade = TradeCommands.Trade ?? sh.BeginSubCommand("trade").WithDescription("Traders").RequiresPrivilege(Privilege.controlserver);
        trade.BeginSubCommand("orders")
                .WithDescription("Orders: orders [trader|player], create <trader> <item> <qty> <days>, complete <id>, cancel <id>")
                .RequiresPrivilege(Privilege.controlserver)
                .WithArgs(parsers.OptionalWord("trader|player|create|complete|cancel"), parsers.OptionalAll("arguments"))
                .HandleWith(args => Admin(api, system, args))
            .EndSubCommand();
    }

    // ---- Admin ----

    private static TextCommandResult Admin(ICoreServerAPI api, OrdersSystem system, TextCommandCallingArgs args)
    {
        string? word = args[0] as string;
        var rest = ((args[1] as string) ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        double today = api.World.Calendar.TotalDays;
        switch (word)
        {
            case "create":
                return Create(api, system, args, rest);
            case "complete" or "cancel":
            {
                if (rest.Length < 1 || !int.TryParse(rest[0], out int id)) return TextCommandResult.Error(L("trading-orders-badid", rest.FirstOrDefault() ?? ""));
                var change = word == "complete" ? system.Book.Complete(id, today) : system.Book.Cancel(id, today);
                if (change is null) return TextCommandResult.Error(L(word == "complete" ? "trading-orders-notaccepted" : "trading-orders-notopen", id));
                if (word == "complete")
                    system.Settle(change, change.Order.PlayerUid is { } uid ? api.World.PlayerByUid(uid) as IServerPlayer : null);
                else
                    system.Close(change);
                return TextCommandResult.Success(L("trading-orders-admin-" + word, id));
            }
        }
        IEnumerable<Order> orders;
        if (string.IsNullOrEmpty(word)) orders = system.Book.All.Where(o => o.IsOpen);
        else if (word == "near" || word.StartsWith("camp:", StringComparison.Ordinal) || word.StartsWith("entity:", StringComparison.Ordinal))
        {
            string id = word == "near" ? TraderFinder.Named(api, word, args.Caller.Entity) is { } t ? TraderFinder.IdOf(api, t) : "" : word;
            if (id.Length == 0) return TextCommandResult.Error(L("trading-orders-notrader"));
            orders = system.Book.All.Where(o => o.TraderId == id);
        }
        else
        {
            var data = api.PlayerData.GetPlayerDataByLastKnownName(word);
            if (data is null) return TextCommandResult.Error(L("trading-orders-noplayer", word));
            orders = system.Book.OfPlayer(data.PlayerUID);
        }
        var list = orders.ToList();
        var lines = new List<string> { L("trading-orders-admin-header", list.Count) };
        lines.AddRange(list.Select(o => AdminLine(api, o, today)));
        return TextCommandResult.Success(string.Join("\n", lines));
    }

    public static string AdminLine(ICoreServerAPI api, Order o, double today) =>
        string.Format(CultureInfo.InvariantCulture, "#{0} {1} {2} ({3}) {4}/{5} {6} @{7:0.##} ×{8:0.##}, premium {9} (paid {10}), {11} {12:0.#} d{13}",
            o.Id, o.State.ToString().ToLowerInvariant(), o.TraderId, o.TraderType, o.Delivered, o.Quantity,
            TraderFinder.ItemName(api.World, o.Item), o.UnitPrice, o.PremiumFactor, o.Premium, o.PremiumPaid,
            o.IsOpen ? "due in" : "closed", o.IsOpen ? o.Deadline - today : today - (o.ClosedDay ?? today),
            o.PlayerName is { } n ? ", " + n : "");

    private static TextCommandResult Create(ICoreServerAPI api, OrdersSystem system, TextCommandCallingArgs args, string[] rest)
    {
        if (rest.Length < 4 || !int.TryParse(rest[2], out int qty) || qty < 1
            || !double.TryParse(rest[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double days) || days <= 0)
            return TextCommandResult.Error(L("trading-orders-create-usage"));
        if (TraderFinder.Named(api, rest[0], args.Caller.Entity) is not { } trader) return TextCommandResult.Error(L("trading-orders-notrader"));
        string code = BuyerIndex.FullCode(rest[1].ToLowerInvariant());
        if (TraderFinder.Collectible(api.World, code) is not { } collectible) return TextCommandResult.Error(L("trading-orders-noitem", rest[1]));
        // The trader's own price if its list buys the item, else the value table's.
        var listed = system.Candidates(trader).FirstOrDefault(c => c.Item == code);
        double unit = listed?.UnitPrice ?? ItemValuesSystem.For(api).ValueOf(code);
        if (unit <= 0) return TextCommandResult.Error(L("trading-orders-noprice", rest[1]));
        var candidate = new OrderCandidate(code, unit, listed?.Lot ?? 1, Math.Max(1, collectible.MaxStackSize));
        string id = TraderFinder.IdOf(api, trader);
        var o = system.MakeOffer(trader, id, candidate, qty, OrderPlanner.Factor(api.World.Rand.NextDouble()), days);
        if (o is null) return TextCommandResult.Error(L("trading-orders-create-broke", OrderPlanner.Premium(qty, unit, OrderPlanner.MaxFactor)));
        return TextCommandResult.Success(L("trading-orders-created", AdminLine(api, o, api.World.Calendar.TotalDays)));
    }
}
