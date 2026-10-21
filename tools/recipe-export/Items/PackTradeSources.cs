using System.Collections;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SeraphHorizons.RecipeExport.Recipes;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SeraphHorizons.RecipeExport.Items;

/// <summary>
/// The pack's own traders' goods (seraphhorizons Trading, docs/trading.md): `traderSells` and
/// `traderBuys` sources from the trade lists its server loaded (<c>TradingSystem.Lists</c>, after
/// the switched-off features' entries were taken out), and `traderSells` sources from the
/// schematic catalog (<c>config/schematic-gates.json</c> "sales"). The pack's traders have no
/// vanilla-format list (<c>tradePropsFile</c>), so <see cref="SourceIndex"/>'s own reader does not
/// see them.
///
/// One entity per trader type: `from` is one variant of the type (temperate and male where there
/// is one), `extra.entityType` is <c>seraphhorizons:trader-{type}</c> (or <c>visitor-{type}</c>),
/// `fromName` the type's name. Every variant of a type stocks from the same list, so the site shows
/// one page with one set of rows instead of six identical chips.
///
/// Prices are the pack's own (<c>Pricing.ListBase</c> and <c>Pricing.Listed</c>, called by
/// reflection as <see cref="GearChain"/> reads the mod): `price` is what a stranger pays (or is
/// paid) with an average roll and no glut, `extra.priceMin` and `extra.priceMax` the whole span over
/// the list's price roll, the regional supply factor (down to the price curve's floor, when
/// RegionalSupply is on) and the standing tiers that can see the entry (their buy or sell price
/// factor, when TraderStanding is on). A catalog schematic has its flat price, min and max alike.
/// </summary>
internal static class PackTradeSources
{
    public const string TradingSystemType = "SeraphHorizons.Mod.Trading.TradingSystem";
    public const string EconomySystemType = "SeraphHorizons.Mod.Trading.Economy.EconomySystem";
    public const string ValuesSystemType = "SeraphHorizons.Mod.Trading.Values.ItemValuesSystem";
    public const string PricingType = "SeraphHorizons.Mod.Trading.Economy.Core.Pricing";
    public const string MachineSchematicPrefix = "seraphhorizons:schematic-";
    public static readonly AssetLocation GatesAsset = new(GearChain.Mod, "config/schematic-gates.json");

    /// <summary>What <c>extra.catalog</c> says for a schematic from the catalog.</summary>
    public const string SchematicsCatalog = "schematics";

    private sealed record Tier(string Code, string Name, double BuyFactor, double SellFactor, bool RareStock);

    private sealed record TraderRef(string From, string FromName, string EntityType);

    private sealed class Context
    {
        public required ICoreServerAPI Api;
        public required object Lists;
        public required MethodInfo ItemFor;
        public required MethodInfo ListBase;
        public required MethodInfo Listed;
        public required MethodInfo TradeUnit;
        public required object Rules;
        public required object Values;
        public required MethodInfo Lookup;
        public required double Roll;
        public required double SupplyFloor;
        /// <summary>Null when standing is off: every trader treats everyone as a stranger.</summary>
        public required List<Tier>? Tiers;
        public required bool RegionalSupply;
    }

    private static string Normalise(string code) => code.Contains(':') ? code : "game:" + code;

    private static object? Prop(object? o, string name) => GearChain.Prop(o, name);

    private static double Dbl(object? o, string name, double fallback = 0) => Prop(o, name) is { } v ? Convert.ToDouble(v) : fallback;

    /// <summary>Every pack trade source, by the item code it is a source of. Empty without the
    /// pack's mod or with its trade lists not loaded.</summary>
    public static List<(string Code, JObject Source)> Read(ICoreServerAPI api)
    {
        var found = new List<(string, JObject)>();
        if (Build(api) is not { } ctx) return found;
        var traders = Traders(api);
        var listsByType = (IEnumerable)Prop(ctx.Lists, "Lists")!;
        var sales = Sales(api);
        var saleCodes = sales.Select(s => s.Code).ToHashSet(StringComparer.Ordinal);
        int listed = 0, catalogued = 0;
        foreach (var kv in listsByType)
        {
            var type = (string)Prop(kv, "Key")!;
            var def = Prop(kv, "Value")!;
            if (!traders.TryGetValue(type, out var trader))
            {
                api.Logger.Warning("[seraphexport] pack trader type {0} has a trade list but no entity; its goods are not exported", type);
                continue;
            }
            listed += AddSide(ctx, found, Prop(def, "Selling")!, false, type, trader, saleCodes);
            listed += AddSide(ctx, found, Prop(def, "Buying")!, true, type, trader, saleCodes);
        }
        foreach (var sale in sales)
            catalogued += AddSale(ctx, found, sale, traders);
        api.Logger.Notification("[seraphexport] {0} pack trade source(s) from {1} trade list(s), {2} from the schematic catalog",
            listed, traders.Count, catalogued);
        return found;
    }

