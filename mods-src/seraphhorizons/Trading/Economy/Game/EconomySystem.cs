using HarmonyLib;
using SeraphHorizons.Mod.Trading.Core;
using SeraphHorizons.Mod.Trading.Economy.Core;
using SeraphHorizons.Mod.Trading.Values;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.Trading.Economy;

/// <summary>
/// The trading economy (#450, #451): a price for everything, a side budget, and regional supply.
///
/// <list type="bullet">
/// <item><b>Everything has a price</b> (switch <see cref="SeraphHorizonsConfig.EverythingHasAPrice"/>):
/// our traders take any item, priced by <see cref="Pricing.OffList"/> and paid from a side budget
/// (<see cref="SideBudgetAttr"/>), except what <see cref="TraderRelations.Refused"/> names, money,
/// and goods worth nothing. Hooked in <see cref="EconomyPatches"/>.</item>
/// <item><b>Regional supply</b> (switch <see cref="SeraphHorizonsConfig.RegionalSupply"/>): a
/// <see cref="SupplyBook"/> saved with the world, ticked once a calendar day, fed by every deal at
/// our traders; its factor scales every price, and it is the <see cref="ISupplyGate"/> that shelves
/// player-supplied goods.</item>
/// </list>
///
/// Prices are computed the same way on both sides (the client shows what it will be paid) from what
/// both know: the value table, the lists, the relations, and per trader the watched attributes
/// <see cref="PricedAttr"/>, <see cref="SideBudgetAttr"/> and <see cref="SupplyFactorsAttr"/> (the
/// supply factor of every item in its region that is not 1), which the server keeps current.
/// </summary>
public class EconomySystem : ModSystem
{
    public const string PricedAttr = "seraphhorizons:everythingpriced";
    public const string SideBudgetAttr = "seraphhorizons:sidebudget";
    public const string SupplyFactorsAttr = "seraphhorizons:supplyfactors";
    public const string SaveKey = "seraphhorizons:supply";
    public const string SaveDayKey = "seraphhorizons:supplyday";
    public static readonly AssetLocation RelationsAsset = new("seraphhorizons", "config/trading/trader-relations.json");

    // Patched once per process (singleplayer runs both sides in one) and unpatched by the last side out.
    private static readonly object PatchLock = new();
    private static Harmony? s_harmony;
    private static int s_patchUsers;
    private bool _patched;

    private ICoreAPI? _api;
    private ICoreServerAPI? _sapi;
    private long _tickListener;
    private int _lastDay = int.MinValue;

    public static EconomySystem? Of(ICoreAPI? api) => api?.ModLoader.GetModSystem<EconomySystem>();

    public override double ExecuteOrder() => 0.65;

    public TraderRelations Relations { get; private set; } = new();

    public BuyerIndex Buyers { get; private set; } = new();

    /// <summary>Price factors after base, fit and supply; standing (#452, #463) adds itself here, on
    /// both sides.</summary>
    public List<IPriceModifier> Modifiers { get; } = [];

    /// <summary>Regional supply (server).</summary>
    public SupplyBook Supply { get; private set; } = new();

    /// <summary>The switches as this server has them.</summary>
    public bool EverythingHasAPrice { get; private set; }
    public bool RegionalSupply { get; private set; }

    /// <summary>Raised for every simulated day of <c>/sh trade simulate</c>, after supply has ticked,
    /// with the book's day: orders and deliveries (#453, #454) advance their own clocks here.</summary>
    public event Action<int>? SimulatedDay;

    public override void Start(ICoreAPI api)
    {
        _api = api;
        lock (PatchLock)
        {
            if (s_patchUsers++ == 0)
            {
                s_harmony = new Harmony(EconomyPatches.HarmonyId);
                EconomyPatches.Patch(s_harmony);
            }
            _patched = true;
        }
    }

