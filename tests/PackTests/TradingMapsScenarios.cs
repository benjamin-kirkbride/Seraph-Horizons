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
/// Trader maps (#455, <see cref="TradingMapsScenarioBase"/>): maps are on and the boot logs nothing
/// about them; a general store offers a gravel map exactly when a field is in reach (naming its rock
/// and metals once checked); standing changes the prices a trader quotes but not an ore map's precision. Camp leads (per buyer, off
/// the shelf, distance in rings of grid cells): a fresh player at a trader that is no prospector is
/// offered their first map alone, 10 gears to a prospector, which generates the camp and marks it, and
/// is that trader's one stranger map; at the prospector's camp they buy one onward in ring 1; a
/// prospector offers no first map and a stranger's reach is ring 1; a known customer is offered two in
/// ring 1 at the formula's price, and buying one brings the next-nearest; a partner's lead to a camp
/// nobody generated settles the camp and marks it where it stands. From the playtest after the trade
/// window: a lead bought is not offered again, carried or marked, while the gravel map bought after it
/// arrives; a gravel offer whose field turned out empty is refused before payment; and meeting a camp's
/// trader marks it exactly in place of the lead's marker. The prospector's shelf and a far camp's
/// background checks are <see cref="TradingMapsShelfScenarios"/> and <see cref="TradingMapsApproachScenarios"/>.
/// </summary>
[AtlasWorld(Seed = Seed, WorldType = "standard")]
public class TradingMapsScenarios(ITestOutputHelper output) : TradingMapsScenarioBase(output)
{
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
    public async Task A_general_store_offers_a_gravel_map_exactly_when_a_field_is_in_reach()
    {
        var trader = await SpawnTrader("generalstore", -24, 18);
        int x = (int)trader.Pos.X, z = (int)trader.Pos.Z;
        var fields = Deposits.GravelFields(x, z, Maps.Prices.GravelRadius);
        var unsold = fields.Where(f => f.Record.State == DepositState.Unsold && !Maps.Reserved.Contains(f.Key.Id)).ToList();
        var offers = DepositOffers(trader, MapIssuer.GravelMapCode);
        output.WriteLine($"{fields.Count} fields within {Maps.Prices.GravelRadius}, {unsold.Count} unsold; offers: " + string.Join(", ", offers.Select(Describe)));
        if (unsold.Count == 0)
            Assert.Empty(offers);
        else
            Assert.Equal(unsold[0].Key.Id, Assert.Single(offers).Itemstack.Attributes.GetString(MapOfferAttrs.Deposit));
        Assert.Equal(fields.Count > 0 && unsold.Count == 0, Offers(trader, MapOfferAttrs.SoldOut).Count == 1);
        // No ore maps here: those are the prospector's.
        Assert.Empty(DepositOffers(trader, MapIssuer.OreMapCode));
        if (unsold.Count > 0)
        {
            // Checked (its one column) when the trade opens if it wasn't: then it names its rock and metals (#692).
            var buyer = await Buyer("gravelbuyer", trader);
            Open(trader, buyer);
            trader.OnTradeOpened(buyer);
            await SurveysLand(trader, "general store");
            output.WriteLine("after the check: " + string.Join(", ", DepositOffers(trader, MapIssuer.GravelMapCode).Select(Describe)));
            foreach (var o in Offers(trader, MapOfferAttrs.GravelMap))
            {
                Assert.True(DepositKey.TryParse(o.Itemstack.Attributes.GetString(MapOfferAttrs.Deposit), out var key));
                var field = Deposits.FieldOf(key);
                Assert.NotNull(field);
                Assert.Equal(field!.Rock, o.Itemstack.Attributes.GetString(MapOfferAttrs.Rock));
                Assert.NotEmpty(OreNames.Split(o.Itemstack.Attributes.GetString(MapOfferAttrs.Metals)));
                output.WriteLine($"{key}: '{o.Itemstack.GetName()}'");
            }
            Close(trader);
        }
        // Maps are never bought back.
        var economy = Mod.Trading.Economy.EconomySystem.Of(Api)!;
        var gravelMap = new ItemStack(W.GetItem(MapIssuer.GravelMapCode)!);
        Assert.Equal(Mod.Trading.Economy.Core.Refusal.MapOrLead, economy.QuoteOffList(trader, gravelMap).Refusal);
        var lead = new ItemStack(W.GetItem(ItemTraderLead.LeadCode)!);
        Assert.Equal(Mod.Trading.Economy.Core.Refusal.MapOrLead, economy.QuoteOffList(trader, lead).Refusal);
        trader.Die(EnumDespawnReason.Removed);
    }

