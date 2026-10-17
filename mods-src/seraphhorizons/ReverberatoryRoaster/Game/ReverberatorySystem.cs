using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.ReverberatoryRoaster.Core;
using Vintagestory.API.Common;

namespace SeraphHorizons.Mod.ReverberatoryRoaster;

/// <summary>
/// The reverberatory roaster (#736; README "Ore processing: the reverberatory roaster"): registers its block entity and
/// holds its settings (ReverberatorySettings in ModConfig/seraphhorizons.json) and the gameplay half of its rig
/// (<see cref="ReverberatoryRig"/>). Its frame is the shared ore machine block (<see cref="BlockOreMachine"/>), its cells
/// the shared ones. With the <c>ReverberatoryRoaster</c> switch off, or ore processing off (it roasts concentrate into
/// roasted concentrate, which then do not exist), the server leaves its frame and recipe out of the game.
/// </summary>
public class ReverberatorySystem : ModSystem
{
    public const string Domain = "seraphhorizons";
    public const string Machine = "reverberatory";
    public static readonly AssetLocation RigAsset = new(Domain, "config/reverberatory-rig.json");

    public static readonly AssetLocation[] TypeAssets = [new(Domain, "blocktypes/reverberatory/frame.json")];
    public static readonly AssetLocation[] RecipeAssets = [new(Domain, "recipes/grid/reverberatory.json")];

    private ICoreAPI? _api;
    private ReverberatoryConfig? _config;
    private ReverberatoryRig? _rig;
    private bool _rigRead;

    public static ReverberatorySystem Of(ICoreAPI api) => api.ModLoader.GetModSystem<ReverberatorySystem>();

    public ReverberatoryConfig Config => _config ??= LoadConfig(_api!);

    /// <summary>The rig's gameplay half, read once; null when it is missing or broken (logged).</summary>
    public ReverberatoryRig? Rig
    {
        get
        {
            if (_rigRead || _api == null)
                return _rig;
            _rigRead = true;
            if (_api.Assets.TryGet(RigAsset) is not { } asset)
                _api.Logger.Error("[seraphhorizons] {0} is missing; the reverberatory roaster cannot work", RigAsset);
            else
                try
                {
                    _rig = ReverberatoryRig.Parse(asset.ToText());
                }
                catch (FormatException e)
                {
                    _api.Logger.Error("[seraphhorizons] {0} is broken, so the reverberatory roaster cannot work: {1}", RigAsset, e.Message);
                }
            return _rig;
        }
    }

    /// <summary>Whether the reverberatory roaster is in the game: its switch and ore processing's both on.</summary>
    public static bool Applies(ICoreAPI api)
    {
        var config = SeraphHorizonsSystem.ConfigFor(api);
        return config.ReverberatoryRoaster && config.OreProcessing;
    }

    // The class is registered on both sides whatever the setting: the server decides whether the
    // blocks exist, and a client must know the class then.
    public override void Start(ICoreAPI api)
    {
        _api = api;
        api.RegisterBlockEntityClass("seraphhorizons.Reverberatory", typeof(BEReverberatory));
    }

    public override void AssetsLoaded(ICoreAPI api)
    {
        if (api.Side == EnumAppSide.Server && !Applies(api))
            OreMachinesSystem.Disable(api, TypeAssets, RecipeAssets);
    }

    // The settings object is the one in the mod's loaded config, so the fixes hold for every reader.
    private static ReverberatoryConfig LoadConfig(ICoreAPI api)
    {
        var config = SeraphHorizonsSystem.ConfigFor(api).ReverberatorySettings ?? new ReverberatoryConfig();
        foreach (var fix in config.Sanitise())
            api.Logger.Warning($"[seraphhorizons] Reverberatory roaster: ModConfig/{SeraphHorizonsSystem.ConfigFile}, ReverberatorySettings: {fix}");
        return config;
    }
}