    public override void AssetsLoaded(ICoreAPI api)
    {
        var asset = api.Assets.TryGet(RelationsAsset);
        if (asset is null)
        {
            api.Logger.Warning("[seraphhorizons] Trading economy: {0} is missing; every off-list good is unrelated", RelationsAsset);
            return;
        }
        try
        {
            Relations = TraderRelations.Parse(asset.ToText());
            foreach (string problem in Relations.Problems(TraderTypes.All.ToList()))
                api.Logger.Warning("[seraphhorizons] Trading economy: trader relations: {0}", problem);
        }
        catch (Exception e)
        {
            api.Logger.Error("[seraphhorizons] Trading economy: {0} does not parse: {1}", RelationsAsset, e.Message);
        }
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        // The fit needs every type's buying list; the client has the lists (config assets are
        // universal) and, once the level is finalized, the items they name.
        api.Event.LevelFinalize += () => Buyers = IndexOf(TradeLists.Load(api));
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        _sapi = api;
        var config = SeraphHorizonsSystem.ConfigFor(api);
        EverythingHasAPrice = config.EverythingHasAPrice;
        RegionalSupply = config.RegionalSupply;
        Supply = new SupplyBook(Settings(config));
        api.Event.SaveGameLoaded += LoadSupply;
        api.Event.GameWorldSave += SaveSupply;
        api.Event.ServerRunPhase(EnumServerRunPhase.GameReady, () =>
        {
            if (TradingSystem.Of(api)?.Lists is { } lists) Buyers = IndexOf(lists);
            if (RegionalSupply && TradingSystem.Of(api) is { } trading) trading.SupplyGate = new RegionalSupplyGate(this);
            api.Logger.Notification("[seraphhorizons] Trading economy: everything has a price {0}, regional supply {1} ({2} items bought by some trader)",
                EverythingHasAPrice ? "on" : "off", RegionalSupply ? "on" : "off", Buyers.Count);
        });
        EntitySeraphTrader.Restocked += OnRestocked;
        if (RegionalSupply) _tickListener = api.Event.RegisterGameTickListener(_ => DailyTick(), 2000);
        EconomyCommands.Register(api, this);
    }

    public static SupplySettings Settings(SeraphHorizonsConfig config) => new()
    {
        HalfLifeDays = config.SupplyHalfLifeDays,
        SpreadFraction = config.SupplySpreadFraction,
    };

    private static BuyerIndex IndexOf(TradeLists lists)
    {
        var index = new BuyerIndex();
        foreach (var (type, def) in lists.Lists)
        {
            var side = def.Buying;
            foreach (var e in side.Core.Concat(side.Rotating.List).Concat(side.Regional.Values.SelectMany(r => r.Core.Concat(r.Rotating))))
                index.Add(e.Code, type);
        }
        return index;
    }

    private void LoadSupply()
    {
        var api = _sapi!;
        var settings = Settings(SeraphHorizonsSystem.ConfigFor(api));
        try
        {
            var json = api.WorldManager.SaveGame.GetData<string>(SaveKey);
            Supply = json is null ? new SupplyBook(settings) : SupplyBook.FromJson(json, settings);
        }
        catch (Exception e)
        {
            api.Logger.Error("[seraphhorizons] Trading economy: the saved supply levels do not load, starting empty: {0}", e.Message);
            Supply = new SupplyBook(settings);
        }
        _lastDay = api.WorldManager.SaveGame.GetData(SaveDayKey, int.MinValue);
    }

    private void SaveSupply()
    {
        var api = _sapi!;
        api.WorldManager.SaveGame.StoreData(SaveKey, Supply.ToJson());
        api.WorldManager.SaveGame.StoreData(SaveDayKey, _lastDay);
    }

    /// <summary>Ticks supply once for every calendar day that has passed (at most 30 at once, after a
    /// long sleep or a time skip), then refreshes every loaded trader.</summary>
    private void DailyTick()
    {
        var api = _sapi!;
        int today = (int)Math.Floor(api.World.Calendar.TotalDays);
        if (_lastDay == int.MinValue || _lastDay > today) _lastDay = today;
        if (_lastDay == today) return;
        int days = Math.Min(30, today - _lastDay);
        for (int i = 0; i < days; i++) Supply.Tick();
        _lastDay = today;
        RefreshTraders();
    }

    /// <summary>Advances supply by <paramref name="days"/> days (each raising <see cref="SimulatedDay"/>),
    /// and the loaded traders' restock clocks by as much: each restocks on its next tick for the weeks
    /// that passed, as after a long absence.</summary>
    public int Simulate(int days)
    {
        for (int i = 0; i < days; i++)
        {
            Supply.Tick();
            SimulatedDay?.Invoke(Supply.Day);
        }
        int traders = 0;
        foreach (var t in LoadedTraders())
        {
            double last = t.WatchedAttributes.GetDouble("lastRefreshTotalDays", t.World.Calendar.TotalDays);
            t.WatchedAttributes.SetDouble("lastRefreshTotalDays", last - days);
            traders++;
        }
        RefreshTraders();
        return traders;
    }

    public IEnumerable<EntitySeraphTrader> LoadedTraders() =>
        _sapi?.World.LoadedEntities.Values.OfType<EntitySeraphTrader>().Where(t => t.State != EnumEntityState.Despawned).ToList()
        ?? [];

