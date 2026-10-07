using HarmonyLib;
using SeraphHorizons.Mod.BuckingSawmill.Core;
using SeraphHorizons.Mod.Core;
using SeraphHorizons.Mod.GearCutter.Core;
using SeraphHorizons.Mod.GearReclamation.Core;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.PicklingTub.Core;
using SeraphHorizons.Mod.Rosser;
using SeraphHorizons.Mod.Rosser.Core;
using SeraphHorizons.Mod.TrunkEntities.Core;
using SeraphHorizons.Mod.Woodworking;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace SeraphHorizons.Mod;

/// <summary>
/// The pack's own tweaks. Each tweak is its own class, switched by its own setting in
/// ModConfig/seraphhorizons.json, and skipped with a log line when the mod it changes is not
/// installed or no longer looks as expected. This system applies its own patches explicitly
/// (never PatchAll). Tidy Variants (TidyVariants/), Map Reveal (MapReveal/), the creative mod
/// tabs (CreativeModTabs/), the creative search tweaks (CreativeSearch/), the machines and trunk
/// entities (TrunkEntities/) have their own mod systems, which read their switch through
/// <see cref="ConfigFor"/>.
/// </summary>
public class SeraphHorizonsSystem : ModSystem
{
    public const string HarmonyId = "seraphhorizons";
    public const string ConfigFile = "seraphhorizons.json";

    private SeraphHorizonsConfig? _config;
    private Harmony? _harmony;
    // Its own id: in singleplayer the server's instance of this system unpatches HarmonyId.
    private Harmony? _clientHarmony;
    private bool _ageOfFlax;
    private bool _tunRack;
    private bool _irrigationVessel;
    private bool _barrelRackKegs;
    private bool _debarkedTrunks;
    // Its own id, patched once per process: both sides need it, and singleplayer runs both in one.
    private Harmony? _barrelRackHarmony;
    private Harmony? _heatingRackHarmony;
    // Its own id, patched once per process, as the barrel rack's: the client checks the hotbar too.
    private Harmony? _gearConsumersHarmony;
    private bool _gearConsumers;
    private UnifiedWoodworking? _woodworking;
    // Client side only: Carry On's icon stack fields, cleared again when the client leaves the world.
    private List<System.Reflection.FieldInfo>? _carryOnIconFields;

    /// <summary>Whether Logging Expanded's trunk has its debarked state on this side (the
    /// <c>Rosser</c> switch on and the patch bound); decided in <see cref="Start"/>. The rosser
    /// (<see cref="RosserSystem"/>) exists only with it.</summary>
    public bool DebarkedTrunksOn => _debarkedTrunks;

    /// <summary>The unified woodworking tweak on this side; set in <see cref="Start"/>.</summary>
    public UnifiedWoodworking Woodworking => _woodworking!;

    /// <summary>This side's settings. Loaded on first use (this system's <see cref="Start"/> at the
    /// latest), so another system of the mod can read them in any phase.</summary>
    public static SeraphHorizonsConfig ConfigFor(ICoreAPI api) =>
        api.ModLoader.GetModSystem<SeraphHorizonsSystem>() is { } system ? system.Config(api) : new SeraphHorizonsConfig();

    private SeraphHorizonsConfig Config(ICoreAPI api) => _config ??= LoadConfig(api);

