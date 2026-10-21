using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.SlittingMill.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace SeraphHorizons.Mod.SlittingMill;

/// <summary>
/// The slitting mill: registers its block entity, names its rig to the shared machine base
/// (<see cref="MachinesSystem.RegisterRig"/>; its frame, cells and power cell are the shared blocks, its build
/// <c>config/builds/slittingmill.json</c>, its oil MachineOil's) and holds its settings (SlittingMillSettings in
/// ModConfig/seraphhorizons.json) and the rig (assets/seraphhorizons/config/slittingmill-rig.json). With the
/// <c>SlittingMill</c> switch off the server leaves its block, its slitter cutters and their recipes out of the
/// game (<see cref="Disable"/>).
/// </summary>
public class SlittingMillSystem : ModSystem
{
    public const string Domain = "seraphhorizons";

    /// <summary>The machine's id: its build file's, its frame's first code part.</summary>
    public const string MachineId = "slittingmill";
    public static readonly AssetLocation RigAsset = new(Domain, "config/slittingmill-rig.json");

    public static readonly AssetLocation[] TypeAssets =
    [
        new(Domain, "blocktypes/slittingmill/frame.json"),
        new(Domain, "itemtypes/slittingmill/slittercutter.json"),
    ];

    public static readonly AssetLocation[] RecipeAssets =
    [
        new(Domain, "recipes/grid/slittingmill.json"),
        new(Domain, "recipes/smithing/slittingmill.json"),
    ];

    // The files open with a comment, which a plain Parse keeps as a token or trips over.
    private static readonly JsonLoadSettings IgnoreComments = new() { CommentHandling = CommentHandling.Ignore };

    private ICoreAPI? _api;
    private SlittingMillConfig? _config;
    private SlittingMillRig? _rig;
    private bool _rigLoaded;

    public static SlittingMillSystem Of(ICoreAPI api) => api.ModLoader.GetModSystem<SlittingMillSystem>();

    /// <summary>This side's settings. A client uses the server's figures where they matter (the
    /// block entity syncs them).</summary>
    public SlittingMillConfig Config => _config ??= LoadConfig(_api!);

    /// <summary>The mill's load with no plate on, the lightest metal's (the recipe export's power entry reads it).</summary>
    public float LightestResistance => Config.LightestResistance();

    /// <summary>The mill's load slitting the hardest metal (the recipe export's power entry reads it).</summary>
    public float HeaviestResistance => Config.HeaviestResistance();

    /// <summary>The rig, or null when the file is missing or broken (logged once); the frame then
    /// cannot be placed.</summary>
    public SlittingMillRig? Rig
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

    /// <summary>Whether the slitting mill is in the game: its switch is on.</summary>
    public static bool Applies(ICoreAPI api) => SeraphHorizonsSystem.ConfigFor(api).SlittingMill;

    // The classes are registered on both sides whatever the setting: the server decides whether the
    // blocks exist, and a client must know the classes then.
    public override void Start(ICoreAPI api)
    {
        _api = api;
        _config = LoadConfig(api);
        api.RegisterBlockEntityClass("seraphhorizons.SlittingMill", typeof(BESlittingMill));
        MachinesSystem.Of(api).RegisterRig(MachineId, () => Rig);
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

    /// <summary>The machine oil page without the slitting mill, when it is off: its place in the list of
    /// machines and its drain. Each passage is the mill's alone, so these apply whatever the other machines'
    /// switches, whose own edits touch other passages of the same text.</summary>
    public static readonly LangEdit[] LangEdits =
    [
        new("en", Domain + ":machineoil-text",
            "powered chopper</a>, the <a href=\"handbook://block-seraphhorizons:slittingmill-frame-north\">slitting mill</a>, the "
            + "<a href=\"handbook://block-seraphhorizons:buckingmill-frame-north\">",
            "powered chopper</a>, the <a href=\"handbook://block-seraphhorizons:buckingmill-frame-north\">"),
        new("en", Domain + ":machineoil-text",
            "1 point a log, the slitting mill 5 points a plate slit, and the bucking sawmill",
            "1 point a log, and the bucking sawmill"),
    ];

    private static readonly Regex MillLink =
        new("<a href=\"handbook://(?:block|item)-seraphhorizons:(?:slittingmill|slittercutter)[^\"]*\">(.*?)</a>", RegexOptions.Compiled);

    /// <summary>With the mill off its blocks and cutters have no handbook page, so the mod's own text names
    /// them without a link (the machine oil page drops it altogether). Safe to run twice, as singleplayer's
    /// two sides do on shared entries.</summary>
    public static void UnlinkText(ILogger logger)
    {
        LangText.Apply(LangEdits, "seraphhorizons", logger);
        foreach (var translations in Lang.AvailableLanguages.Values)
        {
            var entries = translations.GetAllEntries();
            foreach (var key in entries.Keys.Where(k => k.StartsWith(Domain + ":", StringComparison.Ordinal)).ToList())
            {
                var text = entries[key];
                if (text.Contains("handbook://", StringComparison.Ordinal) && MillLink.IsMatch(text))
                    entries[key] = MillLink.Replace(text, "$1");
            }
        }
    }

    /// <summary>Leaves the slitting mill out of the game: marks its block and item types and its recipes
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

    private static SlittingMillRig? LoadRig(ICoreAPI api)
    {
        var asset = api.Assets.TryGet(RigAsset);
        if (asset == null)
        {
            api.Logger.Error("[seraphhorizons] Slitting mill: {0} is missing; the slitting mill cannot be placed", RigAsset);
            return null;
        }
        try
        {
            return SlittingMillRig.Parse(asset.ToText());
        }
        catch (FormatException e)
        {
            api.Logger.Error("[seraphhorizons] Slitting mill: {0} is broken, so the slitting mill cannot be placed: {1}", RigAsset, e.Message);
            return null;
        }
    }

    // The settings object is the one in the mod's loaded config, so the fixes hold for every reader.
    private static SlittingMillConfig LoadConfig(ICoreAPI api)
    {
        var config = SeraphHorizonsSystem.ConfigFor(api).SlittingMillSettings ?? new SlittingMillConfig();
        foreach (var fix in config.Sanitise())
            api.Logger.Warning($"[seraphhorizons] Slitting mill: ModConfig/{SeraphHorizonsSystem.ConfigFile}, SlittingMillSettings: {fix}");
        return config;
    }
}
