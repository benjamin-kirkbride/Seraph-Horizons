using System.Text.Json.Nodes;
using Atlas.XUnit;
using SeraphHorizons.Mod.Admin;
using SeraphHorizons.Mod.Core;
using SeraphHorizons.Mod.Ore;
using SeraphHorizons.Mod.Ore.Core;
using SeraphHorizons.Mod.Trading;
using SeraphHorizons.Mod.Trading.Economy;
using SeraphHorizons.Mod.Trading.Standing;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Xunit.Abstractions;

namespace SeraphHorizons.PackTests;

/// <summary>
/// mods-src/seraphhorizons/Trading/Admin, the trader admin tools (#459), on a new standard world
/// (the camp grid and the deposit registry on): every <c>/sh trade</c> subcommand answers and its
/// <c>--json</c> answer parses, the money and stock commands change the trader, an export of
/// supply, standing and the registry imports back, players without <c>controlserver</c> are
/// refused while <c>/sh company</c> stays theirs, and the trade overlay reaches the admin.
/// </summary>
[AtlasWorld(Seed = Seed, WorldType = "standard")]
public class TradingAdminScenarios(ITestOutputHelper output) : AtlasScenarioBase
{
    private const int Seed = 459459;

    private ICoreServerAPI Api => World.Api;
    private IWorldAccessor W => World.Api.World;
    private EconomySystem Economy => EconomySystem.Of(Api) ?? throw new Xunit.Sdk.XunitException("no EconomySystem");

    private async Task<EntitySeraphTrader> Spawn(string type, int dx, int dz)
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

    private async Task<string> Run(string command, Atlas.Api.ITestPlayer? player = null)
    {
        using var watch = new FormatErrorWatch(World.Api.Logger);
        var result = player == null ? await World.ExecuteCommand(command) : await player.ExecuteCommand(command);
        Assert.True(watch.Errors.Count == 0, $"{command} hit the translation formatter:\n" + string.Join("\n", watch.Errors));
        FormatErrorWatch.AssertChatSafe(command, result.Raw);
        output.WriteLine($"{command}: {(result.Ok ? "ok" : "FAILED")}:\n{result.Message}");
        Assert.True(result.Ok, $"{command}: {result.Message}");
        Assert.False(string.IsNullOrWhiteSpace(result.Message), $"{command} answered nothing");
        return result.Message!;
    }

    private async Task<JsonObject> Json(string command, Atlas.Api.ITestPlayer? player = null)
    {
        string text = await Run(command + " --json", player);
        var json = JsonNode.Parse(text) as JsonObject;
        Assert.True(json != null, $"{command} --json is not a JSON object: {text}");
        Assert.True((bool)json!["ok"]!);
        Assert.StartsWith("trade", (string)json["command"]!);
        Assert.False(string.IsNullOrEmpty((string?)json["summary"]));
        return json;
    }

    private async Task<Atlas.Api.ITestPlayer> Admin(string name)
    {
        var player = await World.JoinPlayer(name);
        Assert.True((await World.ExecuteCommand($"/player {name} role admin")).Ok);
        return player;
    }

