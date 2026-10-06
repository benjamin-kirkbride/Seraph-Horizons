using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SeraphHorizons.Mod.Ore;
using SeraphHorizons.Mod.Ore.Core;
using SeraphHorizons.Mod.Trading.Core;
using SeraphHorizons.Mod.Trading.Economy;
using SeraphHorizons.Mod.Trading.Economy.Core;
using SeraphHorizons.Mod.Trading.Glue;
using SeraphHorizons.Mod.Trading.Maps.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.Trading.Maps;

/// <summary>
/// Traders sell ore maps, gravel maps and leads (#455), switch
/// <see cref="SeraphHorizonsConfig.TraderMaps"/>. Registers the lead item class on both sides; on
/// the server it expands the lists' special entries (<see cref="TradeEntry.Kind"/> <c>oremap</c>,
/// <c>gravelmap</c>, <c>lead</c>; <see cref="TradingSystem.Offers"/>) into offers at each restock and
/// settles their sale.
///
/// <list type="bullet">
/// <item><b>Offers</b>: ore maps (the prospector's list): per metal the nearest unsold deposit of
/// <see cref="DepositService.Candidates"/> within <c>oreRadius</c>, up to <c>maxOreOffers</c>; a
/// gravel map (every list): the nearest unsold field within <c>gravelRadius</c>; leads (every list,
/// <see cref="LeadTargets"/>). With deposits in range but none left, a sold-out entry (stock 0,
/// shown unavailable). Prices from <c>config/trading/map-prices.json</c>.</item>
/// <item><b>Per player</b>: the shelf has no buyer, so an ore map offer shows the precision of the
/// player trading (<see cref="MapOffers.MaxPrecision"/> of their <c>mapTier</c>), re-set when the
/// trading player changes (<see cref="TradingGlueSystem.TradingPlayerPriced"/>); leads past the
/// nearest camp are shelved when the shelf's tier has <c>mapsToTraders</c> and sold only to a buyer
/// whose own tier has it.</item>
/// <item><b>A sale</b> (the game's ITradeableCollectible, <see cref="MapTradeHooks"/>): the deal is
/// refused if the deposit sold meanwhile or the buyer's standing does not reach the offer. The
/// player receives the offer as a pending stack; the deposit is reserved and verified
/// (<see cref="DepositService.Verify"/>, which may generate chunks), and the pending stack becomes
/// the map (<see cref="MapIssuer.Issue"/>, which marks it sold) or, if the deposit is gone or
/// worked out, is taken back and the price refunded. A lead to a camp not generated yet generates
/// its spot's chunk first, as <c>/sh trade tp</c> does.</item>
/// </list>
/// </summary>
public class MapsSystem : ModSystem
{
    public static readonly AssetLocation PricesAsset = new("seraphhorizons", "config/trading/map-prices.json");
    public static readonly AssetLocation OfferOreCode = MapIssuer.OreMapCode;
    public static readonly AssetLocation OfferGravelCode = MapIssuer.GravelMapCode;
    private static readonly AssetLocation Gear = new("game", "gear-rusty");

    private ICoreServerAPI? _sapi;
    private TradingSystem? _trading;
    private readonly HashSet<string> _reserved = new();
    private readonly Dictionary<(long Trader, string Offer), (int Price, string Uid)> _quotes = new();

    /// <summary>Maps and leads are sold here (server, switch on, the table loaded).</summary>
    public bool Active { get; private set; }

    public MapPriceTable Prices { get; private set; } = new();

    /// <summary>Deposits whose sale is being checked: no trader offers them meanwhile.</summary>
    public IReadOnlyCollection<string> Reserved => _reserved;

    public override double ExecuteOrder() => 0.68;

