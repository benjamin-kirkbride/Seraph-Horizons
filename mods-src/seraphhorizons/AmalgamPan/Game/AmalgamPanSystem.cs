using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using SeraphHorizons.Mod.AmalgamPan.Core;
using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Machines.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace SeraphHorizons.Mod.AmalgamPan;

/// <summary>
/// The amalgam pan (#718): registers its block entity, names its rig to the shared machine base
/// (<see cref="MachinesSystem.RegisterRig"/>; its frame is the shared block, its axle the shared
/// <see cref="BEBehaviorMachineMP"/>) and holds its settings
/// (AmalgamPanSettings in ModConfig/seraphhorizons.json), the rig
/// (assets/seraphhorizons/config/amalgampan-rig.json); its build stages are the shared machine code's
/// (config/builds/amalgampan.json, <see cref="MachinesSystem.Build"/>). With the <c>AmalgamPan</c> switch off, or ore processing's, whose
/// quartz and amalgam it works, the server leaves its block and its recipe out of the game
/// (<see cref="Disable"/>). Worked by hand or by an axle below; no oil.
/// </summary>
public class AmalgamPanSystem : ModSystem
{
    public const string Domain = "seraphhorizons";
    public static readonly AssetLocation RigAsset = new(Domain, "config/amalgampan-rig.json");
    /// <summary>The pan's id in the shared machine code: its build file's name.</summary>
    public const string MachineId = "amalgampan";
    public static readonly AssetLocation FrameCode = new(Domain, "amalgampan-frame-north");

    public static readonly AssetLocation[] TypeAssets =
    [
        new(Domain, "blocktypes/amalgampan/frame.json"),
    ];

    public static readonly AssetLocation[] RecipeAssets =
    [
        new(Domain, "recipes/grid/amalgampan.json"),
    ];

    private static readonly JsonLoadSettings IgnoreComments = new() { CommentHandling = CommentHandling.Ignore };

    private ICoreAPI? _api;
    private AmalgamPanConfig? _config;
    private AmalgamPanRig? _rig;
    private bool _loaded;

    public static AmalgamPanSystem Of(ICoreAPI api) => api.ModLoader.GetModSystem<AmalgamPanSystem>();

    public AmalgamPanConfig Config => _config ??= LoadConfig(_api!);

    /// <summary>The rig, or null when the file is missing or broken (logged once); the pan then cannot
    /// be placed.</summary>
    public AmalgamPanRig? Rig
    {
        get
        {
            Load();
            return _rig;
        }
    }

    /// <summary>The build stages after the frame, or null when the build file is missing or broken
    /// (logged by the shared machine code); the pan then cannot be built.</summary>
    public MachineBuild? Build => _api == null ? null : MachinesSystem.Of(_api).Build(MachineId);

    /// <summary>Whether the pan is in the game: its switch is on, and ore processing's.</summary>
    public static bool Applies(ICoreAPI api)
    {
        var config = SeraphHorizonsSystem.ConfigFor(api);
        return config.AmalgamPan && config.OreProcessing;
    }

    public override void Start(ICoreAPI api)
    {
        _api = api;
        _config = LoadConfig(api);
        api.RegisterBlockEntityClass("seraphhorizons.AmalgamPan", typeof(BEAmalgamPan));
        MachinesSystem.Of(api).RegisterRig(MachineId, () => Rig);
    }

    public override void AssetsLoaded(ICoreAPI api)
    {
        Load();
        if (Applies(api))
            return;
        if (api.Side == EnumAppSide.Server)
            Disable(api);
        UnlinkText();
    }

    /// <summary>The frame's attribute that says, for the recipe export, what a full charge of each kind
    /// of quartz gives: <c>{ chargeItems, water, mercury, amalgamMercury, turnsPerItem, handTurnsPerSecond,
    /// charges: [ { input, ore, grade, amalgam, amalgamItems } ] }</c>.</summary>
    public const string ExportKey = "seraphhorizonsAmalgamPan";

