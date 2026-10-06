using System.Reflection;
using Atlas.XUnit;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using SeraphHorizons.Mod;
using SeraphHorizons.Mod.BuckingSawmill;
using SeraphHorizons.Mod.Core;
using SeraphHorizons.Mod.MachineOil;
using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Woodworking;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.Common;
using Vintagestory.GameContent;
using Xunit.Abstractions;

namespace SeraphHorizons.PackTests;

/// <summary>
/// mods-src/seraphhorizons with its switches off, each tweak as the mod it changes ships: one server
/// for all of them (<c>ModConfig/seraphhorizons.json</c> seeded from fixtures/switches-off before it
/// boots), since every class boots its own and that, not the scenarios, is what an off check costs.
/// <c>BoilerLidBlowsOpen</c>, the text-only and client-only switches, <c>FoodHydration</c> and
/// <c>AssembledMachinesInCreative</c> stay on: nothing here needs them off, and the Tidy Variants and
/// steam source scenarios require the boiler tweak still applied.
/// <para>A new switch's off scenario goes here, with its key in the fixture, not in a class of its
/// own; only one whose premise needs another switch on (which this world turns off) gets its own
/// class and fixture. Each scenario requires its switch read as off first, so a key missing from the
/// fixture fails it instead of passing on the default.</para>
/// </summary>
[AtlasWorld]
[AtlasDataFiles("fixtures/switches-off", TargetPath = "ModConfig")]
public class SwitchesOffScenarios(ITestOutputHelper output) : AtlasScenarioBase
{
    private IWorldAccessor W => World.Api.World;

    /// <summary>The setting as the mod reads it, from the file it was seeded with and wrote back:
    /// a missing key counts as neither off nor on.</summary>
    private bool Off(string key) => !World.Api.LoadModConfig(SeraphHorizonsSystem.ConfigFile)[key].AsBool(true);

    private bool On(string key) => World.Api.LoadModConfig(SeraphHorizonsSystem.ConfigFile)[key].AsBool(false);

    /// <summary><c>AgeOfFlaxRebalance</c>: Age of Flax as it ships.</summary>
    [AtlasScenario]
    public void Age_of_Flax_rebalance_off_Age_of_Flax_is_as_it_ships()
    {
        Assert.True(Off("AgeOfFlaxRebalance"));
        Assert.Equal(1.2f, AgeOfFlax.Field<float>(W, "ageofflax:ripple-primitive-east", "defaultFlaxSeedDropAvg"));
        Assert.Equal(12f, AgeOfFlax.Field<float>(W, "ageofflax:ripple-advanced-east", "defaultFlaxGrainDropAvg"));
        Assert.Equal(8f, AgeOfFlax.Field<float>(W, "ageofflax:hatchel-advanced-east", "defaultFlaxDropAvg"));
        Assert.Equal("game:metalnailsandstrips-iron",
            AgeOfFlax.Ingredient(AgeOfFlax.Recipe(W, "ageofflax:ripple-advanced-east"), "N"));
        Assert.Equal("game:fat", AgeOfFlax.Ingredient(AgeOfFlax.Recipe(W, "ageofflax:break-primitive-east"), "V"));
        Assert.DoesNotContain(W.GetBlock(new AssetLocation("game:crop-flax-9"))!.Drops,
            d => d.Code?.ToString() == "game:seeds-flax");

        var pos = World.Spawn.AddCopy(30, 2, 30);
        World.SetBlock("game:farmland-dry-medium", pos.DownCopy());
        Assert.Equal(0, AgeOfFlax.MeanDrop(W, W.GetBlock(new AssetLocation("game:crop-flax-9"))!, pos, "game:seeds-flax", 500));
        Assert.Equal("A tool used for extracting seeds from flax", Lang.GetMatching("ageofflax:blockdesc-ripple-simple-east"));
    }

