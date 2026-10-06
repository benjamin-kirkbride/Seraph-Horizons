using System.Globalization;
using SeraphHorizons.Mod.Trading.Economy.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace SeraphHorizons.Mod.Trading.Economy;

/// <summary>
/// The economy's admin commands under <c>/sh trade</c> (privilege controlserver). Supply commands
/// act on the caller's supply region (the spawn's from the console); an item is a code, with or
/// without its domain (<c>ingot-iron</c> is <c>game:ingot-iron</c>), or the held item when left out.
/// <list type="bullet">
/// <item><c>supply [item|all]</c>: one item's level and price factor, or every item's in the region.</item>
/// <item><c>supply set &lt;item&gt; &lt;level&gt;</c>, <c>supply add &lt;item&gt; &lt;amount&gt;</c>,
/// <c>supply reset [item|all]</c>, <c>supply trace &lt;item&gt;</c> (the entry's last changes).</item>
/// <item><c>price [item]</c>: what the nearest of our traders (16 blocks) offers for it, and why.</item>
/// <item><c>simulate &lt;days&gt;</c>: supply decays and spreads for that many days, and the loaded
/// traders' restock clocks move on as much (<see cref="EconomySystem.Simulate"/>).</item>
/// </list>
/// </summary>
public static class EconomyCommands
{
    public const int MaxSimulateDays = 365;

    public static void Register(ICoreServerAPI api, EconomySystem economy)
    {
        var parsers = api.ChatCommands.Parsers;
        var trade = api.ChatCommands.GetOrCreate("sh").BeginSubCommand("trade");
        // The supply node takes no parsers of its own (a parser would swallow the subcommand's name):
        // its handler reads the optional item from the raw arguments.
        trade.BeginSubCommand("supply")
                .WithDescription("Regional supply levels where you are: supply [item|all], or a subcommand")
                .RequiresPrivilege(Privilege.controlserver)
                .IgnoreAdditionalArgs()
                .HandleWith(args => Show(api, economy, args))
                .BeginSubCommand("set")
                    .WithDescription("Set an item's supply level in your region")
                    .RequiresPrivilege(Privilege.controlserver)
                    .WithArgs(parsers.Word("item"), parsers.Double("level"))
                    .HandleWith(args => Change(api, economy, args, set: true))
                .EndSubCommand()
                .BeginSubCommand("add")
                    .WithDescription("Add to an item's supply level in your region (negative drains)")
                    .RequiresPrivilege(Privilege.controlserver)
                    .WithArgs(parsers.Word("item"), parsers.Double("amount"))
                    .HandleWith(args => Change(api, economy, args, set: false))
                .EndSubCommand()
                .BeginSubCommand("reset")
                    .WithDescription("Drop an item's supply level in your region, or all of them")
                    .RequiresPrivilege(Privilege.controlserver)
                    .WithArgs(parsers.OptionalWord("item|all"))
                    .HandleWith(args => Reset(api, economy, args))
                .EndSubCommand()
                .BeginSubCommand("trace")
                    .WithDescription("An item's last supply changes in your region")
                    .RequiresPrivilege(Privilege.controlserver)
                    .WithArgs(parsers.OptionalWord("item"))
                    .HandleWith(args => Trace(api, economy, args))
                .EndSubCommand()
            .EndSubCommand()
            .BeginSubCommand("price")
                .WithDescription("What the nearest trader pays for an item (the held one if none), and why")
                .RequiresPrivilege(Privilege.controlserver)
                .RequiresPlayer()
                .WithArgs(parsers.OptionalWord("item"))
                .HandleWith(args => Price(api, economy, args))
            .EndSubCommand()
            .BeginSubCommand("simulate")
                .WithDescription($"Advance supply and the loaded traders' restock clocks by days (at most {MaxSimulateDays})")
                .RequiresPrivilege(Privilege.controlserver)
                .WithArgs(parsers.Int("days"))
                .HandleWith(args => Simulate(economy, args))
            .EndSubCommand()
        .EndSubCommand();
    }

    private static string L(string key, params object[] args) => Lang.Get("seraphhorizons:" + key, args);

