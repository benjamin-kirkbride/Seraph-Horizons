using Newtonsoft.Json.Linq;
using Vintagestory.API.Server;

namespace SeraphHorizons.RecipeExport;

/// <summary>
/// Builds one export document (schema/recipe-export.schema.json). Usable without the mod
/// being loaded, so the Atlas scenarios can call it on their own server.
/// </summary>
public static class Exporter
{
    public const int SchemaVersion = 1;
    public const string GeneratorName = "seraphexport";

    public static JObject Build(ICoreServerAPI api, PackInfo pack) => Build(api, pack, out _);

    /// <param name="referenced">Every item and block code the exported recipes reference.</param>
    public static JObject Build(ICoreServerAPI api, PackInfo pack, out ISet<string> referenced)
    {
        var root = new JObject
        {
            ["schemaVersion"] = SchemaVersion,
            ["generator"] = new JObject
            {
                ["name"] = GeneratorName,
                ["version"] = typeof(Exporter).Assembly.GetName().Version?.ToString(3) ?? "0.0.0",
            },
            ["pack"] = new JObject
            {
                ["id"] = pack.Id,
                ["version"] = pack.Version,
                ["gameVersion"] = pack.GameVersion,
            },
        };

        // Recipes first: the item section needs to know which codes they reference.
        referenced = RecipeSection.Fill(api, root);
        ItemSection.Fill(api, root, referenced);
        return root;
    }

    /// <summary>
    /// The document's <c>guides</c> section alone, as <see cref="Build(ICoreServerAPI, PackInfo)"/>
    /// writes it: cheap next to a full export, for a caller that reads only the guide pages.
    /// </summary>
    public static JArray Guides(ICoreServerAPI api)
    {
        using var english = new Items.EnglishLocale();
        return Items.Guides.Build(api, new Items.ModIndex(api));
    }
}

public sealed record PackInfo(string Id, string Version, string GameVersion);