    private static Context? Build(ICoreServerAPI api)
    {
        if (GearChain.System(api, TradingSystemType) is not { } trading) return null;
        if (Prop(trading, "Lists") is not { } lists) return null;
        var economy = GearChain.System(api, EconomySystemType);
        var asm = trading.GetType().Assembly;
        var pricing = asm.GetType(PricingType);
        var valuesFor = asm.GetType(ValuesSystemType)?.GetMethod("For", BindingFlags.Public | BindingFlags.Static, [typeof(ICoreAPI)]);
        var values = valuesFor?.Invoke(null, [api]);
        var itemFor = lists.GetType().GetMethod("ItemFor");
        var listBase = pricing?.GetMethod("ListBase", BindingFlags.Public | BindingFlags.Static);
        var listed = pricing?.GetMethod("Listed", BindingFlags.Public | BindingFlags.Static);
        var tradeUnit = economy?.GetType().GetMethod("TradeUnit", BindingFlags.Public | BindingFlags.Static, [typeof(CollectibleObject)]);
        var rules = Prop(economy, "ListPrices");
        var lookup = values?.GetType().GetMethod("Lookup", [typeof(string)]);
        if (economy == null || itemFor == null || listBase == null || listed == null || tradeUnit == null || rules == null || values == null || lookup == null)
        {
            api.Logger.Warning("[seraphexport] the pack's trading is loaded but not as expected; its traders' goods are not exported");
            return null;
        }
        List<Tier>? tiers = null;
        var standing = Prop(trading, "Standing");
        if (Prop(standing, "Enabled") is true && Prop(Prop(standing, "Rules"), "Tiers") is IEnumerable list)
        {
            tiers = list.Cast<object>().Select(t =>
            {
                var code = (string)Prop(t, "Code")!;
                var unlocks = Prop(t, "Unlocks");
                return new Tier(code, Lang.Get($"{GearChain.Mod}:trading-standing-tier-{code}"),
                    Dbl(unlocks, "BuyPriceFactor", 1), Dbl(unlocks, "SellPriceFactor", 1), Prop(unlocks, "RareStock") is true);
            }).ToList();
            if (tiers.Count == 0) tiers = null;
        }
        bool regional = Prop(economy, "RegionalSupply") is true;
        return new Context
        {
            Api = api,
            Lists = lists,
            ItemFor = itemFor,
            ListBase = listBase,
            Listed = listed,
            TradeUnit = tradeUnit,
            Rules = rules,
            Values = values,
            Lookup = lookup,
            Roll = Dbl(rules, "Roll"),
            SupplyFloor = regional ? Dbl(Prop(Prop(Prop(economy, "Supply"), "Settings"), "Curve"), "Floor", 1) : 1,
            Tiers = tiers,
            RegionalSupply = regional,
        };
    }

    /// <summary>One entity per pack trader type with a list: <c>seraphhorizons:{trader|visitor}-{gender}-{type}-{climate}</c>,
    /// temperate and male first.</summary>
    private static Dictionary<string, TraderRef> Traders(ICoreServerAPI api)
    {
        var byType = new Dictionary<string, List<EntityProperties>>(StringComparer.Ordinal);
        foreach (var entity in api.World.EntityTypes)
        {
            if (entity?.Code?.Domain != GearChain.Mod) continue;
            var parts = entity.Code.Path.Split('-');
            if (parts.Length != 4 || parts[0] is not ("trader" or "visitor")) continue;
            if (!byType.TryGetValue(parts[2], out var list)) byType[parts[2]] = list = new();
            list.Add(entity);
        }
        var traders = new Dictionary<string, TraderRef>(StringComparer.Ordinal);
        foreach (var (type, list) in byType)
        {
            var pick = list
                .OrderBy(e => e.Code.Path.EndsWith("-temperate", StringComparison.Ordinal) ? 0 : 1)
                .ThenBy(e => e.Code.Path.Split('-')[1] == "male" ? 0 : 1)
                .ThenBy(e => e.Code.Path, StringComparer.Ordinal)
                .First();
            var kind = pick.Code.Path.Split('-')[0];
            var from = pick.Code.ToString();
            traders[type] = new TraderRef(from, Lang.GetMatching(pick.Code.Domain + ":item-creature-" + pick.Code.Path),
                $"{pick.Code.Domain}:{kind}-{type}");
        }
        return traders;
    }

