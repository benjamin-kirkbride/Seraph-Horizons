using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using SeraphHorizons.Mod.Admin;
using SeraphHorizons.Mod.Core;
using SeraphHorizons.Mod.Ore;
using SeraphHorizons.Mod.Ore.Core;
using SeraphHorizons.Mod.Trading.Core;
using SeraphHorizons.Mod.Trading.Economy;
using SeraphHorizons.Mod.Trading.Economy.Core;
using SeraphHorizons.Mod.Trading.Maps;
using SeraphHorizons.Mod.Trading.Maps.Core;
using SeraphHorizons.Mod.Trading.Standing;
using SeraphHorizons.Mod.Trading.Standing.Core;
using SeraphHorizons.Mod.Trading.Values;
using SeraphHorizons.Mod.Trading.Values.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using JsonObject = System.Text.Json.Nodes.JsonObject;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.Trading.Admin;

/// <summary>A trader being inspected (<c>/sh trade inspect</c>): later waves add their lines (open
/// orders, deliveries) through <see cref="TradingAdminSystem.InspectContributors"/>.</summary>
public sealed class TraderInspection(EntitySeraphTrader trader, string traderId)
{
    public EntitySeraphTrader Trader { get; } = trader;
    public string TraderId { get; } = traderId;
    public List<string> Lines { get; } = [];
    public JsonObject Data { get; } = new();
}

/// <summary>
/// The trader admin tools (#459) that the earlier waves' commands don't have, under <c>/sh trade</c>
/// (<c>controlserver</c>; <c>--json</c> from <see cref="AdminCommands"/>). A trader is <c>near</c>,
/// <c>camp:x,z</c>, <c>entity:n</c> or a camp cell <c>x,z</c> (<see cref="TraderLookup"/>), loaded.
/// <list type="bullet">
/// <item><c>inspect [trader]</c>: type, region, list core, current slots, wallet, side budget, next
/// restock, standing of every player with it.</item>
/// <item><c>restock [trader] [--full]</c>, <c>reroll [trader]</c>, <c>wallet &lt;trader&gt; &lt;gears&gt;</c>,
/// <c>budget &lt;trader&gt; &lt;gears&gt;</c>.</item>
/// <item><c>values missing|suspicious</c>: the item values report's checks on the loaded table.</item>
/// <item><c>maps [trader]</c>: the deposits and gravel fields around a trader and whether a map of
/// each could be sold.</item>
/// <item><c>leads [player] [trader]</c>: a player's camp lead history (their group's maps bought
/// per trader, the traders that sold them a stranger's map, the camps visited) and the camp leads a
/// trader offers them now with their prices.</item>
/// <item><c>export|import &lt;file&gt;</c>: supply, standing, the deposit registry, and what later
/// waves register (<see cref="TradingSystem.AdminState"/>).</item>
/// <item><c>log on|off [channel]</c>: <c>Logs/seraphhorizons-trade.log</c>.</item>
/// <item><c>map on|off [item]</c>: the trade overlay on the caller's world map.</item>
/// </list>
/// </summary>
internal sealed class TradeAdminCommands(ICoreServerAPI api, TradingSystem trading, TradingAdminSystem admin)
{
    public const int ListLimit = 40;
    public const int MapsOreRadius = 6000;
    public const int MapsGravelRadius = 2000;
    public const int DefaultMapRadius = 12000;

    private static string L(string key, params object[] args) => Lang.Get("seraphhorizons:" + key, args);

    private static string F(double v, string format = "0.##") => v.ToString(format, CultureInfo.InvariantCulture);

