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
/// Trader maps (#455, <see cref="TradingMapsScenarioBase"/>): a prospector's shelf offers ore maps to
/// the deposits of the registry it would sell, those not checked yet "being surveyed" until the trade
/// opens and their checks land (#693), each named by its ores, grades and host rock (#692); buying one
/// through the trade window (its server side: one unit, held) yields a real ore map and marks the
/// deposit sold, and another trader no longer offers it. A class of its own: the shelf's checks
/// generate a deposit's columns each.
/// </summary>
[AtlasWorld(Seed = Seed, WorldType = "standard")]
public class TradingMapsShelfScenarios(ITestOutputHelper output) : TradingMapsScenarioBase(output)
{
    [AtlasScenario(TimeoutMs = 900_000)]
    public async Task A_prospector_sells_an_ore_map_once_naming_the_ore()
    {
        var trader = await SpawnTrader("prospector", 24, 24);
        int x = (int)trader.Pos.X, z = (int)trader.Pos.Z;
        var candidates = Deposits.Candidates(x, z, Maps.Prices.OreRadius);
        Assert.NotEmpty(candidates);
        // One offer per metal, the nearest unsold: a map once its deposit is checked, else "being
        // surveyed" (#693), never a map to a deposit nobody has checked.
        var picks = Maps.PickOre(Deposits, x, z).Offers;
        var shelf = DepositOffers(trader, MapIssuer.OreMapCode);
        output.WriteLine($"candidates {candidates.Count}; picks {string.Join(", ", picks.Select(p => $"{p.Key}{(p.Surveyed ? "" : " (unchecked)")}"))}; "
                         + "shelf " + string.Join(", ", shelf.Select(Describe)));
        Assert.Equal(picks.Select(p => p.Key.Id).Order(), shelf.Select(s => s.Itemstack.Attributes.GetString(MapOfferAttrs.Deposit)).Order());
        foreach (var s in shelf)
        {
            Assert.True(DepositKey.TryParse(s.Itemstack.Attributes.GetString(MapOfferAttrs.Deposit), out var key));
            bool surveyed = Deposits.Candidate(key)!.Surveyed;
            Assert.Equal(surveyed ? MapOfferAttrs.OreMap : MapOfferAttrs.Surveying, s.Itemstack.Attributes.GetString(MapOfferAttrs.Offer));
            if (!surveyed) Assert.Equal(0, s.TradeItem.Stock);
        }
        Assert.Empty(Offers(trader, MapOfferAttrs.SoldOut));

        var buyer = await Buyer("mapbuyer", trader);
        // A deposit being surveyed is refused before payment.
        if (Offers(trader, MapOfferAttrs.Surveying).FirstOrDefault() is { } surveying)
            Assert.Equal("trading-maps-error-surveying", Maps.Refusal(trader, buyer, surveying.Itemstack, surveying)?.Key);
        // The trade opens: anything still unchecked is checked now, and its offer takes its place.
        Open(trader, buyer);
        trader.OnTradeOpened(buyer);
        await SurveysLand(trader, "prospector");
        var offers = Offers(trader, MapOfferAttrs.OreMap);
        output.WriteLine("after the checks: " + string.Join(", ", DepositOffers(trader, MapIssuer.OreMapCode).Select(Describe)));
        Assert.NotEmpty(offers);
        foreach (var o in offers)
        {
            var a = o.Itemstack.Attributes;
            Assert.True(DepositKey.TryParse(a.GetString(MapOfferAttrs.Deposit), out var key));
            var record = Deposits.Registry.Get(key);
            Assert.True(record.Surveyed, $"{key} is offered unchecked");
            // Named by what the ore is (#692), never the metal.
            var ores = OreNames.Split(a.GetString(MapOfferAttrs.Ores));
            Assert.NotEmpty(ores);
            Assert.All(ores, ore => Assert.Equal(key.Kind, OreMetals.MetalOf(ore)));
            Assert.Equal(record.Makeup!.MainOres(), ores);
            Assert.Equal(record.Makeup.HostRock(), a.GetString(MapOfferAttrs.Rock));
            Assert.Equal(record.Makeup.Mix()?.Code, a.GetString(MapOfferAttrs.Grades));
            Assert.Equal(record.Tier!.Value.ToString().ToLowerInvariant(), a.GetString(MapOfferAttrs.SizeTier));
            string name = o.Itemstack.GetName();
            output.WriteLine($"{key}: '{name}'");
            Assert.Contains(Lang.Get("seraphhorizons:" + OreNames.LangKey(ores[0])), name, StringComparison.OrdinalIgnoreCase);
            Assert.True(o.TradeItem.Stock > 0);
        }

        var offer = offers.OrderBy(o => o.TradeItem.Price).First();
        Assert.True(DepositKey.TryParse(offer.Itemstack.Attributes.GetString(MapOfferAttrs.Deposit), out var bought));
        string offeredOres = offer.Itemstack.Attributes.GetString(MapOfferAttrs.Ores);
        int price = offer.TradeItem.Price;
        // Drawn when shelved (map-prices.json precisionWeights): the map sold is the offer's precision.
        int precision = offer.Itemstack.Attributes.GetAsInt(MapOfferAttrs.Precision);
        Assert.InRange(precision, MapPrecision.Rough, MapPrecision.Fine);
        await BackAt(trader, buyer);
        int gearsBefore = InventoryTrader.GetPlayerAssets(buyer.Entity);
        Buy(trader, buyer, offer);
        Assert.Null(trader.Inventory.GetBuyingCartSlot(0).Itemstack);
        Assert.Equal(gearsBefore - price, InventoryTrader.GetPlayerAssets(buyer.Entity));

        // The sale checks the deposit again: the map follows.
        await World.Until(() => Holding(buyer, MapIssuer.OreMapCode, s => s.Attributes.HasAttribute(ItemOreMap.AttrX)) != null, 300_000);
        var map = Holding(buyer, MapIssuer.OreMapCode, s => s.Attributes.HasAttribute(ItemOreMap.AttrX))!.Itemstack;
        output.WriteLine($"bought {bought} for {price}: {map.GetName()}");
        Assert.Equal(bought.Id, map.Attributes.GetString(ItemOreMap.AttrDeposit));
        Assert.Null(map.Attributes.GetString(MapOfferAttrs.Pending));
        Assert.Null(map.Attributes.GetString(MapOfferAttrs.Offer));
        Assert.NotNull(map.Attributes.GetString(ItemOreMap.AttrSizeTier));
        Assert.Equal(offeredOres, map.Attributes.GetString(ItemOreMap.AttrOres));
        Assert.Equal(precision, map.Attributes.GetInt(ItemOreMap.AttrPrecision));
        Assert.NotNull(map.Attributes.GetString(ItemOreMap.AttrRock));
        var record2 = Deposits.Registry.Get(bought);
        Assert.Equal(DepositState.Sold, record2.State);
        Assert.Equal(buyer.PlayerUID, record2.SoldToUid);
        Assert.Null(Holding(buyer, MapIssuer.OreMapCode, s => s.Attributes.HasAttribute(MapOfferAttrs.Pending)));
        Assert.Empty(Maps.Reserved);

        // No other trader offers it.
        var other = await SpawnTrader("prospector", -30, 30);
        var otherOffers = DepositOffers(other, MapIssuer.OreMapCode).Select(o => o.Itemstack.Attributes.GetString(MapOfferAttrs.Deposit)).ToList();
        output.WriteLine("the other prospector offers " + string.Join(", ", otherOffers));
        Assert.DoesNotContain(bought.Id, otherOffers);
        // Nor does this one at its next restock.
        trader.Restock(1.1f);
        Assert.DoesNotContain(bought.Id, DepositOffers(trader, MapIssuer.OreMapCode).Select(o => o.Itemstack.Attributes.GetString(MapOfferAttrs.Deposit)));
        Close(trader);
        trader.Die(EnumDespawnReason.Removed);
        other.Die(EnumDespawnReason.Removed);
    }
}