    /// <summary>Re-syncs supply and re-prices the loaded traders (those of one supply region, or all).</summary>
    public void RefreshTraders(string? region = null)
    {
        foreach (var t in LoadedTraders())
            if (region is null || RegionOf(t) == region)
                Refresh(t, broadcast: true);
    }

    private void OnRestocked(EntitySeraphTrader trader)
    {
        if (trader.Api != _sapi) return;
        trader.WatchedAttributes.SetBool(PricedAttr, EverythingHasAPrice);
        if (EverythingHasAPrice)
        {
            var wallet = TradingSystem.Of(trader.Api)?.Lists?.For(trader.TraderType)?.WalletFor(0) ?? new NatSpec(60, 10);
            SetSideBudget(trader, SideBudget.RefillTo(wallet.Avg));
        }
        Refresh(trader, broadcast: false);
    }

    /// <summary>The trader's supply factors from the book, then its shelf prices from them.</summary>
    public void Refresh(EntitySeraphTrader trader, bool broadcast)
    {
        SyncSupply(trader);
        if (!EverythingHasAPrice && !RegionalSupply) return;
        Reprice(trader);
        var store = new TreeAttribute();
        trader.Inventory.ToTreeAttributes(store);
        trader.WatchedAttributes["traderInventory"] = store;
        trader.WatchedAttributes.MarkPathDirty("traderInventory");
        if (broadcast) _sapi?.Network.BroadcastEntityPacket(trader.EntityId, 1234, store.ToBytes());
    }

    private void SyncSupply(EntitySeraphTrader trader)
    {
        if (!RegionalSupply)
        {
            if (trader.WatchedAttributes.HasAttribute(SupplyFactorsAttr)) trader.WatchedAttributes.RemoveAttribute(SupplyFactorsAttr);
            return;
        }
        var tree = new TreeAttribute();
        foreach (var (item, entry) in Supply.In(RegionOf(trader)))
        {
            double f = Supply.Settings.Curve.Factor(entry.Level);
            if (f < 0.999) tree.SetDouble(item, Math.Round(f, 4));
        }
        trader.WatchedAttributes[SupplyFactorsAttr] = tree;
        trader.WatchedAttributes.MarkPathDirty(SupplyFactorsAttr);
    }

    /// <summary>Prices every listed slot from its entry: list price × supply × modifiers, a buying
    /// price capped by the sell-back share of what the trader asks for the same item. Vanilla's
    /// rolled spread is dropped: a listed price is its list average.</summary>
    private void Reprice(EntitySeraphTrader trader)
    {
        var system = TradingSystem.Of(trader.Api);
        if (system?.Lists?.For(trader.TraderType) is not { } def) return;
        var resolved = TradeListResolver.Resolve(def, trader.Region, trader.StockTier);
        var entries = new Dictionary<string, TradeEntry>();
        foreach (var e in resolved.Selling.Core.Concat(resolved.Selling.Rotating)) entries.TryAdd("s" + e.Key, e);
        foreach (var e in resolved.Buying.Core.Concat(resolved.Buying.Rotating)) entries.TryAdd("b" + e.Key, e);
        Price(trader, trader.Inventory.SellingSlots, EntitySeraphTrader.SellingKeysAttr, "s", entries, false);
        Price(trader, trader.Inventory.BuyingSlots, EntitySeraphTrader.BuyingKeysAttr, "b", entries, true);
    }

    private void Price(EntitySeraphTrader trader, ItemSlotTrade[] slots, string keysAttr, string prefix, Dictionary<string, TradeEntry> entries, bool traderBuys)
    {
        var keys = (trader.WatchedAttributes[keysAttr] as StringArrayAttribute)?.value ?? [];
        for (int i = 0; i < slots.Length && i < keys.Length; i++)
        {
            var slot = slots[i];
            if (slot.Itemstack is null || slot.TradeItem is null || !entries.TryGetValue(prefix + keys[i], out var entry) || entry.Price is null) continue;
            string code = slot.Itemstack.Collectible.Code.ToString();
            var context = Context(trader, code, traderBuys, null);
            var offer = Pricing.Listed(entry.Price.Avg, slot.TradeItem.Stack?.StackSize ?? entry.StackSize, SupplyFactor(trader, code),
                Pricing.Modifiers(Modifiers, context), traderBuys ? SellPricePerItem(trader, code) : null, traderBuys);
            slot.TradeItem.Price = offer.UnitPrice;
        }
    }

    // ---- Prices, on both sides ----

    public static string RegionOf(Entity trader) => SupplyRegion.KeyOf(trader.Pos.X, trader.Pos.Z);

    public static bool IsPriced(EntitySeraphTrader trader) => trader.WatchedAttributes.GetBool(PricedAttr);