    public void Register()
    {
        var parsers = api.ChatCommands.Parsers;
        var trade = api.ChatCommands.GetOrCreate("sh").BeginSubCommand("trade");
        if (string.IsNullOrEmpty(trade.Description)) trade.WithDescription("Traders (admin)");
        trade.RequiresPrivilege(Privilege.controlserver);
        trade.BeginSubCommand("inspect")
                .WithDescription("A trader's type, region, stock, wallet, side budget, next restock and every player's standing with it (near if none given)")
                .RequiresPrivilege(Privilege.controlserver)
                .WithArgs(parsers.OptionalWord("trader"))
                .HandleWith(OnInspect)
            .EndSubCommand()
            .BeginSubCommand("restock")
                .WithDescription("Restock a trader now as its weekly restock would; --full redraws every rotating slot and refills its wallet")
                .RequiresPrivilege(Privilege.controlserver)
                .WithArgs(parsers.OptionalAll("trader [--full]"))
                .HandleWith(OnRestock)
            .EndSubCommand()
            .BeginSubCommand("reroll")
                .WithDescription("Draw new rotating slots for a trader")
                .RequiresPrivilege(Privilege.controlserver)
                .WithArgs(parsers.OptionalWord("trader"))
                .HandleWith(OnReroll)
            .EndSubCommand()
            .BeginSubCommand("wallet")
                .WithDescription("Set a trader's gears")
                .RequiresPrivilege(Privilege.controlserver)
                .WithArgs(parsers.Word("trader"), parsers.Int("gears"))
                .HandleWith(args => OnMoney(args, sideBudget: false))
            .EndSubCommand()
            .BeginSubCommand("budget")
                .WithDescription("Set a trader's side budget (what it pays for goods off its list)")
                .RequiresPrivilege(Privilege.controlserver)
                .WithArgs(parsers.Word("trader"), parsers.Int("gears"))
                .HandleWith(args => OnMoney(args, sideBudget: true))
            .EndSubCommand()
            .BeginSubCommand("values")
                .WithDescription("Items with no value (missing), or valued below their ingredients (suspicious), from the loaded value table")
                .RequiresPrivilege(Privilege.controlserver)
                .WithArgs(parsers.WordRange("check", "missing", "suspicious"))
                .HandleWith(OnValues)
            .EndSubCommand()
            .BeginSubCommand("maps")
                .WithDescription("The ore and gravel maps a trader could sell now, and why each candidate is or isn't one (near if none given)")
                .RequiresPrivilege(Privilege.controlserver)
                .WithArgs(parsers.OptionalWord("trader"))
                .HandleWith(OnMaps)
            .EndSubCommand()
            .BeginSubCommand("leads")
                .WithDescription("A player's camp lead history (their group's) and the camp leads a trader offers them now with prices (yourself and near if none given)")
                .RequiresPrivilege(Privilege.controlserver)
                .WithArgs(parsers.OptionalWord("player"), parsers.OptionalWord("trader"))
                .HandleWith(OnLeads)
            .EndSubCommand()
            .BeginSubCommand("export")
                .WithDescription($"Write supply, standing, the deposit registry (and orders, deliveries once they exist) to a file in the server's {AdminFiles.Folder} folder")
                .RequiresPrivilege(Privilege.controlserver)
                .WithArgs(parsers.Word("file"))
                .HandleWith(OnExport)
            .EndSubCommand()
            .BeginSubCommand("import")
                .WithDescription("Replace the state sections a file holds with its contents (an export from this or another world)")
                .RequiresPrivilege(Privilege.controlserver)
                .WithArgs(parsers.Word("file"))
                .HandleWith(OnImport)
            .EndSubCommand()
            .BeginSubCommand("log")
                .WithDescription($"Log a channel ({string.Join(", ", AdminLogs.TradeChannels)}), or all, to Logs/{AdminLogs.TradeFile}")
                .RequiresPrivilege(Privilege.controlserver)
                .WithArgs(parsers.WordRange("state", "on", "off"), parsers.OptionalWordRange("channel", AdminLogs.TradeChannels))
                .HandleWith(OnLog)
            .EndSubCommand()
            .BeginSubCommand("map")
                .WithDescription("The trade overlay on your world map: camp grid by type, settlement reserves, supply heat for an item (the held one if none)")
                .RequiresPrivilege(Privilege.controlserver)
                .RequiresPlayer()
                .WithArgs(parsers.WordRange("state", "on", "off"), parsers.OptionalWord("item"))
                .HandleWith(OnMap)
            .EndSubCommand();
    }

    private EntitySeraphTrader? Trader(TextCommandCallingArgs args, string? text, out TextCommandResult? error)
    {
        var trader = TraderLookup.Find(api, args, text, out string why);
        error = trader == null ? TextCommandResult.Error(L("trading-admin-notrader", why)) : null;
        return trader;
    }

    /// <summary>The inventory into the entity's attributes, as <see cref="EntitySeraphTrader.Restock"/>
    /// saves it, and to the players watching.</summary>
    private void Save(EntitySeraphTrader trader)
    {
        var store = trader.WatchedAttributes.GetTreeAttribute("traderInventory") as TreeAttribute ?? new TreeAttribute();
        trader.Inventory.ToTreeAttributes(store);
        trader.WatchedAttributes["traderInventory"] = store;
        trader.WatchedAttributes.MarkAllDirty();
        api.Network.BroadcastEntityPacket(trader.EntityId, 1234, store.ToBytes());
    }