    [AtlasScenario(TimeoutMs = 300_000)]
    public async Task Every_subcommand_answers_in_text_and_json()
    {
        var trader = await Spawn("smith", 6, 6);
        string id = "entity:" + trader.EntityId;
        var admin = await Admin("tradeadmin");
        // A standard world joins a player at a random spot around the spawn; `near` needs the trader
        // within 16 blocks.
        await admin.TeleportTo(trader.Pos.AsBlockPos.AddCopy(2, 0, 0));
        foreach (var command in new[]
                 {
                     $"/sh trade inspect {id}", $"/sh trade restock {id}", $"/sh trade restock {id} --full", $"/sh trade reroll {id}",
                     $"/sh trade wallet {id} 50", $"/sh trade budget {id} 7", "/sh trade values missing", "/sh trade values suspicious",
                     $"/sh trade maps {id}", "/sh trade export tradeadmin-all", "/sh trade import tradeadmin-all", "/sh trade log on supply",
                     "/sh trade log off", "/sh trade camps 3000", "/sh trade value game:ingot-iron", "/sh trade supply all",
                     "/sh trade supply game:ingot-iron", "/sh trade supply trace game:ingot-iron", "/sh trade simulate 1", "/sh trade standing tradeadmin",
                 })
        {
            await Run(command);
            await Json(command);
        }
        foreach (var command in new[] { "/sh trade inspect near", "/sh trade inspect", "/sh trade maps", "/sh trade map on game:ingot-iron", "/sh trade map off" })
        {
            await Run(command, admin);
            await Json(command, admin);
        }

        var inspect = await Json($"/sh trade inspect {id}");
        Assert.Equal("smith", (string)inspect["trader"]!["type"]!);
        Assert.NotEmpty(inspect["selling"]!.AsArray());
        Assert.Contains(inspect["selling"]!.AsArray(), s => (bool)s!["core"]!);
        var camps = await Json("/sh trade camps 3000");
        Assert.NotEmpty(camps["camps"]!.AsArray());
        var value = await Json("/sh trade value game:ingot-iron");
        Assert.Equal("direct", (string)value["source"]!);
        var maps = await Json($"/sh trade maps {id}");
        Assert.All(maps["candidates"]!.AsArray(), c => Assert.False((bool)c!["accepted"]! && !((string)c["id"]!).StartsWith("gravel:")));
        var suspicious = await Json("/sh trade values suspicious");
        Assert.IsType<JsonArray>(suspicious["belowIngredients"]);
    }

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Wallet_budget_restock_and_reroll_change_the_trader()
    {
        var trader = await Spawn("generalstore", -6, 6);
        string id = "entity:" + trader.EntityId;
        await Run($"/sh trade wallet {id} 123");
        Assert.Equal(123, trader.Inventory.GetTraderAssets());
        await Run($"/sh trade wallet {id} 0");
        Assert.Equal(0, trader.Inventory.GetTraderAssets());
        await Run($"/sh trade budget {id} 9");
        Assert.Equal(9, EconomySystem.SideBudgetOf(trader));
        trader.WatchedAttributes.SetDouble("lastRefreshTotalDays", W.Calendar.TotalDays - 3);
        await Run($"/sh trade restock {id} --full");
        Assert.True(trader.Inventory.GetTraderAssets() > 0, "a full restock refills the wallet");
        Assert.Equal(W.Calendar.TotalDays, trader.WatchedAttributes.GetDouble("lastRefreshTotalDays"), 1);
        Assert.Contains(trader.Inventory.SellingSlots, s => s.Itemstack != null);
        await Run($"/sh trade reroll {id}");
        Assert.Contains(trader.Inventory.SellingSlots, s => s.Itemstack != null);
        Assert.False((await World.ExecuteCommand("/sh trade wallet entity:999999999 5")).Ok);
        Assert.False((await World.ExecuteCommand($"/sh trade budget {id} -1")).Ok);
    }

