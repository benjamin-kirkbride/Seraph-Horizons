using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using SeraphHorizons.Mod.SquaringShear.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace SeraphHorizons.Mod.SquaringShear;

/// <summary>
/// The squaring shear: registers its classes and holds what its blocks share: its settings
/// (SquaringShearSettings in ModConfig/seraphhorizons.json) and the rig
/// (assets/seraphhorizons/config/squaringshear-rig.json). With the <c>SquaringShear</c> switch off the
/// server leaves its blocks, its half plate and its recipe out of the game (<see cref="Disable"/>). A
/// hand machine: no mechanical power and no oil.
/// </summary>
public class SquaringShearSystem : ModSystem
{
    public const string Domain = "seraphhorizons";
    public static readonly AssetLocation RigAsset = new(Domain, "config/squaringshear-rig.json");

    public static readonly AssetLocation[] TypeAssets =
    [
        new(Domain, "blocktypes/squaringshear/frame.json"),
        new(Domain, "blocktypes/squaringshear/ghost.json"),
        // what it cuts a plate into: the half plate exists only with the shear
        new(Domain, "itemtypes/halfplate.json"),
    ];

    public static readonly AssetLocation[] RecipeAssets =
    [
        new(Domain, "recipes/grid/squaringshear.json"),
    ];

    // The files open with a comment, which a plain Parse keeps as a token or trips over.
    private static readonly JsonLoadSettings IgnoreComments = new() { CommentHandling = CommentHandling.Ignore };

    private ICoreAPI? _api;
    private SquaringShearConfig? _config;
    private SquaringShearRig? _rig;
    private bool _rigLoaded;

    public static SquaringShearSystem Of(ICoreAPI api) => api.ModLoader.GetModSystem<SquaringShearSystem>();

    /// <summary>This side's settings. A client uses the server's figures where they matter (the
    /// block entity syncs them).</summary>
    public SquaringShearConfig Config => _config ??= LoadConfig(_api!);

    /// <summary>The rig, or null when the file is missing or broken (logged once); the frame then
    /// cannot be placed.</summary>
    public SquaringShearRig? Rig
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

    /// <summary>Whether the squaring shear is in the game: its switch is on.</summary>
    public static bool Applies(ICoreAPI api) => SeraphHorizonsSystem.ConfigFor(api).SquaringShear;

    // The classes are registered on both sides whatever the setting: the server decides whether the
    // blocks exist, and a client must know the classes then.
    public override void Start(ICoreAPI api)
    {
        _api = api;
        _config = LoadConfig(api);
        api.RegisterBlockClass("seraphhorizons.SquaringShear", typeof(BlockSquaringShear));
        api.RegisterBlockClass("seraphhorizons.SquaringShearGhost", typeof(BlockSquaringShearGhost));
        api.RegisterBlockEntityClass("seraphhorizons.SquaringShear", typeof(BESquaringShear));
        api.RegisterBlockEntityClass("seraphhorizons.SquaringShearGhost", typeof(BESquaringShearGhost));
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
        UnlinkText();
    }

    private static readonly Regex ShearLink =
        new("<a href=\"(?:handbook://block-seraphhorizons:squaringshear|handbook://item-seraphhorizons:halfplate|handbooksearch://squaring shear|handbooksearch://half plate)[^\"]*\">(.*?)</a>", RegexOptions.Compiled);

    /// <summary>With the shear off its blocks and the half plate have no handbook page, so the mod's
    /// own text names them without a link. Safe to run twice, as singleplayer's two sides do on
    /// shared entries.</summary>
    public static void UnlinkText()
    {
        foreach (var translations in Lang.AvailableLanguages.Values)
        {
            var entries = translations.GetAllEntries();
            foreach (var key in entries.Keys.Where(k => k.StartsWith(Domain + ":", StringComparison.Ordinal)).ToList())
            {
                var text = entries[key];
                if (text.Contains("handbook://", StringComparison.Ordinal) && ShearLink.IsMatch(text))
                    entries[key] = ShearLink.Replace(text, "$1");
            }
        }
    }

    /// <summary>Leaves the squaring shear out of the game: marks its block types, the half plate's
    /// item type and its recipe disabled before the game loads them.</summary>
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

    private static SquaringShearRig? LoadRig(ICoreAPI api)
    {
        var asset = api.Assets.TryGet(RigAsset);
        if (asset == null)
        {
            api.Logger.Error("[seraphhorizons] Squaring shear: {0} is missing; the squaring shear cannot be placed", RigAsset);
            return null;
        }
        try
        {
            return SquaringShearRig.Parse(asset.ToText());
        }
        catch (FormatException e)
        {
            api.Logger.Error("[seraphhorizons] Squaring shear: {0} is broken, so the squaring shear cannot be placed: {1}", RigAsset, e.Message);
            return null;
        }
    }

    // The settings object is the one in the mod's loaded config, so the fixes hold for every reader.
    private static SquaringShearConfig LoadConfig(ICoreAPI api)
    {
        var config = SeraphHorizonsSystem.ConfigFor(api).SquaringShearSettings ?? new SquaringShearConfig();
        foreach (var fix in config.Sanitise())
            api.Logger.Warning($"[seraphhorizons] Squaring shear: ModConfig/{SeraphHorizonsSystem.ConfigFile}, SquaringShearSettings: {fix}");
        return config;
    }
}
