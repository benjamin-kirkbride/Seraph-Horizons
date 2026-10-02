using SeraphHorizons.TidyVariants.Core;

namespace SeraphHorizons.TidyVariants.Tests;

/// <summary>
/// <see cref="TitleDeriver"/>: group titles from member names. Fixtures are real English names from the pack's
/// largest untitled groups (game 1.22.7; trimmed), plus made-up French/German names for the language rules.
/// </summary>
public class TitleDeriverTests
{
    static string? Derive(params string[] names) => TitleDeriver.Derive(names, names[0]);

    // ---- the shared part ----

    [Fact]
    public void Varying_material_before_the_noun_is_dropped()
    {
        Assert.Equal("Gravel", Derive("Andesite gravel", "Chalk gravel", "Chert gravel", "Conglomerate gravel", "Granite gravel"));
        Assert.Equal("Cobblestone slab", Derive("Andesite cobblestone slab", "Chalk cobblestone slab", "Basalt cobblestone slab"));
        Assert.Equal("Axe", Derive("Copper axe", "Scrap axe", "Tin bronze axe", "Bismuth bronze axe", "Black bronze axe"));
    }

    [Fact]
    public void Varying_word_in_the_middle_is_dropped()
    {
        Assert.Equal("Fanned cobblestone", Derive("Fanned granite cobblestone", "Fanned andesite cobblestone", "Fanned basalt cobblestone"));
        Assert.Equal("Residue-covered metal pot", Derive("Residue-covered Copper metal pot", "Residue-covered Brass metal pot", "Residue-covered Tin bronze metal pot"));
        Assert.Equal("Rock slab (Polished)", Derive("White marble rock slab (Polished)", "Green marble rock slab (Polished)", "Suevite rock slab (Polished)"));
    }

    [Fact]
    public void Brackets_go_unless_every_word_in_them_is_shared()
    {
        Assert.Equal("Imprinted Spore Paper", Derive("Imprinted Spore Paper (Bearded Tooth)", "Imprinted Spore Paper (Chicken of the Woods)", "Imprinted Spore Paper (Dryad's Saddle)"));
        Assert.Equal("Immersive Wire (512V)", Derive("Copper Immersive Wire (512V)", "Silver Immersive Wire (512V)", "Lead Immersive Wire (512V)"));
        // "(Gold, Plain cloth)" ... share "cloth" only: the whole bracket goes, never "( cloth)".
        Assert.Equal("Luxury gondola seat", Derive("Luxury gondola seat (Gold, Plain cloth)", "Luxury gondola seat (Silver, Plain cloth)",
            "Luxury gondola seat (Gold, Mordanted cloth)", "Luxury gondola seat (Silver, Black cloth)"));
    }

    [Fact]
    public void Separators_left_at_an_edge_go()
    {
        string[] names = ["Frame: Wide Cart (birch)", "Unfinished: Wide Cart (birch)", "Frame: Wide Cart (oak)", "Unfinished: Wide Cart (oak)"];
        Assert.Equal("Wide Cart", Derive(names));
        Assert.Equal("Small Electric Lamp", Derive("Small Electric Lamp: orange-warm (3000K)", "Small Electric Lamp: warm white (5000K)", "Small Electric Lamp: cool white (8000K)"));
        Assert.Equal("Sword", Derive("Sword - copper", "Sword - iron", "Sword - steel"));
    }

    [Fact]
    public void Connectives_left_dangling_go_but_not_those_that_start_the_name()
    {
        Assert.Equal("Snowball", Derive("Granite stone in snowball", "Andesite stone in snowball", "Basalt stone in snowball", "Snowball", "Chalk in snowball", "Chert in snowball"));
        Assert.Equal("Die Cast", Derive("Die Cast (LV Gauge)", "Die Cast (MV Gauge)", "Die Cast (Metal Bar)"));
        Assert.Equal("Walking stick", Derive("Walking stick with copper lantern", "Walking stick with iron lantern", "Walking stick with cow skull"));
    }

    [Fact]
    public void Case_is_ignored_and_the_first_letter_upper_cased()
    {
        Assert.Equal("Cart Lantern", Derive("Cart Lantern (copper)", "Old Cart Lantern (copper)", "extinguished Cart Lantern (copper)", "Cart Lantern (tinbronze)"));
        Assert.Equal("Stone", Derive("Andesite stone", "Chalk Stone", "Basalt stone"));
        Assert.Equal("3-tall door", Derive("3-tall door (Aged)", "3-tall door (Birch)", "3-tall door (Oak)"));
        Assert.Equal("Ölfass", Derive("ölfass (Eiche)", "ölfass (Birke)"));
    }

    [Fact]
    public void Nearly_all_is_enough_and_the_order_comes_from_a_name_with_every_kept_word()
    {
        // 9 of 10 names end in "ingot"; the representative "Blister steel" has none, so another name gives the order.
        string[] names = ["Copper ingot", "Tin ingot", "Zinc ingot", "Bismuth ingot", "Gold ingot", "Silver ingot", "Lead ingot", "Iron ingot", "Steel ingot", "Blister steel"];
        Assert.Equal("Ingot", TitleDeriver.Derive(names, "Blister steel"));
        // The noun may sit anywhere: before a bracket, after a varying adjective.
        Assert.Equal("Ruler", Derive("Aged ruler", "Rotten ruler", "Ruler (Brass)", "Ruler (Copper)"));
    }