    /// <summary><c>BarrelRackKegs</c>: the rack takes barrels only and nothing is patched.</summary>
    [AtlasScenario]
    public async Task Barrel_rack_kegs_off_the_rack_refuses_kegs()
    {
        Assert.True(Off("BarrelRackKegs"));
        Assert.False(RackSite.Patched());
        var p = await World.JoinPlayer("kegrefused");
        var pos = World.Spawn.AddCopy(0, 12, 40);
        await p.TeleportTo(pos.AddCopy(2, 0, 0));
        var site = new RackSite(World, pos, p);
        await site.Build();
        Assert.False(site.Takes(RackSite.Untapped));
        Assert.True(site.Takes(RackSite.Barrel));
        Assert.False(site.RightClick(site.Keg(RackSite.Untapped, 10)));
        Assert.True(site.Entity.Inventory.Empty);
    }

    /// <summary><c>BuckingSawmill</c>: its blocks and its recipe are not in the game, and nothing is
    /// logged about them.</summary>
    [AtlasScenario]
    public void Bucking_sawmill_off_the_mill_is_not_in_the_game()
    {
        Assert.True(Off("BuckingSawmill"));
        Assert.False(BuckingSawmillSystem.Applies(World.Api));
        Assert.DoesNotContain(W.Blocks, b => b?.Code is { Domain: "seraphhorizons" } code && code.Path.StartsWith("buckingmill"));
        Assert.DoesNotContain(W.GridRecipes, r => r.Output?.Code?.Path.StartsWith("buckingmill") == true);
        var logged = World.BootDiagnostics
            .Where(e => e.Level is EnumLogType.Warning or EnumLogType.Error or EnumLogType.Fatal)
            .Where(e => e.Message.Contains("buckingmill", StringComparison.OrdinalIgnoreCase)
                        || e.Message.Contains("bucking sawmill", StringComparison.OrdinalIgnoreCase))
            .Select(e => $"[{e.Level}] {e.Message}")
            .ToList();
        Assert.True(logged.Count == 0, "Logged:\n" + string.Join("\n", logged));
    }

    /// <summary><c>ChopperDropsInFront</c>: the chopper is not patched and throws its batch past the
    /// cell in front, as Immersive Woodworking ships it.</summary>
    [AtlasScenario(TimeoutMs = 180_000)]
    public async Task Chopper_drops_in_front_off_the_chopper_throws_its_batch_as_it_ships()
    {
        Assert.True(Off("ChopperDropsInFront"));
        Assert.False(ChopperSite.Patched(W));

        var origin = World.Spawn.AddCopy(60, 12, -60);
        await ChopperSite.Watcher(World, origin, "chopwatcher");
        var site = new ChopperSite(World, origin, BlockFacing.NORTH);
        await site.Build(hopper: false);
        int ejected = 0;
        for (int n = 0; n < 10; n++)
            ejected += site.ChopOneLog();
        await World.Ticks(150);

        var loose = site.Loose();
        Assert.Equal(ejected, loose.Sum(e => e.Itemstack.StackSize));
        var spots = loose.Select(site.Local).ToList();
        foreach (var (along, across, up) in spots)
            output.WriteLine($"along {along:F3} across {across:F3} up {up:F3}");
        // Immersive Woodworking's throw carries the batch past the cell in front.
        Assert.Contains(spots, s => s.Along > 2);
    }

    /// <summary><c>ClearCommand</c>: there is no <c>/clear</c>.</summary>
    [AtlasScenario]
    public async Task Clear_command_off_there_is_no_clear_command()
    {
        Assert.True(Off("ClearCommand"));
        Assert.Null(World.Api.ChatCommands.Get(ClearSky.Command));
        Assert.False((await World.ExecuteCommand("/clear")).Ok);
    }

