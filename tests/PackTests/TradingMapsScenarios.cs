using Atlas.XUnit;
using SeraphHorizons.Mod.Ore;
using SeraphHorizons.Mod.Ore.Core;
using SeraphHorizons.Mod.Trading;
using SeraphHorizons.Mod.Trading.Glue;
using SeraphHorizons.Mod.Trading.Maps;
using SeraphHorizons.Mod.Trading.Maps.Core;
using SeraphHorizons.Mod.Trading.Standing;
using SeraphHorizons.Mod.Trading.Window;
using SeraphHorizons.Mod.Trading.Window.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Xunit.Abstractions;

namespace SeraphHorizons.PackTests;

/// <summary>
/// mods-src/seraphhorizons/Trading/Maps (#455) and the wave-2 glue, in a new standard world with a
/// fixed seed (ore cells, placer fields and the camp grid on): a prospector's shelf offers ore maps
/// from the deposit registry (which lists deposits whose chunks nobody generated), buying one through
/// the trade window (its server side: one unit, held) yields a real ore map and marks the deposit sold, another trader
/// no longer offers it, a general store offers a gravel map exactly when a field is in reach, and
/// standing changes the prices a trader quotes. Camp leads (per buyer, off the shelf): a stranger
/// buys exactly one from a trader and is refused a second, and buys onward at the camp it leads to;
/// a known customer is offered two within 3 km, buying one brings the next-nearest and doubles the
/// next price; a partner's lead to a camp nobody generated settles the camp and marks it where it
/// stands. From the playtest after the trade window: a lead bought is not offered again, carried or
/// marked, while the gravel map bought after it arrives; a gravel offer whose field turned out empty
/// is refused before payment; and meeting a camp's trader marks it exactly in place of the lead's
/// marker.
/// </summary>
[AtlasWorld(Seed = Seed, WorldType = "standard")]
public class TradingMapsScenarios(ITestOutputHelper output) : AtlasScenarioBase
{
    private const int Seed = 515151;

    private ICoreServerAPI Api => World.Api;
    private IWorldAccessor W => World.Api.World;
    private OreSystem Ore => Api.ModLoader.GetModSystem<OreSystem>();
    private DepositService Deposits => Ore.Deposits ?? throw new Xunit.Sdk.XunitException("the deposit service is not bound");
    private MapsSystem Maps => Api.ModLoader.GetModSystem<MapsSystem>();
    private StandingSystem Standing => StandingSystem.Of(Api) ?? throw new Xunit.Sdk.XunitException("no StandingSystem");

    private async Task<EntitySeraphTrader> SpawnTrader(string type, int dx, int dz)
    {
        var pos = World.Spawn.AddCopy(dx, 0, dz);
        pos.Y = W.BlockAccessor.GetTerrainMapheightAt(pos) + 1;
        var props = W.GetEntityType(new AssetLocation("seraphhorizons", $"trader-male-{type}-temperate"))!;
        var trader = (EntitySeraphTrader)W.ClassRegistry.CreateEntity(props);
        trader.Pos.SetPos(pos.X + 0.5, pos.Y, pos.Z + 0.5);
        W.SpawnEntity(trader);
        await World.Ticks(5);
        return trader;
    }

    private async Task<IServerPlayer> Buyer(string name, EntitySeraphTrader trader)
    {
        var p = await World.JoinPlayer(name);
        // Within vanilla's trading reach (a squared distance of 5 closes the trade).
        await p.TeleportTo(trader.Pos.AsBlockPos.AddCopy(1, 0, 0));
        await p.GiveItem("game:gear-rusty", 64);
        var sp = (IServerPlayer)p.Player;
        sp.WorldData.CurrentGameMode = EnumGameMode.Survival;
        return sp;
    }

    /// <summary>As the dialog does: the player opens the trade (vanilla sets the trading player).</summary>
    private void Open(EntitySeraphTrader trader, IServerPlayer player)
    {
        trader.WatchedAttributes.SetString(StandingPriceModifier.TradingPlayerAttr, player.PlayerUID);
        Api.ModLoader.GetModSystem<TradingGlueSystem>().Sync(trader);
    }

    private void Close(EntitySeraphTrader trader)
    {
        trader.WatchedAttributes.RemoveAttribute(StandingPriceModifier.TradingPlayerAttr);
        Api.ModLoader.GetModSystem<TradingGlueSystem>().Sync(trader);
    }

    private static List<ItemSlotTrade> Offers(EntitySeraphTrader trader, string offer) =>
        trader.Inventory.SellingSlots.Where(s => s.Itemstack?.Attributes.GetString(MapOfferAttrs.Offer) == offer).ToList();

    /// <summary>Buys one lot of a selling slot through the trade window: its Buy request, as a
    /// completed hold sends it.</summary>
    private void Buy(EntitySeraphTrader trader, IServerPlayer player, ItemSlotTrade selling)
    {
        int slot = Array.IndexOf(trader.Inventory.SellingSlots, selling);
        Assert.True(slot >= 0);
        var result = TradeWindowSystem.Of(Api)!.Handle(player, trader, new TradeRequest { Action = TradeAction.Buy, Slot = slot });
        Assert.True(result.Ok, $"{result.Key} {string.Join(",", result.Args)}");
    }

