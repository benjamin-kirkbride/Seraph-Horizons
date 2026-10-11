using SeraphHorizons.Mod.CrucibleFurnace.Core;
using SeraphHorizons.Mod.Machines.Core;
using Xunit;

namespace SeraphHorizons.Tests.CrucibleFurnace;

/// <summary>CrucibleFurnace/Core: the stainless ratio, charging a pot (what goes in, how many, why
/// not), matching a charge to a recipe and what it makes, the hole's heat model, the row and its
/// chimney, the pulled pot's pour window and the settings.</summary>
public class CrucibleFurnaceTests
{
    private static readonly PotRecipes Book = PotRecipes.Default;
    private const int Cap = 300;

    private static List<ChargeItem> Charge(params (string Code, int Count)[] items) =>
        items.Select(i => new ChargeItem(i.Code, i.Count)).ToList();

    // ---- The stainless ratio (shared with the Bessemer route) ----

    [Theory]
    [InlineData(160, 40, true)]    // 4 : 1
    [InlineData(164, 36, true)]    // 18 %
    [InlineData(156, 44, true)]    // 22 %
    [InlineData(170, 30, false)]   // 15 %
    [InlineData(150, 50, false)]   // 25 %
    [InlineData(200, 0, false)]
    [InlineData(0, 40, false)]
    public void Stainless_wants_about_four_iron_to_one_ferrochrome(int iron, int ferrochrome, bool met) =>
        Assert.Equal(met, Stainless.RatioMet(iron, ferrochrome));

    [Fact]
    public void The_stainless_recipe_is_built_from_the_shared_ratio()
    {
        var fe = PotRecipes.StainlessRecipe.Ingredients.Single(i => i.Name == "iron");
        var cr = PotRecipes.StainlessRecipe.Ingredients.Single(i => i.Name == "ferrochrome");
        Assert.Equal(Stainless.FerrochromeMin, cr.Min);
        Assert.Equal(Stainless.FerrochromeMax, cr.Max);
        Assert.Equal(1 - Stainless.FerrochromeMax, fe.Min, 9);
        Assert.Equal(1 - Stainless.FerrochromeMin, fe.Max, 9);
        Assert.Equal(Stainless.Ferrochrome, cr.Codes.Single());
    }

    // ---- Units ----

    [Theory]
    [InlineData("game:ingot-iron", 100)]
    [InlineData("game:ingot-stainlesssteel", 100)]
    [InlineData("game:metalbit-iron", 5)]
    [InlineData("seraphhorizons:ferrochrome", 5)]
    [InlineData("game:crushed-quartz", 5)]
    public void An_ingot_is_100_units_anything_else_5(string code, int units) =>
        Assert.Equal(units, PotRecipes.UnitsOf(code));

    // ---- Charging ----

    [Fact]
    public void Something_no_recipe_takes_is_refused()
    {
        Assert.Equal(new ChargeAdd(0, ChargeRefusal.NotAnIngredient), Book.CanAdd([], "game:ingot-copper", 4, Cap));
        Assert.Equal(new ChargeAdd(0, ChargeRefusal.NotAnIngredient), Book.CanAdd([], "game:ingot-steel", 1, Cap));
    }

    [Fact]
    public void Iron_with_ferrochrome_goes_in_up_to_its_largest_share_of_a_full_pot()
    {
        // 82 % of 300 is 246: after ferrochrome, two ingots fit, then 9 bits (245) and no more; or 49 bits alone
        var fecr = Charge(("seraphhorizons:ferrochrome", 4));
        Assert.Equal(new ChargeAdd(2, ChargeRefusal.None), Book.CanAdd(fecr, "game:ingot-iron", 4, Cap));
        Assert.Equal(new ChargeAdd(0, ChargeRefusal.TooMuch),
            Book.CanAdd([.. fecr, new("game:ingot-iron", 2), new("game:metalbit-iron", 9)], "game:metalbit-iron", 1, Cap));
        Assert.Equal(new ChargeAdd(49, ChargeRefusal.None), Book.CanAdd(fecr, "game:metalbit-iron", 64, Cap));
        Assert.Equal(new ChargeAdd(9, ChargeRefusal.None), Book.CanAdd([.. fecr, new("game:ingot-iron", 2)], "game:metalbit-iron", 64, Cap));
    }

