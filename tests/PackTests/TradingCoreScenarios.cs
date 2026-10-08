using Atlas.XUnit;
using HarmonyLib;
using SeraphHorizons.Mod.Trading;
using SeraphHorizons.Mod.Trading.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Vintagestory.ServerMods;
using Xunit.Abstractions;

namespace SeraphHorizons.PackTests;

/// <summary>
/// mods-src/seraphhorizons/Trading, the trader overhaul's foundation (#447, #448) against the pinned
/// mods: the eleven trader types, their lists resolved against everything the pack registers, a
/// trader stocking from them, and the camp grid in a new world of a fixed seed (the grid is on for
/// worlds created with the mod). A class of its own: the seed is part of what it checks, and it needs
/// a standard world (Atlas' default superflat one generates no structures).
/// </summary>
[AtlasWorld(Seed = Seed, WorldType = "standard")]
[TestCaseOrderer(BootLogFirst.Name, BootLogFirst.Assembly)]
public class TradingCoreScenarios(ITestOutputHelper output) : AtlasScenarioBase
{
    public const int Seed = 436447448;

    private ICoreServerAPI Api => World.Api;
    private IWorldAccessor W => World.Api.World;
    private TradingSystem Trading => TradingSystem.Of(Api) ?? throw new Xunit.Sdk.XunitException("no TradingSystem");

    [AtlasScenario]
    [ReadsBootLog]
    public void The_boot_logs_nothing_about_trade_lists_or_traders()
    {
        var logged = World.BootDiagnostics
            .Where(e => e.Level is EnumLogType.Warning or EnumLogType.Error or EnumLogType.Fatal)
            .Where(e => e.Message.Contains("tradelist", StringComparison.OrdinalIgnoreCase)
                        || e.Message.Contains("trade list", StringComparison.OrdinalIgnoreCase)
                        || e.Message.Contains("TradeProperties", StringComparison.Ordinal)
                        || e.Message.Contains("trader", StringComparison.OrdinalIgnoreCase)
                        || e.Message.Contains("Trading", StringComparison.Ordinal))
            .Select(e => $"[{e.Level}] {e.Message}")
            .ToList();
        Assert.True(logged.Count == 0, "Logged:\n" + string.Join("\n", logged));
        // The eleven camp types and the two travelling merchants (#456).
        Assert.Equal(13, Trading.Lists?.Lists.Count);
    }

    [AtlasScenario]
    public void Eleven_trader_types_register_with_the_packs_class()
    {
        foreach (string type in TraderTypes.All)
            foreach (string gender in new[] { "male", "female" })
                foreach (string climate in new[] { "cold", "temperate", "desert" })
                {
                    var props = W.GetEntityType(new AssetLocation("seraphhorizons", TraderTypes.EntityPath(gender, type, climate)));
                    Assert.True(props != null, $"no entity type for {type} {gender} {climate}");
                    Assert.Equal(EntitySeraphTrader.ClassName, props!.Class);
                }
        Assert.Equal(11 * 2 * 3, W.EntityTypes.Count(t => t.Code.Domain == "seraphhorizons" && t.Class == EntitySeraphTrader.ClassName));
    }

    [AtlasScenario]
    public void Every_pack_trader_has_a_trade_window_title_and_a_name()
    {
        // The game's trade dialog titles itself Lang.GetMatching("tradingwindow-" + code path, name):
        // a game-domain key, whatever the entity's domain, so without one the raw key shows.
        var traders = W.EntityTypes.Where(t => t.Code.Domain == "seraphhorizons"
                                               && (t.Code.Path.StartsWith("trader-") || t.Code.Path.StartsWith("visitor-"))).ToList();
        Assert.Equal((11 + 2) * 2 * 3, traders.Count);
        foreach (var t in traders)
        {
            string type = t.Variant["type"];
            string title = Vintagestory.API.Config.Lang.GetMatching("tradingwindow-" + t.Code.Path, "Haldor");
            Assert.True(title.StartsWith("Local goods - Haldor the ", StringComparison.Ordinal), $"{t.Code}: {title}");
            Assert.Equal(Vintagestory.API.Config.Lang.Get("seraphhorizons:trading-type-" + type).Replace("general store", "general store keeper"),
                title["Local goods - Haldor the ".Length..]);
            string name = Vintagestory.API.Config.Lang.GetMatching("seraphhorizons:item-creature-" + t.Code.Path);
            Assert.False(name.Contains("item-creature", StringComparison.Ordinal), $"{t.Code}: {name}");
        }
    }