    public override void Start(ICoreAPI api)
    {
        Config(api);
        CreativeSteamSource.RegisterClasses(api);
        AssembledMachines.RegisterClasses(api);
        // Before the game's patch loader, which applies the patches in AssetsLoaded. On the server
        // only: a client has no assets in Start (the game throws on reading one), and the patch
        // files change what the server loads.
        void DisablePatches(Action<ICoreAPI> disable)
        {
            if (api.Side == EnumAppSide.Server)
                disable(api);
        }
        _ageOfFlax = Config(api).AgeOfFlaxRebalance && AgeOfFlaxRebalance.Applies(api)
                     && AgeOfFlaxRebalance.Bind(api.Logger);
        if (!_ageOfFlax)
            DisablePatches(AgeOfFlaxRebalance.DisablePatches);
        if (!(Config(api).FoodHydration && FoodHydration.Applies(api)))
            DisablePatches(FoodHydration.DisablePatches);
        if (!(Config(api).HydrateTunRetired && HydrateTun.Applies(api)))
            DisablePatches(HydrateTun.DisablePatches);
        _irrigationVessel = Config(api).IrrigationVesselRetired && IrrigationVessel.Applies(api);
        if (!_irrigationVessel)
            DisablePatches(IrrigationVessel.DisablePatches);
        if (!(Config(api).BloodSausageInMixingBowl && BloodSausage.Applies(api)))
            DisablePatches(BloodSausage.DisablePatches);
        if (!(Config(api).DurableSawmillBlades && SawmillBladeDurability.Applies(api)))
            DisablePatches(SawmillBladeDurability.DisablePatches);
        if (!(Config(api).IronWoodworkingMachines && WoodworkingMachineCosts.Applies(api)))
            DisablePatches(WoodworkingMachineCosts.DisablePatches);
        _debarkedTrunks = Config(api).Rosser && DebarkedTrunks.Applies(api) && DebarkedTrunks.Bind(api);
        if (!_debarkedTrunks)
            DisablePatches(DebarkedTrunks.DisablePatches);
        _tunRack = Config(api).LargerTunRack && TunRackCapacity.Applies(api) && TunRackCapacity.Bind(api.Logger);
        if (!_tunRack)
            DisablePatches(TunRackCapacity.DisablePatches);
        _barrelRackKegs = Config(api).BarrelRackKegs && BarrelRackKegs.Applies(api) && BarrelRackKegs.Bind(api.Logger);
        if (_barrelRackKegs)
            BarrelRackKegs.Patch(_barrelRackHarmony = new Harmony(BarrelRackKegs.HarmonyId));
        else
            DisablePatches(BarrelRackKegs.DisablePatches);
        if (Config(api).HeatingRackKeepsPosition && HeatingRackPosition.Applies(api) && HeatingRackPosition.Bind(api.Logger))
            HeatingRackPosition.Patch(_heatingRackHarmony = new Harmony(HeatingRackPosition.HarmonyId));
        _gearConsumers = Config(api).GearConsumers;
        if (!_gearConsumers)
            DisablePatches(GearConsumers.DisablePatches);
        else if (GearConsumers.BessemerApplies(api) && GearConsumers.Bind(api.Logger))
            GearConsumers.Patch(_gearConsumersHarmony = new Harmony(GearConsumers.HarmonyId));
        // Registers its classes whatever the setting; on the server, decides whether it runs and
        // tells clients; sets the two mods' settings and patches. Last, and it catches its own
        // failures, so nothing above depends on it.
        _woodworking = new UnifiedWoodworking();
        _woodworking.Start(api, Config(api).UnifiedWoodworking);
    }

    // Behavior changes run on the server only: that is where the tweaked mods simulate.
    public override void StartServerSide(ICoreServerAPI api)
    {
        if (Config(api).BoilerLidBlowsOpen && BoilerLidRelief.Applies(api))
        {
            _harmony ??= new Harmony(HarmonyId);
            BoilerLidRelief.Patch(_harmony, api.Logger);
        }
        if (_ageOfFlax)
        {
            _harmony ??= new Harmony(HarmonyId);
            AgeOfFlaxRebalance.Patch(_harmony);
        }
        if (Config(api).ChopperDropsInFront && ChopperOutput.Applies(api))
        {
            _harmony ??= new Harmony(HarmonyId);
            ChopperOutput.Patch(_harmony, api.Logger);
        }
        if (Config(api).GearboxSourceRatio && GearboxSourceRatio.Applies(api))
            GearboxSourceRatio.Patch(_harmony ??= new Harmony(HarmonyId), api.Logger);
        if (_tunRack)
            TunRackCapacity.Patch(_harmony ??= new Harmony(HarmonyId));
        if (_debarkedTrunks)
            DebarkedTrunks.Patch(_harmony ??= new Harmony(HarmonyId));
        ClearSky = new ClearSky(api);
        if (Config(api).ClearCommand)
            ClearSky.Register(_harmony ??= new Harmony(HarmonyId));
        else
            ClearSky.ReleaseLeftoverLock();
    }

    /// <summary>The <c>/clear</c> command (server side).</summary>
    public ClearSky? ClearSky { get; private set; }

    // Cart reach acts where the player picks what is under the crosshair: the client. Entity types
    // arrive from the server, so the matching ones are known once the level is finalized.
    public override void StartClientSide(ICoreClientAPI api)
    {
        if (Config(api).CartReach)
            api.Event.LevelFinalize += () => PatchCartReach(api);
        if (Config(api).CarryOnIconsPerWorld && CarryOnIcons.Find(api) is { } iconFields)
        {
            _carryOnIconFields = iconFields;
            CarryOnIcons.Clear(iconFields);
            api.Logger.Notification("[seraphhorizons] Carry On icons: {0} cached icon stack fields are cleared when the world is left",
                iconFields.Count);
        }
    }

