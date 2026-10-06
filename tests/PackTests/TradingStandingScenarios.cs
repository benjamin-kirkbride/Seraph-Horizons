using Atlas.Api;
using Atlas.XUnit;
using HarmonyLib;
using SeraphHorizons.Mod.Trading;
using SeraphHorizons.Mod.Trading.Standing;
using SeraphHorizons.Mod.Trading.Standing.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Xunit.Abstractions;

namespace SeraphHorizons.PackTests;

/// <summary>
/// mods-src/seraphhorizons/Trading/Standing: standing per trader (#452) and companies (#463)
/// against the pinned mods. Deals go through the trader's own trade packet (1000, the dialog's
/// button) with carts filled as the game's dialog fills them; groups are made through the server's
/// group manager and its player data, and left with <c>/group leave</c>. Atlas' default world: no
/// camps, so traders are known by their entity.
/// </summary>
[TestCaseOrderer(BootLogFirst.Name, BootLogFirst.Assembly)]
public class TradingStandingScenarios(ITestOutputHelper output) : AtlasScenarioBase
{
    private ICoreServerAPI Api => World.Api;
    private IWorldAccessor W => World.Api.World;
    private StandingSystem Standing => StandingSystem.Of(Api) ?? throw new Xunit.Sdk.XunitException("no StandingSystem");

    [AtlasScenario]
    [ReadsBootLog]
    public void Standing_is_on_with_the_group_hooks_and_the_boot_logs_nothing_about_it()
    {
        var logged = World.BootDiagnostics
            .Where(e => e.Level is EnumLogType.Warning or EnumLogType.Error or EnumLogType.Fatal)
            .Where(e => e.Message.Contains("standing", StringComparison.OrdinalIgnoreCase))
            .Select(e => $"[{e.Level}] {e.Message}")
            .ToList();
        Assert.True(logged.Count == 0, "Logged:\n" + string.Join("\n", logged));
        Assert.True(Standing.Enabled);
        Assert.True(Standing.GroupHooksBound);
        Assert.Same(Standing, TradingSystem.Of(Api)!.Standing);
        Assert.Equal(["stranger", "known", "regular", "trusted", "partner"], Standing.Rules.Tiers.Select(t => t.Code));
    }

    private async Task<EntitySeraphTrader> SpawnTrader(int dx, int dz, string type = "generalstore")
    {
        var pos = World.Spawn.AddCopy(dx, 0, dz);
        pos.Y = W.BlockAccessor.GetTerrainMapheightAt(pos) + 1;
        var props = W.GetEntityType(new AssetLocation("seraphhorizons", $"trader-male-{type}-temperate"))!;
        var entity = (EntitySeraphTrader)W.ClassRegistry.CreateEntity(props);
        entity.Pos.SetPos(pos.X + 0.5, pos.Y, pos.Z + 0.5);
        W.SpawnEntity(entity);
        await World.Ticks(5);
        return entity;
    }

    private async Task<ITestPlayer> PlayerAt(string name, EntitySeraphTrader trader)
    {
        var player = await World.JoinPlayer(name);
        await player.TeleportTo(trader.Pos.AsBlockPos.AddCopy(2, 0, 0));
        return player;
    }

