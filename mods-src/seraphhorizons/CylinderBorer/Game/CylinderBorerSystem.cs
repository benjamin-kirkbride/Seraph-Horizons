using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using SeraphHorizons.Mod.CylinderBorer.Core;
using SeraphHorizons.Mod.Machines;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace SeraphHorizons.Mod.CylinderBorer;

/// <summary>
/// The cylinder borer (#639): registers its block entity, names its rig to the shared machine base
/// (<see cref="MachinesSystem.RegisterRig"/>; its frame, cells and power cell are the shared blocks, its build
/// <c>config/builds/cylinderborer.json</c>, its oil MachineOil's) and holds its settings
/// (CylinderBorerSettings in ModConfig/seraphhorizons.json) and the rig
/// (assets/seraphhorizons/config/cylinderborer-rig.json). With the <c>CylinderBorer</c> switch off the
/// server leaves its blocks, its parts, the cylinder mold, the castings and the bored cylinders and
/// their recipes out of the game (<see cref="Disable"/>), and empties the patch that makes Pipes and
/// Power Expanded's Watt and Cornish engines take a bored cylinder (<see cref="DisablePatches"/>), so
/// they take their plates and pipes again.
/// </summary>
public class CylinderBorerSystem : ModSystem
{
    public const string Domain = "seraphhorizons";

    /// <summary>The machine's id: its build file's, its frame's first code part.</summary>
    public const string MachineId = "cylinderborer";
    public static readonly AssetLocation RigAsset = new(Domain, "config/cylinderborer-rig.json");

    public static readonly AssetLocation[] TypeAssets =
    [
        new(Domain, "blocktypes/cylinderborer/frame.json"),
        new(Domain, "itemtypes/cylinderborer/head.json"),
        new(Domain, "itemtypes/cylinderblank.json"),
        new(Domain, "itemtypes/cylinder.json"),
        new(Domain, "blocktypes/clay/cylindermold-raw.json"),
        new(Domain, "blocktypes/clay/cylindermold-fired.json"),
    ];

    public static readonly AssetLocation[] RecipeAssets =
    [
        new(Domain, "recipes/grid/cylinderborer.json"),
        new(Domain, "recipes/smithing/cylinderborer.json"),
        new(Domain, "recipes/clayforming/cylindermold.json"),
    ];

    /// <summary>The patch on ppex's engines (<c>recipes/grid/machines.json</c>): their cylinder is
    /// the borer's.</summary>
    public static readonly AssetLocation PatchAsset = new(Domain, "patches/cylinderborer-ppex.json");

    // The files open with a comment, which a plain Parse keeps as a token or trips over.
    private static readonly JsonLoadSettings IgnoreComments = new() { CommentHandling = CommentHandling.Ignore };

    private ICoreAPI? _api;
    private CylinderBorerConfig? _config;
    private CylinderBorerRig? _rig;
    private bool _rigLoaded;

    public static CylinderBorerSystem Of(ICoreAPI api) => api.ModLoader.GetModSystem<CylinderBorerSystem>();

    /// <summary>This side's settings. A client uses the server's figures where they matter (the
    /// block entity syncs them).</summary>
    public CylinderBorerConfig Config => _config ??= LoadConfig(_api!);

    /// <summary>The rig, or null when the file is missing or broken (logged once); the frame then
    /// cannot be placed.</summary>
    public CylinderBorerRig? Rig
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

    /// <summary>Whether the cylinder borer is in the game: its switch is on.</summary>
    public static bool Applies(ICoreAPI api) => SeraphHorizonsSystem.ConfigFor(api).CylinderBorer;

    // The classes are registered on both sides whatever the setting: the server decides whether the
    // blocks exist, and a client must know the classes then. The patch is emptied here, on the
    // server, before the game's patch loader applies it in AssetsLoaded (a client has no assets yet).
    public override void Start(ICoreAPI api)
    {
        _api = api;
        _config = LoadConfig(api);
        api.RegisterBlockEntityClass("seraphhorizons.CylinderBorer", typeof(BECylinderBorer));
        MachinesSystem.Of(api).RegisterRig(MachineId, () => Rig);
        if (api.Side == EnumAppSide.Server && !Applies(api))
            DisablePatches(api);
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

    /// <summary>Empties the engines' patch, so the patch loader applies none of it.</summary>
    public static void DisablePatches(ICoreAPI api)
    {
        if (api.Assets.TryGet(PatchAsset) is { } asset)
            asset.Data = "[]"u8.ToArray();
    }

    /// <summary>The machine oil page without the borer, when it is off: its place in the list of
    /// machines, and its drain with its exception to the dry rule. Each passage is the borer's alone,
    /// so these apply whatever the gear cutter's and the draw bench's switches, whose own edits touch
    /// other passages of the same text. (No edit may replace a passage with nothing: one whose
    /// replacement the text already holds is taken as done, and every text holds "".)</summary>
    public static readonly LangEdit[] LangEdits =
    [
        new("en", Domain + ":machineoil-text",
            "automated sawmill</a>, the <a href=\"handbook://block-seraphhorizons:cylinderborer-frame-north\">cylinder borer</a>, the "
            + "<a href=\"handbook://block-immersivewoodworking:chopper-frame-north\">",
            "automated sawmill</a>, the <a href=\"handbook://block-immersivewoodworking:chopper-frame-north\">"),
        new("en", Domain + ":machineoil-text",
            "the sawmill 2 points a log, the cylinder borer 10 points a cylinder bored (dry or oiled it takes the same power: its "
            + "oil spares its cutter head, which wears faster as the tank runs down and breaks on the first cylinder bored dry), "
            + "the chopper",
            "the sawmill 2 points a log, the chopper"),
    ];

    private static readonly Regex BorerLink =
        new("<a href=\"handbook://(?:block|item)-seraphhorizons:cylinder[^\"]*\">(.*?)</a>", RegexOptions.Compiled);

    /// <summary>With the borer off its blocks and items have no handbook page, so the mod's own text
    /// names them without a link (the machine oil page drops it altogether). Safe to run twice, as
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
                if (text.Contains("handbook://", StringComparison.Ordinal) && BorerLink.IsMatch(text))
                    entries[key] = BorerLink.Replace(text, "$1");
            }
        }
    }

    /// <summary>Leaves the cylinder borer out of the game: marks its block and item types and its
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

    private static CylinderBorerRig? LoadRig(ICoreAPI api)
    {
        var asset = api.Assets.TryGet(RigAsset);
        if (asset == null)
        {
            api.Logger.Error("[seraphhorizons] Cylinder borer: {0} is missing; the cylinder borer cannot be placed", RigAsset);
            return null;
        }
        try
        {
            return CylinderBorerRig.Parse(asset.ToText());
        }
        catch (FormatException e)
        {
            api.Logger.Error("[seraphhorizons] Cylinder borer: {0} is broken, so the cylinder borer cannot be placed: {1}", RigAsset, e.Message);
            return null;
        }
    }

    // The settings object is the one in the mod's loaded config, so the fixes hold for every reader.
    private static CylinderBorerConfig LoadConfig(ICoreAPI api)
    {
        var config = SeraphHorizonsSystem.ConfigFor(api).CylinderBorerSettings ?? new CylinderBorerConfig();
        foreach (var fix in config.Sanitise())
            api.Logger.Warning($"[seraphhorizons] Cylinder borer: ModConfig/{SeraphHorizonsSystem.ConfigFile}, CylinderBorerSettings: {fix}");
        return config;
    }
}
