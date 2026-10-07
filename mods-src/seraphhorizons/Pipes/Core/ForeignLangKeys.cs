using Newtonsoft.Json.Linq;

namespace SeraphHorizons.Mod.Pipes.Core;

/// <summary>
/// Names for another mod's blocks, written in this mod's lang file under that mod's domain
/// (<c>"smex:block-toolmold-*-raw-pipe"</c>): the game reads such keys from any lang file, but
/// Expanded Lib's lang coverage check reads a block's own domain's lang assets, so a state patched
/// onto another mod's block is "missing" to it unless its name is in that mod's file too. This
/// copies the keys for <c>domain</c> into that file's text, without the prefix, leaving every key it
/// already has alone.
/// </summary>
public static class ForeignLangKeys
{
    /// <summary>The entries of <paramref name="ownLangJson"/> whose key starts with
    /// <c>domain:</c>, with the prefix removed.</summary>
    public static IReadOnlyDictionary<string, string> For(string ownLangJson, string domain)
    {
        var prefix = domain + ":";
        var found = new Dictionary<string, string>();
        foreach (var property in JObject.Parse(ownLangJson).Properties())
            if (property.Name.StartsWith(prefix, StringComparison.Ordinal) && property.Value.Type == JTokenType.String)
                found[property.Name[prefix.Length..]] = (string)property.Value!;
        return found;
    }

    /// <summary>The text of <paramref name="theirLangJson"/> with every entry of
    /// <paramref name="keys"/> it lacks appended, or null when it lacks none.</summary>
    public static string? Merge(string theirLangJson, IReadOnlyDictionary<string, string> keys)
    {
        var json = JObject.Parse(theirLangJson);
        var added = false;
        foreach (var (key, text) in keys)
        {
            if (json.ContainsKey(key))
                continue;
            json[key] = text;
            added = true;
        }
        return added ? json.ToString() : null;
    }
}