    [Fact]
    public void Iron_alone_fills_the_pot_for_the_iron_melt()
    {
        // the iron melt takes iron to the brim: three ingots, or 60 bits
        Assert.Equal(new ChargeAdd(3, ChargeRefusal.None), Book.CanAdd([], "game:ingot-iron", 4, Cap));
        Assert.Equal(new ChargeAdd(60, ChargeRefusal.None), Book.CanAdd([], "game:metalbit-iron", 64, Cap));
        Assert.Equal(new ChargeAdd(0, ChargeRefusal.PotFull), Book.CanAdd(Charge(("game:ingot-iron", 3)), "game:metalbit-iron", 1, Cap));
        // a full pot of iron takes no ferrochrome; 240 iron does, up to 60
        Assert.Equal(new ChargeAdd(0, ChargeRefusal.PotFull), Book.CanAdd(Charge(("game:ingot-iron", 3)), "seraphhorizons:ferrochrome", 1, Cap));
        Assert.Equal(new ChargeAdd(12, ChargeRefusal.None), Book.CanAdd(Charge(("game:ingot-iron", 2), ("game:metalbit-iron", 8)), "seraphhorizons:ferrochrome", 64, Cap));
        // steel and meteoric iron go in no recipe
        Assert.Equal(new ChargeAdd(0, ChargeRefusal.NotAnIngredient), Book.CanAdd([], "game:metalbit-steel", 4, Cap));
        Assert.Equal(new ChargeAdd(0, ChargeRefusal.NotAnIngredient), Book.CanAdd([], "game:ingot-meteoriciron", 1, Cap));
    }

    [Fact]
    public void The_iron_melt_is_iron_alone_at_1538_unit_for_unit()
    {
        var r = PotRecipes.IronRecipe;
        Assert.Equal(1538, r.Temperature);
        Assert.True(r.Pourable);
        Assert.Equal("game:ingot-iron", r.Output);
        Assert.Equal(1, r.Yield);
        var iron = Assert.Single(r.Ingredients);
        Assert.Equal((1.0, 1.0), (iron.Min, iron.Max));
        Assert.Equal(new[] { "game:ingot-iron", "game:metalbit-iron" }, iron.Codes);

        var full = Book.Match(Charge(("game:ingot-iron", 3)));
        Assert.Equal("iron", full.Recipe?.Code);
        Assert.Equal(new PotProducts(300, 0, 0), PotRecipes.Products(full.Recipe!, 300));
        Assert.Equal("iron", Book.Match(Charge(("game:ingot-iron", 1), ("game:metalbit-iron", 7))).Recipe?.Code);
        // with ferrochrome in it, it is stainless or nothing
        Assert.Equal("stainless", Book.Match(Charge(("game:ingot-iron", 2), ("game:metalbit-iron", 8), ("seraphhorizons:ferrochrome", 12))).Recipe?.Code);
        Assert.Null(Book.Match(Charge(("game:ingot-iron", 2), ("seraphhorizons:ferrochrome", 1))).Recipe);
        // iron bits with quartz are ferrosilicon's, never the iron melt
        Assert.NotEqual("iron", Book.Match(Charge(("game:crushed-quartz", 30), ("game:metalbit-iron", 18))).Closest?.Code);
    }

