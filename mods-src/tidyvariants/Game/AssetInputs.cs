using Newtonsoft.Json.Linq;
using SeraphHorizons.TidyVariants.Core;
using Vintagestory.API.Common;

namespace SeraphHorizons.TidyVariants;

/// <summary>The engine's asset inputs: the worldproperties lists and the pack's override file.</summary>
public static class AssetInputs
{
    /// <summary>The override file, <c>assets/tidyvariants/config/overrides.json</c>.</summary>
    public static readonly AssetLocation OverridesLocation = new("tidyvariants", "config/overrides.json");

    const string PropertiesPrefix = "worldproperties/";

    /// <summary>
    /// Every <c>worldproperties/**.json</c> in every domain, keyed <c>domain:path</c> (under
    /// <c>worldproperties/</c>, without <c>.json</c>), each its variant codes in file order. A file that
    /// doesn't parse or has no <c>variants[].code</c> is skipped with a warning.
    /// </summary>
    public static Dictionary<string, IReadOnlyList<string>> LoadWorldProperties(ICoreAPI api)
    {
        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var asset in api.Assets.GetMany(PropertiesPrefix))
        {
            var loc = asset.Location;
            if (loc?.Path is null || !loc.Path.EndsWith(".json", StringComparison.Ordinal)) continue;
            string key = loc.Domain + ":" + loc.Path[PropertiesPrefix.Length..^".json".Length];
            try
            {
                var root = JToken.Parse(asset.ToText());
                if (root["variants"] is not JArray variants) continue;
                var codes = new List<string>(variants.Count);
                foreach (var v in variants)
                    if ((v as JObject)?["code"]?.Value<string>() is { Length: > 0 } code) codes.Add(code);
                result[key] = codes;
            }
            catch (Exception ex)
            {
                api.Logger.Warning("[tidyvariants] worldproperties {0} could not be read, skipped: {1}", loc, ex.Message);
            }
        }
        return result;
    }

    /// <summary>
    /// The parsed override file, or <see cref="OverrideFile.Empty"/> when it is absent or invalid. Every
    /// problem is logged as an error naming the file; none of them stops the game.
    /// </summary>
    public static OverrideFile LoadOverrides(ICoreAPI api, out bool present, out IReadOnlyList<string> errors)
    {
        present = false;
        errors = [];
        IAsset? asset;
        try { asset = api.Assets.TryGet(OverridesLocation); }
        catch (Exception ex)
        {
            errors = [ex.Message];
            api.Logger.Error("[tidyvariants] {0} could not be loaded, using no overrides: {1}", OverridesLocation, ex.Message);
            return OverrideFile.Empty;
        }
        if (asset is null)
        {
            api.Logger.Notification("[tidyvariants] no {0}, using no overrides", OverridesLocation);
            return OverrideFile.Empty;
        }
        present = true;
        try
        {
            if (OverrideFile.TryParse(asset.ToText(), out var file, out var errs) && file is not null)
                return file;
            errors = errs;
        }
        catch (Exception ex)
        {
            errors = [ex.ToString()];
        }
        api.Logger.Error("[tidyvariants] {0} is invalid, using no overrides ({1} problem(s)):\n  {2}",
            OverridesLocation, errors.Count, string.Join("\n  ", errors));
        return OverrideFile.Empty;
    }
}
