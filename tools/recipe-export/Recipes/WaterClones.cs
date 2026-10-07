using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;

namespace SeraphHorizons.RecipeExport.Recipes;

/// <summary>
/// Folds Hydrate or Diedrate's copies of water recipes back into the recipes they copy.
///
/// At load, HoD's RecipeGenerator (2.5.6) goes through every recipe list and, for each
/// registered recipe with an ingredient that is `game:waterportion` (or a container whose
/// `recipeAttributes.requiresContent` is), registers one clone per kind of HoD water
/// (`hydrateordiedrate:waterportion-boiled-natural-clean`, `-boiled-rain-clean`,
/// `-fresh-distilled-clean`, `-fresh-rain-clean`, `-fresh-well-clean`), and for
/// `game:saltwaterportion` one with `waterportion-salt-well-clean`. Every matching slot of
/// the clone gets the same water. The clone's Name becomes
/// `hydrateordiedrate:-HoD-<original Name path>-<water path>`; nothing else changes, except
/// that liquid quantities are resolved again from their litres. (Recipe types whose slots
/// take a list of alternatives, A Culinary Artillery's kneading, get the waters appended to
/// the slot instead, which <see cref="Grouper"/> already handles.)
///
/// A clone is folded when another registered recipe of the same registry is equal to it in
/// everything but the water: the same slots in the same order with the same codes,
/// quantities (litres for liquids), attributes and recipe attributes, the same outputs and
/// the same type block, and every slot that differs holds the source water in the original
/// and the clone's water in the clone. The clone's water is appended to the original slot's
/// alternatives (for a container, the container is already there and only `extra` records
/// the water), and the clone is dropped. Anything else stays a recipe of its own.
/// </summary>
public static class WaterClones
{
    public const string Mod = "hydrateordiedrate";
    private const string NamePrefix = "-HoD-";
    private const string WaterPrefix = "waterportion-";
    private const string Masked = "\u0001water";

    /// <summary>Returns the recipes left once every clone with an original is folded into it.</summary>
    public static List<RecipeForm> Fold(IReadOnlyList<RecipeForm> registered, out int folded)
    {
        folded = 0;
        var originals = new Dictionary<string, List<(RecipeForm Form, List<AssetLocation> Sources)>>();
        foreach (var form in registered)
        {
            if (IsClone(form)) continue;
            var sources = new List<AssetLocation>();
            var key = Signature(form, code => IsSource(code) ? code : null, sources);
            if (sources.Count > 0)
            {
                if (!originals.TryGetValue(key, out var list)) originals[key] = list = new();
                list.Add((form, sources));
            }
        }

        var kept = new List<RecipeForm>(registered.Count);
        foreach (var form in registered)
        {
            if (!IsClone(form) || Original(form, originals) is not var (original, water))
            {
                kept.Add(form);
                continue;
            }
            Merge(original, form, water);
            folded++;
        }
        return kept;
    }

    private static bool IsClone(RecipeForm form) =>
        form.Name is { } n && n.Domain == Mod && n.Path.StartsWith(NamePrefix, StringComparison.Ordinal);

    private static bool IsSource(AssetLocation code) =>
        code.Domain == "game" && code.Path is "waterportion" or "saltwaterportion";

    private static bool IsWater(AssetLocation code) =>
        code.Domain == Mod && code.Path.StartsWith(WaterPrefix, StringComparison.Ordinal);

    /// <summary>HoD gives salt water its salt well water, and plain water every other kind.</summary>
    private static bool Replaces(AssetLocation source, AssetLocation water) =>
        (source.Path == "saltwaterportion") == water.Path.StartsWith(WaterPrefix + "salt-", StringComparison.Ordinal);

    private static (RecipeForm Original, AssetLocation Water)? Original(
        RecipeForm clone, Dictionary<string, List<(RecipeForm Form, List<AssetLocation> Sources)>> originals)
    {
        var waters = new List<AssetLocation>();
        var key = Signature(clone, code => IsWater(code) ? code : null, waters);
        if (waters.Count == 0) return null;
        var water = waters[0];
        if (waters.Any(w => !w.Equals(water))) return null;
        if (!originals.TryGetValue(key, out var candidates)) return null;
        var fitting = candidates
            .Where(c => c.Sources.Count == waters.Count && c.Sources.All(s => Replaces(s, water)) && SameTags(c.Form, clone))
            .Select(c => c.Form).ToList();
        if (fitting.Count == 0) return null;
        // Equal recipes in two files: the one the clone's Name was made from.
        var named = fitting.FirstOrDefault(f => f.Name != null && clone.Name!.Path == $"{NamePrefix}{f.Name.Path}-{water.Path}");
        return (named ?? fitting[0], water);
    }

