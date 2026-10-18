using System.Reflection;
using Atlas.XUnit;
using HarmonyLib;
using SeraphHorizons.Mod.Trading;
using SeraphHorizons.Mod.Trading.Core;
using SeraphHorizons.Mod.Trading.Standing;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Xunit.Abstractions;

namespace SeraphHorizons.PackTests;

/// <summary>
/// The story trader (mods-src/seraphhorizons/Trading/Core/StoryTraders.cs): in a world with the
/// trader grid the game's treasure hunter becomes the pack's treasure hunter, with every quest of the
/// game's treasure hunter dialogue kept. Its house's spawner is rewritten to the pack's code while
/// every other spawner of a story structure keeps its trader; the handbook leaves the game's treasure
/// hunter out; the quests run as the game's (the bronze pickaxe for the archive's map, the elk paid in
/// gears by dialogue, not by deal); a game treasure hunter spawned before is converted with its
/// dialogue variables; and the house spawns the pack's, known to standing by its structure. A class of
/// its own: the story locations need a survival world (Atlas' default creative play style turns lore
/// content off, and with it the story structures), standard, so the grid is on.
/// </summary>
[AtlasWorld(Seed = Seed, WorldType = "standard", PlayStyle = "surviveandbuild")]
public class TradingStoryScenarios(ITestOutputHelper output) : AtlasScenarioBase
{
    public const int Seed = 436447448;

    private ICoreServerAPI Api => World.Api;
    private IWorldAccessor W => World.Api.World;
    private IServerWorldAccessor SW => World.Api.World;
    private TradingSystem Trading => TradingSystem.Of(Api) ?? throw new Xunit.Sdk.XunitException("no TradingSystem");
    private VariablesModSystem Vars => Api.ModLoader.GetModSystem<VariablesModSystem>();

    private static readonly FieldInfo SpawnedField = AccessTools.Field(typeof(BlockEntitySpawner), "spawnedEntities");

    private Cuboidi HunterArea()
    {
        var story = Api.ModLoader.GetModSystem<GenStoryStructures>();
        output.WriteLine("story locations: " + string.Join(", ", story.Structures.Select(s => $"{s.Key} {s.Value.Location}")));
        var hunter = story.Structures.Get("treasurehunter");
        Assert.True(hunter != null, "the world has no treasure hunter location");
        return hunter!.Location;
    }

    private static string? Spawned(ICoreServerAPI api, string code, BlockPos pos)
    {
        var tree = new TreeAttribute();
        tree.SetString("type", code);
        tree.SetBlockPos("pos", pos);
        api.Event.PushEvent("onattemptspawnerspawn", tree);
        return tree.GetString("type");
    }

    /// <summary>One of the pack's (or the game's) traders of that code on the ground at that offset from spawn.</summary>
    private async Task<Entity> SpawnAt(string code, int dx, int dz, Action<Entity>? before = null)
    {
        var pos = World.Spawn.AddCopy(dx, 0, dz);
        pos.Y = W.BlockAccessor.GetTerrainMapheightAt(pos) + 1;
        var props = W.GetEntityType(new AssetLocation(code)) ?? throw new Xunit.Sdk.XunitException($"no entity {code}");
        var entity = W.ClassRegistry.CreateEntity(props);
        entity.Pos.SetPos(pos.X + 0.5, pos.Y, pos.Z + 0.5);
        before?.Invoke(entity);
        W.SpawnEntity(entity);
        await World.Ticks(5);
        return entity;
    }