    private void PatchCartReach(ICoreClientAPI api)
    {
        var rule = new EntityReach(Config(api).CartReachEntities);
        var types = CartReach.MatchingTypes(api.World, rule);
        if (types.Count == 0)
        {
            api.Logger.Notification("[seraphhorizons] Cart reach: no entity type matches {0}; nothing to do",
                string.Join(", ", rule.Patterns));
            return;
        }
        _clientHarmony ??= new Harmony(CartReach.HarmonyId);
        if (CartReach.Patch(_clientHarmony, api, rule))
            api.Logger.Notification("[seraphhorizons] Cart reach: far selection boxes of {0} entity types are in reach",
                types.Count);
    }

    // Lang files are loaded, mod assets included, before this phase on both sides.
    // Blocktypes are read from the assets later in this phase (the game's loader runs at 0.2, this
    // system at the default 0.1), on the server only: clients get the blocks from the server.
    public override void AssetsLoaded(ICoreAPI api)
    {
        if (Config(api).BoilerLidBlowsOpen && BoilerLidRelief.Applies(api))
            BoilerLidRelief.RewriteText(api.Logger);
        if (Config(api).ChimneyVentingExplained && ChimneyVentText.Applies(api))
            ChimneyVentText.RewriteText(api.Logger);
        if (Config(api).WellShaftExplained && WellShaftText.Applies(api))
            WellShaftText.RewriteText(api.Logger);
        if (_ageOfFlax)
        {
            LangText.Apply(AgeOfFlaxRebalance.LangEdits, AgeOfFlaxRebalance.ModId, api.Logger);
            if (api.Side == EnumAppSide.Server)
                AgeOfFlaxRebalance.AddSeedsToBlocktype(api);
        }
        if (api.Side == EnumAppSide.Server
            && !(Config(api).CreativeSteamSource && CreativeSteamSource.Applies(api) && CreativeSteamSource.Bind(api.Logger)))
            CreativeSteamSource.Disable(api);
        if (api.Side == EnumAppSide.Server && Config(api).AssembledMachinesInCreative && AssembledMachines.Applies(api))
            AssembledMachines.AddToBlocktypes(api);
        if (api.Side == EnumAppSide.Server && _irrigationVessel)
            IrrigationVessel.DropFromRuinLoot(api);
        if (api.Side == EnumAppSide.Server && !Config(api).GearBlanks)
            Gears.GearBlanks.Disable(api);
        if (Config(api).PanningDropsTrimmed)
        {
            if (api.Side == EnumAppSide.Server)
                PanningDrops.TrimPanAsset(api);
            PanningDrops.RewriteText(api);
        }
        if (Config(api).IronWoodworkingMachines && WoodworkingMachineCosts.Applies(api))
            LangText.Apply(WoodworkingMachineCosts.LangEdits, WoodworkingMachineCosts.ModId, api.Logger);
        if (_barrelRackKegs)
            LangText.Apply(BarrelRackKegs.LangEdits, BarrelRackKegs.FoodShelvesId, api.Logger);
        if (_gearConsumers && GearConsumers.BessemerApplies(api))
            LangText.Apply(GearConsumers.LangEdits, GearConsumers.SmexId, api.Logger);
        _woodworking?.AssetsLoaded(api);
    }

    public override void AssetsFinalize(ICoreAPI api) => _woodworking?.AssetsFinalize(api);

    public override void Dispose()
    {
        _woodworking?.Dispose();
        _woodworking = null;
        _harmony?.UnpatchAll(HarmonyId);
        _harmony = null;
        if (ClearSky != null)
        {
            ClearSky.Unbind();
            ClearSky = null;
        }
        if (_clientHarmony != null)
        {
            _clientHarmony.UnpatchAll(CartReach.HarmonyId);
            _clientHarmony = null;
            CartReach.Unbind();
        }
        _barrelRackHarmony?.UnpatchAll(BarrelRackKegs.HarmonyId);
        _barrelRackHarmony = null;
        _heatingRackHarmony?.UnpatchAll(HeatingRackPosition.HarmonyId);
        _heatingRackHarmony = null;
        _gearConsumersHarmony?.UnpatchAll(GearConsumers.HarmonyId);
        _gearConsumersHarmony = null;
        if (_carryOnIconFields != null)
        {
            CarryOnIcons.Clear(_carryOnIconFields);
            _carryOnIconFields = null;
        }
    }

