using Newtonsoft.Json.Linq;

namespace SeraphHorizons.Mod.Pipes.Core;

/// <summary>
/// The hollow section is the game's chute section (<c>game:chutesection-{material}</c>, a square
/// tube; the game has copper only). <c>patches/unifiedpipes-chutesection.json</c> adds lead to its
/// <c>material</c> variant (its texture is by <c>{material}</c>, so lead gets its own sheet), lists
/// only copper in the creative inventory's mechanics tab (chutes take copper only), gives it a
/// handbook section, switches off the game's two recipes for it, the anvil's (one ingot) and the
/// grid's "plate + 2 solder + soldering iron + hammer = 2 sections": a section is two angles
/// (<see cref="PipeSections"/>) soldered together on the grid (<c>recipes/grid/chutesection.json</c>).
/// The patch also makes the game's five chute recipes take a tin or silver solder bar per section and
/// a soldering iron (<see cref="ChuteRecipes"/>). <c>patches/unifiedpipes-betterruins.json</c>
/// switches off Better Ruins' five bulk chute recipes on its Machinist's Mechanism Blueprint
/// (<see cref="BetterRuinsChutes"/>), which take no solder. This checks the files are what the
/// patches assume. Game-independent: <c>UnifiedPipesSystem</c> hands it the asset text, the unit
/// tests a fixture. The game's JSON is lenient (unquoted keys, single quotes), so this reads it
/// with Newtonsoft.
/// </summary>
public static class ChuteSections
{
    public const string ItemFile = "itemtypes/resource/chutesection.json";
    public const string RecipeFile = "recipes/grid/chute.json";
    public const string SmithingFile = "recipes/smithing/chutesection.json";

    public const string Group = "material";

    /// <summary>The material states the game ships.</summary>
    public static readonly string[] GameMetals = [PipeRules.Copper];

    /// <summary>The states the patch adds: lead (iron and steel never make hollow sections).</summary>
    public static readonly string[] AddedMetals = [PipeRules.Lead];

    /// <summary>Every hollow section metal: the soldered ones.</summary>
    public static readonly string[] Metals = [PipeRules.Copper, PipeRules.Lead];

    /// <summary>The game's plate-and-solder recipe the patch switches off, by index in <see cref="RecipeFile"/>.</summary>
    public const int PlateRecipeIndex = 5;

    /// <summary>The game's chute recipes, by index in <see cref="RecipeFile"/>: what each makes, its
    /// pattern of sections (<c>I</c>, copper sections alone), and the pattern the patch gives it with
    /// the solder bars (<c>S</c>, one per section) and the soldering iron (<c>T</c>) in free cells.
    /// The yields stay the game's.</summary>
    public static readonly (int Index, string Output, string Pattern, int Width, int Height, string Soldered, int SolderedWidth, int SolderedHeight, int Sections)[] ChuteRecipes =
    [
        (0, "chute-elbow-down-east", "I_,_I", 2, 2, "IS,TI", 2, 2, 2),
        (1, "chute-straight-ns", "I,I", 2, 1, "II,ST", 2, 2, 2),
        (2, "chute-cross-ground", "_I_,I_I,_I_", 3, 3, "SIT,I_I,_I_", 3, 3, 4),
        (3, "chute-t-ns", "_I_,I_I", 3, 2, "SIT,I_I", 3, 2, 3),
        (4, "chute-3way-down-east", "_I_,II_", 3, 2, "SIT,II_", 3, 2, 3),
    ];

    public const string SolderKey = "S";
    public const string SolderingIronKey = "T";

    /// <summary>Better Ruins' recipes on its Machinist's Mechanism Blueprint (0.6.4).</summary>
    public const string BetterRuinsId = "betterruins";
    public const string BetterRuinsFile = "recipes/grid/schematic-mechanical/mechanical.json";
    public const string BetterRuinsBlueprint = "betterruins:br-schematic-mechanical";

    /// <summary>Its five chute recipes, by index, with what each makes (8 straight, 4 of each other
    /// kind, from copper sections and the blueprint, no solder); the patch switches them off.</summary>
    public static readonly IReadOnlyDictionary<int, string> BetterRuinsChutes = new Dictionary<int, string>
    {
        [6] = "game:chute-elbow-down-east",
        [7] = "game:chute-straight-ns",
        [8] = "game:chute-cross-ground",
        [9] = "game:chute-t-ns",
        [10] = "game:chute-3way-down-east",
    };

    public const string TextureKey = "metaltex";

    /// <summary>The soldering recipe: two angles and two solder bars make one hollow section.</summary>
    public const int AnglesPerSection = 2;
    public const int SolderBarsPerSection = 2;

    public static string Section(string metal) => $"game:chutesection-{metal}";

    private static readonly JsonLoadSettings Lenient = new() { CommentHandling = CommentHandling.Ignore };

