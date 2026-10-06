using Atlas.XUnit;
using SeraphHorizons.Mod.Ore;
using SeraphHorizons.Mod.Ore.Core;
using SeraphHorizons.Mod.Trading;
using SeraphHorizons.Mod.Trading.Glue;
using SeraphHorizons.Mod.Trading.Maps;
using SeraphHorizons.Mod.Trading.Standing;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Xunit.Abstractions;

namespace SeraphHorizons.PackTests;

/// <summary>
/// mods-src/seraphhorizons/Trading/Maps (#455) and the wave-2 glue, in a new standard world with a
/// fixed seed (ore cells, placer fields and the camp grid on): a prospector's shelf offers ore maps
/// from the deposit registry (which lists deposits whose chunks nobody generated), buying one through
/// the trader's own trade packet yields a real ore map and marks the deposit sold, another trader
/// no longer offers it, a general store offers a gravel map exactly when a field is in reach, a lead
/// marks a camp, and standing changes the prices a trader quotes.
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
        await p.TeleportTo(trader.Pos.AsBlockPos.AddCopy(2, 0, 0));
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

    /// <summary>Buys one lot of a selling slot through the trade packet (the dialog's button).</summary>
    private void Buy(EntitySeraphTrader trader, IServerPlayer player, ItemSlotTrade selling)
    {
        var inv = trader.Inventory;
        var cart = inv.GetBuyingCartSlot(0);
        cart.Itemstack = selling.TradeItem.Stack.Clone();
        cart.Itemstack.ResolveBlockOrItem(W);
        cart.TradeItem = selling.TradeItem;
        trader.OnReceivedClientPacket(player, 1000, []);
    }

    private static ItemSlot? Holding(IServerPlayer player, AssetLocation code, System.Func<ItemStack, bool> match)
    {
        foreach (var inv in player.InventoryManager.InventoriesOrdered)
            if (inv.ClassName != GlobalConstants.creativeInvClassName)
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

    [AtlasScenario(TimeoutMs = 600_000)]
    public async Task A_lead_to_the_nearest_camp_places_a_waypoint()
    {
        var trader = await SpawnTrader("generalstore", 40, -36);
        var leads = Offers(trader, MapOfferAttrs.Lead);
        output.WriteLine("leads: " + string.Join(", ", leads.Select(l =>
            $"{l.Itemstack.Attributes.GetString(MapOfferAttrs.LeadKind)} {l.Itemstack.Attributes.GetString(MapOfferAttrs.Cell)} @{l.TradeItem.Price}")));
        // A stranger's shelf (nobody traded here): the nearest camp only.
        var lead = Assert.Single(leads);
        Assert.Equal("camp", lead.Itemstack.Attributes.GetString(MapOfferAttrs.LeadKind));

        var buyer = await Buyer("leadbuyer", trader);
        Open(trader, buyer);
        Buy(trader, buyer, lead);
        await World.Until(() => Holding(buyer, ItemTraderLead.LeadCode, ItemTraderLead.IsDrawn) != null, 300_000);
        var slot = Holding(buyer, ItemTraderLead.LeadCode, ItemTraderLead.IsDrawn)!;
        var a = slot.Itemstack.Attributes;
        Assert.True(Mod.Trading.Core.CellKey.TryParse(a.GetString(MapOfferAttrs.Cell), out var cell));
        var camp = TradingSystem.Of(Api)!.Camps!.Registry.Get(cell);
        output.WriteLine($"lead to {cell}: {slot.Itemstack.GetName()} at {a.GetInt(MapOfferAttrs.X)},{a.GetInt(MapOfferAttrs.Z)}; camp {camp?.Status}");
        Assert.Equal(Mod.Trading.Core.CampStatus.Placed, camp!.Status);
        Assert.Equal(camp.X, a.GetInt(MapOfferAttrs.X));
        Assert.Equal(camp.Type, a.GetString(MapOfferAttrs.Type));

        var layer = Api.ModLoader.GetModSystem<WorldMapManager>().MapLayers.OfType<WaypointMapLayer>().Single();
        int before = layer.Waypoints.Count(w => w.OwningPlayerUid == buyer.PlayerUID);
        EnumHandHandling handling = EnumHandHandling.NotHandled;
        slot.Itemstack.Collectible.OnHeldInteractStart(slot, buyer.Entity, null, null, true, ref handling);
        var mine = layer.Waypoints.Where(w => w.OwningPlayerUid == buyer.PlayerUID).ToList();
        Assert.Equal(before + 1, mine.Count);
        Assert.Equal("trader", mine.Last().Icon);
        Assert.Equal(camp.X + 0.5, mine.Last().Position.X);
        output.WriteLine($"waypoint '{mine.Last().Title}'");
        Assert.False(slot.Empty);
        Close(trader);
        trader.Die(EnumDespawnReason.Removed);
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
}