    private static ItemSlot? Holding(IServerPlayer player, AssetLocation code, System.Func<ItemStack, bool> match)
    {
        foreach (var inv in player.InventoryManager.InventoriesOrdered)
            if (inv is InventoryBasePlayer && inv.ClassName != GlobalConstants.creativeInvClassName)
                foreach (var slot in inv)
                    if (slot.Itemstack?.Collectible?.Code == code && match(slot.Itemstack))
                        return slot;
        return null;
    }

    [AtlasScenario]
    [ReadsBootLog]
    public void Maps_are_on_and_the_boot_logs_nothing_about_them()
    {
        var logged = World.BootDiagnostics
            .Where(e => e.Level is EnumLogType.Warning or EnumLogType.Error or EnumLogType.Fatal)
            .Where(e => e.Message.Contains("Trader maps", StringComparison.Ordinal))
            .Select(e => $"[{e.Level}] {e.Message}")
            .ToList();
        Assert.True(logged.Count == 0, "Logged:\n" + string.Join("\n", logged));
        Assert.True(Maps.Active);
        Assert.Empty(Maps.Prices.Problems());
        Assert.True(TradingSystem.Of(Api)!.GridReady);
    }

    [AtlasScenario(TimeoutMs = 600_000)]
    public async Task A_prospector_sells_an_ore_map_once()
    {
        var trader = await SpawnTrader("prospector", 24, 24);
        int x = (int)trader.Pos.X, z = (int)trader.Pos.Z;
        var candidates = Deposits.Candidates(x, z, Maps.Prices.OreRadius);
        Assert.NotEmpty(candidates);
        var metals = candidates.Where(c => c.Record.State == DepositState.Unsold).Select(c => c.Metal).Distinct().ToList();
        var offers = Offers(trader, MapOfferAttrs.OreMap);
        output.WriteLine($"candidates {candidates.Count}, unsold metals {string.Join(",", metals)}; offers " +
                         string.Join(", ", offers.Select(o => $"{o.Itemstack.Attributes.GetString(MapOfferAttrs.Deposit)} @{o.TradeItem.Price}")));
        Assert.Equal(Math.Min(metals.Count, Maps.Prices.MaxOreOffers), offers.Count);
        foreach (var o in offers)
        {
            string id = o.Itemstack.Attributes.GetString(MapOfferAttrs.Deposit);
            Assert.True(DepositKey.TryParse(id, out var key));
            // The nearest unsold of its metal.
            Assert.Equal(candidates.First(c => c.Metal == key.Kind && c.Record.State == DepositState.Unsold).Key, key);
            Assert.Equal(MapPrecision.Rough, o.Itemstack.Attributes.GetAsInt(MapOfferAttrs.Precision));
        }
        Assert.Empty(Offers(trader, MapOfferAttrs.SoldOut));

        var buyer = await Buyer("mapbuyer", trader);
        Open(trader, buyer);
        var offer = Offers(trader, MapOfferAttrs.OreMap).OrderBy(o => o.TradeItem.Price).First();
        Assert.True(DepositKey.TryParse(offer.Itemstack.Attributes.GetString(MapOfferAttrs.Deposit), out var bought));
        int price = offer.TradeItem.Price;
        int gearsBefore = InventoryTrader.GetPlayerAssets(buyer.Entity);
        Buy(trader, buyer, offer);
        Assert.Null(trader.Inventory.GetBuyingCartSlot(0).Itemstack);
        Assert.Equal(gearsBefore - price, InventoryTrader.GetPlayerAssets(buyer.Entity));

        // The deposit may need generating: the map follows.
        await World.Until(() => Holding(buyer, MapIssuer.OreMapCode, s => s.Attributes.HasAttribute(ItemOreMap.AttrX)) != null, 300_000);
        var map = Holding(buyer, MapIssuer.OreMapCode, s => s.Attributes.HasAttribute(ItemOreMap.AttrX))!.Itemstack;
        output.WriteLine($"bought {bought} for {price}: {map.GetName()}");
        Assert.Equal(bought.Id, map.Attributes.GetString(ItemOreMap.AttrDeposit));
        Assert.Null(map.Attributes.GetString(MapOfferAttrs.Pending));
        Assert.Null(map.Attributes.GetString(MapOfferAttrs.Offer));
        Assert.NotNull(map.Attributes.GetString(ItemOreMap.AttrSizeTier));
        var record = Deposits.Registry.Get(bought);
        Assert.Equal(DepositState.Sold, record.State);
        Assert.Equal(buyer.PlayerUID, record.SoldToUid);
        Assert.Null(Holding(buyer, MapIssuer.OreMapCode, s => s.Attributes.HasAttribute(MapOfferAttrs.Pending)));
        Assert.Empty(Maps.Reserved);

        // No other trader offers it.
        var other = await SpawnTrader("prospector", -30, 30);
        var otherOffers = Offers(other, MapOfferAttrs.OreMap).Select(o => o.Itemstack.Attributes.GetString(MapOfferAttrs.Deposit)).ToList();
        output.WriteLine("the other prospector offers " + string.Join(", ", otherOffers));
        Assert.DoesNotContain(bought.Id, otherOffers);
        // Nor does this one at its next restock.
        trader.Restock(1.1f);
        Assert.DoesNotContain(bought.Id, Offers(trader, MapOfferAttrs.OreMap).Select(o => o.Itemstack.Attributes.GetString(MapOfferAttrs.Deposit)));
        Close(trader);
        trader.Die(EnumDespawnReason.Removed);
        other.Die(EnumDespawnReason.Removed);
    }

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task A_general_store_offers_a_gravel_map_exactly_when_a_field_is_in_reach()
    {
        var trader = await SpawnTrader("generalstore", -24, 18);
        int x = (int)trader.Pos.X, z = (int)trader.Pos.Z;
        var fields = Deposits.GravelFields(x, z, Maps.Prices.GravelRadius);
        var unsold = fields.Where(f => f.Record.State == DepositState.Unsold && !Maps.Reserved.Contains(f.Key.Id)).ToList();
        var offers = Offers(trader, MapOfferAttrs.GravelMap);
        output.WriteLine($"{fields.Count} fields within {Maps.Prices.GravelRadius}, {unsold.Count} unsold; offers: " +
                         string.Join(", ", offers.Select(o => o.Itemstack.Attributes.GetString(MapOfferAttrs.Deposit))));
        if (unsold.Count == 0)
            Assert.Empty(offers);
        else
            Assert.Equal(unsold[0].Key.Id, Assert.Single(offers).Itemstack.Attributes.GetString(MapOfferAttrs.Deposit));
        Assert.Equal(fields.Count > 0 && unsold.Count == 0, Offers(trader, MapOfferAttrs.SoldOut).Count == 1);
        // No ore maps here: those are the prospector's.
        Assert.Empty(Offers(trader, MapOfferAttrs.OreMap));
        // Maps are never bought back.
        var economy = Mod.Trading.Economy.EconomySystem.Of(Api)!;
        var gravelMap = new ItemStack(W.GetItem(MapIssuer.GravelMapCode)!);
        Assert.Equal(Mod.Trading.Economy.Core.Refusal.MapOrLead, economy.QuoteOffList(trader, gravelMap).Refusal);
        var lead = new ItemStack(W.GetItem(ItemTraderLead.LeadCode)!);
        Assert.Equal(Mod.Trading.Economy.Core.Refusal.MapOrLead, economy.QuoteOffList(trader, lead).Refusal);
        trader.Die(EnumDespawnReason.Removed);
    }

