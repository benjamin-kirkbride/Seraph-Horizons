using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Util;

namespace SeraphHorizons.RecipeExport.Recipes;

/// <summary>
/// Turns what a registered recipe accepts into the concrete, registered stacks that satisfy
/// it, with the same rules as CraftingRecipeIngredient.SatisfiesAsIngredient (class, wildcard
/// with allowed and skipped variants, tags). Results are cached: many recipes share a slot.
/// </summary>
public sealed class StackExpander
{
    private readonly IWorldAccessor _world;
    private readonly Dictionary<string, List<CollectibleObject>> _byDomain;
    private readonly List<CollectibleObject> _all;
    private readonly Dictionary<string, List<CollectibleObject>> _cache = new();

    public StackExpander(IWorldAccessor world)
    {
        _world = world;
        _all = world.Collectibles.Where(c => c?.Code != null && !c.IsMissing)
            .OrderBy(c => c.Code.ToString(), StringComparer.Ordinal).ToList();
        _byDomain = _all.GroupBy(c => c.Code.Domain).ToDictionary(g => g.Key, g => g.ToList());
    }

    /// <summary>Every registered stack the spec accepts, sorted by code. Empty when none.</summary>
    public List<JObject> Accepted(StackSpec spec)
    {
        var matches = Collectibles(spec);
        return matches.Select(c => Stack(c, spec)).ToList();
    }

    /// <summary>The concrete stack a recipe produces, or null when its code is not registered.</summary>
    public JObject? Produced(StackSpec spec)
    {
        var c = spec.Resolved?.Collectible ?? Lookup(spec);
        if (c == null) return null;
        var quantity = spec.Resolved?.StackSize ?? spec.Quantity;
        return Stack(c, spec, quantity);
    }

    private List<CollectibleObject> Collectibles(StackSpec spec)
    {
        if (spec.Code != null && !Readers.IsPattern(spec.Code))
        {
            var c = spec.Resolved?.Collectible ?? Lookup(spec);
            return c == null ? new() : new() { c };
        }

        var key = string.Join("|", spec.Type, spec.Code, Join(spec.AllowedVariants), Join(spec.SkipVariants),
                              spec.Tags.IsEmpty ? "" : spec.Tags.ToString());
        if (_cache.TryGetValue(key, out var hit)) return hit;

        IEnumerable<CollectibleObject> pool = spec.Code == null || spec.Code.Domain == "*"
            ? _all
            : _byDomain.GetValueOrDefault(spec.Code.Domain) ?? new();
        var result = new List<CollectibleObject>();
        foreach (var c in pool)
        {
            if (c.ItemClass != spec.Type) continue;
            if (spec.Code != null)
            {
                bool match = spec.Code.Path.StartsWith('@')
                    ? WildcardUtil.Match(spec.Code, c.Code)
                    : WildcardUtil.Match(spec.Code, c.Code, spec.AllowedVariants);
                if (!match) continue;
                if (spec.SkipVariants != null && WildcardUtil.Match(spec.Code, c.Code, spec.SkipVariants)) continue;
            }
            if (!spec.Tags.IsEmpty && !spec.Tags.Matches(c.Tags)) continue;
            result.Add(c);
        }
        _cache[key] = result;
        return result;
    }

    private CollectibleObject? Lookup(StackSpec spec)
    {
        if (spec.Code == null || Readers.IsPattern(spec.Code)) return null;
        CollectibleObject? c = spec.Type == EnumItemClass.Block ? _world.GetBlock(spec.Code) : _world.GetItem(spec.Code);
        return c == null || c.IsMissing || c.Code == null ? null : c;
    }

    private static JObject Stack(CollectibleObject c, StackSpec spec, double? quantity = null)
    {
        var o = new JObject
        {
            ["code"] = c.Code.ToString(),
            ["kind"] = c.ItemClass == EnumItemClass.Block ? "block" : "item",
            ["quantity"] = quantity ?? spec.Quantity,
        };
        if (spec.Litres != null) o["litres"] = spec.Litres;
        if (spec.Attributes is JObject attrs && attrs.HasValues) o["attributes"] = attrs.DeepClone();
        return o;
    }

    private static string Join(string[]? values) => values == null ? "" : string.Join(",", values);
}
