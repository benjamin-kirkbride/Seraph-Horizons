using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace SeraphHorizons.SeraphTweaks;

/// <summary>
/// The pack's gameplay tweaks to other mods. Each tweak is its own class, switched by its own
/// setting in ModConfig/seraphtweaks.json, and skipped with a log line when the mod it changes
/// is not installed or no longer looks as expected.
/// </summary>
public class SeraphTweaksSystem : ModSystem
{
    public const string HarmonyId = "seraphtweaks";
    public const string ConfigFile = "seraphtweaks.json";

    private SeraphTweaksConfig _config = new();
    private Harmony? _harmony;

    public override void Start(ICoreAPI api)
    {
        _config = LoadConfig(api);
    }

    // Behavior changes run on the server only: that is where the tweaked mods simulate.
    public override void StartServerSide(ICoreServerAPI api)
    {
        if (_config.BoilerLidBlowsOpen && BoilerLidRelief.Applies(api))
        {
            _harmony ??= new Harmony(HarmonyId);
            BoilerLidRelief.Patch(_harmony, api.Logger);
        }
    }

    // Lang files are loaded, mod assets included, before this phase on both sides.
    public override void AssetsLoaded(ICoreAPI api)
    {
        if (_config.BoilerLidBlowsOpen && BoilerLidRelief.Applies(api))
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
}