    public override void Start(ICoreAPI api)
    {
        api.RegisterItemClass(ItemTraderLead.ClassName, typeof(ItemTraderLead));
        ItemOreMap.Hooks = MapTradeHooks.Instance;
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        _sapi = api;
        if (!SeraphHorizonsSystem.ConfigFor(api).TraderMaps)
        {
            api.Logger.Notification("[seraphhorizons] Trader maps: off (TraderMaps in ModConfig/{0})", SeraphHorizonsSystem.ConfigFile);
            return;
        }
        _trading = TradingSystem.Of(api);
        if (_trading is null)
        {
            api.Logger.Warning("[seraphhorizons] Trader maps: no trading system; off");
            return;
        }
        var asset = api.Assets.TryGet(PricesAsset);
        try
        {
            Prices = asset is null ? throw new FileNotFoundException(PricesAsset.ToString())
                : JObject.Parse(asset.ToText(), new JsonLoadSettings { CommentHandling = CommentHandling.Ignore }).ToObject<MapPriceTable>()
                  ?? throw new InvalidDataException("empty");
        }
        catch (Exception e)
        {
            api.Logger.Warning("[seraphhorizons] Trader maps: {0} does not load ({1}); off", PricesAsset, e.Message);
            return;
        }
        foreach (string problem in Prices.Problems())
            api.Logger.Warning("[seraphhorizons] Trader maps: {0}: {1}", PricesAsset, problem);
        _trading.Offers = Expand;
        EntitySeraphTrader.Restocked += OnRestocked;
        TradingGlueSystem.TradingPlayerPriced += OnPriced;
        Active = true;
        api.Logger.Notification("[seraphhorizons] Trader maps: on (ore maps within {0}, gravel maps within {1}, leads {2} cells out)",
            Prices.OreRadius, Prices.GravelRadius, Prices.LeadCells);
    }

    public override void Dispose()
    {
        EntitySeraphTrader.Restocked -= OnRestocked;
        TradingGlueSystem.TradingPlayerPriced -= OnPriced;
        if (_trading?.Offers == Expand) _trading.Offers = null;
        ItemOreMap.Hooks = null;
    }

    private static string L(string key, params object[] args) => Lang.Get("seraphhorizons:" + key, args);

    private DepositService? Deposits => _sapi?.ModLoader.GetModSystem<OreSystem>()?.Deposits;

    private MapIssuer? Issuer => _sapi?.ModLoader.GetModSystem<OreSystem>()?.Maps;

    // ---- Offers at a restock ----

    /// <summary>A special entry's offers for this trader now (<see cref="TradingSystem.Offers"/>).</summary>
    public IEnumerable<TradeEntry> Expand(EntitySeraphTrader trader, TradeEntry entry)
    {
        int x = (int)trader.Pos.X, z = (int)trader.Pos.Z;
        switch (entry.Kind)
        {
            case MapOfferAttrs.OreMap when Deposits is { HasOre: true } deposits:
            {
                var options = deposits.Candidates(x, z, Prices.OreRadius).Select(Option);
                var (offers, soldOut) = MapOffers.PickOre(options, Prices.MaxOreOffers, _reserved);
                foreach (var o in offers)
                    yield return Entry(OfferOreCode, OreAttrs(o, MapPrecision.Rough), Prices.OrePrice(o.Metal, o.SizeClass, MapPrecision.Rough), 1, false);
                if (soldOut) yield return Entry(OfferOreCode, new JObject { [MapOfferAttrs.Offer] = MapOfferAttrs.SoldOut }, 1, 1, false);
                break;
            }
            case MapOfferAttrs.GravelMap when Deposits is { HasGravel: true } deposits:
            {
                var (offer, soldOut) = MapOffers.PickGravel(deposits.GravelFields(x, z, Prices.GravelRadius).Select(Option), _reserved);
                if (offer is { } o)
                    yield return Entry(OfferGravelCode, new JObject
                    {
                        [MapOfferAttrs.Offer] = MapOfferAttrs.GravelMap, [MapOfferAttrs.Deposit] = o.Id, [MapOfferAttrs.Metal] = PlacerCells.Kind,
                    }, Prices.GravelPrice(), 1, false);
                else if (soldOut) yield return Entry(OfferGravelCode, new JObject { [MapOfferAttrs.Offer] = MapOfferAttrs.SoldOut }, 1, 1, false);
                break;
            }
            case MapOfferAttrs.Lead when _trading is { GridReady: true } trading:
            {
                var standing = trading.Standing;
                bool extras = standing.UnlocksOfTier(standing.ShelfTierFor(trader)).MapsToTraders;
                foreach (var t in LeadTargets.Pick(TraderGrid.CellOf(x, z), x, z, Site, Prices.LeadCells, Prices.FarLeads, extras))
                    yield return Entry(ItemTraderLead.LeadCode, new JObject
                    {
                        [MapOfferAttrs.Offer] = MapOfferAttrs.Lead, [MapOfferAttrs.LeadKind] = LeadTargets.Code(t.Kind),
                        [MapOfferAttrs.Cell] = t.Kind == LeadKind.Settlement ? "" : t.Cell.ToString(), [MapOfferAttrs.Type] = t.Type,
                        [MapOfferAttrs.X] = t.X, [MapOfferAttrs.Z] = t.Z,
                    }, Prices.LeadPrice(t.Kind), 2, t.Optional);
                break;
            }
        }
    }