    private static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    private static string RegionOfCaller(ICoreServerAPI api, TextCommandCallingArgs args)
    {
        var pos = args.Caller.Entity?.Pos ?? api.World.DefaultSpawnPosition;
        return SupplyRegion.KeyOf(pos.X, pos.Z);
    }

    /// <summary>The item a command names: a code (domain optional), or the caller's held item.</summary>
    private static string? ItemOf(TextCommandCallingArgs args, string? word)
    {
        if (string.IsNullOrWhiteSpace(word))
            return args.Caller.Player?.InventoryManager?.ActiveHotbarSlot?.Itemstack?.Collectible?.Code?.ToString();
        return BuyerIndex.FullCode(word.Trim().ToLowerInvariant());
    }

    private static TextCommandResult? Off(EconomySystem economy) =>
        economy.RegionalSupply ? null : TextCommandResult.Error(L("trading-supply-off", SeraphHorizonsSystem.ConfigFile));

    private static TextCommandResult Show(ICoreServerAPI api, EconomySystem economy, TextCommandCallingArgs args)
    {
        if (Off(economy) is { } off) return off;
        string region = RegionOfCaller(api, args);
        string? word = args.RawArgs.PopWord();
        var book = economy.Supply;
        if (word == "all")
        {
            var rows = book.In(region).ToList();
            var lines = new List<string> { L("trading-supply-all-header", region, rows.Count, book.Day) };
            lines.AddRange(rows.Take(40).Select(r => L("trading-supply-line", r.Item, F(r.Entry.Level), F(book.Settings.Curve.Factor(r.Entry.Level)))));
            SeraphHorizons.Mod.Admin.AdminCommands.Attach(args, new System.Text.Json.Nodes.JsonObject
            {
                ["region"] = region, ["day"] = book.Day,
                ["items"] = SeraphHorizons.Mod.Core.AdminOutput.Rows(rows, r => SupplyJson(book, r.Item, r.Entry.Level, r.Entry)),
            });
            if (rows.Count > 40) lines.Add(L("trading-supply-more", rows.Count - 40));
            return TextCommandResult.Success(string.Join("\n", lines));
        }
        string? item = ItemOf(args, word);
        if (item is null) return TextCommandResult.Error(L("trading-supply-noitem"));
        double level = book.Level(region, item);
        SeraphHorizons.Mod.Admin.AdminCommands.Attach(args, new System.Text.Json.Nodes.JsonObject
        {
            ["region"] = region, ["day"] = book.Day, ["items"] = new System.Text.Json.Nodes.JsonArray(SupplyJson(book, item, level, book.Entry(region, item))),
        });
        return TextCommandResult.Success(L("trading-supply-one", item, region, F(level), F(book.Settings.Curve.Factor(level))));
    }

    /// <summary>One item's supply as the admin JSON gives it (docs/admin-tools.md).</summary>
    private static System.Text.Json.Nodes.JsonObject SupplyJson(SupplyBook book, string item, double level, SupplyEntry? entry) => new()
    {
        ["item"] = item, ["level"] = Math.Round(level, 4), ["factor"] = Math.Round(book.Settings.Curve.Factor(level), 4),
        ["decayPerDay"] = Math.Round(book.Settings.DailyDecay, 4),
        ["last"] = entry is { History.Count: > 0 } e ? new System.Text.Json.Nodes.JsonObject
        {
            ["day"] = e.History[^1].Day, ["kind"] = e.History[^1].Kind.ToString().ToLowerInvariant(), ["delta"] = Math.Round(e.History[^1].Delta, 4),
        } : null,
    };

    private static TextCommandResult Change(ICoreServerAPI api, EconomySystem economy, TextCommandCallingArgs args, bool set)
    {
        if (Off(economy) is { } off) return off;
        string region = RegionOfCaller(api, args);
        string item = BuyerIndex.FullCode(((string)args[0]).ToLowerInvariant());
        double amount = (double)args[1];
        if (set) economy.Supply.Set(region, item, amount);
        else economy.Supply.Add(region, item, amount);
        economy.RefreshTraders(region);
        double level = economy.Supply.Level(region, item);
        return TextCommandResult.Success(L("trading-supply-one", item, region, F(level), F(economy.Supply.Settings.Curve.Factor(level))));
    }