    [Fact]
    public void Ingredients_of_different_recipes_do_not_mix()
    {
        // quartz is ferrosilicon's, ferrochrome stainless's
        Assert.Equal(new ChargeAdd(0, ChargeRefusal.DoesNotMix), Book.CanAdd(Charge(("game:crushed-quartz", 4)), "seraphhorizons:ferrochrome", 4, Cap));
        Assert.Equal(new ChargeAdd(0, ChargeRefusal.DoesNotMix), Book.CanAdd(Charge(("game:metalbit-stainlesssteel", 4)), "game:ingot-iron", 1, Cap));
        // iron bits go with either, so they go in after ferrochrome or after quartz
        Assert.Equal(49, Book.CanAdd(Charge(("seraphhorizons:ferrochrome", 8)), "game:metalbit-iron", 64, Cap).Count);
        Assert.Equal(21, Book.CanAdd(Charge(("game:crushed-quartz", 30)), "game:metalbit-iron", 64, Cap).Count);
    }

    [Fact]
    public void A_full_pot_takes_nothing_more()
    {
        var full = Charge(("game:metalbit-stainlesssteel", 60));
        Assert.Equal(new ChargeAdd(0, ChargeRefusal.PotFull), Book.CanAdd(full, "game:metalbit-stainlesssteel", 1, Cap));
        Assert.Equal(new ChargeAdd(40, ChargeRefusal.None), Book.CanAdd(Charge(("game:metalbit-stainlesssteel", 20)), "game:metalbit-stainlesssteel", 64, Cap));
    }

    // ---- Matching ----

    [Fact]
    public void The_full_pot_charges_make_their_products()
    {
        // a full pot is 300 units: the ferroalloys give 150 units of lumps
        var fesi = Book.Match(Charge(("game:crushed-quartz", 30), ("game:metalbit-iron", 18), ("game:coke", 12)));
        Assert.Equal("ferrosilicon", fesi.Recipe?.Code);
        Assert.Equal(new PotProducts(150, 30, 0), PotRecipes.Products(fesi.Recipe!, Cap));

        var fecr = Book.Match(Charge(("game:crushed-chromite", 30), ("seraphhorizons:ferrosilicon", 18), ("game:lime", 12)));
        Assert.Equal("ferrochrome", fecr.Recipe?.Code);
        Assert.Equal(new PotProducts(150, 30, 12), PotRecipes.Products(fecr.Recipe!, Cap));
        Assert.Equal("smex:slag", fecr.Recipe!.Byproduct);

        // 240 iron and 60 ferrochrome
        var ss = Book.Match(Charge(("game:ingot-iron", 2), ("game:metalbit-iron", 8), ("seraphhorizons:ferrochrome", 12)));
        Assert.Equal("stainless", ss.Recipe?.Code);
        Assert.Equal(new PotProducts(300, 0, 0), PotRecipes.Products(ss.Recipe!, Cap));
        Assert.True(ss.Recipe!.Pourable);
        Assert.Equal(Stainless.Ingot, ss.Recipe.Output);

        var remelt = Book.Match(Charge(("game:metalbit-stainlesssteel", 20)));
        Assert.Equal("stainlessremelt", remelt.Recipe?.Code);
        Assert.Equal(100, PotRecipes.Products(remelt.Recipe!, 100).Units);   // 20 bits = 100 units
    }

    [Fact]
    public void A_wrong_ratio_makes_nothing_and_names_the_closest_recipe()
    {
        var m = Book.Match(Charge(("game:ingot-iron", 1), ("seraphhorizons:ferrochrome", 2)));   // 9 %
        Assert.Null(m.Recipe);
        Assert.Equal("stainless", m.Closest?.Code);
        var cr = m.Shares.Single(s => s.Ingredient.Name == "ferrochrome");
        Assert.Equal(10.0 / 110, cr.Share, 6);
    }