    // ---- Camp leads ----

    private MapsSystem.CampLeadView LeadsAt(IServerPlayer player, EntitySeraphTrader trader)
    {
        var view = Maps.CampLeadsFor(player, trader) ?? throw new Xunit.Sdk.XunitException("no camp leads (maps or the grid off)");
        output.WriteLine($"{player.PlayerName} at {view.TraderId} as {view.Buyer.Tier} ({view.Buyer.Bought} bought, {view.Why}): "
                         + string.Join(", ", view.Offers.Select(o => $"{o.Cell} {o.Type} {o.Distance:0} m {o.Price} g{(o.Prospector ? " *" : "")}")));
        return view;
    }

    private TradeResult BuyLead(IServerPlayer player, EntitySeraphTrader trader, CampLeadOffer offer)
    {
        var result = TradeWindowSystem.Of(Api)!.Handle(player, trader,
            new TradeRequest { Action = TradeAction.BuyLead, Code = offer.Cell.ToString(), Price = offer.Price });
        output.WriteLine($"buy lead to {offer.Cell}: {(result.Ok ? "ok" : "refused")} {result.Key} {string.Join(",", result.Args)}");
        return result;
    }

    private static bool LeadTo(ItemStack stack, Mod.Trading.Core.CellKey cell) =>
        ItemTraderLead.IsDrawn(stack) && stack.Attributes.GetString(MapOfferAttrs.Cell) == cell.ToString();

    /// <summary>Waits for the lead to the cell to be drawn; with <paramref name="trader"/>, the player
    /// is its trading player again after (vanilla ends a trade when the player strays while chunks
    /// generate, as on CI).</summary>
    private async Task<ItemSlot> DrawnLead(IServerPlayer player, Mod.Trading.Core.CellKey cell, EntitySeraphTrader? trader = null)
    {
        await World.Until(() => Holding(player, ItemTraderLead.LeadCode, s => LeadTo(s, cell)) != null, 300_000);
        if (trader != null && trader.WatchedAttributes.GetString("tradingPlayerUID") != player.PlayerUID)
        {
            player.Entity.TeleportTo(trader.Pos.AsBlockPos.AddCopy(1, 0, 0));
            await World.Ticks(5);
            Assert.True(trader.BeginTrade(player));
        }
        return Holding(player, ItemTraderLead.LeadCode, s => LeadTo(s, cell))!;
    }

    private Mod.Trading.Core.CampRecord? Camp(Mod.Trading.Core.CellKey cell) => TradingSystem.Of(Api)!.Camps!.Registry.Get(cell);

    /// <summary>The camp's trader, spawned at its camp, the player next to it.</summary>
    private async Task<EntitySeraphTrader> TraderAt(Mod.Trading.Core.CampRecord camp, Atlas.Api.ITestPlayer p)
    {
        await p.TeleportTo(new BlockPos(camp.X, camp.Y + 2, camp.Z));
        await World.Ticks(20);
        var props = W.GetEntityType(new AssetLocation("seraphhorizons", $"trader-male-{camp.Type}-temperate"))!;
        var trader = (EntitySeraphTrader)W.ClassRegistry.CreateEntity(props);
        int ty = W.BlockAccessor.GetTerrainMapheightAt(new BlockPos(camp.X + 3, 0, camp.Z)) + 1;
        trader.Pos.SetPos(camp.X + 3.5, ty, camp.Z + 0.5);
        W.SpawnEntity(trader);
        await World.Ticks(5);
        await p.TeleportTo(trader.Pos.AsBlockPos.AddCopy(1, 0, 0));
        return trader;
    }