    [AtlasScenario(TimeoutMs = 300_000)]
    public async Task Export_then_import_round_trips_supply_standing_and_the_registry()
    {
        var trader = await Spawn("mason", 8, -8);
        var standing = StandingSystem.Of(Api)!;
        var deposits = Api.ModLoader.GetModSystem<OreSystem>().Deposits!;
        var trading = TradingSystem.Of(Api)!;
        Assert.Contains("supply", trading.AdminState.Sections);
        Assert.Contains("standing", trading.AdminState.Sections);
        Assert.Contains("deposits", trading.AdminState.Sections);

        await World.JoinPlayer("roundtripper");
        string region = EconomySystem.RegionOf(trader);
        Economy.Supply.Set(region, "game:ingot-iron", 4.5);
        string traderId = standing.TraderIdOf(trader);
        await Run($"/sh trade standing set roundtripper {traderId} 300");
        var spawn = World.Spawn;
        var key = deposits.Candidates(spawn.X, spawn.Z, 9000, "copper").First().Key;
        deposits.Registry.Reset(key);
        deposits.Registry.MarkSold(key, "uid-r", "Roundtripper", 2);

        var exported = await Json("/sh trade export tradeadmin-roundtrip");
        Assert.Equal(["deposits", "standing", "supply"], exported["sections"]!.AsArray().Select(s => (string)s!).Order());

        Economy.Supply.Clear();
        await Run("/sh trade standing reset roundtripper");
        deposits.Registry.Reset(key);
        Assert.Equal(0, Economy.Supply.Level(region, "game:ingot-iron"));

        var imported = await Json("/sh trade import tradeadmin-roundtrip");
        Assert.Equal("imported", (string)imported["sections"]!["supply"]!);
        Assert.Equal(4.5, Economy.Supply.Level(region, "game:ingot-iron"), 6);
        var uid = Api.PlayerData.GetPlayerDataByLastKnownName("roundtripper")!.PlayerUID;
        Assert.Equal(300, standing.Ledger.Personal(uid, traderId)!.Points, 3);
        Assert.Equal("Roundtripper", deposits.Registry.Get(key).SoldToName);

        // The ore registry import reads the deposits section of a trade export too.
        deposits.Registry.Reset(key);
        await Run("/sh ore registry import tradeadmin-roundtrip");
        Assert.Equal(DepositState.Sold, deposits.Registry.Get(key).State);
        Assert.False((await World.ExecuteCommand("/sh trade import no-such-export")).Ok);
    }

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Players_without_controlserver_are_refused_but_keep_company()
    {
        await Spawn("farmer", -8, -8);
        var visitor = await World.JoinPlayer("tradevisitor");
        // A joined test player is admin by default (Atlas); a plain player has suplayer.
        visitor.Player.SetRole("suplayer");
        Assert.False(visitor.Player.HasPrivilege(Privilege.controlserver));
        foreach (var command in new[] { "/sh trade inspect near", "/sh trade values missing", "/sh trade export x", "/sh trade map on", "/sh trade camps" })
            Assert.False((await visitor.ExecuteCommand(command)).Ok, command);
        Assert.True((await visitor.ExecuteCommand("/sh company")).Ok);
        Assert.Empty(visitor.Client.Packets<AdminMapPacket>(AdminSystem.ChannelName));

        var admin = await Admin("overlaytrader");
        await Run("/sh trade map on game:ingot-iron", admin);
        await World.Ticks(5);
        var packet = admin.Client.Packets<AdminMapPacket>(AdminSystem.ChannelName).LastOrDefault(p => p.Key == "trade");
        Assert.True(packet != null, "no trade overlay arrived");
        var overlay = MapOverlay.FromJson(packet!.Json);
        Assert.Contains(overlay.Rects, r => r.Label?.StartsWith("camp cell") == true);
        Assert.Contains(overlay.Rects, r => r.Label?.StartsWith("supply") == true);
    }

    [AtlasScenario]
    public async Task The_trade_log_writes_its_channels()
    {
        await Run("/sh trade log on supply");
        Assert.Equal(["supply"], AdminLogs.Trade!.On);
        AdminLogs.Trade.Write("supply", "scenario supply line");
        AdminLogs.Trade.Write("standing", "not on");
        string text = File.ReadAllText(AdminLogs.Trade.Path);
        Assert.Contains("[supply] scenario supply line", text);
        Assert.DoesNotContain("not on", text);
        Assert.False((await World.ExecuteCommand("/sh trade log on gossip")).Ok);
        await Run("/sh trade log off");
        Assert.Empty(AdminLogs.Trade.On);
    }
}