    private static TextCommandResult Reset(ICoreServerAPI api, EconomySystem economy, TextCommandCallingArgs args)
    {
        if (Off(economy) is { } off) return off;
        string region = RegionOfCaller(api, args);
        string? word = args[0] as string;
        string? item = word == "all" ? null : ItemOf(args, word);
        if (word != "all" && item is null) return TextCommandResult.Error(L("trading-supply-noitem"));
        int n = economy.Supply.Reset(region, item);
        economy.RefreshTraders(region);
        return TextCommandResult.Success(L("trading-supply-reset", n, region));
    }

    private static TextCommandResult Trace(ICoreServerAPI api, EconomySystem economy, TextCommandCallingArgs args)
    {
        if (Off(economy) is { } off) return off;
        string region = RegionOfCaller(api, args);
        string? item = ItemOf(args, args[0] as string);
        if (item is null) return TextCommandResult.Error(L("trading-supply-noitem"));
        var entry = economy.Supply.Entry(region, item);
        if (entry is null) return TextCommandResult.Success(L("trading-supply-one", item, region, "0", "1"));
        var lines = new List<string> { L("trading-supply-trace-header", item, region, F(entry.Level), economy.Supply.Day) };
        lines.AddRange(entry.History.Select(h => L("trading-supply-trace-line", h.Day, L("trading-supply-kind-" + h.Kind.ToString().ToLowerInvariant()),
            (h.Delta >= 0 ? "+" : "") + F(h.Delta), F(h.Level))));
        return TextCommandResult.Success(string.Join("\n", lines));
    }

    private static TextCommandResult Price(ICoreServerAPI api, EconomySystem economy, TextCommandCallingArgs args)
    {
        var caller = args.Caller.Entity;
        var trader = api.World.GetNearestEntity(caller.Pos.XYZ, 16, 16, e => e is EntitySeraphTrader) as EntitySeraphTrader;
        if (trader is null) return TextCommandResult.Error(L("trading-price-notrader"));
        string? code = ItemOf(args, args[0] as string);
        if (code is null) return TextCommandResult.Error(L("trading-supply-noitem"));
        var collectible = (CollectibleObject?)api.World.GetItem(new AssetLocation(code)) ?? api.World.GetBlock(new AssetLocation(code));
        if (collectible is null || collectible.Id == 0) return TextCommandResult.Error(L("trading-price-unknown", code));
        var stack = new ItemStack(collectible);
        var inv = trader.Inventory;
        string type = L("trading-type-" + trader.TraderType);
        if (inv.GetBuyingConditionsSlot(stack) is { } slot and not OffListSlot)
            return TextCommandResult.Success(L("trading-price-listed", type, code, slot.TradeItem.Price, slot.TradeItem.Stack?.StackSize ?? 1,
                F(EconomySystem.SupplyFactor(trader, code))));
        var o = economy.QuoteOffList(trader, stack, args.Caller.Player?.PlayerUID);
        if (!o.Accepted) return TextCommandResult.Success(L("trading-price-refused", type, code, EconomyPatches.RefusalText(o.Refusal) ?? ""));
        return TextCommandResult.Success(L("trading-price-offlist", type, code, o.UnitPrice, o.UnitSize, F(o.Base), F(o.Fit), F(o.Supply), F(o.Modifiers),
            o.Capped ? L("trading-economy-offer-capped") : "", EconomySystem.SideBudgetOf(trader)));
    }

    private static TextCommandResult Simulate(EconomySystem economy, TextCommandCallingArgs args)
    {
        int days = (int)args[0];
        if (days < 1 || days > MaxSimulateDays) return TextCommandResult.Error(L("trading-simulate-range", MaxSimulateDays));
        int traders = economy.Simulate(days);
        return TextCommandResult.Success(L("trading-simulate-done", days, economy.Supply.Day, traders));
    }
}