    /// <summary>As the dialog's AddToBuyingCart: one lot of a selling slot into the buying cart.</summary>
    private void AddToBuyingCart(InventoryTrader inv, ItemSlotTrade selling, int cart)
    {
        var slot = inv.GetBuyingCartSlot(cart);
        slot.Itemstack = selling.TradeItem.Stack.Clone();
        slot.Itemstack.ResolveBlockOrItem(W);
        slot.TradeItem = selling.TradeItem;
    }

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task A_deal_through_the_trader_raises_standing_by_its_gear_value()
    {
        var trader = await SpawnTrader(20, -20);
        var p = await PlayerAt("standingbuyer", trader);
        var sp = (IServerPlayer)p.Player;
        await p.GiveItem("game:gear-rusty", 32);
        var inv = trader.Inventory;

        // Buy the cheapest thing on the shelf, and sell back something the trader buys.
        var selling = inv.SellingSlots.Where(s => s.TradeItem is { Stock: > 0 }).OrderBy(s => s.TradeItem.Price).First();
        AddToBuyingCart(inv, selling, 0);
        var buying = inv.BuyingSlots.Where(s => s.TradeItem is { Stock: > 0 }).OrderBy(s => s.TradeItem.Price).First();
        var offered = buying.TradeItem.Stack.Clone();
        offered.ResolveBlockOrItem(W);
        inv.GetSellingCartSlot(0).Itemstack = offered;
        int paid = inv.GetTotalCost(), received = inv.GetTotalGain();
        output.WriteLine($"buys {selling.TradeItem.Stack.Collectible.Code} for {paid}, sells {offered.Collectible.Code} for {received}");
        Assert.True(paid > 0 && received > 0);

        string id = Standing.TraderIdOf(trader);
        Assert.Equal(TraderIds.Entity(trader.EntityId), id);
        Assert.Null(Standing.Ledger.Personal(sp.PlayerUID, id));
        trader.OnReceivedClientPacket(sp, 1000, []);
        Assert.Null(inv.GetBuyingCartSlot(0).Itemstack);
        var record = Standing.Ledger.Personal(sp.PlayerUID, id);
        Assert.NotNull(record);
        Assert.Equal(paid + received, record!.Points);
        Assert.Equal(StandingKinds.Deal, record.Events.Single().Kind);

        // A deal that fails (the trader is out of what is in the cart) scores nothing.
        selling.TradeItem.Stock = 0;
        AddToBuyingCart(inv, selling, 0);
        trader.OnReceivedClientPacket(sp, 1000, []);
        Assert.NotNull(inv.GetBuyingCartSlot(0).Itemstack);
        Assert.Equal(paid + received, Standing.Ledger.Personal(sp.PlayerUID, id)!.Points);
        inv.GetBuyingCartSlot(0).Itemstack = null;

        // The admin view lists it, by name and by id.
        var view = await World.ExecuteCommand($"/sh trade standing standingbuyer {id}");
        output.WriteLine(view.Message);
        Assert.True(view.Ok, view.Message);
        Assert.Contains(id, view.Message);
        Assert.Contains("stranger", view.Message);
        trader.Die(EnumDespawnReason.Removed);
    }

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task A_higher_tier_raises_the_wallet_at_the_next_restock()
    {
        var trader = await SpawnTrader(-20, 20);
        var p = await PlayerAt("standingwallet", trader);
        var def = TradingSystem.Of(Api)!.Lists!.For("generalstore")!;
        Assert.Equal(def.WalletFor(0).Avg, trader.TradeProps.Money.avg);
        string id = Standing.TraderIdOf(trader);
        Assert.Equal(0, Standing.WalletTierFor(trader));

        var set = await World.ExecuteCommand($"/sh trade standing set standingwallet {id} 300");
        output.WriteLine(set.Message);
        Assert.True(set.Ok, set.Message);
        Assert.Equal("regular", Standing.ViewFor(p.Player.PlayerUID, trader).Tier.Code);
        Assert.Equal(1, Standing.WalletTierFor(trader));

        // The weekly restock is due: the wallet vanilla tops up towards is the regular tier's.
        double due = W.Calendar.TotalDays - 8;
        trader.WatchedAttributes.SetDouble("lastRefreshTotalDays", due);
        await World.Until(() => trader.WatchedAttributes.GetDouble("lastRefreshTotalDays") > due, 60_000);
        Assert.Equal(def.WalletFor(1).Avg, trader.TradeProps.Money.avg);
        trader.Die(EnumDespawnReason.Removed);
    }

    private PlayerGroup MakeGroup(string name, string owner)
    {
        var group = new PlayerGroup { Name = name, OwnerUID = owner };
        Api.Groups.AddPlayerGroup(group);
        return group;
    }

