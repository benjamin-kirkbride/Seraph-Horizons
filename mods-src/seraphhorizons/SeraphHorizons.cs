using HarmonyLib;
using SeraphHorizons.Mod.BuckingSawmill.Core;
using SeraphHorizons.Mod.Core;
using SeraphHorizons.Mod.GearCutter.Core;
using SeraphHorizons.Mod.GearReclamation.Core;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.NanMotion;
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
    private Harmony? _heatingRackPlacementHarmony;
    // Its own id, patched once per process, as the barrel rack's: the client checks the hotbar too.
    private Harmony? _gearConsumersHarmony;
    private Harmony? _gearPartsHarmony;
    // Its own id, patched once per process, as the heating rack's: both sides ask Yang's seat check.
    private Harmony? _locomotiveStayOnHarmony;
    // Its own id, server side only.
    private Harmony? _locomotiveBreatheHarmony;
    private bool _gearConsumers;
    private bool _fellingWear;
    private UnifiedWoodworking? _woodworking;
    // Client side only: Carry On's icon stack fields, cleared again when the client leaves the world.
    private List<System.Reflection.FieldInfo>? _carryOnIconFields;
    // Client side only: the sun of /clear stay on the client's calendar.
    private ClearSkyClient? _clearSkyClient;
    // Client side only, its own id: the NaN motion diagnostics' patches (#405).
    private Harmony? _nanMotionHarmony;

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
        // On both sides whatever the switch says: the client cannot know the server's.
        api.Network.RegisterChannel(ClearSkySun.Channel).RegisterMessageType<ClearSkyPacket>();
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
        if (!Config(api).DuplicateRecipes)
            DisablePatches(DuplicateRecipes.DisablePatches);
        if (!(Config(api).DurableSawmillBlades && SawmillBladeDurability.Applies(api)))
            DisablePatches(SawmillBladeDurability.DisablePatches);
        if (!(Config(api).FewerSupportChains && SupportChains.Applies(api)))
            DisablePatches(SupportChains.DisablePatches);
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
        if (Config(api).HeatingRackStandsOnBlock && HeatingRackPlacement.Applies(api) && HeatingRackPlacement.Bind(api.Logger))
            HeatingRackPlacement.Patch(_heatingRackPlacementHarmony = new Harmony(HeatingRackPlacement.HarmonyId));
        _gearConsumers = Config(api).GearConsumers;
        if (!_gearConsumers)
            DisablePatches(GearConsumers.DisablePatches);
        else if (GearConsumers.BessemerApplies(api) && GearConsumers.Bind(api.Logger))
            GearConsumers.Patch(_gearConsumersHarmony = new Harmony(GearConsumers.HarmonyId));
        // BetterLoot+ applies its loot on the server only, in AssetsLoaded.
        if (!(Config(api).GearPartsRemoved && GearPartsRemoved.Applies(api)))
            DisablePatches(GearPartsRemoved.DisablePatches);
        else if (api.Side == EnumAppSide.Server)
            GearPartsRemoved.Patch(_gearPartsHarmony = new Harmony(GearPartsRemoved.HarmonyId), api.Logger);
        _fellingWear = Config(api).FlatFellingWear && FellingWear.Applies(api) && FellingWear.Bind(api.Logger);
        if (Config(api).LocomotiveRidersStayOn && LocomotiveSeats.Applies(api) && LocomotiveSeats.BindStayOn(api.Logger))
            LocomotiveSeats.PatchStayOn(_locomotiveStayOnHarmony = new Harmony(LocomotiveSeats.StayOnHarmonyId));
        // Rewrites Battle Towers' own patch file, on the server, before the patch loader reads it.
        if (api.Side == EnumAppSide.Server && Config(api).RarerBattleTowers && BattleTowers.Applies(api))
            BattleTowers.MakeRarer(api);
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
        if (_fellingWear)
            FellingWear.Patch(_harmony ??= new Harmony(HarmonyId), api, Config(api).FlatFellingWearSettings ?? new FellingWearConfig());
        if (Config(api).LocomotiveRidersBreathe && LocomotiveSeats.Applies(api))
            LocomotiveSeats.PatchBreathe(_locomotiveBreatheHarmony = new Harmony(LocomotiveSeats.BreatheHarmonyId));
        if (Config(api).RuinsOnMedianGround)
            RuinSurfaceMedian.Patch(_harmony ??= new Harmony(HarmonyId), api.Logger);
        ClearSky = new ClearSky(api);
        if (Config(api).ClearCommand)
            ClearSky.Register();
        else
            ClearSky.ReleaseLeftoverLock();
    }

    /// <summary>The <c>/clear</c> command (server side).</summary>
    public ClearSky? ClearSky { get; private set; }

    // Cart reach acts where the player picks what is under the crosshair: the client. Entity types
    // arrive from the server, so the matching ones are known once the level is finalized.
    public override void StartClientSide(ICoreClientAPI api)
    {
        // Always: the server says whether /clear stay holds the sun.
        _clearSkyClient = new ClearSkyClient(api);
        if (Config(api).CartReach)
            api.Event.LevelFinalize += () => PatchCartReach(api);
        // When the level is final: every mod's assemblies are loaded, and the player exists.
        if (Config(api).NanMotionDiagnostics)
            api.Event.LevelFinalize += () => PatchNanMotion(api);
        else
            api.Logger.Notification("[seraphhorizons] NaN motion diagnostics are switched off; a NaN motion crash (#405) leaves no report");
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

    private void PatchNanMotion(ICoreClientAPI api)
    {
        try
        {
            NanMotionDiagnostics.PatchClient(_nanMotionHarmony = new Harmony(NanMotionDiagnostics.HarmonyId), api);
        }
        catch (Exception e)
        {
            api.Logger.Error("[seraphhorizons] NaN motion diagnostics could not start, and are off: {0}", e);
        }
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
        FellingWear.Unbind();
        RuinSurfaceMedian.Unbind();
        if (ClearSky != null)
        {
            ClearSky.Dispose();
            ClearSky = null;
        }
        _clearSkyClient?.Dispose();
        _clearSkyClient = null;
        if (_clientHarmony != null)
        {
            _clientHarmony.UnpatchAll(CartReach.HarmonyId);
            _clientHarmony = null;
            CartReach.Unbind();
        }
        if (_nanMotionHarmony != null)
        {
            NanMotionDiagnostics.Unbind();
            _nanMotionHarmony.UnpatchAll(NanMotionDiagnostics.HarmonyId);
            _nanMotionHarmony = null;
        }
        _barrelRackHarmony?.UnpatchAll(BarrelRackKegs.HarmonyId);
        _barrelRackHarmony = null;
        _heatingRackHarmony?.UnpatchAll(HeatingRackPosition.HarmonyId);
        _heatingRackHarmony = null;
        _heatingRackPlacementHarmony?.UnpatchAll(HeatingRackPlacement.HarmonyId);
        _heatingRackPlacementHarmony = null;
        _gearConsumersHarmony?.UnpatchAll(GearConsumers.HarmonyId);
        _gearConsumersHarmony = null;
        if (_gearPartsHarmony != null)
        {
            _gearPartsHarmony.UnpatchAll(GearPartsRemoved.HarmonyId);
            _gearPartsHarmony = null;
            GearPartsRemoved.Unbind();
        }
        _locomotiveStayOnHarmony?.UnpatchAll(LocomotiveSeats.StayOnHarmonyId);
        _locomotiveStayOnHarmony = null;
        _locomotiveBreatheHarmony?.UnpatchAll(LocomotiveSeats.BreatheHarmonyId);
        _locomotiveBreatheHarmony = null;
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

    /// <summary>NaN motion diagnostics (#405): the client traces every place that can write the
    /// player's motion and, when it goes NaN and the client crashes, first writes a report of where it
    /// went NaN and of everything around the player to the client log and to
    /// <c>Logs/seraphhorizons-nanmotion-*.txt</c> (client side; the crash itself is left as it is; off
    /// means no tracing and no report).</summary>
    public bool NanMotionDiagnostics { get; set; } = true;

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

    /// <summary>Recipes that duplicate or undercut another recipe for the same thing are off:
    /// Expanded Foods' offal-free kneading sausages and scrap brazier, Material Needs' re-declared
    /// aged roofing, raft, oar and round shield, the game's barrel cottage cheese and sandstone daub
    /// (server side; each only with the mod whose recipe stays; off means all as they ship).</summary>
    public bool DuplicateRecipes { get; set; } = true;

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

    /// <summary>Logging Expanded: a Trunk Heating Rack placed onto the top face of a block (from the
    /// hotbar, a creative pick or Carry On) stands on it, one cell up with the cell between left for
    /// a firepit, unless the block is a firepit (both sides; off means it stands in the block, legs
    /// in the floor, as Logging Expanded ships it).</summary>
    public bool HeatingRackStandsOnBlock { get; set; } = true;

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

    /// <summary>Better Ruins: its Machinist's Mechanism Blueprint makes 4 support chains a craft,
    /// not 64 (server side; off means its own 64).</summary>
    public bool FewerSupportChains { get; set; } = true;

    /// <summary>Immersive Woodworking: the sawmill's and the chopper's frames and parts take iron,
    /// meteoric iron or steel, and far more nails and strips (off means its own recipes).</summary>
    public bool IronWoodworkingMachines { get; set; } = true;

    /// <summary>Gears (#473): the rusty gear is salvage and money. Every recipe that took one (ppex's
    /// and smex's machines, the glider, BetterRuins' Jonas parts and lamps, ...) takes the stainless
    /// gear in the same number, ppex's anvil gears and large gears are no longer made and are hidden,
    /// and smex's Bessemer converter is raised with the stainless large gear (off means every recipe as its
    /// mod ships it). The server's recipes are used; both sides patch the converter.</summary>
    public bool GearConsumers { get; set; } = true;

    /// <summary>BetterLoot+: its rusty gear part is gone (the item and both its grid recipes; parts in
    /// a world vanish), and every gear part drop in its loot is a rusty gear drop at a quarter of the
    /// average, the same gears on average (server side; off means as BetterLoot+ ships it).</summary>
    public bool GearPartsRemoved { get; set; } = true;

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

    /// <summary>Logging Expanded: felling a tree that leaves a trunk costs the axe a flat
    /// <c>FlatFellingWearSettings.ThinTree</c> (4), or <c>ThickTree</c> (8) for a tree with a
    /// two-by-two trunk (the redwood), in place of one durability per log; the trunk's logs cost
    /// their own at the stations and machines. A felling that leaves no trunk costs the game's
    /// one per log (server side; off means every felling does).</summary>
    public bool FlatFellingWear { get; set; } = true;

    /// <summary>The flat felling wear's figures; a value out of range falls back to its default
    /// with a warning. The server's are used.</summary>
    public FellingWearConfig FlatFellingWearSettings { get; set; } = new();

    /// <summary>Yang's Transport Tycoon: blocks beside the track (a tree's leaves) no longer throw a
    /// rider off a standard-gauge locomotive (<c>yangtransport:sglocomotive-*</c>); its seats'
    /// collision check is off. Both sides; either side's switch on keeps riders on.</summary>
    public bool LocomotiveRidersStayOn { get; set; } = true;

    /// <summary>Yang's Transport Tycoon: whoever sits in a standard-gauge locomotive does not
    /// suffocate in a block their head passes through, though they still drown under water
    /// (server side).</summary>
    public bool LocomotiveRidersBreathe { get; set; } = true;

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

    /// <summary>Battle Towers (#519, README "Rarer battle towers"): its towers rarer, at the chances
    /// and spacings in <c>config/battletowers-rates.json</c>: about one surface tower per 16 km², one
    /// hard tower per 66 km² and one underground tower per 17 km², where Battle Towers places dozens
    /// of underground towers per km² (server side; off means as Battle Towers ships them). Worldgen
    /// only: it changes the chunks generated from then on.</summary>
    public bool RarerBattleTowers { get; set; } = true;

    /// <summary>Surface ruins (BetterRuins' and the game's) sit on the median of the ground the game
    /// samples around them, not its lowest point, so on a slope they are no longer buried on the
    /// uphill side, and the air under their downhill side is filled with the ground there; the
    /// game's limit on how uneven that ground may be is unchanged (server side;
    /// off means the lowest point, as the game places them). Worldgen only: it changes the chunks
    /// generated from then on.</summary>
    public bool RuinsOnMedianGround { get; set; } = true;

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

    /// <summary>Traders: the pack's traders take any item, not only what their list buys: off-list
    /// goods at three quarters of their value for goods a related type buys, paid from the wallet, a
    /// fifth otherwise (the curio dealer three tenths; config/trading/trader-relations.json), paid
    /// from a side budget of a quarter of their wallet, refilled at restock, as is what they buy back
    /// off their own shelf. Maps, leads, money and worthless goods are refused. Both sides follow the
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

    /// <summary>Standing orders (#453, README "Orders and deliveries"): each trader asks for some of
    /// what it buys, more and better paid the higher the standing, taken in the trade window's Orders
    /// tab and delivered by handing the goods in there for many times their value (new money); an
    /// order taken and left undelivered costs standing. Server side.</summary>
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
    /// felled tree leaves a trunk lying on the ground as an entity, which you move with a rope or
    /// push on foot from one end like a sled (right-click with an empty hand), which is solid to walk into, float down
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
    /// rusty gears are corroded stainless steel, reclaimed into stainless gears by boiling in lye,
    /// pickling and then passivating in nitric acid in the pickling tub, and neutralizing in lime
    /// water, one in ten sound and the rest stainless bits. Off means none of the steps' recipes, no
    /// roll, no salvage text and no pickling tub; the gear items exist either way. The server's
    /// setting decides.</summary>
    public bool GearReclamation { get; set; } = true;

    /// <summary>Gear reclamation's figures; a value out of range falls back to its default with a
    /// warning. The server's are used.</summary>
    public GearReclamationConfig GearReclamationSettings { get; set; } = new();

    /// <summary>The pickling tub's figures (#476): batch size, capacity and the acid rule table
    /// (pickling and passivating); a value out of range falls back to its default with a warning, a broken
    /// rule is dropped. The server's are used.</summary>
    public PicklingTubConfig PicklingTubSettings { get; set; } = new();

    /// <summary>Steel bits recovery (#478, SteelBits/): steel bits, which no fuel melts, go back into
    /// steel. In the game's stone coffin 20 bits take an iron ingot's place and come out a blister
    /// steel ingot (put in directly, or packed in the crafting grid first); and Steelmaking
    /// Expanded's Bessemer converter takes them as scrap, which its default setting already does and
    /// a server's file that leaves them out is overridden for the run. Both sides; off means the
    /// coffin is not patched, there is no packing recipe and smex's setting is as its file says.</summary>
    public bool SteelBitsRecovery { get; set; } = true;

    /// <summary>Stainless gear blanks (#479, Gears/, README "Stainless gear blanks"): a stainless gear
    /// blank and a large one, cast in clay-formed gear blank molds (four small ones to an ingot) or
    /// smithed, two small ones from a stainless steel ingot and a large one from two, by hand or with
    /// the helve hammer (off means
    /// the blanks, their molds and their recipes do not exist, and those already in a world are
    /// lost). The server's setting decides.</summary>
    public bool GearBlanks { get; set; } = true;

    /// <summary>The gear cutter (#480, #481, GearCutter/, README "Gear cutter"): a mechanically
    /// powered generating gear cutter, built on a frame in ten stages from steel parts, Jonas parts
    /// and a temporal gear master, that cuts stainless gear blanks into stainless gears and large
    /// stainless gears; a MachineOil machine whose oil wears its cutter kit, not its shaft load (off means its
    /// blocks, its parts and their recipes do not exist, and cutters already placed are lost). The
    /// server's setting decides.</summary>
    public bool GearCutter { get; set; } = true;

    /// <summary>The gear cutter's figures; a value out of range falls back to its default with a
    /// warning. The server's are used.</summary>
    public GearCutterConfig GearCutterSettings { get; set; } = new();

    /// <summary>The draw bench (DrawBench/, README "Draw bench"): a mechanically powered chain draw
    /// bench, built on a frame in five stages (a Jonas gearbox, a chain, a heavy bracket as the dog,
    /// a rod as the mandrel and a smithed die), that draws a lead or copper ingot into three of the
    /// game's chute sections (the pack's pipe section) of its metal; an iron die draws lead, a steel one lead and
    /// copper; a MachineOil machine (off means its blocks, its dies and their recipes do not exist,
    /// and benches already placed are lost). The server's setting decides.</summary>
    public bool DrawBench { get; set; } = true;

    /// <summary>The draw bench's figures; a value out of range falls back to its default with a
    /// warning. The server's are used.</summary>
    public DrawBench.Core.DrawBenchConfig DrawBenchSettings { get; set; } = new();

    /// <summary>The press brake (PressBrake/, README "Press brake"): a hand-worked leaf brake of oak
    /// with iron edges and screws, built on a frame in two stages (a rod as its clamp screws, a plate
    /// as its wearing edges), that folds a lead or copper half plate (the squaring shear's) once across
    /// its middle into an angle while the player holds right-click on it, as on the quern: the only
    /// maker of angles; no power and no oil (off means its blocks and its recipe do not exist, and
    /// brakes already placed are lost; with <see cref="SquaringShear"/> off it has no half plates and
    /// refuses work). The server's setting decides.</summary>
    public bool PressBrake { get; set; } = true;

    /// <summary>The press brake's figures; a value out of range falls back to its default with a
    /// warning. The server's are used.</summary>
    public PressBrake.Core.PressBrakeConfig PressBrakeSettings { get; set; } = new();

    /// <summary>The squaring shear (SquaringShear/, README "Squaring shear"): a tinsmith's
    /// foot-treadle squaring shear of oak with iron blades, built on a frame in two stages (a plate as
    /// its blades, a rod as its back gauge and hold-down), that cuts a lead or copper plate across its
    /// middle into two half plates while the player holds right-click on it, as on the quern; no power
    /// and no oil (off means its blocks, the half plate and its recipe do not exist, and shears already
    /// placed and half plates already made are lost). The server's setting decides.</summary>
    public bool SquaringShear { get; set; } = true;

    /// <summary>The squaring shear's figures; a value out of range falls back to its default with a
    /// warning. The server's are used.</summary>
    public SquaringShear.Core.SquaringShearConfig SquaringShearSettings { get; set; } = new();

    /// <summary>The mandrel forging station (MandrelStation/, README "Mandrel forging station"): an
    /// oak stump with an iron bracket holding a rod as its mandrel, on which a lead or copper hollow
    /// section (the game's chute section) is hammered, a right-click with a hammer a blow as on the
    /// anvil, into two pipe sections of its metal; no power and no oil (off means its blocks and its
    /// recipe do not exist, and stations already placed are lost). The server's setting decides.</summary>
    public bool MandrelStation { get; set; } = true;

    /// <summary>The mandrel station's figures; a value out of range falls back to its default with a
    /// warning. The server's are used.</summary>
    public MandrelStation.Core.MandrelStationConfig MandrelStationSettings { get; set; } = new();

    /// <summary>Stainless steel (#484, CrucibleFurnace/, README "Crucible furnace"): a crucible
    /// furnace of melting holes lined with tier-3 refractory brick, in a row of up to four on a brick
    /// chimney at least six blocks tall, fired with coke under iron lids (faster with forced air piped
    /// in from Steelmaking Expanded's blowers), each holding a fireclay melting pot of 200 units that
    /// lasts three heats. Its charges, by ratio: quartz, iron bits and coke into ferrosilicon; chromite,
    /// ferrosilicon and lime into ferrochrome and slag; iron and ferrochrome, about 4 to 1, into
    /// stainless steel; stainless bits back into stainless. A pulled pot of stainless pours like the
    /// game's crucible into any mold, for about 20 seconds before it freezes and the pot is lost (off
    /// means the holes, the pots, the ferroalloys and their recipes do not exist, and those already in
    /// a world are lost). The server's setting decides.</summary>
    public bool StainlessSteel { get; set; } = true;

    /// <summary>The crucible furnace's figures; a value out of range falls back to its default with a
    /// warning. The server's are used.</summary>
    public CrucibleFurnace.Core.CrucibleFurnaceConfig CrucibleFurnaceSettings { get; set; } = new();

    /// <summary>Cast pipes (Pipes/Game/CastPipesSystem.cs, README "Cast pipes"): Steelmaking
    /// Expanded's tool mold gets a pipe tool type, filled from its canal (or a crucible) with one
    /// ingot of iron or steel, which casts two pipe sections of that metal, banded into ppex pipe on
    /// the grid (needs UnifiedPipes, which adds the pipe section; off, or with smex's mold
    /// files not as expected, means there is no pipe mold and no recipe for one, and those already in
    /// a world are lost). The server's setting decides.</summary>
    public bool CastPipes { get; set; } = true;

    /// <summary>The handcar (Handcar/, README "Handcar"): a standard-gauge rail car for Yang's Transport
    /// Tycoon, pumped by hand by up to two riders standing at the ends of its walking beam (forward or
    /// back pumps, left or right picks the branch at the next switch), with a cargo slot for a chest
    /// or crate. Off means its entity, its item and its recipe do not exist, and handcars already in a
    /// world are lost. The server's setting decides; without Yang's Transport Tycoon there is none.</summary>
    public bool Handcar { get; set; } = true;

    /// <summary>The handcar's figures: speeds, drag, braking, the riders' satiety and the effort's fade;
    /// a value out of range falls back to its default with a warning. The server's are used for the
    /// drive.</summary>
    public SeraphHorizons.Mod.Handcar.Core.HandcarConfig HandcarSettings { get; set; } = new();

    /// <summary>Pack version check (PackCheck/, README "Pack version check"): each side compares its
    /// loaded mods and game version with the pack this build was released with (pack/lock.json,
    /// built in): a locked mod at another version or missing, a mod the pack does not have, another
    /// game version, or this mod at another version than the pack's. The server logs a warning for
    /// each and tells a joining admin in chat; a client shows a dialog once in the world, until the
    /// player dismisses that set of findings. Each side's own setting decides for it; off means
    /// nothing is checked.</summary>
    public bool PackVersionCheck { get; set; } = true;

    /// <summary>Settings follow the pack's defaults (ConfigDefaults/, README "Settings follow the
    /// pack's defaults", docs/config-defaults.md): at start, before any mod reads its config, every
    /// setting in ModConfig (any mod's) that still has the default of the pack version the install
    /// last ran is moved to the current version's default when that changed; a value anyone set is
    /// kept. A fresh install also gets the values the pack sets instead of a mod's own default
    /// (pack/config/ModConfig). Each side's own setting decides for its own config folder; off
    /// means nothing is changed or written.</summary>
    public bool FollowPackDefaults { get; set; } = true;

    /// <summary>Unified pipes (Pipes/, README "Unified pipes"): one pipe network. Pipes and Power
    /// Expanded's pipes come in copper and lead as well as iron and steel, and its valves and pressure
    /// valves in bronze; its plate-and-nails pipes and iron and steel valves are not made any more;
    /// pipe of every shape is made from this mod's pipe sections, soldered or banded with nails; a
    /// copper or lead pipe section comes from the game's chute section (in lead too), now made only
    /// from two soldered angles (the game's anvil and plate recipes for it off); and each metal bursts
    /// at its own figure, lead at once on steam or exhaust (off means ppex's pipes and the game's chute
    /// section are as they ship, and copper, lead and bronze pipes, angles, pipe sections and lead
    /// chute sections already in a world are lost). The server's setting decides.</summary>
    public bool UnifiedPipes { get; set; } = true;

    /// <summary>The unified pipes' burst figures, in atm; a value out of range falls back to its
    /// default with a warning. Each side uses its own for the text, the server's for the pipes.</summary>
    public Pipes.Core.UnifiedPipesConfig UnifiedPipesSettings { get; set; } = new();

    /// <summary>The eidolon (#668; Eidolon/, EidolonGantry/, README "Eidolon"): a player-built laborer
    /// automaton raised in a wooden gantry, behind the eidolon schematic the curio dealer sells, and
    /// ordered with a command tool (off means the command tool and its recipe do not exist, those
    /// already in a world are lost, and the curio dealer does not stock the schematic or the Jonas
    /// pump head). The server's setting decides.</summary>
    public bool Eidolon { get; set; } = true;
}
