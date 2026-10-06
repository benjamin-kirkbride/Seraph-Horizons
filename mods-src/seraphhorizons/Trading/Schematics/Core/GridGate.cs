namespace SeraphHorizons.Mod.Trading.Schematics.Core;

/// <summary>
/// Where a gated grid recipe takes its schematic. <see cref="Pattern"/> is the new pattern (no
/// commas, <see cref="Width"/> × <see cref="Height"/>) with <see cref="SchematicKey"/> in one slot.
/// When the recipe had no room, two slots of one ingredient became one: <see cref="DoubledKey"/>
/// is a new key for <see cref="DoubledFrom"/>'s ingredient at twice its quantity.
/// </summary>
public sealed record GridPlacement(string Pattern, int Width, int Height, char SchematicKey, char? DoubledFrom, char? DoubledKey);

/// <summary>
/// Adds a slot to a grid recipe's pattern: the first empty slot (<c>_</c> or space); else a new
/// column on the right if the recipe is narrower than the grid, else a new row at the bottom;
/// else, in a full 3×3, two slots of the ingredient filling the most slots become one at double
/// quantity (only ingredients the caller allows: consumed, not a tool, and a doubled stack still
/// fits), which frees the other. Null when none of these works.
/// </summary>
public static class GridGate
{
    public const int GridSize = 3;

    private const string FreeKeys = "ZYXWVUTSRQPONMLKJIHGFEDCBA9876543210";

    public static bool IsEmpty(char c) => c == '_' || c == ' ';

    /// <param name="pattern">The recipe's pattern, commas allowed.</param>
    /// <param name="usedKeys">Every ingredient key the recipe defines (pattern or not).</param>
    /// <param name="canDouble">Whether two slots of this key's ingredient may become one slot of
    /// twice the quantity.</param>
    public static GridPlacement? Place(string pattern, int width, int height, IEnumerable<char> usedKeys, Func<char, bool> canDouble)
    {
        string p = pattern.Replace(",", "").Replace("\t", "").Replace("\r", "").Replace("\n", "");
        if (p.Length != width * height) return null;
        var used = new HashSet<char>(usedKeys.Concat(p));
        char key = FreeKeys.First(k => !used.Contains(k));
        used.Add(key);

        int blank = p.IndexOfAny(['_', ' ']);
        if (blank >= 0)
            return new(Set(p, blank, key), width, height, key, null, null);

        if (width < GridSize)
        {
            var rows = Enumerable.Range(0, height).Select(r => p.Substring(r * width, width) + (r == 0 ? key : '_'));
            return new(string.Concat(rows), width + 1, height, key, null, null);
        }
        if (height < GridSize)
            return new(p + key + new string('_', width - 1), width, height + 1, key, null, null);

        var candidate = p.GroupBy(c => c).Where(g => g.Count() >= 2 && canDouble(g.Key))
            .OrderByDescending(g => g.Count()).ThenBy(g => p.IndexOf(g.Key)).FirstOrDefault();
        if (candidate is null) return null;
        char from = candidate.Key;
        char doubled = FreeKeys.First(k => !used.Contains(k));
        int last = p.LastIndexOf(from);
        int previous = p.LastIndexOf(from, last - 1);
        p = Set(Set(p, last, key), previous, doubled);
        return new(p, width, height, key, from, doubled);
    }

    private static string Set(string s, int i, char c) => s[..i] + c + s[(i + 1)..];
}

/// <summary>The rules for recipes that make or use a sold schematic.</summary>
public static class SchematicRecipeRules
{
    /// <summary>A recipe of one slot, consumed, making one (Scrolled's rolling and unrolling,
    /// MadMechanics' conversions): it turns one thing into another and duplicates nothing.</summary>
    public static bool IsConversion(int filledSlots, bool onlySlotConsumed, int outputQuantity) =>
        filledSlots == 1 && onlySlotConsumed && outputQuantity == 1;

    /// <summary>A recipe making a sold schematic that is not a conversion makes one from nothing or
    /// copies one: taken out (schematics come only from traders).</summary>
    public static bool RemoveRecipe(bool outputIsSold, bool isConversion) => outputIsSold && !isConversion;

    /// <summary>A sold schematic in a recipe that is not a conversion is kept on crafting.</summary>
    public static bool KeepIngredient(bool ingredientIsSold, bool isConversion) => ingredientIsSold && !isConversion;
}
