using System.Text;
using Newtonsoft.Json.Linq;
using SeraphHorizons.Mod.PicklingTub.Core;
using Vintagestory.API.Common;

namespace SeraphHorizons.Mod.PicklingTub;

/// <summary>
/// The pickling tub (#476) and its brine bath (#482), part of gear reclamation
/// (<c>GearReclamation</c>): a pitch-lined wooden tub that holds a liquid and a batch of gears, and
/// turns them by the rules in <c>PicklingTubSettings</c> (<see cref="PicklingTubConfig"/>): the
/// acids pickle degreased gears and dip steel ones bare, and eat a batch left too long; brine rusts
/// steel gears into the game's rusty gears, a share of them to bits. Registers the classes on both
/// sides; on the server, with the switch off, leaves the tub, its recipe and the bare steel gear
/// out of the game, and without Immersive Woodworking (whose bark tar lines it) only the recipe.
/// </summary>
public class PicklingTubSystem : ModSystem
{
    public const string Domain = "seraphhorizons";
    public const string IwModId = "immersivewoodworking";
    public static readonly AssetLocation BlockAsset = new(Domain, "blocktypes/picklingtub.json");
    public static readonly AssetLocation BareGearAsset = new(Domain, "itemtypes/gear-steel-bare.json");
    public static readonly AssetLocation RecipeAsset = new(Domain, "recipes/grid/picklingtub.json");

    private ICoreAPI? _api;
    private PicklingTubConfig? _config;
    private TubRuleBook? _rules;

    public static PicklingTubSystem Of(ICoreAPI api) => api.ModLoader.GetModSystem<PicklingTubSystem>();

    /// <summary>The switch on this side (the server's decides whether the tub exists).</summary>
    public bool On => _api != null && SeraphHorizonsSystem.ConfigFor(_api).GearReclamation;

    /// <summary>This side's settings, sanitised.</summary>
    public PicklingTubConfig Config => _config ??= LoadConfig(_api!);

    public TubRuleBook Rules => _rules ??= new TubRuleBook(Config);

    public override void Start(ICoreAPI api)
    {
        _api = api;
        api.RegisterBlockClass("seraphhorizons.PicklingTub", typeof(BlockPicklingTub));
        api.RegisterBlockEntityClass("seraphhorizons.PicklingTub", typeof(BEPicklingTub));
    }

    // Blocktypes, itemtypes and recipes are read from the assets later in this phase (the game's
    // loaders run at 0.2 and 1, this system at the default 0.1), on the server only.
    public override void AssetsLoaded(ICoreAPI api)
    {
        if (api.Side != EnumAppSide.Server)
            return;
        if (!On)
        {
            SetEnabled(api, BlockAsset, false);
            SetEnabled(api, BareGearAsset, false);
            DisableRecipes(api);
        }
        else if (!api.ModLoader.IsModEnabled(IwModId))
        {
            api.Logger.Notification("[seraphhorizons] Pickling tub: Immersive Woodworking is not installed, so the tub has no recipe (its lining is bark tar)");
            DisableRecipes(api);
        }
    }

    private static void SetEnabled(ICoreAPI api, AssetLocation location, bool enabled)
    {
        if (api.Assets.TryGet(location) is not { } asset)
            return;
        var json = JObject.Parse(asset.ToText());
        json["enabled"] = enabled;
        asset.Data = Encoding.UTF8.GetBytes(json.ToString());
    }

    private static void DisableRecipes(ICoreAPI api)
    {
        if (api.Assets.TryGet(RecipeAsset) is not { } recipes)
            return;
        var json = JArray.Parse(recipes.ToText());
        foreach (var recipe in json.OfType<JObject>())
            recipe["enabled"] = false;
        recipes.Data = Encoding.UTF8.GetBytes(json.ToString());
    }

    // The settings object is the one in the mod's loaded config, so the fixes hold for every reader.
    private static PicklingTubConfig LoadConfig(ICoreAPI api)
    {
        var config = SeraphHorizonsSystem.ConfigFor(api).PicklingTubSettings ?? new PicklingTubConfig();
        foreach (var fix in config.Sanitise())
            api.Logger.Warning($"[seraphhorizons] Pickling tub: ModConfig/{SeraphHorizonsSystem.ConfigFile}, PicklingTubSettings: {fix}");
        return config;
    }
}
