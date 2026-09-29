using Newtonsoft.Json.Linq;
using Vintagestory.API.Server;

namespace SeraphHorizons.RecipeExport;

/// <summary>Writes `mods`, `items` and `guides`.</summary>
public static class ItemSection
{
    /// <param name="referenced">Codes recipes reference; exported even when hidden from the handbook.</param>
    public static void Fill(ICoreServerAPI api, JObject root, ISet<string> referenced)
    {
        root["mods"] = new JObject();
        root["items"] = new JObject();
        root["guides"] = new JArray();
    }
}
