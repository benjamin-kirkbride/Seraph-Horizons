using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.ParkesKettle.Core;
using Vintagestory.API.Common;

namespace SeraphHorizons.Mod.ParkesKettle;

/// <summary>
/// The Parkes kettle (#927; README "Ore processing: the Parkes kettle"): registers its block entity and holds its
/// settings (ParkesKettleSettings in ModConfig/seraphhorizons.json) and the gameplay half of its rig
/// (<see cref="ParkesKettleRig"/>). Its frame is the shared ore machine block (<see cref="BlockOreMachine"/>), its
/// cells the shared ones. With the <c>ParkesKettle</c> switch off, or ore processing off (it parts silver-lead
/// bullion, which then does not exist), the server leaves its frame and recipe out of the game.
/// </summary>
public class ParkesKettleSystem : ModSystem
{
    public const string Domain = "seraphhorizons";
    public const string Machine = "parkeskettle";
    public static readonly AssetLocation RigAsset = new(Domain, "config/parkeskettle-rig.json");

    public static readonly AssetLocation[] TypeAssets = [new(Domain, "blocktypes/parkeskettle/frame.json")];
    public static readonly AssetLocation[] RecipeAssets = [new(Domain, "recipes/grid/parkeskettle.json")];

    private ICoreAPI? _api;
    private ParkesKettleConfig? _config;
    private ParkesKettleRig? _rig;
    private bool _rigRead;

    public static ParkesKettleSystem Of(ICoreAPI api) => api.ModLoader.GetModSystem<ParkesKettleSystem>();

    public ParkesKettleConfig Config => _config ??= LoadConfig(_api!);

    /// <summary>The rig's gameplay half, read once; null when it is missing or broken (logged).</summary>
    public ParkesKettleRig? Rig
    {
        get
        {
            if (_rigRead || _api == null)
                return _rig;
            _rigRead = true;
            if (_api.Assets.TryGet(RigAsset) is not { } asset)
                _api.Logger.Error("[seraphhorizons] {0} is missing; the Parkes kettle cannot work", RigAsset);
            else
                try
                {
                    _rig = ParkesKettleRig.Parse(asset.ToText());
                }
                catch (FormatException e)
                {
                    _api.Logger.Error("[seraphhorizons] {0} is broken, so the Parkes kettle cannot work: {1}", RigAsset, e.Message);
                }
            return _rig;
        }
    }

    /// <summary>Whether the Parkes kettle is in the game: its switch and ore processing's both on.</summary>
    public static bool Applies(ICoreAPI api)
    {
        var config = SeraphHorizonsSystem.ConfigFor(api);
        return config.ParkesKettle && config.OreProcessing;
    }

    // The class is registered on both sides whatever the setting: the server decides whether the
    // blocks exist, and a client must know the class then.
    public override void Start(ICoreAPI api)
    {
        _api = api;
        api.RegisterBlockEntityClass("seraphhorizons.ParkesKettle", typeof(BEParkesKettle));
    }

    public override void AssetsLoaded(ICoreAPI api)
    {
        if (api.Side == EnumAppSide.Server && !Applies(api))
            OreMachinesSystem.Disable(api, TypeAssets, RecipeAssets);
    }

    // The settings object is the one in the mod's loaded config, so the fixes hold for every reader.
    private static ParkesKettleConfig LoadConfig(ICoreAPI api)
    {
        var config = SeraphHorizonsSystem.ConfigFor(api).ParkesKettleSettings ?? new ParkesKettleConfig();
        foreach (var fix in config.Sanitise())
            api.Logger.Warning($"[seraphhorizons] Parkes kettle: ModConfig/{SeraphHorizonsSystem.ConfigFile}, ParkesKettleSettings: {fix}");
        return config;
    }
}