    public override void AssetsFinalize(ICoreAPI api)
    {
        if (api.Side != EnumAppSide.Server || !Applies(api) || Ore.Processing.OreProcessingSystem.Of(api).Recovery is not { } recovery)
            return;
        var config = Config;
        var charges = new JArray();
        foreach (var spec in recovery.Ores.Where(o => o.Free).OrderBy(o => o.Ore, StringComparer.Ordinal))
            foreach (var grade in Ore.Core.OreProducts.Grades)
            {
                string input = Ore.Core.OreProducts.CrushedCode(spec.Ore, grade);
                if (api.World.GetItem(new AssetLocation(input)) is not { IsMissing: false } item
                    || Ore.Processing.OreStreamAttributes.Read(new ItemStack(item)) is not { } stream)
                    continue;
                double units = recovery.Amalgamate(Ore.Core.HandStation.AmalgamPan, stream).Amalgam * config.ChargeItems;
                charges.Add(new JObject
                {
                    ["input"] = input,
                    ["ore"] = spec.Ore,
                    ["grade"] = grade,
                    ["amalgam"] = Ore.Core.OreProducts.AmalgamCode(spec.Ore),
                    ["amalgamItems"] = units / recovery.ConcentrateUnits,
                });
            }
        var mark = new JObject
        {
            ["chargeItems"] = config.ChargeItems,
            ["water"] = config.WaterLitresPerCharge,
            ["mercury"] = recovery.Retort.Mercury,
            ["amalgamMercury"] = recovery.Retort.AmalgamMercury,
            ["turnsPerItem"] = config.TurnsPerItem,
            ["handTurnsPerSecond"] = config.HandTurnsPerSecond,
            ["charges"] = charges,
        };
        foreach (var block in api.World.Blocks.Where(b => b?.Code is { Domain: Domain } c && c.Path.StartsWith("amalgampan-", StringComparison.Ordinal)))
        {
            var token = block.Attributes?.Token as JObject ?? new JObject();
            token[ExportKey] = mark.DeepClone();
            block.Attributes = new Vintagestory.API.Datastructures.JsonObject(token);
        }
    }

    private void Load()
    {
        if (_loaded || _api == null)
            return;
        _loaded = true;
        _rig = LoadRig(_api);
    }

    private static readonly Regex PanLink =
        new("<a href=\"handbook://block-seraphhorizons:amalgampan[^\"]*\">(.*?)</a>", RegexOptions.Compiled);

    /// <summary>With the pan off its block has no handbook page, so the mod's own text names it
    /// without a link. Safe to run twice.</summary>
    public static void UnlinkText()
    {
        foreach (var translations in Lang.AvailableLanguages.Values)
        {
            var entries = translations.GetAllEntries();
            foreach (var key in entries.Keys.Where(k => k.StartsWith(Domain + ":", StringComparison.Ordinal)).ToList())
            {
                var text = entries[key];
                if (text.Contains("handbook://", StringComparison.Ordinal) && PanLink.IsMatch(text))
                    entries[key] = PanLink.Replace(text, "$1");
            }
        }
    }

    /// <summary>Leaves the pan out of the game: marks its block type and its recipe disabled before the
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

    private static AmalgamPanRig? LoadRig(ICoreAPI api)
    {
        if (api.Assets.TryGet(RigAsset) is not { } asset)
        {
            api.Logger.Error("[seraphhorizons] Amalgam pan: {0} is missing; the pan cannot be placed", RigAsset);
            return null;
        }
        try
        {
            return AmalgamPanRig.Parse(asset.ToText());
        }
        catch (FormatException e)
        {
            api.Logger.Error("[seraphhorizons] Amalgam pan: {0} is broken, so the pan cannot be placed: {1}", RigAsset, e.Message);
            return null;
        }
    }

    private static AmalgamPanConfig LoadConfig(ICoreAPI api)
    {
        var config = SeraphHorizonsSystem.ConfigFor(api).AmalgamPanSettings ?? new AmalgamPanConfig();
        foreach (var fix in config.Sanitise())
            api.Logger.Warning($"[seraphhorizons] Amalgam pan: ModConfig/{SeraphHorizonsSystem.ConfigFile}, AmalgamPanSettings: {fix}");
        return config;
    }
}
