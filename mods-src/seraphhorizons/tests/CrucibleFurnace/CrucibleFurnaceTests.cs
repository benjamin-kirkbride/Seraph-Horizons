using SeraphHorizons.Mod.CrucibleFurnace.Core;
using SeraphHorizons.Mod.Machines.Core;
using Xunit;

namespace SeraphHorizons.Tests.CrucibleFurnace;

/// <summary>CrucibleFurnace/Core: the stainless ratio, charging a pot (what goes in, how many, why
/// not), matching a charge to a recipe and what it makes, the furnace's heat model (the draught by the
/// chimney's height, the ash-pit damper), the row and its shared chimney, the pulled pot's pour window
/// and the settings.</summary>
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

    // ---- Emptying a held pot ----

    [Theory]
    [InlineData(2, 20, true)]
    [InlineData(1, 300, true)]      // the game's too-hot-to-touch is still holdable
    [InlineData(1, 301, false)]     // hotter: as before, it is not emptied
    [InlineData(0, 20, false)]      // no charge, nothing to empty
    public void A_pot_empties_with_a_charge_and_cool_enough_to_hold(int chargeItems, double temperature, bool emptiable) =>
        Assert.Equal(emptiable, PotEmpty.Emptiable(chargeItems, temperature, 300));

    [Theory]
    [InlineData(30, 64, new[] { 30 })]
    [InlineData(64, 64, new[] { 64 })]
    [InlineData(150, 64, new[] { 64, 64, 22 })]
    [InlineData(3, 1, new[] { 1, 1, 1 })]
    [InlineData(0, 64, new int[0])]
    public void An_emptied_charge_drops_in_full_stacks(int count, int maxStack, int[] stacks) =>
        Assert.Equal(stacks, PotEmpty.Stacks(count, maxStack));

    // ---- Heat ----

    private static readonly CrucibleFurnaceConfig C = new();

    // ---- Draught by the chimney's height ----

    private static Draught D(int height) => FurnaceHeat.DraughtOf(height, C)!.Value;

    [Theory]
    [InlineData(6, 200, 1600)]
    [InlineData(7, 240, 1610)]
    [InlineData(9, 320, 1630)]
    [InlineData(11, 400, 1650)]
    [InlineData(12, 400, 1650)]    // counted up to eleven
    [InlineData(40, 400, 1650)]
    public void The_draught_grows_with_the_chimney_up_to_eleven_blocks(int height, double rate, double max)
    {
        var d = D(height);
        Assert.Equal(rate, d.HeatingPerHour, 6);
        Assert.Equal(max, d.MaxTemperature, 6);
        Assert.Equal(height, d.Height);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(5)]
    public void A_chimney_under_six_blocks_draws_nothing(int height) => Assert.Null(FurnaceHeat.DraughtOf(height, C));

    [Fact]
    public void The_draught_figures_are_settings()
    {
        var c = new CrucibleFurnaceConfig { MinChimneyHeight = 4, MaxChimneyHeight = 5, ChimneyHeatingPerHour = 100, ChimneyHeatingPerExtraBlock = 10,
            ChimneyMaxTemperature = 1500, ChimneyMaxPerExtraBlock = 50 };
        Assert.Null(FurnaceHeat.DraughtOf(3, c));
        Assert.Equal(100, FurnaceHeat.DraughtOf(4, c)!.Value.HeatingPerHour);
        Assert.Equal(110, FurnaceHeat.DraughtOf(9, c)!.Value.HeatingPerHour);
        Assert.Equal(1550, FurnaceHeat.DraughtOf(9, c)!.Value.MaxTemperature);
    }

    [Fact]
    public void Stainless_melts_in_about_eight_hours_on_six_blocks_and_four_on_eleven()
    {
        double low = FurnaceHeat.HoursToReach(20, 1530, D(6), Damper.Open, C);
        double high = FurnaceHeat.HoursToReach(20, 1530, D(11), Damper.Open, C);
        Assert.Equal(7.55, low, 2);
        Assert.Equal(3.775, high, 3);
        Assert.Equal(double.PositiveInfinity, FurnaceHeat.HoursToReach(20, 1530, null, Damper.Open, C));
        Assert.Equal(double.PositiveInfinity, FurnaceHeat.HoursToReach(20, 1620, D(6), Damper.Open, C));
        // one fill of coke lasts the slow firing
        Assert.True(C.CokeCapacity * C.HoursPerCoke > low);
    }

    [Fact]
    public void A_lit_furnace_heats_under_its_lid_and_burns_its_coke()
    {
        var s = FurnaceHeat.Advance(new HoleHeat(20, 6, true), 2, D(6), lidClosed: true, Damper.Open, C);
        Assert.Equal(420, s.Temperature, 6);
        Assert.Equal(4, s.Coke, 6);
        Assert.True(s.Lit);
        // twice as fast on eleven blocks
        Assert.Equal(820, FurnaceHeat.Advance(new HoleHeat(20, 6, true), 2, D(11), true, Damper.Open, C).Temperature, 6);
        // capped at the draught's highest
        s = FurnaceHeat.Advance(new HoleHeat(1550, 6, true), 1, D(6), true, Damper.Open, C);
        Assert.Equal(1600, s.Temperature, 6);
        // above it (the chimney cut down), it falls back to it
        s = FurnaceHeat.Advance(new HoleHeat(1650, 6, true), 1, D(6), true, Damper.Open, C);
        Assert.Equal(1600, s.Temperature, 6);
    }

    [Fact]
    public void Out_of_coke_it_goes_out_and_cools()
    {
        var s = FurnaceHeat.Advance(new HoleHeat(1000, 1, true), 2, D(11), true, Damper.Open, C);
        Assert.False(s.Lit);
        Assert.Equal(0, s.Coke);
        Assert.Equal(1000 + 400 - 300, s.Temperature, 6);   // an hour's heat, then an hour's cooling
    }

    [Fact]
    public void Open_lid_or_no_draught_it_cools_and_still_burns()
    {
        var open = FurnaceHeat.Advance(new HoleHeat(1000, 4, true), 1, D(11), lidClosed: false, Damper.Open, C);
        Assert.Equal(400, open.Temperature, 6);
        Assert.Equal(3, open.Coke, 6);
        var none = FurnaceHeat.Advance(new HoleHeat(1000, 4, true), 1, null, lidClosed: true, Damper.Open, C);
        Assert.Equal(700, none.Temperature, 6);
        var cold = FurnaceHeat.Advance(new HoleHeat(100, 0, false), 10, D(6), true, Damper.Open, C);
        Assert.Equal(FurnaceHeat.Ambient, cold.Temperature);
    }

    // ---- The ash-pit damper ----

    [Fact]
    public void Half_shut_the_damper_halves_the_heating_and_slows_the_coke()
    {
        var s = FurnaceHeat.Advance(new HoleHeat(20, 6, true), 2, D(6), true, Damper.Half, C);
        Assert.Equal(220, s.Temperature, 6);            // 100 °C an hour
        Assert.Equal(6 - 2 * 0.6, s.Coke, 6);           // 0.6 of a coke an hour
        Assert.Equal(1600, FurnaceHeat.Advance(new HoleHeat(1590, 6, true), 1, D(6), true, Damper.Half, C).Temperature, 6);   // the same top
        Assert.Equal(100, FurnaceHeat.HeatingRate(D(6), Damper.Half, C), 6);
        Assert.Equal(200, FurnaceHeat.HeatingRate(D(11), Damper.Half, C), 6);
        Assert.Equal(1 / 0.6, FurnaceHeat.HoursPerCoke(Damper.Half, C), 6);
    }

    [Fact]
    public void Shut_the_fire_is_banked_no_heating_slow_cooling_and_slow_coke()
    {
        var s = FurnaceHeat.Advance(new HoleHeat(1500, 6, true), 4, D(11), true, Damper.Shut, C);
        Assert.Equal(1500 - 4 * 75, s.Temperature, 6);  // a quarter of 300 °C an hour
        Assert.Equal(6 - 4 * 0.25, s.Coke, 6);          // a quarter of a coke an hour
        Assert.True(s.Lit);
        Assert.Equal(0, FurnaceHeat.HeatingRate(D(11), Damper.Shut, C));
        Assert.Equal(double.PositiveInfinity, FurnaceHeat.HoursToReach(20, 100, D(11), Damper.Shut, C));
        // banked, a fill of coke lasts four times as long: 48 hours
        Assert.Equal(48, C.CokeCapacity * FurnaceHeat.HoursPerCoke(Damper.Shut, C), 6);
        // with the lid open, it cools at the open rate whatever the damper
        Assert.Equal(900, FurnaceHeat.Advance(new HoleHeat(1500, 6, true), 1, D(11), false, Damper.Shut, C).Temperature, 6);
        // cold and shut, it cools slowly too (no fire, lid and damper shut)
        Assert.Equal(1425, FurnaceHeat.Advance(new HoleHeat(1500, 0, false), 1, null, true, Damper.Shut, C).Temperature, 6);
    }

    [Fact]
    public void The_damper_figures_are_settings()
    {
        var c = new CrucibleFurnaceConfig { DamperHalfHeating = 0.25, DamperHalfBurn = 0.5, DamperShutBurn = 0.1, DamperShutCooling = 0.5 };
        Assert.Equal(50, FurnaceHeat.HeatingRate(D(6), Damper.Half, c), 6);
        Assert.Equal(2, FurnaceHeat.HoursPerCoke(Damper.Half, c), 6);
        Assert.Equal(10, FurnaceHeat.HoursPerCoke(Damper.Shut, c), 6);
        Assert.Equal(150, FurnaceHeat.CoolingRate(true, Damper.Shut, c), 6);
        Assert.Equal(300, FurnaceHeat.CoolingRate(true, Damper.Half, c), 6);
    }

    [Theory]
    [InlineData(Damper.Open)]
    [InlineData(Damper.Half)]
    [InlineData(Damper.Shut)]
    public void Many_short_steps_are_one_long_one(Damper damper)
    {
        var one = FurnaceHeat.Advance(new HoleHeat(20, 5.5, true), 8, D(8), true, damper, C);
        var many = new HoleHeat(20, 5.5, true);
        for (int i = 0; i < 800; i++)
            many = FurnaceHeat.Advance(many, 0.01, D(8), true, damper, C);
        Assert.Equal(one.Temperature, many.Temperature, 3);
        Assert.Equal(one.Coke, many.Coke, 6);
        Assert.Equal(one.Lit, many.Lit);
    }

    // ---- The row and its chimney ----

    /// <summary>A row: members by offset (capped, chimney height), null between and beyond.</summary>
    private static Func<int, RowMember?> Row(params (int At, bool Capped, int Chimney)[] members)
    {
        var map = members.ToDictionary(m => m.At, m => new RowMember(m.Capped, m.Chimney));
        return k => map.TryGetValue(k, out var m) ? m : null;
    }

    [Fact]
    public void A_lone_furnace_draws_on_its_own_chimney()
    {
        var row = FurnaceRow.Find(Row((0, false, 6)), 6, 6);
        Assert.True(row.Works);
        Assert.Equal((0, 1, 0, 6), (row.First, row.Count, row.ChimneyAt!.Value, row.ChimneyHeight));
        Assert.Equal(RowProblem.ChimneyTooShort, FurnaceRow.Find(Row((0, false, 5)), 6, 6).Problem);
        Assert.Equal(RowProblem.NoChimney, FurnaceRow.Find(Row((0, false, 0)), 6, 6).Problem);
        // capped, its chimney does not count
        Assert.Equal(RowProblem.NoChimney, FurnaceRow.Find(Row((0, true, 8)), 6, 6).Problem);
    }

    [Fact]
    public void A_row_of_three_shares_one_chimney_the_rest_capped()
    {
        var at = Row((-1, true, 0), (0, true, 0), (1, false, 7));
        foreach (int self in new[] { -1, 0, 1 })
        {
            var row = FurnaceRow.Find(k => at(k + self), 6, 6);
            Assert.True(row.Works, $"{self}: {row.Problem}");
            Assert.Equal(3, row.Count);
            Assert.Equal(1 - self, row.ChimneyAt);
            Assert.Equal(7, row.ChimneyHeight);
            Assert.Equal(-1 - self, row.First);
        }
    }

    [Fact]
    public void An_uncapped_outlet_leaks_the_whole_rows_draught_and_is_named()
    {
        var row = FurnaceRow.Find(Row((-1, false, 0), (0, true, 0), (1, false, 7)), 6, 6);
        Assert.False(row.Works);
        Assert.Equal(RowProblem.OutletOpen, row.Problem);
        Assert.Equal(-1, row.OpenOutlet);
        Assert.Equal(1, row.ChimneyAt);
        // a second chimney is an open outlet too: the taller one serves, the other leaks
        row = FurnaceRow.Find(Row((0, false, 6), (1, false, 8)), 6, 6);
        Assert.Equal(RowProblem.OutletOpen, row.Problem);
        Assert.Equal(1, row.ChimneyAt);
        Assert.Equal(0, row.OpenOutlet);
        // a leak is named before a short chimney
        Assert.Equal(RowProblem.OutletOpen, FurnaceRow.Find(Row((0, false, 0), (1, false, 3)), 6, 6).Problem);
    }

    [Fact]
    public void Five_furnaces_share_one_chimney_but_six_are_too_many()
    {
        var five = Enumerable.Range(0, 5).Select(i => (i, i != 4, i == 4 ? 6 : 0)).ToArray();
        var row = FurnaceRow.Find(Row(five), 5, 6);
        Assert.True(row.Works);
        Assert.Equal(5, row.Count);
        var six = Enumerable.Range(0, 6).Select(i => (i, i != 5, i == 5 ? 6 : 0)).ToArray();
        Assert.Equal(RowProblem.TooManyFurnaces, FurnaceRow.Find(Row(six), 5, 6).Problem);
        // from the middle too, scanning both ways
        Assert.Equal(RowProblem.TooManyFurnaces, FurnaceRow.Find(k => Row(six)(k + 3), 5, 6).Problem);
        Assert.Equal(RowProblem.TooManyFurnaces, FurnaceRow.Find(Row(five), 4, 6).Problem);
    }

    [Fact]
    public void A_gap_ends_the_row()
    {
        // a furnace two widths along is not touching: its chimney is not this one's
        var row = FurnaceRow.Find(Row((0, true, 0), (2, false, 6)), 6, 6);
        Assert.Equal(RowProblem.NoChimney, row.Problem);
        Assert.Equal(1, row.Count);
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
        var bad = new CrucibleFurnaceConfig { PotCapacityUnits = 0, PourWindowSeconds = double.NaN, HoursPerCoke = -1, ChimneyBlocks = [],
            DamperHalfBurn = 0, DamperShutCooling = 2, MaxChimneyHeight = 3, MaxFurnacesInRow = 0 };
        var fixes = bad.Sanitise();
        Assert.Equal(8, fixes.Count);
        Assert.Equal(0.6, bad.DamperHalfBurn);
        Assert.Equal(0.25, bad.DamperShutCooling);
        Assert.Equal(11, bad.MaxChimneyHeight);
        Assert.Equal(5, bad.MaxFurnacesInRow);
        Assert.Equal(300, bad.PotCapacityUnits);
        Assert.Equal(20, bad.PourWindowSeconds);
        Assert.Equal(1, bad.HoursPerCoke);
        Assert.Contains("game:claybricks-*", bad.ChimneyBlocks);
        // smex's smoke stack is no chimney block: it cannot stand on the outlet
        Assert.DoesNotContain(new CrucibleFurnaceConfig().ChimneyBlocks, c => c.StartsWith("smex:", StringComparison.Ordinal));
        Assert.True(new CrucibleFurnaceConfig().IsChimneyBlock("game:claybricks-good-fire"));
        Assert.True(new CrucibleFurnaceConfig().IsChimneyBlock("game:stonebricks-granite"));
        Assert.False(new CrucibleFurnaceConfig().IsChimneyBlock("smex:smokestack-bottom"));
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
