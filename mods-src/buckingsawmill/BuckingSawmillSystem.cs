using BuckingSawmill.Core;
using Vintagestory.API.Common;

namespace BuckingSawmill;

/// <summary>
/// Registers the mill's classes and holds what its blocks share: the settings in
/// ModConfig/buckingsawmill.json, the rig (assets/buckingsawmill/config/rig.json: the footprint
/// and anchor points) and the bridge to Logging Expanded.
/// </summary>
public class BuckingSawmillSystem : ModSystem
{
    public const string Domain = "buckingsawmill";
    public const string ConfigFile = "buckingsawmill.json";
    public static readonly AssetLocation RigAsset = new(Domain, "config/rig.json");

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
                    _api.Logger.Warning("[buckingsawmill] Logging Expanded is not as expected, so the mill takes and cuts no trunks: {0}",
                        string.Join("; ", problems));
            }
            return _logging;
        }
    }

    public override void Start(ICoreAPI api)
    {
        _api = api;
        _config = LoadConfig(api);
        api.RegisterBlockClass("buckingsawmill.Frame", typeof(BlockBuckingMill));
        api.RegisterBlockClass("buckingsawmill.Ghost", typeof(BlockMillGhost));
        api.RegisterBlockClass("buckingsawmill.GhostPower", typeof(BlockMillGhostPower));
        api.RegisterBlockEntityClass("buckingsawmill.Frame", typeof(BEBuckingMill));
        api.RegisterBlockEntityClass("buckingsawmill.Ghost", typeof(BEMillGhost));
        api.RegisterBlockEntityBehaviorClass("buckingsawmill.MP", typeof(BEBehaviorMillMP));
    }

    // Loaded here so the collision box lookups, which can run off the main thread, find it ready.
    public override void AssetsLoaded(ICoreAPI api) => _ = Rig;

    private static Rig? LoadRig(ICoreAPI api)
    {
        var asset = api.Assets.TryGet(RigAsset);
        if (asset == null)
        {
            api.Logger.Error("[buckingsawmill] {0} is missing; the mill cannot be placed", RigAsset);
            return null;
        }
        try
        {
            return Rig.Parse(asset.ToText());
        }
        catch (FormatException e)
        {
            api.Logger.Error("[buckingsawmill] {0} is broken, so the mill cannot be placed: {1}", RigAsset, e.Message);
            return null;
        }
    }

    private static MillConfig LoadConfig(ICoreAPI api)
    {
        MillConfig? config;
        try
        {
            config = api.LoadModConfig<MillConfig>(ConfigFile);
        }
        catch (Exception e)
        {
            api.Logger.Error($"[buckingsawmill] Could not read ModConfig/{ConfigFile}, using the defaults: {e.Message}");
            return new MillConfig();
        }
        config ??= new MillConfig();
        foreach (var fix in config.Sanitise())
            api.Logger.Warning($"[buckingsawmill] ModConfig/{ConfigFile}: {fix}");
        // Writes back settings added since the file was made.
        api.StoreModConfig(config, ConfigFile);
        return config;
    }
}
