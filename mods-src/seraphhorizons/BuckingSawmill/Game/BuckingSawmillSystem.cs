using SeraphHorizons.Mod.BuckingSawmill.Core;
using System.Text;
using Newtonsoft.Json.Linq;
using SeraphHorizons.Mod.Machines;
using Vintagestory.API.Common;

namespace SeraphHorizons.Mod.BuckingSawmill;

/// <summary>
/// The bucking sawmill: registers its classes and holds what its blocks share: its settings
/// (BuckingSawmillSettings in ModConfig/seraphhorizons.json), the rig
/// (assets/seraphhorizons/config/buckingmill-rig.json: the footprint and anchor points), the
/// bridge to Logging Expanded and the blade kits' tool tiers. The mill is built from Immersive
/// Woodworking's parts and cuts Logging Expanded's trunks, so with the switch off, or either mod
/// missing, the server leaves its blocks and recipe out of the game (<see cref="Disable"/>).
/// </summary>
public class BuckingSawmillSystem : ModSystem
{
    public const string Domain = "seraphhorizons";
    public const string IwModId = "immersivewoodworking";
    public const string LeModId = "loggingmod";
    public static readonly AssetLocation RigAsset = new(Domain, "config/buckingmill-rig.json");
    public static readonly AssetLocation RecipeAsset = new(Domain, "recipes/grid/buckingmill.json");
    public static readonly AssetLocation[] BlockAssets =
    [
        new(Domain, "blocktypes/buckingmill/frame.json"),
        new(Domain, "blocktypes/buckingmill/ghost.json"),
        new(Domain, "blocktypes/buckingmill/ghostpower.json"),
    ];

    private ICoreAPI? _api;
    private MillConfig? _config;
    private Rig? _rig;
    private bool _rigLoaded;
    private LoggingBridge? _logging;
    private bool _loggingResolved;
    // Blade kit tool tiers by item code, looked up once each (server side).
    private readonly Dictionary<AssetLocation, int?> _bladeTiers = [];
    private Dictionary<string, int>? _metalTiers;

    public static BuckingSawmillSystem Of(ICoreAPI api) => api.ModLoader.GetModSystem<BuckingSawmillSystem>();

    /// <summary>This side's settings.</summary>
    public MillConfig Config => _config ??= LoadConfig(_api!);

    /// <summary>The rig, or null when the file is missing or broken (logged once); the frame then
    /// cannot be placed.</summary>
    public Rig? Rig
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

    /// <summary>Logging Expanded, or null when something the mill needs is missing (logged once).</summary>
    public LoggingBridge? Logging
    {
        get
        {
            if (!_loggingResolved && _api != null)
            {
                _loggingResolved = true;
                _logging = LoggingBridge.Resolve(_api, out var problems);
                if (_logging == null)
                    _api.Logger.Warning("[seraphhorizons] Bucking sawmill: Logging Expanded is not as expected, so the mill takes and cuts no trunks: {0}",
                        string.Join("; ", problems));
            }
            return _logging;
        }
    }

    /// <summary>How fast <paramref name="bladeKit"/> cuts, as a multiple of a copper kit's speed:
    /// <see cref="Cutting.BladeSpeed"/> of its tool tier (<see cref="BladeTier"/>) with this side's
    /// <c>BladeSpeedPerTier</c>.</summary>
    public float BladeSpeed(ItemStack bladeKit) => Cutting.BladeSpeed(BladeTier(bladeKit), Config.BladeSpeedPerTier);

    /// <summary>
    /// The tool tier of a blade kit, so that any metal the kit comes in, a modded one too, has one
    /// without a table here. Immersive Woodworking's <c>sawmillblade-{metal}</c> has no tier of its
    /// own (it is not a tool), and it is crafted from the game's saw blade of its metal, so in order:
    /// the kit's own <c>ToolTier</c> if it has one; the tier of the game's saw of that metal
    /// (<c>game:saw-{metal}</c>: copper, gold and silver 2, the bronzes 3, iron and meteoric iron 4,
    /// steel 5); the metal's tier in the game's metal properties plus one (they run one below the
    /// tools': copper 1, steel 4); else null, which cuts at copper's speed.
    /// </summary>
    public int? BladeTier(ItemStack bladeKit)
    {
        var code = bladeKit.Collectible?.Code;
        if (code == null || _api == null)
            return null;
        lock (_bladeTiers)
        {
            if (!_bladeTiers.TryGetValue(code, out var tier))
                _bladeTiers[code] = tier = LookUpTier(bladeKit.Collectible!);
            return tier;
        }
    }

