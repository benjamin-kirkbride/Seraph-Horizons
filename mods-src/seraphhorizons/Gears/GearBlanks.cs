using System.Text;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;

namespace SeraphHorizons.Mod.Gears;

/// <summary>
/// Steel gear blanks (#479, README "Steel gear blanks"): <c>seraphhorizons:gearblank-steel</c> and
/// <c>seraphhorizons:largegearblank-steel</c>, cast in clay-formed gear blank molds or smithed from
/// one and two steel ingots. All of it is assets on the game's own classes (the molds are
/// <c>BlockToolMold</c>s), so the switch only decides whether those assets load: with it off the
/// server marks them disabled before the game reads them, and the blanks and molds do not exist.
/// </summary>
public static class GearBlanks
{
    public const string Domain = "seraphhorizons";
    public const string Blank = Domain + ":gearblank-steel";
    public const string LargeBlank = Domain + ":largegearblank-steel";

    /// <summary>The molds' tool types, the third part of <c>seraphhorizons:toolmold-{color}-{raw|fired}-{tooltype}</c>.</summary>
    public const string MoldType = "gearblank", LargeMoldType = "largegearblank";

    /// <summary>Units of molten metal a mold takes (an ingot is 100).</summary>
    public const int MoldUnits = 100, LargeMoldUnits = 200;

    public static readonly AssetLocation[] TypeAssets =
    [
        new(Domain, "itemtypes/gearblank.json"),
        new(Domain, "itemtypes/largegearblank.json"),
        new(Domain, "blocktypes/clay/gearblankmold-raw.json"),
        new(Domain, "blocktypes/clay/gearblankmold-fired.json"),
    ];

    public static readonly AssetLocation[] RecipeAssets =
    [
        new(Domain, "recipes/clayforming/gearblankmold.json"),
        new(Domain, "recipes/smithing/gearblank.json"),
    ];

    // The files open with a comment, which a plain Parse keeps as a token or trips over.
    private static readonly JsonLoadSettings IgnoreComments = new() { CommentHandling = CommentHandling.Ignore };

    /// <summary>Leaves the blanks, their molds and their recipes out of the game: marks them
    /// disabled before the game loads them (this runs in AssetsLoaded at 0.1, before the game's
    /// type loader at 0.2 and its recipe loader at 1). Server side: clients get the types from the
    /// server.</summary>
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
}
