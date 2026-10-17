using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using SeraphHorizons.Mod.Riddle.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace SeraphHorizons.Mod.Riddle;

/// <summary>
/// The riddle (#714): registers its classes and holds what its block shares: its settings (RiddleSettings
/// in ModConfig/seraphhorizons.json) and the rig (assets/seraphhorizons/config/riddle-rig.json). With the
/// <c>Riddle</c> switch off, or <c>OreProcessing</c> off (there is no crushed ore to riddle), the server
/// leaves its block and its recipe out of the game (<see cref="Disable"/>). A hand station: no power, no
/// oil, and no automated input or output.
/// </summary>
public class RiddleSystem : ModSystem
{
    public const string Domain = "seraphhorizons";
    public static readonly AssetLocation RigAsset = new(Domain, "config/riddle-rig.json");

    public static readonly AssetLocation[] TypeAssets =
    [
        new(Domain, "blocktypes/riddle/frame.json"),
    ];

    public static readonly AssetLocation[] RecipeAssets =
    [
        new(Domain, "recipes/grid/riddle.json"),
    ];

    // The files open with a comment, which a plain Parse keeps as a token or trips over.
    private static readonly JsonLoadSettings IgnoreComments = new() { CommentHandling = CommentHandling.Ignore };

    private ICoreAPI? _api;
    private RiddleConfig? _config;
    private RiddleRig? _rig;
    private bool _rigLoaded;

    public static RiddleSystem Of(ICoreAPI api) => api.ModLoader.GetModSystem<RiddleSystem>();

    /// <summary>This side's settings. A client uses the server's figures where they matter (the block
    /// entity syncs them).</summary>
    public RiddleConfig Config => _config ??= LoadConfig(_api!);

    /// <summary>The rig, or null when the file is missing or broken (logged once); the frame then cannot
    /// be placed.</summary>
    public RiddleRig? Rig
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

    /// <summary>Whether the riddle is in the game: its switch is on, and ore processing (its crushed ore).</summary>
    public static bool Applies(ICoreAPI api) =>
        SeraphHorizonsSystem.ConfigFor(api) is { Riddle: true, OreProcessing: true };

    // The classes are registered on both sides whatever the setting: the server decides whether the
    // block exists, and a client must know the classes then.
    public override void Start(ICoreAPI api)
    {
        _api = api;
        _config = LoadConfig(api);
        api.RegisterBlockClass("seraphhorizons.Riddle", typeof(BlockRiddle));
        api.RegisterBlockEntityClass("seraphhorizons.Riddle", typeof(BERiddle));
    }

    // The rig is loaded here so the collision box lookups, which can run off the main thread, find it
    // ready. Types and recipes are read from the assets later in this phase (the game's loaders run at
    // 0.2 and 1, this system at the default 0.1), on the server only.
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

    private static readonly Regex RiddleLink =
        new("<a href=\"(?:handbook://block-seraphhorizons:riddle|handbooksearch://riddle)[^\"]*\">(.*?)</a>", RegexOptions.Compiled);

    /// <summary>With the riddle off its block has no handbook page, so the mod's own text names it
    /// without a link. Safe to run twice, as singleplayer's two sides do on shared entries.</summary>
    public static void UnlinkText()
    {
        foreach (var translations in Lang.AvailableLanguages.Values)
        {
            var entries = translations.GetAllEntries();
            foreach (var key in entries.Keys.Where(k => k.StartsWith(Domain + ":", StringComparison.Ordinal)).ToList())
            {
                var text = entries[key];
                if (text.Contains("handbook://", StringComparison.Ordinal) && RiddleLink.IsMatch(text))
                    entries[key] = RiddleLink.Replace(text, "$1");
            }
        }
    }

    /// <summary>Leaves the riddle out of the game: marks its block type and its recipe disabled before the
    /// game loads them.</summary>
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

    private static RiddleRig? LoadRig(ICoreAPI api)
    {
        var asset = api.Assets.TryGet(RigAsset);
        if (asset == null)
        {
            api.Logger.Error("[seraphhorizons] Riddle: {0} is missing; the riddle cannot be placed", RigAsset);
            return null;
        }
        try
        {
            return RiddleRig.Parse(asset.ToText());
        }
        catch (FormatException e)
        {
            api.Logger.Error("[seraphhorizons] Riddle: {0} is broken, so the riddle cannot be placed: {1}", RigAsset, e.Message);
            return null;
        }
    }

    // The settings object is the one in the mod's loaded config, so the fixes hold for every reader.
    private static RiddleConfig LoadConfig(ICoreAPI api)
    {
        var config = SeraphHorizonsSystem.ConfigFor(api).RiddleSettings ?? new RiddleConfig();
        foreach (var fix in config.Sanitise())
            api.Logger.Warning($"[seraphhorizons] Riddle: ModConfig/{SeraphHorizonsSystem.ConfigFile}, RiddleSettings: {fix}");
        return config;
    }
}
