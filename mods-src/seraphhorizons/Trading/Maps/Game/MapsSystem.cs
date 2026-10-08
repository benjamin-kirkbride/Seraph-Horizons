using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SeraphHorizons.Mod.Ore;
using SeraphHorizons.Mod.Ore.Core;
using SeraphHorizons.Mod.Trading.Core;
using SeraphHorizons.Mod.Trading.Economy;
using SeraphHorizons.Mod.Trading.Economy.Core;
using SeraphHorizons.Mod.Trading.Glue;
using SeraphHorizons.Mod.Trading.Maps.Core;
using SeraphHorizons.Mod.Trading.Orders;
using SeraphHorizons.Mod.Trading.Standing;
using SeraphHorizons.Mod.Trading.Standing.Core;
using Vintagestory.API.MathTools;
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
/// gravel map (every list): the nearest unsold field within <c>gravelRadius</c>; the settlement
/// ground lead (every list, when the shelf's tier has <c>mapsToTraders</c>). With deposits in range
/// but none left, a sold-out entry (stock 0, shown unavailable). Prices from
/// <c>config/trading/map-prices.json</c>.</item>
/// <item><b>Camp leads</b> are not on the shelf: they are per buyer (<see cref="CampLeadsFor"/>,
/// <see cref="CampLeads"/>), listed on the trade window's Maps &amp; leads tab and bought there
/// (<see cref="BuyCampLead"/>), the group's history in <see cref="Leads"/> (saved as
/// <see cref="LeadsSaveKey"/>).</item>
/// <item><b>Per player</b>: the shelf has no buyer, so an ore map offer shows the precision of the
/// player trading (<see cref="MapOffers.MaxPrecision"/> of their <c>mapTier</c>), re-set when the
/// trading player changes (<see cref="TradingGlueSystem.TradingPlayerPriced"/>); leads past the
/// settlement lead is shelved when the shelf's tier has <c>mapsToTraders</c> and sold only to a buyer
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
    public const string LeadsSaveKey = "seraphhorizons:leads";
    public static readonly AssetLocation OfferOreCode = MapIssuer.OreMapCode;
    public static readonly AssetLocation OfferGravelCode = MapIssuer.GravelMapCode;
    private static readonly AssetLocation Gear = new("game", "gear-rusty");

    private ICoreServerAPI? _sapi;
    private TradingSystem? _trading;
    private readonly HashSet<string> _reserved = new();
    private readonly Dictionary<(long Trader, string Offer), (int Price, string Uid)> _quotes = new();
    /// <summary>Camp leads being drawn (their cell's camp generating), by buyer: not offered again meanwhile.</summary>
    private readonly HashSet<(string Uid, CellKey Cell)> _drawing = new();
    /// <summary>Cells a sale found no camp for (every spot missed): skipped from then on.</summary>
    private readonly HashSet<CellKey> _noCamp = new();

    /// <summary>Every group's camp lead history (saved with the world).</summary>
    public LeadBook Leads { get; private set; } = new();

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
        api.Event.SaveGameLoaded += LoadLeads;
        api.Event.GameWorldSave += () => api.WorldManager.SaveGame.StoreData(LeadsSaveKey, Leads.ToJson());
        _trading.Offers = Expand;
        EntitySeraphTrader.Restocked += OnRestocked;
        TradingGlueSystem.TradingPlayerPriced += OnPriced;
        EntitySeraphTrader.Met += OnMet;
        Active = true;
        api.Logger.Notification("[seraphhorizons] Trader maps: on (ore maps within {0}, gravel maps within {1}, camp leads {2})",
            Prices.OreRadius, Prices.GravelRadius,
            string.Join(", ", Prices.CampLeads.Tiers.Select(t => $"{t.Key} {t.Value.Maps} within {t.Value.Radius}")));
    }

    private void LoadLeads()
    {
        try
        {
            Leads = LeadBook.FromJson(_sapi!.WorldManager.SaveGame.GetData<string>(LeadsSaveKey), out var problem);
            if (problem != null) _sapi.Logger.Warning("[seraphhorizons] Trader maps: the saved lead history {0}; starting afresh", problem);
        }
        catch (Exception e)
        {
            _sapi!.Logger.Warning("[seraphhorizons] Trader maps: the saved lead history does not load ({0}); starting afresh", e.Message);
            Leads = new LeadBook();
        }
    }

    public override void Dispose()
    {
        EntitySeraphTrader.Restocked -= OnRestocked;
        TradingGlueSystem.TradingPlayerPriced -= OnPriced;
        EntitySeraphTrader.Met -= OnMet;
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
                        [MapOfferAttrs.Distance] = Math.Round(o.Distance),
                    }, Prices.GravelPrice(), 1, false);
                else if (soldOut) yield return Entry(OfferGravelCode, new JObject { [MapOfferAttrs.Offer] = MapOfferAttrs.SoldOut }, 1, 1, false);
                break;
            }
            case MapOfferAttrs.Lead when _trading is { GridReady: true } trading:
            {
                // Camp leads are per buyer, off the shelf (CampLeadsFor); the shelf keeps the
                // settlement ground's lead, for the tier with mapsToTraders.
                var standing = trading.Standing;
                if (!standing.UnlocksOfTier(standing.ShelfTierFor(trader)).MapsToTraders) break;
                var (sx, sz) = LeadTargets.Settlement(x, z);
                yield return Entry(ItemTraderLead.LeadCode, new JObject
                {
                    [MapOfferAttrs.Offer] = MapOfferAttrs.Lead, [MapOfferAttrs.LeadKind] = LeadTargets.Code(LeadKind.Settlement),
                    [MapOfferAttrs.Cell] = "", [MapOfferAttrs.Type] = "", [MapOfferAttrs.X] = sx, [MapOfferAttrs.Z] = sz,
                }, Prices.SettlementPrice(), 2, true);
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
            [MapOfferAttrs.Precision] = precision, [MapOfferAttrs.Distance] = Math.Round(o.Distance),
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

    /// <summary>A camp cell's site for a lead: the placed camp, else the spot it waits for next; none
    /// if every spot missed (or a sale found none there).</summary>
    private CampSite? Site(CellKey cell)
    {
        var trading = _trading!;
        if (_noCamp.Contains(cell)) return null;
        var record = trading.Camps!.Registry.Get(cell);
        if (record is { Status: CampStatus.Placed }) return new CampSite(cell, record.Type, record.X, record.Z);
        var spots = trading.Grid!.Spots(cell);
        if (CampRegistry.NextSpot(record, spots.Count) is not { } next) return null;
        var spot = spots[next];
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
                case MapOfferAttrs.OreMap or MapOfferAttrs.GravelMap when Gone(a):
                    // Sold, being sold, or the cell turned out to have none since the restock.
                    item.Stock = 0;
                    slot.MarkDirty();
                    continue;
                case MapOfferAttrs.OreMap:
                    a.SetInt(MapOfferAttrs.Precision, maxPrecision);
                    price = Prices.OrePrice(a.GetString(MapOfferAttrs.Metal) ?? "", a.GetString(MapOfferAttrs.SizeTier), maxPrecision);
                    break;
                case MapOfferAttrs.GravelMap:
                    price = Prices.GravelPrice();
                    break;
                case MapOfferAttrs.Lead when LeadTargets.TryParse(a.GetString(MapOfferAttrs.LeadKind), out var kind) && kind == LeadKind.Settlement:
                    price = Prices.SettlementPrice();
                    break;
                case MapOfferAttrs.Lead:
                    // A camp lead shelved before camp leads moved off the shelf: gone at the next restock.
                    item.Stock = 0;
                    slot.MarkDirty();
                    continue;
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

    /// <summary>Whether a deposit offer's deposit is no longer to be had: sold, being sold, or its
    /// cell turned out to have none (every spot failed) since the shelf was stocked.</summary>
    private bool Gone(ITreeAttribute a) =>
        DepositKey.TryParse(a.GetString(MapOfferAttrs.Deposit), out var key) && Deposits is { } deposits
        && (_reserved.Contains(key.Id) || deposits.Registry.Get(key).State != DepositState.Unsold || deposits.Candidate(key) is null);

    /// <summary>
    /// Why this map or lead offer may not be sold to <paramref name="player"/> now, as a lang key (mod
    /// domain) and its arguments, or null: sold out; the deposit sold, being sold, or gone (the shelf's
    /// stock is set to 0 then); an ore map above the player's precision; a further lead without their
    /// own <c>mapsToTraders</c>; or a map they have already (<see cref="MapMarksSystem.Check"/>: its
    /// target marked on their map as precisely or more, or a copy carried). The trade window asks
    /// before a buy (so nothing is paid), and the deal's own hook (<see cref="OnTryBuy"/>) again.
    /// </summary>
    public (string Key, object[] Args)? Refusal(EntitySeraphTrader trader, IPlayer player, ItemStack stack, ItemSlotTrade? shelf)
    {
        var a = stack.Attributes;
        string offer = a.GetString(MapOfferAttrs.Offer) ?? "";
        if (_trading is null) return ("trading-window-failed", []);
        if (offer == MapOfferAttrs.SoldOut) return ("trading-maps-error-soldout", []);
        var unlocks = _trading.Standing.UnlocksFor(player, trader);
        switch (offer)
        {
            case MapOfferAttrs.OreMap or MapOfferAttrs.GravelMap:
            {
                if (!DepositKey.TryParse(a.GetString(MapOfferAttrs.Deposit), out var key) || Deposits is not { } deposits || Issuer is null)
                    return ("trading-window-failed", []);
                if (_reserved.Contains(key.Id) || deposits.Registry.Get(key).State != DepositState.Unsold)
                {
                    if (shelf?.TradeItem is { } item) item.Stock = 0;
                    return ("trading-maps-error-sold", []);
                }
                if (deposits.Candidate(key) is null)
                {
                    // The cell turned out to have none since the shelf was stocked (#playtest: a
                    // gravel map paid for, then refunded as "fell through").
                    if (shelf?.TradeItem is { } item) item.Stock = 0;
                    return ("trading-maps-error-gone", []);
                }
                int max = MapOffers.MaxPrecision(unlocks.MapTier);
                if (offer == MapOfferAttrs.OreMap && a.GetAsInt(MapOfferAttrs.Precision, MapPrecision.Rough) > max)
                    return ("trading-maps-error-precision", [max]);
                break;
            }
            case MapOfferAttrs.Lead:
                if (!LeadTargets.TryParse(a.GetString(MapOfferAttrs.LeadKind), out var kind)) return ("trading-window-failed", []);
                if (kind != LeadKind.Settlement)
                {
                    // A camp lead shelved before camp leads moved off the shelf (CampLeadsFor).
                    if (shelf?.TradeItem is { } item) item.Stock = 0;
                    return ("trading-maps-error-soldout", []);
                }
                if (!unlocks.MapsToTraders) return ("trading-maps-error-lead", []);
                break;
            default:
                return ("trading-window-failed", []);
        }
        return MapMarksSystem.Of(_sapi!)?.Check(player, stack) switch
        {
            MarkCheck.Marked => ("trading-maps-error-marked", []),
            MarkCheck.Held => ("trading-maps-error-held", []),
            _ => null,
        };
    }

    /// <summary>Before the money moves (the deal's hook): whether this offer may be sold to the player
    /// trading (<see cref="Refusal"/>); notes the price for a refund.</summary>
    public EnumTransactionResult OnTryBuy(EntitySeraphTrader trader, ItemSlot cartSlot)
    {
        var a = cartSlot.Itemstack!.Attributes;
        var player = PlayerTrading(trader);
        if (player is null || _trading is null) return EnumTransactionResult.Failure;
        if (Refusal(trader, player, cartSlot.Itemstack, cartSlot as ItemSlotTrade) is { } refusal)
        {
            Error(player, refusal.Key, refusal.Args);
            return refusal.Key is "trading-maps-error-soldout" or "trading-maps-error-sold" or "trading-maps-error-gone"
                ? EnumTransactionResult.TraderNotEnoughSupplyOrDemand
                : EnumTransactionResult.Failure;
        }
        int price = cartSlot is ItemSlotTrade { TradeItem: { } t } ? t.Price : 0;
        _quotes[(trader.EntityId, OfferId(a))] = (price, player.PlayerUID);
        return EnumTransactionResult.Success;
    }

    // ---- Camp leads (per buyer, off the shelf) ----

    /// <summary>The lead history keys of a player: their own, and their company's (standing's).</summary>
    public List<string> KeysOf(string uid) =>
        LeadBook.KeysOf(uid, _trading?.Standing is StandingSystem { Enabled: true } s ? s.CompanyOf(uid) : null);

    /// <summary>The player's standing tier code here (the stranger's with standing off).</summary>
    public string TierCode(IPlayer player, EntitySeraphTrader trader) =>
        _trading?.Standing is StandingSystem { Enabled: true } s ? s.ViewFor(player.PlayerUID, trader).Tier.Code : CampLeadRules.Stranger;

    /// <summary>The cell of a camp target (<c>camp:x,z</c>), or null.</summary>
    public static CellKey? CampCell(string key) =>
        key.StartsWith("camp:", StringComparison.Ordinal) && CellKey.TryParse(key["camp:".Length..], out var cell) ? cell : null;

    /// <summary>What the window says when a buyer has no camp lead here (a lang key), or null.</summary>
    public static string? WhyKey(CampLeadsWhy why) => why switch
    {
        CampLeadsWhy.StrangerUsed => "trading-window-leads-strangerused",
        CampLeadsWhy.NoneInReach => "trading-window-leads-none",
        _ => null,
    };

    /// <summary>A buyer's camp leads at a trader: the offers with prices, why there are none, what the
    /// picker knew of them, the trader's id and how far it looked.</summary>
    public sealed record CampLeadView(List<CampLeadOffer> Offers, CampLeadsWhy Why, LeadBuyer Buyer, string TraderId, int Reach);

    /// <summary>
    /// The camp leads this trader offers the player now (<see cref="CampLeads.Pick"/>): the camps
    /// they have are those marked on their map (<see cref="MapMarksSystem.MarkedKeys"/>), carried as a
    /// lead (<see cref="MapMarksSystem.Held"/>) or being drawn for them; the camps their group visited
    /// and its maps bought here are <see cref="Leads"/>'. Null when maps or the grid are off.
    /// </summary>
    public CampLeadView? CampLeadsFor(IPlayer player, EntitySeraphTrader trader)
    {
        if (!Active || _trading is not { GridReady: true }) return null;
        string uid = player.PlayerUID;
        var keys = KeysOf(uid);
        string traderId = TraderFinder.IdOf(_sapi!, trader);
        string tier = TierCode(player, trader);
        var have = new HashSet<CellKey>();
        var targets = (MapMarksSystem.Of(_sapi!)?.MarkedKeys(player) ?? []).Concat(MapMarksSystem.Held(player).Select(t => t.Key));
        foreach (string key in targets)
            if (CampCell(key) is { } c) have.Add(c);
        foreach (var (u, c) in _drawing)
            if (u == uid) have.Add(c);
        var buyer = new LeadBuyer
        {
            Tier = tier, Have = have,
            Visited = Leads.Visited(keys).Select(CampCell).Where(c => c.HasValue).Select(c => c!.Value).ToHashSet(),
            Bought = Leads.Bought(keys, traderId), StrangerUsed = Leads.StrangerUsed(keys, traderId),
        };
        var rules = Prices.CampLeads;
        int reach = rules.TierFor(tier)?.Radius ?? rules.StrangerReach;
        int x = (int)trader.Pos.X, z = (int)trader.Pos.Z;
        var camps = CampLeads.CellsAround(x, z, reach).Select(Site).Where(s => s.HasValue).Select(s => s!.Value)
            .Select(s => new CampOption(s.Cell, s.Type, s.X, s.Z, CampLeads.Distance(s.X, s.Z, trader.Pos.X, trader.Pos.Z)))
            .ToList();
        var (offers, why) = CampLeads.Pick(rules, TraderGrid.CellOf(x, z), camps, buyer);
        return new CampLeadView(offers, why, buyer, traderId, reach);
    }

    /// <summary>
    /// The player buys the camp lead to <paramref name="cellCode"/> from the trader's offers to them
    /// (at <paramref name="expectPrice"/>, if given): refused, as a lang key and its arguments, when it
    /// is not on offer at that price, their bags have no room, they lack the gears, or a lead of
    /// theirs is still being drawn. Paid at once (the trader's wallet takes it, standing counts it as
    /// a deal); the buyer gets a lead "being drawn" while the cell's camp is settled
    /// (<see cref="ResolveCamp"/>, generating its next spots as <c>/sh trade tp</c> does), then the
    /// lead to where it stands, and the group's count here goes up (a stranger's map is spent). A
    /// cell that places no camp refunds the gears and is skipped from then on, so the next camp takes
    /// its place on the shelf.
    /// </summary>
    public (bool Ok, string Key, object[] Args) BuyCampLead(IServerPlayer player, EntitySeraphTrader trader, string? cellCode, int? expectPrice)
    {
        if (CampLeadsFor(player, trader) is not { } view) return (false, "trading-window-off", []);
        string uid = player.PlayerUID;
        if (_drawing.Any(d => d.Uid == uid)) return (false, "trading-maps-error-drawing", []);
        if (!CellKey.TryParse(cellCode ?? "", out var cell) || view.Offers.FindIndex(o => o.Cell == cell) is var i && i < 0)
            return (false, WhyKey(view.Why) ?? "trading-window-changed", []);
        var offer = view.Offers[i];
        if (expectPrice is int seen && seen != offer.Price) return (false, "trading-window-changed", []);
        if (_sapi!.World.GetItem(ItemTraderLead.LeadCode) is not { } item) return (false, "trading-window-failed", []);
        var stack = new ItemStack(item);
        if (!Window.TradeWindowSystem.HasRoom(player, stack)) return (false, Window.Core.TradeGuard.NoRoomKey, []);
        if (InventoryTrader.GetPlayerAssets(player.Entity) < offer.Price) return (false, "trading-maps-error-gears", [offer.Price]);

        InventoryTrader.DeductFromEntity(_sapi, player.Entity, offer.Price);
        trader.Inventory.GiveToTrader(offer.Price);
        _trading!.Standing.OnDeal(player, trader, offer.Price, 0);
        var a = stack.Attributes;
        a.SetString(MapOfferAttrs.Offer, MapOfferAttrs.Lead);
        a.SetString(MapOfferAttrs.LeadKind, LeadTargets.Code(LeadKind.Camp));
        a.SetString(MapOfferAttrs.Cell, cell.ToString());
        a.SetString(MapOfferAttrs.Type, offer.Type);
        a.SetInt(MapOfferAttrs.X, offer.X);
        a.SetInt(MapOfferAttrs.Z, offer.Z);
        string token = Guid.NewGuid().ToString("N");
        a.SetString(MapOfferAttrs.Pending, token);
        var sale = new Sale(trader, stack.Clone(), uid, token, offer.Price) { Inside = false };
        if (!player.Entity.TryGiveItemStack(stack)) _sapi.World.SpawnItemEntity(stack, player.Entity.Pos.XYZ);
        var keys = KeysOf(uid);
        string traderId = view.TraderId;
        bool asStranger = Prices.CampLeads.TierFor(view.Buyer.Tier) is null;
        _drawing.Add((uid, cell));
        SeraphHorizons.Mod.Admin.AdminLogs.Trade?.Write("maps", $"{player.PlayerName} buys a lead at {traderId} to {cell} ({offer.Type}, {offer.Distance:0} m) for {offer.Price}"
                                             + $" ({view.Buyer.Tier}, {view.Buyer.Bought} bought here before)");
        ResolveCamp(cell, 0, record =>
        {
            _drawing.Remove((uid, cell));
            if (record is null) _noCamp.Add(cell);
            else Leads.RecordBought(keys, traderId, asStranger);
            Settle(sale, record is null ? null : Lead(LeadKind.Camp, record.Type, record.X, record.Y, record.Z, cell.ToString()));
        });
        if (!sale.Done) Tell(player, "trading-maps-checking-lead");
        return (true, "trading-maps-lead-bought", [TraderTitle(player.LanguageCode ?? Lang.DefaultLocale, offer.Type), offer.Price]);
    }

    // ---- Meeting a trader ----

    /// <summary>A player meets a camp's trader (talks to it or opens its trade): the camp goes on
    /// their map exactly, at the trader, titled with its type, once; a lead's rougher marker of it
    /// goes (remembered ones by the camp's id, older ones by icon, place and title). Traders outside
    /// the grid's camps (travelling merchants, story traders) move on, so they are not marked.</summary>
    private void OnMet(IServerPlayer player, EntitySeraphTrader trader)
    {
        if (trader.Api != _sapi || CampOf(trader) is not { } cell) return;
        // The group has been here: a stranger's map no longer leads here (CampLeads).
        Leads.RecordVisit(KeysOf(player.PlayerUID), TraderIds.Camp(cell.X, cell.Z));
        if (MapMarksSystem.Of(_sapi!) is not { } marks) return;
        string lang = player.LanguageCode ?? Lang.DefaultLocale;
        string name = TraderTitle(lang, trader.TraderType);
        string title = MapMarksSystem.Title(lang, name, MapMarks.Exact);
        double x = Math.Floor(trader.Pos.X) + 0.5, z = Math.Floor(trader.Pos.Z) + 0.5;
        var pos = new Vec3d(x, Math.Floor(trader.Pos.Y) + 0.5, z);
        var titles = new[] { name, TraderTitle(Lang.DefaultLocale, trader.TraderType) }.Distinct().ToList();
        var outcome = marks.Mark(player, new MarkTarget(TraderIds.Camp(cell.X, cell.Z), MapMarks.Exact), pos, title, "trader", MapMarksSystem.TraderColor,
            views => MapMarks.LegacyMatches(views, marks.Book.Of(player.PlayerUID), x, z, MapMarks.LegacyReach, "trader", titles));
        if (outcome == MapMarksSystem.Outcome.Added) Tell(player, "trading-maps-met-marked", title);
    }

    /// <summary>"Trader camp (cook)", as a lead to the camp is titled.</summary>
    private static string TraderTitle(string lang, string type) =>
        Lang.GetL(lang, "seraphhorizons:trading-maps-lead-title", Lang.GetL(lang, "seraphhorizons:trading-type-" + type));

    /// <summary>The grid cell of the placed camp the trader belongs to (within the camp reach standing
    /// uses), or null.</summary>
    public CellKey? CampOf(EntitySeraphTrader trader)
    {
        if (_trading?.Camps?.Registry is not { } registry) return null;
        var cell = TraderGrid.CellOf((int)trader.Pos.X, (int)trader.Pos.Z);
        return registry.Get(cell) is { Status: CampStatus.Placed } camp
               && Math.Abs(camp.X - trader.Pos.X) <= StandingSystem.CampReach && Math.Abs(camp.Z - trader.Pos.Z) <= StandingSystem.CampReach
            ? cell
            : null;
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

    /// <summary>The camp of a cell, placed if need be: its next spot's chunk is generated (which
    /// places the camp or leaves the next spot) until it is placed or every spot has missed.</summary>
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
        if (CampRegistry.NextSpot(record, spots.Count) is not { } next || round > TraderGrid.Attempts)
        {
            done(null);
            return;
        }
        var spot = spots[next];
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
