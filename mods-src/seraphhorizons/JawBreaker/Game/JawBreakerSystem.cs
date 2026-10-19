using SeraphHorizons.Mod.JawBreaker.Core;
using SeraphHorizons.Mod.Machines;
using Vintagestory.API.Common;

namespace SeraphHorizons.Mod.JawBreaker;

/// <summary>
/// The jaw breaker (#912; README "Jaw breaker"): registers its block entity and holds its settings
/// (JawBreakerSettings in ModConfig/seraphhorizons.json). Its frame is the shared ore machine block
/// (<see cref="BlockMachine"/>), its cells the shared ones. With the <c>JawBreaker</c> switch off, or
/// ore processing off (it breaks raw ore into crushed ore, which then does not exist), the server leaves
/// its frame and recipe out of the game.
/// </summary>
public class JawBreakerSystem : ModSystem
{
    public const string Domain = "seraphhorizons";
    public const string Machine = "jawbreaker";

    public static readonly AssetLocation[] TypeAssets = [new(Domain, "blocktypes/jawbreaker/frame.json")];
    public static readonly AssetLocation[] RecipeAssets = [new(Domain, "recipes/grid/jawbreaker.json")];

    private ICoreAPI? _api;
    private JawBreakerConfig? _config;

    public static JawBreakerSystem Of(ICoreAPI api) => api.ModLoader.GetModSystem<JawBreakerSystem>();

    public JawBreakerConfig Config => _config ??= LoadConfig(_api!);

    /// <summary>Whether the jaw breaker is in the game: its switch and ore processing's both on.</summary>
    public static bool Applies(ICoreAPI api)
    {
        var config = SeraphHorizonsSystem.ConfigFor(api);
        return config.JawBreaker && config.OreProcessing;
    }

    // The class is registered on both sides whatever the setting: the server decides whether the
    // blocks exist, and a client must know the class then.
    public override void Start(ICoreAPI api)
    {
        _api = api;
        api.RegisterBlockEntityClass("seraphhorizons.JawBreaker", typeof(BEJawBreaker));
    }

    public override void AssetsLoaded(ICoreAPI api)
    {
        if (api.Side == EnumAppSide.Server && !Applies(api))
            MachinesSystem.Disable(api, TypeAssets, RecipeAssets);
    }

    // The settings object is the one in the mod's loaded config, so the fixes hold for every reader.
    private static JawBreakerConfig LoadConfig(ICoreAPI api)
    {
        var config = SeraphHorizonsSystem.ConfigFor(api).JawBreakerSettings ?? new JawBreakerConfig();
        foreach (var fix in config.Sanitise())
            api.Logger.Warning($"[seraphhorizons] Jaw breaker: ModConfig/{SeraphHorizonsSystem.ConfigFile}, JawBreakerSettings: {fix}");
        return config;
    }
}
