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
/// fixed seed (ore cells, placer fields and the camp grid on): what the trader map scenarios share.
/// Checking a deposit generates its nine columns and the 11×11 around them, so the scenarios that
/// wait on checks are classes of their own, each its own world of this seed (and CI shard):
/// <see cref="TradingMapsShelfScenarios"/> (a prospector's shelf and its surveys),
/// <see cref="TradingMapsApproachScenarios"/> (a player nearing a far camp) and
/// <see cref="TradingMapsScenarios"/> (gravel maps, standing and camp leads).
/// </summary>
public abstract class TradingMapsScenarioBase(ITestOutputHelper testOutput) : AtlasScenarioBase
{
    public const int Seed = 515151;

    protected readonly ITestOutputHelper output = testOutput;

    protected ICoreServerAPI Api => World.Api;
    protected IWorldAccessor W => World.Api.World;
    protected OreSystem Ore => Api.ModLoader.GetModSystem<OreSystem>();
    protected DepositService Deposits => Ore.Deposits ?? throw new Xunit.Sdk.XunitException("the deposit service is not bound");
    protected MapsSystem Maps => Api.ModLoader.GetModSystem<MapsSystem>();
    protected StandingSystem Standing => StandingSystem.Of(Api) ?? throw new Xunit.Sdk.XunitException("no StandingSystem");

    protected async Task<EntitySeraphTrader> SpawnTrader(string type, int dx, int dz)
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

    protected async Task<IServerPlayer> Buyer(string name, EntitySeraphTrader trader, int gears = 64)
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
    protected void Open(EntitySeraphTrader trader, IServerPlayer player)
    {
        trader.WatchedAttributes.SetString(StandingPriceModifier.TradingPlayerAttr, player.PlayerUID);
        Api.ModLoader.GetModSystem<TradingGlueSystem>().Sync(trader);
    }

    protected void Close(EntitySeraphTrader trader)
    {
        trader.WatchedAttributes.RemoveAttribute(StandingPriceModifier.TradingPlayerAttr);
        Api.ModLoader.GetModSystem<TradingGlueSystem>().Sync(trader);
    }

    protected static List<ItemSlotTrade> Offers(EntitySeraphTrader trader, string offer) =>
        trader.Inventory.SellingSlots.Where(s => s.Itemstack?.Attributes.GetString(MapOfferAttrs.Offer) == offer).ToList();

    /// <summary>
    /// After a long wait with the trade open (checks landing, chunks generating), the player steps
    /// back up to the trader before asking anything of it: the trader's AI keeps wandering meanwhile,
    /// and the window's server check turns a request from past vanilla's reach away
    /// (<c>trading-window-toofar</c>), as on CI where surveys took minutes. If the trade dropped
    /// meanwhile (vanilla ends it when the two part), the player is its trading player again.
    /// </summary>
    protected async Task BackAt(EntitySeraphTrader trader, IServerPlayer player)
    {
        player.Entity.TeleportTo(trader.Pos.AsBlockPos.AddCopy(1, 0, 0));
        await World.Ticks(5);
        if (trader.WatchedAttributes.GetString("tradingPlayerUID") != player.PlayerUID)
            Assert.True(trader.BeginTrade(player));
        double d2 = player.Entity.Pos.SquareDistanceTo(trader.Pos);
        Assert.True(d2 <= TradeGuard.MaxDistanceSq, $"{player.PlayerName} is {Math.Sqrt(d2):0.0} blocks from the trader");
    }

    /// <summary>Buys one lot of a selling slot through the trade window: its Buy request, as a
    /// completed hold sends it.</summary>
    protected void Buy(EntitySeraphTrader trader, IServerPlayer player, ItemSlotTrade selling)
    {
        int slot = Array.IndexOf(trader.Inventory.SellingSlots, selling);
        Assert.True(slot >= 0);
        var result = TradeWindowSystem.Of(Api)!.Handle(player, trader, new TradeRequest { Action = TradeAction.Buy, Slot = slot });
        Assert.True(result.Ok, $"{result.Key} {string.Join(",", result.Args)}");
    }