    [AtlasScenario(TimeoutMs = 600_000)]
    public async Task A_stranger_buys_one_lead_per_trader_and_buys_onward_at_the_camp_it_leads_to()
    {
        var store = await SpawnTrader("generalstore", 40, -36);
        // The shelf holds no camp lead: they are per buyer.
        Assert.DoesNotContain(Offers(store, MapOfferAttrs.Lead), s => s.Itemstack.Attributes.GetString(MapOfferAttrs.LeadKind) != "settlement");
        var p = await World.JoinPlayer("leadbuyer");
        await p.TeleportTo(store.Pos.AsBlockPos.AddCopy(1, 0, 0));
        await p.GiveItem("game:gear-rusty", 64);
        var buyer = (IServerPlayer)p.Player;
        buyer.WorldData.CurrentGameMode = EnumGameMode.Survival;
        Assert.True(store.BeginTrade(buyer));
        var view = LeadsAt(buyer, store);
        Assert.Equal("stranger", view.Buyer.Tier);
        var offer = Assert.Single(view.Offers);
        Assert.InRange(offer.Price, 1, 8);
        var state = WindowSystem.BuildState(buyer, store);
        Assert.Equal(offer.Cell.ToString(), Assert.Single(state.LeadOffers).Cell);
        Assert.Equal(offer.Price, state.LeadOffers[0].Price);

        int gears = InventoryTrader.GetPlayerAssets(buyer.Entity);
        Assert.True(BuyLead(buyer, store, offer).Ok);
        Assert.Equal(gears - offer.Price, InventoryTrader.GetPlayerAssets(buyer.Entity));
        var slot = await DrawnLead(buyer, offer.Cell, store);
        var camp = Camp(offer.Cell)!;
        Assert.Equal(Mod.Trading.Core.CampStatus.Placed, camp.Status);
        Assert.Equal(camp.X, slot.Itemstack.Attributes.GetInt(MapOfferAttrs.X));
        Assert.Equal(camp.Type, slot.Itemstack.Attributes.GetString(MapOfferAttrs.Type));

        // A second from the same trader: none on offer, and a buy naming the first again is refused
        // with nothing taken.
        var again = LeadsAt(buyer, store);
        Assert.Empty(again.Offers);
        Assert.Equal(CampLeadsWhy.StrangerUsed, again.Why);
        Assert.Equal("trading-window-leads-strangerused", WindowSystem.BuildState(buyer, store).LeadsWhy);
        var refused = BuyLead(buyer, store, offer);
        Assert.False(refused.Ok);
        Assert.Equal("trading-window-leads-strangerused", refused.Key);
        Assert.Equal(gears - offer.Price, InventoryTrader.GetPlayerAssets(buyer.Entity));

        // Read, it marks the camp.
        Read(slot, buyer);
        var mark = Assert.Single(Waypoints(Api, buyer), w => w.Icon == "trader");
        Assert.Equal(camp.X + 0.5, mark.Position.X);
        Assert.Equal(camp.Z + 0.5, mark.Position.Z);
        Close(store);
        store.WatchedAttributes.RemoveAttribute("tradingPlayerUID");

        // At the camp: its trader sells one onward, to a camp they lack, not back to where they were.
        var trader = await TraderAt(camp, p);
        Assert.Equal(offer.Cell, Maps.CampOf(trader));
        Assert.True(trader.BeginTrade(buyer));
        Assert.Contains(Mod.Trading.Standing.Core.TraderIds.Camp(offer.Cell.X, offer.Cell.Z), Maps.Leads.Visited(Maps.KeysOf(buyer.PlayerUID)));
        var onward = Assert.Single(LeadsAt(buyer, trader).Offers);
        Assert.NotEqual(offer.Cell, onward.Cell);
        Assert.NotEqual(Maps.CampOf(store), onward.Cell);
        Assert.True(BuyLead(buyer, trader, onward).Ok);
        await DrawnLead(buyer, onward.Cell);
        Assert.Empty(LeadsAt(buyer, trader).Offers);
        Close(trader);
        trader.Die(EnumDespawnReason.Removed);
        store.Die(EnumDespawnReason.Removed);
    }