    private int? LookUpTier(CollectibleObject kit)
    {
        if (kit.ToolTier > 0)
            return kit.ToolTier;
        if (Parts.KindOf(kit.Code.Path, out var metal) != PartKind.BladeKit || metal == null)
            return null;
        if (_api!.World.GetItem(new AssetLocation("game", "saw-" + metal)) is { ToolTier: > 0 } saw)
            return saw.ToolTier;
        return MetalPropertyTier(metal) is int tier ? tier + 1 : null;
    }

    /// <summary>A metal's tier in the game's metal properties
    /// (<c>game:worldproperties/block/metal.json</c>, mods' metals included when they patch it in),
    /// or null; read once.</summary>
    public int? MetalPropertyTier(string metal)
    {
        if (_api == null)
            return null;
        _metalTiers ??= LoadMetalTiers(_api);
        return _metalTiers.TryGetValue(metal, out int tier) ? tier : null;
    }

    private static Dictionary<string, int> LoadMetalTiers(ICoreAPI api)
    {
        try
        {
            var json = api.Assets.TryGet(new AssetLocation("game", "worldproperties/block/metal.json"))?.ToObject<JObject>();
            return (json?["variants"] as JArray ?? [])
                .OfType<JObject>()
                .Where(v => v["code"]?.Type == JTokenType.String && v["tier"]?.Type == JTokenType.Integer)
                .GroupBy(v => (string)v["code"]!)
                .ToDictionary(g => g.Key, g => (int)g.First()["tier"]!);
        }
        catch (Exception e)
        {
            api.Logger.Warning("[seraphhorizons] Bucking sawmill: could not read the game's metal tiers: {0}", e.Message);
            return [];
        }
    }

    /// <summary>Whether the mill is in the game: its switch is on and both mods are installed.</summary>
    public static bool Applies(ICoreAPI api) =>
        SeraphHorizonsSystem.ConfigFor(api).BuckingSawmill
        && api.ModLoader.IsModEnabled(IwModId) && api.ModLoader.IsModEnabled(LeModId);

    // The classes are registered on both sides whatever the setting: the server decides whether the
    // blocks exist, and a client must know the classes then.
    public override void Start(ICoreAPI api)
    {
        _api = api;
        _config = LoadConfig(api);
        api.RegisterBlockClass("seraphhorizons.BuckingMill", typeof(BlockBuckingMill));
        api.RegisterBlockClass("seraphhorizons.BuckingMillGhost", typeof(BlockMillGhost));
        api.RegisterBlockClass("seraphhorizons.BuckingMillGhostPower", typeof(BlockMillGhostPower));
        api.RegisterBlockEntityClass("seraphhorizons.BuckingMill", typeof(BEBuckingMill));
        api.RegisterBlockEntityClass("seraphhorizons.BuckingMillGhost", typeof(BEMillGhost));
        api.RegisterBlockEntityBehaviorClass("seraphhorizons.BuckingMillMP", typeof(BEBehaviorMillMP));
    }

    // The rig is loaded here so the collision box lookups, which can run off the main thread, find
    // it ready. Blocktypes and recipes are read from the assets later in this phase (the game's
    // loaders run at 0.2 and 1, this system at the default 0.1), on the server only.
    public override void AssetsLoaded(ICoreAPI api)
    {
        _ = Rig;
        if (api.Side == EnumAppSide.Server && !Applies(api))
            Disable(api);
    }

    /// <summary>Leaves the mill out of the game: marks its blocktypes and its recipe disabled
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

    private static Rig? LoadRig(ICoreAPI api)
    {
        var asset = api.Assets.TryGet(RigAsset);
        if (asset == null)
        {
            api.Logger.Error("[seraphhorizons] Bucking sawmill: {0} is missing; the mill cannot be placed", RigAsset);
            return null;
        }
        try
        {
            return Rig.Parse(asset.ToText());
        }
        catch (FormatException e)
        {
            api.Logger.Error("[seraphhorizons] Bucking sawmill: {0} is broken, so the mill cannot be placed: {1}", RigAsset, e.Message);
            return null;
        }
    }

    // The settings object is the one in the mod's loaded config, so the fixes hold for every reader.
    private static MillConfig LoadConfig(ICoreAPI api)
    {
        var config = SeraphHorizonsSystem.ConfigFor(api).BuckingSawmillSettings ?? new MillConfig();
        foreach (var fix in config.Sanitise())
            api.Logger.Warning($"[seraphhorizons] Bucking sawmill: ModConfig/{SeraphHorizonsSystem.ConfigFile}, BuckingSawmillSettings: {fix}");
        return config;
    }
}