    protected static ItemSlot? Holding(IServerPlayer player, AssetLocation code, System.Func<ItemStack, bool> match)
    {
        foreach (var inv in player.InventoryManager.InventoriesOrdered)
            if (inv is InventoryBasePlayer && inv.ClassName != GlobalConstants.creativeInvClassName)
                foreach (var slot in inv)
                    if (slot.Itemstack?.Collectible?.Code == code && match(slot.Itemstack))
                        return slot;
        return null;
    }

    /// <summary>A trader's ore or gravel map offers: real ones and those "being surveyed" (#693).</summary>
    protected static List<ItemSlotTrade> DepositOffers(EntitySeraphTrader trader, AssetLocation code) =>
        trader.Inventory.SellingSlots.Where(s => s.Itemstack?.Collectible.Code == code
                                                 && s.Itemstack.Attributes.GetString(MapOfferAttrs.Offer) is MapOfferAttrs.OreMap or MapOfferAttrs.GravelMap or MapOfferAttrs.Surveying)
            .ToList();

    protected static string Describe(ItemSlotTrade s)
    {
        var a = s.Itemstack.Attributes;
        return $"{a.GetString(MapOfferAttrs.Offer)} {a.GetString(MapOfferAttrs.Deposit)} ({a.GetString(MapOfferAttrs.Ores)}; {a.GetString(MapOfferAttrs.Grades)};"
               + $" {a.GetString(MapOfferAttrs.Rock)}; {a.GetString(MapOfferAttrs.Metals)}) stock {s.TradeItem?.Stock} @{s.TradeItem?.Price}";
    }