    [AtlasScenario(TimeoutMs = 900_000)]
    public async Task A_fresh_players_first_map_is_ten_gears_to_a_prospector_and_they_buy_onward_at_its_camp()
    {
        var store = await SpawnTrader("generalstore", 40, -36);
        // The shelf holds no camp lead: they are per buyer.
        Assert.DoesNotContain(Offers(store, MapOfferAttrs.Lead), s => s.Itemstack.Attributes.GetString(MapOfferAttrs.LeadKind) != "settlement");
        var p = await World.JoinAtSpawn("leadbuyer");
        await p.TeleportTo(store.Pos.AsBlockPos.AddCopy(1, 0, 0));
        await p.GiveItem("game:gear-rusty", 64);
        var buyer = (IServerPlayer)p.Player;
        buyer.WorldData.CurrentGameMode = EnumGameMode.Survival;
        Assert.True(store.BeginTrade(buyer));
        var view = LeadsAt(buyer, store);
        Assert.Equal("stranger", view.Buyer.Tier);
        Assert.False(view.PityUsed);
        // A fresh player at a trader that is no prospector: the first map, alone, at 10 gears, to the
        // nearest prospector (the lattice puts one within two rings).
        var offer = Assert.Single(view.Offers);
        Assert.True(offer.Pity);
        Assert.Equal(10, offer.Price);
        Assert.Equal("prospector", offer.Type);
        Assert.True(offer.Prospector);
        Assert.InRange(offer.Ring, 1, 2);
        var state = WindowSystem.BuildState(buyer, store);
        var row = Assert.Single(state.LeadOffers);
        Assert.True(row.Pity);
        Assert.Equal(offer.Cell.ToString(), row.Cell);
        Assert.Equal(10, row.Price);
        Assert.Contains("trading-window-offer-firstmap", Mod.Trading.Window.Core.TradeWindowModel.CampLeadRow(row).Details.Select(d => ((Mod.Trading.Window.Core.Text)d).Key));

        int gears = InventoryTrader.GetPlayerAssets(buyer.Entity);
        Assert.True(BuyLead(buyer, store, offer).Ok);
        Assert.Equal(gears - 10, InventoryTrader.GetPlayerAssets(buyer.Entity));
        var (slot, cell) = await DrawnPity(buyer, store);
        var camp = Camp(cell)!;
        output.WriteLine($"first map: offered {offer.Cell}, settled {cell} ({camp.Type} at {camp.X},{camp.Z})");
        Assert.Equal(Mod.Trading.Core.CampStatus.Placed, camp.Status);
        Assert.Equal("prospector", camp.Type);
        Assert.Equal(camp.X, slot.Itemstack.Attributes.GetInt(MapOfferAttrs.X));
        Assert.Equal(camp.Type, slot.Itemstack.Attributes.GetString(MapOfferAttrs.Type));
        Assert.True(Maps.Leads.PityUsed(buyer.PlayerUID));

        // It was this trader's one stranger map: none more on offer, and a buy naming it again is
        // refused with nothing taken.
        var again = LeadsAt(buyer, store);
        Assert.Empty(again.Offers);
        Assert.Equal(CampLeadsWhy.StrangerUsed, again.Why);
        Assert.Equal(1, again.Buyer.Bought);
        Assert.Equal("trading-window-leads-strangerused", WindowSystem.BuildState(buyer, store).LeadsWhy);
        var refused = BuyLead(buyer, store, offer);
        Assert.False(refused.Ok);
        Assert.Equal("trading-window-leads-strangerused", refused.Key);
        Assert.Equal(gears - 10, InventoryTrader.GetPlayerAssets(buyer.Entity));

        // Read, it marks the camp.
        Read(slot, buyer);
        var mark = Assert.Single(Waypoints(Api, buyer), w => w.Icon == "trader");
        Assert.Equal(camp.X + 0.5, mark.Position.X);
        Assert.Equal(camp.Z + 0.5, mark.Position.Z);
        Close(store);
        store.WatchedAttributes.RemoveAttribute("tradingPlayerUID");

        // At the prospector's camp: no first map again (had, and a prospector never offers it); its
        // trader sells one onward in ring 1, to a camp they lack, not back to where they were.
        var trader = await TraderAt(camp, p);
        Assert.Equal(cell, Maps.CampOf(trader));
        Assert.True(trader.BeginTrade(buyer));
        Assert.Contains(Mod.Trading.Standing.Core.TraderIds.Camp(cell.X, cell.Z), Maps.Leads.Visited(Maps.KeysOf(buyer.PlayerUID)));
        var onwardView = LeadsAt(buyer, trader);
        var onward = Assert.Single(onwardView.Offers);
        Assert.False(onward.Pity);
        Assert.Equal(1, onward.Ring);
        Assert.Equal(Maps.Prices.CampLeads.Price(1, 0, "stranger"), onward.Price);
        Assert.NotEqual(cell, onward.Cell);
        Assert.True(BuyLead(buyer, trader, onward).Ok);
        await DrawnLead(buyer, onward.Cell);
        Assert.Empty(LeadsAt(buyer, trader).Offers);
        Close(trader);
        trader.Die(EnumDespawnReason.Removed);
        store.Die(EnumDespawnReason.Removed);
    }