    [AtlasScenario(TimeoutMs = 600_000)]
    public async Task A_known_customer_sees_two_leads_and_buying_one_brings_the_next_at_double_the_price()
    {
        var store = await SpawnTrader("generalstore", -36, -44);
        var buyer = await Buyer("knownbuyer", store);
        Assert.True(store.BeginTrade(buyer));
        Assert.True((await World.ExecuteCommand($"/sh trade standing set knownbuyer {Standing.TraderIdOf(store)} 100")).Ok);
        var rules = Maps.Prices.CampLeads;
        var before = LeadsAt(buyer, store);
        Assert.Equal("known", before.Buyer.Tier);
        Assert.Equal(2, before.Offers.Count);
        Assert.All(before.Offers, o => Assert.True(o.Distance <= 3000, $"{o.Cell} at {o.Distance:0} m"));
        Assert.All(before.Offers, o => Assert.Equal(rules.Price(o.Distance, 0, "known"), o.Price));

        var bought = before.Offers[0];
        var kept = before.Offers[1];
        Assert.True(BuyLead(buyer, store, bought).Ok);
        await DrawnLead(buyer, bought.Cell, store);
        var after = LeadsAt(buyer, store);
        Assert.Equal(1, after.Buyer.Bought);
        Assert.DoesNotContain(bought.Cell, after.Offers.Select(o => o.Cell));
        // The other stays, at twice the price (rounding apart); the next-nearest takes the slot.
        var keptAfter = after.Offers.Single(o => o.Cell == kept.Cell);
        Assert.Equal(rules.Price(kept.Distance, 1, "known"), keptAfter.Price);
        Assert.InRange(keptAfter.Price, 2 * kept.Price - 1, 2 * kept.Price + 1);
        if (after.Offers.Count == 2)
        {
            var next = after.Offers.Single(o => o.Cell != kept.Cell);
            Assert.DoesNotContain(next.Cell, before.Offers.Select(o => o.Cell));
            Assert.True(next.Prospector || next.Distance >= before.Offers.Where(o => !o.Prospector).Max(o => o.Distance) - 1e-6);
        }
        else Assert.Single(after.Offers);

        // Once more: the count goes on, and so does the price.
        var second = after.Offers[0];
        Assert.True(BuyLead(buyer, store, second).Ok);
        await DrawnLead(buyer, second.Cell, store);
        var third = LeadsAt(buyer, store);
        Assert.Equal(2, third.Buyer.Bought);
        Assert.All(third.Offers, o => Assert.Equal(rules.Price(o.Distance, 2, "known"), o.Price));
        Close(store);
        store.Die(EnumDespawnReason.Removed);
    }

