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

    /// <summary>This side's settings. Loaded on first use (this system's <see cref="Start"/> at the
    /// latest), so another system of the mod can read them in any phase.</summary>
    public static SeraphTweaksConfig ConfigFor(ICoreAPI api) =>
        api.ModLoader.GetModSystem<SeraphTweaksSystem>() is { } system ? system.Config(api) : new SeraphTweaksConfig();

    private SeraphTweaksConfig Config(ICoreAPI api) => _config ??= LoadConfig(api);

    public override void Start(ICoreAPI api)
    {
        Config(api);
    }

    // Behavior changes run on the server only: that is where the tweaked mods simulate.
    public override void StartServerSide(ICoreServerAPI api)
    {
        if (Config(api).BoilerLidBlowsOpen && BoilerLidRelief.Applies(api))
        {
            _harmony ??= new Harmony(HarmonyId);
            BoilerLidRelief.Patch(_harmony, api.Logger);
        }
    }

    // Lang files are loaded, mod assets included, before this phase on both sides.
    public override void AssetsLoaded(ICoreAPI api)
    {
        if (Config(api).BoilerLidBlowsOpen && BoilerLidRelief.Applies(api))
            BoilerLidRelief.RewriteText(api.Logger);
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
}
