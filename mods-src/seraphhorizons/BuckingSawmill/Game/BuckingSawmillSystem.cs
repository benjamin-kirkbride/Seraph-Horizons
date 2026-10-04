using SeraphHorizons.Mod.BuckingSawmill.Core;
using System.Text;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;

namespace SeraphHorizons.Mod.BuckingSawmill;

/// <summary>
/// The bucking sawmill: registers its classes and holds what its blocks share: its settings
/// (BuckingSawmillSettings in ModConfig/seraphhorizons.json), the rig
/// (assets/seraphhorizons/config/buckingmill-rig.json: the footprint and anchor points) and the
/// bridge to Logging Expanded. The mill is built from Immersive Woodworking's parts and cuts
/// Logging Expanded's trunks, so with the switch off, or either mod missing, the server leaves its
/// blocks and recipe out of the game (<see cref="Disable"/>).
/// </summary>
public class BuckingSawmillSystem : ModSystem
{
    public const string Domain = "seraphhorizons";
    public const string IwModId = "immersivewoodworking";
    public const string LeModId = "loggingmod";
    public static readonly AssetLocation RigAsset = new(Domain, "config/buckingmill-rig.json");
    public static readonly AssetLocation RecipeAsset = new(Domain, "recipes/grid/buckingmill.json");
    public static readonly AssetLocation[] BlockAssets =
    [
        new(Domain, "blocktypes/buckingmill/frame.json"),
        new(Domain, "blocktypes/buckingmill/ghost.json"),
        new(Domain, "blocktypes/buckingmill/ghostpower.json"),
    ];

    private ICoreAPI? _api;
    private MillConfig? _config;
    private Rig? _rig;
    private bool _rigLoaded;
    private LoggingBridge? _logging;
    private bool _loggingResolved;

    public static BuckingSawmillSystem Of(ICoreAPI api) => api.ModLoader.GetModSystem<BuckingSawmillSystem>();

    /// <summary>This side's settings.</summary>
    public MillConfig Config => _config ??= LoadConfig(_api!);

    /// <summary>The rig, or null when the file is missing or broken (logged once); the frame then
    /// cannot be placed.</summary>
    public Rig? Rig
    {
        get
        {
            if (!_rigLoaded && _api != null)
            {
                _rigLoaded = true;
                _rig = LoadRig(_api);
            }
            return _rig;
        }
    }

    /// <summary>Logging Expanded, or null when something the mill needs is missing (logged once).</summary>
    public LoggingBridge? Logging
    {
        get
        {
            if (!_loggingResolved && _api != null)
            {
                _loggingResolved = true;
                _logging = LoggingBridge.Resolve(_api, out var problems);
                if (_logging == null)
                    _api.Logger.Warning("[seraphhorizons] Bucking sawmill: Logging Expanded is not as expected, so the mill takes and cuts no trunks: {0}",
                        string.Join("; ", problems));
            }
            return _logging;
        }
    }

    /// <summary>Whether the mill is in the game: its switch is on and both mods are installed.</summary>
    public static bool Applies(ICoreAPI api) =>
        SeraphHorizonsSystem.ConfigFor(api).BuckingSawmill
        && api.ModLoader.IsModEnabled(IwModId) && api.ModLoader.IsModEnabled(LeModId);

    // The classes are registered on both sides whatever the setting: the server decides whether the
    // blocks exist, and a client must know the classes then.
    public override void Start(ICoreAPI api)
    {
        _api = api;
        _config = LoadConfig(api);
        api.RegisterBlockClass("seraphhorizons.BuckingMill", typeof(BlockBuckingMill));
        api.RegisterBlockClass("seraphhorizons.BuckingMillGhost", typeof(BlockMillGhost));
        api.RegisterBlockClass("seraphhorizons.BuckingMillGhostPower", typeof(BlockMillGhostPower));
        api.RegisterBlockEntityClass("seraphhorizons.BuckingMill", typeof(BEBuckingMill));
        api.RegisterBlockEntityClass("seraphhorizons.BuckingMillGhost", typeof(BEMillGhost));
        api.RegisterBlockEntityBehaviorClass("seraphhorizons.BuckingMillMP", typeof(BEBehaviorMillMP));
    }

    // The rig is loaded here so the collision box lookups, which can run off the main thread, find
    // it ready. Blocktypes and recipes are read from the assets later in this phase (the game's
    // loaders run at 0.2 and 1, this system at the default 0.1), on the server only.
    public override void AssetsLoaded(ICoreAPI api)
    {
        _ = Rig;
        if (api.Side == EnumAppSide.Server && !Applies(api))
            Disable(api);
    }

    /// <summary>Leaves the mill out of the game: marks its blocktypes and its recipe disabled
    /// before the game loads them.</summary>
    public static void Disable(ICoreAPI api)
    {
        foreach (var location in BlockAssets)
        {
            if (api.Assets.TryGet(location) is not { } asset)
                continue;
            var json = JObject.Parse(asset.ToText());
            json["enabled"] = false;
            asset.Data = Encoding.UTF8.GetBytes(json.ToString());
        }
        if (api.Assets.TryGet(RecipeAsset) is { } recipes)
        {
            var json = JArray.Parse(recipes.ToText());
            foreach (var recipe in json.OfType<JObject>())
                recipe["enabled"] = false;
            recipes.Data = Encoding.UTF8.GetBytes(json.ToString());
        }
    }

    private static Rig? LoadRig(ICoreAPI api)
    {
        var asset = api.Assets.TryGet(RigAsset);
        if (asset == null)
        {
            api.Logger.Error("[seraphhorizons] Bucking sawmill: {0} is missing; the mill cannot be placed", RigAsset);
            return null;
        }
        try
        {
            return Rig.Parse(asset.ToText());
        }
        catch (FormatException e)
        {
            api.Logger.Error("[seraphhorizons] Bucking sawmill: {0} is broken, so the mill cannot be placed: {1}", RigAsset, e.Message);
            return null;
        }
    }

    // The settings object is the one in the mod's loaded config, so the fixes hold for every reader.
    private static MillConfig LoadConfig(ICoreAPI api)
    {
        var config = SeraphHorizonsSystem.ConfigFor(api).BuckingSawmillSettings ?? new MillConfig();
        foreach (var fix in config.Sanitise())
            api.Logger.Warning($"[seraphhorizons] Bucking sawmill: ModConfig/{SeraphHorizonsSystem.ConfigFile}, BuckingSawmillSettings: {fix}");
        return config;
    }
}