    [Fact]
    public void A_ferroalloy_charge_too_small_for_one_lump_makes_nothing()
    {
        var m = Book.Match(Charge(("game:crushed-quartz", 1), ("game:metalbit-iron", 1)));
        Assert.Null(m.Recipe);
        var tiny = new PotRecipes([PotRecipes.FerrosiliconRecipe with { Ingredients = [new("q", ["game:crushed-quartz"], 0, 1)] }]);
        Assert.True(tiny.Match(Charge(("game:crushed-quartz", 1))).TooSmall);
        Assert.Equal("ferrosilicon", tiny.Match(Charge(("game:crushed-quartz", 2))).Recipe?.Code);
    }

    [Fact]
    public void An_empty_charge_matches_nothing() => Assert.Same(PotMatch.Empty, Book.Match([]));

    [Theory]
    [InlineData("game:clay-*", "game:clay-fire", true)]
    [InlineData("game:clay-*", "clay-blue", true)]
    [InlineData("smex:smokestack-*", "smex:smokestack-intake", true)]
    [InlineData("game:clay-*", "em:clay-fire", false)]
    [InlineData("game:coke", "game:coke", true)]
    public void Codes_match_with_wildcards_and_the_game_domain(string pattern, string code, bool matches) =>
        Assert.Equal(matches, PotRecipes.CodeMatches(pattern, code));

    // ---- Heat ----

    private static readonly CrucibleFurnaceConfig C = new();

    [Fact]
    public void Forced_air_melts_stainless_in_about_four_hours_the_chimney_in_about_eight()
    {
        double forced = FurnaceHeat.HoursToReach(20, 1530, Draft.Forced, C);
        double chimney = FurnaceHeat.HoursToReach(20, 1530, Draft.Chimney, C);
        Assert.InRange(forced, 3.5, 4.5);
        Assert.InRange(chimney, 6, 8);
        Assert.Equal(double.PositiveInfinity, FurnaceHeat.HoursToReach(20, 1530, Draft.None, C));
        Assert.Equal(double.PositiveInfinity, FurnaceHeat.HoursToReach(20, 1620, Draft.Chimney, C));
        // a full load of coke lasts the forced firing, the chimney's needs a top-up
        Assert.True(C.CokeCapacity * C.HoursPerCoke > forced);
        Assert.True(C.CokeCapacity * C.HoursPerCoke < chimney);
    }

    [Fact]
    public void A_lit_hole_heats_under_its_lid_and_burns_its_coke()
    {
        var s = FurnaceHeat.Advance(new HoleHeat(20, 6, true), 2, Draft.Chimney, lidClosed: true, C);
        Assert.Equal(420, s.Temperature, 6);
        Assert.Equal(4, s.Coke, 6);
        Assert.True(s.Lit);
        // capped at the draft's highest
        s = FurnaceHeat.Advance(new HoleHeat(1550, 6, true), 1, Draft.Chimney, true, C);
        Assert.Equal(1600, s.Temperature, 6);
        // above it (forced air stopped), it falls back to it
        s = FurnaceHeat.Advance(new HoleHeat(1650, 6, true), 1, Draft.Chimney, true, C);
        Assert.Equal(1600, s.Temperature, 6);
    }

    [Fact]
    public void Out_of_coke_it_goes_out_and_cools()
    {
        var s = FurnaceHeat.Advance(new HoleHeat(1000, 1, true), 2, Draft.Forced, true, C);
        Assert.False(s.Lit);
        Assert.Equal(0, s.Coke);
        Assert.Equal(1000 + 400 - 300, s.Temperature, 6);   // an hour's heat, then an hour's cooling
    }

    [Fact]
    public void Open_lid_or_no_draft_it_cools_and_still_burns()
    {
        var open = FurnaceHeat.Advance(new HoleHeat(1000, 4, true), 1, Draft.Forced, lidClosed: false, C);
        Assert.Equal(400, open.Temperature, 6);
        Assert.Equal(3, open.Coke, 6);
        var none = FurnaceHeat.Advance(new HoleHeat(1000, 4, true), 1, Draft.None, lidClosed: true, C);
        Assert.Equal(700, none.Temperature, 6);
        var cold = FurnaceHeat.Advance(new HoleHeat(100, 0, false), 10, Draft.Chimney, true, C);
        Assert.Equal(FurnaceHeat.Ambient, cold.Temperature);
    }