    /// <summary>The server's own ServerPlayerData.JoinGroup, as every /group path calls it.</summary>
    private void Join(string uid, PlayerGroup group, EnumPlayerGroupMemberShip level)
    {
        var data = Api.PlayerData.GetPlayerDataByUid(uid);
        AccessTools.Method(data.GetType(), "JoinGroup", [typeof(PlayerGroup), typeof(EnumPlayerGroupMemberShip)])!
            .Invoke(data, [group, level]);
    }

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task A_company_pools_standing_by_max_and_a_leaver_keeps_only_their_own()
    {
        var trader = await SpawnTrader(30, 30, "smith");
        var vet = await PlayerAt("standingvet", trader);
        var newbie = await PlayerAt("standingnew", trader);
        string vetUid = vet.Player.PlayerUID, newUid = newbie.Player.PlayerUID;
        string id = Standing.TraderIdOf(trader);
        Assert.True((await World.ExecuteCommand($"/sh trade standing set standingvet {id} 900")).Ok);
        Assert.True((await World.ExecuteCommand($"/sh trade standing set standingnew {id} 40")).Ok);
        Assert.Equal("stranger", Standing.ViewFor(newUid, trader).Tier.Code);

        // The veteran forms a company; the newcomer joins and picks it with /sh company.
        var acme = MakeGroup("acmestanding", vetUid);
        Join(vetUid, acme, EnumPlayerGroupMemberShip.Owner);
        Assert.Equal(900, Standing.Ledger.Companies.Get(acme.Uid, id)?.Points);
        var other = MakeGroup("chatstanding", newUid);
        Join(newUid, other, EnumPlayerGroupMemberShip.Owner);
        Join(newUid, acme, EnumPlayerGroupMemberShip.Member);
        // Its first group is the newcomer's company until it chooses.
        Assert.Equal(other.Uid, Standing.CompanyOf(newUid));
        var pick = await newbie.ExecuteCommand("/sh company acmestanding");
        output.WriteLine(pick.Message);
        Assert.True(pick.Ok, pick.Message);
        Assert.Equal(acme.Uid, Standing.CompanyOf(newUid));
        var view = Standing.ViewFor(newUid, trader);
        Assert.Equal(900, view.Own);
        Assert.Equal("trusted", view.Tier.Code);
        Assert.Equal(40, view.Personal);

        // Trading credits both.
        Standing.OnDeal(newbie.Player, trader, 10, 0);
        Assert.Equal(50, Standing.Ledger.Personal(newUid, id)!.Points);
        Assert.Equal(910, Standing.Ledger.Companies.Get(acme.Uid, id)!.Points);

        var admin = await World.ExecuteCommand("/sh trade company standingnew");
        output.WriteLine(admin.Message);
        Assert.True(admin.Ok, admin.Message);
        Assert.Contains("acmestanding", admin.Message);
        Assert.Contains("standingvet", admin.Message);

        // Leaving: back to the newcomer's own 50; the company keeps its 910.
        var leave = await newbie.ExecuteCommand("/group leave acmestanding");
        output.WriteLine(leave.Message);
        Assert.True(leave.Ok, leave.Message);
        Assert.DoesNotContain(newUid, Standing.Ledger.Companies.Record(acme.Uid)!.Merged);
        Assert.Equal(other.Uid, Standing.CompanyOf(newUid));
        view = Standing.ViewFor(newUid, trader);
        Assert.Equal(50, view.Own);
        Assert.Equal("stranger", view.Tier.Code);
        Assert.Equal(910, Standing.ViewFor(vetUid, trader).Own);

        // Disbanding forgets the company.
        Api.Groups.RemovePlayerGroup(acme);
        Assert.Null(Standing.Ledger.Companies.Record(acme.Uid));
        trader.Die(EnumDespawnReason.Removed);
    }

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Opening_the_trade_dialog_shows_standing_once_a_visit()
    {
        var trader = await SpawnTrader(-30, -30);
        var p = await PlayerAt("standingvisit", trader);
        var line = StandingText.Line(trader, Standing.ViewFor(p.Player.PlayerUID, trader));
        output.WriteLine(line);
        Assert.Contains("general store", line);
        Assert.Contains("stranger", line);
        Assert.Contains("known at 60", line);
    }
}