    /// <summary><c>CreativeModTabs</c>: no plan, no patch, and the creative inventory has only the
    /// game's tabs.</summary>
    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Creative_mod_tabs_off_there_are_no_mod_tabs()
    {
        await World.Ticks(2);
        var system = World.Api.ModLoader.GetMod("seraphhorizons").Systems
            .Single(s => s.GetType().FullName == "SeraphHorizons.Mod.CreativeModTabs.ModTabsModSystem");
        Assert.Null(system.GetType().GetProperty("Authority", BindingFlags.Public | BindingFlags.Static)!.GetValue(null));
        Assert.True(Off("CreativeModTabs"));

        var owners = Harmony.GetAllPatchedMethods().SelectMany(m => Harmony.GetPatchInfo(m)?.Owners ?? []).ToHashSet();
        Assert.DoesNotContain("seraphhorizons.modtabs", owners);

        var inv = new InventoryPlayerCreative("creative", "atlas-modtabs-off", World.Api);
        typeof(InventoryPlayerCreative).GetMethod("UpdateFromWorld", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(inv, [W]);
        Assert.NotEmpty(inv.CreativeTabs.Tabs);
        Assert.DoesNotContain(inv.CreativeTabs.Tabs, t => t.Code.StartsWith("seraphhorizons-modtab-", StringComparison.Ordinal));
    }

    /// <summary><c>CreativeSteamSource</c>: its blocktype is disabled before the game loads
    /// blocktypes, so the block does not exist.</summary>
    [AtlasScenario]
    public void Creative_steam_source_off_the_block_does_not_exist()
    {
        Assert.True(Off("CreativeSteamSource"));
        Assert.True(On("BoilerLidBlowsOpen"));
        Assert.Null(W.GetBlock(new AssetLocation("seraphhorizons", CreativeSteamSource.BlockCode)));
        // The mod's other blocks are the bucking sawmill's, off here as well, and the inn flag and
        // sign, which load with TravellingMerchants off too (a world may hold them).
        Assert.DoesNotContain(W.Blocks, b => b.Code?.Domain == "seraphhorizons"
            && !b.Code.Path.StartsWith("innflag", StringComparison.Ordinal) && !b.Code.Path.StartsWith("innsign", StringComparison.Ordinal));
    }

    /// <summary><c>DurableSawmillBlades</c>: Immersive Woodworking's blade kits keep their own
    /// durability.</summary>
    [AtlasScenario]
    public void Durable_sawmill_blades_off_the_kits_are_as_they_ship()
    {
        Assert.True(Off("DurableSawmillBlades"));
        foreach (var (metal, durability) in SawmillBladeDurability.Shipped)
        {
            var kit = W.GetItem(new AssetLocation(SawmillBladeDurability.ModId, "sawmillblade-" + metal))!;
            Assert.Equal(durability, kit.GetMaxDurability(new ItemStack(kit)));
        }
    }

    /// <summary><c>GearboxSourceRatio</c>: nothing is patched, and a rotor placed after its gearbox,
    /// on the low side, takes the high side's ratio, as MPE Gearbox ships it (#462). When this fails
    /// with the rotor at 1, MPE Gearbox has fixed it and the tweak can go.</summary>
    [AtlasScenario]
    public async Task Gearbox_source_ratio_off_a_rotor_after_its_gearbox_takes_the_far_sides_ratio()
    {
        Assert.True(Off("GearboxSourceRatio"));
        Assert.False(GearboxTrain.Patched());
        var train = new GearboxTrain(World, World.Spawn.AddCopy(0, 12, -60));
        await train.Clear();
        await train.PlaceGearbox(lowSideToRotor: true);
        await train.PlaceConsumer();
        await train.PlaceRotor();
        train.AssertOneNetwork();
        var ratios = train.Ratios();
        output.WriteLine($"ratios (rotor, gearbox, consumer): {string.Join(", ", ratios)}");
        Assert.Equal([5f, 1f, 5f], ratios);
    }

    /// <summary><c>HeatingRackKeepsPosition</c>: nothing is patched, and the heating rack's picked
    /// stack carries its position, as Logging Expanded ships it.</summary>
    [AtlasScenario]
    public async Task Heating_rack_keeps_position_off_the_picked_stack_carries_its_position()
    {
        Assert.True(Off("HeatingRackKeepsPosition"));
        Assert.False(Harmony.HasAnyPatches(HeatingRackPosition.HarmonyId));
        var pos = World.Spawn.AddCopy(-60, 12, -60);
        World.SetBlock("loggingmod:resinrack-fire-north", pos);
        await World.Ticks(2);
        var stack = World.BlockAt(pos).OnPickBlock(W, pos);
        Assert.Equal(pos.X, stack.Attributes.GetInt("posx", int.MinValue));
    }

    /// <summary><c>IronWoodworkingMachines</c>: Immersive Woodworking's machine parts keep their own
    /// recipes, of any metal, and the Machines chapter says nothing of iron.</summary>
    [AtlasScenario]
    public void Iron_woodworking_machines_off_the_parts_are_as_they_ship()
    {
        Assert.True(Off("IronWoodworkingMachines"));
        foreach (var part in WoodworkingMachineCosts.Parts)
        {
            var recipe = MachineParts.Recipe(W, part);
            Assert.Equal(part.ShippedNails, MachineParts.Count(recipe, MachineParts.Nails));
            Assert.Equal(0, MachineParts.Count(recipe, MachineParts.Rod));
            foreach (string code in new[] { MachineParts.Nails, MachineParts.Plate })
                Assert.Null(MachineParts.Ingredient(recipe, code)?.AllowedVariants);
        }
        Assert.DoesNotContain("iron work", Lang.GetL("en", "seraphhorizons:woodworking-machines-text"));
    }

    /// <summary><c>HydrateTunRetired</c> and <c>LargerTunRack</c>: both tuns as their mods ship
    /// them.</summary>
    [AtlasScenario]
    public async Task Tun_switches_off_both_tuns_are_as_they_ship()
    {
        Assert.True(Off("HydrateTunRetired"));
        Assert.True(Off("LargerTunRack"));

        var tun = Tuns.Block(W, Tuns.HydrateTun);
        Assert.Single(Tuns.Recipes(W, "hydrateordiedrate", "tun"));
        Assert.True(Tuns.InCreative(tun));
        Assert.False(Tuns.HandbookExcluded(tun));

        Assert.False(Tuns.RackPatched());
        var pos = World.Spawn.AddCopy(-40, 12, 40);
        var rack = (BlockEntityContainer)await Tuns.Place(World, Tuns.Rack, pos);
        Assert.Equal((500f, 500, 500f), Tuns.RackCapacity(rack));
        rack.Inventory[0].Itemstack = new ItemStack(Tuns.Block(W, Tuns.RackTun));
        Assert.Equal(500f, Tuns.FillWithWater(W, pos));
    }

    /// <summary><c>IrrigationVesselRetired</c>: the irrigation vessel as Primitive Survival ships
    /// it.</summary>
    [AtlasScenario]
    public void Irrigation_vessel_retired_off_the_vessel_is_as_it_ships()
    {
        Assert.True(Off("IrrigationVesselRetired"));

        Assert.Contains(Ollas.VesselRecipes(W), r => r.Output?.Code?.ToString() == IrrigationVessel.Block);
        var vessel = Tuns.Block(W, IrrigationVessel.Block);
        Assert.True(Tuns.InCreative(vessel));
        Assert.False(Tuns.HandbookExcluded(vessel));
        Assert.Contains(Ollas.RuinLootCodes(W), c => c.StartsWith(IrrigationVessel.CodePrefix));
    }

    /// <summary><c>PanningDropsTrimmed</c>: panning as Wool, Tailor's Delight and Expanded Matter ship
    /// it, and their text as it ships.</summary>
    [AtlasScenario]
    public void Panning_drops_trimmed_off_panning_is_as_the_mods_ship_it()
    {
        Assert.True(Off("PanningDropsTrimmed"));
        var codes = Panning.AllDrops(W).Where(d => d.Block == Panning.Pan).Select(d => d.Code).ToHashSet();
        foreach (var code in (string[])["wool:fibers-generic-brown", "tailorsdelight:awl-flint", "tailorsdelight:buttons-horn", "game:nugget-uranium"])
            Assert.Contains(code, codes);
        Assert.Equal("Can be found in loot, while panning or sometimes bought from traders.",
            Lang.GetL("en", "tailorsdelight:handbook-item-buttons"));
    }

    /// <summary><c>MapReveal</c>: there is no <c>/revealmap</c> command.</summary>
    /// <summary><c>Rosser</c>: there is no rosser (its blocks and recipe are not in the game, and
    /// nothing is logged about them) and no debarked trunk, and Logging Expanded's trunk code is not
    /// patched.</summary>
    [AtlasScenario]
    public void Rosser_off_there_is_no_debarked_trunk()
    {
        Assert.True(Off("Rosser"));
        Assert.False(SeraphHorizons.Mod.Rosser.RosserSystem.Applies(World.Api));
        Assert.DoesNotContain(W.Blocks, b => b?.Code is { Domain: "seraphhorizons" } code && code.Path.StartsWith("rosser"));
        Assert.DoesNotContain(W.GridRecipes, r => r.Output?.Code?.Path.StartsWith("rosser") == true);
        var logged = World.BootDiagnostics
            .Where(e => e.Level is EnumLogType.Warning or EnumLogType.Error or EnumLogType.Fatal)
            .Where(e => e.Message.Contains("rosser", StringComparison.OrdinalIgnoreCase))
            .Select(e => $"[{e.Level}] {e.Message}")
            .ToList();
        Assert.True(logged.Count == 0, "Logged:\n" + string.Join("\n", logged));
        Assert.DoesNotContain(W.Blocks, b => b?.Code is { Domain: "loggingmod" } c && c.Path.StartsWith("treetrunk-")
                                             && b.Variant["branches"] == "debarked");
        var clean = W.GetBlock(new AssetLocation("loggingmod:treetrunk-oak-md-no-north"))!;
        Assert.Equal("loggingmod:treetrunk-md-no", clean.Shape.Base.ToString());
        var trunk = new ItemStack(clean);
        Assert.Null(Trunks.Debark(trunk, W));
        foreach (var (type, method, args) in new[] { ("LoggingMod.TreeManager", "GetPlacedLogCode", new[] { typeof(string) }),
                     ("LoggingMod.BEWorkstation", "BuildUnloadStack", new[] { typeof(IWorldAccessor) }) })
            Assert.DoesNotContain(SeraphHorizonsSystem.HarmonyId,
                Harmony.GetPatchInfo(AccessTools.Method(AccessTools.TypeByName(type), method, args))?.Owners ?? []);
    }

    [AtlasScenario]
    public void Map_reveal_off_there_is_no_command()
    {
        Assert.True(Off("MapReveal"));
        Assert.Null(World.Api.ChatCommands.Get("revealmap"));
    }

    /// <summary><c>TidyVariants</c>: the feature resolves nothing, so the client side has nothing to
    /// act on, and the tweaks left on are unaffected. (Named so that the tidy shard's
    /// <c>.TidyVariants</c> filter does not take it.)</summary>
    [AtlasScenario]
    public async Task Tidy_variants_off_the_feature_resolves_nothing_and_the_boiler_tweak_still_applies()
    {
        await World.Ticks(2);   // past the WorldReady run phase, where the server would resolve
        var system = World.Api.ModLoader.GetMod("seraphhorizons").Systems
            .Single(s => s.GetType().FullName == "SeraphHorizons.Mod.TidyVariants.TidyVariantsModSystem");
        Assert.Null(system.GetType().GetProperty("Bridge")!.GetValue(system));

        // The mod read the seeded file (and wrote back the settings it lacked, all on by default).
        Assert.True(Off("TidyVariants"));
        Assert.True(On("BoilerLidBlowsOpen"));

        // The boiler tweak's patch is still applied; nothing of the creative inventory's is.
        var owners = Harmony.GetAllPatchedMethods().SelectMany(m => Harmony.GetPatchInfo(m)?.Owners ?? []).ToHashSet();
        Assert.Contains("seraphhorizons", owners);
        Assert.DoesNotContain("seraphhorizons.creative", owners);
    }

    // UnifiedWoodworking: Immersive Woodworking and Logging Expanded as they ship. Nothing is
    // patched, Immersive Woodworking's settings are its defaults, its chopping block has its grid
    // recipes and goes into the chopper as it is, an axe on a log makes Logging Expanded's
    // splitting log, clients are told it does not run, and the recipe export lists the two mods'
    // guides, not the unified one.

    [AtlasScenario]
    public void Unified_woodworking_off_nothing_is_patched_or_set()
    {
        Assert.True(Off("UnifiedWoodworking"));
        Assert.False(World.Api.ModLoader.GetModSystem<SeraphHorizonsSystem>().Woodworking.Active);
        // What clients follow, whatever their own switch says.
        Assert.False(W.Config.GetBool(UnifiedWoodworking.RunningKey, true));
        Assert.DoesNotContain(Harmony.GetAllPatchedMethods(), m => Harmony.GetPatchInfo(m)?.Owners.Contains(UnifiedWoodworking.ServerHarmonyId) == true);

        var patched = Harmony.GetAllPatchedMethods()
            .Where(m => Harmony.GetPatchInfo(m)?.Owners.Contains(SeraphHorizonsSystem.HarmonyId) == true)
            .Select(m => m.DeclaringType?.Namespace)
            .ToList();
        Assert.DoesNotContain(WoodworkingMods.LeNamespace, patched);
        // ChopperDropsInFront, the mod's only other patch there, is off too: nothing of the mod's.
        Assert.DoesNotContain(WoodworkingMods.IwNamespace, patched);

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
    public void Unified_woodworking_off_the_stations_are_as_they_ship()
    {
        Assert.True(Off("UnifiedWoodworking"));
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
        // The recipe export leaves out the six pages a player does not see, and only them (and
        // machine oil's page, whose switch is off here too).
        Assert.Equal(WoodworkingGuidePages.Pages.Select(p => (p.PageCode, p.TitleKey()))
                .Append((MachineOilSystem.GuidePageCode, MachineOilSystem.GuideTitleKey)).Order(),
            ((IEnumerable<(string, string)>)World.Api.ObjectCache[WoodworkingGuide.HiddenGuidesKey]).Order());
    }

    [AtlasScenario(TimeoutMs = 600_000)]
    public void Unified_woodworking_off_the_export_lists_the_two_mods_guides()
    {
        Assert.True(Off("UnifiedWoodworking"));
        var guides = ((JArray)ExportUnderTest.Get(World.Api)["guides"]!).OfType<JObject>().ToList();
        Assert.DoesNotContain(guides, g => (string?)g["mod"] == "seraphhorizons");
        foreach (var (mod, code) in new[] { (WoodworkingMods.IwModId, "craftinginfo-woodworking"), (WoodworkingMods.LeModId, "introduction") })
            Assert.Single(guides, g => (string?)g["mod"] == mod && (string?)g["code"] == code);
    }

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Unified_woodworking_off_an_axe_on_a_log_makes_Logging_Expandeds_splitting_log()
    {
        Assert.True(Off("UnifiedWoodworking"));
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
    public async Task Unified_woodworking_off_the_chopper_takes_a_plain_chopping_block()
    {
        Assert.True(Off("UnifiedWoodworking"));
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

    /// <summary><c>OreCells</c>, <c>NoSurfaceCopper</c>, <c>SmallerDeposits</c>,
    /// <c>RarerDistricts</c>, <c>PlacerFields</c>: a world created with them off records them off,
    /// Interesting Ore Gen's spacing filter is not patched, and the deposits are as the mods ship
    /// them.</summary>
    [AtlasScenario]
    public void Ore_worldgen_off_ore_is_as_the_mods_ship_it()
    {
        foreach (var key in new[] { "OreCells", "NoSurfaceCopper", "SmallerDeposits", "RarerDistricts", "PlacerFields" })
            Assert.True(Off(key), key);
        var ore = World.Api.ModLoader.GetModSystem<SeraphHorizons.Mod.Ore.OreSystem>();
        Assert.Equal(SeraphHorizons.Mod.Ore.Core.OreWorldRecord.AllOff, ore.World);
        Assert.Null(ore.Placement);
        Assert.Null(ore.Placer);
        var approve = SeraphHorizons.Mod.Ore.OreCellPlacement.ApproveMethod;
        Assert.NotNull(approve);
        Assert.DoesNotContain(Harmony.GetPatchInfo(approve)?.Prefixes ?? [], p => p.owner == SeraphHorizons.Mod.Ore.OreSystem.HarmonyId);

        var deposits = World.Api.ModLoader.Systems.OfType<Vintagestory.ServerMods.GenDeposits>().SelectMany(g => g.Deposits ?? []).ToList();
        Assert.Contains(deposits, v => v.Code == "surfacecopper" && v.TriesPerChunk > 0);
        var hematite = deposits.First(v => v.Code == "hematite" && v.TriesPerChunk > 0);
        var radius = (NatFloat)AccessTools.Field(hematite.GeneratorInst.GetType(), "Radius").GetValue(hematite.GeneratorInst)!;
        Assert.Equal(hematite.Attributes["radius"]["avg"].AsFloat(), radius.avg);

        var districts = World.Api.ModLoader.Systems.First(s => s.GetType().FullName == "InterestingOreGen.Generators.HydrothermalDistrictSystem");
        var configs = (System.Collections.IEnumerable)AccessTools.Field(districts.GetType(), "_configs").GetValue(districts)!;
        Assert.All(configs.Cast<object>(), c =>
            Assert.NotEqual(7000, (int)AccessTools.Field(c.GetType(), "MinDistanceBetweenDistricts").GetValue(c)!));
    }

    /// <summary><c>TraderGrid</c>: a world created with it off records it off for good and keeps
    /// vanilla's camps; the systems built on the grid (standing, orders, deliveries, maps) are off with
    /// their own switches.</summary>
    [AtlasScenario]
    public void Trader_grid_off_the_world_keeps_vanillas_camps()
    {
        Assert.True(Off("TraderGrid"));
        var trading = SeraphHorizons.Mod.Trading.TradingSystem.Of(World.Api)!;
        Assert.False(trading.GridActive);
        Assert.False(trading.GridReady);
        Assert.Equal("off", World.Api.WorldManager.SaveGame.GetData<string>(SeraphHorizons.Mod.Trading.TradingSystem.GridStateKey));
    }

    /// <summary><c>TraderStanding</c>, <c>TraderOrders</c>, <c>TraderDeliveries</c>, <c>TraderMaps</c>:
    /// each system stands down, and the traders see no standing (everyone a stranger).</summary>
    [AtlasScenario]
    public void Trader_standing_orders_deliveries_and_maps_off_none_of_them_runs()
    {
        foreach (var key in new[] { "TraderStanding", "TraderOrders", "TraderDeliveries", "TraderMaps" })
            Assert.True(Off(key), key);
        Assert.False(SeraphHorizons.Mod.Trading.Standing.StandingSystem.Of(World.Api)!.Enabled);
        Assert.False(SeraphHorizons.Mod.Trading.TradingSystem.Of(World.Api)!.Standing.Enabled);
        Assert.False(SeraphHorizons.Mod.Trading.Orders.OrdersSystem.Of(World.Api)!.Enabled);
        Assert.False(SeraphHorizons.Mod.Trading.Deliveries.DeliveriesSystem.Of(World.Api)!.Enabled);
        Assert.False(World.Api.ModLoader.GetModSystem<SeraphHorizons.Mod.Trading.Maps.MapsSystem>()!.Active);
    }

    /// <summary><c>EverythingHasAPrice</c>, <c>RegionalSupply</c>: traders deal in their lists only
    /// and nothing gates their stock.</summary>
    [AtlasScenario]
    public void Economy_off_traders_deal_in_their_lists_only()
    {
        Assert.True(Off("EverythingHasAPrice"));
        Assert.True(Off("RegionalSupply"));
        var economy = SeraphHorizons.Mod.Trading.Economy.EconomySystem.Of(World.Api)!;
        Assert.False(economy.EverythingHasAPrice);
        Assert.False(economy.RegionalSupply);
        Assert.IsNotType<SeraphHorizons.Mod.Trading.Economy.RegionalSupplyGate>(SeraphHorizons.Mod.Trading.TradingSystem.Of(World.Api)!.SupplyGate);
    }

    /// <summary><c>TraderSchematics</c>, <c>MachineSchematics</c>: loot is not scrubbed, no recipe is
    /// gated or removed, and the traders' lists keep no machine schematic.</summary>
    [AtlasScenario]
    public void Schematics_off_loot_and_recipes_are_as_the_mods_ship_them()
    {
        Assert.True(Off("TraderSchematics"));
        Assert.True(Off("MachineSchematics"));
        var schematics = SeraphHorizons.Mod.Trading.Schematics.SchematicsSystem.Of(World.Api)!;
        Assert.NotNull(schematics.Table);
        Assert.Null(schematics.Report);
        Assert.DoesNotContain(Harmony.GetAllPatchedMethods(), m =>
            Harmony.GetPatchInfo(m)?.Owners.Contains(SeraphHorizons.Mod.Trading.Schematics.SchematicsSystem.HarmonyId) == true);
        Assert.DoesNotContain(W.GridRecipes, r => (r.ResolvedIngredients ?? []).Any(i =>
            i?.Code?.Domain == "seraphhorizons" && i.Code.Path.StartsWith("schematic-", StringComparison.Ordinal)));
    }

    /// <summary><c>AdminTools</c>: no admin map, no overlays, no admin subcommands.</summary>
    [AtlasScenario]
    public void Admin_tools_off_there_is_no_admin_map()
    {
        Assert.True(Off("AdminTools"));
        var admin = SeraphHorizons.Mod.Admin.AdminSystem.Of(World.Api)!;
        Assert.False(admin.Enabled);
        Assert.Null(admin.Map);
        Assert.Empty(admin.MapProviders);
    }

    /// <summary><c>TravellingMerchants</c>: no inns. The flag, the sign and the visitor entities
    /// still load (a world may hold them), a raised flag makes no inn, and there is no
    /// <c>/sh trade inn</c>.</summary>
    [AtlasScenario]
    public async Task Travelling_merchants_off_a_raised_flag_makes_no_inn()
    {
        Assert.True(Off("TravellingMerchants"));
        var inns = SeraphHorizons.Mod.Trading.Visitors.InnSystem.Of(World.Api)!;
        Assert.False(inns.Active);
        Assert.NotNull(W.GetEntityType(new AssetLocation("seraphhorizons:visitor-male-travellingmerchant-temperate")));
        var pos = World.Spawn.AddCopy(-30, 2, 30);
        World.SetBlock("seraphhorizons:innflag", pos);
        await World.Ticks(5);
        Assert.Empty(inns.Book.Inns);
        var result = await World.ExecuteCommand("/sh trade inn check");
        Assert.False(result.Ok, result.Message);
    }

    /// <summary><c>MachineOil</c>: nothing is patched, a pulverizer loads its shaft as the game ships
    /// it with no tank, and tallow on it is not taken.</summary>
    [AtlasScenario]
    public async Task Machine_oil_off_machines_turn_as_they_ship()
    {
        Assert.True(Off("MachineOil"));
        Assert.False(OilSite.AnyPatched());
        var p = await World.JoinPlayer("nooil");
        var pos = World.Spawn.AddCopy(100, 12, 100);
        await p.TeleportTo(pos.AddCopy(2, 0, 0));
        var site = new OilSite(World, pos, p.Player);
        await site.Clear();
        World.SetBlock(OilSite.Pulverizer, pos);
        await World.Ticks(2);
        var be = Assert.IsType<Vintagestory.GameContent.Mechanics.BEPulverizer>(W.BlockAccessor.GetBlockEntity(pos));
        be.hasAxle = true;
        Assert.Equal(0.085f, be.GetBehavior<Vintagestory.GameContent.Mechanics.BEBehaviorMPPulverizer>()!.GetResistance(), 4);
        Assert.Null(ForeignMachines.StateOf(be));
        site.RightClick(pos, site.Stack(OilSite.Tallow, 4));
        Assert.Equal(4, site.Hand.Itemstack?.StackSize ?? 0);
        var tree = new Vintagestory.API.Datastructures.TreeAttribute();
        be.ToTreeAttributes(tree);
        Assert.Null(tree[SeraphHorizons.Mod.Machines.Oil.TreeKey]);
        Assert.DoesNotContain("Oil", OilSite.Info(be, p.Player));
    }
}
