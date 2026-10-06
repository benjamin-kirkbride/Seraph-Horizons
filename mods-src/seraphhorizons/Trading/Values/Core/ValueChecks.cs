namespace SeraphHorizons.Mod.Trading.Values.Core;

/// <summary>One recipe as the value checks read it: output code and count, and per consumed
/// ingredient its code (one of its accepted codes) and quantity. Tools are left out.</summary>
public sealed record RecipeRow(string Name, string Output, int OutputCount, IReadOnlyList<(string Code, int Quantity)> Ingredients);

/// <summary>An item valued below what the cheapest recipe making it spends on ingredients.</summary>
public sealed record BelowIngredients(string Code, double Value, double Ingredients, string Recipe);

/// <summary>
/// The item values report's two checks (<c>tools/item-values/itemvalues.py</c>, <c>report</c>),
/// for <c>/sh trade values missing|suspicious</c> (#459) against the table the server loaded:
/// codes with no value at all, and items valued below their ingredients. The tool derives a value
/// from its recipe route plus a labour markup, so a derived value is never below its ingredients;
/// one that is, is a hand price (override or raw) to review. Here the routes are the game's grid
/// recipes, and an ingredient's cost is its table value (direct or family).
/// </summary>
public static class ValueChecks
{
    /// <summary>The codes <paramref name="values"/> has no value for, not even a family's, sorted.</summary>
    public static List<string> Missing(ItemValues values, IEnumerable<string> codes) =>
        codes.Distinct(StringComparer.Ordinal)
            .Where(c => values.Lookup(c).Source == ValueSource.Missing)
            .Order(StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// Directly valued outputs whose value is under the ingredients' cost per item of the cheapest
    /// recipe making them (all of whose ingredients have a value); by how far under, worst first.
    /// </summary>
    public static List<BelowIngredients> Below(ItemValues values, IEnumerable<RecipeRow> recipes, double tolerance = 1e-6)
    {
        var best = new Dictionary<string, (double Cost, string Recipe)>(StringComparer.Ordinal);
        foreach (var r in recipes)
        {
            if (r.OutputCount <= 0 || r.Ingredients.Count == 0) continue;
            double cost = 0;
            bool known = true;
            foreach (var (code, quantity) in r.Ingredients)
            {
                var l = values.Lookup(code);
                if (l.Source == ValueSource.Missing)
                {
                    known = false;
                    break;
                }
                cost += l.Value * quantity;
            }
            if (!known) continue;
            string output = Normalise(r.Output);
            double perItem = cost / r.OutputCount;
            if (!best.TryGetValue(output, out var b) || perItem < b.Cost) best[output] = (perItem, r.Name);
        }
        var below = new List<BelowIngredients>();
        foreach (var (code, (cost, recipe)) in best)
        {
            var l = values.Lookup(code);
            if (l.Source != ValueSource.Direct) continue;
            if (l.Value < cost - tolerance * Math.Max(1, cost))
                below.Add(new BelowIngredients(code, l.Value, Math.Round(cost, 3), recipe));
        }
        return below.OrderBy(b => b.Value / Math.Max(1e-9, b.Ingredients)).ThenBy(b => b.Code, StringComparer.Ordinal).ToList();
    }

    private static string Normalise(string code)
    {
        code = code.ToLowerInvariant();
        return code.Contains(':') ? code : "game:" + code;
    }
}