    [AtlasScenario]
    public void Every_list_resolves_against_the_pack_and_stocks_a_core_everywhere()
    {
        var lists = Trading.Lists ?? throw new Xunit.Sdk.XunitException("the lists did not load");
        Assert.True(lists.Problems.Count == 0, string.Join("\n", lists.Problems));
        Assert.True(lists.Unresolved.Count == 0, "Entries the game does not know:\n" + string.Join("\n", lists.Unresolved));
        Assert.Equal(TraderTypes.All.Concat(TraderTypes.Visitors).OrderBy(t => t), lists.Lists.Keys.OrderBy(t => t));
        foreach (var (type, def) in lists.Lists)
            foreach (var region in Region.All)
            {
                var resolved = TradeListResolver.Resolve(def, region);
                Assert.True(resolved.Selling.Core.Any(e => !e.PlayerSupplied), $"{type} has no core stock to sell in {region}");
                Assert.NotEmpty(resolved.Buying.Core);
                // Every entry makes the game's own trade item, with a stack.
                foreach (var e in resolved.Selling.Core.Concat(resolved.Selling.Rotating).Concat(resolved.Buying.Core).Concat(resolved.Buying.Rotating))
                    Assert.NotNull(lists.ItemFor(e).Resolve(W).Stack);
            }
    }

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task A_spawned_trader_stocks_from_its_list_for_its_region()
    {
        var pos = World.Spawn.AddCopy(40, 0, -40);
        pos.Y = Api.World.BlockAccessor.GetTerrainMapheightAt(pos) + 1;
        var props = W.GetEntityType(new AssetLocation("seraphhorizons", "trader-male-generalstore-temperate"))!;
        var entity = (EntitySeraphTrader)W.ClassRegistry.CreateEntity(props);
        entity.Pos.SetPos(pos.X + 0.5, pos.Y, pos.Z + 0.5);
        W.SpawnEntity(entity);
        await World.Ticks(5);

        Assert.Equal("generalstore", entity.TraderType);
        Assert.True(Region.TryParse(entity.WatchedAttributes.GetString(EntitySeraphTrader.RegionAttr, ""), out var region),
            "the trader has no region");
        var resolved = TradeListResolver.Resolve(Trading.Lists!.For("generalstore")!, region);
        output.WriteLine($"region {region}");

        var selling = entity.Inventory.SellingSlots.Where(s => s.Itemstack != null).ToList();
        var buying = entity.Inventory.BuyingSlots.Where(s => s.Itemstack != null).ToList();
        foreach (var s in selling.Concat(buying)) output.WriteLine($"{(s.IsBuyingSlot ? "buys" : "sells")} {s.Itemstack!.Collectible.Code} x{s.Itemstack.StackSize} stock {s.TradeItem.Stock} at {s.TradeItem.Price}");

        bool Same(ItemSlotTrade slot, TradeEntry e) => slot.Itemstack!.Collectible.Code.Equals(Trading.Lists!.ItemFor(e).Resolve(W).Stack.Collectible.Code);
        // The whole core that needs no player supply is on the shelf, and nothing player-supplied.
        foreach (var e in resolved.Selling.Core.Where(e => !e.PlayerSupplied))
            Assert.Contains(selling, s => Same(s, e));
        Assert.DoesNotContain(selling, s => resolved.Selling.Core.Concat(resolved.Selling.Rotating).Any(e => e.PlayerSupplied && Same(s, e)));
        Assert.All(selling, s => Assert.Contains(resolved.Selling.Core.Concat(resolved.Selling.Rotating), e => Same(s, e)));
        Assert.All(selling.Concat(buying), s => Assert.True(s.TradeItem.Stock > 0 && s.TradeItem.Price > 0));
        Assert.True(selling.Count >= resolved.Selling.Core.Count(e => !e.PlayerSupplied) + 1, "no rotating goods");
        foreach (var e in resolved.Buying.Core)
            Assert.Contains(buying, s => Same(s, e));
        Assert.All(buying, s => Assert.Contains(resolved.Buying.Core.Concat(resolved.Buying.Rotating), e => Same(s, e)));
        // The wallet is the list's, not vanilla's 30. The orders it posts on a restock (a random
        // number, #453) each hold their premium back from it.
        int held = SeraphHorizons.Mod.Trading.Orders.OrdersSystem.Of(Api) is { } orders
            ? orders.Book.OpenAt(SeraphHorizons.Mod.Trading.Orders.TraderFinder.IdOf(Api, entity)).Sum(o => o.Reserved)
            : 0;
        output.WriteLine($"wallet {entity.Inventory.GetTraderAssets()}, held for orders {held}");
        Assert.InRange(entity.Inventory.GetTraderAssets() + held, 60, 100);

        // A restock (as the weekly one does) keeps the core and stays within the list.
        entity.Restock(0.5f);
        selling = entity.Inventory.SellingSlots.Where(s => s.Itemstack != null).ToList();
        foreach (var e in resolved.Selling.Core.Where(e => !e.PlayerSupplied))
            Assert.Contains(selling, s => Same(s, e));
        entity.Die(EnumDespawnReason.Removed);
    }

