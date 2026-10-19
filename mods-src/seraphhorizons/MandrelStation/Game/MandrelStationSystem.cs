using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.MandrelStation.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace SeraphHorizons.Mod.MandrelStation;

/// <summary>
/// The mandrel forging station: registers its block entity, names its rig to the shared machine base
/// (<see cref="MachinesSystem.RegisterRig"/>; its frame and cell are the shared blocks, its build
/// <c>config/builds/mandrelstation.json</c>) and holds its settings (MandrelStationSettings in
/// ModConfig/seraphhorizons.json) and the rig (assets/seraphhorizons/config/mandrelstation-rig.json). With
/// the <c>MandrelStation</c> switch off the server leaves its block and its recipe out of the game
/// (<see cref="Disable"/>). A hand station: no mechanical power and no oil.
/// </summary>
public class MandrelStationSystem : ModSystem
{
    public const string Domain = "seraphhorizons";

    /// <summary>The machine's id: its build file's, its frame's first code part.</summary>
    public const string MachineId = "mandrelstation";
    public static readonly AssetLocation RigAsset = new(Domain, "config/mandrelstation-rig.json");

    public static readonly AssetLocation[] TypeAssets =
    [
        new(Domain, "blocktypes/mandrelstation/frame.json"),
    ];

    public static readonly AssetLocation[] RecipeAssets =
    [
        new(Domain, "recipes/grid/mandrelstation.json"),
    ];

    /// <summary>The spring the station winds from a steel rod: the <c>MandrelSprings</c> switch's (and
    /// gone with the station too).</summary>
    public static readonly AssetLocation[] SpringTypeAssets =
    [
        new(Domain, "itemtypes/spring.json"),
    ];

    // The files open with a comment, which a plain Parse keeps as a token or trips over.
    private static readonly JsonLoadSettings IgnoreComments = new() { CommentHandling = CommentHandling.Ignore };

    private ICoreAPI? _api;
    private MandrelStationConfig? _config;
    private MandrelStationRig? _rig;
    private bool _rigLoaded;

    public static MandrelStationSystem Of(ICoreAPI api) => api.ModLoader.GetModSystem<MandrelStationSystem>();

    /// <summary>This side's settings. A client uses the server's figures where they matter (the
    /// block entity syncs them).</summary>
    public MandrelStationConfig Config => _config ??= LoadConfig(_api!);

    /// <summary>The rig, or null when the file is missing or broken (logged once); the frame then
    /// cannot be placed.</summary>
    public MandrelStationRig? Rig
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

    /// <summary>Whether the mandrel station is in the game: its switch is on.</summary>
    public static bool Applies(ICoreAPI api) => SeraphHorizonsSystem.ConfigFor(api).MandrelStation;

    /// <summary>Whether the spring job is in the game: the station's switch and <c>MandrelSprings</c> on.</summary>
    public static bool SpringsApply(ICoreAPI api) => Applies(api) && SeraphHorizonsSystem.ConfigFor(api).MandrelSprings;

    // The classes are registered on both sides whatever the setting: the server decides whether the
    // blocks exist, and a client must know the classes then.
    public override void Start(ICoreAPI api)
    {
        _api = api;
        _config = LoadConfig(api);
        api.RegisterBlockEntityClass("seraphhorizons.MandrelStation", typeof(BEMandrelStation));
        MachinesSystem.Of(api).RegisterRig(MachineId, () => Rig);
    }

    // The rig is loaded here so the collision box lookups, which can run off the main thread, find
    // it ready. Types and recipes are read from the assets later in this phase (the game's loaders
    // run at 0.2 and 1, this system at the default 0.1), on the server only.
    public override void AssetsLoaded(ICoreAPI api)
    {
        if (!SpringsApply(api))
        {
            if (api.Side == EnumAppSide.Server)
            {
                DisableTypes(api, SpringTypeAssets);
                DropSpringSection(api);
            }
            UnlinkSpringText();
        }
        if (!Applies(api))
        {
            if (api.Side == EnumAppSide.Server)
                Disable(api);
            // Both sides, each from its own setting, as the handbook is the client's.
            UnlinkText();
            return;
        }
        _ = Rig;
    }

    // The mod's own text links the spring's page.
    private static readonly Regex SpringLink =
        new("<a href=\"handbook://item-seraphhorizons:spring[^\"]*\">(.*?)</a>", RegexOptions.Compiled);

