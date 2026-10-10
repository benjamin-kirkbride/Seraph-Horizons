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
using SeraphHorizons.Mod.Trading.Values;
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
/// <c>config/trading/map-prices.json</c>. Offers name what the deposit is (#692: its ores, grades
/// and host rock; a gravel field's rock and metals).</item>
/// <item><b>Checked first</b> (#693): only a checked deposit is offered; one not checked yet is a
/// "being surveyed" entry (stock 0) while <see cref="Surveys"/> checks it, ahead of players nearing a
/// camp or at once when a trade opens on it, and <see cref="RefreshShelves"/> puts the real offer in
/// its place when the check lands.</item>
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
    /// <summary>Metals warned about having no size range or no ingot value (not offered).</summary>
    private readonly HashSet<string> _unpriced = new();

    /// <summary>Every group's camp lead history (saved with the world).</summary>
    public LeadBook Leads { get; private set; } = new();

    /// <summary>Maps and leads are sold here (server, switch on, the table loaded).</summary>
    public bool Active { get; private set; }

    public MapPriceTable Prices { get; private set; } = new();

    /// <summary>Deposits whose sale is being checked: no trader offers them meanwhile.</summary>
    public IReadOnlyCollection<string> Reserved => _reserved;

    /// <summary>The background deposit checks (#693); null while maps are off.</summary>
    public DepositSurveys? Surveys { get; private set; }

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
        EntitySeraphTrader.TradeOpened += OnTradeOpened;
        var config = SeraphHorizonsSystem.ConfigFor(api);
        Surveys = new DepositSurveys(api, this, config.DepositCheckApproachMetres, config.DepositCheckPauseSeconds);
        Active = true;
        api.Logger.Notification("[seraphhorizons] Trader maps: on (ore maps within {0}, gravel maps within {1}, camp leads {2};"
                                + " deposits checked {3}, {4} s apart)",
            Prices.OreRadius, Prices.GravelRadius,
            string.Join(", ", Prices.CampLeads.Tiers.Select(t => $"{t.Key} {t.Value.Maps} within {t.Value.Reach} rings")),
            Surveys.ApproachMetres > 0 ? $"as a player comes within {Surveys.ApproachMetres} blocks of a camp" : "only when a trade opens",
            Surveys.Queue.PauseSeconds);
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
        EntitySeraphTrader.TradeOpened -= OnTradeOpened;
        Surveys?.Dispose();
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
                var (offers, soldOut) = PickOre(deposits, x, z);
                bool urgent = PlayerTrading(trader) != null;
                foreach (var c in offers)
                    if (OreEntry(c, urgent) is { } e)
                        yield return e;
                if (soldOut) yield return Entry(OfferOreCode, new JObject { [MapOfferAttrs.Offer] = MapOfferAttrs.SoldOut }, 1, 1, false);
                break;
            }
            case MapOfferAttrs.GravelMap when Deposits is { HasGravel: true } deposits:
            {
                var (offer, soldOut) = PickGravel(deposits, x, z);
                if (offer is { } c)
                    yield return GravelEntry(c, PlayerTrading(trader) != null);
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

    /// <summary>An ore map's price before standing (<see cref="MapPriceTable.OrePrice"/>), from the
    /// metal's size range (<c>config/ore-sizes.json</c>) and its ingot's item value; null, with a
    /// warning once per metal, when either is missing.</summary>
    private int? OrePrice(string metal, string? sizeClass, int precision)
    {
        int? price = Prices.OrePrice(metal, sizeClass, precision, Deposits?.TargetsFor(metal),
            ItemValuesSystem.For(_sapi!).ValueOf(MapPriceTable.IngotCode(metal)));
        if (price is null && _unpriced.Add(metal))
            _sapi!.Logger.Warning("[seraphhorizons] Trader maps: no price for {0} ore maps (no size range in ore-sizes.json, or no value for {1}); not offered",
                metal, MapPriceTable.IngotCode(metal));
        return price;
    }

    private static DepositOption Option(DepositCandidate c) =>
        new(c.Key.Id, c.Metal, c.Distance, c.Record.State == DepositState.Unsold, c.Record.Tier?.ToString().ToLowerInvariant());

    /// <summary>The deposits a prospector at (x, z) offers maps to (<see cref="MapOffers.PickOre"/>:
    /// per metal the nearest unsold, unreserved and not in <paramref name="skip"/>, at most
    /// <c>maxOreOffers</c>, optionally of one metal), checked or not; and whether the trader is sold out.</summary>
    public (List<DepositCandidate> Offers, bool SoldOut) PickOre(DepositService deposits, int x, int z, string? metal = null, ISet<string>? skip = null)
    {
        var candidates = deposits.Candidates(x, z, Prices.OreRadius, metal);
        var reserved = Skipped(skip);
        var (offers, soldOut) = MapOffers.PickOre(candidates.Select(Option), Prices.MaxOreOffers, reserved);
        var byId = candidates.ToDictionary(c => c.Key.Id);
        return (offers.Select(o => byId[o.Id]).ToList(), soldOut);
    }

    /// <summary>What no offer goes to: deposits being sold, those a check could not settle
    /// (<see cref="DepositSurveys.GaveUp"/>), and <paramref name="skip"/>.</summary>
    private ISet<string> Skipped(ISet<string>? skip)
    {
        if (skip is null && Surveys is not { GaveUp.Count: > 0 }) return _reserved;
        var set = new HashSet<string>(_reserved);
        if (skip != null) set.UnionWith(skip);
        if (Surveys != null) set.UnionWith(Surveys.GaveUp);
        return set;
    }

    /// <summary>The gravel field a trader at (x, z) offers a map to, checked or not; and whether it is sold out.</summary>
    public (DepositCandidate? Offer, bool SoldOut) PickGravel(DepositService deposits, int x, int z, ISet<string>? skip = null)
    {
        var fields = deposits.GravelFields(x, z, Prices.GravelRadius);
        var reserved = Skipped(skip);
        var (offer, soldOut) = MapOffers.PickGravel(fields.Select(Option), reserved);
        return (offer is { } o ? fields.First(f => f.Key.Id == o.Id) : null, soldOut);
    }

    /// <summary>The shelf entry for an ore deposit: its map offer once checked (#693), named by what
    /// it is (#692); until then a "being surveyed" entry for its metal, and the check is asked for
    /// (<paramref name="urgent"/>: a player is trading). None when the metal has no price.</summary>
    private TradeEntry? OreEntry(DepositCandidate c, bool urgent)
    {
        if (OrePrice(c.Metal, c.Record.Tier?.ToString().ToLowerInvariant(), MapPrecision.Rough) is not { } price) return null;
        if (!c.Surveyed)
        {
            Surveys?.Want(c.Key, urgent);
            return Entry(OfferOreCode, SurveyingAttrs(c), 1, 1, false);
        }
        var attrs = new JObject
        {
            [MapOfferAttrs.Offer] = MapOfferAttrs.OreMap, [MapOfferAttrs.Deposit] = c.Key.Id, [MapOfferAttrs.Metal] = c.Metal,
            [MapOfferAttrs.Precision] = MapPrecision.Rough, [MapOfferAttrs.Distance] = Math.Round(c.Distance),
            [MapOfferAttrs.SizeTier] = c.Record.Tier!.Value.ToString().ToLowerInvariant(),
        };
        var makeup = c.Record.Makeup!;
        if (makeup.MainOres() is { Count: > 0 } ores) attrs[MapOfferAttrs.Ores] = OreNames.Csv(ores);
        if (makeup.Mix() is { } mix) attrs[MapOfferAttrs.Grades] = mix.Code;
        if (makeup.HostRock() is { } rock) attrs[MapOfferAttrs.Rock] = rock;
        return Entry(OfferOreCode, attrs, price, 1, false);
    }

    /// <summary>The shelf entry for a gravel field: its map offer once placed (#693), with its rock
    /// and the metals it pans (#692); until then a "being surveyed" entry, and the check is asked for.</summary>
    private TradeEntry GravelEntry(DepositCandidate c, bool urgent)
    {
        if (!c.Surveyed)
        {
            Surveys?.Want(c.Key, urgent);
            return Entry(OfferGravelCode, SurveyingAttrs(c), 1, 1, false);
        }
        var attrs = new JObject
        {
            [MapOfferAttrs.Offer] = MapOfferAttrs.GravelMap, [MapOfferAttrs.Deposit] = c.Key.Id, [MapOfferAttrs.Metal] = PlacerCells.Kind,
            [MapOfferAttrs.Distance] = Math.Round(c.Distance),
        };
        if (Deposits?.FieldOf(c.Key)?.Rock is { } rock)
        {
            attrs[MapOfferAttrs.Rock] = rock;
            if (Deposits.PanMetals(rock) is { Count: > 0 } metals) attrs[MapOfferAttrs.Metals] = OreNames.Csv(metals);
        }
        return Entry(OfferGravelCode, attrs, Prices.GravelPrice(), 1, false);
    }

    private static JObject SurveyingAttrs(DepositCandidate c) => new()
    {
        [MapOfferAttrs.Offer] = MapOfferAttrs.Surveying, [MapOfferAttrs.Deposit] = c.Key.Id, [MapOfferAttrs.Metal] = c.Metal,
        [MapOfferAttrs.Distance] = Math.Round(c.Distance),
    };

    private static TradeEntry Entry(AssetLocation code, JObject attrs, int price, int stock, bool optional) => new()
    {
        Type = "item",
        Code = code.ToString(),
        Attributes = attrs,
        AttributesKey = attrs.ToString(Formatting.None),
        StackSize = 1,
        Stock = new NatSpec(stock, 0),
        Price = price,
        PriceReason = "a map offer, priced by the maps system",
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
            if (offer is MapOfferAttrs.SoldOut or MapOfferAttrs.Surveying) slot.TradeItem.Stock = 0;
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
                case MapOfferAttrs.Surveying:
                    // Unavailable until its check lands and the real offer takes its place (RefreshShelves).
                    item.Stock = 0;
                    slot.MarkDirty();
                    continue;
                case MapOfferAttrs.OreMap or MapOfferAttrs.GravelMap when Gone(a):
                    // Sold, being sold, or the cell turned out to have none since the restock.
                    item.Stock = 0;
                    slot.MarkDirty();
                    continue;
                case MapOfferAttrs.OreMap:
                    a.SetInt(MapOfferAttrs.Precision, maxPrecision);
                    if (OrePrice(a.GetString(MapOfferAttrs.Metal) ?? "", a.GetString(MapOfferAttrs.SizeTier), maxPrecision) is not { } orePrice)
                    {
                        item.Stock = 0;
                        slot.MarkDirty();
                        continue;
                    }
                    price = orePrice;
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

    // ---- Deposit checks (#693) ----

    /// <summary>The fallback: a trade window opens on a shelf still showing deposits "being
    /// surveyed" (a player who teleported or travelled fast): their checks go first, at once.</summary>
    private void OnTradeOpened(IServerPlayer player, EntitySeraphTrader trader)
    {
        if (trader.Api != _sapi || Surveys is null) return;
        foreach (var slot in trader.Inventory.SellingSlots)
            if (slot.Itemstack?.Attributes is { } a && a.GetString(MapOfferAttrs.Offer) == MapOfferAttrs.Surveying
                && DepositKey.TryParse(a.GetString(MapOfferAttrs.Deposit), out var key))
                Surveys.Want(key, urgent: true);
    }

    /// <summary>A camp's site for the background checks (<see cref="DepositSurveys"/>): the placed
    /// camp, else the spot it waits for; null if it can take none.</summary>
    public (int X, int Z, string Type)? SiteOf(CellKey cell) => Site(cell) is { } s ? (s.X, s.Z, s.Type) : null;

    /// <summary>Asks for checks of what a trader at (x, z) would offer that is not checked yet: a
    /// prospector's ore maps (<paramref name="ore"/>) and anyone's gravel map. Returns how many were
    /// asked for (none when everything is checked).</summary>
    public int QueueChecks(int x, int z, bool ore, bool urgent)
    {
        if (Deposits is not { } deposits || Surveys is null) return 0;
        int asked = 0;
        if (ore && deposits.HasOre)
            foreach (var c in PickOre(deposits, x, z).Offers)
                if (!c.Surveyed && OrePrice(c.Metal, null, MapPrecision.Rough) != null && Surveys.Want(c.Key, urgent, (x, z, ore)))
                    asked++;
        if (deposits.HasGravel && PickGravel(deposits, x, z).Offer is { Surveyed: false } field && Surveys.Want(field.Key, urgent, (x, z, ore)))
            asked++;
        return asked;
    }

    /// <summary>
    /// A deposit's check has landed: every loaded trader whose shelf shows it "being surveyed" gets
    /// the real offer in its place (<see cref="EntitySeraphTrader.ReplaceSelling"/>), or, when the
    /// cell turned out to have none or the deposit is worked out, its metal's next deposit (itself
    /// checked, or "being surveyed" while its own check runs), or nothing. Re-priced for the player
    /// trading, and their window told.
    /// </summary>
    public void RefreshShelves(DepositKey key)
    {
        if (_sapi is null || Deposits is not { } deposits) return;
        var traders = _sapi.World.LoadedEntities.Values.OfType<EntitySeraphTrader>().Where(t => t.Alive && t.Inventory != null).ToList();
        foreach (var trader in traders)
        {
            var slots = trader.Inventory.SellingSlots;
            bool changed = false;
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i].Itemstack?.Attributes is not { } a || a.GetString(MapOfferAttrs.Offer) != MapOfferAttrs.Surveying
                    || a.GetString(MapOfferAttrs.Deposit) != key.Id)
                    continue;
                // Deposits already on this shelf are not offered twice.
                var shelf = slots.Select(s => s.Itemstack?.Attributes?.GetString(MapOfferAttrs.Deposit)).OfType<string>().Where(id => id != key.Id).ToHashSet();
                int x = (int)trader.Pos.X, z = (int)trader.Pos.Z;
                bool urgent = PlayerTrading(trader) != null;
                TradeEntry? entry = key.IsGravel
                    ? PickGravel(deposits, x, z, shelf).Offer is { } field ? GravelEntry(field, urgent) : null
                    : PickOre(deposits, x, z, key.Kind, shelf).Offers.FirstOrDefault() is { } c ? OreEntry(c, urgent) : null;
                trader.ReplaceSelling(i, entry);
                changed = true;
            }
            if (!changed) continue;
            foreach (var slot in slots)
                if (slot.TradeItem != null && slot.Itemstack?.Attributes.GetString(MapOfferAttrs.Offer) is MapOfferAttrs.SoldOut or MapOfferAttrs.Surveying)
                    slot.TradeItem.Stock = 0;
            var player = PlayerTrading(trader);
            Price(trader, player);
            Store(trader);
            if (player != null) Window.TradeWindowSystem.Of(_sapi)?.SendState(player, trader);
        }
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
        if (offer == MapOfferAttrs.Surveying) return ("trading-maps-error-surveying", []);
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
            return refusal.Key is "trading-maps-error-soldout" or "trading-maps-error-sold" or "trading-maps-error-gone" or "trading-maps-error-surveying"
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
    /// picker knew of them, the trader's id, how many rings out it looked, and whether the player has
    /// had their very first map.</summary>
    public sealed record CampLeadView(List<CampLeadOffer> Offers, CampLeadsWhy Why, LeadBuyer Buyer, string TraderId, int Reach, bool PityUsed);

    /// <summary>The <c>Code</c> a buy of the pity map names (its camp is settled when bought, so it is
    /// not named by cell).</summary>
    public const string PityCode = "pity";

    /// <summary>The camps a player has: marked on their map (<see cref="MapMarksSystem.MarkedKeys"/>),
    /// carried as a lead (<see cref="MapMarksSystem.Held"/>) or being drawn for them.</summary>
    private HashSet<CellKey> CampsHad(IPlayer player)
    {
        var have = new HashSet<CellKey>();
        var targets = (MapMarksSystem.Of(_sapi!)?.MarkedKeys(player) ?? []).Concat(MapMarksSystem.Held(player).Select(t => t.Key));
        foreach (string key in targets)
            if (CampCell(key) is { } c) have.Add(c);
        foreach (var (u, c) in _drawing)
            if (u == player.PlayerUID) have.Add(c);
        return have;
    }

    /// <summary>A cell's camp as the picker sees it from a trader at (x, z): its site, ring and
    /// distance, or null if it can take none.</summary>
    private CampOption? Option(CellKey own, double x, double z, CellKey cell) =>
        Site(cell) is { } s ? new CampOption(s.Cell, s.Type, s.X, s.Z, CampLeads.Ring(own, s.Cell), CampLeads.Distance(s.X, s.Z, x, z)) : null;

    /// <summary>The pity map's camp from a trader at (x, z) for a buyer who has <paramref name="have"/>
    /// (<see cref="PityMap.Target"/>: the nearest seeded prospector cell that can take a camp, else
    /// the nearest camp of any type, within the pity's reach).</summary>
    private CampOption? PityTarget(double x, double z, ISet<CellKey> have)
    {
        var own = TraderGrid.CellOf((int)x, (int)z);
        var grid = _trading!.Grid!;
        return PityMap.Target(own, Prices.CampLeads.Pity.Reach, grid.IsProspector, c => Option(own, x, z, c), have);
    }

    /// <summary>
    /// The camp leads this trader offers the player now (<see cref="CampLeads.Pick"/>): the camps
    /// they have are <see cref="CampsHad"/>; the camps their group visited and its maps bought here
    /// are <see cref="Leads"/>'. The candidates are the cells within the tier's reach in rings of the
    /// trader's cell. Their very first map (the pity map) is offered first when they have not had it,
    /// the trader is not a prospector, nothing of theirs is being drawn and, for a stranger, the
    /// group's one map from this trader is unspent. Null when maps or the grid are off.
    /// </summary>
    public CampLeadView? CampLeadsFor(IPlayer player, EntitySeraphTrader trader)
    {
        if (!Active || _trading is not { GridReady: true }) return null;
        string uid = player.PlayerUID;
        var keys = KeysOf(uid);
        string traderId = TraderFinder.IdOf(_sapi!, trader);
        string tier = TierCode(player, trader);
        var have = CampsHad(player);
        var rules = Prices.CampLeads;
        bool strangerUsed = Leads.StrangerUsed(keys, traderId);
        bool pityUsed = Leads.PityUsed(uid);
        bool pityHere = !pityUsed && trader.TraderType != TraderTypes.Prospector && !_drawing.Any(d => d.Uid == uid)
                        && !(rules.IsStranger(tier) && strangerUsed);
        var buyer = new LeadBuyer
        {
            Tier = tier, Have = have,
            Visited = Leads.Visited(keys).Select(CampCell).Where(c => c.HasValue).Select(c => c!.Value).ToHashSet(),
            Bought = Leads.Bought(keys, traderId), StrangerUsed = strangerUsed,
            Pity = pityHere ? PityTarget(trader.Pos.X, trader.Pos.Z, have) : null,
        };
        int reach = rules.TierFor(tier).Reach;
        var own = TraderGrid.CellOf((int)trader.Pos.X, (int)trader.Pos.Z);
        var camps = CampLeads.CellsAround(own, reach).Select(c => Option(own, trader.Pos.X, trader.Pos.Z, c))
            .Where(o => o.HasValue).Select(o => o!.Value).ToList();
        var (offers, why) = CampLeads.Pick(rules, own, camps, buyer);
        return new CampLeadView(offers, why, buyer, traderId, reach, pityUsed);
    }

    /// <summary>
    /// The player buys the camp lead to <paramref name="cellCode"/> (or their pity map,
    /// <see cref="PityCode"/>) from the trader's offers to them (at <paramref name="expectPrice"/>, if
    /// given): refused, as a lang key and its arguments, when it is not on offer at that price, their
    /// bags have no room, they lack the gears, or a lead of theirs is still being drawn. Paid at once
    /// (the trader's wallet takes it, standing counts it as a deal); the buyer gets a lead "being
    /// drawn" while the cell's camp is settled (<see cref="ResolveCamp"/>, generating its next spots
    /// as <c>/sh trade tp</c> does), then the lead to where it stands, and the group's count here goes
    /// up (a stranger's map is spent). A cell that places no camp is skipped from then on: a camp lead
    /// refunds the gears, so the next camp takes its place on offer; the pity map goes on to its next
    /// target (<see cref="PityTarget"/>: the next-nearest prospector cell, then any camp, out to its
    /// reach) and refunds only when none places. The pity map spends the stranger's map here, counts
    /// as a lead bought here, and is the player's own (<see cref="LeadBook.RecordPity"/>).
    /// </summary>
    public (bool Ok, string Key, object[] Args) BuyCampLead(IServerPlayer player, EntitySeraphTrader trader, string? cellCode, int? expectPrice)
    {
        if (CampLeadsFor(player, trader) is not { } view) return (false, "trading-window-off", []);
        string uid = player.PlayerUID;
        if (_drawing.Any(d => d.Uid == uid)) return (false, "trading-maps-error-drawing", []);
        int i = cellCode == PityCode ? view.Offers.FindIndex(o => o.Pity)
            : CellKey.TryParse(cellCode ?? "", out var named) ? view.Offers.FindIndex(o => o.Cell == named && !o.Pity) : -1;
        if (i < 0) return (false, WhyKey(view.Why) ?? "trading-window-changed", []);
        var offer = view.Offers[i];
        var cell = offer.Cell;
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
        bool pity = offer.Pity;
        // The pity map is the trader's one stranger map too.
        bool asStranger = pity || Prices.CampLeads.IsStranger(view.Buyer.Tier);
        double tx = trader.Pos.X, tz = trader.Pos.Z;
        _drawing.Add((uid, cell));
        SeraphHorizons.Mod.Admin.AdminLogs.Trade?.Write("maps", $"{player.PlayerName} buys {(pity ? "their first map" : "a lead")} at {traderId} to {cell}"
                                             + $" ({offer.Type}, ring {offer.Ring}, {offer.Distance:0} m) for {offer.Price}"
                                             + $" ({view.Buyer.Tier}, {view.Buyer.Bought} bought here before)");
        // Settles the cell; a pity map whose cell places none goes on to its next target (at most
        // PityTries cells), measured from where the trader stood.
        void Resolve(CellKey at, int tries)
        {
            ResolveCamp(at, 0, record =>
            {
                _drawing.Remove((uid, at));
                if (record is null)
                {
                    _noCamp.Add(at);
                    if (pity && tries < PityTries && _sapi!.World.PlayerByUid(uid) is { } p
                        && PityTarget(tx, tz, CampsHad(p)) is { } next)
                    {
                        _drawing.Add((uid, next.Cell));
                        Resolve(next.Cell, tries + 1);
                        return;
                    }
                }
                else
                {
                    Leads.RecordBought(keys, traderId, asStranger);
                    if (pity) Leads.RecordPity(uid);
                }
                Settle(sale, record is null ? null : Lead(LeadKind.Camp, record.Type, record.X, record.Y, record.Z, at.ToString()));
            });
        }
        Resolve(cell, 1);
        if (!sale.Done) Tell(player, "trading-maps-checking-lead");
        return (true, "trading-maps-lead-bought", [TraderTitle(player.LanguageCode ?? Lang.DefaultLocale, offer.Type), offer.Price]);
    }

    /// <summary>Cells a pity map tries at most before it refunds (each one whose camp places none is
    /// skipped for the next-nearest).</summary>
    public const int PityTries = 16;

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