    [AtlasScenario(TimeoutMs = 300_000)]
    public async Task A_prospector_offers_no_first_map_and_a_strangers_reach_is_ring_one()
    {
        var prospector = await SpawnTrader("prospector", -52, 20);
        var buyer = await Buyer("prospectfresh", prospector);
        Assert.True(prospector.BeginTrade(buyer));
        var view = LeadsAt(buyer, prospector);
        Assert.Equal("stranger", view.Buyer.Tier);
        Assert.False(view.PityUsed);
        Assert.Equal(1, view.Reach);
        Assert.DoesNotContain(view.Offers, o => o.Pity);
        Assert.DoesNotContain(WindowSystem.BuildState(buyer, prospector).LeadOffers, r => r.Pity);
        // The stranger's one map: ring 1, at the formula's price.
        var offer = Assert.Single(view.Offers);
        Assert.Equal(1, offer.Ring);
        Assert.Equal(Maps.Prices.CampLeads.Price(1, 0, "stranger"), offer.Price);
        Assert.Equal(24, offer.Price);
        // The /sh trade leads output names the ring and the first map's state.
        var answer = await World.ExecuteCommand($"/sh trade leads prospectfresh entity:{prospector.EntityId}");
        output.WriteLine(answer.Message);
        Assert.True(answer.Ok, answer.Message);
        Assert.Contains("first map: not yet", answer.Message);
        Assert.Contains($"{offer.Cell} ring 1 ", answer.Message);
        Close(prospector);
        prospector.Die(EnumDespawnReason.Removed);
    }