    [AtlasScenario]
    public void The_treasure_hunters_spawner_spawns_the_packs_and_other_story_spawners_keep_theirs()
    {
        Assert.True(Trading.GridActive, Trading.GridOffReason);
        var area = HunterArea();
        var inside = new BlockPos(area.CenterX, area.Y1 + 2, area.CenterZ);
        Assert.Equal("treasurehunter", Trading.Camps!.StoryStructureAt(inside));

        // Vanilla's treasure hunter: a meta-spawner of trader-{male,female}-treasurehunter-temperate in
        // its house. It becomes the pack's treasure hunter, same gender and outfit set (the game's
        // climate system does not touch game: codes), never the cell's type: made a prospector, it
        // once lost its story dialogue and sold a map to itself.
        foreach (string gender in new[] { "male", "female" })
        {
            string code = $"game:trader-{gender}-treasurehunter-temperate";
            Assert.NotNull(W.GetEntityType(new AssetLocation(code)));
            Assert.Equal($"seraphhorizons:trader-{gender}-treasurehunter-temperate", Spawned(Api, code, inside));
            Assert.NotNull(W.GetEntityType(new AssetLocation($"seraphhorizons:trader-{gender}-treasurehunter-temperate")));
        }
        // Its corner too: the schematic's whole area.
        Assert.Equal("seraphhorizons:trader-male-treasurehunter-temperate",
            Spawned(Api, "game:trader-male-treasurehunter-temperate", new BlockPos(area.X1, area.Y1, area.Z1)));
        // Any other trader a story structure spawns is a story NPC and keeps its code.
        Assert.Equal("game:trader-female-agriculture-temperate", Spawned(Api, "game:trader-female-agriculture-temperate", inside));

        // Outside the house but within its landform radius (200 blocks), where the grid may place a
        // camp (structures keep only 100 blocks off): a camp's spawner there is still the cell's.
        var story = Api.ModLoader.GetModSystem<GenStoryStructures>();
        var near = new BlockPos(area.CenterX + 150, area.Y1, area.CenterZ);
        Assert.DoesNotContain(story.Structures, s => s.Value.Location.Contains(near.X, near.Z));
        Assert.Null(Trading.Camps.StoryStructureAt(near));
        string type = Trading.Grid!.TypeOf(TraderGrid.CellOf(near.X, near.Z));
        Assert.Matches($"^seraphhorizons:trader-female-{type}-(cold|temperate|desert)$", Spawned(Api, "game:trader-female-agriculture-temperate", near));
        // A treasure hunter's code from a camp's spawner (none has one) would be the cell's type too.
        Assert.Matches($"^seraphhorizons:trader-male-{type}-(cold|temperate|desert)$", Spawned(Api, "game:trader-male-treasurehunter-temperate", near));
    }