    private static SeraphHorizonsConfig LoadConfig(ICoreAPI api)
    {
        SeraphHorizonsConfig? config = null;
        try
        {
            config = api.LoadModConfig<SeraphHorizonsConfig>(ConfigFile);
        }
        catch (Exception e)
        {
            api.Logger.Error($"[seraphhorizons] Could not read ModConfig/{ConfigFile}, using the defaults: {e.Message}");
            return new SeraphHorizonsConfig();
        }
        config ??= new SeraphHorizonsConfig();
        // Writes back settings added since the file was made.
        api.StoreModConfig(config, ConfigFile);
        return config;
    }
}

public class SeraphHorizonsConfig
{
    /// <summary>Pipes and Power Expanded: an over-pressured boiler blows its lid open instead of
    /// exploding.</summary>
    public bool BoilerLidBlowsOpen { get; set; } = true;

    /// <summary>Tidy Variants: hides orientation and open/closed variants and groups the rest in
    /// the creative inventory and the handbook (client side; off means both stay vanilla).</summary>
    public bool TidyVariants { get; set; } = true;

    /// <summary>Creative inventory: a button over the right-hand tabs flips to one tab per mod, holding
    /// every creative-listed stack of that mod, and back (both sides: the server adds the tabs and decides
    /// their contents, the client shows them; off means the tabs and the button do not exist).</summary>
    public bool CreativeModTabs { get; set; } = true;

    /// <summary>Pipes and Power Expanded: a creative-only block that fills the pipes connected to it
    /// with steam, set up like the auto rotor (off means the block does not exist).</summary>
    public bool CreativeSteamSource { get; set; } = true;

    /// <summary>Age of Flax (fork): seeds drop from the flax plant again, not the ripple; the
    /// ripple's grain and the hatchel's fibers per ripe plant are 2/3, 1 and 4/3 of vanilla flax's
    /// by tool tier; the advanced tools take steel; every break takes raw or rendered fat; and its
    /// text says so.</summary>
    public bool AgeOfFlaxRebalance { get; set; } = true;

    /// <summary>Pipes and Power Expanded: the Fittings handbook page says which blocks a chimney
    /// vents a pipe network through, and that one on a plain pipe only caps it (text only).</summary>
    public bool ChimneyVentingExplained { get; set; } = true;

    /// <summary>Cartwright's Caravan: the slots at a cart's far end can be used from there, as
    /// the ones near its middle can (client side). The picking range is unchanged: it is measured
    /// to the slot instead of to the cart's origin, for the entities in
    /// <see cref="CartReachEntities"/>.</summary>
    public bool CartReach { get; set; } = true;

    /// <summary>Entity codes that <see cref="CartReach"/> applies to (<c>domain:path</c>, <c>*</c>
    /// wildcards): Cartwright's carts, sleds and market stalls.</summary>
    public string[] CartReachEntities { get; set; } = ["cartwrightscaravan:*"];

    /// <summary>Carry On: its interaction help icons are built again in every world of a client run,
    /// so a second world does not crash the client drawing the first world's (#401, client side;
    /// off means Carry On keeps them, as it ships).</summary>
    public bool CarryOnIconsPerWorld { get; set; } = true;

    /// <summary>Hydrate or Diedrate: foods it gives no hydration (vanilla, Biodiversity: Crops,
    /// Expanded Foods and Primitive Survival ones) get a value modelled on a similar food's
    /// (server side; off means they stay at 0).</summary>
    public bool FoodHydration { get; set; } = true;

    /// <summary>Hydrate or Diedrate: the Wells handbook page says a deep well's shaft must be one
    /// block wide with solid walls, and how the wall blocks cap what it holds (text only).</summary>
    public bool WellShaftExplained { get; set; } = true;

    /// <summary>Immersive Woodworking: the chopper and the sawmill are in the creative inventory
    /// assembled, with a steel head or blade kit, next to their empty frames.</summary>
    public bool AssembledMachinesInCreative { get; set; } = true;

    /// <summary>Immersive Woodworking: the powered chopper drops its output gently in the cell in
    /// front of its output side, as the sawmill does, instead of throwing it two blocks out
    /// (server side).</summary>
    public bool ChopperDropsInFront { get; set; } = true;

    /// <summary>Map Reveal: <c>/revealmap &lt;radius&gt;</c> (creative mode or privilege
    /// controlserver) shows on the caller's world map the terrain already generated within radius
    /// chunks, read from the savegame (off means no command; on a client, nothing is patched).</summary>
    public bool MapReveal { get; set; } = true;

    /// <summary>The admin command <c>/clear</c>: clear weather, no temporal storm and daytime, and
    /// <c>/clear stay</c> / <c>/clear stop</c> to hold it (server side; off means no command, and a
    /// held lock is released).</summary>
    public bool ClearCommand { get; set; } = true;