    [AtlasScenario(TimeoutMs = 300_000)]
    public async Task A_stranger_whose_group_had_its_map_here_is_still_offered_their_first_map()
    {
        // The first map is offered at stranger standing whatever the group has had from this trader
        // (as for a player in a world saved before the first map, whose stranger maps are spent).
        var store = await SpawnTrader("generalstore", 36, 44);
        var buyer = await Buyer("pitystranger", store);
        Assert.True(store.BeginTrade(buyer));
        var fresh = LeadsAt(buyer, store);
        Assert.Equal("stranger", fresh.Buyer.Tier);
        Maps.Leads.RecordBought(Maps.KeysOf(buyer.PlayerUID), fresh.TraderId, asStranger: true);
        var view = LeadsAt(buyer, store);
        Assert.True(view.Buyer.StrangerUsed);
        Assert.False(view.PityUsed);
        var offer = Assert.Single(view.Offers);
        Assert.True(offer.Pity);
        Assert.Equal(10, offer.Price);
        Assert.Equal(CampLeadsWhy.None, view.Why);
        // Its line says how far in km, and no ring.
        var row = Assert.Single(WindowSystem.BuildState(buyer, store).LeadOffers);
        string line = Mod.Trading.Window.Core.TradeWindowModel.CampLeadRow(row).ToString();
        output.WriteLine(line);
        Assert.Contains("map-distance-", line);
        Close(store);
        store.Die(EnumDespawnReason.Removed);
    }