    [AtlasScenario]
    public void The_handbook_leaves_out_the_traders_the_grid_replaces_the_game_treasure_hunter_too()
    {
        // Trading/Game/TraderHandbook.cs: the server lists them in the world config for its
        // clients' handbooks and the recipe exporter.
        string[] hidden = TraderHandbookSystem.HiddenCodes(Api);
        output.WriteLine("hidden: " + string.Join(", ", hidden));
        var story = TraderHandbookSystem.StoryTraderCodes(Api);
        Assert.NotNull(story);
        output.WriteLine("story spawners: " + string.Join(", ", story!.Distinct()));
        // The treasure hunter's house spawns it (the game's code, which the grid rewrites).
        Assert.Contains(story, c => c.Contains("trader-male-treasurehunter-temperate"));

        // Vanilla's camp traders, Culinary Artillery's and Domestic Animal Trader's: their
        // spawners become the pack's traders.
        Assert.Contains("game:trader-male-agriculture-temperate", hidden);
        Assert.Contains("game:trader-female-commodities-desert", hidden);
        Assert.Contains("aculinaryartillery:trader-male-kitchenware-temperate", hidden);
        Assert.Contains("game:trader-female-domesticanimal-cold", hidden);
        // So does the game's treasure hunter, in every climate: the pack's is the one met.
        foreach (string climate in new[] { "temperate", "cold", "desert" })
        {
            Assert.Contains($"game:trader-male-treasurehunter-{climate}", hidden);
            Assert.Contains($"game:trader-female-treasurehunter-{climate}", hidden);
        }
        // The story villagers and the pack's own stay.
        Assert.DoesNotContain(hidden, c => c.Contains("villager", StringComparison.Ordinal) || c.StartsWith("seraphhorizons:", StringComparison.Ordinal));
        // Every hidden one has a trade list the handbook would have read.
        var listed = TraderHandbookSystem.Listed(Api).Select(t => t.Code.ToString()).ToHashSet();
        Assert.All(hidden, c => Assert.Contains(c, listed));
    }

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task The_packs_treasure_hunter_trades_as_the_packs_and_keeps_the_games_quests()
    {
        var hunter = (EntitySeraphTrader)await SpawnAt("seraphhorizons:trader-male-treasurehunter-temperate", 12, 12);
        try
        {
            Assert.Equal(TraderTypes.TreasureHunter, hunter.TraderType);
            Assert.Equal(EntitySeraphTrader.ClassName, hunter.Properties.Class);
            // Stocked from its own list (core first: the gravel map and lead offers, the treasure map).
            var list = Trading.Lists!.For(TraderTypes.TreasureHunter)!;
            Assert.Equal(0, list.CampWeight);
            var shelf = hunter.Inventory.SellingSlots.Where(s => s.Itemstack != null).Select(s => s.Itemstack.Collectible.Code.ToString()).ToList();
            output.WriteLine("selling: " + string.Join(", ", shelf));
            Assert.Contains("game:locatormap-treasures", shelf);
            Assert.Contains(hunter.Inventory.BuyingSlots, s => s.Itemstack?.Collectible.Code.ToString() == "game:pickaxe-tinbronze");
            // Away from its house it is an entity to standing, as any trader outside a camp.
            Assert.StartsWith("entity:", Trading.Standing.TraderIdOf(hunter));
            // The standing option is on (entity variable), beside the quest's.
            Assert.Equal("on", hunter.WatchedAttributes.GetTreeAttribute("variables")?.GetString(SeraphHorizons.Mod.Trading.Window.TradeWindowSystem.StandingVariable));

            // Its dialogue is the game's treasure hunter dialogue: every component, the same, but the
            // main menu's one more option and its answer.
            var talk = hunter.GetBehavior<EntityBehaviorConversable>();
            var vanilla = Api.Assets.Get(new AssetLocation("game:config/dialogue/treasurehunter.json")).ToObject<DialogueConfig>();
            var ours = Api.Assets.Get(new AssetLocation("seraphhorizons:config/dialogue/treasurehunter.json")).ToObject<DialogueConfig>();
            Assert.Equal(vanilla.components.Select(c => c.Code).Append(SeraphHorizons.Mod.Trading.Window.TradeWindowSystem.StandingComponent),
                ours.components.Select(c => c.Code));
            foreach (string quest in new[] { "takebronze", "map", "maprepeat", "maprepeatlazaret", "lazaret", "buyelk-purchased", "buyelk-purchaseddiscount2", "buyelk-callelk", "offerelk" })
            {
                var a = vanilla.components.Single(c => c.Code == quest);
                var b = ours.components.Single(c => c.Code == quest);
                Assert.Equal(a.Trigger, b.Trigger);
                Assert.Equal(a.JumpTo, b.JumpTo);
                Assert.Equal(a.SetVariables?.OrderBy(kv => kv.Key), b.SetVariables?.OrderBy(kv => kv.Key));
                Assert.Equal(a.TriggerData?.ToString(), b.TriggerData?.ToString());
            }
            var main = (DlgTalkComponent)ours.components.Single(c => c.Code == "main");
            var vmain = (DlgTalkComponent)vanilla.components.Single(c => c.Code == "main");
            Assert.Equal(vmain.Text.Select(t => t.JumpTo).ToList(), main.Text.Select(t => t.JumpTo).Where(j => j != SeraphHorizons.Mod.Trading.Window.TradeWindowSystem.StandingComponent).ToList());

            var p = await World.JoinRetrying("treasureseeker");
            await p.TeleportTo(hunter.Pos.AsBlockPos.AddCopy(2, 0, 0));
            var player = (IServerPlayer)p.Player;
            var controller = talk!.GetOrCreateController(player.Entity);
            Assert.NotNull(controller);

            // The bronze pickaxe for the archive's map: asked for, taken, the map given, remembered.
            Assert.True(player.Entity.TryGiveItemStack(new ItemStack(W.GetItem(new AssetLocation("game:pickaxe-tinbronze")))));
            controller.JumpTo("bronze");
            Assert.Equal("true", Vars.GetVariable(EnumActivityVariableScope.Entity, "requestbronze", hunter));
            controller.JumpTo("takebronze");
            await World.Ticks(2);
            Assert.Equal("true", Vars.GetVariable(EnumActivityVariableScope.Entity, "bronzereceived", hunter));
            Assert.Equal(0, Count(player, "game:pickaxe-tinbronze"));
            Assert.Equal(1, Count(player, "game:locatormap-resonancearchive"));

            // The elk, sold for the archive eidolon's vessel and 50 gears: paid in dialogue, out of the
            // player's purse into nobody's wallet, no deal and no standing for it; the elk called; the
            // player remembered.
            var gear = W.GetItem(new AssetLocation("game:gear-rusty"));
            Assert.True(player.Entity.TryGiveItemStack(new ItemStack(gear, 60)));
            Assert.True(player.Entity.TryGiveItemStack(new ItemStack(W.GetItem(new AssetLocation("game:rustypart-eidolon2tr")))));
            int purse = InventoryTrader.GetPlayerAssets(player.Entity);
            int wallet = hunter.Inventory.GetTraderAssets();
            var standing = StandingSystem.Of(Api);
            double before = standing?.ViewFor(player.PlayerUID, hunter).Personal ?? 0;
            var elksBefore = SW.LoadedEntities.Values.Where(e => e.Code.Path.StartsWith("tameddeer-")).Select(e => e.EntityId).ToHashSet();
            int elks = elksBefore.Count;
            controller.JumpTo("buyelk-purchaseddiscount1");
            await World.Until(() => SW.LoadedEntities.Values.Count(e => e.Code.Path.StartsWith("tameddeer-") && e.Alive) > elks, 200);
            Assert.Equal(purse - 50, InventoryTrader.GetPlayerAssets(player.Entity));
            Assert.Equal(0, Count(player, "game:rustypart-eidolon2tr"));
            Assert.Equal(wallet, hunter.Inventory.GetTraderAssets());
            Assert.Equal(before, standing?.ViewFor(player.PlayerUID, hunter).Personal ?? 0);
            Assert.Equal("true", Vars.GetPlayerVariable(player.PlayerUID, "boughtelk"));
            // Called within the dialogue's range (15 each way) of the trader.
            var elk = SW.LoadedEntities.Values.Single(e => e.Code.Path.StartsWith("tameddeer-") && !elksBefore.Contains(e.EntityId));
            output.WriteLine($"elk {elk.Code} at {elk.Pos.HorDistanceTo(hunter.Pos):0.0} blocks");
            Assert.InRange(Math.Abs(elk.Pos.X - hunter.Pos.X), 0, 16);
            Assert.InRange(Math.Abs(elk.Pos.Z - hunter.Pos.Z), 0, 16);
            elk.Die(EnumDespawnReason.Removed);

            // Another elk for 100 gears, through the answers the player picks, each on its conditions:
            // the gears stack to 64 (RustyGearStack), so 100 are in two stacks, which the dialogue
            // counts and takes across (DialogueStacks), exactly 100. Its patch is on even after an
            // earlier class's server stopped (MadMechanics & co. unpatch every id then).
            Assert.True(SeraphHorizons.Mod.DialogueStacks.Patched, "DialogueStacks' patch is gone");
            Assert.True(player.Entity.TryGiveItemStack(new ItemStack(gear, 64)));
            Assert.True(player.Entity.TryGiveItemStack(new ItemStack(gear, 64)));
            var saddle = W.Items.First(i => i?.Code?.Path.StartsWith("hoovedwearables-middleback-saddle") == true);
            var bridle = W.Items.First(i => i?.Code?.Path.StartsWith("hoovedwearables-head-bridle") == true);
            Assert.True(player.Entity.TryGiveItemStack(new ItemStack(saddle)));
            Assert.True(player.Entity.TryGiveItemStack(new ItemStack(bridle)));
            output.WriteLine("gear slots: " + string.Join(", ", GearSlots(player)));
            Assert.True(GearSlots(player).All(n => n < 100), "the gears should be in stacks under 100");
            purse = InventoryTrader.GetPlayerAssets(player.Entity);
            var components = (DialogueComponent[])AccessTools.Field(typeof(DialogueController), "dialogue").GetValue(controller)!;
            int Answer(string component, string jumpTo) =>
                ((DlgTalkComponent)components.Single(c => c.Code == component)).Text.Single(a => a.JumpTo == jumpTo).Id;
            elksBefore = SpawnedElks();
            controller.JumpTo("buyelkagain");
            controller.PlayerSelectAnswerById(Answer("buyelkagain-answer", "buyelk-saddle"));
            controller.PlayerSelectAnswerById(Answer("buyelk-saddleanswer", "buyelk-purchased"));
            await World.Until(() => SpawnedElks().Any(id => !elksBefore.Contains(id)), 200);
            Assert.Equal(purse - 100, InventoryTrader.GetPlayerAssets(player.Entity));
            Assert.Equal(wallet, hunter.Inventory.GetTraderAssets());
            var second = SW.LoadedEntities.Values.Single(e => e.Code.Path.StartsWith("tameddeer-") && e.Alive && !elksBefore.Contains(e.EntityId));
            output.WriteLine($"second elk {second.Code}; gears left {InventoryTrader.GetPlayerAssets(player.Entity)}");
            second.Die(EnumDespawnReason.Removed);
        }
        finally
        {
            hunter.Die(EnumDespawnReason.Removed);
        }
    }