    private static int AddSide(Context ctx, List<(string, JObject)> found, object side, bool traderBuys, string type, TraderRef trader,
        ISet<string> saleCodes)
    {
        int added = 0;
        var coreKeys = new HashSet<string>(StringComparer.Ordinal);
        var core = (IEnumerable)Prop(side, "Core")!;
        foreach (var e in core) coreKeys.Add((string)Prop(e, "Key")!);
        var tables = new List<(IEnumerable Entries, string List, string? Region)>
        {
            (core, "core", null),
            ((IEnumerable)Prop(Prop(side, "Rotating"), "List")!, "rotating", null),
        };
        foreach (var kv in (IEnumerable)Prop(side, "Regional")!)
        {
            var region = (string)Prop(kv, "Key")!;
            var regional = Prop(kv, "Value");
            tables.Add(((IEnumerable)Prop(regional, "Core")!, "core", region));
            tables.Add(((IEnumerable)Prop(regional, "Rotating")!, "rotating", region));
        }
        foreach (var (entries, listName, region) in tables)
        {
            foreach (var entry in entries)
            {
                // The trader stocks an entry once: the list's own core wins over the rest.
                if ((listName != "core" || region != null) && coreKeys.Contains((string)Prop(entry, "Key")!)) continue;
                if (Entry(ctx, entry, traderBuys, type, trader, listName, region, saleCodes) is not var (code, source)) continue;
                found.Add((code, source));
                added++;
            }
        }
        return added;
    }

