using System.Globalization;
using SeraphHorizons.Mod.Trading.Commands;
using SeraphHorizons.Mod.Trading.Deliveries.Core;
using SeraphHorizons.Mod.Trading.Orders;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace SeraphHorizons.Mod.Trading.Deliveries;

/// <summary>
/// Delivery commands (#454), by chat command rather than dialogue as orders are
/// (<see cref="OrderCommands"/>).
/// <list type="bullet">
/// <item><c>/sh delivery</c> (any player, next to one of the pack's traders): your deliveries and
/// this trader's offer; <c>/sh delivery accept</c> takes the offer; <c>/sh delivery handin</c>
/// hands over your package for this trader.</item>
/// <item><c>/sh trade deliveries [player]</c> (controlserver): active deliveries, or a player's;
/// <c>deliveries create &lt;from&gt; &lt;to&gt; &lt;player&gt;</c> (trader ids or <c>near</c>; the player
/// online, the deposit from their gears); <c>deliveries complete &lt;id&gt;</c> (handed in on time),
/// <c>fail &lt;id&gt;</c> (fails now), <c>expire &lt;id&gt;</c> (the deadline is now: late until the
/// grace runs out).</item>
/// </list>
/// </summary>
public static class DeliveryCommands
{
    private static string L(string key, params object[] args) => Lang.Get("seraphhorizons:" + key, args);

    public static void Register(ICoreServerAPI api, DeliveriesSystem system)
    {
        var sh = api.ChatCommands.GetOrCreate("sh");
        if (string.IsNullOrEmpty(sh.Description)) sh.WithDescription("Seraph Horizons commands").RequiresPrivilege(Privilege.controlserver);
        sh.BeginSubCommand("delivery")
                .WithDescription("Deliveries: delivery (yours, and the offer of the trader next to you), delivery accept, delivery handin")
                .RequiresPrivilege(Privilege.chat)
                .RequiresPlayer()
                .IgnoreAdditionalArgs()
                .HandleWith(args => Here(api, system, args))
                .BeginSubCommand("accept")
                    .WithDescription("Take the delivery the trader next to you offers")
                    .RequiresPrivilege(Privilege.chat)
                    .RequiresPlayer()
                    .HandleWith(args => Accept(api, system, args))
                .EndSubCommand()
                .BeginSubCommand("handin")
                    .WithDescription("Hand your package to the trader next to you, its receiver")
                    .RequiresPrivilege(Privilege.chat)
                    .RequiresPlayer()
                    .HandleWith(args => HandIn(api, system, args))
                .EndSubCommand()
            .EndSubCommand();
        var parsers = api.ChatCommands.Parsers;
        var trade = TradeCommands.Trade ?? sh.BeginSubCommand("trade").WithDescription("Traders").RequiresPrivilege(Privilege.controlserver);
        trade.BeginSubCommand("deliveries")
                .WithDescription("Deliveries: deliveries [player], create <from> <to> <player>, complete|fail|expire <id>")
                .RequiresPrivilege(Privilege.controlserver)
                .WithArgs(parsers.OptionalWord("player|create|complete|fail|expire"), parsers.OptionalAll("arguments"))
                .HandleWith(args => Admin(api, system, args))
            .EndSubCommand();
    }

    private static TextCommandResult Here(ICoreServerAPI api, DeliveriesSystem system, TextCommandCallingArgs args)
    {
        var player = args.Caller.Player;
        var lines = system.Book.All.Where(d => d.IsActive && d.PlayerUid == player.PlayerUID).Select(system.Line).ToList();
        if (TraderFinder.Nearest(api, args.Caller.Entity.Pos.XYZ) is { } trader)
        {
            var (offer, why) = system.OfferFor(player, trader);
            lines.Add(offer is null ? L(why!) : system.OfferLine(offer) + " " + L("trading-deliveries-offer-how"));
        }
        return TextCommandResult.Success(lines.Count == 0 ? L("trading-deliveries-none") : string.Join("\n", lines));
    }

    private static TextCommandResult Accept(ICoreServerAPI api, DeliveriesSystem system, TextCommandCallingArgs args)
    {
        if (TraderFinder.Nearest(api, args.Caller.Entity.Pos.XYZ) is not { } trader) return TextCommandResult.Error(L("trading-orders-notrader"));
        var player = (IServerPlayer)args.Caller.Player;
        var (offer, why) = system.OfferFor(player, trader);
        if (offer is null) return TextCommandResult.Error(L(why!));
        if (system.Begin(player, offer, out var d) is { } error) return TextCommandResult.Error(L(error, offer.Deposit));
        return TextCommandResult.Success(L("trading-deliveries-accepted", system.Line(d!)));
    }