    private HashSet<long> SpawnedElks() =>
        SW.LoadedEntities.Values.Where(e => e.Code.Path.StartsWith("tameddeer-")).Select(e => e.EntityId).ToHashSet();

    private static List<int> GearSlots(IServerPlayer player) =>
        new[] { Vintagestory.API.Config.GlobalConstants.hotBarInvClassName, Vintagestory.API.Config.GlobalConstants.backpackInvClassName }
            .SelectMany(i => player.InventoryManager.GetOwnInventory(i) ?? Enumerable.Empty<ItemSlot>())
            .Where(s => s.Itemstack?.Collectible.Code.Path == "gear-rusty").Select(s => s.Itemstack.StackSize).ToList();

    private static int Count(IServerPlayer player, string code)
    {
        int n = 0;
        foreach (string inv in new[] { Vintagestory.API.Config.GlobalConstants.hotBarInvClassName, Vintagestory.API.Config.GlobalConstants.backpackInvClassName })
            foreach (var slot in player.InventoryManager.GetOwnInventory(inv) ?? Enumerable.Empty<ItemSlot>())
                if (slot.Itemstack?.Collectible.Code.ToString() == code) n += slot.Itemstack.StackSize;
        return n;
    }

    [AtlasScenario(TimeoutMs = 60_000)]
    public async Task A_game_treasure_hunter_already_in_a_grid_world_becomes_the_packs_with_its_quest_progress()
    {
        // A save from before: the house's spawner had spawned the game's treasure hunter, which the
        // player had met and done quests with. Its spawner counts it (a meta-spawner here, by hand).
        var spawnerPos = World.Spawn.AddCopy(-14, 0, -14);
        spawnerPos.Y = W.BlockAccessor.GetTerrainMapheightAt(spawnerPos) + 1;
        W.BlockAccessor.SetBlock(W.GetBlock(new AssetLocation("game:meta-spawner")).Id, spawnerPos);
        var spawner = W.BlockAccessor.GetBlockEntity(spawnerPos) as BlockEntitySpawner;
        Assert.NotNull(spawner);
        Entity? old = null;
        try
        {
            // Counted by the spawner as it spawns (the conversion waits a tick).
            var props = W.GetEntityType(new AssetLocation("game:trader-female-treasurehunter-temperate"))!;
            old = W.ClassRegistry.CreateEntity(props);
            var pos = World.Spawn.AddCopy(-12, 0, -12);
            pos.Y = W.BlockAccessor.GetTerrainMapheightAt(pos) + 1;
            old.Pos.SetPos(pos.X + 0.5, pos.Y, pos.Z + 0.5);
            var vars = new TreeAttribute();
            foreach (string v in new[] { "hasmet", "requestbronze", "bronzereceived", "heardlazaret", "offeredelk" }) vars.SetString(v, "true");
            vars.SetString("someplayer-sometopic", "x");
            old.WatchedAttributes["variables"] = vars;
            old.Attributes.SetString("origin", "entityspawner");
            W.SpawnEntity(old);
            ((HashSet<long>)SpawnedField.GetValue(spawner)!).Add(old.EntityId);
            string name = old.GetBehavior<EntityBehaviorNameTag>()?.DisplayName ?? "";
            string[]? outfit = (old.WatchedAttributes["outfitcodes"] as StringArrayAttribute)?.value;
            string personality = old.WatchedAttributes.GetString("personality");
            var at = old.Pos.XYZ;
            output.WriteLine($"the game's: {old.Code} {name} {personality} outfit {string.Join(",", outfit ?? [])}");

            await World.Until(() => old.State == EnumEntityState.Despawned, 100);
            var hunter = W.GetEntitiesAround(at, 2, 2, e => e is EntitySeraphTrader { Alive: true }).OfType<EntitySeraphTrader>().Single();
            try
            {
                Assert.Equal(new AssetLocation("seraphhorizons:trader-female-treasurehunter-temperate"), hunter.Code);
                Assert.True(hunter.Pos.XYZ.DistanceTo(at) < 0.01);
                // Its quest progress, every entity variable, the per-player ones too; the standing option beside them.
                foreach (string v in new[] { "hasmet", "requestbronze", "bronzereceived", "heardlazaret", "offeredelk" })
                    Assert.Equal("true", Vars.GetVariable(EnumActivityVariableScope.Entity, v, hunter));
                Assert.Equal("x", hunter.WatchedAttributes.GetTreeAttribute("variables").GetString("someplayer-sometopic"));
                Assert.Equal("on", hunter.WatchedAttributes.GetTreeAttribute("variables").GetString(SeraphHorizons.Mod.Trading.Window.TradeWindowSystem.StandingVariable));
                // Itself: name, outfit, personality, origin.
                Assert.Equal(name, hunter.GetBehavior<EntityBehaviorNameTag>()?.DisplayName);
                Assert.Equal(outfit, (hunter.WatchedAttributes["outfitcodes"] as StringArrayAttribute)?.value);
                Assert.Equal(personality, hunter.Personality);
                Assert.Equal("entityspawner", hunter.Attributes.GetString("origin"));
                // Stocked from the pack's list.
                Assert.Contains(hunter.Inventory.SellingSlots, s => s.Itemstack?.Collectible.Code.ToString() == "game:locatormap-treasures");
                // The spawner counts the new one, so it does not spawn a second.
                var spawned = (HashSet<long>)SpawnedField.GetValue(spawner)!;
                Assert.Contains(hunter.EntityId, spawned);
                Assert.DoesNotContain(old.EntityId, spawned);
                // And the quest goes on from where it was: the archive's map again, as the game's would.
                Assert.Equal("true", Vars.GetVariable(EnumActivityVariableScope.Entity, "bronzereceived", hunter));
            }
            finally
            {
                hunter.Die(EnumDespawnReason.Removed);
            }
        }
        finally
        {
            if (old is { State: not EnumEntityState.Despawned }) old.Die(EnumDespawnReason.Removed);
            W.BlockAccessor.SetBlock(0, spawnerPos);
        }
    }