    [Fact]
    public void Many_short_steps_are_one_long_one()
    {
        var one = FurnaceHeat.Advance(new HoleHeat(20, 5.5, true), 8, Draft.Chimney, true, C);
        var many = new HoleHeat(20, 5.5, true);
        for (int i = 0; i < 800; i++)
            many = FurnaceHeat.Advance(many, 0.01, Draft.Chimney, true, C);
        Assert.Equal(one.Temperature, many.Temperature, 3);
        Assert.Equal(one.Coke, many.Coke, 6);
        Assert.Equal(one.Lit, many.Lit);
    }

    // ---- The row ----

    private static Func<Int3, FurnaceCell> World(IEnumerable<Int3> holes, IEnumerable<Int3> chimney)
    {
        var h = holes.ToHashSet();
        var c = chimney.ToHashSet();
        return p => h.Contains(p) ? FurnaceCell.Hole : c.Contains(p) ? FurnaceCell.Chimney : FurnaceCell.Other;
    }

    private static IEnumerable<Int3> Column(int x, int z, int height) => Enumerable.Range(0, height).Select(y => new Int3(x, y, z));

    [Fact]
    public void Four_holes_with_a_six_block_chimney_at_one_end_work()
    {
        var holes = Enumerable.Range(0, 4).Select(x => new Int3(x, 0, 0)).ToList();
        var at = World(holes, Column(4, 0, 6));
        foreach (var hole in holes)
        {
            var row = FurnaceRow.Find(hole, at, 4, 6);
            Assert.True(row.Works);
            Assert.Equal(holes, row.Holes);
            Assert.Equal(new Int3(4, 0, 0), row.Chimney);
            Assert.Equal(6, row.ChimneyHeight);
        }
        // the chimney at the other end, and along z
        Assert.True(FurnaceRow.Find(new Int3(0, 0, 0), World(holes, Column(-1, 0, 7)), 4, 6).Works);
        Assert.True(FurnaceRow.Find(new Int3(0, 0, 2), World([new(0, 0, 2), new(0, 0, 3)], Column(0, 1, 6)), 4, 6).Works);
    }

    [Fact]
    public void A_short_chimney_too_many_holes_or_none_say_so()
    {
        var holes = Enumerable.Range(0, 4).Select(x => new Int3(x, 0, 0)).ToList();
        var shortRow = FurnaceRow.Find(new Int3(1, 0, 0), World(holes, Column(4, 0, 5)), 4, 6);
        Assert.Equal(RowProblem.ChimneyTooShort, shortRow.Problem);
        Assert.Equal(5, shortRow.ChimneyHeight);

        var five = Enumerable.Range(0, 5).Select(x => new Int3(x, 0, 0)).ToList();
        Assert.Equal(RowProblem.TooManyHoles, FurnaceRow.Find(new Int3(2, 0, 0), World(five, Column(5, 0, 6)), 4, 6).Problem);

        Assert.Equal(RowProblem.NoChimney, FurnaceRow.Find(new Int3(0, 0, 0), World(holes, []), 4, 6).Problem);
        // a chimney beside the row, not in line with it, does not count
        Assert.Equal(RowProblem.NoChimney, FurnaceRow.Find(new Int3(0, 0, 0), World(holes, Column(1, 1, 6)), 4, 6).Problem);
    }

    // ---- The pour window ----

