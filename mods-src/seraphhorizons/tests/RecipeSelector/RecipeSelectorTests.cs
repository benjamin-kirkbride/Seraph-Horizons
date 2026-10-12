using SeraphHorizons.Mod.RecipeSelector.Core;

namespace SeraphHorizons.Tests.RecipeSelector;

/// <summary>Recipe selector search (RecipeSelector/Core): the creative inventory's search over recipe
/// outputs, ranked as creative ranks it, mapped back to the recipes' own indices, and the grid's layout
/// and hit-testing.</summary>
public class RecipeSelectorTests
{
    // Knapping flint, in the game's order (by output code), with descriptions as the cache holds them.
    static readonly List<SearchEntry> Flint =
    [
        SearchEntry.Of("Flint axe head", RecipeSearch.DescriptionText("Attach to a stick to make an axe.")),
        SearchEntry.Of("Flint arrowhead", RecipeSearch.DescriptionText("For arrows.")),
        SearchEntry.Of("Flint hoe head", ""),
        SearchEntry.Of("Flint knife blade", RecipeSearch.DescriptionText("A sharp edge (cuts grass).")),
        SearchEntry.Of("Flint shovel head", ""),
        SearchEntry.Of("Flint spear head", ""),
        SearchEntry.Of("Axe", ""),
    ];

    // ---- search friendly text ----

    [Fact]
    public void Friendly_text_drops_accents_and_spaces_out_quotes_and_brackets()
    {
        Assert.Equal("Creme brulee", RecipeSearch.Friendly("Crème brûlée"));
        Assert.Equal(" copper  ingot", RecipeSearch.Friendly("(copper) ingot"));
        Assert.Equal(" a  ", RecipeSearch.Friendly("«a»\""));
        Assert.Equal("", RecipeSearch.Friendly(null));
    }

    [Fact]
    public void The_query_is_friendly_and_lower_case()
    {
        Assert.Equal("creme", RecipeSearch.Query("CRÈME"));
    }

    [Fact]
    public void An_entry_keeps_the_shown_name_in_its_text_and_a_friendly_lower_name()
    {
        var e = SearchEntry.Of("Crème Pot", "for cream");
        Assert.Equal("creme pot", e.Name);
        Assert.Equal("Crème Pot for cream", e.Text);
    }

    // ---- creative's ranking ----

    [Theory]
    [InlineData("flint axe head", "flint axe head", 0f)]      // the whole name
    [InlineData("flint axe head", "flint", 0.125f)]           // its first word
    [InlineData("flint axe head", "head", 0.25f)]             // its last word
    [InlineData("flint axe head", "axe", 0.5f)]               // another word's start
    [InlineData("flint axe head", "fli", 0.75f)]              // the name's start, inside a word
    [InlineData("flint axe head", "xe", 1f)]                  // anywhere else in the name
    public void Name_matches_rank_as_creative_ranks_them(string name, string query, float weight)
    {
        Assert.Equal(weight, RecipeSearch.Weight(new SearchEntry(name, name), query));
    }

    [Fact]
    public void Text_matches_rank_after_every_name_match()
    {
        var e = new SearchEntry("flint knife blade", "Flint knife blade a sharp edge cuts grass");
        Assert.Equal(2f, RecipeSearch.Weight(e with { Text = "sharp things" }, "sharp"));
        Assert.Equal(3f, RecipeSearch.Weight(e, "sharp"));
        Assert.Null(RecipeSearch.Weight(e, "copper"));
    }

    // ---- filtering and the recipes' indices ----

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void An_empty_search_shows_every_recipe_in_the_games_order(string? text)
    {
        Assert.Equal([0, 1, 2, 3, 4, 5, 6], RecipeSearch.Filter(Flint, text));
    }

    [Fact]
    public void A_search_shows_the_matches_best_first_by_their_own_index()
    {
        // "axe": Axe whole name (6), flint axe head word start (0), then the description of the axe head
        // already matched; nothing else holds "axe".
        Assert.Equal([6, 0], RecipeSearch.Filter(Flint, "axe"));
        // "head": the last word of four names, in the game's order, then inside "arrowhead".
        Assert.Equal([0, 2, 4, 5, 1], RecipeSearch.Filter(Flint, "HEAD"));
    }

    [Fact]
    public void Descriptions_find_recipes_their_names_do_not()
    {
        Assert.Equal([3], RecipeSearch.Filter(Flint, "grass"));
        // Parentheses in a description are spaces, so the word is found whole.
        Assert.Equal([3], RecipeSearch.Filter(Flint, "cuts grass"));
        Assert.Equal([1], RecipeSearch.Filter(Flint, "arrows"));
    }

    [Fact]
    public void Equal_ranks_keep_the_games_order()
    {
        // Every name starts with "flint " except the axe: all 0.125, in order.
        Assert.Equal([0, 1, 2, 3, 4, 5], RecipeSearch.Filter(Flint, "flint"));
    }

    [Fact]
    public void A_search_nothing_holds_shows_nothing()
    {
        Assert.Empty(RecipeSearch.Filter(Flint, "copper"));
    }

    [Fact]
    public void Accents_in_the_search_or_the_name_still_match()
    {
        List<SearchEntry> entries = [SearchEntry.Of("Crème jug", ""), SearchEntry.Of("Clay jug", "")];
        Assert.Equal([0], RecipeSearch.Filter(entries, "creme"));
        Assert.Equal([0], RecipeSearch.Filter(entries, "CRÈME"));
    }

    [Fact]
    public void Each_shown_slot_selects_its_own_recipe()
    {
        // The selection callback takes the recipe's index: slot i of the filtered grid is recipe shown[i].
        var shown = RecipeSearch.Filter(Flint, "spear");
        Assert.Equal([5], shown);
        Assert.Equal("flint spear head", Flint[shown[SelectorLayout.SlotAt(10, 10, 51, 7, shown.Count)]].Name);
    }

    // ---- layout ----

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(5, 5)]
    [InlineData(7, 7)]
    [InlineData(80, 7)]
    public void Columns_are_the_games(int count, int cols) => Assert.Equal(cols, SelectorLayout.Columns(count));

    [Theory]
    [InlineData(0, 7, 0, 1, false)]
    [InlineData(7, 7, 1, 1, false)]
    [InlineData(42, 7, 6, 6, false)]
    [InlineData(43, 7, 7, 6, true)]
    [InlineData(100, 7, 15, 6, true)]
    public void Rows_and_the_viewport(int count, int cols, int rows, int visible, bool scrolls)
    {
        Assert.Equal(rows, SelectorLayout.Rows(count, cols));
        Assert.Equal(visible, SelectorLayout.VisibleRows(count, cols));
        Assert.Equal(scrolls, SelectorLayout.Scrolls(count, cols));
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(50.9, 0, 0)]
    [InlineData(51, 0, 1)]
    [InlineData(0, 51, 7)]
    [InlineData(51 * 6 + 1, 51 * 2 + 1, 20)]
    [InlineData(51 * 7 + 1, 0, -1)]   // right of the last column
    [InlineData(-1, 0, -1)]
    [InlineData(0, -1, -1)]
    [InlineData(51 * 1 + 1, 51 * 3 + 1, -1)] // slot 22, past the 21 recipes
    public void Slots_are_hit_as_the_games_grid_hits_them(double x, double y, int slot)
    {
        Assert.Equal(slot, SelectorLayout.SlotAt(x, y, 51, 7, 21));
    }
}
