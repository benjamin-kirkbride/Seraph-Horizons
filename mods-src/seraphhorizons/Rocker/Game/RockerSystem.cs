using System.Reflection;
using System.Text;
using Newtonsoft.Json.Linq;
using SeraphHorizons.Mod.Ore.Core;
using SeraphHorizons.Mod.Ore.Processing;
using SeraphHorizons.Mod.Rocker.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.Rocker;

/// <summary>
/// The rocker (#716): registers its classes and holds what its blocks share: its settings
/// (RockerSettings in ModConfig/seraphhorizons.json), the rig (assets/seraphhorizons/config/rocker-rig.json)
/// and what an item of its feed gives (<see cref="YieldOf"/>: the recovery figures are ore processing's,
/// the placer table the gold pan's). With the <c>Rocker</c> switch off, or ore processing off (it makes
/// ore processing's concentrate), the server leaves its block and its recipe out of the game
/// (<see cref="Disable"/>). A hand station: no power, no oil, no pipe; water from the bucket in hand.
/// </summary>
public class RockerSystem : ModSystem
{
    public const string Domain = "seraphhorizons";
    public static readonly AssetLocation RigAsset = new(Domain, "config/rocker-rig.json");
    public static readonly AssetLocation[] TypeAssets = [new(Domain, "blocktypes/rocker.json")];
    public static readonly AssetLocation[] RecipeAssets = [new(Domain, "recipes/grid/rocker.json")];

    /// <summary>A block of rich gravel is this many pans' worth (Wilderlands Panning: 8 pans a block).</summary>
    public const double PansPerBlock = 8;

    /// <summary>The gold pan whose table placer gravel follows.</summary>
    public static readonly AssetLocation PanCode = new("game", "pan-wooden");

    private static readonly JsonLoadSettings IgnoreComments = new() { CommentHandling = CommentHandling.Ignore };
    private static readonly FieldInfo? PanDropsField =
        typeof(BlockPan).GetField("dropsBySourceMat", BindingFlags.NonPublic | BindingFlags.Instance);

    private ICoreAPI? _api;
    private RockerConfig? _config;
    private RockerRig? _rig;
    private bool _rigLoaded;
    private readonly Dictionary<string, IReadOnlyList<RockerYield>> _placer = new(StringComparer.Ordinal);

    public static RockerSystem Of(ICoreAPI api) => api.ModLoader.GetModSystem<RockerSystem>();

    public RockerConfig Config => _config ??= LoadConfig(_api!);

    /// <summary>The rig, or null when the file is missing or broken (logged once); the rocker then
    /// cannot be placed.</summary>
    public RockerRig? Rig
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

    /// <summary>Whether the rocker is in the game: its switch and ore processing's are on.</summary>
    public static bool Applies(ICoreAPI api)
    {
        var config = SeraphHorizonsSystem.ConfigFor(api);
        return config.Rocker && config.OreProcessing;
    }

    public override void Start(ICoreAPI api)
    {
        _api = api;
        _config = LoadConfig(api);
        api.RegisterBlockClass("seraphhorizons.Rocker", typeof(BlockRocker));
        api.RegisterBlockEntityClass("seraphhorizons.Rocker", typeof(BERocker));
    }

    public override void AssetsLoaded(ICoreAPI api)
    {
        _ = Rig;
        if (!Applies(api) && api.Side == EnumAppSide.Server)
            Disable(api);
    }

    /// <summary>Leaves the rocker out of the game: marks its block type and its recipe disabled.</summary>
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

    /// <summary>
    /// What one item like <paramref name="stack"/> gives when washed through (server side): crushed or
    /// ground ore its concentrate's units, ore processing's hand concentration at the rocker
    /// (<c>HandConcentrate(HandStation.Rocker, stream)</c>: the item's units, its locked shares by the
    /// state it carries (riddled, ground, amalgamated), 55 %, its density); placer gravel the gold pan's
    /// table at the rocker's rate (<see cref="RockerRecovery.PlacerYield"/>).
    /// </summary>
    public IReadOnlyList<RockerYield> YieldOf(ItemStack stack)
    {
        string code = stack.Collectible.Code.ToString();
        if (RockerFeed.Parse(code) is not { } feed || _api == null)
            return [];
        var recovery = OreProcessingSystem.RecoveryFor(_api);
        if (feed.Kind != RockerFeedKind.Placer)
        {
            if (OreStreamAttributes.Read(stack) is not { } stream)
                return [];
            double units = recovery.HandConcentrate(HandStation.Rocker, stream);
            return [new RockerYield(OreProducts.ConcentrateCode(stream.Ore), units, recovery.ConcentrateUnits)];
        }
        if (!_placer.TryGetValue(code, out var yields))
            _placer[code] = yields = RockerRecovery.PlacerYield(recovery.Figure(HandStation.Rocker), recovery.Figure(HandStation.Pan),
                PlacerDrops(_api.World, code), PansPerBlock);
        return yields;
    }

    /// <summary>The gold pan's table for a placer gravel block: each drop's code (with the gravel's rock
    /// put in), its chance a pan, and whether it is metal (a nugget). Empty when the pan or the gravel
    /// is missing or its table cannot be read.</summary>
    public static List<(string Code, double ChancePerPan, bool Metal)> PlacerDrops(IWorldAccessor world, string gravelCode)
    {
        var result = new List<(string, double, bool)>();
        if (world.GetBlock(PanCode) is not BlockPan pan || world.GetBlock(new AssetLocation(gravelCode)) is not { Id: > 0 } gravel
            || PanDropsField?.GetValue(pan) is not Dictionary<string, PanningDrop[]> table)
            return result;
        string material = gravel.Code.ToShortString();
        PanningDrop[]? drops = null;
        foreach (var (key, list) in table)
            if (WildcardUtil.Match(key, material))
                drops = list;
        if (drops == null || drops.Length == 0)
            return result;
        var chances = drops.Select(d => (double)Math.Clamp(d.Chance.avg + d.Chance.offset, 0, 1)).ToList();
        for (int i = 0; i < drops.Length; i++)
        {
            var code = drops[i].Code.Clone();
            if (code.Path.Contains("{rocktype}"))
                code.Path = code.Path.Replace("{rocktype}", gravel.Variant["rock"]);
            CollectibleObject? c = drops[i].Type == EnumItemClass.Block ? world.GetBlock(code) : world.GetItem(code);
            if (c == null || c.IsMissing)
                continue;
            bool metal = c.Code.Path.StartsWith("nugget-", StringComparison.Ordinal);
            result.Add((c.Code.ToString(), RockerRecovery.ChancePerPan(chances, i), metal));
        }
        return result;
    }

    private static RockerRig? LoadRig(ICoreAPI api)
    {
        var asset = api.Assets.TryGet(RigAsset);
        if (asset == null)
        {
            api.Logger.Error("[seraphhorizons] Rocker: {0} is missing; the rocker cannot be placed", RigAsset);
            return null;
        }
        try
        {
            return RockerRig.Parse(asset.ToText());
        }
        catch (FormatException e)
        {
            api.Logger.Error("[seraphhorizons] Rocker: {0} is broken, so the rocker cannot be placed: {1}", RigAsset, e.Message);
            return null;
        }
    }

    private static RockerConfig LoadConfig(ICoreAPI api)
    {
        var config = SeraphHorizonsSystem.ConfigFor(api).RockerSettings ?? new RockerConfig();
        foreach (var fix in config.Sanitise())
            api.Logger.Warning($"[seraphhorizons] Rocker: ModConfig/{SeraphHorizonsSystem.ConfigFile}, RockerSettings: {fix}");
        return config;
    }
}
