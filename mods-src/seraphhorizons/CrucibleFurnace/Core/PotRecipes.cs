using System.Text.RegularExpressions;

namespace SeraphHorizons.Mod.CrucibleFurnace.Core;

/// <summary>One ingredient of a pot recipe: the item codes that count as it (exact, or with <c>*</c>
/// wildcards) and its share of the charge's units, <see cref="Min"/> to <see cref="Max"/> (0 to 1).
/// <see cref="Name"/> is its lang key's last part.</summary>
public sealed record PotIngredient(string Name, IReadOnlyList<string> Codes, double Min, double Max)
{
    public bool Matches(string code) => Codes.Any(c => PotRecipes.CodeMatches(c, code));
}

/// <summary>
/// A pot recipe (README "Crucible furnace"): a charge whose every item is one of the ingredients,
/// each within its share, melts at <see cref="Temperature"/> into <see cref="Output"/>:
/// <see cref="Yield"/> units of it per unit charged. A <see cref="Pourable"/> output stays in the pot
/// molten (its code is the ingot a mold casts); any other comes out as lumps of
/// <see cref="Stainless.UnitsPerLump"/> units, broken out of the pot when it is pulled, with
/// <see cref="Byproduct"/>, one per <see cref="UnitsPerByproduct"/> units charged. Ratios only: no
/// chemistry is tracked.
/// </summary>
public sealed record PotRecipe(
    string Code,
    IReadOnlyList<PotIngredient> Ingredients,
    string Output,
    bool Pourable,
    double Yield,
    double Temperature,
    string? Byproduct = null,
    int UnitsPerByproduct = 0)
{
    /// <summary>The ingredient <paramref name="code"/> counts as, or null.</summary>
    public PotIngredient? IngredientFor(string code) => Ingredients.FirstOrDefault(i => i.Matches(code));

    /// <summary>Every code a recipe item can be matched by, its domains included (the recipe is
    /// skipped when a domain is not loaded).</summary>
    public IEnumerable<string> Domains =>
        Ingredients.SelectMany(i => i.Codes).Append(Output).Concat(Byproduct is null ? [] : [Byproduct])
            .Select(c => c.Split(':')[0]).Distinct();
}

/// <summary>An item stack in a pot's charge: its code and how many.</summary>
public readonly record struct ChargeItem(string Code, int Count);

/// <summary>Why an item does not go into the pot.</summary>
public enum ChargeRefusal
{
    None,
    /// <summary>No recipe takes it.</summary>
    NotAnIngredient,
    /// <summary>A recipe takes it, but none that takes what is already in the pot.</summary>
    DoesNotMix,
    /// <summary>The pot holds its capacity.</summary>
    PotFull,
    /// <summary>More of it would be more than its largest share of a full pot.</summary>
    TooMuch,
}

/// <summary>How many of an item go into the pot, and if none, why.</summary>
public readonly record struct ChargeAdd(int Count, ChargeRefusal Refusal);

/// <summary>A charge measured against a recipe: the recipe it makes (null if none), or the closest
/// one (the recipe that takes every item in it) with each ingredient's share, to say what is off.</summary>
public sealed record PotMatch(PotRecipe? Recipe, PotRecipe? Closest, IReadOnlyList<(PotIngredient Ingredient, double Share)> Shares, bool TooSmall)
{
    public static readonly PotMatch Empty = new(null, null, [], false);
}

/// <summary>What a melted charge gives: the output's units (a pourable output) or lumps, and the byproduct.</summary>
public readonly record struct PotProducts(int Units, int Lumps, int Byproducts);

/// <summary>
/// The pot recipes and the rules for charging a pot and melting it: what may go in, whether a charge
/// makes anything, and what it makes. Game-independent, with the default recipes built in (the four of
/// README "Crucible furnace"); the game side drops a recipe whose mods are not loaded.
/// </summary>
public sealed class PotRecipes(IReadOnlyList<PotRecipe> recipes)
{
    /// <summary>Units an ingot is worth.</summary>
    public const int UnitsPerIngot = 100;

    /// <summary>Units every other item is worth: a bit's, a nugget's.</summary>
    public const int UnitsPerItem = 5;

    public IReadOnlyList<PotRecipe> Recipes { get; } = recipes;

    public static readonly PotRecipe FerrosiliconRecipe = new("ferrosilicon",
    [
        new("crushedquartz", ["game:crushed-quartz"], 0.45, 0.55),
        new("ironbits", [Stainless.IronBit], 0.25, 0.35),
        new("coke", ["game:coke"], 0.15, 0.25),
    ], Stainless.Ferrosilicon, Pourable: false, Yield: 0.5, Temperature: 1400);

    public static readonly PotRecipe FerrochromeRecipe = new("ferrochrome",
    [
        // Chromite concentrate with ore processing on (OreProcessing, #688): 5 units, as the game's
        // crushed chromite counts here; the item exists only with that switch.
        new("crushedchromite", ["game:crushed-chromite", "seraphhorizons:concentrate-chromite"], 0.45, 0.55),
        new("ferrosilicon", [Stainless.Ferrosilicon], 0.25, 0.35),
        new("lime", ["game:lime"], 0.15, 0.25),
    ], Stainless.Ferrochrome, Pourable: false, Yield: 0.5, Temperature: 1550, Byproduct: "smex:slag", UnitsPerByproduct: 25);

    public static readonly PotRecipe StainlessRecipe = new("stainless",
    [
        new("iron", [Stainless.IronIngot, Stainless.IronBit], 1 - Stainless.FerrochromeMax, 1 - Stainless.FerrochromeMin),
        new("ferrochrome", [Stainless.Ferrochrome], Stainless.FerrochromeMin, Stainless.FerrochromeMax),
    ], Stainless.Ingot, Pourable: true, Yield: 1, Temperature: 1530);

