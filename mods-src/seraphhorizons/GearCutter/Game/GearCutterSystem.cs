using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using SeraphHorizons.Mod.GearCutter.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

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
        if (Applies(api))
            return;
        if (api.Side == EnumAppSide.Server)
            Disable(api);
        // Both sides, each from its own setting, as the handbook is the client's.
        UnlinkText(api.Logger);
    }

    /// <summary>The machine oil page without the cutter, when it is off: its list of machines, and
    /// its drain and its exception to the dry rule.</summary>
    public static readonly LangEdit[] LangEdits =
    [
        new("en", Domain + ":machineoil-text",
            ", the <a href=\"handbook://block-seraphhorizons:rosser-frame-north\">rosser</a> and the "
            + "<a href=\"handbook://block-seraphhorizons:gearcutter-frame-north\">gear cutter</a>.",
            " and the <a href=\"handbook://block-seraphhorizons:rosser-frame-north\">rosser</a>."),
        new("en", Domain + ":machineoil-text",
            ", and the bucking sawmill and the rosser 2 points for each log stored in a trunk, and the gear cutter 10 points a "
            + "gear (20 a large one). The gear cutter is the exception to the dry rule: dry or oiled it takes the same power, "
            + "but its oil spares its cutter kit, which wears faster as the tank runs down and breaks on the first gear cut dry.",
            ", and the bucking sawmill and the rosser 2 points for each log stored in a trunk."),
    ];

    private static readonly Regex CutterLink =
        new("<a href=\"handbook://(?:block|item)-seraphhorizons:gearcutter[^\"]*\">(.*?)</a>", RegexOptions.Compiled);

    /// <summary>With the cutter off its blocks and items have no handbook page, so the mod's own text
    /// names them without a link (the gear article, the machine oil page). Safe to run twice, as
    /// singleplayer's two sides do on shared entries.</summary>
    public static void UnlinkText(ILogger logger)
    {
        LangText.Apply(LangEdits, "seraphhorizons", logger);
        foreach (var translations in Lang.AvailableLanguages.Values)
        {
            var entries = translations.GetAllEntries();
            foreach (var key in entries.Keys.Where(k => k.StartsWith(Domain + ":", StringComparison.Ordinal)).ToList())
            {
                var text = entries[key];
                if (text.Contains("handbook://", StringComparison.Ordinal) && CutterLink.IsMatch(text))
                    entries[key] = CutterLink.Replace(text, "$1");
            }
        }
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