    private static DepositOption Option(DepositCandidate c) =>
        new(c.Key.Id, c.Metal, c.Distance, c.Record.State == DepositState.Unsold, c.Record.Tier?.ToString().ToLowerInvariant());

    private static JObject OreAttrs(DepositOption o, int precision)
    {
        var attrs = new JObject
        {
            [MapOfferAttrs.Offer] = MapOfferAttrs.OreMap, [MapOfferAttrs.Deposit] = o.Id, [MapOfferAttrs.Metal] = o.Metal,
            [MapOfferAttrs.Precision] = precision,
        };
        if (o.SizeClass != null) attrs[MapOfferAttrs.SizeTier] = o.SizeClass;
        return attrs;
    }

    private static TradeEntry Entry(AssetLocation code, JObject attrs, int price, int stock, bool optional) => new()
    {
        Type = "item",
        Code = code.ToString(),
        Attributes = attrs,
        AttributesKey = attrs.ToString(Formatting.None),
        StackSize = 1,
        Stock = new NatSpec(stock, 0),
        Price = new NatSpec(price, 0),
        Optional = optional,
    };

    /// <summary>A camp cell's site for a lead: the placed camp, else the spot it waits for; none if
    /// every spot failed.</summary>
    private CampSite? Site(CellKey cell)
    {
        var trading = _trading!;
        var record = trading.Camps!.Registry.Get(cell);
        if (record is { Status: CampStatus.Placed }) return new CampSite(cell, record.Type, record.X, record.Z);
        if (record is { Status: CampStatus.Failed }) return null;
        var spots = trading.Grid!.Spots(cell);
        if (spots.Count == 0) return null;
        var spot = spots[Math.Min(record?.Attempt ?? 0, spots.Count - 1)];
        return new CampSite(cell, trading.Grid.TypeOf(cell), spot.X, spot.Z);
    }

    private void OnRestocked(EntitySeraphTrader trader)
    {
        if (trader.Api != _sapi) return;
        bool any = false;
        foreach (var slot in trader.Inventory.SellingSlots)
        {
            if (slot.Itemstack?.Attributes.GetString(MapOfferAttrs.Offer) is not { } offer || slot.TradeItem is null) continue;
            any = true;
            if (offer == MapOfferAttrs.SoldOut) slot.TradeItem.Stock = 0;
        }
        if (!any) return;
        Price(trader, PlayerTrading(trader));
        Store(trader);
    }

    // ---- Per player ----

    private void OnPriced(EntitySeraphTrader trader, IPlayer? player)
    {
        if (trader.Api == _sapi) Price(trader, player);
    }

