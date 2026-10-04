using Newtonsoft.Json.Linq;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace SeraphHorizons.RecipeExport.Items;

/// <summary>
/// The handbook's guide pages: every config/handbook/*.json of every domain, as the survival
/// mod's GuiDialogSurvivalHandbook loads them (sorted by asset location, text translated
/// only when it is shorter than 255 characters, i.e. a lang key). Call inside an EnglishLocale.
/// A page a mod hides from players in client code is left out when that mod lists it, as
/// <c>(string PageCode, string Title)</c> tuples with the title's lang key, in the server's
/// <c>ObjectCache[</c><see cref="HiddenKey"/><c>]</c> (seraphhorizons' unified woodworking guide
/// does so for the two guides it replaces).
/// </summary>
internal static class Guides
{
    public const string HiddenKey = "handbook-hiddenGuides";

    public static JArray Build(ICoreServerAPI api, ModIndex mods)
    {
        var hidden = api.ObjectCache.TryGetValue(HiddenKey, out var listed) && listed is IEnumerable<(string, string)> pages
            ? pages.ToHashSet()
            : [];
        var guides = new JArray();
        var assets = api.Assets.GetMany("config/handbook/")
            .Where(a => a.Location.Path.EndsWith(".json", StringComparison.Ordinal))
            .OrderBy(a => a.Location.ToString(), StringComparer.Ordinal);
        foreach (var asset in assets)
        {
            JObject? page;
            try { page = asset.ToObject<JObject>(); }
            catch (Exception e)
            {
                api.Logger.Warning("[seraphexport] could not read handbook page {0}: {1}", asset.Location, e.Message);
                continue;
            }
            if (page == null) continue;
            var code = Get(page, "pageCode");
            if (string.IsNullOrEmpty(code)) continue;
            var title = Get(page, "title") ?? "";
            if (hidden.Contains((code, title))) continue;
            var text = Get(page, "text") ?? "";
            if (text.Length < 255) text = Lang.GetUnformatted(text);

            var guide = new JObject
            {
                ["code"] = code,
                ["title"] = Lang.GetUnformatted(title),
                ["text"] = text,
            };
            var mod = mods.ModForAsset(asset);
            if (mod != null) guide["mod"] = mod;
            guides.Add(guide);
        }
        return guides;
    }

    // The game deserialises these case-insensitively into GuiHandbookTextPage.
    private static string? Get(JObject o, string name) =>
        o.GetValue(name, StringComparison.OrdinalIgnoreCase)?.Value<string>();
}
