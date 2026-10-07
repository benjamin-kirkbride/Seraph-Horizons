using System.Text.Json;
using System.Text.Json.Nodes;

namespace SeraphHorizons.Mod.Pipes.Core;

/// <summary>
/// What <c>patches/unifiedpipes-ppex.json</c> assumes of Pipes and Power Expanded's assets (0.7.1),
/// checked before the patch loader runs: each patched blocktype has its <c>material</c> variant
/// third, with exactly the states the patch adds to, and an <c>iron4</c> texture by type for each;
/// and the recipes the patch switches off are, at the indices it names, the ones it means. Each
/// check returns the problem, or null when the asset is as expected.
/// </summary>
public static class PipeAssetGuard
{
    /// <summary>The pipe blocktypes the patch adds copper and lead to.</summary>
    public static readonly string[] PipeBlocktypes =
    [
        "blocktypes/pipes/straight.json", "blocktypes/pipes/bend.json",
        "blocktypes/pipes/tjunction.json", "blocktypes/pipes/xjunction.json",
    ];

    /// <summary>The valve blocktypes the patch adds the bronzes to.</summary>
    public static readonly string[] ValveBlocktypes = ["blocktypes/pipes/valve.json", "blocktypes/pipes/pressurevalve.json"];

    public const string RecipeFile = "recipes/grid/pipes.json";

    /// <summary>ppex's pipe recipes the patch switches off, by index in <see cref="RecipeFile"/>,
    /// with the start of each one's output code: the plate-and-nails pipes (0-3) and the valves,
    /// each with its ppex-gear twin (7-10).</summary>
    public static readonly IReadOnlyDictionary<int, string> DisabledRecipes = new Dictionary<int, string>
    {
        [0] = "ppex:pipe-straight-ns-",
        [1] = "ppex:pipe-bend-nw-",
        [2] = "ppex:pipe-tjunction-uns-",
        [3] = "ppex:pipe-xjunction-nswe-",
        [7] = "ppex:pipe-valve-sn-",
        [8] = "ppex:pipe-pressurevalve-sn-",
        [9] = "ppex:pipe-valve-sn-",
        [10] = "ppex:pipe-pressurevalve-sn-",
    };

    public const string MaterialGroup = "material";
    public const int MaterialGroupIndex = 2;
    public const string TextureKey = "iron4";

    private static readonly JsonDocumentOptions Lenient = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static JsonNode? Parse(string json) => JsonNode.Parse(json, documentOptions: Lenient);

    /// <summary>A pipe or valve blocktype: the material group third with states iron and steel, a
    /// <c>*-iron</c> and <c>*-steel</c> texture by type naming <see cref="TextureKey"/>, and none
    /// yet for <paramref name="added"/>.</summary>
    public static string? CheckBlocktype(string name, string json, IEnumerable<string> added)
    {
        JsonNode? root;
        try
        {
            root = Parse(json);
        }
        catch (JsonException e)
        {
            return $"{name} does not parse ({e.Message})";
        }
        var groups = root?["variantgroups"] as JsonArray;
        var group = groups != null && groups.Count > MaterialGroupIndex ? groups[MaterialGroupIndex] : null;
        if ((string?)group?["code"] != MaterialGroup
            || group["states"] is not JsonArray states
            || !states.Select(s => (string?)s).SequenceEqual(PipeRules.PpexMaterials))
            return $"{name}'s variants (the third is not {MaterialGroup} with states iron and steel)";
        if (root!["texturesByType"] is not JsonObject textures
            || PipeRules.PpexMaterials.Any(m => textures[$"*-{m}"]?[TextureKey] is not JsonObject)
            || added.Any(m => textures.ContainsKey($"*-{m}")))
            return $"{name}'s textures (no {TextureKey} by material)";
        return null;
    }

    /// <summary>ppex's pipe recipe file: each of <see cref="DisabledRecipes"/> at its index makes
    /// what the patch takes it for.</summary>
    public static string? CheckRecipes(string json)
    {
        JsonNode? root;
        try
        {
            root = Parse(json);
        }
        catch (JsonException e)
        {
            return $"{RecipeFile} does not parse ({e.Message})";
        }
        if (root is not JsonArray recipes)
            return $"{RecipeFile} is not a list";
        foreach (var (index, output) in DisabledRecipes)
        {
            var code = index < recipes.Count ? (string?)recipes[index]?["output"]?["code"] : null;
            if (code == null || !code.StartsWith(output, StringComparison.Ordinal))
                return $"{RecipeFile} (recipe {index} does not make {output}*)";
        }
        return null;
    }
}
