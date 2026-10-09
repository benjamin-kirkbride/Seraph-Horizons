using System.Text;
using Newtonsoft.Json.Linq;
using SeraphHorizons.Mod.BuckingSawmill;
using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Rosser.Core;
using SeraphHorizons.Mod.Woodworking;
using Vintagestory.API.Common;

namespace SeraphHorizons.Mod.Rosser;

/// <summary>
/// The rosser, a mechanically powered ring debarker for Logging Expanded's trunks: registers its
/// classes and holds what its blocks share: its settings (RosserSettings in
/// ModConfig/seraphhorizons.json), the rig (assets/seraphhorizons/config/rosser-rig.json: the
/// footprint, anchors and moving parts), the pace, the bridge to Logging Expanded, Immersive
/// Woodworking's bark and the scraper heads' tiers and durabilities. It is built from Immersive
/// Woodworking's and the game's parts, takes Logging Expanded's trunks and gives the debarked
/// trunk the <c>Rosser</c> switch adds (<see cref="DebarkedTrunks"/>), so with the switch off,
/// either mod missing or the debarked trunk not there, the server leaves its blocks and recipe out
/// of the game (<see cref="Disable"/>). Pipes and Power Expanded is optional: it waters the drip
/// (<see cref="PpexWater"/>), and with UnifiedPipes' copper and lead pipes the drip's pipes are a
/// stage of the build (<see cref="PipesNeeded"/>).
/// </summary>
public class RosserSystem : ModSystem
{
    public const string Domain = "seraphhorizons";
    public const string IwModId = "immersivewoodworking";
    public const string LeModId = "loggingmod";
    public static readonly AssetLocation RigAsset = new(Domain, "config/rosser-rig.json");
    public static readonly AssetLocation RecipeAsset = new(Domain, "recipes/grid/rosser.json");
    public static readonly AssetLocation[] BlockAssets =
    [
        new(Domain, "blocktypes/rosser/frame.json"),
        new(Domain, "blocktypes/rosser/ghost.json"),
        new(Domain, "blocktypes/rosser/ghostpower.json"),
        new(Domain, "blocktypes/rosser/ghostwater.json"),
    ];

    private ICoreAPI? _api;
    private RosserConfig? _config;
    private RosserRig? _rig;
    private RosserPace? _pace;
    private bool _rigLoaded;
    private LoggingBridge? _logging;
    private bool _loggingResolved;
    private bool? _barkBound;
    private readonly Dictionary<string, int?> _headTiers = [];
    private readonly Dictionary<string, int> _headCapacities = [];

    public static RosserSystem Of(ICoreAPI api) => api.ModLoader.GetModSystem<RosserSystem>();

    /// <summary>This side's settings. A client uses the server's figures where they matter (the
    /// block entity syncs them).</summary>
    public RosserConfig Config => _config ??= LoadConfig(_api!);

    /// <summary>The rig, or null when the file is missing or broken (logged once); the frame then
    /// cannot be placed.</summary>
    public RosserRig? Rig
    {
        get
        {
            if (!_rigLoaded && _api != null)
            {
                _rigLoaded = true;
                _rig = LoadRig(_api);
                _pace = _rig == null ? null : new RosserPace(_rig, Config);
            }
            return _rig;
        }
    }

    /// <summary>The feed's pace for the rig and this side's settings; null without a rig.</summary>
    public RosserPace? Pace => Rig == null ? null : _pace;

    /// <summary>Logging Expanded, or null when something the rosser needs is missing (logged once).</summary>
    public LoggingBridge? Logging
    {
        get
        {
            if (!_loggingResolved && _api != null)
            {
                _loggingResolved = true;
                _logging = LoggingBridge.Resolve(_api, out var problems);
                if (_logging == null)
                    _api.Logger.Warning("[seraphhorizons] Rosser: Logging Expanded is not as expected, so the rosser takes no trunks: {0}",
                        string.Join("; ", problems));
            }
            return _logging;
        }
    }

