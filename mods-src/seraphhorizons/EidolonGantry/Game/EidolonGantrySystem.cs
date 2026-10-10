using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using SeraphHorizons.Mod.EidolonGantry.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace SeraphHorizons.Mod.EidolonGantry;

/// <summary>
/// The eidolon gantry (#671): registers its classes and holds what its blocks share, the rig
/// (assets/seraphhorizons/config/eidolongantry-rig.json). Part of the eidolon (#668), behind the
/// <c>Eidolon</c> switch: with it off the server leaves its blocks and its recipe out of the game
/// (<see cref="Disable"/>). A hand-built frame: no power, no oil, no settings of its own.
/// </summary>
public class EidolonGantrySystem : ModSystem
{
    public const string Domain = "seraphhorizons";
    public static readonly AssetLocation RigAsset = new(Domain, "config/eidolongantry-rig.json");

    public static readonly AssetLocation[] TypeAssets =
    [
        new(Domain, "blocktypes/eidolongantry/frame.json"),
        new(Domain, "blocktypes/eidolongantry/ghost.json"),
    ];

    public static readonly AssetLocation[] RecipeAssets =
    [
        new(Domain, "recipes/grid/eidolongantry.json"),
    ];

    // The files open with a comment, which a plain Parse keeps as a token or trips over.
    private static readonly JsonLoadSettings IgnoreComments = new() { CommentHandling = CommentHandling.Ignore };

    private ICoreAPI? _api;
    private GantryRig? _rig;
    private bool _rigLoaded;

    public static EidolonGantrySystem Of(ICoreAPI api) => api.ModLoader.GetModSystem<EidolonGantrySystem>();

    /// <summary>The rig, or null when the file is missing or broken (logged once); the gantry then
    /// cannot be placed.</summary>
    public GantryRig? Rig
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

    /// <summary>Whether the gantry is in the game: the <c>Eidolon</c> switch is on.</summary>
    public static bool Applies(ICoreAPI api) => SeraphHorizonsSystem.ConfigFor(api).Eidolon;

    // The classes are registered on both sides whatever the setting: the server decides whether the
    // blocks exist, and a client must know the classes then.
    public override void Start(ICoreAPI api)
    {
        _api = api;
        api.RegisterBlockClass("seraphhorizons.EidolonGantry", typeof(BlockEidolonGantry));
        api.RegisterBlockClass("seraphhorizons.EidolonGantryGhost", typeof(BlockEidolonGantryGhost));
        api.RegisterBlockEntityClass("seraphhorizons.EidolonGantry", typeof(BEEidolonGantry));
        api.RegisterBlockEntityClass("seraphhorizons.EidolonGantryGhost", typeof(BEEidolonGantryGhost));
        api.RegisterBlockEntityBehaviorClass(BEBehaviorEidolonBody.Code, typeof(BEBehaviorEidolonBody));
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

    private static readonly Regex GantryLink =
        new("<a href=\"handbook://block-seraphhorizons:eidolongantry[^\"]*\">(.*?)</a>", RegexOptions.Compiled);

    /// <summary>With the gantry off its blocks have no handbook page, so the mod's own text names
    /// them without a link. Safe to run twice, as singleplayer's two sides do on shared entries.</summary>
    public static void UnlinkText()
    {
        foreach (var translations in Lang.AvailableLanguages.Values)
        {
            var entries = translations.GetAllEntries();
            foreach (var key in entries.Keys.Where(k => k.StartsWith(Domain + ":", StringComparison.Ordinal)).ToList())
            {
                var text = entries[key];
                if (text.Contains("handbook://", StringComparison.Ordinal) && GantryLink.IsMatch(text))
                    entries[key] = GantryLink.Replace(text, "$1");
            }
        }
    }

    /// <summary>Leaves the gantry out of the game: marks its block types and its recipe disabled
    /// before the game loads them.</summary>
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

    private static GantryRig? LoadRig(ICoreAPI api)
    {
        var asset = api.Assets.TryGet(RigAsset);
        if (asset == null)
        {
            api.Logger.Error("[seraphhorizons] Eidolon gantry: {0} is missing; the gantry cannot be placed", RigAsset);
            return null;
        }
        try
        {
            return GantryRig.Parse(asset.ToText());
        }
        catch (FormatException e)
        {
            api.Logger.Error("[seraphhorizons] Eidolon gantry: {0} is broken, so the gantry cannot be placed: {1}", RigAsset, e.Message);
            return null;
        }
    }
}