    /// <summary>Sets every offer's price (and an ore map's precision) for the player trading, or for
    /// nobody (precision 1); standing's price factor through the economy's modifiers.</summary>
    public void Price(EntitySeraphTrader trader, IPlayer? player)
    {
        int maxPrecision = player is null || _trading is null ? MapPrecision.Rough
            : MapOffers.MaxPrecision(_trading.Standing.UnlocksFor(player, trader).MapTier);
        var economy = EconomySystem.Of(trader.Api);
        foreach (var slot in trader.Inventory.SellingSlots)
        {
            if (slot.Itemstack is not { } stack || slot.TradeItem is not { } item) continue;
            var a = stack.Attributes;
            int price;
            switch (a.GetString(MapOfferAttrs.Offer))
            {
                case MapOfferAttrs.OreMap:
                    a.SetInt(MapOfferAttrs.Precision, maxPrecision);
                    price = Prices.OrePrice(a.GetString(MapOfferAttrs.Metal) ?? "", a.GetString(MapOfferAttrs.SizeTier), maxPrecision);
                    break;
                case MapOfferAttrs.GravelMap:
                    price = Prices.GravelPrice();
                    break;
                case MapOfferAttrs.Lead when LeadTargets.TryParse(a.GetString(MapOfferAttrs.LeadKind), out var kind):
                    price = Prices.LeadPrice(kind);
                    break;
                default:
                    continue;
            }
            double factor = economy is null ? 1
                : Pricing.Modifiers(economy.Modifiers, economy.Context(trader, stack.Collectible.Code.ToString(), false, null));
            item.Price = MapPriceTable.Round(price * factor);
            slot.MarkDirty();
        }
    }

    private IServerPlayer? PlayerTrading(EntitySeraphTrader trader) =>
        trader.WatchedAttributes.GetString(StandingPriceModifier.TradingPlayerAttr) is { } uid ? _sapi!.World.PlayerByUid(uid) as IServerPlayer : null;

    private static void Store(EntitySeraphTrader trader)
    {
        var store = new TreeAttribute();
        trader.Inventory.ToTreeAttributes(store);
        trader.WatchedAttributes["traderInventory"] = store;
        trader.WatchedAttributes.MarkPathDirty("traderInventory");
    }

    // ---- A sale ----

    private static string OfferId(ITreeAttribute a) =>
        a.GetString(MapOfferAttrs.Offer) == MapOfferAttrs.Lead
            ? $"lead:{a.GetString(MapOfferAttrs.LeadKind)}:{a.GetAsInt(MapOfferAttrs.X)},{a.GetAsInt(MapOfferAttrs.Z)}"
            : a.GetString(MapOfferAttrs.Deposit) ?? "";

    /// <summary>Before the money moves: whether this offer may be sold to the player trading.</summary>
    public EnumTransactionResult OnTryBuy(EntitySeraphTrader trader, ItemSlot cartSlot)
    {
        var a = cartSlot.Itemstack!.Attributes;
        string offer = a.GetString(MapOfferAttrs.Offer) ?? "";
        var player = PlayerTrading(trader);
        if (player is null || _trading is null) return EnumTransactionResult.Failure;
        if (offer == MapOfferAttrs.SoldOut)
        {
            Error(player, "trading-maps-error-soldout");
            return EnumTransactionResult.TraderNotEnoughSupplyOrDemand;
        }
        var unlocks = _trading.Standing.UnlocksFor(player, trader);
        switch (offer)
        {
            case MapOfferAttrs.OreMap or MapOfferAttrs.GravelMap:
            {
                if (!DepositKey.TryParse(a.GetString(MapOfferAttrs.Deposit), out var key) || Deposits is not { } deposits || Issuer is null)
                    return EnumTransactionResult.Failure;
                if (_reserved.Contains(key.Id) || deposits.Registry.Get(key).State != DepositState.Unsold)
                {
                    if (cartSlot is ItemSlotTrade { TradeItem: { } item }) item.Stock = 0;
                    Error(player, "trading-maps-error-sold");
                    return EnumTransactionResult.TraderNotEnoughSupplyOrDemand;
                }
                int max = MapOffers.MaxPrecision(unlocks.MapTier);
                if (offer == MapOfferAttrs.OreMap && a.GetAsInt(MapOfferAttrs.Precision, MapPrecision.Rough) > max)
                {
                    Error(player, "trading-maps-error-precision", max);
                    return EnumTransactionResult.Failure;
                }
                break;
            }
            case MapOfferAttrs.Lead:
                if (!LeadTargets.TryParse(a.GetString(MapOfferAttrs.LeadKind), out var kind)) return EnumTransactionResult.Failure;
                if (kind != LeadKind.Camp && !unlocks.MapsToTraders)
                {
                    Error(player, "trading-maps-error-lead");
                    return EnumTransactionResult.Failure;
                }
                break;
            default:
                return EnumTransactionResult.Failure;
        }
        int price = cartSlot is ItemSlotTrade { TradeItem: { } t } ? t.Price : 0;
        _quotes[(trader.EntityId, OfferId(a))] = (price, player.PlayerUID);
        return EnumTransactionResult.Success;
    }