    /// <summary>Whether Immersive Woodworking's bark roll is bound (server side; bound on first
    /// use, one warning when it cannot be). Without it trunks are still debarked, with no bark.</summary>
    public bool BarkBound
    {
        get
        {
            if (_barkBound is { } bound || _api == null)
                return _barkBound ?? false;
            var mods = WoodworkingMods.Bind(_api, out string? missing);
            string? problem = mods == null ? missing : BarkDrops.Bind(mods);
            if (problem != null)
                _api.Logger.Warning("[seraphhorizons] Rosser: Immersive Woodworking's bark is not as expected, so the rosser drops no bark: {0}", problem);
            _barkBound = problem == null;
            return _barkBound.Value;
        }
    }

    /// <summary>The metals the rosser's hoops, rods and plates may be of: iron work while
    /// <c>IronWoodworkingMachines</c> is on (as Immersive Woodworking's machine parts then are),
    /// else any (null).</summary>
    public IReadOnlySet<string>? PartMetals =>
        _api != null && SeraphHorizonsSystem.ConfigFor(_api).IronWoodworkingMachines && WoodworkingMachineCosts.Applies(_api)
            ? RosserParts.IronMetals
            : null;

    /// <summary>Whether the rosser needs its drip pipes (<see cref="RosserParts.PipesNeeded"/>): Pipes
    /// and Power Expanded's straight pipe exists in copper and lead, the metals UnifiedPipes adds.
    /// Without ppex, or with UnifiedPipes off, it does not, and the rosser is built without pipes.
    /// Each side asks its own world, which has the same blocks.</summary>
    public bool PipesNeeded =>
        _api?.World is { } world
        && RosserParts.PipeMetals.All(m => world.GetBlock(new AssetLocation(RosserParts.PipeCode(m))) is { Id: > 0, IsMissing: false });

    /// <summary>
    /// The tool tier of scraper heads of <paramref name="metal"/>, as the mill finds its blade
    /// kit's (<see cref="BuckingSawmillSystem.BladeTier"/>): Immersive Woodworking's bark spud of
    /// that metal if it has a tier (1.3.11's has none); the game's saw of that metal; the metal's
    /// tier in the game's metal properties plus one; else the table in <see cref="HeadMetals"/>;
    /// else null, which feeds at copper's speed.
    /// </summary>
    public int? HeadTier(string? metal)
    {
        if (metal == null || _api == null)
            return null;
        lock (_headTiers)
        {
            if (!_headTiers.TryGetValue(metal, out var tier))
                _headTiers[metal] = tier = LookUpTier(metal);
            return tier;
        }
    }

    private int? LookUpTier(string metal)
    {
        var world = _api!.World;
        if (world.GetItem(new AssetLocation(IwModId, "barkspud-" + metal)) is { ToolTier: > 0 } spud)
            return spud.ToolTier;
        if (world.GetItem(new AssetLocation("game", "saw-" + metal)) is { ToolTier: > 0 } saw)
            return saw.ToolTier;
        if (BuckingSawmillSystem.Of(_api).MetalPropertyTier(metal) is int tier)
            return tier + 1;
        return HeadMetals.SawTier(metal);
    }

    /// <summary>The wear points four scraper heads of <paramref name="metal"/> last:
    /// <c>HeadWearMultiple</c> × the durability of Immersive Woodworking's bark spud of that metal
    /// (<see cref="HeadMetals"/>' table when the item is missing, else a copper spud's).</summary>
    public int HeadCapacity(string metal)
    {
        lock (_headCapacities)
        {
            if (!_headCapacities.TryGetValue(metal, out int capacity))
            {
                int durability = _api?.World.GetItem(new AssetLocation(IwModId, "barkspud-" + metal)) is { } spud
                                 && spud.GetMaxDurability(new ItemStack(spud)) is > 1 and var d
                    ? d
                    : HeadMetals.SpudDurability(metal) ?? HeadMetals.SpudDurability("copper")!.Value;
                _headCapacities[metal] = capacity = RosserParts.HeadCapacity(durability, Config.HeadWearMultiple);
            }
            return capacity;
        }
    }