    /// <summary>Hydrate or Diedrate: its tun has no recipe and is left out of the creative inventory
    /// and the handbook, so Food Shelves' tun rack is the pack's tun; tuns already placed stay and
    /// keep working (server side; off means it is as Hydrate or Diedrate ships it).</summary>
    public bool HydrateTunRetired { get; set; } = true;

    /// <summary>Primitive Survival: its irrigation vessel has no recipe, is left out of the creative
    /// inventory, the handbook and BetterRuins' ruin loot, so Olla's olla is the pack's; vessels
    /// already placed stay and keep working (server side; off means it is as Primitive Survival
    /// ships it).</summary>
    public bool IrrigationVesselRetired { get; set; } = true;

    /// <summary>Butchering: raw blood sausage and raw black pudding have no grid recipe, so they are
    /// made only by Butchering's kneading recipes in A Culinary Artillery's mixing bowl, which it
    /// enables with Expanded Foods (server side; needs all three; off means Butchering's grid
    /// recipes too).</summary>
    public bool BloodSausageInMixingBowl { get; set; } = true;

    /// <summary>Panning gives no wool (Wool), stitching awls or buttons and clasps (Tailor's Delight)
    /// and no uranium nuggets (Expanded Matter); the rest of each mod's panning drops stay (server
    /// side; off means panning is as the mods ship it).</summary>
    public bool PanningDropsTrimmed { get; set; } = true;

    /// <summary>MPE Gearbox: a power source (a rotor, the creative rotor) that creates its network
    /// through a gearbox takes the ratio of the gearbox side it touches, as it does when the gearbox
    /// is placed after it, instead of the far side's (server side; off means a source placed after
    /// its gearbox, or rebuilt after a block on its network is broken, may drive it at the wrong
    /// speed: through a 1:5 gearbox, a fifth of it or five times it).</summary>
    public bool GearboxSourceRatio { get; set; } = true;

    /// <summary>Food Shelves: the tun in a tun rack holds 950 litres, as Hydrate or Diedrate's tun
    /// does, instead of 500 (server side).</summary>
    public bool LargerTunRack { get; set; } = true;

    /// <summary>Food Shelves and Hydrate or Diedrate: the barrel rack takes kegs too, holding a keg's
    /// worth, the liquid moving between the keg and the rack as the keg goes in and out, and
    /// perishing at the keg's own rate times the rack's (both sides; the server's switch decides
    /// what the rack takes).</summary>
    public bool BarrelRackKegs { get; set; } = true;

    /// <summary>Logging Expanded: a Trunk Heating Rack placed from a picked-up stack (Carry On's
    /// client, a creative pick) knows its new position, not the one it was picked up from (both
    /// sides; off means it is as Logging Expanded ships it).</summary>
    public bool HeatingRackKeepsPosition { get; set; } = true;

    /// <summary>Immersive Woodworking + Logging Expanded: one woodworking system. Immersive
    /// Woodworking's chopping block is the splitting block, made in the world with an axe and
    /// upgraded through Logging Expanded's tiers (the chopper takes only the advanced one);
    /// Logging Expanded's sawhorses are the only sawhorses and also saw support beams; Immersive
    /// Woodworking's sawhorse and pit saw and Logging Expanded's splitting logs are retired; one
    /// handbook guide covers it all (off means both mods as they ship). The server's setting
    /// decides; a client follows the server, whatever its own says.</summary>
    public bool UnifiedWoodworking { get; set; } = true;

    /// <summary>Creative inventory: reopening it puts back the search text, and the scroll position
    /// if the same tab is shown, that it had when it was closed, in the same session (client side;
    /// off means it reopens with an empty search at the top, as vanilla does).</summary>
    public bool CreativeKeepsPlace { get; set; } = true;

    /// <summary>A right-click on the creative inventory's search box, or the handbook's, empties it
    /// and leaves it focused for typing (client side; off means a right-click only focuses it).</summary>
    public bool SearchRightClickClears { get; set; } = true;

    /// <summary>The bucking sawmill: a mechanically powered pair of drag saws, built from a frame
    /// and Immersive Woodworking's sawmill parts, that cross-cuts Logging Expanded tree trunks into
    /// logs (needs both mods; off means its blocks and recipe do not exist, and mills already
    /// placed are lost). The server's setting decides.</summary>
    public bool BuckingSawmill { get; set; } = true;

    /// <summary>The bucking sawmill's figures; a value out of range falls back to its default with
    /// a warning. The server's are used.</summary>
    public MillConfig BuckingSawmillSettings { get; set; } = new();