    [AtlasScenario(TimeoutMs = 600_000)]
    public async Task A_known_customer_sees_two_leads_in_ring_one_priced_by_the_formula_and_buying_one_brings_the_next()
    {
        var store = await SpawnTrader("generalstore", -36, -44);
        var buyer = await Buyer("knownbuyer", store, 192);
        Assert.True(store.BeginTrade(buyer));
        Assert.True((await World.ExecuteCommand($"/sh trade standing set knownbuyer {Standing.TraderIdOf(store)} 100")).Ok);
        var rules = Maps.Prices.CampLeads;
        var before = LeadsAt(buyer, store);
        Assert.Equal("known", before.Buyer.Tier);
        Assert.Equal(1, before.Reach);
        // Their first map is on offer first (they have not had it), the tier's two after it.
        Assert.True(before.Offers[0].Pity);
        Assert.Equal(10, before.Offers[0].Price);
        var plain = Plain(before);
        Assert.Equal(2, plain.Count);
        Assert.All(plain, o => Assert.Equal(1, o.Ring));
        // price = round(12 × (1 + 1.5 × 0.85 × ln(1 + ring)) × (1 + 3 × 0.85 × ln(1 + n)) × 0.85)
        double Formula(int ring, int n) => 12 * (1 + 1.5 * 0.85 * Math.Log(1 + ring)) * (1 + 3 * 0.85 * Math.Log(1 + n)) * 0.85;
        Assert.All(plain, o => Assert.Equal((int)Math.Round(Formula(o.Ring, 0), MidpointRounding.AwayFromZero), o.Price));
        Assert.All(plain, o => Assert.Equal(19, o.Price));

        var bought = plain[0];
        var kept = plain[1];
        Assert.True(BuyLead(buyer, store, bought).Ok);
        await DrawnLead(buyer, bought.Cell, store);
        var after = LeadsAt(buyer, store);
        Assert.Equal(1, after.Buyer.Bought);
        var plainAfter = Plain(after);
        Assert.DoesNotContain(bought.Cell, after.Offers.Select(o => o.Cell));
        // The other stays, priced for one bought here; the next-nearest takes the slot.
        var keptAfter = plainAfter.Single(o => o.Cell == kept.Cell);
        Assert.Equal(rules.Price(kept.Ring, 1, "known"), keptAfter.Price);
        Assert.Equal((int)Math.Round(Formula(1, 1), MidpointRounding.AwayFromZero), keptAfter.Price);
        Assert.Equal(53, keptAfter.Price);
        if (plainAfter.Count == 2)
        {
            var next = plainAfter.Single(o => o.Cell != kept.Cell);
            Assert.DoesNotContain(next.Cell, plain.Select(o => o.Cell));
            Assert.Equal(1, next.Ring);
        }
        else Assert.Single(plainAfter);

        // Once more: the count goes on, and so does the price.
        var second = plainAfter[0];
        Assert.True(BuyLead(buyer, store, second).Ok);
        await DrawnLead(buyer, second.Cell, store);
        var third = LeadsAt(buyer, store);
        Assert.Equal(2, third.Buyer.Bought);
        Assert.All(Plain(third), o => Assert.Equal(rules.Price(o.Ring, 2, "known"), o.Price));
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
        Assert.Equal(5, view.Reach);
        // Their first map first; the tier's eight after it, within five rings.
        Assert.True(view.Offers[0].Pity);
        var plain = Plain(view);
        Assert.Equal(8, plain.Count);
        Assert.All(plain, o => Assert.True(o.Ring <= 5, $"{o.Cell} in ring {o.Ring}"));
        Assert.All(plain, o => Assert.Equal(Maps.Prices.CampLeads.Price(o.Ring, 0, "partner"), o.Price));
        // From known up, the first slot is the nearest prospector when the buyer has none in reach
        // (the first map's prospector is not among them, and does not stop it).
        Assert.True(plain[0].Prospector);
        Assert.NotEqual(view.Offers[0].Cell, plain[0].Cell);

        foreach (var offer in plain.Where(o => Camp(o.Cell) is not { Status: Mod.Trading.Core.CampStatus.Placed }).OrderBy(o => o.Distance).Take(3))
        {
            await BackAt(store, buyer);
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
    public async Task Standing_changes_the_prices_a_trader_quotes_but_not_its_maps_precision()
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
        var firstMap = Offers(trader, MapOfferAttrs.OreMap).FirstOrDefault();
        int strangerMap = firstMap?.TradeItem.Price ?? 0;
        string? strangerMapId = firstMap?.Itemstack.Attributes.GetString(MapOfferAttrs.Deposit);
        // Each ore map offer's precision was drawn as it was shelved (map-prices.json precisionWeights).
        var precisions = Offers(trader, MapOfferAttrs.OreMap).ToDictionary(
            o => o.Itemstack.Attributes.GetString(MapOfferAttrs.Deposit), o => o.Itemstack.Attributes.GetAsInt(MapOfferAttrs.Precision));
        Assert.All(precisions.Values, p => Assert.InRange(p, MapPrecision.Rough, MapPrecision.Fine));
        Close(trader);

        Assert.True((await World.ExecuteCommand($"/sh trade standing set glueregular {id} 900")).Ok);
        Assert.Equal("trusted", Standing.ViewFor(buyer.PlayerUID, trader).Tier.Code);
        Open(trader, buyer);
        var tree = (Vintagestory.API.Datastructures.ITreeAttribute)trader.WatchedAttributes[StandingPriceModifier.Attr];
        Assert.Equal(buyer.PlayerUID, tree.GetString("uid"));
        Assert.Equal(0.9, tree.GetDouble("buy"), 6);
        int trusted = trader.Inventory.SellingSlots.First(s => s.Itemstack?.Collectible.Code.ToString() == code).TradeItem.Price;
        output.WriteLine($"{code}: {stranger} for a stranger, {trusted} for a trusted customer");
        // 0.9 of a price under 10 may round back to it (8.4 and 7.56 are both 8 gears).
        if (stranger >= 10) Assert.True(trusted < stranger, $"{code}: {trusted} is not under {stranger}");
        else Assert.True(trusted <= stranger, $"{code}: {trusted} is over {stranger}");
        // Standing does not change an ore map's precision (buying one map once unlocked finer and
        // dearer ones, and the cheap rough map was gone): every offer shelved before is as it was.
        foreach (var o in Offers(trader, MapOfferAttrs.OreMap))
            if (precisions.TryGetValue(o.Itemstack.Attributes.GetString(MapOfferAttrs.Deposit), out int before))
                Assert.Equal(before, o.Itemstack.Attributes.GetAsInt(MapOfferAttrs.Precision));
        // The same map costs no more for a trusted customer: only the discount applies.
        // (The same deposit's: checks landing meanwhile may put others on the shelf.)
        if (strangerMap > 0)
            Assert.True(Offers(trader, MapOfferAttrs.OreMap).First(o => o.Itemstack.Attributes.GetString(MapOfferAttrs.Deposit) == strangerMapId).TradeItem.Price <= strangerMap);
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
        // The factor the shelf is priced with (Reprice's own call): the price shown is rounded to whole gears.
        double DearFactor() => Mod.Trading.Economy.Core.Pricing.Modifiers(economy.Modifiers, economy.Context(curio, dearCode, false, null));
        double dearFactorStranger = DearFactor();
        var offerStranger = economy.QuoteOffList(curio, copper);
        Close(curio);
        Assert.True((await World.ExecuteCommand($"/sh trade standing set glueregular {Standing.TraderIdOf(curio)} 900")).Ok);
        Open(curio, buyer);
        // The same slot: a shelf can hold one code at several prices (tapestries).
        Assert.Equal(dearCode, dear.Itemstack?.Collectible.Code.ToString());
        int dearTrusted = dear.TradeItem.Price;
        double dearFactorTrusted = DearFactor();
        var offerTrusted = economy.QuoteOffList(curio, copper);
        output.WriteLine($"{dearCode}: {dearStranger} -> {dearTrusted} (factor {dearFactorStranger} -> {dearFactorTrusted}); "
            + $"copper offer modifiers {offerStranger.Modifiers} -> {offerTrusted.Modifiers}");
        Assert.Equal(1, dearFactorStranger, 6);
        Assert.Equal(0.9, dearFactorTrusted, 6);
        // The stranger's price is a list price within half a gear of it; the trusted one is 0.9 of that, rounded
        // (#1001). Under 10 gears that may round back to the same (8.4 and 7.56 are both 8).
        static double Gears(double price) => Math.Round(price, MidpointRounding.AwayFromZero);
        Assert.InRange<double>(dearTrusted, Gears(0.9 * (dearStranger - 0.5)), Gears(0.9 * (dearStranger + 0.5)));
        if (dearStranger >= 10) Assert.True(dearTrusted < dearStranger, $"{dearCode}: {dearTrusted} is not under {dearStranger}");
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

    [AtlasScenario(TimeoutMs = 600_000)]
    public async Task A_lead_bought_is_not_offered_again_and_a_gravel_map_bought_after_it_arrives()
    {
        // The playtest's sequence: a lead, the same lead again, then the gravel map.
        var trader = await StoreWithGravel((-60, 60), (60, 60), (-24, 18), (90, -20), (-90, -20));
        var buyer = await Buyer("dupebuyer", trader);
        Assert.True(trader.BeginTrade(buyer));
        // Known here, so the trader has more than the stranger's one lead for them.
        Assert.True((await World.ExecuteCommand($"/sh trade standing set dupebuyer {Standing.TraderIdOf(trader)} 100")).Ok);
        var lead = Plain(LeadsAt(buyer, trader))[0];
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
        var p = await World.JoinAtSpawn("meetbuyer");
        await p.TeleportTo(store.Pos.AsBlockPos.AddCopy(1, 0, 0));
        await p.GiveItem("game:gear-rusty", 64);
        var buyer = (IServerPlayer)p.Player;
        buyer.WorldData.CurrentGameMode = EnumGameMode.Survival;
        Assert.True(store.BeginTrade(buyer));
        // A fresh player's one map here is their first (to a prospector).
        var lead = Assert.Single(LeadsAt(buyer, store).Offers);
        Assert.True(lead.Pity);
        Assert.True(BuyLead(buyer, store, lead).Ok);
        var (leadSlot, cell) = await DrawnPity(buyer);
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