    /// <summary>With springs off the spring has no handbook page, so the mod's own text names it without
    /// a link. Safe to run twice.</summary>
    public static void UnlinkSpringText()
    {
        foreach (var translations in Lang.AvailableLanguages.Values)
        {
            var entries = translations.GetAllEntries();
            foreach (var key in entries.Keys.Where(k => k.StartsWith(Domain + ":", StringComparison.Ordinal)).ToList())
            {
                var text = entries[key];
                if (text.Contains("handbook://item-seraphhorizons:spring", StringComparison.Ordinal))
                    entries[key] = SpringLink.Replace(text, "$1");
            }
        }
    }

    /// <summary>The frame's handbook section on winding springs, which the frame drops with springs off.</summary>
    public const string SpringSectionTitle = Domain + ":mandrelstation-handbook-spring-title";

    /// <summary>Takes the spring section out of the frame's handbook (server side, before the game loads
    /// the type; the client gets the attributes from the server).</summary>
    public static void DropSpringSection(ICoreAPI api)
    {
        if (api.Assets.TryGet(TypeAssets[0]) is not { } asset)
            return;
        var json = JObject.Parse(asset.ToText(), IgnoreComments);
        if (json.SelectToken("attributes.handbook.extraSections") is not JArray sections)
            return;
        foreach (var section in sections.OfType<JObject>().Where(s => (string?)s["title"] == SpringSectionTitle).ToList())
            section.Remove();
        asset.Data = Encoding.UTF8.GetBytes(json.ToString());
    }

    /// <summary>Marks <paramref name="types"/> disabled before the game loads them.</summary>
    public static void DisableTypes(ICoreAPI api, IEnumerable<AssetLocation> types)
    {
        foreach (var location in types)
        {
            if (api.Assets.TryGet(location) is not { } asset)
                continue;
            var json = JObject.Parse(asset.ToText(), IgnoreComments);
            json["enabled"] = false;
            asset.Data = Encoding.UTF8.GetBytes(json.ToString());
        }
    }

    // The mod's own text links the station's page, or searches the handbook for it.
    private static readonly Regex StationLink =
        new("<a href=\"(?:handbook://block-seraphhorizons:mandrelstation|handbooksearch://mandrel station)[^\"]*\">(.*?)</a>", RegexOptions.Compiled);

    /// <summary>With the station off its blocks have no handbook page, so the mod's own text names it
    /// without a link. Safe to run twice, as singleplayer's two sides do on shared entries.</summary>
    public static void UnlinkText()
    {
        foreach (var translations in Lang.AvailableLanguages.Values)
        {
            var entries = translations.GetAllEntries();
            foreach (var key in entries.Keys.Where(k => k.StartsWith(Domain + ":", StringComparison.Ordinal)).ToList())
            {
                var text = entries[key];
                if (text.Contains("handbook", StringComparison.Ordinal) && StationLink.IsMatch(text))
                    entries[key] = StationLink.Replace(text, "$1");
            }
        }
    }

    /// <summary>Leaves the mandrel station out of the game: marks its block type and its recipe
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

    private static MandrelStationRig? LoadRig(ICoreAPI api)
    {
        var asset = api.Assets.TryGet(RigAsset);
        if (asset == null)
        {
            api.Logger.Error("[seraphhorizons] Mandrel station: {0} is missing; the mandrel station cannot be placed", RigAsset);
            return null;
        }
        try
        {
            return MandrelStationRig.Parse(asset.ToText());
        }
        catch (FormatException e)
        {
            api.Logger.Error("[seraphhorizons] Mandrel station: {0} is broken, so the mandrel station cannot be placed: {1}", RigAsset, e.Message);
            return null;
        }
    }

    // The settings object is the one in the mod's loaded config, so the fixes hold for every reader.
    private static MandrelStationConfig LoadConfig(ICoreAPI api)
    {
        var config = SeraphHorizonsSystem.ConfigFor(api).MandrelStationSettings ?? new MandrelStationConfig();
        foreach (var fix in config.Sanitise())
            api.Logger.Warning($"[seraphhorizons] Mandrel station: ModConfig/{SeraphHorizonsSystem.ConfigFile}, MandrelStationSettings: {fix}");
        return config;
    }
}
