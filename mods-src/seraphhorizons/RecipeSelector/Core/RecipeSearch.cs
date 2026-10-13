using System.Globalization;
using System.Text;

namespace SeraphHorizons.Mod.RecipeSelector.Core;

/// <summary>
/// One recipe's output as the search sees it, built as the creative inventory builds its search cache
/// (the game's <c>CreativeTab.CreateSearchCache</c>, 1.22.7): <see cref="Name"/> is the output's name made
/// search friendly and lower case (the cache's <c>SearchCacheNames</c>), <see cref="Text"/> is the name as
/// shown, a space, and the output's other search text (the cache's <c>SearchCache</c>: the collectible's
/// <c>ISearchTextProvider.GetSearchText</c>, or else its description, search friendly and lower case).
/// </summary>
public sealed record SearchEntry(string Name, string Text)
{
    /// <param name="displayName">The output's name (<c>ItemStack.GetName()</c>).</param>
    /// <param name="searchText">Its other search text, already in the form the game caches it: a
    /// provider's text as returned, or the description through <see cref="RecipeSearch.Friendly"/>
    /// and lower cased (<see cref="RecipeSearch.DescriptionText"/>).</param>
    public static SearchEntry Of(string? displayName, string? searchText)
    {
        string name = displayName ?? "";
        return new SearchEntry(RecipeSearch.Friendly(name).ToLowerInvariant(), name + " " + (searchText ?? ""));
    }
}

/// <summary>
/// The recipe selector's search (README, "Recipe selection has a search and a scrollbar"): the creative
/// inventory's own search, <c>GuiElementItemSlotGridBase.FilterItemsBySearchText</c> (1.22.7), over the
/// recipe outputs, mapped back to the recipes' own indices. Game-independent.
///
/// The text is made search friendly (<see cref="Friendly"/>) and lower cased. Empty, every recipe shows in
/// the game's order. Otherwise a recipe shows if its name or its other text holds the text, ranked as
/// creative ranks it (<see cref="Weight"/>), lower first, ties in the game's order; the rest are hidden.
/// </summary>
public static class RecipeSearch
{
    /// <summary>
    /// The game's <c>StringUtil.ToSearchFriendly</c>: accents and other combining marks dropped
    /// (decomposed, marks removed, recomposed), and guillemets, double quotes and parentheses turned to
    /// spaces. Case is kept.
    /// </summary>
    public static string Friendly(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        string decomposed = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (char c in decomposed)
        {
            if (c is '«' or '»' or '"' or '(' or ')') sb.Append(' ');
            else if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>The search box's text as the search uses it (the game's <c>searchText</c>).</summary>
    public static string Query(string? text) => Friendly(text).ToLowerInvariant();

    /// <summary>A description in the form the creative search cache holds it: search friendly, lower case.</summary>
    public static string DescriptionText(string? description) => Friendly(description).ToLowerInvariant();

    /// <summary>
    /// Creative's rank of one entry for a non-empty <paramref name="query"/> (from <see cref="Query"/>), or
    /// null when it does not match. On the name: the whole name 0; its first word 0.125; its last word 0.25;
    /// another word's start 0.5; the name's start inside a word 0.75; anywhere else in it 1. Then on the
    /// whole text: its start 2, anywhere in it 3. Comparisons ignore case (invariant culture), as the game's do.
    /// </summary>
    public static float? Weight(SearchEntry entry, string query)
    {
        string name = entry.Name;
        int at = name.IndexOf(query, StringComparison.InvariantCultureIgnoreCase);
        if (at == 0 && name.Length == query.Length) return 0f;
        if (at == 0 && name.Length > query.Length && name[query.Length] == ' ') return 0.125f;
        if (at > 0 && name[at - 1] == ' ' && at + query.Length == name.Length) return 0.25f;
        if (at > 0 && name[at - 1] == ' ') return 0.5f;
        if (at == 0) return 0.75f;
        if (at > 0) return 1f;
        if (entry.Text.StartsWith(query, StringComparison.InvariantCultureIgnoreCase)) return 2f;
        if (entry.Text.IndexOf(query, StringComparison.InvariantCultureIgnoreCase) >= 0) return 3f;
        return null;
    }

    /// <summary>
    /// The recipes shown for the box's <paramref name="text"/>, as indices into <paramref name="entries"/>
    /// (the recipes' own order, which the game's selection callback takes) in the order shown.
    /// </summary>
    public static List<int> Filter(IReadOnlyList<SearchEntry> entries, string? text)
    {
        string query = Query(text);
        if (query.Length == 0) return Enumerable.Range(0, entries.Count).ToList();
        var hits = new List<(int Index, float Weight)>();
        for (int i = 0; i < entries.Count; i++)
            if (Weight(entries[i], query) is { } w) hits.Add((i, w));
        // OrderBy is stable, as the game's is: equal weights keep the recipes' order.
        return hits.OrderBy(h => h.Weight).Select(h => h.Index).ToList();
    }
}

/// <summary>
/// The selector's grid: the game's columns (up to 7, fewer for fewer recipes), every row of the whole list
/// up to <see cref="MaxVisibleRows"/>, then a scrollbar. Game-independent.
/// </summary>
public static class SelectorLayout
{
    /// <summary>The game's columns at most (<c>GuiDialogBlockEntityRecipeSelector.SetupDialog</c>).</summary>
    public const int MaxColumns = 7;

    /// <summary>Rows shown before the grid scrolls.</summary>
    public const int MaxVisibleRows = 6;

    /// <summary>The game's columns for the whole list: as many as recipes, 1 to <see cref="MaxColumns"/>.</summary>
    public static int Columns(int count) => Math.Min(Math.Max(1, count), MaxColumns);

    /// <summary>Rows holding <paramref name="count"/> slots (0 for none).</summary>
    public static int Rows(int count, int cols) => count <= 0 ? 0 : (count + Math.Max(1, cols) - 1) / Math.Max(1, cols);

    /// <summary>Rows of the viewport for the whole list: the game's (at least one), at most <see cref="MaxVisibleRows"/>.</summary>
    public static int VisibleRows(int count, int cols) => Math.Min(Math.Max(1, Rows(count, cols)), MaxVisibleRows);

    /// <summary>Whether the whole list has more rows than the viewport shows. A search only hides recipes,
    /// so a list that fits whole fits under every search.</summary>
    public static bool Scrolls(int count, int cols) => Rows(count, cols) > MaxVisibleRows;

    /// <summary>
    /// The slot under a point, as the game's skill item grid hit-tests it: <paramref name="x"/> and
    /// <paramref name="y"/> from the grid's top left (scrolled with it), <paramref name="cell"/> a slot
    /// and its padding; -1 off the slots. The index is into the slots shown.
    /// </summary>
    public static int SlotAt(double x, double y, double cell, int cols, int count)
    {
        if (x < 0 || y < 0 || cell <= 0 || cols <= 0) return -1;
        int col = (int)(x / cell), row = (int)(y / cell);
        if (col >= cols) return -1;
        int index = row * cols + col;
        return index < count ? index : -1;
    }
}
