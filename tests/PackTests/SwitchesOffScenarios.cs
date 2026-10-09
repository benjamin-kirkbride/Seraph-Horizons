using System.Reflection;
using Atlas.XUnit;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using SeraphHorizons.Mod;
using SeraphHorizons.Mod.BuckingSawmill;
using SeraphHorizons.Mod.Core;
using SeraphHorizons.Mod.GearReclamation;
using SeraphHorizons.Mod.GearReclamation.Core;
using SeraphHorizons.Mod.MachineOil;
using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.TrunkEntities;
using SeraphHorizons.Mod.Woodworking;
using SeraphHorizons.RecipeExport;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
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

    /// <summary><c>FewerSupportChains</c>: Better Ruins' blueprint makes its own 64 chains a craft.</summary>
    [AtlasScenario]
    public void Fewer_support_chains_off_the_blueprint_makes_64()
    {
        Assert.True(Off("FewerSupportChains"));
        foreach (var code in SupportChains.Codes)
            Assert.Equal(SupportChains.Shipped, Assert.Single(SupportChainRecipes.Of(W, code)).Output.Quantity);
    }

    /// <summary><c>LocomotiveRidersStayOn</c> and <c>LocomotiveRidersBreathe</c>: nothing is
    /// patched, and Yang's seat check still answers yes for a standard locomotive, as Yang ships it.</summary>
    [AtlasScenario]
    public void Locomotive_riders_switches_off_the_seat_check_is_as_Yang_ships_it()
    {
        Assert.True(Off("LocomotiveRidersStayOn"));
        Assert.True(Off("LocomotiveRidersBreathe"));
        Assert.False(Harmony.HasAnyPatches(LocomotiveSeats.StayOnHarmonyId));
        Assert.False(Harmony.HasAnyPatches(LocomotiveSeats.BreatheHarmonyId));
        var type = W.GetEntityType(new AssetLocation("yangtransport:sglocomotive-standard"));
        Assert.NotNull(type);
        var loco = W.ClassRegistry.CreateEntity(type);
        AccessTools.Property(typeof(Entity), nameof(Entity.Properties)).SetValue(loco, type);
        var check = AccessTools.DeclaredMethod(AccessTools.TypeByName(LocomotiveSeats.SeatType), LocomotiveSeats.CollisionCheckMethod, [typeof(Entity)]);
        Assert.NotNull(check);
        Assert.True((bool)check.Invoke(null, [loco])!);
    }

    /// <summary><c>FlatFellingWear</c>: nothing is patched, and felling a tree that leaves a trunk
    /// costs the axe the game's one durability per log.</summary>
    [AtlasScenario(TimeoutMs = 180_000)]
    public async Task Flat_felling_wear_off_felling_costs_a_log_each()
    {
        Assert.True(Off("FlatFellingWear"));
        Assert.False(FellingWear.Patched);
        var site = await Felling.Sky(World, World.Spawn.AddCopy(-300, 90, 1200));
        var log = await Felling.Grow(World, site, Felling.Oak, 1.0f, 101);
        var (_, wood) = Felling.Tree(W, (ItemAxe)W.GetItem(new AssetLocation("game:axe-felling-iron")), log);
        Assert.True(wood > 4, $"an oak of {wood} logs from {log} {W.BlockAccessor.GetBlock(log).Code}");
        var (player, slot, axe) = await Felling.Feller(World, "vanillafeller");

        int lost = await Felling.Fell(World, player, slot, axe, log);

        Assert.Equal(wood, lost);
        Assert.NotEmpty(Felling.TrunksNear(World, site));
    }

    /// <summary><c>PackVersionCheck</c>: nothing is read or compared, and nothing is logged about it
    /// but the line that says it is off.</summary>
    [AtlasScenario]
    public void Pack_version_check_off_nothing_is_checked()
    {
        Assert.True(Off("PackVersionCheck"));
        var check = World.Api.ModLoader.GetModSystem<SeraphHorizons.Mod.PackCheck.PackCheckSystem>();
        Assert.NotNull(check);
        Assert.Null(check.Pack);
        Assert.Empty(check.Findings);
        Assert.DoesNotContain(World.BootDiagnostics, e => e.Level is EnumLogType.Warning
            && e.Message.Contains("Pack version check", StringComparison.Ordinal));
    }

    /// <summary><c>GearBlanks</c>: no gear blanks, no gear blank molds and no recipes for either,
    /// and nothing logged about them.</summary>
    [AtlasScenario]
    public void Gear_blanks_off_there_are_no_blanks_or_molds()
    {
        Assert.True(Off("GearBlanks"));
        Assert.Null(W.GetItem(new AssetLocation(SeraphHorizons.Mod.Gears.GearBlanks.Blank)));
        Assert.Null(W.GetItem(new AssetLocation(SeraphHorizons.Mod.Gears.GearBlanks.LargeBlank)));
        Assert.DoesNotContain(W.Blocks, b => b?.Code is { Domain: "seraphhorizons" } c && c.Path.StartsWith("toolmold-"));
        Assert.DoesNotContain(World.Api.GetSmithingRecipes(), r => r.Output?.Code?.Domain == "seraphhorizons");
        Assert.DoesNotContain(World.Api.GetClayformingRecipes(), r => r.Output?.Code?.Domain == "seraphhorizons");
        var logged = World.BootDiagnostics
            .Where(e => e.Level is EnumLogType.Warning or EnumLogType.Error or EnumLogType.Fatal)
            .Where(e => e.Message.Contains("gearblank", StringComparison.OrdinalIgnoreCase))
            .Select(e => $"[{e.Level}] {e.Message}")
            .ToList();
        Assert.True(logged.Count == 0, "Logged:\n" + string.Join("\n", logged));
    }

    /// <summary><c>CastPipes</c>: Steelmaking Expanded's tool molds as it ships them (no pipe tool
    /// type, the patch file emptied), no recipe for one, and nothing logged about them.</summary>
    [AtlasScenario]
    public void Cast_pipes_off_there_is_no_pipe_mold()
    {
        Assert.True(Off("CastPipes"));
        Assert.DoesNotContain(W.Blocks, b => b?.Code is { Domain: "smex" } c && c.Path.StartsWith("toolmold-") && c.Path.EndsWith("-pipe"));
        Assert.NotNull(W.GetBlock(new AssetLocation("smex:toolmold-blue-fired-quadrod")));
        Assert.DoesNotContain(World.Api.GetClayformingRecipes(), r => r.Output?.Code?.Path?.EndsWith("-pipe") == true);
        var logged = World.BootDiagnostics
            .Where(e => e.Level is EnumLogType.Warning or EnumLogType.Error or EnumLogType.Fatal)
            .Where(e => e.Message.Contains("castpipe", StringComparison.OrdinalIgnoreCase)
                        || e.Message.Contains("Cast pipes", StringComparison.Ordinal)
                        || e.Message.Contains("-pipe", StringComparison.Ordinal) && e.Message.Contains("toolmold", StringComparison.Ordinal))
            .Select(e => $"[{e.Level}] {e.Message}")
            .ToList();
        Assert.True(logged.Count == 0, "Logged:\n" + string.Join("\n", logged));
    }

    /// <summary>Switch ownership (SwitchRegistry): every switch off here owns nothing that is
    /// registered, so the registry's "adds it when on" holds; and the server tells clients which
    /// switches are off, for the handbook's value line.</summary>
    [AtlasScenario]
    public void Switches_off_own_nothing_registered_and_are_published()
    {
        var registry = SwitchRegistry.For(World.Api);
        var off = registry.Switches.Where(Off).ToList();
        Assert.Contains("GearCutter", off);
        Assert.Contains("DrawBench", off);
        Assert.Contains("PressBrake", off);
        Assert.Contains("SquaringShear", off);
        Assert.Contains("MandrelStation", off);
        Assert.Contains("Rosser", off);
        var left = W.Collectibles.Where(c => c?.Code != null && !c.IsMissing)
            .Select(c => (Code: c.Code.ToString(), Owner: registry.SwitchForCode(c.Code.ToString())))
            .Where(c => c.Owner != null && off.Contains(c.Owner))
            .Select(c => $"{c.Code} ({c.Owner})").ToList();
        Assert.True(left.Count == 0, "Registered with their switch off: " + string.Join(", ", left.Take(20)));
        var published = SwitchOwnership.DecodeOff(World.Api.World.Config.GetString(SeraphHorizons.Mod.Trading.Values.ValueHandbook.OffKey));
        Assert.Contains("GearCutter", published);
        Assert.Contains("Rosser", published);
        Assert.DoesNotContain("BoilerLidBlowsOpen", published);
    }

    /// <summary><c>GearCutter</c>: no gear cutter blocks, none of its new parts and no recipe for
    /// them, and nothing logged about them.</summary>
    [AtlasScenario]
    public void Gear_cutter_off_there_is_no_gear_cutter()
    {
        Assert.True(Off("GearCutter"));
        Assert.False(SeraphHorizons.Mod.GearCutter.GearCutterSystem.Applies(World.Api));
        Assert.DoesNotContain(W.Blocks, b => b?.Code is { Domain: "seraphhorizons" } c && c.Path.StartsWith("gearcutter"));
        Assert.DoesNotContain(W.Items, i => i?.Code is { Domain: "seraphhorizons" } c && c.Path.StartsWith("gearcutter"));
        Assert.DoesNotContain(W.GridRecipes, r => r.Output?.Code?.Path?.StartsWith("gearcutter") == true);
        Assert.DoesNotContain(World.Api.GetSmithingRecipes(), r => r.Output?.Code?.Path?.StartsWith("gearcutter") == true);
        // the gears it cuts exist either way
        Assert.NotNull(W.GetItem(new AssetLocation(SeraphHorizons.Mod.GearCutter.Core.GearCut.Gear)));
        // The mod's own text names the cutter without a link, which would open no page: the gear
        // article and the machine oil page among it; the machine oil page no longer lists it.
        var linked = Lang.AvailableLanguages["en"].GetAllEntries()
            .Where(e => e.Key.StartsWith("seraphhorizons:", StringComparison.Ordinal)
                        && System.Text.RegularExpressions.Regex.IsMatch(e.Value, "handbook://(block|item)-seraphhorizons:gearcutter"))
            .Select(e => e.Key).ToList();
        Assert.True(linked.Count == 0, "Still link the gear cutter: " + string.Join(", ", linked));
        Assert.Contains("gear cutter", Lang.Get("seraphhorizons:gearreclamation-text"));
        Assert.DoesNotContain("gear cutter", Lang.Get("seraphhorizons:machineoil-text"));
        var logged = World.BootDiagnostics
            .Where(e => e.Level is EnumLogType.Warning or EnumLogType.Error or EnumLogType.Fatal)
            .Where(e => e.Message.Contains("gearcutter", StringComparison.OrdinalIgnoreCase)
                        || e.Message.Contains("machineoil-text", StringComparison.Ordinal))
            .Select(e => $"[{e.Level}] {e.Message}")
            .ToList();
        Assert.True(logged.Count == 0, "Logged:\n" + string.Join("\n", logged));
    }

    /// <summary><c>DrawBench</c>: no draw bench blocks, no dies and no recipe for them, no link to
    /// them in the mod's own text, the machine oil page without it whatever the gear cutter's switch
    /// (both are off here, and their edits to that page touch different passages), and nothing
    /// logged about them.</summary>
    [AtlasScenario]
    public void Draw_bench_off_there_is_no_draw_bench()
    {
        Assert.True(Off("DrawBench"));
        Assert.False(SeraphHorizons.Mod.DrawBench.DrawBenchSystem.Applies(World.Api));
        Assert.DoesNotContain(W.Blocks, b => b?.Code is { Domain: "seraphhorizons" } c && c.Path.StartsWith("drawbench"));
        Assert.DoesNotContain(W.Items, i => i?.Code is { Domain: "seraphhorizons" } c && c.Path.StartsWith("drawdie"));
        Assert.DoesNotContain(W.GridRecipes, r => r.Output?.Code?.Path?.StartsWith("drawbench") == true);
        Assert.DoesNotContain(World.Api.GetSmithingRecipes(), r => r.Output?.Code?.Path?.StartsWith("drawdie") == true);
        // the parts it takes are the game's, and exist either way
        Assert.NotNull(W.GetItem(new AssetLocation(SeraphHorizons.Mod.DrawBench.Core.DrawBenchParts.GearboxCode)));
        var linked = Lang.AvailableLanguages["en"].GetAllEntries()
            .Where(e => e.Key.StartsWith("seraphhorizons:", StringComparison.Ordinal)
                        && System.Text.RegularExpressions.Regex.IsMatch(e.Value, "handbook://(block|item)-seraphhorizons:(drawbench|drawdie)"))
            .Select(e => e.Key).ToList();
        Assert.True(linked.Count == 0, "Still link the draw bench: " + string.Join(", ", linked));
        var oil = Lang.Get("seraphhorizons:machineoil-text");
        Assert.DoesNotContain("draw bench", oil);
        Assert.Contains("pulverizer</a>, the <a href=\"handbook://block-immersivewoodworking:sawmill-frame-north\">", oil);
        Assert.Contains("a pulverizer half a point an item, the sawmill", oil);
        var logged = World.BootDiagnostics
            .Where(e => e.Level is EnumLogType.Warning or EnumLogType.Error or EnumLogType.Fatal)
            .Where(e => e.Message.Contains("drawbench", StringComparison.OrdinalIgnoreCase)
                        || e.Message.Contains("draw bench", StringComparison.OrdinalIgnoreCase)
                        || e.Message.Contains("machineoil-text", StringComparison.Ordinal))
            .Select(e => $"[{e.Level}] {e.Message}")
            .ToList();
        Assert.True(logged.Count == 0, "Logged:\n" + string.Join("\n", logged));
    }

    /// <summary><c>PressBrake</c>: no press brake blocks and no recipe for its frame, no link to it in
    /// the mod's own text, and nothing logged about it. The angles it makes are UnifiedPipes' items
    /// (here off too); the half plates it folds are the squaring shear's (here off too).</summary>
    [AtlasScenario]
    public void Press_brake_off_there_is_no_press_brake()
    {
        Assert.True(Off("PressBrake"));
        Assert.False(SeraphHorizons.Mod.PressBrake.PressBrakeSystem.Applies(World.Api));
        Assert.DoesNotContain(W.Blocks, b => b?.Code is { Domain: "seraphhorizons" } c && c.Path.StartsWith("pressbrake"));
        Assert.DoesNotContain(W.GridRecipes, r => r.Output?.Code?.Path?.StartsWith("pressbrake") == true);
        Assert.Null(W.GetItem(new AssetLocation(SeraphHorizons.Mod.PressBrake.Core.Folding.LeadHalfPlate)));
        var linked = Lang.AvailableLanguages["en"].GetAllEntries()
            .Where(e => e.Key.StartsWith("seraphhorizons:", StringComparison.Ordinal)
                        && System.Text.RegularExpressions.Regex.IsMatch(e.Value, "handbook://block-seraphhorizons:pressbrake"))
            .Select(e => e.Key).ToList();
        Assert.True(linked.Count == 0, "Still link the press brake: " + string.Join(", ", linked));
        var logged = World.BootDiagnostics
            .Where(e => e.Level is EnumLogType.Warning or EnumLogType.Error or EnumLogType.Fatal)
            .Where(e => e.Message.Contains("pressbrake", StringComparison.OrdinalIgnoreCase)
                        || e.Message.Contains("press brake", StringComparison.OrdinalIgnoreCase))
            .Select(e => $"[{e.Level}] {e.Message}")
            .ToList();
        Assert.True(logged.Count == 0, "Logged:\n" + string.Join("\n", logged));
    }

    /// <summary><c>SquaringShear</c>: no squaring shear blocks, no half plate and no recipe for the
    /// frame, no link to either in the mod's own text, and nothing logged about them. The plates it
    /// cuts and the parts it takes (the game's) exist either way.</summary>
    [AtlasScenario]
    public void Squaring_shear_off_there_is_no_squaring_shear_and_no_half_plate()
    {
        Assert.True(Off("SquaringShear"));
        Assert.False(SeraphHorizons.Mod.SquaringShear.SquaringShearSystem.Applies(World.Api));
        Assert.DoesNotContain(W.Blocks, b => b?.Code is { Domain: "seraphhorizons" } c && c.Path.StartsWith("squaringshear"));
        Assert.DoesNotContain(W.Items, i => i?.Code is { Domain: "seraphhorizons" } c && c.Path.StartsWith("halfplate"));
        Assert.DoesNotContain(W.GridRecipes, r => r.Output?.Code?.Path?.StartsWith("squaringshear") == true);
        Assert.NotNull(W.GetItem(new AssetLocation(SeraphHorizons.Mod.SquaringShear.Core.Cutting.LeadPlate)));
        foreach (var code in SeraphHorizons.Mod.SquaringShear.Core.SquaringShearParts.GaugeCodes)
            Assert.NotNull(W.GetItem(new AssetLocation(code)));
        var linked = Lang.AvailableLanguages["en"].GetAllEntries()
            .Where(e => e.Key.StartsWith("seraphhorizons:", StringComparison.Ordinal)
                        && System.Text.RegularExpressions.Regex.IsMatch(e.Value,
                            "handbook://(block-seraphhorizons:squaringshear|item-seraphhorizons:halfplate)|handbooksearch://(squaring shear|half plate)"))
            .Select(e => e.Key).ToList();
        Assert.True(linked.Count == 0, "Still link the squaring shear or the half plate: " + string.Join(", ", linked));
        Assert.Contains("squaring shear", Lang.Get("seraphhorizons:halfplate-handbook-text"));
        var logged = World.BootDiagnostics
            .Where(e => e.Level is EnumLogType.Warning or EnumLogType.Error or EnumLogType.Fatal)
            .Where(e => e.Message.Contains("squaringshear", StringComparison.OrdinalIgnoreCase)
                        || e.Message.Contains("squaring shear", StringComparison.OrdinalIgnoreCase)
                        || e.Message.Contains("halfplate", StringComparison.OrdinalIgnoreCase))
            .Select(e => $"[{e.Level}] {e.Message}")
            .ToList();
        Assert.True(logged.Count == 0, "Logged:\n" + string.Join("\n", logged));
    }

    /// <summary><c>MandrelStation</c>: no mandrel station blocks and no recipe for its frame, no link
    /// to it in the mod's own text, and nothing logged about it. The hollows it forges and the pipe
    /// sections it makes (UnifiedPipes') exist either way.</summary>
    [AtlasScenario]
    public void Mandrel_station_off_there_is_no_mandrel_station()
    {
        Assert.True(Off("MandrelStation"));
        Assert.False(SeraphHorizons.Mod.MandrelStation.MandrelStationSystem.Applies(World.Api));
        Assert.DoesNotContain(W.Blocks, b => b?.Code is { Domain: "seraphhorizons" } c && c.Path.StartsWith("mandrelstation"));
        Assert.DoesNotContain(W.GridRecipes, r => r.Output?.Code?.Path?.StartsWith("mandrelstation") == true);
        Assert.NotNull(W.GetItem(new AssetLocation(SeraphHorizons.Mod.MandrelStation.Core.Forging.CopperHollow)));
        var linked = Lang.AvailableLanguages["en"].GetAllEntries()
            .Where(e => e.Key.StartsWith("seraphhorizons:", StringComparison.Ordinal)
                        && System.Text.RegularExpressions.Regex.IsMatch(e.Value, "handbook://block-seraphhorizons:mandrelstation|handbooksearch://mandrel station"))
            .Select(e => e.Key).ToList();
        Assert.True(linked.Count == 0, "Still link the mandrel station: " + string.Join(", ", linked));
        var logged = World.BootDiagnostics
            .Where(e => e.Level is EnumLogType.Warning or EnumLogType.Error or EnumLogType.Fatal)
            .Where(e => e.Message.Contains("mandrelstation", StringComparison.OrdinalIgnoreCase)
                        || e.Message.Contains("mandrel station", StringComparison.OrdinalIgnoreCase))
            .Select(e => $"[{e.Level}] {e.Message}")
            .ToList();
        Assert.True(logged.Count == 0, "Logged:\n" + string.Join("\n", logged));
    }

    /// <summary><c>Handcar</c>: no handcar entity, item or recipe, the riders' animations not added to
    /// the seraph or the player, the drive not patched into Yang's, and nothing logged about it.</summary>
    [AtlasScenario]
    public void Handcar_off_there_is_no_handcar()
    {
        Assert.True(Off("Handcar"));
        var handcars = SeraphHorizons.Mod.Handcar.HandcarSystem.Of(World.Api);
        Assert.False(handcars.Enabled);
        Assert.False(World.Api.World.Config.GetBool(SeraphHorizons.Mod.Handcar.HandcarSystem.RunningKey, true));
        Assert.Null(W.GetEntityType(SeraphHorizons.Mod.Handcar.HandcarSystem.EntityCode));
        Assert.DoesNotContain(W.Items, i => i?.Code is { Domain: "seraphhorizons" } c && c.Path == "handcar");
        Assert.DoesNotContain(W.GridRecipes, r => r.Output?.Code?.Path == "handcar");
        var seraph = World.Api.Assets.Get(new AssetLocation("game:shapes/entity/humanoid/seraph-faceless.json")).ToObject<Shape>();
        Assert.DoesNotContain(seraph.Animations, a => a.Code.StartsWith("seraphhorizons-handcar", StringComparison.Ordinal));
        var player = W.GetEntityType(new AssetLocation("game:player"))!;
        Assert.DoesNotContain(player.Client.Animations, m => m.Code.StartsWith("seraphhorizons-handcar", StringComparison.Ordinal));
        Assert.False(Harmony.HasAnyPatches(SeraphHorizons.Mod.Handcar.HandcarPatches.ServerHarmonyId));
        // Yang's own locomotive still drives
        Assert.NotNull(W.GetEntityType(new AssetLocation("yangtransport", "sglocomotive-primitive")));
        var logged = World.BootDiagnostics
            .Where(e => e.Level is EnumLogType.Warning or EnumLogType.Error or EnumLogType.Fatal)
            .Where(e => e.Message.Contains("handcar", StringComparison.OrdinalIgnoreCase))
            .Select(e => $"[{e.Level}] {e.Message}")
            .ToList();
        Assert.True(logged.Count == 0, "Logged:\n" + string.Join("\n", logged));
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

    /// <summary><c>GearConsumers</c>: every recipe takes the gears its mod ships it with, ppex still
    /// smiths and shows its gears, and smex's converter is not patched.</summary>
    [AtlasScenario]
    public void Gear_consumers_off_recipes_take_the_gears_their_mods_ship_with()
    {
        Assert.True(Off("GearConsumers"));
        Assert.False(Harmony.HasAnyPatches(GearConsumers.HarmonyId));
        Assert.DoesNotContain(W.GridRecipes, r => r.Ingredients?.Values.Any(i => i.Code?.ToString() == GearConsumers.StainlessGear) == true);
        // ppex's Cornish engine: one recipe with the rusty gear, its twin with ppex's gears.
        var cornish = W.GridRecipes.Where(r => r.Output?.Code?.ToString() == "ppex:enginecornish-north").ToList();
        // Keyed and resolved ingredients both (GearConsumerUses.Codes): the server drops a grid recipe's
        // keyed ones once recipes are sent to a client, so after a scenario joins a player there are none.
        Assert.Contains(cornish, r => GearConsumerUses.Codes(r).Contains("game:gear-rusty"));
        Assert.Contains(cornish, r => GearConsumerUses.Codes(r).Contains("ppex:gear-*"));
        Assert.Contains(W.GridRecipes, r => r.Output?.Code?.ToString() == "game:glider"
                                            && GearConsumerUses.Codes(r).Contains("game:gear-rusty"));
        Assert.Contains(World.Api.GetSmithingRecipes(), r => r.Output?.Code?.ToString() == "ppex:gear-steel");
        Assert.Contains(World.Api.GetSmithingRecipes(), r => r.Output?.Code?.ToString() == "ppex:largegear-steel");
        var gear = W.GetItem(new AssetLocation("ppex:gear-steel"))!;
        Assert.True(gear.CreativeInventoryTabs is { Length: > 0 });
        Assert.False(gear.Attributes?["handbook"]?["exclude"].AsBool() == true);
        Assert.DoesNotContain("stainless large gear", Lang.GetL("en", "smex:bessemer-err-materials"));
    }

    /// <summary><c>UnifiedPipes</c>: ppex's pipes are as it ships them (iron and steel, from plate and
    /// nails, iron and steel valves), nothing of ppex or exlib is patched, the game's chute section is
    /// copper alone and made on the anvil and from a plate again, and there is no angle, pipe section,
    /// copper, lead or bronze pipe, or recipe of this mod's for them.</summary>
    [AtlasScenario]
    public void UnifiedPipes_off_ppex_pipes_are_as_they_ship()
    {
        Assert.True(Off("UnifiedPipes"));
        Assert.False(SeraphHorizons.Mod.Pipes.UnifiedPipesSystem.Applies(World.Api));
        Assert.False(Harmony.HasAnyPatches(SeraphHorizons.Mod.Pipes.UnifiedPipesSystem.HarmonyId));
        string[] added = ["copper", "lead", "tinbronze", "bismuthbronze", "blackbronze"];
        Assert.DoesNotContain(W.Blocks, b => b?.Code is { Domain: "ppex" } c && c.Path.StartsWith("pipe-") && added.Contains(b.Variant?["material"]));
        Assert.DoesNotContain(W.Items, i => i?.Code is { Domain: "seraphhorizons" } c && (c.Path.StartsWith("angle-") || c.Path.StartsWith("pipesection-")));
        Assert.Equal(["game:chutesection-copper"], W.Items.Where(i => i?.Code is { Domain: "game" } c && c.Path.StartsWith("chutesection-"))
            .Select(i => i.Code.ToString()));
        Assert.Contains(W.GridRecipes, r => r.Output?.Code?.ToString() == "game:chutesection-copper"
                                            && r.ResolvedIngredients.Any(i => i?.Code?.ToString() == "game:metalplate-copper"));
        Assert.DoesNotContain(W.GridRecipes, r => r.Name?.Domain == "seraphhorizons" && r.Output?.Code?.Domain == "ppex"
                                                  && r.Output.Code.Path.StartsWith("pipe-"));
        Assert.DoesNotContain(W.GridRecipes, r => r.Name?.Domain == "seraphhorizons" && r.Output?.Code?.Path.StartsWith("chutesection-") == true);
        Assert.DoesNotContain(World.Api.GetSmithingRecipes(), r => r.Output?.Code?.Path.StartsWith("angle-") == true);
        Assert.Contains(World.Api.GetSmithingRecipes(), r => r.Output?.Code?.ToString() == "game:chutesection-copper");
        // the game's chutes from sections alone again, and Better Ruins' blueprint chutes back
        Assert.Contains(W.GridRecipes, r => r.Output?.Code?.ToString() == "game:chute-straight-ns"
                                            && r.ResolvedIngredients.Where(i => i != null).All(i => i.Code?.ToString() == "game:chutesection-copper"));
        Assert.DoesNotContain(W.GridRecipes, r => r.Output?.Code?.Path.StartsWith("chute-") == true
                                                  && r.ResolvedIngredients.Any(i => i?.Code?.Path.StartsWith("solderbar-") == true));
        Assert.Contains(W.GridRecipes, r => r.Output?.Code?.ToString() == "game:chute-straight-ns"
                                            && r.ResolvedIngredients.Any(i => i?.Code?.ToString() == "betterruins:br-schematic-mechanical"));
        // ppex's plate-and-nails straight pipe and its iron and steel valves are made again
        Assert.Contains(W.GridRecipes, r => r.Output?.Code?.ToString() == "ppex:pipe-straight-ns-iron" && r.Name?.Domain == "ppex");
        Assert.Contains(W.GridRecipes, r => r.Output?.Code?.ToString() == "ppex:pipe-valve-sn-steel" && r.Name?.Domain == "ppex");
        Assert.Contains("<strong>iron</strong> pipe bursts above <strong>5 atm</strong>", Lang.GetL("en", "ppex:handbook-steampower-text"));
        Assert.Equal(5f, (float)W.GetBlock(new AssetLocation("ppex:pipe-straight-ns-iron"))!.GetType().GetProperty("BurstPressure")!.GetValue(
            W.GetBlock(new AssetLocation("ppex:pipe-straight-ns-iron")))!);
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

    /// <summary><c>HeatingRackStandsOnBlock</c>: nothing is patched, and a heating rack placed from a
    /// stack onto a floor's top face goes in the cell over it, legs in the floor, as Logging Expanded
    /// ships it.</summary>
    [AtlasScenario]
    public async Task Heating_rack_stands_on_block_off_the_rack_goes_in_the_cell_over_the_floor()
    {
        Assert.True(Off("HeatingRackStandsOnBlock"));
        Assert.False(Harmony.HasAnyPatches(HeatingRackPlacement.HarmonyId));
        var shop = await Woodshop.Open(World, World.Spawn.AddCopy(80, 3, 110));
        var to = shop.Cell(2);
        // Open's floor can be lost in a chunk nothing had loaded; lay it again with the player there.
        World.SetBlock("game:rock-granite", to.DownCopy());
        await World.Ticks(2);

        var stack = new ItemStack(W.GetBlock(new AssetLocation("loggingmod:resinrack-fire-north")));
        var sel = new BlockSelection { Position = to.Copy(), Face = BlockFacing.UP, HitPosition = new Vec3d(0.5, 1, 0.5), DidOffset = true };
        string failure = "";
        shop.Holding(stack);
        Assert.True(stack.Block.TryPlaceBlock(W, shop.P, stack, sel, ref failure), $"not placed: {failure}");
        shop.Holding(null);
        await World.Ticks(2);

        Assert.Equal(to, sel.Position);
        Assert.StartsWith("loggingmod:resinrack-fire-", World.BlockAt(to).Code.ToString());
        Assert.Equal("game:air", World.BlockAt(to.UpCopy()).Code.ToString());
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

    /// <summary><c>BloodSausageInMixingBowl</c>: Butchering's grid recipes for raw blood sausage
    /// (three) and raw black pudding (one) as it ships them.</summary>
    [AtlasScenario]
    public void Blood_sausage_in_mixing_bowl_off_the_grid_recipes_are_as_they_ship()
    {
        Assert.True(Off("BloodSausageInMixingBowl"));
        var grid = BloodSausages.GridRecipes(W);
        Assert.Equal(3, grid.Count(r => r.Output?.Code?.ToString() == BloodSausage.BloodSausageRaw));
        Assert.Equal(1, grid.Count(r => r.Output?.Code?.ToString() == BloodSausage.BlackPuddingRaw));
    }

    /// <summary><c>DuplicateRecipes</c>: every duplicate as its mod ships it, Expanded Foods' offal-free
    /// sausages and scrap brazier, Material Needs' aged wood recipes, the game's barrel cottage cheese
    /// and its sandstone daub giving 8.</summary>
    [AtlasScenario]
    public void Duplicate_recipes_off_the_duplicates_are_as_they_ship()
    {
        Assert.True(Off("DuplicateRecipes"));
        var api = (Vintagestory.API.Server.ICoreServerAPI)World.Api;
        var sausages = Duplicates.SausageRecipes(api);
        foreach (var file in Duplicates.EfSausageFiles)
            Assert.Contains(sausages, f => f.Name?.ToString() == file && !Duplicates.TakesOffal(f));
        Assert.Contains(W.GridRecipes, r => Duplicates.Out(r) == Duplicates.ScrapBrazier && Duplicates.From(r) == Duplicates.EfBrazierFile);
        foreach (var output in Duplicates.AgedOutputs)
            Assert.Contains(W.GridRecipes, r => Duplicates.Out(r) == output && Duplicates.FromMaterialNeeds(r));
        Assert.Contains(W.GridRecipes, r => Duplicates.VeryAgedIronShield(r) && Duplicates.FromMaterialNeeds(r));
        Assert.Contains(World.Api.GetBarrelRecipes(), r => r.Output?.Code?.ToString() == Duplicates.CottageCheese);
        Assert.Contains(W.GridRecipes, r => Duplicates.SandstoneDaub(r) && r.Output.Quantity == 8);
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

    /// <summary><c>TrunkEntities</c>: Logging Expanded's trunks are items as it ships them (its
    /// storage flag, its rack's Carryable, Cartwright's carts as they ship, no tool behaviour, no
    /// placement or carry patches), and a trunk entity from a world that ran them turns back into a
    /// trunk item as it loads.</summary>
    [AtlasScenario]
    public async Task Trunk_entities_off_trunks_are_items_and_trunk_entities_turn_back_into_them()
    {
        Assert.True(Off("TrunkEntities"));
        var mod = TrunkEntitySystem.Of(World.Api);
        Assert.False(mod.Enabled);
        Assert.False(W.Config.GetBool(TrunkEntitySystem.RunningKey, true));
        Assert.False(TrunkCarry.Available(World.Api));

        // the trunks' storage flag is Logging Expanded's (backpack only, its default)
        var trunkBlock = W.GetBlock(new AssetLocation("loggingmod:treetrunk-oak-md-no-north"))!;
        Assert.Equal(EnumItemStorageFlags.Backpack, trunkBlock.StorageFlags);
        // the Trunk Storage Rack keeps Logging Expanded's Carryable
        var rack = W.Blocks.First(b => b?.Code is { Domain: "loggingmod" } c && c.Path.StartsWith("trunkstorage-"));
        Assert.Contains(rack.BlockBehaviors, TrunkCarry.IsCarryable);
        // Cartwright's carts and sleds as the two mods make them: Carry On's own carryonmore patch
        // gives them one attachablecarryable, and the feature adds none
        var carts = W.EntityTypes.Where(t => t.Code.Domain == "cartwrightscaravan"
                                             && (t.Code.Path.StartsWith("cart-") || t.Code.Path.StartsWith("sled"))).ToList();
        Assert.NotEmpty(carts);
        foreach (var type in carts)
            Assert.True(type.Server.BehaviorsAsJsonObj.Count(b => b["code"].AsString() == "carryon:attachablecarryable") <= 1, $"{type.Code}");
        // no tool works a trunk, and nothing of the feature is patched in
        foreach (var code in new[] { "game:axe-felling-iron", "game:knife-generic-iron", "game:saw-iron" })
            Assert.Null(W.GetItem(new AssetLocation(code))!.GetCollectibleBehavior<TrunkToolBehavior>(true));
        foreach (var id in new[] { OldTrunkBlocks.PlaceHarmonyId, TrunkCarry.HarmonyId, TrunkToolsSystem.HarmonyId, TrunkEntitySystem.HarmonyId })
            Assert.False(Harmony.HasAnyPatches(id), $"{id} patched something");
        Assert.DoesNotContain(W.GetEntityType(TrunkEntitySystem.ThinCode)!.Server.BehaviorsAsJsonObj,
            b => b["code"].AsString() == TrunkCarry.BehaviorCode);

        var origin = World.Spawn.AddCopy(-40, 40, -40);
        await World.Until(() => W.BlockAccessor.GetChunkAtBlockPos(origin) != null, 30000);
        int granite = W.GetBlock(new AssetLocation("game:rock-granite"))!.Id;
        for (int x = -4; x <= 4; x++)
        for (int z = -4; z <= 4; z++)
        {
            W.BlockAccessor.SetBlock(granite, origin.AddCopy(x, -1, z));
            for (int y = 0; y <= 4; y++)
                W.BlockAccessor.SetBlock(0, origin.AddCopy(x, y, z));
        }
        ItemStack Trunk(int logs)
        {
            var stack = new ItemStack(trunkBlock);
            var slots = new Vintagestory.API.Datastructures.TreeAttribute();
            slots["0"] = new Vintagestory.API.Datastructures.ItemstackAttribute(new ItemStack(W.GetBlock(new AssetLocation("game:log-placed-oak-ud"))!, logs));
            stack.Attributes["slots"] = slots;
            return stack;
        }
        List<Entity> Around(System.Func<Entity, bool> match) =>
            W.GetEntitiesAround(origin.ToVec3d().Add(0.5, 0.5, 0.5), 8, 8, e => e.Alive && match(e)).ToList();

        // a trunk item stays an item
        W.SpawnItemEntity(Trunk(7), origin.ToVec3d().Add(0.5, 0.2, 0.5));
        await World.Ticks(5);
        Assert.Empty(Around(e => e is EntityTrunk));
        var item = Assert.IsType<EntityItem>(Assert.Single(Around(e => e is EntityItem i && Trunks.IsTrunk(i.Itemstack))));
        Assert.Equal(7, Trunks.StoredLogs(item.Itemstack, W));
        item.Die(EnumDespawnReason.Removed);

        // a trunk entity (as a world that ran them saved it) becomes the trunk item it holds
        var entity = TrunkSpawns.Spawn(W, Trunk(12), origin.ToVec3d().Add(0.5, 0, 0.5), 0);
        Assert.NotNull(entity);
        await World.Ticks(5);
        Assert.False(entity.Alive);
        Assert.Empty(Around(e => e is EntityTrunk));
        item = Assert.IsType<EntityItem>(Assert.Single(Around(e => e is EntityItem i && Trunks.IsTrunk(i.Itemstack))));
        Assert.Equal(12, Trunks.StoredLogs(item.Itemstack, W));
        Assert.Equal("loggingmod:treetrunk-oak-md-no-north", item.Itemstack.Collectible.Code.ToString());
        item.Die(EnumDespawnReason.Removed);
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
        // So the recipe export gets no Tidy Variants group (and, given no items, no handbook group either).
        Assert.Null(SeraphHorizons.RecipeExport.Recipes.VariantGroups.Build((ICoreServerAPI)World.Api, new JObject()));

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
        // machine oil's and gear reclamation's pages, whose switches are off here too).
        Assert.Equal(WoodworkingGuidePages.Pages.Select(p => (p.PageCode, p.TitleKey()))
                .Append((MachineOilSystem.GuidePageCode, MachineOilSystem.GuideTitleKey))
                .Append((GearReclamationSystem.GuidePageCode, GearReclamationSystem.GuideTitleKey)).Order(),
            ((IEnumerable<(string, string)>)World.Api.ObjectCache[WoodworkingGuide.HiddenGuidesKey]).Order());
    }

    [AtlasScenario]
    public void Unified_woodworking_off_the_export_lists_the_two_mods_guides()
    {
        Assert.True(Off("UnifiedWoodworking"));
        var guides = Exporter.Guides((ICoreServerAPI)World.Api).OfType<JObject>().ToList();
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

    /// <summary><c>RarerBattleTowers</c>: Battle Towers' three towers at the chances and spacings
    /// it ships (1.1.0).</summary>
    [AtlasScenario]
    public void Rarer_battle_towers_off_towers_are_as_Battle_Towers_ships_them()
    {
        Assert.True(Off("RarerBattleTowers"));
        var structures = Vintagestory.API.Datastructures.JsonObject.FromJson(
                World.Api.Assets.Get(new AssetLocation("game", "worldgen/structures.json")).ToText())
            ["structures"].AsArray()!;
        foreach (var (code, chance, distance) in new[]
                 { ("surfacetowers", 0.03, 200), ("surfacehardtowers", 0.005, 1000), ("undergroundtowers", 200.0, 50) })
        {
            var tower = Assert.Single(structures, s => s["code"].AsString() == code);
            Assert.Equal(chance, tower["chance"].AsDouble(), 6);
            Assert.Equal(distance, tower["minGroupDistance"].AsInt());
        }
    }

    /// <summary><c>RuinsOnMedianGround</c>: nothing is patched (no seating, no foundation), and a
    /// surface ruin sits on the lowest sample, as the game places it.</summary>
    [AtlasScenario]
    public void Ruins_on_median_ground_off_a_surface_ruin_sits_on_the_lowest_sample()
    {
        Assert.True(Off("RuinsOnMedianGround"));
        Assert.False(RuinSurfaceMedian.Patched);
        Assert.False(Harmony.GetPatchInfo(RuinSurfaceMedian.Target)?.Transpilers.Any(p => p.owner == SeraphHorizonsSystem.HarmonyId) ?? false);
        Assert.False(Harmony.GetPatchInfo(RuinSurfaceMedian.Target)?.Postfixes.Any(p => p.owner == SeraphHorizonsSystem.HarmonyId) ?? false);
        var (low, high) = RuinSeating.Heights(W);
        Assert.Equal(low, RuinSeating.SeatedHeight(W, low, high));
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

    /// <summary><c>GearReclamation</c>: the gear items exist, but none of the steps' recipes, no
    /// salvage section on the rusty gear, and a neutralized gear in a hand stays as it is.</summary>
    [AtlasScenario]
    public async Task Gear_reclamation_off_no_recipes_no_roll()
    {
        Assert.True(Off("GearReclamation"));
        foreach (var code in new[] { GearCodes.Stainless, GearCodes.Degreased, GearCodes.Pickled, GearCodes.Passivated, GearCodes.Neutralized, GearCodes.LargeStainless })
            Assert.NotNull(W.GetItem(new AssetLocation(code)));
        Assert.DoesNotContain(World.Api.GetCookingRecipes(), r => r.Code.StartsWith("seraphhorizons-gear-"));
        Assert.DoesNotContain(World.Api.GetBarrelRecipes(), r => r.Code.StartsWith("seraphhorizons-gear-"));
        Assert.False(W.GetItem(new AssetLocation(GearCodes.Rusty))!.Attributes["handbook"].Exists);
        var p = await World.JoinPlayer("nogears");
        var hand = p.Player.InventoryManager.ActiveHotbarSlot;
        hand.Itemstack = new ItemStack(W.GetItem(new AssetLocation(GearCodes.Neutralized))!, 10);
        hand.MarkDirty();
        await World.Ticks(10);
        Assert.Equal(GearCodes.Neutralized, hand.Itemstack?.Collectible.Code.ToString());
        Assert.Equal(0, GearReclamationSystem.Of(World.Api).ResolveAll(p.Player));
        Assert.Equal(10, hand.StackSize);
    }

    /// <summary><c>GearReclamation</c>: no pickling tub and no tub recipe.</summary>
    [AtlasScenario]
    public void Gear_reclamation_off_there_is_no_pickling_tub()
    {
        Assert.True(Off("GearReclamation"));
        Assert.False(W.GetBlock(new AssetLocation("seraphhorizons:picklingtub")) is { Id: > 0 }, "the tub exists");
        Assert.DoesNotContain(W.GridRecipes, r => r.Output?.Code?.ToString() == "seraphhorizons:picklingtub" && r.Enabled);
    }

    /// <summary><c>SteelBitsRecovery</c>: the coffin is not patched and refuses steel bits, there is no
    /// packing recipe and no handbook section, and smex's scrap setting is left alone; packed steel
    /// bits still exist.</summary>
    [AtlasScenario(TimeoutMs = 180_000)]
    public async Task Steel_bits_recovery_off_the_coffin_refuses_steel_bits()
    {
        Assert.True(Off("SteelBitsRecovery"));
        Assert.False(Harmony.HasAnyPatches(SeraphHorizons.Mod.SteelBits.SteelBitsSystem.HarmonyId));
        Assert.False(CoffinSite.Patched(CoffinSite.AddIngotMethod()));
        Assert.Null(SeraphHorizons.Mod.SteelBits.SteelBitsSystem.AddIngot);
        Assert.Equal(SeraphHorizons.Mod.SteelBits.SmexScrap.Status.Off,
            SeraphHorizons.Mod.SteelBits.SteelBitsSystem.Of(World.Api).SmexStatus);
        Assert.DoesNotContain(W.GridRecipes, r => r.Output?.Code?.Equals(SeraphHorizons.Mod.SteelBits.SteelBitsSystem.ChargeCode) == true);
        Assert.False(W.GetItem(SeraphHorizons.Mod.SteelBits.SteelBitsSystem.SteelBit)!.Attributes["handbook"]["extraSections"].Exists);
        Assert.NotNull(W.GetItem(SeraphHorizons.Mod.SteelBits.SteelBitsSystem.ChargeCode));

        var p = await World.JoinPlayer("nocementing");
        var pos = World.Spawn.AddCopy(-180, 12, -300);
        await p.TeleportTo(pos.AddCopy(8, 0, 0));
        var site = new CoffinSite(World, pos, p.Player);
        await site.Build();
        site.AddCoal();
        site.Click(site.Stack(SeraphHorizons.Mod.Core.SteelBitsRules.SteelBit, 40));
        Assert.Equal(0, site.Coffin.IngotCount);
        Assert.Equal(40, site.Hand.StackSize);
        site.Click(site.Stack(CoffinSite.IronIngot, 1));
        Assert.Equal(1, site.Coffin.IngotCount);
    }
}