    /// <summary>The fallback (#693): with the trade open, waits for every deposit still "being
    /// surveyed" on the shelf to be checked and its real offer to take its place.</summary>
    protected async Task SurveysLand(EntitySeraphTrader trader, string what)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        await World.Until(() => Offers(trader, MapOfferAttrs.Surveying).Count == 0, 400_000);
        output.WriteLine($"{what}: the shelf's surveys landed in {watch.Elapsed.TotalSeconds:0.0} s ({Maps.Surveys!.Checked} checks so far)");
        Assert.Empty(Offers(trader, MapOfferAttrs.Surveying));
    }

    // ---- Camp leads ----

    protected MapsSystem.CampLeadView LeadsAt(IServerPlayer player, EntitySeraphTrader trader)
    {
        var view = Maps.CampLeadsFor(player, trader) ?? throw new Xunit.Sdk.XunitException("no camp leads (maps or the grid off)");
        output.WriteLine($"{player.PlayerName} at {view.TraderId} as {view.Buyer.Tier} ({view.Buyer.Bought} bought, reach {view.Reach},"
                         + $" first map {(view.PityUsed ? "had" : "not yet")}, {view.Why}): "
                         + string.Join(", ", view.Offers.Select(o => $"{(o.Pity ? "first map " : "")}{o.Cell} ring {o.Ring} {o.Type} {o.Distance:0} m {o.Price} g{(o.Prospector ? " *" : "")}")));
        return view;
    }

    protected static List<CampLeadOffer> Plain(MapsSystem.CampLeadView view) => view.Offers.Where(o => !o.Pity).ToList();

    protected TradeResult BuyLead(IServerPlayer player, EntitySeraphTrader trader, CampLeadOffer offer)
    {
        var result = TradeWindowSystem.Of(Api)!.Handle(player, trader,
            new TradeRequest { Action = TradeAction.BuyLead, Code = offer.Pity ? MapsSystem.PityCode : offer.Cell.ToString(), Price = offer.Price });
        output.WriteLine($"buy {(offer.Pity ? "first map" : "lead")} to {offer.Cell}: {(result.Ok ? "ok" : "refused")} {result.Key} {string.Join(",", result.Args)}");
        return result;
    }

    protected static bool LeadTo(ItemStack stack, Mod.Trading.Core.CellKey cell) =>
        ItemTraderLead.IsDrawn(stack) && stack.Attributes.GetString(MapOfferAttrs.Cell) == cell.ToString();

    /// <summary>Waits for the player's first map to be drawn (to its offer's cell, or the next its
    /// chain settled); returns its slot and cell.</summary>
    protected async Task<(ItemSlot Slot, Mod.Trading.Core.CellKey Cell)> DrawnPity(IServerPlayer player, EntitySeraphTrader? trader = null)
    {
        static bool Drawn(ItemStack s) => ItemTraderLead.IsDrawn(s) && !string.IsNullOrEmpty(s.Attributes.GetString(MapOfferAttrs.Cell));
        await World.Until(() => Holding(player, ItemTraderLead.LeadCode, Drawn) != null, 600_000);
        var slot = Holding(player, ItemTraderLead.LeadCode, Drawn)!;
        Assert.True(Mod.Trading.Core.CellKey.TryParse(slot.Itemstack.Attributes.GetString(MapOfferAttrs.Cell), out var cell));
        if (trader != null) await BackAt(trader, player);
        return (slot, cell);
    }

    /// <summary>Waits for the lead to the cell to be drawn; with <paramref name="trader"/>, the player
    /// is its trading player again after (vanilla ends a trade when the player strays while chunks
    /// generate, as on CI).</summary>
    protected async Task<ItemSlot> DrawnLead(IServerPlayer player, Mod.Trading.Core.CellKey cell, EntitySeraphTrader? trader = null)
    {
        await World.Until(() => Holding(player, ItemTraderLead.LeadCode, s => LeadTo(s, cell)) != null, 300_000);
        if (trader != null) await BackAt(trader, player);
        return Holding(player, ItemTraderLead.LeadCode, s => LeadTo(s, cell))!;
    }

    protected Mod.Trading.Core.CampRecord? Camp(Mod.Trading.Core.CellKey cell) => TradingSystem.Of(Api)!.Camps!.Registry.Get(cell);

    /// <summary>The camp's trader, spawned at its camp, the player next to it.</summary>
    protected async Task<EntitySeraphTrader> TraderAt(Mod.Trading.Core.CampRecord camp, Atlas.Api.ITestPlayer p)
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

    // ---- The playtest after the trade window: maps the player has, a claim gone, meeting a camp ----

    protected TradeWindowSystem WindowSystem => TradeWindowSystem.Of(Api)!;

    protected TradeResult TryBuy(EntitySeraphTrader trader, IServerPlayer player, ItemSlotTrade selling)
    {
        int slot = Array.IndexOf(trader.Inventory.SellingSlots, selling);
        Assert.True(slot >= 0);
        var result = WindowSystem.Handle(player, trader, new TradeRequest { Action = TradeAction.Buy, Slot = slot });
        output.WriteLine($"buy {selling.Itemstack?.GetName()}: {(result.Ok ? "ok" : "refused")} {result.Key}");
        return result;
    }

    protected static int SlotOf(EntitySeraphTrader trader, ItemSlotTrade selling) => Array.IndexOf(trader.Inventory.SellingSlots, selling);

    /// <summary>A general store with a gravel map on its shelf, at the first of these offsets that has one.</summary>
    protected async Task<EntitySeraphTrader> StoreWithGravel(params (int Dx, int Dz)[] spots)
    {
        foreach (var (dx, dz) in spots)
        {
            var t = await SpawnTrader("generalstore", dx, dz);
            if (Offers(t, MapOfferAttrs.GravelMap).Count == 1) return t;
            t.Die(EnumDespawnReason.Removed);
        }
        throw new Xunit.Sdk.XunitException("no general store with a gravel map at " + string.Join(" ", spots));
    }

    protected static List<Waypoint> Waypoints(ICoreServerAPI api, IServerPlayer player) =>
        api.ModLoader.GetModSystem<WorldMapManager>().MapLayers.OfType<WaypointMapLayer>().Single().Waypoints
            .Where(w => w.OwningPlayerUid == player.PlayerUID).ToList();

    protected static void Read(ItemSlot slot, IServerPlayer player)
    {
        EnumHandHandling handling = EnumHandHandling.NotHandled;
        slot.Itemstack.Collectible.OnHeldInteractStart(slot, player.Entity, null, null, true, ref handling);
    }

}