    [Fact]
    public void A_pot_pulled_at_1600_is_solid_after_the_window()
    {
        double hoursPerSecond = 1.0 / 120;   // the game's default: an hour is two real minutes
        double speed = PourWindow.CooldownPerHour(1600, 1530, 20, hoursPerSecond);
        double solidAt = PourWindow.SolidifiesAt(1530);
        Assert.Equal(1377, solidAt, 6);
        Assert.Equal(solidAt, 1600 - speed * 20 * hoursPerSecond, 6);
        // already solid: the game's own speed
        Assert.Equal(PourWindow.GameCooldownPerHour, PourWindow.CooldownPerHour(1300, 1530, 20, hoursPerSecond));
    }

    // ---- Settings ----

    [Fact]
    public void Defaults_are_sane_and_bad_values_fall_back()
    {
        Assert.Empty(new CrucibleFurnaceConfig().Sanitise());
        var bad = new CrucibleFurnaceConfig { PotCapacityUnits = 0, PourWindowSeconds = double.NaN, HoursPerCoke = -1, ChimneyBlocks = [] };
        var fixes = bad.Sanitise();
        Assert.Equal(4, fixes.Count);
        Assert.Equal(300, bad.PotCapacityUnits);
        Assert.Equal(20, bad.PourWindowSeconds);
        Assert.Equal(1, bad.HoursPerCoke);
        Assert.Contains("game:claybricks-*", bad.ChimneyBlocks);
        var blank = new CrucibleFurnaceConfig { LargeCastingMetals = null!, LargeCastingMolds = ["game:ingotmold-*", " "] };
        Assert.Equal(2, blank.Sanitise().Count);
        Assert.Equal(new[] { "game:ingot-iron" }, blank.LargeCastingMetals);
        Assert.Equal(new[] { "game:ingotmold-*", "seraphhorizons:cylindermold-*" }, blank.LargeCastingMolds);
    }

    // ---- Large castings: where a pot's molten iron pours ----

    [Theory]
    [InlineData("game:ingot-iron", "game:ingotmold-blue-fired", true)]
    [InlineData("game:ingot-iron", "seraphhorizons:cylindermold-fire-fired", true)]
    [InlineData("game:ingot-iron", "game:toolmold-blue-fired-axe", false)]
    [InlineData("game:ingot-iron", "game:toolmold-red-fired-anvil", false)]
    [InlineData("game:ingot-iron", "game:toolmold-tan-fired-helvehammer", false)]
    [InlineData("game:ingot-iron", "seraphhorizons:toolmold-blue-fired-gearblank", false)]
    [InlineData("game:ingot-iron", "seraphhorizons:castpipemold-blue-fired", false)]
    [InlineData("game:ingot-stainlesssteel", "game:toolmold-blue-fired-axe", true)]
    [InlineData("game:ingot-stainlesssteel", "seraphhorizons:toolmold-blue-fired-gearblank", true)]
    [InlineData("game:ingot-steel", "game:toolmold-blue-fired-axe", true)]
    public void Molten_iron_from_a_pot_casts_only_into_large_casting_molds(string metal, string mold, bool allowed) =>
        Assert.Equal(allowed, new CrucibleFurnaceConfig().PourAllowed(metal, mold));

    [Fact]
    public void The_large_casting_lists_are_settings()
    {
        var c = new CrucibleFurnaceConfig { LargeCastingMetals = [], LargeCastingMolds = [] };
        Assert.True(c.PourAllowed("game:ingot-iron", "game:toolmold-blue-fired-axe"));   // no metal restricted
        c = new CrucibleFurnaceConfig { LargeCastingMetals = ["game:ingot-*"], LargeCastingMolds = ["seraphhorizons:cylindermold-*"] };
        Assert.False(c.PourAllowed("game:ingot-stainlesssteel", "game:ingotmold-blue-fired"));
        Assert.True(c.PourAllowed("game:ingot-stainlesssteel", "seraphhorizons:cylindermold-blue-fired"));
        Assert.True(new CrucibleFurnaceConfig().IsLargeCastingMetal("game:ingot-iron"));
        Assert.False(new CrucibleFurnaceConfig().IsLargeCastingMetal("game:ingot-meteoriciron"));
    }
}