    [AtlasScenario]
    public void The_games_random_trader_camps_are_taken_over_by_the_grid()
    {
        Assert.True(Trading.GridActive, Trading.GridOffReason);
        var gen = Api.ModLoader.GetModSystem<GenStructures>();
        var scfg = (WorldGenStructuresConfig)AccessTools.Field(typeof(GenStructures), "scfg").GetValue(gen)!;
        Assert.DoesNotContain(scfg.Structures, s => s.Group is "trader" or "acatraderclosetospawn");
        var camps = Trading.Camps!.Structures;
        output.WriteLine(string.Join(", ", camps.Select(s => s.Code)));
        // Vanilla's camps but its outposts, BetterTraders', Domestic Animal Trader's and Culinary Artillery's.
        Assert.True(camps.Count >= 14, $"{camps.Count} camp structures");
        Assert.All(camps, s => Assert.Equal(0, s.MinGroupDistance));
        Assert.DoesNotContain(camps, s => s.Schematics.Any(l => l.Path.StartsWith("trader/outpost")));
        Assert.Equal("on", Api.WorldManager.SaveGame.GetData<string>(TradingSystem.GridStateKey));
    }

    [AtlasScenario]
    public async Task Camps_lists_the_cells_around_the_spawn_for_the_seed()
    {
        var result = await World.ExecuteCommand("/sh trade camps 3000");
        output.WriteLine(result.Message);
        Assert.True(result.Ok, result.Message);
        var grid = new TraderGrid(Api.World.Seed, Trading.Lists!.CampWeights);
        var spawn = Api.World.DefaultSpawnPosition.AsBlockPos;
        var rows = CampListing.Rows(grid, Trading.Camps!.Registry, spawn.X, spawn.Z, 3000);
        Assert.NotEmpty(rows);
        var lines = result.Message!.Split('\n');
        Assert.Equal($"{rows.Count} trader camp cells within 3000 blocks of 0, 0:", lines[0]);
        Assert.Equal(rows.Count + 1, lines.Length);
        for (int i = 0; i < rows.Count; i++)
            Assert.StartsWith($"{rows[i].Cell} {Vintagestory.API.Config.Lang.Get("seraphhorizons:trading-type-" + rows[i].Type)}: ", lines[i + 1]);
        // The same seed gives the same types.
        Assert.Equal(new TraderGrid(Api.World.Seed, Trading.Lists.CampWeights).TypeOf(TraderGrid.CellOf(spawn.X, spawn.Z)),
            rows.Single(r => r.Cell == TraderGrid.CellOf(spawn.X, spawn.Z)).Type);

        var admin = await World.JoinPlayer("campadmin");
        Assert.True((await World.ExecuteCommand("/player campadmin role admin")).Ok);
        var bad = await admin.ExecuteCommand("/sh trade tp nowhere");
        Assert.False(bad.Ok);
        Assert.Contains("is not a camp id", bad.Message);
    }