    [AtlasScenario(TimeoutMs = 900_000)]
    public async Task A_partners_lead_to_a_camp_nobody_generated_settles_it_and_marks_it_there()
    {
        var store = await SpawnTrader("generalstore", 44, 44);
        var buyer = await Buyer("partnerbuyer", store);
        Assert.True(store.BeginTrade(buyer));
        Assert.True((await World.ExecuteCommand($"/sh trade standing set partnerbuyer {Standing.TraderIdOf(store)} 2500")).Ok);
        var view = LeadsAt(buyer, store);
        Assert.Equal("partner", view.Buyer.Tier);
        Assert.InRange(view.Offers.Count, 3, 8);
        Assert.All(view.Offers, o => Assert.True(o.Distance <= 12000));
        // From known up, the first slot is the nearest prospector when the buyer has none in reach.
        if (view.Offers.Any(o => o.Type == "prospector")) Assert.True(view.Offers[0].Prospector);

        foreach (var offer in view.Offers.Where(o => Camp(o.Cell) is not { Status: Mod.Trading.Core.CampStatus.Placed }).OrderBy(o => o.Distance).Take(3))
        {
            int gears = InventoryTrader.GetPlayerAssets(buyer.Entity);
            Assert.True(BuyLead(buyer, store, offer).Ok);
            // Drawn, or refunded when the cell turned out to have no camp.
            await World.Until(() => Holding(buyer, ItemTraderLead.LeadCode, s => LeadTo(s, offer.Cell)) != null
                                    || InventoryTrader.GetPlayerAssets(buyer.Entity) == gears, 600_000);
            var slot = Holding(buyer, ItemTraderLead.LeadCode, s => LeadTo(s, offer.Cell));
            if (slot is null)
            {
                output.WriteLine($"{offer.Cell} placed no camp: refunded, and skipped from now on");
                Assert.DoesNotContain(offer.Cell, LeadsAt(buyer, store).Offers.Select(o => o.Cell));
                continue;
            }
            var camp = Camp(offer.Cell)!;
            Assert.Equal(Mod.Trading.Core.CampStatus.Placed, camp.Status);
            Assert.Equal(camp.X, slot.Itemstack.Attributes.GetInt(MapOfferAttrs.X));
            Assert.Equal(camp.Z, slot.Itemstack.Attributes.GetInt(MapOfferAttrs.Z));
            Read(slot, buyer);
            var mark = Waypoints(Api, buyer).Last(w => w.Icon == "trader");
            output.WriteLine($"{offer.Cell}: offered at {offer.X},{offer.Z}, settled at {camp.X},{camp.Z}; '{mark.Title}'");
            Assert.Equal(camp.X + 0.5, mark.Position.X);
            Assert.Equal(camp.Z + 0.5, mark.Position.Z);
            Close(store);
            store.Die(EnumDespawnReason.Removed);
            return;
        }
        throw new Xunit.Sdk.XunitException("no unplaced camp among the partner's leads settled");
    }

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Standing_changes_the_prices_a_trader_quotes_and_the_maps_it_draws()
    {
        var trader = await SpawnTrader("prospector", -40, -40);
        var buyer = await Buyer("glueregular", trader);
        string id = Standing.TraderIdOf(trader);
        var priciest = trader.Inventory.SellingSlots
            .Where(s => s.TradeItem is { Price: > 0 } && s.Itemstack.Attributes.GetString(MapOfferAttrs.Offer) is null)
            .OrderByDescending(s => s.TradeItem.Price).First();
        string code = priciest.Itemstack.Collectible.Code.ToString();
        Open(trader, buyer);
        int stranger = priciest.TradeItem.Price;
        int strangerMap = Offers(trader, MapOfferAttrs.OreMap).FirstOrDefault()?.TradeItem.Price ?? 0;
        Close(trader);

        Assert.True((await World.ExecuteCommand($"/sh trade standing set glueregular {id} 900")).Ok);
        Assert.Equal("trusted", Standing.ViewFor(buyer.PlayerUID, trader).Tier.Code);
        Open(trader, buyer);
        var tree = (Vintagestory.API.Datastructures.ITreeAttribute)trader.WatchedAttributes[StandingPriceModifier.Attr];
        Assert.Equal(buyer.PlayerUID, tree.GetString("uid"));
        Assert.Equal(0.9, tree.GetDouble("buy"), 6);
        int trusted = trader.Inventory.SellingSlots.First(s => s.Itemstack?.Collectible.Code.ToString() == code).TradeItem.Price;
        output.WriteLine($"{code}: {stranger} for a stranger, {trusted} for a trusted customer");
        // 0.9 of a price under 5 may round back to it.
        if (stranger >= 5) Assert.True(trusted < stranger, $"{code}: {trusted} is not under {stranger}");
        else Assert.True(trusted <= stranger, $"{code}: {trusted} is over {stranger}");
        // A trusted customer is shown (and sold) exact ore maps.
        foreach (var o in Offers(trader, MapOfferAttrs.OreMap))
            Assert.Equal(MapPrecision.Exact, o.Itemstack.Attributes.GetAsInt(MapOfferAttrs.Precision));
        // An exact map costs more than a rough one, standing's discount notwithstanding.
        if (strangerMap > 0) Assert.True(Offers(trader, MapOfferAttrs.OreMap)[0].TradeItem.Price > strangerMap);
        // Someone else at the trader: the stranger's prices again.
        Close(trader);
        Assert.Equal(stranger, trader.Inventory.SellingSlots.First(s => s.Itemstack?.Collectible.Code.ToString() == code).TradeItem.Price);

        // A curio dealer's dear goods show the discount plainly, and what it pays off its list rises.
        var curio = await SpawnTrader("curiodealer", -46, -40);
        var dear = curio.Inventory.SellingSlots
            .Where(s => s.TradeItem is { Price: > 0 } && s.Itemstack.Attributes.GetString(MapOfferAttrs.Offer) is null)
            .OrderByDescending(s => s.TradeItem.Price).First();
        output.WriteLine("curio shelf: " + string.Join(", ", curio.Inventory.SellingSlots.Where(s => s.TradeItem != null)
            .Select(s => $"{s.Itemstack.Collectible.Code.Path} {s.TradeItem.Price}")));
        Assert.True(dear.TradeItem.Price >= 5, $"the curio dealer's dearest good costs {dear.TradeItem.Price}");
        string dearCode = dear.Itemstack.Collectible.Code.ToString();
        var economy = Mod.Trading.Economy.EconomySystem.Of(Api)!;
        var copper = new ItemStack(W.GetItem(new AssetLocation("game:ingot-copper"))!, 1);
        Open(curio, buyer);
        int dearStranger = dear.TradeItem.Price;
        var offerStranger = economy.QuoteOffList(curio, copper);
        Close(curio);
        Assert.True((await World.ExecuteCommand($"/sh trade standing set glueregular {Standing.TraderIdOf(curio)} 900")).Ok);
        Open(curio, buyer);
        int dearTrusted = curio.Inventory.SellingSlots.First(s => s.Itemstack?.Collectible.Code.ToString() == dearCode).TradeItem.Price;
        var offerTrusted = economy.QuoteOffList(curio, copper);
        output.WriteLine($"{dearCode}: {dearStranger} -> {dearTrusted}; copper offer modifiers {offerStranger.Modifiers} -> {offerTrusted.Modifiers}");
        Assert.True(dearTrusted < dearStranger);
        Assert.Equal(1, offerStranger.Modifiers, 6);
        Assert.Equal(1.08, offerTrusted.Modifiers, 6);
        Close(curio);
        curio.Die(EnumDespawnReason.Removed);

        // The shelf follows the best recent customer; simulated days age that out.
        Assert.Equal(3, Standing.ShelfTierFor(trader));
        Assert.True((await World.ExecuteCommand("/sh trade simulate 15")).Ok);
        Assert.Equal(0, Standing.ShelfTierFor(trader));
        trader.Die(EnumDespawnReason.Removed);
    }

    // ---- The playtest after the trade window: maps the player has, a claim gone, meeting a camp ----

    private TradeWindowSystem WindowSystem => TradeWindowSystem.Of(Api)!;

    private TradeResult TryBuy(EntitySeraphTrader trader, IServerPlayer player, ItemSlotTrade selling)
    {
        int slot = Array.IndexOf(trader.Inventory.SellingSlots, selling);
        Assert.True(slot >= 0);
        var result = WindowSystem.Handle(player, trader, new TradeRequest { Action = TradeAction.Buy, Slot = slot });
        output.WriteLine($"buy {selling.Itemstack?.GetName()}: {(result.Ok ? "ok" : "refused")} {result.Key}");
        return result;
    }

    private static int SlotOf(EntitySeraphTrader trader, ItemSlotTrade selling) => Array.IndexOf(trader.Inventory.SellingSlots, selling);

    /// <summary>A general store with a gravel map on its shelf, at the first of these offsets that has one.</summary>
    private async Task<EntitySeraphTrader> StoreWithGravel(params (int Dx, int Dz)[] spots)
    {
        foreach (var (dx, dz) in spots)
        {
            var t = await SpawnTrader("generalstore", dx, dz);
            if (Offers(t, MapOfferAttrs.GravelMap).Count == 1) return t;
            t.Die(EnumDespawnReason.Removed);
        }
        throw new Xunit.Sdk.XunitException("no general store with a gravel map at " + string.Join(" ", spots));
    }

