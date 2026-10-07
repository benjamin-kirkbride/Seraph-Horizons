using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using SeraphHorizons.Mod.DrawBench.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace SeraphHorizons.Mod.DrawBench;

/// <summary>
/// The draw bench: registers its classes and holds what its blocks share: its settings
/// (DrawBenchSettings in ModConfig/seraphhorizons.json) and the rig
/// (assets/seraphhorizons/config/drawbench-rig.json). With the <c>DrawBench</c> switch off the
/// server leaves its blocks, its dies and their recipes out of the game (<see cref="Disable"/>).
/// </summary>
public class DrawBenchSystem : ModSystem
{
    public const string Domain = "seraphhorizons";
    public static readonly AssetLocation RigAsset = new(Domain, "config/drawbench-rig.json");

    public static readonly AssetLocation[] TypeAssets =
    [
        new(Domain, "blocktypes/drawbench/frame.json"),
        new(Domain, "blocktypes/drawbench/ghost.json"),
        new(Domain, "blocktypes/drawbench/ghostpower.json"),
        new(Domain, "itemtypes/drawbench/drawdie.json"),
    ];

    public static readonly AssetLocation[] RecipeAssets =
    [
        new(Domain, "recipes/grid/drawbench.json"),
        new(Domain, "recipes/smithing/drawbench.json"),
    ];

    // The files open with a comment, which a plain Parse keeps as a token or trips over.
    private static readonly JsonLoadSettings IgnoreComments = new() { CommentHandling = CommentHandling.Ignore };

    private ICoreAPI? _api;
    private DrawBenchConfig? _config;
    private DrawBenchRig? _rig;
    private bool _rigLoaded;

    public static DrawBenchSystem Of(ICoreAPI api) => api.ModLoader.GetModSystem<DrawBenchSystem>();

    /// <summary>This side's settings. A client uses the server's figures where they matter (the
    /// block entity syncs them).</summary>
    public DrawBenchConfig Config => _config ??= LoadConfig(_api!);

    /// <summary>The rig, or null when the file is missing or broken (logged once); the frame then
    /// cannot be placed.</summary>
    public DrawBenchRig? Rig
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

    /// <summary>Whether the draw bench is in the game: its switch is on.</summary>
    public static bool Applies(ICoreAPI api) => SeraphHorizonsSystem.ConfigFor(api).DrawBench;

    // The classes are registered on both sides whatever the setting: the server decides whether the
    // blocks exist, and a client must know the classes then.
    public override void Start(ICoreAPI api)
    {
        _api = api;
        _config = LoadConfig(api);
        api.RegisterBlockClass("seraphhorizons.DrawBench", typeof(BlockDrawBench));
        api.RegisterBlockClass("seraphhorizons.DrawBenchGhost", typeof(BlockDrawBenchGhost));
        api.RegisterBlockClass("seraphhorizons.DrawBenchGhostPower", typeof(BlockDrawBenchGhostPower));
        api.RegisterBlockEntityClass("seraphhorizons.DrawBench", typeof(BEDrawBench));
        api.RegisterBlockEntityClass("seraphhorizons.DrawBenchGhost", typeof(BEDrawBenchGhost));
        api.RegisterBlockEntityBehaviorClass("seraphhorizons.DrawBenchMP", typeof(BEBehaviorDrawBenchMP));
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

    /// <summary>A die's durability is the setting's (the item type file says 100, the default):
    /// set on the server before the types go to clients.</summary>
    public override void AssetsFinalize(ICoreAPI api)
    {
        if (api.Side != EnumAppSide.Server || !Applies(api))
            return;
        foreach (var metal in Drawing.DieMetalNames)
            if (api.World.GetItem(new AssetLocation(DrawBenchParts.DieCodeFor(metal)!)) is { } die)
                die.Durability = Config.DieDurability;
    }

    /// <summary>The machine oil page without the draw bench, when it is off: its place in the list
    /// of machines and its drain. Each passage is the bench's alone, so these apply whatever the
    /// gear cutter's switch, whose own edits (<c>GearCutterSystem.LangEdits</c>) touch other
    /// passages of the same text.</summary>
    public static readonly LangEdit[] LangEdits =
    [
        new("en", Domain + ":machineoil-text",
            "pulverizer</a>, the <a href=\"handbook://block-seraphhorizons:drawbench-frame-north\">draw bench</a>, the "
            + "<a href=\"handbook://block-immersivewoodworking:sawmill-frame-north\">",
            "pulverizer</a>, the <a href=\"handbook://block-immersivewoodworking:sawmill-frame-north\">"),
        new("en", Domain + ":machineoil-text",
            "a pulverizer half a point an item, the draw bench 2 points a pipe section drawn (8 a hollow section), the sawmill",
            "a pulverizer half a point an item, the sawmill"),
    ];

    private static readonly Regex BenchLink =
        new("<a href=\"handbook://(?:block|item)-seraphhorizons:(?:drawbench|drawdie)[^\"]*\">(.*?)</a>", RegexOptions.Compiled);

    /// <summary>With the bench off its blocks and dies have no handbook page, so the mod's own text
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
                if (text.Contains("handbook://", StringComparison.Ordinal) && BenchLink.IsMatch(text))
                    entries[key] = BenchLink.Replace(text, "$1");
            }
        }
    }

    /// <summary>Leaves the draw bench out of the game: marks its block and item types and its
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

    private static DrawBenchRig? LoadRig(ICoreAPI api)
    {
        var asset = api.Assets.TryGet(RigAsset);
        if (asset == null)
        {
            api.Logger.Error("[seraphhorizons] Draw bench: {0} is missing; the draw bench cannot be placed", RigAsset);
            return null;
        }
        try
        {
            return DrawBenchRig.Parse(asset.ToText());
        }
        catch (FormatException e)
        {
            api.Logger.Error("[seraphhorizons] Draw bench: {0} is broken, so the draw bench cannot be placed: {1}", RigAsset, e.Message);
            return null;
        }
    }

    // The settings object is the one in the mod's loaded config, so the fixes hold for every reader.
    private static DrawBenchConfig LoadConfig(ICoreAPI api)
    {
        var config = SeraphHorizonsSystem.ConfigFor(api).DrawBenchSettings ?? new DrawBenchConfig();
        foreach (var fix in config.Sanitise())
            api.Logger.Warning($"[seraphhorizons] Draw bench: ModConfig/{SeraphHorizonsSystem.ConfigFile}, DrawBenchSettings: {fix}");
        return config;
    }
}
