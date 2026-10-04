using System.Text;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;

namespace SeraphHorizons.Mod.Woodworking;

/// <summary>
/// Edits of the two mods' JSON assets for the woodworking parts, the way
/// <see cref="AssembledMachines"/> edits Immersive Woodworking's frames: read the asset, check its
/// shape, change it, write it back before the game reads it. A part checks its assets in
/// <see cref="WoodworkingPart.Bind"/> with <see cref="Read"/> and edits them in
/// <see cref="WoodworkingPart.EditAssets"/> with <see cref="Edit"/>.
/// </summary>
public static class WoodworkingAssets
{
    /// <summary>The asset as JSON, or null if it is missing or not a JSON object. The game's
    /// lenient JSON (unquoted keys, comments) parses.</summary>
    public static JObject? Read(ICoreAPI api, AssetLocation location)
    {
        var asset = api.Assets.TryGet(location);
        if (asset == null)
            return null;
        try
        {
            return JToken.Parse(asset.ToText()) as JObject;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Applies <paramref name="edit"/> to the asset and writes it back. When the asset is
    /// missing, or <paramref name="edit"/> returns false (not the shape it expects) or throws,
    /// nothing is written and one warning says that <paramref name="what"/> is left as it is, and
    /// what that <paramref name="means"/> in the game.</summary>
    public static bool Edit(ICoreAPI api, AssetLocation location, System.Func<JObject, bool> edit, string what, string means)
    {
        var json = Read(api, location);
        bool done;
        try
        {
            done = json != null && edit(json);
        }
        catch (Exception)
        {
            done = false;
        }
        if (!done)
        {
            api.Logger.Warning($"[seraphhorizons] {location} is missing or not as expected; its mod, or another mod's patch "
                               + $"of it, changed, so {what} is left as it is: {means}");
            return false;
        }
        api.Assets.TryGet(location)!.Data = Encoding.UTF8.GetBytes(json!.ToString());
        return true;
    }

    /// <summary>A key of a blocktype or itemtype, which the game reads case-insensitively.</summary>
    public static JToken? Get(JObject json, string key) => json.GetValue(key, StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether the type's <c>code</c> is <paramref name="code"/>.</summary>
    public static bool HasCode(JObject json, string code) => Get(json, "code")?.Type == JTokenType.String
                                                             && (string)Get(json, "code")! == code;

    /// <summary>Takes a block or item type out of the creative inventory and the handbook: its
    /// creative tabs and stacks go, and <c>attributes.handbook</c> says <c>exclude</c> (its
    /// <c>include</c> goes). It stays registered, so worlds and code that have it keep working.
    /// Returns false, changing nothing, when <c>attributes</c> or <c>attributes.handbook</c> is
    /// not an object.</summary>
    public static bool Hide(JObject json)
    {
        var attributes = Get(json, "attributes");
        var handbook = (attributes as JObject)?.GetValue("handbook", StringComparison.OrdinalIgnoreCase);
        if (attributes != null && attributes is not JObject || handbook != null && handbook is not JObject)
            return false;

        foreach (var key in new[] { "creativeinventory", "creativeinventoryByType", "creativeinventoryStacks",
                     "creativeinventoryStacksByType" })
            if (Get(json, key) is { Parent: JProperty property })
                property.Remove();
        if (attributes == null)
            json["attributes"] = attributes = new JObject();
        if (handbook == null)
            attributes["handbook"] = handbook = new JObject();
        if (((JObject)handbook).GetValue("include", StringComparison.OrdinalIgnoreCase) is { Parent: JProperty include })
            include.Remove();
        handbook["exclude"] = true;
        return true;
    }
}