    private static (string, JObject)? Entry(Context ctx, object entry, bool traderBuys, string type, TraderRef trader, string listName,
        string? region, ISet<string> saleCodes)
    {
        // Maps and leads are offers the trader makes up at a restock, priced by the maps system.
        if (Prop(entry, "Kind") is string) return null;
        if (saleCodes.Contains(Normalise((string)Prop(entry, "Code")!))) return null;
        bool playerSupplied = Prop(entry, "PlayerSupplied") is true;
        // A player-supplied good is shelved only by regional supply.
        if (!traderBuys && playerSupplied && !ctx.RegionalSupply) return null;
        int tier = Prop(entry, "StandingTier") is int t ? t : 0;
        bool rare = Prop(entry, "Rare") is true;
        if (ctx.Tiers == null)
        {
            // Standing off: everyone is a stranger, and a gated or rare entry is never shelved.
            if (tier > 0 || rare) return null;
        }
        else
        {
            if (rare)
            {
                int first = ctx.Tiers.FindIndex(x => x.RareStock);
                if (first < 0) return null;
                tier = Math.Max(tier, first);
            }
            if (tier >= ctx.Tiers.Count) return null;
        }

        if (ctx.ItemFor.Invoke(ctx.Lists, [entry]) is not TradeItem item) return null;
        if (!item.Resolve(ctx.Api.World, "seraphexport pack trade list", false) || item.ResolvedItemstack?.Collectible?.Code == null) return null;
        var collectible = item.ResolvedItemstack.Collectible;
        var code = collectible.Code.ToString();
        int stack = Math.Max(1, (int)Prop(entry, "StackSize")!);
        bool overridden = Prop(entry, "Price") is double;
        var lookup = ctx.Lookup.Invoke(ctx.Values, [code]);
        double value = Prop(lookup, "Source")?.ToString() == "Missing" ? 0 : Dbl(lookup, "Value");
        int tradeUnit = (int)ctx.TradeUnit.Invoke(null, [collectible])!;

        // Standing factors over the tiers that see the entry: the player buys (trader sells) at the
        // buy factor, sells (trader buys) at the sell factor.
        var seen = ctx.Tiers?.Skip(tier).Select(x => traderBuys ? x.SellFactor : x.BuyFactor).ToList() ?? [1.0];
        double atTier = seen[0];
        double roll = overridden ? 0 : ctx.Roll;
        var typical = Offer(ctx, entry, value, traderBuys, 1, stack, 1, atTier, tradeUnit);
        var low = Offer(ctx, entry, value, traderBuys, 1 - roll, stack, ctx.SupplyFloor, seen.Min(), tradeUnit);
        var high = Offer(ctx, entry, value, traderBuys, 1 + roll, stack, 1, seen.Max(), tradeUnit);

        // A cheap stack the trader buys by a bigger unit: every figure per the typical unit.
        int unit = typical?.Unit ?? stack;
        int Per(Offered? o) => o == null ? 1 : o.Unit == unit ? o.Price : Math.Max(1, (int)Math.Round(o.Worth * unit, MidpointRounding.AwayFromZero));

        var s = new JObject
        {
            ["type"] = traderBuys ? "traderBuys" : "traderSells",
            ["from"] = trader.From,
            ["fromName"] = trader.FromName,
            ["quantity"] = new JObject { ["avg"] = unit },
            ["price"] = Per(typical),
        };
        var extra = new JObject
        {
            ["entityType"] = trader.EntityType,
            ["traderType"] = type,
            ["list"] = listName,
            ["priceMin"] = Per(low),
            ["priceMax"] = Per(high),
        };
        if (region != null) extra["region"] = region;
        if (tier > 0 && ctx.Tiers != null)
        {
            extra["standingTier"] = tier;
            extra["standingTierName"] = ctx.Tiers[tier].Name;
        }
        if (rare) extra["rare"] = true;
        if (playerSupplied) extra["playerSupplied"] = true;
        if (!traderBuys && Prop(entry, "Stock") is { } stock)
        {
            var q = new JObject { ["avg"] = Json.Round((float)Dbl(stock, "Avg")) };
            if (Dbl(stock, "Var") != 0) q["var"] = Json.Round((float)Dbl(stock, "Var"));
            extra["stock"] = q;
        }
        if (Prop(entry, "Attributes") is JObject attrs && attrs.Count > 0) extra["attributes"] = attrs.DeepClone();
        s["extra"] = extra;
        return (code, s);
    }

    private sealed record Offered(int Unit, int Price, double Worth);

    /// <summary>The pack's own price of an entry (<c>Pricing.ListBase</c>, then <c>Pricing.Listed</c>);
    /// null when it has neither a value nor an override (it keeps its placeholder price of 1).</summary>
    private static Offered? Offer(Context ctx, object entry, double value, bool traderBuys, double roll, int stack, double supply,
        double modifiers, int tradeUnit)
    {
        if (ctx.ListBase.Invoke(null, [entry, value, traderBuys, roll, ctx.Rules]) is not double listPrice) return null;
        var offer = ctx.Listed.Invoke(null, [listPrice, stack, supply, modifiers, traderBuys, tradeUnit]);
        return new Offered((int)Prop(offer, "UnitSize")!, (int)Prop(offer, "UnitPrice")!, Dbl(offer, "Worth"));
    }

    // ---- The schematic catalog ----

    private sealed record Sale(string Code, string Mod, List<string> Sellers, int Tier, double? Price, string? Switch);

