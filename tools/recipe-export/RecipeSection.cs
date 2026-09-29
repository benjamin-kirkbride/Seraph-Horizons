using Newtonsoft.Json.Linq;
using Vintagestory.API.Server;

namespace SeraphHorizons.RecipeExport;

/// <summary>Writes `recipes` and `recipeTypes`.</summary>
public static class RecipeSection
{
    /// <returns>Every item and block code the exported recipes reference.</returns>
    public static ISet<string> Fill(ICoreServerAPI api, JObject root)
    {
        root["recipes"] = new JArray();
        root["recipeTypes"] = new JObject();
        return new HashSet<string>();
    }
}
