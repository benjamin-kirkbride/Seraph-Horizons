using System.Text;
using Newtonsoft.Json.Linq;
using SeraphHorizons.Mod.GearCutter.Core;
using Vintagestory.API.Common;

namespace SeraphHorizons.Mod.GearCutter;

/// <summary>
/// The gear cutter (#480, #481): registers its classes and holds what its blocks share: its
/// settings (GearCutterSettings in ModConfig/seraphhorizons.json) and the rig
/// (assets/seraphhorizons/config/gearcutter-rig.json). With the <c>GearCutter</c> switch off the
/// server leaves its blocks, its new items and their recipes out of the game (<see cref="Disable"/>).
/// </summary>
public class GearCutterSystem : ModSystem
{
    public const string Domain = "seraphhorizons";
    public static readonly AssetLocation RigAsset = new(Domain, "config/gearcutter-rig.json");

    public static readonly AssetLocation[] TypeAssets =
    [
        new(Domain, "blocktypes/gearcutter/frame.json"),
        new(Domain, "blocktypes/gearcutter/ghost.json"),
        new(Domain, "blocktypes/gearcutter/ghostpower.json"),
        new(Domain, "itemtypes/gearcutter/spindle.json"),
        new(Domain, "itemtypes/gearcutter/feedscrew.json"),
        new(Domain, "itemtypes/gearcutter/liftcam.json"),
        new(Domain, "itemtypes/gearcutter/index.json"),
        new(Domain, "itemtypes/gearcutter/kit.json"),
    ];

    public static readonly AssetLocation[] RecipeAssets =
    [
        new(Domain, "recipes/grid/gearcutter.json"),
        new(Domain, "recipes/smithing/gearcutter.json"),
    ];

    // The files open with a comment, which a plain Parse keeps as a token or trips over.
    private static readonly JsonLoadSettings IgnoreComments = new() { CommentHandling = CommentHandling.Ignore };

    private ICoreAPI? _api;
    private GearCutterConfig? _config;
    private GearCutterRig? _rig;
    private bool _rigLoaded;

    public static GearCutterSystem Of(ICoreAPI api) => api.ModLoader.GetModSystem<GearCutterSystem>();

    /// <summary>This side's settings. A client uses the server's figures where they matter (the
    /// block entity syncs them).</summary>
    public GearCutterConfig Config => _config ??= LoadConfig(_api!);

    /// <summary>The rig, or null when the file is missing or broken (logged once); the frame then
    /// cannot be placed.</summary>
    public GearCutterRig? Rig
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

    /// <summary>Whether the gear cutter is in the game: its switch is on.</summary>
    public static bool Applies(ICoreAPI api) => SeraphHorizonsSystem.ConfigFor(api).GearCutter;

    // The classes are registered on both sides whatever the setting: the server decides whether the
    // blocks exist, and a client must know the classes then.
    public override void Start(ICoreAPI api)
    {
        _api = api;
        _config = LoadConfig(api);
        api.RegisterBlockClass("seraphhorizons.GearCutter", typeof(BlockGearCutter));
        api.RegisterBlockClass("seraphhorizons.GearCutterGhost", typeof(BlockGearCutterGhost));
        api.RegisterBlockClass("seraphhorizons.GearCutterGhostPower", typeof(BlockGearCutterGhostPower));
        api.RegisterBlockEntityClass("seraphhorizons.GearCutter", typeof(BEGearCutter));
        api.RegisterBlockEntityClass("seraphhorizons.GearCutterGhost", typeof(BEGearCutterGhost));
        api.RegisterBlockEntityBehaviorClass("seraphhorizons.GearCutterMP", typeof(BEBehaviorGearCutterMP));
    }

    // The rig is loaded here so the collision box lookups, which can run off the main thread, find
    // it ready. Types and recipes are read from the assets later in this phase (the game's loaders
    // run at 0.2 and 1, this system at the default 0.1), on the server only.
    public override void AssetsLoaded(ICoreAPI api)
    {
        _ = Rig;
        if (api.Side == EnumAppSide.Server && !Applies(api))
            Disable(api);
    }

    /// <summary>Leaves the gear cutter out of the game: marks its block and item types and its
    /// recipes disabled before the game loads them.</summary>
    public static void Disable(ICoreAPI api)
    {
        foreach (var location in TypeAssets)
        {
            if (api.Assets.TryGet(location) is not { } asset)
                continue;
            var json = JObject.Parse(asset.ToText(), IgnoreComments);
            json["enabled"] = false;
            asset.Data = Encoding.UTF8.GetBytes(json.ToString());
        }
        foreach (var location in RecipeAssets)
        {
            if (api.Assets.TryGet(location) is not { } asset)
                continue;
            var json = JArray.Parse(asset.ToText(), IgnoreComments);
            foreach (var recipe in json.OfType<JObject>())
                recipe["enabled"] = false;
            asset.Data = Encoding.UTF8.GetBytes(json.ToString());
        }
    }

    private static GearCutterRig? LoadRig(ICoreAPI api)
    {
        var asset = api.Assets.TryGet(RigAsset);
        if (asset == null)
        {
            api.Logger.Error("[seraphhorizons] Gear cutter: {0} is missing; the gear cutter cannot be placed", RigAsset);
            return null;
        }
        try
        {
            return GearCutterRig.Parse(asset.ToText());
        }
        catch (FormatException e)
        {
            api.Logger.Error("[seraphhorizons] Gear cutter: {0} is broken, so the gear cutter cannot be placed: {1}", RigAsset, e.Message);
            return null;
        }
    }

    // The settings object is the one in the mod's loaded config, so the fixes hold for every reader.
    private static GearCutterConfig LoadConfig(ICoreAPI api)
    {
        var config = SeraphHorizonsSystem.ConfigFor(api).GearCutterSettings ?? new GearCutterConfig();
        foreach (var fix in config.Sanitise())
            api.Logger.Warning($"[seraphhorizons] Gear cutter: ModConfig/{SeraphHorizonsSystem.ConfigFile}, GearCutterSettings: {fix}");
        return config;
    }
}