    [AtlasScenario]
    public void A_trader_spawner_in_a_camp_spawns_the_cells_trader()
    {
        var pos = World.Spawn.AddCopy(-30, 0, 30);
        var cell = TraderGrid.CellOf(pos.X, pos.Z);
        var tree = new TreeAttribute();
        tree.SetString("type", "game:trader-female-agriculture-temperate");
        tree.SetBlockPos("pos", pos);
        Api.Event.PushEvent("onattemptspawnerspawn", tree);
        string type = Trading.Grid!.TypeOf(cell);
        Assert.Matches($"^seraphhorizons:trader-female-{type}-(cold|temperate|desert)$", tree.GetString("type"));
        Assert.NotNull(W.GetEntityType(new AssetLocation(tree.GetString("type"))));

        // Not a trader: left alone.
        var wolf = new TreeAttribute();
        wolf.SetString("type", "game:wolf-eurasian-adult-male");
        wolf.SetBlockPos("pos", pos);
        Api.Event.PushEvent("onattemptspawnerspawn", wolf);
        Assert.Equal("game:wolf-eurasian-adult-male", wolf.GetString("type"));
    }

    [AtlasScenario(TimeoutMs = 900_000)]
    public async Task Generating_a_cells_spots_decides_its_camp()
    {
        var spawn = Api.World.DefaultSpawnPosition.AsBlockPos;
        var home = TraderGrid.CellOf(spawn.X, spawn.Z);
        var grid = Trading.Grid!;
        var registry = Trading.Camps!.Registry;
        var placed = new List<CampRecord>();
        // The spawn's cell and its neighbours: generate each cell's spots in order until its camp is
        // placed or every spot is used.
        foreach (var cell in new[] { home }.Concat(TraderGrid.Neighbours(home)))
        {
            var spots = grid.Spots(cell);
            for (int i = 0; i < spots.Count && registry.Get(cell)?.Status is null or CampStatus.Pending; i++)
            {
                var spot = spots[i];
                bool loaded = false;
                Api.WorldManager.LoadChunkColumnPriority(spot.ChunkX, spot.ChunkZ, new ChunkLoadOptions { OnLoaded = () => loaded = true });
                await World.Until(() => loaded, 120_000);
                var at = new BlockPos(spot.X, 0, spot.Z);
                var climate = W.BlockAccessor.GetClimateAt(new BlockPos(spot.X, W.BlockAccessor.GetTerrainMapheightAt(at), spot.Z), EnumGetClimateMode.WorldGenValues);
                output.WriteLine($"cell {cell} spot {i} at {spot.X},{spot.Z}: height {W.BlockAccessor.GetTerrainMapheightAt(at)} (sea {W.SeaLevel}), "
                                 + $"{climate?.Temperature:0.0} °C, rain {climate?.Rainfall:0.00}, forest {climate?.ForestDensity:0.00} -> {registry.Get(cell)?.Status} attempt {registry.Get(cell)?.Attempt}");
            }
            var record = registry.Get(cell);
            Assert.NotNull(record);
            Assert.NotEqual(CampStatus.Pending, record!.Status);
            output.WriteLine($"cell {cell}: {record.Status} {record.Type} at {record.X},{record.Y},{record.Z} {record.Region} {record.Structure}");
            if (record.Status == CampStatus.Placed) placed.Add(record);
            if (placed.Count >= 2) break;
        }
        Assert.NotEmpty(placed);
        foreach (var record in placed)
        {
            Assert.Equal(grid.TypeOf(new CellKey(record.CellX, record.CellZ)), record.Type);
            Assert.True(Region.TryParse(record.Region, out _));
            // Recorded as the game records a camp: a generated structure of group trader there.
            var region = Api.WorldManager.GetMapRegion(record.X / Api.WorldManager.RegionSize, record.Z / Api.WorldManager.RegionSize);
            Assert.Contains(region.GeneratedStructures, s => s.Group == "trader" && s.Location.Contains(record.X, s.Location.Y1, record.Z));
        }
    }
}
