using System.Reflection;
using Atlas.XUnit;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using SeraphHorizons.Mod;
using SeraphHorizons.Mod.Core;
using SeraphHorizons.Mod.Woodworking;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace SeraphHorizons.PackTests;

/// <summary>
/// mods-src/seraphhorizons, UnifiedWoodworking with its switch off (<c>"UnifiedWoodworking": false</c>,
/// fixtures/unifiedwoodworking-off): Immersive Woodworking and Logging Expanded as they ship. Nothing
/// is patched, Immersive Woodworking's settings are its defaults, its chopping block has its grid
/// recipes and goes into the chopper as it is, an axe on a log makes Logging Expanded's
/// splitting log, clients are told it does not run, and the recipe export lists the two mods'
/// guides, not the unified one. Its own server.
/// </summary>
[AtlasWorld]
[AtlasDataFiles("fixtures/unifiedwoodworking-off", TargetPath = "ModConfig")]
public class UnifiedWoodworkingOffScenarios : AtlasScenarioBase
{
    private IWorldAccessor W => World.Api.World;

    [AtlasScenario]
    public void Switched_off_nothing_is_patched_or_set()
    {
        Assert.False(World.Api.LoadModConfig("seraphhorizons.json")["UnifiedWoodworking"].AsBool(true));
        Assert.False(World.Api.ModLoader.GetModSystem<SeraphHorizonsSystem>().Woodworking.Active);
        // What clients follow, whatever their own switch says.
        Assert.False(W.Config.GetBool(UnifiedWoodworking.RunningKey, true));
        Assert.DoesNotContain(Harmony.GetAllPatchedMethods(), m => Harmony.GetPatchInfo(m)?.Owners.Contains(UnifiedWoodworking.ServerHarmonyId) == true);

        var patched = Harmony.GetAllPatchedMethods()
            .Where(m => Harmony.GetPatchInfo(m)?.Owners.Contains(SeraphHorizonsSystem.HarmonyId) == true)
            .Select(m => m.DeclaringType?.Namespace)
            .ToList();
        Assert.DoesNotContain(WoodworkingMods.LeNamespace, patched);
        // ChopperDropsInFront still patches the chopper's EjectBatch; nothing else of the mod's.
        Assert.All(Harmony.GetAllPatchedMethods().Where(m => m.DeclaringType?.Namespace == WoodworkingMods.IwNamespace
                                                              && Harmony.GetPatchInfo(m)!.Owners.Contains(SeraphHorizonsSystem.HarmonyId)),
            m => Assert.Equal("EjectBatch", m.Name));

        // Every one of Immersive Woodworking's settings is its default.
        var system = World.Api.ModLoader.GetModSystem(WoodworkingMods.IwSystemType);
        var config = AccessTools.Property(system.GetType(), "Config").GetValue(system)!;
        var defaults = Activator.CreateInstance(config.GetType())!;
        foreach (var field in config.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance)
                     .Where(f => f.FieldType.IsPrimitive || f.FieldType == typeof(string)))
            Assert.True(Equals(field.GetValue(defaults), field.GetValue(config)), $"{field.Name} is {field.GetValue(config)}");
        Assert.Equal(8, (int)AccessTools.Field(config.GetType(), "FirewoodPerLog").GetValue(config)!);
    }

    [AtlasScenario]
    public void Switched_off_the_stations_are_as_they_ship()
    {
        var choppingBlock = W.GetBlock(new AssetLocation(Woodshop.SplittingBlock))!;
        Assert.Contains(W.GridRecipes, r => r.Output?.Code == choppingBlock.Code);
        Assert.Equal("Chopping block", Lang.GetL("en", "immersivewoodworking:block-choppingblock"));
        var splittingLog = W.GetBlock(new AssetLocation("loggingmod:splittinglog-oak"))!;
        Assert.NotEmpty(splittingLog.CreativeInventoryTabs);
        Assert.NotEqual(true, splittingLog.Attributes?["handbook"]["exclude"].AsBool());
        Assert.Contains(W.Items, i => i.Code?.Domain == WoodworkingMods.IwModId && i.Code.FirstCodePart() == "pitsaw"
                                      && i.CreativeInventoryTabs?.Length > 0);
        Assert.DoesNotContain(choppingBlock.BlockEntityBehaviors ?? [], b => b.Name == BEBehaviorSplittingBlockTier.Name);
        Assert.Equal(0.6875f, choppingBlock.CollisionBoxes[0].Y2, 4);
        // The recipe export leaves out the six pages a player does not see, and only them.
        Assert.Equal(WoodworkingGuidePages.Pages.Select(p => (p.PageCode, p.TitleKey())).Order(),
            ((IEnumerable<(string, string)>)World.Api.ObjectCache[WoodworkingGuide.HiddenGuidesKey]).Order());
    }

    [AtlasScenario(TimeoutMs = 600_000)]
    public void Switched_off_the_export_lists_the_two_mods_guides()
    {
        var guides = ((JArray)ExportUnderTest.Get(World.Api)["guides"]!).OfType<JObject>().ToList();
        Assert.DoesNotContain(guides, g => (string?)g["mod"] == "seraphhorizons");
        foreach (var (mod, code) in new[] { (WoodworkingMods.IwModId, "craftinginfo-woodworking"), (WoodworkingMods.LeModId, "introduction") })
            Assert.Single(guides, g => (string?)g["mod"] == mod && (string?)g["code"] == code);
    }

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Switched_off_an_axe_on_a_log_makes_Logging_Expandeds_splitting_log()
    {
        var shop = await Woodshop.Open(World, World.Spawn.AddCopy(80, 3, 80));
        var pos = shop.Cell(0);
        World.SetBlock("game:log-placed-oak-ud", pos);
        await World.Ticks(2);
        shop.Holding(Woodshop.Axe);
        Assert.True(shop.Click(pos));
        await World.Ticks(2);
        Assert.Equal("loggingmod:splittinglog-oak", World.BlockAt(pos).Code.ToString());
    }

    [AtlasScenario(TimeoutMs = 180_000)]
    public async Task Switched_off_the_chopper_takes_a_plain_chopping_block()
    {
        var origin = World.Spawn.AddCopy(120, 12, 0);
        var site = new ChopperSite(World, origin, BlockFacing.NORTH);
        await site.Build(hopper: false);
        var player = await World.JoinPlayer("offbed");
        await player.TeleportTo(origin.AddCopy(0, 0, 3));
        player.Player.WorldData.CurrentGameMode = EnumGameMode.Survival;
        var shop = new Woodshop(World, player, origin);

        var bed = new ItemStack(W.GetBlock(new AssetLocation(Woodshop.SplittingBlock)));
        bed.Attributes.SetString("wood", "birch");
        bed.Attributes.SetString("woodDomain", "game");
        shop.Holding(bed);
        Assert.True(W.BlockAccessor.GetBlock(origin).OnBlockInteractStart(W, shop.P, Woodshop.Selection(origin)));
        Assert.True((bool)AccessTools.Field(site.Chopper.GetType(), "hasBed").GetValue(site.Chopper)!);
        Assert.Null(shop.Hand.Itemstack);
        // Immersive Woodworking's own yield: its hand value, 8.
        Assert.Equal(8, site.ChopOneLog());
    }
}
