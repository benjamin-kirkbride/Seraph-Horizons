using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.StallRoaster.Core;
using Vintagestory.API.Common;

namespace SeraphHorizons.Mod.StallRoaster;

/// <summary>
/// The stall roaster (#925; README "Ore processing: the stall roaster"): registers its block entity and holds its
/// settings (StallRoasterSettings in ModConfig/seraphhorizons.json) and the gameplay half of its rig
/// (<see cref="StallRoasterRig"/>). Its frame is the shared ore machine block (<see cref="BlockOreMachine"/>), its
/// cells the shared ones. With the <c>StallRoaster</c> switch off, or ore processing off (it roasts sulfide rock
/// into stall-roasted rock, which then does not exist), the server leaves its frame and recipe out of the game.
/// </summary>
public class StallRoasterSystem : ModSystem
{
    public const string Domain = "seraphhorizons";
    public const string Machine = "stallroaster";
    public static readonly AssetLocation RigAsset = new(Domain, "config/stallroaster-rig.json");

    public static readonly AssetLocation[] TypeAssets = [new(Domain, "blocktypes/stallroaster/frame.json")];
    public static readonly AssetLocation[] RecipeAssets = [new(Domain, "recipes/grid/stallroaster.json")];

    private ICoreAPI? _api;
    private StallRoasterConfig? _config;
    private StallRoasterRig? _rig;
    private bool _rigRead;

    public static StallRoasterSystem Of(ICoreAPI api) => api.ModLoader.GetModSystem<StallRoasterSystem>();

    public StallRoasterConfig Config => _config ??= LoadConfig(_api!);

    /// <summary>The rig's gameplay half, read once; null when it is missing or broken (logged).</summary>
    public StallRoasterRig? Rig
    {
        get
        {
            if (_rigRead || _api == null)
                return _rig;
            _rigRead = true;
            if (_api.Assets.TryGet(RigAsset) is not { } asset)
                _api.Logger.Error("[seraphhorizons] {0} is missing; the stall roaster cannot work", RigAsset);
            else
                try
                {
                    _rig = StallRoasterRig.Parse(asset.ToText());
                }
                catch (FormatException e)
                {
                    _api.Logger.Error("[seraphhorizons] {0} is broken, so the stall roaster cannot work: {1}", RigAsset, e.Message);
                }
            return _rig;
        }
    }

    /// <summary>Whether the stall roaster is in the game: its switch and ore processing's both on.</summary>
    public static bool Applies(ICoreAPI api)
    {
        var config = SeraphHorizonsSystem.ConfigFor(api);
        return config.StallRoaster && config.OreProcessing;
    }

    // The class is registered on both sides whatever the setting: the server decides whether the
    // blocks exist, and a client must know the class then.
    public override void Start(ICoreAPI api)
    {
        _api = api;
        api.RegisterBlockEntityClass("seraphhorizons.StallRoaster", typeof(BEStallRoaster));
    }

    public override void AssetsLoaded(ICoreAPI api)
    {
        if (api.Side == EnumAppSide.Server && !Applies(api))
            OreMachinesSystem.Disable(api, TypeAssets, RecipeAssets);
    }

    // The settings object is the one in the mod's loaded config, so the fixes hold for every reader.
    private static StallRoasterConfig LoadConfig(ICoreAPI api)
    {
        var config = SeraphHorizonsSystem.ConfigFor(api).StallRoasterSettings ?? new StallRoasterConfig();
        foreach (var fix in config.Sanitise())
            api.Logger.Warning($"[seraphhorizons] Stall roaster: ModConfig/{SeraphHorizonsSystem.ConfigFile}, StallRoasterSettings: {fix}");
        return config;
    }
}