    /// <summary>Immersive Woodworking: its sawmill blade kits last three times as long, in the
    /// bucking sawmill and in its own plank sawmill (server side; off means its own durabilities).</summary>
    public bool DurableSawmillBlades { get; set; } = true;

    /// <summary>Immersive Woodworking: the sawmill's and the chopper's frames and parts take iron,
    /// meteoric iron or steel, and far more nails and strips (off means its own recipes).</summary>
    public bool IronWoodworkingMachines { get; set; } = true;

    /// <summary>Gears (#473): the rusty gear is salvage and money. Every recipe that took one (ppex's
    /// and smex's machines, the glider, BetterRuins' Jonas parts and lamps, ...) takes the steel gear
    /// in the same number, ppex's anvil gears and large gears are no longer made and are hidden, and
    /// smex's Bessemer converter is raised with the steel large gear (off means every recipe as its
    /// mod ships it). The server's recipes are used; both sides patch the converter.</summary>
    public bool GearConsumers { get; set; } = true;

    /// <summary>The rosser: a mechanically powered ring debarker, built from a frame and Immersive
    /// Woodworking's and the game's parts, that strips the bark and branches off Logging Expanded
    /// tree trunks, giving bark and sticks, and hands the debarked trunk on to a Trunk Storage Rack
    /// or a bucking mill in line. Logging Expanded's trunk gets a debarked state
    /// (<c>loggingmod:treetrunk-{wood}-{size}-debarked-{side}</c>), which the Trunk Storage Rack,
    /// the sawhorses and the bucking mill take, giving debarked logs (needs both mods; off means
    /// there is no rosser and no debarked trunk, and rossers and debarked trunks already in a world
    /// are lost). The server's setting decides.</summary>
    public bool Rosser { get; set; } = true;

    /// <summary>The rosser's figures; a value out of range falls back to its default with a
    /// warning. The server's are used.</summary>
    public RosserConfig RosserSettings { get; set; } = new();

    /// <summary>Ore cells (Ore/, README "Ore cells"): Interesting Ore Gen places at most one deposit
    /// of each metal (and of coal and each industrial mineral) per <see cref="OreCellSizeMetres"/>
    /// square, at a spot picked from the world seed, instead of by its own distance rule (server
    /// side). New worlds only: a world created with it off never gets it.</summary>
    public bool OreCells { get; set; } = true;

    /// <summary>The ore cell's side in blocks (at least 500). Fixed when a world is created.</summary>
    public int OreCellSizeMetres { get; set; } = 5000;

    /// <summary>Per-metal cell sizes overriding <see cref="OreCellSizeMetres"/>, by metal group
    /// (<c>copper</c>, <c>iron</c>, <c>tin</c>, <c>gold</c>, <c>coal</c>, ...). Fixed when a world
    /// is created.</summary>
    public Dictionary<string, int> OreCellSizeByMetal { get; set; } = new();

    /// <summary>No surface copper or surface cassiterite pockets in new worlds (Interesting Ore Gen's
    /// surface signs of deep veins stay).</summary>
    public bool NoSurfaceCopper { get; set; } = true;

    /// <summary>Interesting Ore Gen's veins shrunk per metal to the sizes in
    /// <c>config/ore-sizes.json</c> in new worlds (coal and minerals by a quarter at most).</summary>
    public bool SmallerDeposits { get; set; } = true;

    /// <summary>Interesting Ore Gen's hydrothermal districts about one per 120 km² (7 km tiles) instead of
    /// one per 40–90 km² in new worlds.</summary>
    public bool RarerDistricts { get; set; } = true;

    /// <summary>Traders: lone trader camps on a seeded 2 km grid, one per cell, of the pack's eleven
    /// trader types (Trading/), in place of the game's and other mods' randomly placed camps. New
    /// worlds only: a world takes the grid at its first start with this mod if this is on then, and
    /// keeps that choice; off later means the world's new chunks get the game's camps again. The
    /// trader types and their lists exist either way. Server side.</summary>
    public bool TraderGrid { get; set; } = true;

    /// <summary>Trader standing (Trading/Standing/, README "Standing" and "Companies"): standing per
    /// player and trader, raised by trading, pooled by the player's company (a vanilla group), which
    /// unlocks tiers (wallet, and for later waves prices, maps, orders, rare stock). Saved with the
    /// world. Server side.</summary>
    public bool TraderStanding { get; set; } = true;

    /// <summary>How far, in km, standing with another trader of the same type counts (a tenth of it)
    /// at a trader. 0 turns spillover off.</summary>
    public double TraderStandingSpilloverKm { get; set; } = 6;

