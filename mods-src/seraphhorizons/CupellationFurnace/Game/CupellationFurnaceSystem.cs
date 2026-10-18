using SeraphHorizons.Mod.CupellationFurnace.Core;
using SeraphHorizons.Mod.Machines;
using Vintagestory.API.Common;

namespace SeraphHorizons.Mod.CupellationFurnace;

/// <summary>
/// The cupellation furnace (#740; README "Ore processing: the cupellation furnace"): registers its block entity and
/// holds its settings (CupellationFurnaceSettings in ModConfig/seraphhorizons.json) and the gameplay half of its rig
/// (<see cref="CupellationFurnaceRig"/>). Its frame is the shared ore machine block (<see cref="BlockOreMachine"/>), its
/// cells the shared ones. With the <c>CupellationFurnace</c> switch off, or ore processing off (it parts base
/// bullion, which then does not exist), the server leaves its frame and recipe out of the game.
/// </summary>
public class CupellationFurnaceSystem : ModSystem
{
    public const string Domain = "seraphhorizons";
    public const string Machine = "cupellationfurnace";
    public static readonly AssetLocation RigAsset = new(Domain, "config/cupellationfurnace-rig.json");

    public static readonly AssetLocation[] TypeAssets = [new(Domain, "blocktypes/cupellationfurnace/frame.json")];
    public static readonly AssetLocation[] RecipeAssets = [new(Domain, "recipes/grid/cupellationfurnace.json")];

    private ICoreAPI? _api;
    private CupellationFurnaceConfig? _config;
    private CupellationFurnaceRig? _rig;
    private bool _rigRead;

    public static CupellationFurnaceSystem Of(ICoreAPI api) => api.ModLoader.GetModSystem<CupellationFurnaceSystem>();

    public CupellationFurnaceConfig Config => _config ??= LoadConfig(_api!);

    /// <summary>The rig's gameplay half, read once; null when it is missing or broken (logged).</summary>
    public CupellationFurnaceRig? Rig
    {
        get
        {
            if (_rigRead || _api == null)
                return _rig;
            _rigRead = true;
            if (_api.Assets.TryGet(RigAsset) is not { } asset)
                _api.Logger.Error("[seraphhorizons] {0} is missing; the cupellation furnace cannot work", RigAsset);
            else
                try
                {
                    _rig = CupellationFurnaceRig.Parse(asset.ToText());
                }
                catch (FormatException e)
                {
                    _api.Logger.Error("[seraphhorizons] {0} is broken, so the cupellation furnace cannot work: {1}", RigAsset, e.Message);
                }
            return _rig;
        }
    }

    /// <summary>Whether the cupellation furnace is in the game: its switch and ore processing's both on.</summary>
    public static bool Applies(ICoreAPI api)
    {
        var config = SeraphHorizonsSystem.ConfigFor(api);
        return config.CupellationFurnace && config.OreProcessing;
    }

    // The class is registered on both sides whatever the setting: the server decides whether the
    // blocks exist, and a client must know the class then.
    public override void Start(ICoreAPI api)
    {
        _api = api;
        api.RegisterBlockEntityClass("seraphhorizons.CupellationFurnace", typeof(BECupellationFurnace));
    }

    public override void AssetsLoaded(ICoreAPI api)
    {
        if (api.Side == EnumAppSide.Server && !Applies(api))
            OreMachinesSystem.Disable(api, TypeAssets, RecipeAssets);
    }

    // The settings object is the one in the mod's loaded config, so the fixes hold for every reader.
    private static CupellationFurnaceConfig LoadConfig(ICoreAPI api)
    {
        var config = SeraphHorizonsSystem.ConfigFor(api).CupellationFurnaceSettings ?? new CupellationFurnaceConfig();
        foreach (var fix in config.Sanitise())
            api.Logger.Warning($"[seraphhorizons] Cupellation furnace: ModConfig/{SeraphHorizonsSystem.ConfigFile}, CupellationFurnaceSettings: {fix}");
        return config;
    }
}