    private static TextCommandResult HandIn(ICoreServerAPI api, DeliveriesSystem system, TextCommandCallingArgs args)
    {
        if (TraderFinder.Nearest(api, args.Caller.Entity.Pos.XYZ) is not { } trader) return TextCommandResult.Error(L("trading-orders-notrader"));
        return system.HandIn((IServerPlayer)args.Caller.Player, trader) is { } error ? TextCommandResult.Error(L(error)) : TextCommandResult.Success();
    }

    // ---- Admin ----

    private static TextCommandResult Admin(ICoreServerAPI api, DeliveriesSystem system, TextCommandCallingArgs args)
    {
        string? word = args[0] as string;
        var rest = ((args[1] as string) ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        double today = api.World.Calendar.TotalDays;
        switch (word)
        {
            case "create":
                return Create(api, system, args, rest);
            case "complete" or "fail" or "expire":
            {
                if (rest.Length < 1 || !int.TryParse(rest[0], out int id)) return TextCommandResult.Error(L("trading-deliveries-badid", rest.FirstOrDefault() ?? ""));
                if (word == "expire")
                    return system.Book.Expire(id, today) is { } d
                        ? TextCommandResult.Success(L("trading-deliveries-admin-expire", id))
                        : TextCommandResult.Error(L("trading-deliveries-notactive", id));
                var change = word == "complete" ? system.Book.Complete(id, today) : system.Book.Fail(id, today);
                if (change is null) return TextCommandResult.Error(L("trading-deliveries-notactive", id));
                // A completed delivery's package is handed in wherever it is in the player's inventory.
                if (word == "complete" && api.World.PlayerByUid(change.Delivery.PlayerUid) is { } p)
                    foreach (var slot in DeliveriesSystem.PackageSlots(p, id).ToList())
                    {
                        slot.TakeOut(1);
                        slot.MarkDirty();
                    }
                system.Settle(change, null);
                return TextCommandResult.Success(L("trading-deliveries-admin-" + word, id));
            }
        }
        IEnumerable<Delivery> list;
        if (string.IsNullOrEmpty(word)) list = system.Book.All.Where(d => d.IsActive);
        else
        {
            var data = api.PlayerData.GetPlayerDataByLastKnownName(word);
            if (data is null) return TextCommandResult.Error(L("trading-orders-noplayer", word));
            list = system.Book.OfPlayer(data.PlayerUID);
        }
        var rows = list.ToList();
        var lines = new List<string> { L("trading-deliveries-admin-header", rows.Count) };
        lines.AddRange(rows.Select(d => AdminLine(d, today)));
        return TextCommandResult.Success(string.Join("\n", lines));
    }

    public static string AdminLine(Delivery d, double today) =>
        string.Format(CultureInfo.InvariantCulture, "#{0} {1} {2} ({3}) → {4} ({5}) {6:0} m, {7}, deposit {8}, fee {9}, {10}",
            d.Id, d.State.ToString().ToLowerInvariant(), d.From, d.FromType, d.To, d.ToType, d.Distance, d.PlayerName, d.Deposit, d.Fee,
            d.IsActive
                ? string.Format(CultureInfo.InvariantCulture, "due in {0:0.##} d (grace to {1:0.##} d)", d.Deadline - today, d.GraceUntil - today)
                : string.Format(CultureInfo.InvariantCulture, "closed {0:0.#} d ago", today - (d.ClosedDay ?? today)));

    private static TextCommandResult Create(ICoreServerAPI api, DeliveriesSystem system, TextCommandCallingArgs args, string[] rest)
    {
        if (rest.Length < 3) return TextCommandResult.Error(L("trading-deliveries-create-usage"));
        var from = rest[0] == "near" ? TraderFinder.Named(api, "near", args.Caller.Entity) is { } t ? system.SiteOf(t) : null : system.SiteOf(rest[0]);
        var to = rest[1] == "near" ? TraderFinder.Named(api, "near", args.Caller.Entity) is { } u ? system.SiteOf(u) : null : system.SiteOf(rest[1]);
        if (from is null || to is null || from.Value.Id == to.Value.Id) return TextCommandResult.Error(L("trading-deliveries-create-traders", rest[0], rest[1]));
        if (api.World.AllOnlinePlayers.FirstOrDefault(p => p.PlayerName.Equals(rest[2], StringComparison.OrdinalIgnoreCase)) is not IServerPlayer player)
            return TextCommandResult.Error(L("trading-orders-noplayer", rest[2]));
        var rolls = Enumerable.Range(0, 3).Select(_ => api.World.Rand.NextDouble()).ToArray();
        var offer = DeliveryPlanner.Offer(from.Value, to.Value, 1, system.GameDaysPerRealMinute, rolls);
        if (system.Begin(player, offer, out var d) is { } error) return TextCommandResult.Error(L(error, offer.Deposit));
        return TextCommandResult.Success(L("trading-deliveries-created", AdminLine(d!, api.World.Calendar.TotalDays)));
    }
}