    /// <summary>Traders: the pack's traders take any item, not only what their list buys: full price
    /// for listed goods, about half for goods a related type buys, about a fifth otherwise
    /// (config/trading/trader-relations.json), paid from a side budget of a quarter of their wallet,
    /// refilled at restock. Maps, leads, money and worthless goods are refused. Both sides follow the
    /// server's setting.</summary>
    public bool EverythingHasAPrice { get; set; } = true;

    /// <summary>Traders: a supply level per item and 8 km region, raised by selling, drained by buying
    /// and by time, spreading to neighbouring regions; it lowers the price of plentiful goods and puts
    /// player-supplied goods (metal, glass, leather, machine parts) on the shelves. Server side.</summary>
    public bool RegionalSupply { get; set; } = true;

    /// <summary>Regional supply: days for a level to halve on its own.</summary>
    public double SupplyHalfLifeDays { get; set; } = 10;

    /// <summary>Regional supply: the share of a region's level that moves to its neighbours each day.</summary>
    public double SupplySpreadFraction { get; set; } = 0.1;

    /// <summary>Placer fields (Ore/, README "Placer fields"): one rich gravel field of 300–600 blocks
    /// per <see cref="PlacerCellSizeMetres"/> square, by water or on a valley floor, at a spot from
    /// the seed; the scattered rich gravel cut to a quarter; and native copper in the pan from the
    /// rich gravel of every rock (server side). New worlds only: a world created with it off never
    /// gets it.</summary>
    public bool PlacerFields { get; set; } = true;

    /// <summary>The placer cell's side in blocks (at least 500). Fixed when a world is created.</summary>
    public int PlacerCellSizeMetres { get; set; } = 1500;

    /// <summary>Schematics are sold only by traders (#468, Trading/Schematics/, README
    /// "Schematics"): every schematic in the pack is taken out of loot, stack randomizers and
    /// structures' chests, no recipe copies or makes one, and every recipe using one keeps it.
    /// Server side.</summary>
    public bool TraderSchematics { get; set; } = true;

    /// <summary>Machine schematics (#469): every machine's and vehicle's first-stage recipe takes its
    /// own <c>seraphhorizons:schematic-{machine}</c>, kept on crafting, and traders sell them. Off,
    /// the recipes are as their mods ship them and nobody sells the schematics. Server side.</summary>
    public bool MachineSchematics { get; set; } = true;

    /// <summary>Standing orders (#453, README "Orders and deliveries"): each trader asks for one or
    /// two lots of what it buys at a premium, taken with <c>/sh order</c> and delivered through the
    /// trade dialog or by hand; an order taken and left undelivered costs standing. Server side.</summary>
    public bool TraderOrders { get; set; } = true;

    /// <summary>Deliveries (#454, README "Orders and deliveries"): a trader hands a player a package for
    /// another camp against a deposit, paid with a fee and standing on time, less when late, the
    /// deposit and standing with the sender lost when it fails. Server side; the package item exists
    /// either way.</summary>
    public bool TraderDeliveries { get; set; } = true;

    /// <summary>Traders sell maps and leads (#455, Trading/Maps/, README "Maps and leads"):
    /// prospectors ore maps to unsold deposits within 5 km, every trader a gravel map to a field
    /// within 2 km and leads to other camps; precision and the further leads by standing. Needs the
    /// deposit registry (ore cells or placer fields) for maps. Server side.</summary>
    public bool TraderMaps { get; set; } = true;

    /// <summary>Admin tools (#458, #459, docs/admin-tools.md): the debugging subcommands under
    /// <c>/sh ore</c> and <c>/sh trade</c> (privilege controlserver), <c>--json</c> answers, the admin
    /// logs and the admin map layer. Changes nothing in play. Server side.</summary>
    public bool AdminTools { get; set; } = true;

    /// <summary>Travelling merchants (#456, Trading/Visitors/, README "Travelling merchants"): an inn
    /// flag raised by a player-built inn (a market stall or inn sign, a bed, a table with food, lit,
    /// roofed and walled) calls a travelling merchant or curio dealer, when its owner is regular with a
    /// camp within 6 km and the region trades in what the visitor buys; it stays 3–5 days, cannot be
    /// hurt, and comes again after 10 days. Server side.</summary>
    public bool TravellingMerchants { get; set; } = true;

    /// <summary>Travelling merchants: the region's summed supply level of what a visitor buys before it
    /// comes (a level is 10 gears' worth sold there and not yet drained). 0 turns the condition off.</summary>
    public double TravellingMerchantMinSupply { get; set; } = 2;