    public static int SideBudgetOf(EntitySeraphTrader trader) => trader.WatchedAttributes.GetInt(SideBudgetAttr);

    public static void SetSideBudget(EntitySeraphTrader trader, int gears)
    {
        trader.WatchedAttributes.SetInt(SideBudgetAttr, Math.Max(0, gears));
        trader.WatchedAttributes.MarkPathDirty(SideBudgetAttr);
    }

    /// <summary>The trader's supply factor for an item, as the server last synced it (1 if none).</summary>
    public static double SupplyFactor(EntitySeraphTrader trader, string code) =>
        trader.WatchedAttributes[SupplyFactorsAttr] is ITreeAttribute tree && tree.HasAttribute(code) ? tree.GetDouble(code, 1) : 1;

    /// <summary>The lowest price per item the trader asks for an item it sells, or null.</summary>
    public static double? SellPricePerItem(EntitySeraphTrader trader, string code)
    {
        double? best = null;
        foreach (var slot in trader.Inventory.SellingSlots)
        {
            if (slot.Itemstack?.Collectible?.Code?.ToString() != code || slot.TradeItem is not { Price: > 0 } item) continue;
            double per = item.Price / (double)Math.Max(1, item.Stack?.StackSize ?? 1);
            best = best is null ? per : Math.Min(best.Value, per);
        }
        return best;
    }

    public PriceContext Context(EntitySeraphTrader trader, string code, bool traderBuys, string? playerUid) =>
        new(code, trader.TraderType, RegionOf(trader), traderBuys, trader.EntityId, playerUid);

    /// <summary>What the trader offers for a stack its list does not buy.</summary>
    public Offer QuoteOffList(EntitySeraphTrader trader, ItemStack stack, string? playerUid = null)
    {
        var collectible = stack.Collectible;
        string code = collectible.Code.ToString();
        if (Relations.IsRefused(code)) return Offer.Refused(Refusal.MapOrLead);
        if (collectible.Attributes?["currency"]?.Exists == true) return Offer.Refused(Refusal.Currency);
        var values = ItemValuesSystem.For(trader.Api);
        double value = values.ValueOf(code);
        // A worn tool is worth its remaining share.
        int max = collectible.GetMaxDurability(stack);
        if (max > 1) value *= Math.Clamp(collectible.GetRemainingDurability(stack) / (double)max, 0, 1);
        var context = Context(trader, code, true, playerUid);
        return Pricing.OffList(value, values.IsWorthless(code), Relations.Fit(trader.TraderType, Buyers.BuyersOf(code)),
            SupplyFactor(trader, code), Pricing.Modifiers(Modifiers, context), collectible.MaxStackSize, SellPricePerItem(trader, code));
    }

    /// <summary>An item's value in the table (0 when worthless or unknown).</summary>
    public double ValueOf(string code) => _api is null ? 0 : ItemValuesSystem.For(_api).ValueOf(code);

    /// <summary>What one item adds to (or takes from) its supply level: its table value, else the price
    /// per item it traded at.</summary>
    public double SupplyWeight(ICoreAPI api, CollectibleObject collectible, double pricePerItem)
    {
        double value = ItemValuesSystem.For(api).ValueOf(collectible.Code.ToString());
        return Supply.Weight(value > 0 ? value : pricePerItem, collectible.MaxStackSize);
    }

    public override void Dispose()
    {
        EntitySeraphTrader.Restocked -= OnRestocked;
        if (_sapi != null && _tickListener != 0) _sapi.Event.UnregisterGameTickListener(_tickListener);
        if (_patched)
        {
            lock (PatchLock)
            {
                if (--s_patchUsers == 0)
                {
                    s_harmony?.UnpatchAll(EconomyPatches.HarmonyId);
                    s_harmony = null;
                }
            }
            _patched = false;
        }
    }
}

/// <summary>Shelves a player-supplied entry when its item's level in the trader's supply region
/// reaches the threshold, with stock scaling with the level (<see cref="SupplyBook.ShelfStock"/>).</summary>
public sealed class RegionalSupplyGate(EconomySystem economy) : ISupplyGate
{
    public int Stock(TraderContext trader, TradeEntry entry)
    {
        string code = BuyerIndex.FullCode(entry.Code);
        double level = economy.Supply.Level(SupplyRegion.KeyOf(trader.X, trader.Z), code);
        if (level <= 0) return 0;
        double value = economy.ValueOf(code);
        if (value <= 0 && entry.Price is { Avg: > 0 } price) value = price.Avg / Math.Max(1, entry.StackSize);
        return economy.Supply.ShelfStock(level, value * Math.Max(1, entry.StackSize), entry.Stock?.Avg ?? 1);
    }
}