    [Fact]
    public void Identical_and_duplicate_names_give_that_name()
    {
        Assert.Equal("Large potion flask", Derive("Large potion flask", "Large potion flask", "Large potion flask"));
        Assert.Equal("Seed Shelf", Derive("Seed Shelf ", "Seed Shelf (Birch)", "Seed Shelf (Oak)", "Seed Shelf (Birch)"));
        Assert.Equal("Gearbox", Derive("Gearbox (1:5)", "Gearbox (1:5)", "Gearbox (1:4)"));
    }

    // ---- no good title: null, the caller uses the representative's name ----

    [Fact]
    public void A_shared_modifier_of_a_varying_noun_is_no_title()
    {
        Assert.Null(Derive("Dead clownfish", "Dead haddock (adult)", "Dead carp (adult)", "Dead walleye (adult)"));
        Assert.Null(Derive("Dead Acmon Blue (female)", "Dead Acmon Blue (male)", "Dead Aega Morpho (blue female)"));
        Assert.Null(Derive("Tuning Cylinder Rack ", "Tuning Cylinder Rack (Oak)", "Tuning Cylinder Stand ", "Tuning Cylinder Stand (Oak)"));
        Assert.Null(Derive("Andesite ashlar blocks", "Amphibolite ashlar bricks", "Chalk ashlar blocks", "Diorite ashlar bricks"));
    }

    [Fact]
    public void Nothing_shared_or_only_connectives_brackets_or_digits_is_no_title()
    {
        Assert.Null(Derive("Fly agaric", "Field mushroom", "Bitter bolete", "Chanterelle"));
        Assert.Null(Derive("Blueberry", "Cranberry", "Redcurrant"));
        Assert.Null(Derive("Chicken of the woods", "Jack of the lantern", "Death cap"));
        Assert.Null(Derive("Bauxite (red)", "Granite (red)"));
        Assert.Null(Derive("Plank 1", "Board 1", "Slab 1"));
    }

    [Fact]
    public void Empty_input_and_untranslated_codes_are_ignored()
    {
        Assert.Null(TitleDeriver.Derive([], null));
        Assert.Null(TitleDeriver.Derive([null, "", "  "], null));
        Assert.Equal("Sheet metal", TitleDeriver.Derive(["Copper sheet metal", "vinteng:block-vesheetmetal-temporalsteel", "Iron sheet metal"], "Copper sheet metal"));
        Assert.Null(TitleDeriver.Derive(["vinteng:block-mbpowerconnector-lvpower-up", "vinteng:block-mbpowerconnector-hvpower-up"], null));
    }

    // ---- languages ----

    [Fact]
    public void Head_first_languages_mirror_the_modifier_rule()
    {
        // French: the noun comes first, the varying material after it.
        string[] gravel = ["Gravier d'andésite", "Gravier de granite", "Gravier de basalte"];
        Assert.Equal("Gravier", TitleDeriver.Derive(gravel, gravel[0], headLast: false));
        // "Saumon cru", "Bar cru": a shared adjective after a varying noun.
        Assert.Null(TitleDeriver.Derive(["Saumon cru", "Bar cru", "Carpe crue", "Carpe cru"], "Saumon cru", headLast: false));
        // Head-last, a varying word right after the shared one means the shared one is a modifier.
        Assert.Null(TitleDeriver.Derive(["Poisson mort", "Poisson cru"], "Poisson mort", headLast: true));
    }

    [Fact]
    public void German_compounds_and_connectives()
    {
        string[] names = ["Granitkies", "Basaltkies"];
        Assert.Null(TitleDeriver.Derive(names, names[0]));   // compounds share no whole word: fall back
        Assert.Equal("Fass", TitleDeriver.Derive(["Fass aus Eiche", "Fass aus Birke", "Fass aus Kiefer"], "Fass aus Eiche"));
    }

    [Theory]
    [InlineData("en", true)]
    [InlineData("de", true)]
    [InlineData("ru", true)]
    [InlineData("fr", false)]
    [InlineData("pt-br", false)]
    [InlineData("es-es", false)]
    [InlineData("", true)]
    [InlineData(null, true)]
    public void Head_last_by_locale(string? locale, bool expected) => Assert.Equal(expected, TitleDeriver.IsHeadLast(locale));

    [Fact]
    public void Largest_group_is_cheap_and_deterministic()
    {
        var names = new List<string>();
        string[] rocks = ["Andesite", "Chalk", "Chert", "Granite", "Basalt", "Peridotite", "Suevite", "Shale", "Slate", "Phyllite"];
        for (int i = 0; i < 1000; i++) names.Add($"{rocks[i % rocks.Length]} variant{i} gravel (loose)");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        string? a = TitleDeriver.Derive(names, names[0]);
        sw.Stop();
        Assert.Equal("Gravel (loose)", a);
        Assert.Equal(a, TitleDeriver.Derive(names, names[0]));
        Assert.True(sw.ElapsedMilliseconds < 200, $"{sw.ElapsedMilliseconds} ms");
    }
}