    private static List<Waypoint> Waypoints(ICoreServerAPI api, IServerPlayer player) =>
        api.ModLoader.GetModSystem<WorldMapManager>().MapLayers.OfType<WaypointMapLayer>().Single().Waypoints
            .Where(w => w.OwningPlayerUid == player.PlayerUID).ToList();

    private static void Read(ItemSlot slot, IServerPlayer player)
    {
        EnumHandHandling handling = EnumHandHandling.NotHandled;
        slot.Itemstack.Collectible.OnHeldInteractStart(slot, player.Entity, null, null, true, ref handling);
    }

    [AtlasScenario(TimeoutMs = 600_000)]
    public async Task A_lead_bought_is_not_offered_again_and_a_gravel_map_bought_after_it_arrives()
    {
        // The playtest's sequence: a lead, the same lead again, then the gravel map.
        var trader = await StoreWithGravel((-60, 60), (60, 60), (-24, 18), (90, -20), (-90, -20));
        var buyer = await Buyer("dupebuyer", trader);
        Assert.True(trader.BeginTrade(buyer));
        // Known here, so the trader has more than the stranger's one lead for them.
        Assert.True((await World.ExecuteCommand($"/sh trade standing set dupebuyer {Standing.TraderIdOf(trader)} 100")).Ok);
        var lead = LeadsAt(buyer, trader).Offers[0];
        Assert.True(BuyLead(buyer, trader, lead).Ok);
        await DrawnLead(buyer, lead.Cell, trader);

        // The same lead again: no longer offered (a copy carried), and a buy naming it is refused
        // before any gears move.
        int gears = InventoryTrader.GetPlayerAssets(buyer.Entity);
        Assert.DoesNotContain(lead.Cell, LeadsAt(buyer, trader).Offers.Select(o => o.Cell));
        var again = BuyLead(buyer, trader, lead);
        Assert.False(again.Ok);
        Assert.Equal("trading-window-changed", again.Key);
        Assert.Equal(gears, InventoryTrader.GetPlayerAssets(buyer.Entity));

        // The gravel map arrives.
        var gravel = Offers(trader, MapOfferAttrs.GravelMap).Single();
        int price = gravel.TradeItem.Price;
        Assert.DoesNotContain(SlotOf(trader, gravel), WindowSystem.BuildState(buyer, trader).OwnedMaps);
        Buy(trader, buyer, gravel);
        Assert.Equal(gears - price, InventoryTrader.GetPlayerAssets(buyer.Entity));
        await World.Until(() => Holding(buyer, MapIssuer.GravelMapCode, s => s.Attributes.HasAttribute(ItemOreMap.AttrX)) != null, 300_000);
        Assert.Null(Holding(buyer, MapIssuer.GravelMapCode, s => s.Attributes.HasAttribute(MapOfferAttrs.Pending)));
        var leads = buyer.InventoryManager.InventoriesOrdered.Where(i => i is InventoryBasePlayer && i.ClassName != GlobalConstants.creativeInvClassName)
            .SelectMany(i => i).Count(s => s.Itemstack?.Collectible?.Code == ItemTraderLead.LeadCode);
        Assert.Equal(1, leads);

        // Read, the lead marks the camp roughly; read, the gravel map marks the field exactly.
        var leadSlot = Holding(buyer, ItemTraderLead.LeadCode, ItemTraderLead.IsDrawn)!;
        Read(leadSlot, buyer);
        var mapSlot = Holding(buyer, MapIssuer.GravelMapCode, s => s.Attributes.HasAttribute(ItemOreMap.AttrX))!;
        Read(mapSlot, buyer);
        var marks = Waypoints(Api, buyer);
        output.WriteLine("waypoints: " + string.Join(" | ", marks.Select(w => w.Title)));
        Assert.Single(marks, w => w.Icon == "trader" && w.Title.EndsWith("(approximate, ±64 m)", StringComparison.Ordinal));
        Assert.Single(marks, w => w.Icon == "rocks" && w.Title.EndsWith("(exact)", StringComparison.Ordinal));
        // Reading again adds nothing.
        Read(leadSlot, buyer);
        Assert.Equal(marks.Count, Waypoints(Api, buyer).Count);

        // Without the copy, the lead is still not offered: its camp is on the map.
        leadSlot.Itemstack = null;
        leadSlot.MarkDirty();
        Assert.DoesNotContain(lead.Cell, LeadsAt(buyer, trader).Offers.Select(o => o.Cell));
        Assert.Equal(gears - price, InventoryTrader.GetPlayerAssets(buyer.Entity));
        Close(trader);
        trader.Die(EnumDespawnReason.Removed);
    }

