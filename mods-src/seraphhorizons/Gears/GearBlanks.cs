using System.Text;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;

namespace SeraphHorizons.Mod.Gears;

/// <summary>
/// Stainless gear blanks (#479, README "Stainless gear blanks"):
/// <c>seraphhorizons:gearblank-stainlesssteel</c> and <c>seraphhorizons:largegearblank-stainlesssteel</c>
/// (the variant is the game's metal code, which the molds' drop fills in), cast in clay-formed gear
/// blank molds, four small ones to an ingot, or smithed two small ones from a stainless steel ingot
/// and a large one from two. All of it is assets on the game's own classes (the molds are
/// <c>BlockToolMold</c>s), so the switch only decides whether those assets load: with it off the
/// server marks them disabled before the game reads them, and the blanks and molds do not exist.
/// </summary>
public static class GearBlanks
{
    public const string Domain = "seraphhorizons";
    public const string Blank = Domain + ":gearblank-stainlesssteel";
    public const string LargeBlank = Domain + ":largegearblank-stainlesssteel";

    /// <summary>The molds' tool types, the third part of <c>seraphhorizons:toolmold-{color}-{raw|fired}-{tooltype}</c>.</summary>
    public const string MoldType = "gearblank", LargeMoldType = "largegearblank";

    /// <summary>Units of molten metal a mold takes (an ingot is 100).</summary>
    public const int MoldUnits = 25, LargeMoldUnits = 200;

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