    private static int Wallet(EntitySeraphTrader trader) => trader.Inventory?.GetTraderAssets() ?? 0;

    // ---- inspect ----

    private TextCommandResult OnInspect(TextCommandCallingArgs args)
    {
        if (Trader(args, args[0] as string, out var error) is not { } trader) return error!;
        string id = TraderLookup.IdOf(api, trader);
        var region = trader.Region;
        var output = new AdminOutput("trade inspect");
        double nextRestock = trader.NextRefreshTotalDays();
        output.Summary = L("trading-admin-inspect-head", L("trading-type-" + trader.TraderType), id, (int)trader.Pos.X, (int)trader.Pos.Y, (int)trader.Pos.Z,
            region.Climate, region.Rock, Wallet(trader), EconomySystem.SideBudgetOf(trader), F(Math.Max(0, nextRestock), "0.#"));
        var data = output.Data;
        data["trader"] = new JsonObject
        {
            ["id"] = id,
            ["entityId"] = trader.EntityId,
            ["type"] = trader.TraderType,
            ["climate"] = region.Climate,
            ["rock"] = region.Rock,
            ["x"] = trader.Pos.X, ["y"] = trader.Pos.Y, ["z"] = trader.Pos.Z,
            ["wallet"] = Wallet(trader),
            ["sideBudget"] = EconomySystem.SideBudgetOf(trader),
            ["nextRestockDays"] = Math.Round(nextRestock, 2),
            ["supplyRegion"] = EconomySystem.RegionOf(trader),
        };
        if (trading.Lists?.For(trader.TraderType) is { } def)
        {
            double factor = trading.Standing.Enabled ? trading.Standing.WalletFactorFor(trader) : 1;
            var resolved = TradeListResolver.Resolve(def, region);
            var wallet = def.WalletAt(factor);
            output.Lines.Add(L("trading-admin-inspect-wallet", F(wallet.Avg, "0"), F(wallet.Var, "0"), F(factor, "0.##")));
            foreach (var (name, side, slots, keysAttr) in new[]
                     {
                         ("selling", resolved.Selling, trader.Inventory.SellingSlots, EntitySeraphTrader.SellingKeysAttr),
                         ("buying", resolved.Buying, trader.Inventory.BuyingSlots, EntitySeraphTrader.BuyingKeysAttr),
                     })
            {
                var core = side.Core.Select(e => e.Key).ToHashSet(StringComparer.Ordinal);
                output.Lines.Add(L("trading-admin-inspect-core", name, side.Core.Count, side.Rotating.Count, side.MaxRotating));
                var keys = (trader.WatchedAttributes[keysAttr] as StringArrayAttribute)?.value ?? [];
                var rows = new JsonArray();
                for (int i = 0; i < slots.Length; i++)
                {
                    var t = slots[i].TradeItem;
                    if (slots[i].Itemstack is not { } stack || t == null) continue;
                    string key = i < keys.Length ? keys[i] : "";
                    bool isCore = core.Contains(key);
                    string code = stack.Collectible.Code.ToString();
                    output.Lines.Add($"  {(isCore ? "core" : "rotating")} {code} x{stack.StackSize} stock {t.Stock} price {t.Price}");
                    rows.Add(new JsonObject { ["slot"] = i, ["code"] = code, ["key"] = key, ["core"] = isCore, ["stackSize"] = stack.StackSize, ["stock"] = t.Stock, ["price"] = t.Price });
                }
                data[name] = rows;
            }
        }
        // Every player and company with a record with this trader.
        var standing = new JsonArray();
        if (StandingSystem.Of(api) is { Enabled: true } ss)
        {
            foreach (var (uid, records) in ss.Ledger.State.Players)
            {
                if (!records.ContainsKey(id)) continue;
                var v = ss.ViewFor(uid, trader);
                string name = api.PlayerData.GetPlayerDataByUid(uid)?.LastKnownPlayername ?? uid;
                output.Lines.Add(L("trading-admin-inspect-standing", name, StandingText.TierName(v.Tier), Math.Floor(v.Effective), Math.Floor(v.Personal),
                    Math.Floor(v.Company ?? 0), Math.Floor(v.Spill)));
                standing.Add(new JsonObject
                {
                    ["player"] = name, ["uid"] = uid, ["tier"] = v.Tier.Code, ["effective"] = Math.Round(v.Effective, 1),
                    ["personal"] = Math.Round(v.Personal, 1), ["company"] = v.Company is { } c ? Math.Round(c, 1) : null, ["spill"] = Math.Round(v.Spill, 1),
                });
            }
            foreach (var (group, record) in ss.Ledger.State.Companies)
                if (record.Traders.TryGetValue(id, out var r))
                    output.Lines.Add(L("trading-admin-inspect-company", api.Groups.PlayerGroupsById.TryGetValue(group, out var g) ? g.Name : "#" + group, Math.Floor(r.Points)));
        }
        if (standing.Count == 0) output.Lines.Add(L("trading-admin-inspect-nostanding"));
        data["standing"] = standing;
        var inspection = new TraderInspection(trader, id);
        foreach (var contribute in admin.InspectContributors)
            contribute(inspection);
        output.Lines.AddRange(inspection.Lines);
        foreach (var (key, value) in inspection.Data.ToList())
        {
            inspection.Data.Remove(key);
            data[key] = value;
        }
        return AdminCommands.Answer(args, output);
    }