    /// <summary>What the patch expects of the game's chute section item type, or null when it
    /// holds: coded <c>chutesection</c>, its first variant group <c>material</c> with exactly the
    /// states the game ships, a plain <c>metaltex</c> texture by <c>{material}</c> (so a new state
    /// gets its own metal's sheet) and no texture by type, a creative inventory to replace, and no
    /// handbook attributes yet (the patch adds them).</summary>
    public static string? CheckItem(string json)
    {
        JObject root;
        try
        {
            root = JObject.Parse(json, Lenient);
        }
        catch (Exception e)
        {
            return $"its chute section ({ItemFile} does not parse: {e.Message})";
        }
        if ((string?)root["code"] != "chutesection")
            return "its chute section's code";
        var group = (root["variantgroups"] as JArray)?.FirstOrDefault();
        if ((string?)group?["code"] != Group || group["states"] is not JArray states
            || !states.Select(s => (string?)s).SequenceEqual(GameMetals))
            return $"its chute section's variants (the first is not {Group} with copper alone)";
        var texture = (string?)root["textures"]?[TextureKey]?["base"];
        if (texture == null || !texture.Contains("{" + Group + "}", StringComparison.Ordinal)
            || root["texturesByType"] != null || root["texturesbytype"] != null)
            return $"its chute section's texture (not one {TextureKey} by {{{Group}}})";
        if (root["creativeinventory"] is not JObject)
            return "its chute section's creative inventory";
        if (root["attributes"] is not JObject attributes || attributes["handbook"] != null)
            return "its chute section's attributes (missing, or a handbook entry already)";
        return null;
    }

    /// <summary>What the patch expects of the game's chute recipes, or null when it holds: each of
    /// <see cref="ChuteRecipes"/> at its index makes its chute from copper sections alone in its
    /// pattern, the recipe at <see cref="PlateRecipeIndex"/> makes two copper chute sections from a
    /// copper plate and solder, and no other recipe in the file makes a chute section.</summary>
    public static string? CheckRecipes(string json)
    {
        JArray recipes;
        try
        {
            recipes = JArray.Parse(json, Lenient);
        }
        catch (Exception e)
        {
            return $"its chute recipes ({RecipeFile} does not parse: {e.Message})";
        }
        if (recipes.Count <= PlateRecipeIndex)
            return $"its chute recipes (fewer than {PlateRecipeIndex + 1})";
        for (int i = 0; i < recipes.Count; i++)
        {
            bool section = Path((string?)recipes[i]?["output"]?["code"]).StartsWith("chutesection", StringComparison.Ordinal);
            if (section != (i == PlateRecipeIndex))
                return $"its chute recipes (recipe {i} {(section ? "makes" : "does not make")} a chute section)";
        }
        var plate = recipes[PlateRecipeIndex]!;
        var codes = (plate["ingredients"] as JObject)?.Properties().Select(p => Path((string?)p.Value["code"])).ToList() ?? [];
        if (Path((string?)plate["output"]?["code"]) != "chutesection-copper" || (int?)plate["output"]?["quantity"] != 2
            || !codes.Contains("metalplate-copper") || !codes.Contains("solderbar-*"))
            return $"its chute recipes (recipe {PlateRecipeIndex} is not 2 copper sections from a copper plate and solder)";
        foreach (var chute in ChuteRecipes)
        {
            var r = recipes[chute.Index]!;
            var ingredients = r["ingredients"] as JObject;
            if (Path((string?)r["output"]?["code"]) != chute.Output || (string?)r["ingredientPattern"] != chute.Pattern
                || (int?)r["width"] != chute.Width || (int?)r["height"] != chute.Height
                || ingredients == null || !ingredients.Properties().Select(p => p.Name).SequenceEqual(["I"])
                || Path((string?)ingredients["I"]?["code"]) != "chutesection-copper")
                return $"its chute recipes (recipe {chute.Index} is not {chute.Output} from copper sections alone in {chute.Pattern})";
        }
        return null;
    }

    /// <summary>What the patch expects of the game's anvil recipe for the chute section
    /// (<see cref="SmithingFile"/>), or null when it holds: one recipe (an object, not a list) making
    /// <c>chutesection-copper</c> from a copper ingot, not already switched off. The patch adds
    /// <c>/enabled</c> false to it.</summary>
    public static string? CheckSmithing(string json)
    {
        JToken root;
        try
        {
            root = JToken.Parse(json, Lenient);
        }
        catch (Exception e)
        {
            return $"its chute section's anvil recipe ({SmithingFile} does not parse: {e.Message})";
        }
        if (root is not JObject recipe)
            return $"its chute section's anvil recipe ({SmithingFile} is not one recipe)";
        var ingredient = recipe["ingredient"];
        if (Path((string?)recipe["output"]?["code"]) != "chutesection-copper"
            || Path((string?)ingredient?["code"]) != "ingot-*"
            || ingredient?["allowedVariants"] is not JArray metals || !metals.Select(m => (string?)m).SequenceEqual(GameMetals)
            || recipe["enabled"] != null)
            return $"its chute section's anvil recipe ({SmithingFile} is not one copper section from a copper ingot)";
        return null;
    }

    /// <summary>What <c>patches/unifiedpipes-betterruins.json</c> expects of Better Ruins' blueprint
    /// recipes, or null when it holds: each of <see cref="BetterRuinsChutes"/> at its index makes
    /// that chute from copper chute sections and the blueprint, and nothing else.</summary>
    public static string? CheckBetterRuins(string json)
    {
        JArray recipes;
        try
        {
            recipes = JArray.Parse(json, Lenient);
        }
        catch (Exception e)
        {
            return $"{BetterRuinsFile} (does not parse: {e.Message})";
        }
        foreach (var (index, output) in BetterRuinsChutes)
        {
            var r = index < recipes.Count ? recipes[index] : null;
            var codes = (r?["ingredients"] as JObject)?.Properties().Select(p => (string?)p.Value["code"]).Order().ToList();
            if ((string?)r?["output"]?["code"] != output
                || codes is not [var first, var second] || first != BetterRuinsBlueprint || second != "game:chutesection-copper")
                return $"{BetterRuinsFile} (recipe {index} is not {output} from copper chute sections and the blueprint)";
        }
        return null;
    }

    private static string Path(string? code) => code == null ? "" : code.Contains(':') ? code[(code.IndexOf(':') + 1)..] : code;
}
