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
/// to the deposits of the registry it would sell, those not checked yet "being surveyed" until the
/// trade opens and their checks land (#693), each named by its ores, grades and host rock (#692);
/// buying one through the trade window (its server side: one unit, held) yields a real ore map and
/// marks the deposit sold, another trader no longer offers it, a general store offers a gravel map
/// exactly when a field is in reach (naming its rock and metals once checked), a player nearing a
/// prospector camp far away has its deposits checked in the background one at a time, and
/// standing changes the prices a trader quotes. Camp leads (per buyer, off the shelf, distance in
/// rings of grid cells): a fresh player at a trader that is no prospector is offered their first
/// map alone, 10 gears to a prospector, which generates the camp and marks it, and is that trader's
/// one stranger map; at the prospector's camp they buy one onward in ring 1; a prospector offers no
/// first map and a stranger's reach is ring 1; a known customer is offered two in ring 1 at the
/// formula's price, and buying one brings the next-nearest; a partner's lead to a camp nobody
/// generated settles the camp and marks it where it stands. From the playtest after the trade window: a lead bought is not offered again, carried or
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

    private async Task<IServerPlayer> Buyer(string name, EntitySeraphTrader trader, int gears = 64)
    {
        var p = await World.JoinAtSpawn(name);
        // Within vanilla's trading reach (a squared distance of 5 closes the trade).
        await p.TeleportTo(trader.Pos.AsBlockPos.AddCopy(1, 0, 0));
        await p.GiveItem("game:gear-rusty", Math.Min(64, gears));
        var sp = (IServerPlayer)p.Player;
        // More than a stack: the rest into their bags as the game gives items.
        for (int left = gears - 64; left > 0; left -= 64)
            Assert.True(sp.Entity.TryGiveItemStack(new ItemStack(W.GetItem(new AssetLocation("game:gear-rusty"))!, Math.Min(64, left))));
        await World.Ticks(2);
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

    /// <summary>A trader's ore or gravel map offers: real ones and those "being surveyed" (#693).</summary>
    private static List<ItemSlotTrade> DepositOffers(EntitySeraphTrader trader, AssetLocation code) =>
        trader.Inventory.SellingSlots.Where(s => s.Itemstack?.Collectible.Code == code
                                                 && s.Itemstack.Attributes.GetString(MapOfferAttrs.Offer) is MapOfferAttrs.OreMap or MapOfferAttrs.GravelMap or MapOfferAttrs.Surveying)
            .ToList();

    private static string Describe(ItemSlotTrade s)
    {
        var a = s.Itemstack.Attributes;
        return $"{a.GetString(MapOfferAttrs.Offer)} {a.GetString(MapOfferAttrs.Deposit)} ({a.GetString(MapOfferAttrs.Ores)}; {a.GetString(MapOfferAttrs.Grades)};"
               + $" {a.GetString(MapOfferAttrs.Rock)}; {a.GetString(MapOfferAttrs.Metals)}) stock {s.TradeItem?.Stock} @{s.TradeItem?.Price}";
    }

    /// <summary>The fallback (#693): with the trade open, waits for every deposit still "being
    /// surveyed" on the shelf to be checked and its real offer to take its place.</summary>
    private async Task SurveysLand(EntitySeraphTrader trader, string what)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        await World.Until(() => Offers(trader, MapOfferAttrs.Surveying).Count == 0, 400_000);
        output.WriteLine($"{what}: the shelf's surveys landed in {watch.Elapsed.TotalSeconds:0.0} s ({Maps.Surveys!.Checked} checks so far)");
        Assert.Empty(Offers(trader, MapOfferAttrs.Surveying));
    }

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
    public async Task Nearing_a_prospector_camp_checks_its_deposits_in_the_background()
    {
        var trading = TradingSystem.Of(Api)!;
        var surveys = Maps.Surveys!;
        Assert.Equal(1500, surveys.ApproachMetres);
        // A prospector camp far from anything generated, so none of its deposits is checked yet.
        var spawnCell = Mod.Trading.Core.TraderGrid.CellOf(World.Spawn.X, World.Spawn.Z);
        var far = new Mod.Trading.Core.CellKey(spawnCell.X + 12, spawnCell.Z + 12);
        var cell = Mod.Trading.Maps.Core.CampLeads.CellsAround(far, 2).First(c => trading.Grid!.IsProspector(c) && Maps.SiteOf(c) != null);
        var site = Maps.SiteOf(cell)!.Value;
        var picks = Maps.PickOre(Deposits, site.X, site.Z).Offers;
        var field = Maps.PickGravel(Deposits, site.X, site.Z).Offer;
        output.WriteLine($"prospector cell {cell} at {site.X},{site.Z}: picks {string.Join(", ", picks.Select(p => $"{p.Key}{(p.Surveyed ? "" : " (unchecked)")}"))}; "
                         + $"gravel {field?.Key}");
        Assert.NotEmpty(picks);
        Assert.Contains(picks, p => !p.Surveyed);
        int before = surveys.Checked;

        // A player comes within 1.2 km of it: the camp's would-be offers are queued at the next scan.
        var p = await World.JoinAtSpawn("surveyor");
        var sp = (IServerPlayer)p.Player;
        sp.WorldData.CurrentGameMode = EnumGameMode.Creative;
        sp.Entity.TeleportTo(site.X + 1200, 200, site.Z);
        await World.Ticks(5);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        await World.Until(() => picks.Any(c => surveys.Pending(c.Key) || Deposits.Candidate(c.Key)?.Surveyed != false), 5000);
        output.WriteLine($"queued after {watch.Elapsed.TotalSeconds:0.0} s: {surveys.Queue.Waiting} waiting, {surveys.Queue.Running} running");
        Assert.Contains(picks, c => surveys.Pending(c.Key) || Deposits.Candidate(c.Key)?.Surveyed != false);

        // Throttled: one background check at a time, until everything the camp would offer is checked.
        int most = 0;
        bool Done()
        {
            most = Math.Max(most, surveys.Queue.Running);
            return Maps.PickOre(Deposits, site.X, site.Z).Offers.All(c => c.Surveyed)
                   && Maps.PickGravel(Deposits, site.X, site.Z).Offer is null or { Surveyed: true };
        }
        await World.Until(Done, 2_000_000);
        var after = Maps.PickOre(Deposits, site.X, site.Z).Offers;
        output.WriteLine($"all checked after {watch.Elapsed.TotalSeconds:0.0} s, {surveys.Checked - before} checks, at most {most} running at once: "
                         + string.Join("; ", after.Select(c => $"{c.Key} {c.Record.Tier} {string.Join(" and ", c.Record.Makeup!.MainOres())}"
                                                              + $" {c.Record.Makeup.Mix()?.Code} in {c.Record.Makeup.HostRock()}")));
        Assert.True(Done(), "the camp's deposits were not all checked");
        Assert.True(most <= 1, $"{most} background checks ran at once");
        Assert.True(surveys.Checked > before);
        sp.Entity.TeleportTo(World.Spawn.X, World.Spawn.Y + 1, World.Spawn.Z);
    }

    // ---- Camp leads ----

    private MapsSystem.CampLeadView LeadsAt(IServerPlayer player, EntitySeraphTrader trader)
    {
        var view = Maps.CampLeadsFor(player, trader) ?? throw new Xunit.Sdk.XunitException("no camp leads (maps or the grid off)");
        output.WriteLine($"{player.PlayerName} at {view.TraderId} as {view.Buyer.Tier} ({view.Buyer.Bought} bought, reach {view.Reach},"
                         + $" first map {(view.PityUsed ? "had" : "not yet")}, {view.Why}): "
                         + string.Join(", ", view.Offers.Select(o => $"{(o.Pity ? "first map " : "")}{o.Cell} ring {o.Ring} {o.Type} {o.Distance:0} m {o.Price} g{(o.Prospector ? " *" : "")}")));
        return view;
    }

    private static List<CampLeadOffer> Plain(MapsSystem.CampLeadView view) => view.Offers.Where(o => !o.Pity).ToList();

    private TradeResult BuyLead(IServerPlayer player, EntitySeraphTrader trader, CampLeadOffer offer)
    {
        var result = TradeWindowSystem.Of(Api)!.Handle(player, trader,
            new TradeRequest { Action = TradeAction.BuyLead, Code = offer.Pity ? MapsSystem.PityCode : offer.Cell.ToString(), Price = offer.Price });
        output.WriteLine($"buy {(offer.Pity ? "first map" : "lead")} to {offer.Cell}: {(result.Ok ? "ok" : "refused")} {result.Key} {string.Join(",", result.Args)}");
        return result;
    }

    private static bool LeadTo(ItemStack stack, Mod.Trading.Core.CellKey cell) =>
        ItemTraderLead.IsDrawn(stack) && stack.Attributes.GetString(MapOfferAttrs.Cell) == cell.ToString();

    /// <summary>Waits for the player's first map to be drawn (to its offer's cell, or the next its
    /// chain settled); returns its slot and cell.</summary>
    private async Task<(ItemSlot Slot, Mod.Trading.Core.CellKey Cell)> DrawnPity(IServerPlayer player, EntitySeraphTrader? trader = null)
    {
        static bool Drawn(ItemStack s) => ItemTraderLead.IsDrawn(s) && !string.IsNullOrEmpty(s.Attributes.GetString(MapOfferAttrs.Cell));
        await World.Until(() => Holding(player, ItemTraderLead.LeadCode, Drawn) != null, 600_000);
        var slot = Holding(player, ItemTraderLead.LeadCode, Drawn)!;
        Assert.True(Mod.Trading.Core.CellKey.TryParse(slot.Itemstack.Attributes.GetString(MapOfferAttrs.Cell), out var cell));
        if (trader != null && trader.WatchedAttributes.GetString("tradingPlayerUID") != player.PlayerUID)
        {
            player.Entity.TeleportTo(trader.Pos.AsBlockPos.AddCopy(1, 0, 0));
            await World.Ticks(5);
            Assert.True(trader.BeginTrade(player));
        }
        return (slot, cell);
    }

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
        Assert.Equal("trading-window-lead-offer-pity", Mod.Trading.Window.Core.TradeWindowModel.LeadOfferLine(row).Key);

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
        var firstMap = Offers(trader, MapOfferAttrs.OreMap).FirstOrDefault();
        int strangerMap = firstMap?.TradeItem.Price ?? 0;
        string? strangerMapId = firstMap?.Itemstack.Attributes.GetString(MapOfferAttrs.Deposit);
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
        // A trusted customer is shown (and sold) exact ore maps.
        foreach (var o in Offers(trader, MapOfferAttrs.OreMap))
            Assert.Equal(MapPrecision.Exact, o.Itemstack.Attributes.GetAsInt(MapOfferAttrs.Precision));
        // An exact map costs more than a rough one, standing's discount notwithstanding.
        // (The same deposit's: checks landing meanwhile may put others on the shelf.)
        if (strangerMap > 0)
            Assert.True(Offers(trader, MapOfferAttrs.OreMap).First(o => o.Itemstack.Attributes.GetString(MapOfferAttrs.Deposit) == strangerMapId).TradeItem.Price > strangerMap);
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