    public static readonly PotRecipe RemeltRecipe = new("stainlessremelt",
    [
        new("stainless", [Stainless.Bit, Stainless.Ingot], 1, 1),
    ], Stainless.Ingot, Pourable: true, Yield: 1, Temperature: 1530);

    public static readonly PotRecipes Default = new([FerrosiliconRecipe, FerrochromeRecipe, StainlessRecipe, RemeltRecipe]);

    /// <summary>The recipe by its code, or null.</summary>
    public PotRecipe? Get(string? code) => Recipes.FirstOrDefault(r => r.Code == code);

    /// <summary>Units one <paramref name="code"/> is worth in a pot: an ingot 100, anything else 5.</summary>
    public static int UnitsOf(string code) =>
        code.Split(':').Last().StartsWith("ingot-", StringComparison.Ordinal) ? UnitsPerIngot : UnitsPerItem;

    public static int TotalUnits(IEnumerable<ChargeItem> charge) => charge.Sum(i => i.Count * UnitsOf(i.Code));

    /// <summary>Whether any recipe takes <paramref name="code"/> at all.</summary>
    public bool IsIngredient(string code) => Recipes.Any(r => r.IngredientFor(code) != null);

    /// <summary>
    /// How many of <paramref name="available"/> <paramref name="code"/> go into a pot of
    /// <paramref name="capacity"/> units holding <paramref name="charge"/>: as many as fit, by a recipe
    /// that takes every item already in it and this one, without the pot going over its capacity or
    /// this ingredient over its largest share of a full pot. The recipe that takes the most decides.
    /// </summary>
    public ChargeAdd CanAdd(IReadOnlyList<ChargeItem> charge, string code, int available, int capacity)
    {
        if (available <= 0)
            return new(0, ChargeRefusal.None);
        if (!IsIngredient(code))
            return new(0, ChargeRefusal.NotAnIngredient);
        int unit = UnitsOf(code);
        int total = TotalUnits(charge);
        int best = 0;
        var refusal = ChargeRefusal.DoesNotMix;
        foreach (var recipe in Recipes)
        {
            if (recipe.IngredientFor(code) is not { } ingredient || !Takes(recipe, charge))
                continue;
            int had = charge.Where(i => recipe.IngredientFor(i.Code) == ingredient).Sum(i => i.Count * UnitsOf(i.Code));
            int byCapacity = (capacity - total) / unit;
            int byShare = (int)Math.Floor((ingredient.Max * capacity - had) / unit + 1e-9);
            int n = Math.Max(0, Math.Min(available, Math.Min(byCapacity, byShare)));
            if (n > best)
                best = n;
            if (n == 0 && refusal != ChargeRefusal.PotFull)
                refusal = byCapacity <= 0 ? ChargeRefusal.PotFull : ChargeRefusal.TooMuch;
        }
        return best > 0 ? new(best, ChargeRefusal.None) : new(0, refusal);
    }

    /// <summary>Whether <paramref name="recipe"/> takes every item of <paramref name="charge"/>.</summary>
    public static bool Takes(PotRecipe recipe, IEnumerable<ChargeItem> charge) =>
        charge.All(i => recipe.IngredientFor(i.Code) != null);

    /// <summary>The recipe <paramref name="charge"/> makes, or, if none, the closest: the first recipe
    /// that takes every item in it, with each ingredient's share. A non-pourable output needs a charge
    /// that gives at least one lump (<see cref="PotMatch.TooSmall"/> otherwise).</summary>
    public PotMatch Match(IReadOnlyList<ChargeItem> charge)
    {
        int total = TotalUnits(charge);
        if (total <= 0)
            return PotMatch.Empty;
        PotMatch? closest = null;
        foreach (var recipe in Recipes)
        {
            if (!Takes(recipe, charge))
                continue;
            var shares = recipe.Ingredients
                .Select(ing => (ing, (double)charge.Where(i => recipe.IngredientFor(i.Code) == ing).Sum(i => i.Count * UnitsOf(i.Code)) / total))
                .ToList();
            bool within = shares.All(s => s.Item2 >= s.ing.Min - 1e-9 && s.Item2 <= s.ing.Max + 1e-9);
            bool tooSmall = !recipe.Pourable && Products(recipe, total).Lumps < 1;
            if (within && !tooSmall)
                return new PotMatch(recipe, recipe, shares, false);
            closest ??= new PotMatch(null, recipe, shares, within && tooSmall);
        }
        return closest ?? PotMatch.Empty;
    }

    /// <summary>What <paramref name="units"/> charged of <paramref name="recipe"/> make.</summary>
    public static PotProducts Products(PotRecipe recipe, int units)
    {
        int output = (int)Math.Floor(units * recipe.Yield + 1e-9);
        int byproducts = recipe.Byproduct != null && recipe.UnitsPerByproduct > 0 ? units / recipe.UnitsPerByproduct : 0;
        return recipe.Pourable
            ? new PotProducts(output, 0, byproducts)
            : new PotProducts(output, output / Stainless.UnitsPerLump, byproducts);
    }

    /// <summary>Whether <paramref name="pattern"/> (a code, <c>*</c> for any run of characters)
    /// matches <paramref name="code"/>; a code without a domain is the game's.</summary>
    public static bool CodeMatches(string pattern, string code)
    {
        pattern = WithDomain(pattern);
        code = WithDomain(code);
        if (!pattern.Contains('*'))
            return string.Equals(pattern, code, StringComparison.Ordinal);
        return Regex.IsMatch(code, "^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$", RegexOptions.CultureInvariant);
    }

    private static string WithDomain(string code) => code.Contains(':') ? code : "game:" + code;
}