    [AtlasScenario(TimeoutMs = 300_000)]
    public async Task A_gravel_offer_whose_field_turned_out_empty_is_refused_before_payment()
    {
        // The playtest's lost gravel map: its cell's every spot failed after the shelf was stocked,
        // and the sale took the gears, found nothing and refunded them ("fell through").
        var trader = await StoreWithGravel((24, -60), (-60, -24), (60, 24), (-24, 18), (60, 60));
        var buyer = await Buyer("gonebuyer", trader);
        Assert.True(trader.BeginTrade(buyer));
        var gravel = Offers(trader, MapOfferAttrs.GravelMap).Single();
        Assert.True(DepositKey.TryParse(gravel.Itemstack.Attributes.GetString(MapOfferAttrs.Deposit), out var key));
        var placer = Ore.Placer!;
        var book = (PlacerBook)typeof(PlacerFields).GetField("_book", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(placer)!;
        var states = (System.Collections.IDictionary)typeof(OreCellBook).GetField("_states", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(book.Cells)!;
        var stateKey = (PlacerCells.Kind, key.Cell);
        object? saved = states.Contains(stateKey) ? states[stateKey] : null;
        var was = placer.StateOf(key.Cell);
        states[stateKey] = new CellState { SpotCount = was.SpotCount, Active = was.SpotCount };
        try
        {
            Assert.Null(Deposits.Candidate(key));
            int gears = InventoryTrader.GetPlayerAssets(buyer.Entity);
            var result = TryBuy(trader, buyer, gravel);
            Assert.False(result.Ok);
            Assert.Equal("trading-maps-error-gone", result.Key);
            Assert.Equal(gears, InventoryTrader.GetPlayerAssets(buyer.Entity));
            Assert.Equal(0, gravel.TradeItem.Stock);
            Assert.Null(Holding(buyer, MapIssuer.GravelMapCode, _ => true));
            // The window shows it sold out from now on.
            Assert.Equal("trading-window-soldout", TryBuy(trader, buyer, gravel).Key);
        }
        finally
        {
            if (saved is null) states.Remove(stateKey);
            else states[stateKey] = saved;
        }
        Close(trader);
        trader.Die(EnumDespawnReason.Removed);
    }

    [AtlasScenario(TimeoutMs = 600_000)]
    public async Task Meeting_a_camp_trader_marks_it_exactly_in_place_of_the_leads_marker()
    {
        var store = await SpawnTrader("generalstore", -40, 36);
        var p = await World.JoinPlayer("meetbuyer");
        await p.TeleportTo(store.Pos.AsBlockPos.AddCopy(1, 0, 0));
        await p.GiveItem("game:gear-rusty", 64);
        var buyer = (IServerPlayer)p.Player;
        buyer.WorldData.CurrentGameMode = EnumGameMode.Survival;
        Assert.True(store.BeginTrade(buyer));
        var lead = Assert.Single(LeadsAt(buyer, store).Offers);
        Assert.True(BuyLead(buyer, store, lead).Ok);
        var leadSlot = await DrawnLead(buyer, lead.Cell);
        var cell = lead.Cell;
        var camp = TradingSystem.Of(Api)!.Camps!.Registry.Get(cell)!;
        Read(leadSlot, buyer);
        string typeTitle = Lang.Get("seraphhorizons:trading-maps-lead-title", Lang.Get("seraphhorizons:trading-type-" + camp.Type));
        // And a marker of the camp from before markers were remembered: no guid, the lead's old title.
        var layer = Api.ModLoader.GetModSystem<WorldMapManager>().MapLayers.OfType<WaypointMapLayer>().Single();
        layer.Waypoints.Add(new Waypoint
        {
            Icon = "trader", Title = typeTitle, OwningPlayerUid = buyer.PlayerUID, Position = new Vec3d(camp.X + 10.5, camp.Y, camp.Z - 7.5), Pinned = true,
        });
        int before = Waypoints(Api, buyer).Count(w => w.Icon == "trader");
        Assert.Equal(2, before);
        Close(store);
        store.WatchedAttributes.RemoveAttribute("tradingPlayerUID");

        // At the camp: its trader, met.
        var trader = await TraderAt(camp, p);
        Assert.Equal(cell, Maps.CampOf(trader));
        Assert.True(trader.BeginTrade(buyer));
        var traders = Waypoints(Api, buyer).Where(w => w.Icon == "trader").ToList();
        output.WriteLine("after meeting: " + string.Join(" | ", traders.Select(w => $"{w.Title} at {w.Position.X},{w.Position.Z}")));
        var exact = Assert.Single(traders);
        Assert.Equal(typeTitle + " (exact)", exact.Title);
        Assert.Equal(Math.Floor(trader.Pos.X) + 0.5, exact.Position.X);
        Assert.NotNull(exact.Guid);

        // Again: no second marker.
        trader.WatchedAttributes.RemoveAttribute("tradingPlayerUID");
        Assert.True(trader.BeginTrade(buyer));
        Assert.Single(Waypoints(Api, buyer), w => w.Icon == "trader");
        // The lead read again says it is marked, and adds nothing; the camp is not offered again,
        // even to the buyer known at the store (who gets more than a stranger's one).
        Read(leadSlot, buyer);
        Assert.Single(Waypoints(Api, buyer), w => w.Icon == "trader");
        leadSlot.Itemstack = null;
        leadSlot.MarkDirty();
        trader.WatchedAttributes.RemoveAttribute("tradingPlayerUID");
        await p.TeleportTo(store.Pos.AsBlockPos.AddCopy(1, 0, 0));
        Assert.True(store.BeginTrade(buyer));
        Assert.True((await World.ExecuteCommand($"/sh trade standing set meetbuyer {Standing.TraderIdOf(store)} 100")).Ok);
        Assert.DoesNotContain(cell, LeadsAt(buyer, store).Offers.Select(o => o.Cell));
        Close(store);
        store.Die(EnumDespawnReason.Removed);
        trader.Die(EnumDespawnReason.Removed);
    }
}