    private static void Merge(RecipeForm original, RecipeForm clone, AssetLocation water)
    {
        for (int i = 0; i < original.Slots.Count; i++)
        {
            var slot = original.Slots[i];
            var spec = clone.Slots[i].Accepts.FirstOrDefault(s => s.Code != null && IsWater(s.Code));
            if (spec != null && slot.Accepts.All(s => !Equals(s.Code, spec.Code))) slot.Accepts.Add(spec);
        }
        original.FoldedWater.Add(water.ToString());
    }

    /// <summary>
    /// Tag conditions do not serialise; compare them pairwise (the signatures already matched,
    /// so the slots line up). The original may have grown waters from earlier copies at the end.
    /// </summary>
    private static bool SameTags(RecipeForm original, RecipeForm clone)
    {
        for (int i = 0; i < clone.Slots.Count; i++)
        for (int j = 0; j < clone.Slots[i].Accepts.Count; j++)
            if (!original.Slots[i].Accepts[j].Tags.Equals(clone.Slots[i].Accepts[j].Tags)) return false;
        return true;
    }

    /// <summary>
    /// Everything that must be equal, as text, with every water code that <paramref name="mask"/>
    /// picks (in a slot or in a slot's `requiresContent`) replaced by a marker and collected.
    /// </summary>
    private static string Signature(RecipeForm form, System.Func<AssetLocation, AssetLocation?> mask, List<AssetLocation> masked)
    {
        string? Code(AssetLocation? code)
        {
            if (code == null) return null;
            if (mask(code) is { } m) { masked.Add(m); return Masked; }
            return code.ToString();
        }
        static JToken? Amount(StackSpec s) => s.Litres is { } l ? new JValue("L" + l) : new JValue(s.Quantity);
        JObject Stack(StackSpec s) => new()
        {
            ["type"] = s.Type.ToString(),
            ["code"] = Code(s.Code),
            ["amount"] = Amount(s),
            ["attributes"] = s.Attributes?.DeepClone(),
            ["wildcard"] = s.WildcardName,
            ["allowed"] = s.AllowedVariants == null ? null : new JArray(s.AllowedVariants),
            ["skip"] = s.SkipVariants == null ? null : new JArray(s.SkipVariants),
            ["tagsOnly"] = s.Code == null,
        };
        JObject Slot(SlotForm s)
        {
            var extra = (JObject)s.Extra.DeepClone();
            if (extra.SelectToken("recipeAttributes.requiresContent.code") is JValue { Value: string content })
            {
                var location = new AssetLocation(content);
                if (!content.Contains(':')) location.Domain = "game";
                extra["recipeAttributes"]!["requiresContent"]!["code"] = Code(location);
            }
            return new JObject
            {
                ["key"] = s.Key,
                ["role"] = s.Role,
                ["min"] = s.MinQuantity,
                ["max"] = s.MaxQuantity,
                ["minRatio"] = s.MinRatio,
                ["maxRatio"] = s.MaxRatio,
                ["tool"] = s.IsTool,
                ["toolCost"] = s.ToolDurabilityCost,
                ["returned"] = s.Returned == null ? null : Stack(s.Returned),
                ["extra"] = extra,
                ["accepts"] = new JArray(s.Accepts.Select(Stack)),
            };
        }
        var o = new JObject
        {
            ["identity"] = form.IdentityCode,
            ["enabled"] = form.Enabled,
            ["blockName"] = form.BlockName,
            ["block"] = form.Block?.DeepClone(),
            ["voxels"] = form.Voxels?.DeepClone(),
            ["requirements"] = new JArray(form.Requirements),
            ["attributes"] = form.Attributes?.DeepClone(),
            ["extra"] = form.Extra.DeepClone(),
            ["slots"] = new JArray(form.Slots.Select(Slot)),
            // Outputs are never masked: the clone makes exactly what the original makes.
            ["outputs"] = new JArray(form.Outputs.Select(op => new JObject
            {
                ["type"] = op.Stack.Type.ToString(),
                ["code"] = op.Stack.Code?.ToString(),
                ["amount"] = Amount(op.Stack),
                ["attributes"] = op.Stack.Attributes?.DeepClone(),
                ["extra"] = op.Extra.DeepClone(),
            })),
        };
        return o.ToString(Formatting.None);
    }
}
