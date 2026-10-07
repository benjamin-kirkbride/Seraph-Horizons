using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using SeraphHorizons.Mod.PressBrake.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace SeraphHorizons.Mod.PressBrake;

/// <summary>
/// The press brake: registers its classes and holds what its blocks share: its settings
/// (PressBrakeSettings in ModConfig/seraphhorizons.json) and the rig
/// (assets/seraphhorizons/config/pressbrake-rig.json). With the <c>PressBrake</c> switch off the
/// server leaves its blocks and its recipe out of the game (<see cref="Disable"/>). A hand machine:
/// no mechanical power and no oil.
/// </summary>
public class PressBrakeSystem : ModSystem
{
    public const string Domain = "seraphhorizons";
    public static readonly AssetLocation RigAsset = new(Domain, "config/pressbrake-rig.json");

    public static readonly AssetLocation[] TypeAssets =
    [
        new(Domain, "blocktypes/pressbrake/frame.json"),
        new(Domain, "blocktypes/pressbrake/ghost.json"),
    ];

    public static readonly AssetLocation[] RecipeAssets =
    [
        new(Domain, "recipes/grid/pressbrake.json"),
    ];

    // The files open with a comment, which a plain Parse keeps as a token or trips over.
    private static readonly JsonLoadSettings IgnoreComments = new() { CommentHandling = CommentHandling.Ignore };

    private ICoreAPI? _api;
    private PressBrakeConfig? _config;
    private PressBrakeRig? _rig;
    private bool _rigLoaded;

    public static PressBrakeSystem Of(ICoreAPI api) => api.ModLoader.GetModSystem<PressBrakeSystem>();

    /// <summary>This side's settings. A client uses the server's figures where they matter (the
    /// block entity syncs them).</summary>
    public PressBrakeConfig Config => _config ??= LoadConfig(_api!);

    /// <summary>The rig, or null when the file is missing or broken (logged once); the frame then
    /// cannot be placed.</summary>
    public PressBrakeRig? Rig
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

    /// <summary>Whether the press brake is in the game: its switch is on.</summary>
    public static bool Applies(ICoreAPI api) => SeraphHorizonsSystem.ConfigFor(api).PressBrake;

    // The classes are registered on both sides whatever the setting: the server decides whether the
    // blocks exist, and a client must know the classes then.
    public override void Start(ICoreAPI api)
    {
        _api = api;
        _config = LoadConfig(api);
        api.RegisterBlockClass("seraphhorizons.PressBrake", typeof(BlockPressBrake));
        api.RegisterBlockClass("seraphhorizons.PressBrakeGhost", typeof(BlockPressBrakeGhost));
        api.RegisterBlockEntityClass("seraphhorizons.PressBrake", typeof(BEPressBrake));
        api.RegisterBlockEntityClass("seraphhorizons.PressBrakeGhost", typeof(BEPressBrakeGhost));
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

    private static readonly Regex BrakeLink =
        new("<a href=\"handbook://block-seraphhorizons:pressbrake[^\"]*\">(.*?)</a>", RegexOptions.Compiled);

    /// <summary>With the brake off its blocks have no handbook page, so the mod's own text names it
    /// without a link. Safe to run twice, as singleplayer's two sides do on shared entries.</summary>
    public static void UnlinkText()
    {
        foreach (var translations in Lang.AvailableLanguages.Values)
        {
            var entries = translations.GetAllEntries();
            foreach (var key in entries.Keys.Where(k => k.StartsWith(Domain + ":", StringComparison.Ordinal)).ToList())
            {
                var text = entries[key];
                if (text.Contains("handbook://", StringComparison.Ordinal) && BrakeLink.IsMatch(text))
                    entries[key] = BrakeLink.Replace(text, "$1");
            }
        }
    }

    /// <summary>Leaves the press brake out of the game: marks its block types and its recipe
    /// disabled before the game loads them.</summary>
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

    private static PressBrakeRig? LoadRig(ICoreAPI api)
    {
        var asset = api.Assets.TryGet(RigAsset);
        if (asset == null)
        {
            api.Logger.Error("[seraphhorizons] Press brake: {0} is missing; the press brake cannot be placed", RigAsset);
            return null;
        }
        try
        {
            return PressBrakeRig.Parse(asset.ToText());
        }
        catch (FormatException e)
        {
            api.Logger.Error("[seraphhorizons] Press brake: {0} is broken, so the press brake cannot be placed: {1}", RigAsset, e.Message);
            return null;
        }
    }

    // The settings object is the one in the mod's loaded config, so the fixes hold for every reader.
    private static PressBrakeConfig LoadConfig(ICoreAPI api)
    {
        var config = SeraphHorizonsSystem.ConfigFor(api).PressBrakeSettings ?? new PressBrakeConfig();
        foreach (var fix in config.Sanitise())
            api.Logger.Warning($"[seraphhorizons] Press brake: ModConfig/{SeraphHorizonsSystem.ConfigFile}, PressBrakeSettings: {fix}");
        return config;
    }
}
