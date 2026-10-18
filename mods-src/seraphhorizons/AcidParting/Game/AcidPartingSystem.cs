using SeraphHorizons.Mod.AcidParting.Core;
using SeraphHorizons.Mod.Machines;
using Vintagestory.API.Common;

namespace SeraphHorizons.Mod.AcidParting;

/// <summary>
/// The acid parting vessel (#931; README "Ore processing: the acid parting vessel"): registers its block entity and
/// holds its settings (AcidPartingSettings in ModConfig/seraphhorizons.json) and the gameplay half of its rig
/// (<see cref="AcidPartingRig"/>). Its frame is the shared ore machine block (<see cref="BlockOreMachine"/>), its cells
/// the shared ones. With the <c>AcidParting</c> switch off, or ore processing off (it parts dore, ore processing's
/// bullion, which then does not exist), the server leaves its frame and its recipes (the frame's, and aqua fortis
/// cooked from saltpeter and sulfuric acid) out of the game.
/// </summary>
public class AcidPartingSystem : ModSystem
{
    public const string Domain = "seraphhorizons";
    public const string Machine = "acidparting";
    public static readonly AssetLocation RigAsset = new(Domain, "config/acidparting-rig.json");

    public static readonly AssetLocation[] TypeAssets = [new(Domain, "blocktypes/acidparting/frame.json")];
    public static readonly AssetLocation[] RecipeAssets =
        [new(Domain, "recipes/grid/acidparting.json"), new(Domain, "recipes/cooking/aquafortis.json")];

    private ICoreAPI? _api;
    private AcidPartingConfig? _config;
    private AcidPartingRig? _rig;
    private bool _rigRead;

    public static AcidPartingSystem Of(ICoreAPI api) => api.ModLoader.GetModSystem<AcidPartingSystem>();

    public AcidPartingConfig Config => _config ??= LoadConfig(_api!);

    /// <summary>The rig's gameplay half, read once; null when it is missing or broken (logged).</summary>
    public AcidPartingRig? Rig
    {
        get
        {
            if (_rigRead || _api == null)
                return _rig;
            _rigRead = true;
            if (_api.Assets.TryGet(RigAsset) is not { } asset)
                _api.Logger.Error("[seraphhorizons] {0} is missing; the acid parting vessel cannot work", RigAsset);
            else
                try
                {
                    _rig = AcidPartingRig.Parse(asset.ToText());
                }
                catch (FormatException e)
                {
                    _api.Logger.Error("[seraphhorizons] {0} is broken, so the acid parting vessel cannot work: {1}", RigAsset, e.Message);
                }
            return _rig;
        }
    }

    /// <summary>Whether the acid parting vessel is in the game: its switch and ore processing's both on.</summary>
    public static bool Applies(ICoreAPI api)
    {
        var config = SeraphHorizonsSystem.ConfigFor(api);
        return config.AcidParting && config.OreProcessing;
    }

    // The class is registered on both sides whatever the setting: the server decides whether the
    // blocks exist, and a client must know the class then.
    public override void Start(ICoreAPI api)
    {
        _api = api;
        api.RegisterBlockEntityClass("seraphhorizons.AcidParting", typeof(BEAcidParting));
    }

    public override void AssetsLoaded(ICoreAPI api)
    {
        if (api.Side == EnumAppSide.Server && !Applies(api))
            OreMachinesSystem.Disable(api, TypeAssets, RecipeAssets);
    }

    // The settings object is the one in the mod's loaded config, so the fixes hold for every reader.
    private static AcidPartingConfig LoadConfig(ICoreAPI api)
    {
        var config = SeraphHorizonsSystem.ConfigFor(api).AcidPartingSettings ?? new AcidPartingConfig();
        foreach (var fix in config.Sanitise())
            api.Logger.Warning($"[seraphhorizons] Acid parting vessel: ModConfig/{SeraphHorizonsSystem.ConfigFile}, AcidPartingSettings: {fix}");
        return config;
    }
}
