using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.LiquationFurnace.Core;
using Vintagestory.API.Common;

namespace SeraphHorizons.Mod.LiquationFurnace;

/// <summary>
/// The liquation furnace (#929; README "Ore processing: the liquation furnace"): registers its block entity and holds its
/// settings (LiquationFurnaceSettings in ModConfig/seraphhorizons.json) and the gameplay half of its rig
/// (<see cref="LiquationFurnaceRig"/>). Its frame is the shared machine block (<see cref="BlockMachine"/>), its
/// cells the shared ones. With the <c>LiquationFurnace</c> switch off, or ore processing off (it parts the tin-lead
/// bullion ore processing smelts teallite and franckeite to), the server leaves its frame and recipe out of the game.
/// </summary>
public class LiquationFurnaceSystem : ModSystem
{
    public const string Domain = "seraphhorizons";
    public const string Machine = "liquationfurnace";
    public static readonly AssetLocation RigAsset = new(Domain, "config/liquationfurnace-rig.json");

    public static readonly AssetLocation[] TypeAssets = [new(Domain, "blocktypes/liquationfurnace/frame.json")];
    public static readonly AssetLocation[] RecipeAssets = [new(Domain, "recipes/grid/liquationfurnace.json")];

    private ICoreAPI? _api;
    private LiquationFurnaceConfig? _config;
    private LiquationFurnaceRig? _rig;
    private bool _rigRead;

    public static LiquationFurnaceSystem Of(ICoreAPI api) => api.ModLoader.GetModSystem<LiquationFurnaceSystem>();

    public LiquationFurnaceConfig Config => _config ??= LoadConfig(_api!);

    /// <summary>The rig's gameplay half, read once; null when it is missing or broken (logged).</summary>
    public LiquationFurnaceRig? Rig
    {
        get
        {
            if (_rigRead || _api == null)
                return _rig;
            _rigRead = true;
            if (_api.Assets.TryGet(RigAsset) is not { } asset)
                _api.Logger.Error("[seraphhorizons] {0} is missing; the liquation furnace cannot work", RigAsset);
            else
                try
                {
                    _rig = LiquationFurnaceRig.Parse(asset.ToText());
                }
                catch (FormatException e)
                {
                    _api.Logger.Error("[seraphhorizons] {0} is broken, so the liquation furnace cannot work: {1}", RigAsset, e.Message);
                }
            return _rig;
        }
    }

    /// <summary>Whether the liquation furnace is in the game: its switch and ore processing's both on.</summary>
    public static bool Applies(ICoreAPI api)
    {
        var config = SeraphHorizonsSystem.ConfigFor(api);
        return config.LiquationFurnace && config.OreProcessing;
    }

    // The class is registered on both sides whatever the setting: the server decides whether the
    // blocks exist, and a client must know the class then.
    public override void Start(ICoreAPI api)
    {
        _api = api;
        api.RegisterBlockEntityClass("seraphhorizons.LiquationFurnace", typeof(BELiquationFurnace));
    }

    public override void AssetsLoaded(ICoreAPI api)
    {
        if (api.Side == EnumAppSide.Server && !Applies(api))
            MachinesSystem.Disable(api, TypeAssets, RecipeAssets);
    }

    // The settings object is the one in the mod's loaded config, so the fixes hold for every reader.
    private static LiquationFurnaceConfig LoadConfig(ICoreAPI api)
    {
        var config = SeraphHorizonsSystem.ConfigFor(api).LiquationFurnaceSettings ?? new LiquationFurnaceConfig();
        foreach (var fix in config.Sanitise())
            api.Logger.Warning($"[seraphhorizons] Liquation furnace: ModConfig/{SeraphHorizonsSystem.ConfigFile}, LiquationFurnaceSettings: {fix}");
        return config;
    }
}
