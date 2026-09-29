using Newtonsoft.Json.Linq;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace SeraphHorizons.RecipeExport.Items;

/// <summary>
/// The handbook's guide pages: every config/handbook/*.json of every domain, as the survival
/// mod's GuiDialogSurvivalHandbook loads them (sorted by asset location, text translated
/// only when it is shorter than 255 characters, i.e. a lang key). Call inside an EnglishLocale.
/// </summary>
internal static class Guides
{
    public static JArray Build(ICoreServerAPI api, ModIndex mods)
    {
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
            var text = Get(page, "text") ?? "";
            if (text.Length < 255) text = Lang.Get(text);

            var guide = new JObject
            {
                ["code"] = code,
                ["title"] = Lang.Get(title),
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
