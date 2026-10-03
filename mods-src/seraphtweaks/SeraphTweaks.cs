using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace SeraphHorizons.SeraphTweaks;

/// <summary>
/// The pack's own tweaks. Each tweak is its own class, switched by its own setting in
/// ModConfig/seraphtweaks.json, and skipped with a log line when the mod it changes is not
/// installed or no longer looks as expected. This system applies its own patches explicitly
/// (never PatchAll). Tidy Variants (TidyVariants/) has its own mod systems, which read their
/// switch through <see cref="ConfigFor"/>.
/// </summary>
public class SeraphTweaksSystem : ModSystem
{
    public const string HarmonyId = "seraphtweaks";
    public const string ConfigFile = "seraphtweaks.json";

    private SeraphTweaksConfig? _config;
    private Harmony? _harmony;
    private bool _ageOfFlax;

    /// <summary>This side's settings. Loaded on first use (this system's <see cref="Start"/> at the
    /// latest), so another system of the mod can read them in any phase.</summary>
    public static SeraphTweaksConfig ConfigFor(ICoreAPI api) =>
        api.ModLoader.GetModSystem<SeraphTweaksSystem>() is { } system ? system.Config(api) : new SeraphTweaksConfig();

    private SeraphTweaksConfig Config(ICoreAPI api) => _config ??= LoadConfig(api);

    public override void Start(ICoreAPI api)
    {
        Config(api);
        CreativeSteamSource.RegisterClasses(api);
        // Before the game's patch loader, which applies the patches in AssetsLoaded.
        _ageOfFlax = Config(api).AgeOfFlaxRebalance && AgeOfFlaxRebalance.Applies(api)
                     && AgeOfFlaxRebalance.Bind(api.Logger);
        if (!_ageOfFlax)
            AgeOfFlaxRebalance.DisablePatches(api);
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
        if (_ageOfFlax)
        {
            LangText.Apply(AgeOfFlaxRebalance.LangEdits, AgeOfFlaxRebalance.ModId, api.Logger);
            if (api.Side == EnumAppSide.Server)
                AgeOfFlaxRebalance.AddSeedsToBlocktype(api);
        }
        if (api.Side == EnumAppSide.Server
            && !(Config(api).CreativeSteamSource && CreativeSteamSource.Applies(api) && CreativeSteamSource.Bind(api.Logger)))
            CreativeSteamSource.Disable(api);
    }

    public override void Dispose()
    {
        _harmony?.UnpatchAll(HarmonyId);
        _harmony = null;
    }

    private static SeraphTweaksConfig LoadConfig(ICoreAPI api)
    {
        SeraphTweaksConfig? config = null;
        try
        {
            config = api.LoadModConfig<SeraphTweaksConfig>(ConfigFile);
        }
        catch (Exception e)
        {
            api.Logger.Error($"[seraphtweaks] Could not read ModConfig/{ConfigFile}, using the defaults: {e.Message}");
            return new SeraphTweaksConfig();
        }
        config ??= new SeraphTweaksConfig();
        // Writes back settings added since the file was made.
        api.StoreModConfig(config, ConfigFile);
        return config;
    }
}

public class SeraphTweaksConfig
{
    /// <summary>Pipes and Power Expanded: an over-pressured boiler blows its lid open instead of
    /// exploding.</summary>
    public bool BoilerLidBlowsOpen { get; set; } = true;

    /// <summary>Tidy Variants: hides orientation and open/closed variants and groups the rest in
    /// the creative inventory and the handbook (client side; off means both stay vanilla).</summary>
    public bool TidyVariants { get; set; } = true;

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
}