    private static void Error(IServerPlayer player, string key, params object[] args) =>
        player.SendIngameError("seraphhorizons-maps", Lang.GetL(player.LanguageCode ?? Lang.DefaultLocale, "seraphhorizons:" + key, args));

    private static void Tell(IServerPlayer? player, string key, params object[] args) =>
        player?.SendMessage(GlobalConstants.GeneralChatGroup, Lang.GetL(player.LanguageCode ?? Lang.DefaultLocale, "seraphhorizons:" + key, args),
            EnumChatType.Notification);

    /// <summary>The money has moved and <paramref name="stack"/> is about to be handed to the buyer:
    /// it becomes pending, and the sale is settled (at once if nothing needs generating).</summary>
    public void OnBought(EntitySeraphTrader trader, ItemStack stack)
    {
        var a = stack.Attributes;
        string offerId = OfferId(a);
        var (price, uid) = _quotes.Remove((trader.EntityId, offerId), out var q) ? q
            : (0, trader.WatchedAttributes.GetString(StandingPriceModifier.TradingPlayerAttr) ?? "");
        string token = Guid.NewGuid().ToString("N");
        a.SetString(MapOfferAttrs.Pending, token);
        var sale = new Sale(trader, stack, uid, token, price);
        string offer = a.GetString(MapOfferAttrs.Offer) ?? "";
        switch (offer)
        {
            case MapOfferAttrs.OreMap or MapOfferAttrs.GravelMap when DepositKey.TryParse(a.GetString(MapOfferAttrs.Deposit), out var key)
                                                                       && Deposits is { } deposits && Issuer is { } issuer:
            {
                int precision = offer == MapOfferAttrs.OreMap ? a.GetAsInt(MapOfferAttrs.Precision, MapPrecision.Rough) : MapPrecision.Exact;
                _reserved.Add(key.Id);
                deposits.Verify(key, result =>
                {
                    _reserved.Remove(key.Id);
                    bool good = key.IsGravel ? result.Status == VerifyStatus.Field
                        : result.Status == VerifyStatus.Measured && !result.WorkedOut;
                    Settle(sale, good ? issuer.Issue(_sapi!.World.PlayerByUid(uid), key, precision) : null);
                });
                break;
            }
            case MapOfferAttrs.Lead when LeadTargets.TryParse(a.GetString(MapOfferAttrs.LeadKind), out var kind):
                if (kind == LeadKind.Settlement)
                    Settle(sale, Lead(kind, "", a.GetAsInt(MapOfferAttrs.X), 0, a.GetAsInt(MapOfferAttrs.Z), ""));
                else if (CellKey.TryParse(a.GetString(MapOfferAttrs.Cell) ?? "", out var cell))
                    ResolveCamp(cell, 0, record => Settle(sale, record is null ? null : Lead(kind, record.Type, record.X, record.Y, record.Z, cell.ToString())));
                else Settle(sale, null);
                break;
            default:
                Settle(sale, null);
                break;
        }
        sale.Inside = false;
        if (!sale.Done)
            Tell(_sapi!.World.PlayerByUid(uid) as IServerPlayer, offer == MapOfferAttrs.Lead ? "trading-maps-checking-lead" : "trading-maps-checking");
    }

    private sealed class Sale(EntitySeraphTrader trader, ItemStack stack, string uid, string token, int price)
    {
        public EntitySeraphTrader Trader { get; } = trader;
        public ItemStack Stack { get; } = stack;
        public string Uid { get; } = uid;
        public string Token { get; } = token;
        public int Price { get; } = price;
        public string Name { get; } = stack.GetName();
        public bool Inside { get; set; } = true;
        public bool Done { get; set; }
    }