    /// <summary>Machine oil: the heavy mechanical power machines (the game's helve hammer and
    /// pulverizer, Immersive Woodworking's sawmill and chopper, the bucking sawmill and the rosser)
    /// have an oil tank, filled by right-clicking them with oil, that their jobs drain; a machine
    /// starts dry, and a dry one loads its shaft several times as hard (both sides; off means every
    /// machine turns as it ships, with no tank). The server's setting decides.</summary>
    public bool MachineOil { get; set; } = true;

    /// <summary>Machine oil's figures: the oils, the dry multiplier, and each machine's tank and
    /// drain per job; a value out of range falls back to its default with a warning. The server's
    /// are used.</summary>
    public MachineOilConfig MachineOilSettings { get; set; } = new();
    /// <summary>Trunk entities: Logging Expanded's tree trunks are never items in an inventory. A
    /// felled tree leaves a trunk lying on the ground as an entity, which you drag with a rope or by
    /// holding the right mouse button on it with an empty hand, shove by walking into it, float down
    /// rivers, or shoulder very slowly with Carry On; its weight grows with its logs. Loose trunk
    /// items are turned into trunk entities, no survival player is given a trunk stack (one
    /// already in a slot still moves and can be thrown out), and trunks already
    /// placed as blocks are deleted when they load, nothing returned (needs Logging Expanded; Carry
    /// On is optional; off means trunks are as Logging Expanded ships them). The server's setting
    /// decides; a client follows the server.</summary>
    public bool TrunkEntities { get; set; } = true;

    /// <summary>The trunk entities' figures; a value out of range falls back to its default with a
    /// warning. The server's are used.</summary>
    public TrunkEntityConfig TrunkEntitiesSettings { get; set; } = new();

    /// <summary>Gear reclamation (#484, GearReclamation/, PicklingTub/, README "Gear reclamation"):
    /// rusty gears are salvage, reclaimed into steel gears by boiling in lye, pickling in the
    /// pickling tub, neutralizing in lime water and oiling in lard, one in ten sound and the rest
    /// steel bits; bare gears flash-rust back, and steel gears rust back into currency in the tub's
    /// brine bath. Off means none of the steps' recipes, no roll, no salvage text, no pickling tub
    /// and no bare steel gear; the other gear items exist either way. The server's setting decides.</summary>
    public bool GearReclamation { get; set; } = true;

    /// <summary>Gear reclamation's figures; a value out of range falls back to its default with a
    /// warning. The server's are used.</summary>
    public GearReclamationConfig GearReclamationSettings { get; set; } = new();

    /// <summary>The pickling tub's figures (#476, #482): batch size, capacity, the acid rule table
    /// and the brine bath; a value out of range falls back to its default with a warning, a broken
    /// rule is dropped. The server's are used.</summary>
    public PicklingTubConfig PicklingTubSettings { get; set; } = new();

    /// <summary>Steel bits recovery (#478, SteelBits/): steel bits, which no fuel melts, go back into
    /// steel. In the game's stone coffin 20 bits take an iron ingot's place and come out a blister
    /// steel ingot (put in directly, or packed in the crafting grid first); and Steelmaking
    /// Expanded's Bessemer converter takes them as scrap, which its default setting already does and
    /// a server's file that leaves them out is overridden for the run. Both sides; off means the
    /// coffin is not patched, there is no packing recipe and smex's setting is as its file says.</summary>
    public bool SteelBitsRecovery { get; set; } = true;

    /// <summary>Steel gear blanks (#479, Gears/, README "Steel gear blanks"): a steel gear blank and a
    /// large one, cast in clay-formed gear blank molds filled from a crucible (or smex's canal
    /// pedestal) or smithed from one and two steel ingots, by hand or with the helve hammer (off means
    /// the blanks, their molds and their recipes do not exist, and those already in a world are
    /// lost). The server's setting decides.</summary>
    public bool GearBlanks { get; set; } = true;

    /// <summary>The gear cutter (#480, #481, GearCutter/, README "Gear cutter"): a mechanically
    /// powered generating gear cutter, built on a frame in ten stages from steel parts, Jonas parts
    /// and a temporal gear master, that cuts steel gear blanks into steel gears and large steel
    /// gears; a MachineOil machine whose oil wears its cutter kit, not its shaft load (off means its
    /// blocks, its parts and their recipes do not exist, and cutters already placed are lost). The
    /// server's setting decides.</summary>
    public bool GearCutter { get; set; } = true;

    /// <summary>The gear cutter's figures; a value out of range falls back to its default with a
    /// warning. The server's are used.</summary>
    public GearCutterConfig GearCutterSettings { get; set; } = new();
}