    /// <summary>The table's "sales", read from the asset (comments allowed), with each machine
    /// schematic's gate switch. A sale's <c>price</c> may be absent (lists that still carry the
    /// schematics): the seller's list entry's price is used then.</summary>
    private static List<Sale> Sales(ICoreServerAPI api)
    {
        var sales = new List<Sale>();
        if (api.Assets.TryGet(GatesAsset) is not { } asset) return sales;
        JObject table;
        try
        {
            table = JObject.Parse(asset.ToText(), new JsonLoadSettings { CommentHandling = CommentHandling.Ignore });
        }
        catch (Exception e)
        {
            api.Logger.Warning("[seraphexport] {0} does not parse ({1}); no schematic sale is exported", GatesAsset, e.Message);
            return sales;
        }
        var switches = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var gate in table["gates"] as JArray ?? [])
            if ((string?)gate["machine"] is { } machine && (string?)gate["switch"] is { } sw)
                switches[MachineSchematicPrefix + machine] = sw;
        foreach (var sale in table["sales"] as JArray ?? [])
        {
            if ((string?)sale["code"] is not { } code) continue;
            code = Normalise(code);
            var sellers = (sale["sellers"] as JArray ?? []).Select(x => (string?)x).OfType<string>().ToList();
            sales.Add(new Sale(code, (string?)sale["mod"] ?? "game", sellers, (int?)sale["tier"] ?? 0, (double?)sale["price"],
                switches.GetValueOrDefault(code)));
        }
        return sales;
    }

    private static bool ConfigSwitch(ICoreServerAPI api, string name)
    {
        var main = GearChain.System(api, GearChain.MainSystem);
        var config = main?.GetType().GetMethod("ConfigFor", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, [api]);
        return config?.GetType().GetProperty(name)?.GetValue(config) is not false;
    }

    private static int AddSale(Context ctx, List<(string, JObject)> found, Sale sale, Dictionary<string, TraderRef> traders)
    {
        var api = ctx.Api;
        if (!api.ModLoader.IsModEnabled(sale.Mod)) return 0;
        if (sale.Code.StartsWith(MachineSchematicPrefix, StringComparison.Ordinal))
        {
            if (!ConfigSwitch(api, "MachineSchematics")) return 0;
            if (sale.Switch != null && !ConfigSwitch(api, sale.Switch)) return 0;
        }
        var loc = new AssetLocation(sale.Code);
        CollectibleObject? collectible = api.World.GetItem(loc) is { IsMissing: false, Code: not null } i ? i
            : api.World.GetBlock(loc) is { IsMissing: false, Id: > 0, Code: not null } b ? b : null;
        if (collectible == null) return 0;
        int added = 0;
        foreach (var type in sale.Sellers)
        {
            if (!traders.TryGetValue(type, out var trader)) continue;
            double? price = sale.Price ?? ListPrice(ctx, type, sale.Code);
            if (price is not { } p)
            {
                api.Logger.Warning("[seraphexport] schematic {0} sold by {1} has no price in the catalog or the list; not exported", sale.Code, type);
                continue;
            }
            var s = new JObject
            {
                ["type"] = "traderSells",
                ["from"] = trader.From,
                ["fromName"] = trader.FromName,
                ["quantity"] = new JObject { ["avg"] = 1 },
                ["price"] = Json.Round((float)p),
            };
            var extra = new JObject
            {
                ["entityType"] = trader.EntityType,
                ["traderType"] = type,
                ["catalog"] = SchematicsCatalog,
                ["priceMin"] = Json.Round((float)p),
                ["priceMax"] = Json.Round((float)p),
            };
            // Standing off: the catalog is open to everyone.
            if (sale.Tier > 0 && ctx.Tiers != null && sale.Tier < ctx.Tiers.Count)
            {
                extra["standingTier"] = sale.Tier;
                extra["standingTierName"] = ctx.Tiers[sale.Tier].Name;
            }
            s["extra"] = extra;
            found.Add((collectible.Code.ToString(), s));
            added++;
        }
        return added;
    }

    /// <summary>The price override a seller's list gives a schematic (lists from before the
    /// catalog carry them), or null.</summary>
    private static double? ListPrice(Context ctx, string type, string code)
    {
        var def = ctx.Lists.GetType().GetMethod("For")?.Invoke(ctx.Lists, [type]);
        if (Prop(def, "Selling") is not { } side) return null;
        var entries = ((IEnumerable)Prop(side, "Core")!).Cast<object>()
            .Concat(((IEnumerable)Prop(Prop(side, "Rotating"), "List")!).Cast<object>());
        foreach (var kv in (IEnumerable)Prop(side, "Regional")!)
        {
            var regional = Prop(kv, "Value");
            entries = entries.Concat(((IEnumerable)Prop(regional, "Core")!).Cast<object>())
                .Concat(((IEnumerable)Prop(regional, "Rotating")!).Cast<object>());
        }
        foreach (var e in entries)
            if (Normalise((string)Prop(e, "Code")!) == code && Prop(e, "Price") is double p)
                return p;
        return null;
    }
}