    /// <summary>Whether the rosser is in the game: its switch is on, both mods are installed and the
    /// debarked trunk it makes exists.</summary>
    public static bool Applies(ICoreAPI api) =>
        SeraphHorizonsSystem.ConfigFor(api).Rosser
        && api.ModLoader.IsModEnabled(IwModId) && api.ModLoader.IsModEnabled(LeModId)
        && api.ModLoader.GetModSystem<SeraphHorizonsSystem>() is { DebarkedTrunksOn: true };

    // The classes are registered on both sides whatever the setting: the server decides whether the
    // blocks exist, and a client must know the classes then.
    public override void Start(ICoreAPI api)
    {
        _api = api;
        _config = LoadConfig(api);
        api.RegisterBlockClass("seraphhorizons.Rosser", typeof(BlockRosser));
        api.RegisterBlockClass("seraphhorizons.RosserGhost", typeof(BlockRosserGhost));
        api.RegisterBlockClass("seraphhorizons.RosserGhostPower", typeof(BlockRosserGhostPower));
        api.RegisterBlockClass("seraphhorizons.RosserGhostWater", typeof(BlockRosserGhostWater));
        api.RegisterBlockEntityClass("seraphhorizons.Rosser", typeof(BERosser));
        api.RegisterBlockEntityClass("seraphhorizons.RosserGhost", typeof(BERosserGhost));
        api.RegisterBlockEntityBehaviorClass("seraphhorizons.RosserMP", typeof(BEBehaviorRosserMP));
    }

    // The rig is loaded here so the collision box lookups, which can run off the main thread, find
    // it ready. Blocktypes and recipes are read from the assets later in this phase (the game's
    // loaders run at 0.2 and 1, this system at the default 0.1), on the server only. The pack's own
    // system has decided in its Start whether the debarked trunk exists.
    public override void AssetsLoaded(ICoreAPI api)
    {
        _ = Rig;
        if (api.Side == EnumAppSide.Server && !Applies(api))
            Disable(api);
    }

    /// <summary>Leaves the rosser out of the game: marks its blocktypes and its recipe disabled
    /// before the game loads them.</summary>
    public static void Disable(ICoreAPI api)
    {
        foreach (var location in BlockAssets)
        {
            if (api.Assets.TryGet(location) is not { } asset)
                continue;
            var json = JObject.Parse(asset.ToText());
            json["enabled"] = false;
            asset.Data = Encoding.UTF8.GetBytes(json.ToString());
        }
        if (api.Assets.TryGet(RecipeAsset) is { } recipes)
        {
            var json = JArray.Parse(recipes.ToText());
            foreach (var recipe in json.OfType<JObject>())
                recipe["enabled"] = false;
            recipes.Data = Encoding.UTF8.GetBytes(json.ToString());
        }
    }

    private static RosserRig? LoadRig(ICoreAPI api)
    {
        var asset = api.Assets.TryGet(RigAsset);
        if (asset == null)
        {
            api.Logger.Error("[seraphhorizons] Rosser: {0} is missing; the rosser cannot be placed", RigAsset);
            return null;
        }
        try
        {
            return RosserRig.Parse(asset.ToText());
        }
        catch (FormatException e)
        {
            api.Logger.Error("[seraphhorizons] Rosser: {0} is broken, so the rosser cannot be placed: {1}", RigAsset, e.Message);
            return null;
        }
    }

    // The settings object is the one in the mod's loaded config, so the fixes hold for every reader.
    private static RosserConfig LoadConfig(ICoreAPI api)
    {
        var config = SeraphHorizonsSystem.ConfigFor(api).RosserSettings ?? new RosserConfig();
        foreach (var fix in config.Sanitise())
            api.Logger.Warning($"[seraphhorizons] Rosser: ModConfig/{SeraphHorizonsSystem.ConfigFile}, RosserSettings: {fix}");
        return config;
    }
}
