using HarmonyLib;
using SeraphHorizons.Mod.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace SeraphHorizons.Mod;

/// <summary>
/// The pack's own tweaks. Each tweak is its own class, switched by its own setting in
/// ModConfig/seraphhorizons.json, and skipped with a log line when the mod it changes is not
/// installed or no longer looks as expected. This system applies its own patches explicitly
/// (never PatchAll). Tidy Variants (TidyVariants/), Map Reveal (MapReveal/) and the creative mod
/// tabs (CreativeModTabs/) have their own mod systems, which read their switch through
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
    // Its own id, patched once per process: both sides need it, and singleplayer runs both in one.
    private Harmony? _barrelRackHarmony;

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
        // Before the game's patch loader, which applies the patches in AssetsLoaded.
        _ageOfFlax = Config(api).AgeOfFlaxRebalance && AgeOfFlaxRebalance.Applies(api)
                     && AgeOfFlaxRebalance.Bind(api.Logger);
        if (!_ageOfFlax)
            AgeOfFlaxRebalance.DisablePatches(api);
        if (!(Config(api).FoodHydration && FoodHydration.Applies(api)))
            FoodHydration.DisablePatches(api);
        if (!(Config(api).HydrateTunRetired && HydrateTun.Applies(api)))
            HydrateTun.DisablePatches(api);
        _irrigationVessel = Config(api).IrrigationVesselRetired && IrrigationVessel.Applies(api);
        if (!_irrigationVessel)
            IrrigationVessel.DisablePatches(api);
        _tunRack = Config(api).LargerTunRack && TunRackCapacity.Applies(api) && TunRackCapacity.Bind(api.Logger);
        if (!_tunRack)
            TunRackCapacity.DisablePatches(api);
        _barrelRackKegs = Config(api).BarrelRackKegs && BarrelRackKegs.Applies(api) && BarrelRackKegs.Bind(api.Logger);
        if (_barrelRackKegs)
            BarrelRackKegs.Patch(_barrelRackHarmony = new Harmony(BarrelRackKegs.HarmonyId));
        else
            BarrelRackKegs.DisablePatches(api);
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
        if (_tunRack)
            TunRackCapacity.Patch(_harmony ??= new Harmony(HarmonyId));
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
        if (_barrelRackKegs)
            LangText.Apply(BarrelRackKegs.LangEdits, BarrelRackKegs.FoodShelvesId, api.Logger);
    }

    public override void Dispose()
    {
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

    /// <summary>Food Shelves: the tun in a tun rack holds 950 litres, as Hydrate or Diedrate's tun
    /// does, instead of 500 (server side).</summary>
    public bool LargerTunRack { get; set; } = true;

    /// <summary>Food Shelves and Hydrate or Diedrate: the barrel rack takes kegs too, holding a keg's
    /// worth, the liquid moving between the keg and the rack as the keg goes in and out, and
    /// perishing at the keg's own rate times the rack's (both sides; the server's switch decides
    /// what the rack takes).</summary>
    public bool BarrelRackKegs { get; set; } = true;
}