    [AtlasScenario(TimeoutMs = 300_000)]
    public async Task The_house_spawns_the_packs_treasure_hunter_known_to_standing_by_its_house()
    {
        var area = HunterArea();
        var p = await World.JoinRetrying("housecaller");
        await p.TeleportTo(new BlockPos(area.CenterX, W.BlockAccessor.GetTerrainMapheightAt(new BlockPos(area.CenterX, 0, area.CenterZ)) + 2, area.CenterZ));
        // The house is generated with the chunks around the player; its spawner is a meta-spawner of
        // the game's codes.
        BlockEntitySpawner? spawner = null;
        await World.Until(() => (spawner = FindSpawner(area)) != null, 1200);
        Assert.NotNull(spawner);
        output.WriteLine($"spawner at {spawner!.Pos}: {string.Join(", ", spawner.Data.EntityCodes)}, every {spawner.Data.InGameHourInterval} h, range mode {spawner.Data.SpawnRangeMode}");
        // Due now, as at a new world's first visit.
        spawner.Data.LastSpawnTotalHours = -1000;
        // Found by the spawner's own count, not by where it stands: the house is small, and one the
        // spawner made as the chunks loaded may have walked out of its door (and is not made again).
        var spawned = (HashSet<long>)SpawnedField.GetValue(spawner)!;
        EntitySeraphTrader? hunter = null;
        await World.Until(() => (hunter = spawned.Select(id => W.GetEntityById(id)).OfType<EntitySeraphTrader>()
            .FirstOrDefault(e => e.Alive && e.TraderType == TraderTypes.TreasureHunter)) != null, 600);
        Assert.NotNull(hunter);
        Assert.DoesNotContain(SW.LoadedEntities.Values, e => e.Code.Domain == "game" && e.Code.Path.Contains("treasurehunter") && e.Alive);
        Assert.Equal("story:treasurehunter", Trading.Standing.TraderIdOf(hunter!));
        Assert.Equal("seraphhorizons:config/dialogue/treasurehunter",
            hunter!.Properties.Server.BehaviorsAsJsonObj.Single(b => b["code"].AsString() == "conversable")["dialogue"].AsString());
        output.WriteLine($"{hunter.Code} {hunter.GetBehavior<EntityBehaviorNameTag>()?.DisplayName} at {hunter.Pos.AsBlockPos}, region {hunter.Region}");
    }

    private BlockEntitySpawner? FindSpawner(Cuboidi area)
    {
        const int size = Vintagestory.API.Config.GlobalConstants.ChunkSize;
        for (int cx = area.X1 / size; cx <= area.X2 / size; cx++)
            for (int cy = Math.Max(0, area.Y1 / size); cy <= area.Y2 / size; cy++)
                for (int cz = area.Z1 / size; cz <= area.Z2 / size; cz++)
                    if (W.BlockAccessor.GetChunk(cx, cy, cz)?.BlockEntities is { } bes)
                        foreach (var be in bes.Values)
                            if (be is BlockEntitySpawner s && s.Data.EntityCodes?.Any(c => c.Contains("treasurehunter")) == true) return s;
        return null;
    }
}