    // ---- restock, reroll, wallet, budget ----

    private TextCommandResult OnRestock(TextCommandCallingArgs args)
    {
        var words = ((args[0] as string) ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        bool full = words.RemoveAll(w => w.Equals("--full", StringComparison.OrdinalIgnoreCase)) > 0;
        if (Trader(args, words.FirstOrDefault(), out var error) is not { } trader) return error!;
        trader.WatchedAttributes.SetDouble("lastRefreshTotalDays", api.World.Calendar.TotalDays);
        trader.Restock(full ? 1.1f : 0.5f);
        if (full && trader.TradeProps?.Money is { } money)
        {
            trader.Inventory.MoneySlot.Itemstack = null;
            trader.Inventory.GiveToTrader((int)Math.Round(money.avg));
        }
        Save(trader);
        var output = new AdminOutput("trade restock")
        {
            Summary = L(full ? "trading-admin-restocked-full" : "trading-admin-restocked", TraderLookup.IdOf(api, trader), Wallet(trader), EconomySystem.SideBudgetOf(trader)),
        };
        output.Data["full"] = full;
        output.Data["wallet"] = Wallet(trader);
        output.Data["sideBudget"] = EconomySystem.SideBudgetOf(trader);
        return AdminCommands.Answer(args, output);
    }

    private TextCommandResult OnReroll(TextCommandCallingArgs args)
    {
        if (Trader(args, args[0] as string, out var error) is not { } trader) return error!;
        trader.Restock(1.1f);
        Save(trader);
        var output = new AdminOutput("trade reroll") { Summary = L("trading-admin-rerolled", TraderLookup.IdOf(api, trader)) };
        output.Data["selling"] = new JsonArray(trader.Inventory.SellingSlots.Select(s => s.Itemstack).OfType<ItemStack>()
            .Select(s => (JsonNode?)s.Collectible.Code.ToString()).ToArray());
        return AdminCommands.Answer(args, output);
    }

    private TextCommandResult OnMoney(TextCommandCallingArgs args, bool sideBudget)
    {
        if (Trader(args, (string)args[0], out var error) is not { } trader) return error!;
        int gears = (int)args[1];
        if (gears < 0 || gears > 100000) return TextCommandResult.Error(L("trading-admin-gears-range"));
        if (sideBudget) EconomySystem.SetSideBudget(trader, gears);
        else
        {
            trader.Inventory.MoneySlot.Itemstack = null;
            if (gears > 0) trader.Inventory.GiveToTrader(gears);
            Save(trader);
        }
        var output = new AdminOutput(sideBudget ? "trade budget" : "trade wallet")
        {
            Summary = L(sideBudget ? "trading-admin-budget-set" : "trading-admin-wallet-set", TraderLookup.IdOf(api, trader), gears),
        };
        output.Data["wallet"] = Wallet(trader);
        output.Data["sideBudget"] = EconomySystem.SideBudgetOf(trader);
        return AdminCommands.Answer(args, output);
    }

    // ---- values ----

    private TextCommandResult OnValues(TextCommandCallingArgs args)
    {
        var values = ItemValuesSystem.For(api);
        var output = new AdminOutput("trade values");
        if ((string)args[0] == "missing")
        {
            var listed = trading.Lists?.Lists.Values
                .SelectMany(d => Entries(d.Selling).Concat(Entries(d.Buying)))
                .Select(e => e.Code.Contains(':') ? e.Code : "game:" + e.Code) ?? [];
            var listedMissing = ValueChecks.Missing(values, listed);
            // Every collectible a player can get at: those in a creative tab.
            var creative = api.World.Collectibles.Where(c => c?.Code != null && c.CreativeInventoryTabs is { Length: > 0 })
                .Select(c => c.Code.ToString());
            var creativeMissing = ValueChecks.Missing(values, creative);
            output.Summary = L("trading-admin-values-missing", listedMissing.Count, creativeMissing.Count, values.Count);
            output.Lines.Add(L("trading-admin-values-listed"));
            output.Lines.AddRange(Limited(listedMissing.Select(c => "  " + c)));
            output.Lines.Add(L("trading-admin-values-creative"));
            output.Lines.AddRange(Limited(creativeMissing.Select(c => "  " + c)));
            output.Data["listed"] = new JsonArray(listedMissing.Select(c => (JsonNode?)c).ToArray());
            output.Data["creative"] = new JsonArray(creativeMissing.Select(c => (JsonNode?)c).ToArray());
            return AdminCommands.Answer(args, output);
        }
        var below = ValueChecks.Below(values, Recipes());
        output.Summary = L("trading-admin-values-suspicious", below.Count, api.World.GridRecipes.Count);
        output.Lines.AddRange(Limited(below.Select(b => $"  {b.Code} {F(b.Value, "0.#####")} < {F(b.Ingredients, "0.###")} ({b.Recipe})")));
        output.Data["belowIngredients"] = AdminOutput.Rows(below, b => new JsonObject
        {
            ["code"] = b.Code, ["value"] = Math.Round(b.Value, 6), ["ingredients"] = b.Ingredients, ["recipe"] = b.Recipe,
        });
        return AdminCommands.Answer(args, output);
    }

    private static IEnumerable<TradeEntry> Entries(TradeSide side) =>
        side.Core.Concat(side.Rotating.List).Concat(side.Regional.Values.SelectMany(r => r.Core.Concat(r.Rotating)));

    private static IEnumerable<string> Limited(IEnumerable<string> lines)
    {
        var list = lines.ToList();
        return list.Count <= ListLimit ? list : list.Take(ListLimit).Append(L("trading-supply-more", list.Count - ListLimit));
    }

    /// <summary>The game's grid recipes as the value check reads them: consumed ingredients only, a
    /// wildcard by its pattern (the table averages its matches).</summary>
    private IEnumerable<RecipeRow> Recipes()
    {
        foreach (var r in api.World.GridRecipes)
        {
            if (r?.Output?.ResolvedItemStack is not { } output || r.ResolvedIngredients is not { } ingredients) continue;
            var list = new List<(string, int)>();
            bool ok = true;
            foreach (var ing in ingredients)
            {
                if (ing == null || ing.IsTool || !ing.Consume) continue;
                string? code = ing.MatchingType != EnumRecipeMatchType.Exact ? ing.Code?.ToString() : ing.ResolvedItemStack?.Collectible?.Code?.ToString();
                if (code == null)
                {
                    ok = false;
                    break;
                }
                list.Add((code, Math.Max(1, ing.Quantity)));
            }
            if (ok) yield return new RecipeRow(r.Name ?? output.Collectible.Code.ToString(), output.Collectible.Code.ToString(), output.StackSize, list);
        }
    }

    // ---- maps ----

    private TextCommandResult OnMaps(TextCommandCallingArgs args)
    {
        if (Trader(args, args[0] as string, out var error) is not { } trader) return error!;
        // TODO(#455): ask the maps service (Trading/Maps/) what this trader stocks and why, once it is
        // merged; until then this reads the deposit registry directly with the rules #455 sets out:
        // ore maps from the prospector, unsold deposits only, tiers by standing.
        if (api.ModLoader.GetModSystem<OreSystem>()?.Deposits is not { } deposits) return TextCommandResult.Error(L("trading-admin-maps-off"));
        int x = (int)trader.Pos.X, z = (int)trader.Pos.Z;
        var output = new AdminOutput("trade maps");
        bool sellsOre = trader.TraderType == "prospector";
        var rows = new JsonArray();
        int accepted = 0;
        var candidates = deposits.Candidates(x, z, MapsOreRadius).Concat(deposits.GravelFields(x, z, MapsGravelRadius)).ToList();
        foreach (var c in candidates)
        {
            string? reject = c.Record.State switch
            {
                DepositState.Sold => L("trading-admin-maps-sold", c.Record.SoldToName ?? "-"),
                DepositState.SoldOut => L("trading-admin-maps-soldout"),
                _ => null,
            };
            if (reject == null && !sellsOre && !c.Key.IsGravel) reject = L("trading-admin-maps-notprospector");
            if (reject == null) accepted++;
            string size = c.Record.Tier is { } t ? t.ToString().ToLowerInvariant() : c.Generated ? L("trading-admin-maps-unmeasured") : L("trading-admin-maps-ungenerated");
            output.Lines.Add($"  {c.Key.Id} {(int)c.Distance} m, {size}: {reject ?? L("trading-admin-maps-ok")}");
            rows.Add(new JsonObject
            {
                ["id"] = c.Key.Id, ["distance"] = (int)c.Distance, ["state"] = c.Record.State.ToString().ToLowerInvariant(),
                ["tier"] = c.Record.Tier?.ToString().ToLowerInvariant(), ["generated"] = c.Generated, ["accepted"] = reject == null, ["reason"] = reject,
            });
        }
        if (StandingSystem.Of(api) is { Enabled: true } ss)
            output.Lines.Add(L("trading-admin-maps-tiers", string.Join(", ", ss.Rules.Tiers.Select(t => $"{t.Code} {t.Unlocks.MapTier}"))));
        output.Summary = L("trading-admin-maps-head", TraderLookup.IdOf(api, trader), L("trading-type-" + trader.TraderType), accepted, candidates.Count, MapsOreRadius, MapsGravelRadius);
        output.Data["candidates"] = rows;
        output.Data["accepted"] = accepted;
        return AdminCommands.Answer(args, output);
    }

    // ---- leads ----

    private TextCommandResult OnLeads(TextCommandCallingArgs args)
    {
        if (api.ModLoader.GetModSystem<MapsSystem>() is not { Active: true } maps) return TextCommandResult.Error(L("trading-admin-maps-off"));
        string? name = args[0] as string;
        var player = name is null or "me" ? args.Caller.Player as IServerPlayer
            : api.World.AllOnlinePlayers.FirstOrDefault(p => p.PlayerName.Equals(name, StringComparison.OrdinalIgnoreCase)) as IServerPlayer;
        if (player is null) return TextCommandResult.Error(L("trading-admin-leads-noplayer", name ?? "-"));
        var output = new AdminOutput("trade leads");
        var keys = maps.KeysOf(player.PlayerUID);
        var groups = new JsonObject();
        foreach (string key in keys)
        {
            var g = maps.Leads.Groups.GetValueOrDefault(key) ?? new GroupLeads();
            output.Lines.Add(L("trading-admin-leads-group", key,
                g.Bought.Count == 0 ? "-" : string.Join(", ", g.Bought.OrderBy(b => b.Key).Select(b => $"{b.Key} ×{b.Value}")),
                g.StrangerMaps.Count == 0 ? "-" : string.Join(", ", g.StrangerMaps.Order()),
                g.Visited.Count));
            groups[key] = new JsonObject
            {
                ["bought"] = new JsonObject(g.Bought.Select(b => KeyValuePair.Create(b.Key, (JsonNode?)b.Value))),
                ["strangerMaps"] = new JsonArray(g.StrangerMaps.Order().Select(t => (JsonNode?)t).ToArray()),
                ["visited"] = new JsonArray(g.Visited.Order().Select(t => (JsonNode?)t).ToArray()),
                ["pityMap"] = g.PityMap,
            };
        }
        bool pityUsed = maps.Leads.PityUsed(player.PlayerUID);
        output.Data["player"] = player.PlayerName;
        output.Data["groups"] = groups;
        output.Data["pityUsed"] = pityUsed;
        output.Summary = L("trading-admin-leads-head", player.PlayerName, string.Join(", ", keys),
            L(pityUsed ? "trading-admin-leads-pity-used" : "trading-admin-leads-pity-unused"));
        var trader = TraderLookup.Find(api, args, args[1] as string, out _);
        if (trader != null && maps.CampLeadsFor(player, trader) is { } view)
        {
            output.Lines.Add(L("trading-admin-leads-trader", view.TraderId, view.Buyer.Tier, view.Buyer.Bought, view.Reach, view.Why.ToString()));
            var offers = new JsonArray();
            foreach (var o in view.Offers)
            {
                output.Lines.Add($"  {(o.Pity ? "first map: " : "")}{o.Cell} ring {o.Ring} {L("trading-type-" + o.Type)} {o.Distance:0} m ({o.X}, {o.Z}): {o.Price} g{(o.Prospector ? " *" : "")}");
                offers.Add(new JsonObject
                {
                    ["cell"] = o.Cell.ToString(), ["ring"] = o.Ring, ["type"] = o.Type, ["x"] = o.X, ["z"] = o.Z, ["distance"] = Math.Round(o.Distance),
                    ["price"] = o.Price, ["prospector"] = o.Prospector, ["pity"] = o.Pity,
                });
            }
            output.Data["trader"] = view.TraderId;
            output.Data["tier"] = view.Buyer.Tier;
            output.Data["reach"] = view.Reach;
            output.Data["offers"] = offers;
        }
        return AdminCommands.Answer(args, output);
    }

    // ---- export, import ----

    private TextCommandResult OnExport(TextCommandCallingArgs args)
    {
        if (AdminFiles.Resolve(OreAdminCommands.FilesFolder(), (string)args[0]) is not { } path)
            return TextCommandResult.Error(L("trading-admin-badfile", (string)args[0]));
        var (file, errors) = trading.AdminState.Export(stamp: () => DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
        file["world"] = api.World.SavegameIdentifier;
        file["seed"] = api.World.Seed;
        File.WriteAllText(path, file.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        var sections = ((JsonObject)file["sections"]!).Select(kv => kv.Key).ToList();
        var output = new AdminOutput("trade export") { Summary = L("trading-admin-exported", string.Join(", ", sections), path) };
        foreach (var (name, why) in errors) output.Lines.Add(L("trading-admin-section-failed", name, why));
        output.Ok = errors.Count == 0;
        output.Data["file"] = path;
        output.Data["sections"] = new JsonArray(sections.Select(s => (JsonNode?)s).ToArray());
        return AdminCommands.Answer(args, output);
    }

    private TextCommandResult OnImport(TextCommandCallingArgs args)
    {
        if (AdminFiles.Resolve(OreAdminCommands.FilesFolder(), (string)args[0]) is not { } path)
            return TextCommandResult.Error(L("trading-admin-badfile", (string)args[0]));
        if (!File.Exists(path)) return TextCommandResult.Error(L("trading-admin-nofile", path));
        Dictionary<string, (ImportOutcome Outcome, string? Error)> result;
        try
        {
            result = trading.AdminState.Import(JsonNode.Parse(File.ReadAllText(path)));
        }
        catch (Exception e) when (e is JsonException or FormatException)
        {
            return TextCommandResult.Error(L("trading-admin-badjson", path, e.Message));
        }
        var output = new AdminOutput("trade import")
        {
            Summary = L("trading-admin-imported", result.Count(r => r.Value.Outcome == ImportOutcome.Imported), path),
            Ok = result.All(r => r.Value.Outcome != ImportOutcome.Failed),
        };
        var rows = new JsonObject();
        foreach (var (name, (outcome, why)) in result.OrderBy(r => r.Key, StringComparer.Ordinal))
        {
            output.Lines.Add($"  {name}: {outcome.ToString().ToLowerInvariant()}{(why != null ? " (" + why + ")" : "")}");
            rows[name] = outcome.ToString().ToLowerInvariant();
        }
        output.Data["file"] = path;
        output.Data["sections"] = rows;
        return AdminCommands.Answer(args, output);
    }

    // ---- log ----

    private TextCommandResult OnLog(TextCommandCallingArgs args)
    {
        if (AdminLogs.Trade is not { } log) return TextCommandResult.Error(L("trading-admin-tools-off"));
        bool on = (string)args[0] == "on";
        string? channel = args[1] as string;
        if (string.IsNullOrEmpty(channel)) channel = null;
        if (!log.Set(channel, on)) return TextCommandResult.Error(L("trading-admin-log-channel", channel ?? "", string.Join(", ", log.Channels)));
        var output = new AdminOutput("trade log")
        {
            Summary = L("trading-admin-log", log.On.Count == 0 ? "-" : string.Join(", ", log.On), log.Path),
        };
        output.Data["file"] = log.Path;
        output.Data["channels"] = new JsonArray(log.On.Select(c => (JsonNode?)c).ToArray());
        return AdminCommands.Answer(args, output);
    }

    // ---- map ----

    private TextCommandResult OnMap(TextCommandCallingArgs args)
    {
        if (AdminSystem.Of(api)?.Map is not { } map) return TextCommandResult.Error(L("trading-admin-tools-off"));
        if (args.Caller.Player is not IServerPlayer player) return TextCommandResult.Error(L("trading-admin-noplayer"));
        bool on = (string)args[0] == "on";
        string? item = args[1] as string;
        if (string.IsNullOrWhiteSpace(item)) item = player.InventoryManager?.ActiveHotbarSlot?.Itemstack?.Collectible?.Code?.ToString();
        else item = BuyerIndex.FullCode(item.Trim().ToLowerInvariant());
        int drawn = map.Set(player, "trade", on, item ?? "");
        var output = new AdminOutput("trade map")
        {
            Summary = on ? L("trading-admin-map-on", drawn, item ?? "-") : L("trading-admin-map-off"),
        };
        output.Data["on"] = on;
        output.Data["drawn"] = drawn;
        output.Data["item"] = item;
        return AdminCommands.Answer(args, output);
    }

    /// <summary>The trade overlay around (x, z): camp cells coloured by type with placed camps marked,
    /// settlement reserves, the supply heat of <paramref name="item"/> per region (redder = more
    /// supplied, cheaper), and whatever later waves add (delivery routes).</summary>
    internal MapOverlay Overlay(IServerPlayer player, int x, int z, int radius, string? item)
    {
        var overlay = new MapOverlay { Legend = "trade: camp cells by type, camps, settlement reserves, supply heat" + (item != null ? " of " + item : "") };
        if (trading.GridReady)
        {
            foreach (var cell in TraderGrid.CellsAround(x, z, radius))
            {
                string type = trading.Grid!.TypeOf(cell);
                int x1 = cell.X * TraderGrid.CellSize, z1 = cell.Z * TraderGrid.CellSize;
                overlay.Rects.Add(new OverlayRect(x1, z1, x1 + TraderGrid.CellSize, z1 + TraderGrid.CellSize, MapOverlay.ColorFor(type, 170), $"camp cell {cell} {type}"));
                if (trading.Camps!.Registry.Get(cell) is { Status: CampStatus.Placed } camp)
                    overlay.Marks.Add(new OverlayMark(camp.X, camp.Z, MapOverlay.ColorFor(type), $"camp {cell}: {type} ({camp.Structure})", 10));
            }
            int s = TraderGrid.SettlementSize;
            for (int sx = (int)Math.Floor((x - radius) / (double)s); sx <= (int)Math.Floor((x + radius) / (double)s); sx++)
                for (int sz = (int)Math.Floor((z - radius) / (double)s); sz <= (int)Math.Floor((z + radius) / (double)s); sz++)
                {
                    var (cx, cz) = TraderGrid.SettlementCentre(sx * s, sz * s);
                    overlay.Rings.Add(new OverlayRing(cx, cz, TraderGrid.SettlementReserve, MapOverlay.Argb(255, 255, 255), $"settlement reserve {sx},{sz}"));
                }
        }
        if (item != null && EconomySystem.Of(api) is { RegionalSupply: true } economy)
        {
            int s = SupplyRegion.Size;
            for (int rx = (int)Math.Floor((x - radius) / (double)s); rx <= (int)Math.Floor((x + radius) / (double)s); rx++)
                for (int rz = (int)Math.Floor((z - radius) / (double)s); rz <= (int)Math.Floor((z + radius) / (double)s); rz++)
                {
                    string region = $"{rx},{rz}";
                    double level = economy.Supply.Level(region, item);
                    double factor = economy.Supply.Settings.Curve.Factor(level);
                    int heat = (int)Math.Round(Math.Clamp((1 - factor) / 0.7, 0, 1) * 255);
                    overlay.Rects.Add(new OverlayRect(rx * s + 16, rz * s + 16, rx * s + s - 16, rz * s + s - 16,
                        MapOverlay.Argb(255, 255 - heat, 255 - heat), $"supply {region} {item}: level {F(level)}, price x{F(factor)}"));
                    overlay.Marks.Add(new OverlayMark(rx * s + s / 2, rz * s + s / 2, MapOverlay.Argb(255, 255 - heat, 255 - heat),
                        $"supply {region} {item}: level {F(level)}, price x{F(factor)}", 6));
                }
        }
        foreach (var contribute in admin.OverlayContributors)
            contribute(player, x, z, radius, overlay);
        return overlay;
    }
}