    /// <summary>The sale's outcome: the real map or lead, or null for a refund. Inside the deal the
    /// stack about to be handed over is turned into it; later, the pending stack is found in the
    /// buyer's inventory and replaced (handed over anew if it is not there).</summary>
    private void Settle(Sale sale, ItemStack? real)
    {
        sale.Done = true;
        if (sale.Inside && real != null)
        {
            sale.Stack.Attributes = real.Attributes.Clone();
            return;
        }
        if (sale.Inside)
        {
            // The pending stack is handed over after this returns; take it back on the next tick.
            _sapi!.Event.RegisterCallback(_ => Deliver(sale, null), 1);
            return;
        }
        Deliver(sale, real);
    }

    private void Deliver(Sale sale, ItemStack? real)
    {
        var player = _sapi!.World.PlayerByUid(sale.Uid) as IServerPlayer;
        var slot = player is null ? null : PendingSlot(player, sale.Token);
        if (real != null)
        {
            if (slot != null)
            {
                slot.Itemstack = real;
                slot.MarkDirty();
            }
            else if (player?.Entity is { } entity && !entity.TryGiveItemStack(real))
                _sapi.World.SpawnItemEntity(real, entity.Pos.XYZ);
            Tell(player, "trading-maps-ready", real.GetName());
            return;
        }
        if (slot != null)
        {
            slot.Itemstack = null;
            slot.MarkDirty();
        }
        if (sale.Price > 0)
        {
            if (player?.Entity is { } entity && _sapi.World.GetItem(Gear) is { } gear)
                InventoryTrader.GiveOrDrop(entity, new ItemStack(gear, sale.Price), sale.Price, null);
            if (sale.Trader.Alive && sale.Trader.Inventory is { } inventory) inventory.DeductFromTrader(sale.Price);
        }
        Tell(player, "trading-maps-refunded", sale.Name, sale.Price);
    }

    private static ItemSlot? PendingSlot(IServerPlayer player, string token)
    {
        foreach (var inv in player.InventoryManager.InventoriesOrdered)
        {
            if (inv.ClassName == GlobalConstants.creativeInvClassName) continue;
            foreach (var slot in inv)
                if (slot.Itemstack?.Attributes.GetString(MapOfferAttrs.Pending) == token)
                    return slot;
        }
        return null;
    }

    /// <summary>The camp of a cell, placed if need be: its pending spot's chunk is generated (which
    /// places the camp or moves to the next spot) until it is placed or failed.</summary>
    private void ResolveCamp(CellKey cell, int round, Action<CampRecord?> done)
    {
        var trading = _trading!;
        var record = trading.Camps!.Registry.Get(cell);
        if (record is { Status: CampStatus.Placed })
        {
            done(record);
            return;
        }
        var spots = trading.Grid!.Spots(cell);
        if (record is { Status: CampStatus.Failed } || spots.Count == 0 || round > TraderGrid.Attempts)
        {
            done(null);
            return;
        }
        var spot = spots[Math.Min(record?.Attempt ?? 0, spots.Count - 1)];
        _sapi!.WorldManager.LoadChunkColumnPriority(spot.ChunkX, spot.ChunkZ, new ChunkLoadOptions
        {
            OnLoaded = () => ResolveCamp(cell, round + 1, done),
        });
    }

    private ItemStack? Lead(LeadKind kind, string type, int x, int y, int z, string cell)
    {
        if (_sapi!.World.GetItem(ItemTraderLead.LeadCode) is not { } item) return null;
        var stack = new ItemStack(item);
        var a = stack.Attributes;
        a.SetString(MapOfferAttrs.LeadKind, LeadTargets.Code(kind));
        a.SetString(MapOfferAttrs.Type, type);
        a.SetString(MapOfferAttrs.Cell, cell);
        a.SetInt(MapOfferAttrs.X, x);
        a.SetInt(MapOfferAttrs.Y, y);
        a.SetInt(MapOfferAttrs.Z, z);
        return stack;
    }
}
