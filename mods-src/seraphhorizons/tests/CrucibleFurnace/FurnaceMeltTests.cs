using SeraphHorizons.Mod.CrucibleFurnace.Core;
using Xunit;

namespace SeraphHorizons.Tests.CrucibleFurnace;

/// <summary>CrucibleFurnace/Core/FurnaceMelt: melting takes time (smex's blast-furnace melt curve on the
/// margin above the recipe's temperature), pauses below it, comes out the same over a long gap as over
/// many short steps; charging chills the fire by the items' heat; a molten bath freezes after the hold
/// below its solid point; additions to a molten bath dissolve into the next recipe, or, off every ratio,
/// pour as the bath's own metal.</summary>
public class FurnaceMeltTests
{
    private static readonly PotRecipes Book = PotRecipes.Default;
    private static readonly CrucibleFurnaceConfig C = new();
    private const int Cap = 300;

    private static double? Solid(string recipe) => recipe switch
    {
        "iron" => PourWindow.SolidifiesAt(1538),
        "stainless" or "stainlessremelt" => PourWindow.SolidifiesAt(1530),
        _ => null,
    };

    private static FurnacePot StainlessCharge()
    {
        var pot = new FurnacePot();
        pot.Add(Book, Stainless.Ferrochrome, 12, Cap);
        pot.Add(Book, "game:ingot-iron", 2, Cap);
        pot.Add(Book, "game:metalbit-iron", 8, Cap);
        Assert.Equal(300, pot.ChargeUnits);
        return pot;
    }

    private static FurnacePot MoltenIron(int units) => new() { Melted = "iron", MeltedUnits = units };

    /// <summary>Holds the fire at <paramref name="t"/> for <paramref name="hours"/>, in steps.</summary>
    private static void Hold(FurnacePot pot, double t, double hours)
    {
        for (double left = hours; left > 1e-12; left -= FurnaceMelt.MaxStepHours)
            FurnaceMelt.Step(pot, Book, Math.Min(left, FurnaceMelt.MaxStepHours), t, t, Solid, C);
    }

    // ---- The melt curve ----

    [Theory]
    [InlineData(-1, 0)]        // below: no melting
    [InlineData(0, 0.5)]       // just at it: smex's floor
    [InlineData(70, 0.69)]     // stainless on 6 blocks (1600 °C)
    [InlineData(120, 1.04)]    // stainless on 11 blocks (1650 °C)
    [InlineData(400, 2)]       // smex's ceiling
    public void The_melt_speed_is_smex_blast_furnace_curve_on_the_margin(double margin, double factor) =>
        Assert.Equal(factor, FurnaceMelt.HeatFactor(margin, C), 9);

    [Theory]
    [InlineData(1600, 4.348)]
    [InlineData(1650, 2.885)]
    public void A_full_pot_of_stainless_takes_three_hours_over_the_speed(double fire, double hours)
    {
        var pot = StainlessCharge();
        Assert.Equal(hours, FurnaceMelt.HoursLeft(pot, Book, fire, C)!.Value, 2);
        Hold(pot, fire, hours - 0.02);
        Assert.Null(pot.Melted);
        Hold(pot, fire, 0.04);
        Assert.Equal("stainless", pot.Melted);
        Assert.Equal(300, pot.MeltedUnits);
        Assert.Equal(0, pot.Progress);
    }

    [Fact]
    public void Half_a_pot_takes_half_the_time()
    {
        var pot = new FurnacePot();
        pot.Add(Book, "game:metalbit-stainlesssteel", 30, Cap);
        Assert.Equal(4.348 / 2, FurnaceMelt.HoursLeft(pot, Book, 1600, C)!.Value, 2);
    }

    [Fact]
    public void Below_the_recipe_temperature_the_melt_pauses_and_keeps_its_progress()
    {
        var pot = StainlessCharge();
        Hold(pot, 1600, 1);
        Assert.Equal(69, pot.Progress, 6);
        Assert.Equal(0.23, FurnaceMelt.Share(pot), 6);
        Hold(pot, 1500, 5);
        Assert.Equal(69, pot.Progress, 6);
        Assert.Null(FurnaceMelt.HoursLeft(pot, Book, 1500, C));
        Hold(pot, 1600, 1);
        Assert.Equal(138, pot.Progress, 6);
        // a fire climbing through the temperature in a step melts only for the part above it, at the
        // curve's mean over that part: 1500 to 1560 °C in an hour is half an hour from 0.5 to 0.5
        var climbing = StainlessCharge();
        FurnaceMelt.Step(climbing, Book, 1, 1500, 1560, Solid, C);
        Assert.Equal(0.5 * 0.5 * 100, climbing.Progress, 6);
    }

    [Fact]
    public void A_long_gap_comes_out_as_many_short_ticks()
    {
        var draught = FurnaceHeat.DraughtOf(6, C);
        var startHeat = new HoleHeat(1400, 12, true);
        var longPots = new[] { StainlessCharge(), MoltenIron(240) };
        longPots[1].Add(Book, Stainless.Ferrochrome, 12, Cap);
        var shortPots = new[] { StainlessCharge(), MoltenIron(240) };
        shortPots[1].Add(Book, Stainless.Ferrochrome, 12, Cap);

        // 14 hours: up to the draught's 1600, both melting, the coke out at 12 and the fire cooling
        // past the solid points, the baths frozen an hour after
        var once = FurnaceMelt.Run(startHeat, 14, draught, true, Damper.Open, longPots, Book, Solid, C, out _);
        var ticked = startHeat;
        double tick = 1.0 / 120;   // a real second at the game's default speed
        for (int i = 0; i < 14 * 120; i++)
            ticked = FurnaceMelt.Run(ticked, tick, draught, true, Damper.Open, shortPots, Book, Solid, C, out _);

        Assert.Equal(ticked.Temperature, once.Temperature, 3);
        Assert.Equal(ticked.Coke, once.Coke, 6);
        for (int p = 0; p < 2; p++)
        {
            Assert.Equal(shortPots[p].Melted, longPots[p].Melted);
            Assert.Equal(shortPots[p].Progress, longPots[p].Progress, 0);
            Assert.Equal(shortPots[p].BelowSolidHours, longPots[p].BelowSolidHours, 1);
            Assert.Equal(shortPots[p].Frozen, longPots[p].Frozen);
        }
        Assert.Equal("stainless", longPots[0].Melted);
        Assert.Equal("stainless", longPots[1].Melted);
        Assert.True(longPots[0].Frozen && longPots[1].Frozen);

        // and half-way: still melting, the same share either way
        var half = new[] { StainlessCharge() };
        var halfTicked = new[] { StainlessCharge() };
        FurnaceMelt.Run(new HoleHeat(1500, 12, true), 2, draught, true, Damper.Open, half, Book, Solid, C, out _);
        var h = new HoleHeat(1500, 12, true);
        for (int i = 0; i < 240; i++)
            h = FurnaceMelt.Run(h, tick, draught, true, Damper.Open, halfTicked, Book, Solid, C, out _);
        Assert.Null(half[0].Melted);
        Assert.Equal(halfTicked[0].Progress, half[0].Progress, 1);
    }

    // ---- The chill ----

    [Theory]
    [InlineData(1600, 300, 20, 1360)]          // a full cold pot: 240 °C
    [InlineData(1600, 60, 20, 1552)]           // 12 cold ferrochrome
    [InlineData(1600, 300, 1600, 1600)]        // at the fire's own heat: nothing
    [InlineData(1600, 300, 1700, 1600)]        // hotter: nothing either
    [InlineData(200, 300, 20, 20)]             // never below the air
    [InlineData(20, 300, 20, 20)]              // a cold furnace has nothing to lose
    public void Charging_chills_the_fire_by_how_much_colder_the_items_are(double fire, int units, double item, double after) =>
        Assert.Equal(after, FurnaceMelt.ChargeChill(fire, units, item, C), 6);

    [Fact]
    public void Hot_bits_from_a_pour_cost_almost_nothing()
    {
        double after = FurnaceMelt.ChargeChill(1600, 300, 1500, C);
        Assert.Equal(1600 - 0.8 * 300 * 100 / 1580, after, 6);
        Assert.True(1600 - after < 16);
    }

    // ---- Freezing in the furnace ----

    [Fact]
    public void A_melt_freezes_only_after_an_hour_below_its_solid_point()
    {
        var pot = new FurnacePot { Melted = "stainless", MeltedUnits = 300 };
        double solid = PourWindow.SolidifiesAt(1530);   // 1377 °C
        Hold(pot, 1360, 0.9);
        Assert.False(pot.Frozen);
        Assert.Equal(0.1, FurnaceMelt.FreezesIn(pot, C), 6);
        // back above it: the count starts again
        Hold(pot, 1400, 0.05);
        Assert.Equal(0, pot.BelowSolidHours);
        Hold(pot, 1360, 0.95);
        Assert.False(pot.Frozen);
        // a fire cooling through it counts only the time below
        var cooling = new FurnacePot { Melted = "stainless", MeltedUnits = 300 };
        FurnaceMelt.Step(cooling, Book, 1, solid + 100, solid - 100, Solid, C);
        Assert.Equal(0.5, cooling.BelowSolidHours, 6);
        Assert.False(cooling.Frozen);
        Assert.True(FurnaceMelt.Step(cooling, Book, 0.5, solid - 100, solid - 150, Solid, C));
        Assert.True(cooling.Frozen);
        // frozen, it takes nothing and melts no more
        Assert.False(cooling.TakesCharge(Book));
        Assert.Equal(0, cooling.Add(Book, Stainless.Bit, 10, Cap).Count);
        Assert.False(FurnaceMelt.Step(cooling, Book, 5, 1600, 1600, Solid, C));
        Assert.True(cooling.Frozen);
    }

    [Fact]
    public void A_ferroalloy_cake_never_freezes()
    {
        var pot = new FurnacePot { Melted = "ferrochrome", MeltedUnits = 300 };
        Hold(pot, 20, 10);
        Assert.False(pot.Frozen);
    }

    // ---- Additions to a molten bath ----

    [Fact]
    public void Ferrochrome_into_molten_iron_dissolves_into_stainless()
    {
        var pot = MoltenIron(240);
        Assert.True(pot.TakesCharge(Book));
        Assert.Equal(new ChargeItem("game:metalbit-iron", 48), Assert.Single(pot.Bath(Book)));
        // up to the stainless ratio's largest share and the pot's capacity: 12 of 20
        Assert.Equal(new ChargeAdd(12, ChargeRefusal.None), pot.Add(Book, Stainless.Ferrochrome, 20, Cap));
        Assert.Equal("stainless", pot.Melting(Book)?.Code);
        Assert.False(pot.OffRatio(Book));
        // 60 units at a speed of 1 is 0.6 hours; at 1600 °C (0.69) about 0.87
        Assert.Equal(0.6 / 0.69, FurnaceMelt.HoursLeft(pot, Book, 1600, C)!.Value, 6);
        Hold(pot, 1600, 0.8);
        Assert.Equal("iron", pot.Melted);
        Assert.Single(pot.Charge);
        Hold(pot, 1600, 0.1);
        Assert.Equal("stainless", pot.Melted);
        Assert.Equal(300, pot.MeltedUnits);
        Assert.Empty(pot.Charge);
    }

    [Fact]
    public void Iron_into_molten_iron_and_stainless_into_molten_stainless()
    {
        var iron = MoltenIron(200);
        Assert.Equal(new ChargeAdd(20, ChargeRefusal.None), iron.Add(Book, "game:metalbit-iron", 64, Cap));
        Assert.Equal("iron", iron.Melting(Book)?.Code);
        Hold(iron, 1600, 2);
        Assert.Equal(("iron", 300), (iron.Melted, iron.MeltedUnits));

        var stainless = new FurnacePot { Melted = "stainless", MeltedUnits = 200 };
        Assert.Equal(new ChargeAdd(20, ChargeRefusal.None), stainless.Add(Book, Stainless.Bit, 64, Cap));
        Assert.Equal("stainlessremelt", stainless.Melting(Book)?.Code);
        Hold(stainless, 1600, 2);
        Assert.Equal(("stainlessremelt", 300), (stainless.Melted, stainless.MeltedUnits));
    }

    [Fact]
    public void Too_little_ferrochrome_leaves_the_bath_off_ratio_pouring_as_iron()
    {
        var pot = MoltenIron(240);
        Assert.Equal(6, pot.Add(Book, Stainless.Ferrochrome, 6, Cap).Count);   // 30 of 270: 11 %
        Assert.True(pot.OffRatio(Book));
        Assert.Null(pot.Melting(Book));
        Assert.True(pot.ChargeMakesSomething(Book));    // it still pours its iron: no bar to lighting
        var match = Book.Match(pot.Mix(Book));
        Assert.Equal("stainless", match.Closest?.Code);
        // nothing dissolves while it is off ratio; the bath stays iron
        Hold(pot, 1600, 3);
        Assert.Equal(("iron", 240), (pot.Melted, pot.MeltedUnits));
        Assert.Equal(0, pot.Progress);
        // six more put it on the ratio, and it dissolves
        Assert.Equal(6, pot.Add(Book, Stainless.Ferrochrome, 6, Cap).Count);
        Assert.False(pot.OffRatio(Book));
        Hold(pot, 1600, 1);
        Assert.Equal(("stainless", 300), (pot.Melted, pot.MeltedUnits));
    }

    [Fact]
    public void A_molten_bath_refuses_what_overfills_it_or_does_not_mix()
    {
        Assert.Equal(new ChargeAdd(0, ChargeRefusal.PotFull), MoltenIron(300).Add(Book, Stainless.Ferrochrome, 1, Cap));
        Assert.Equal(new ChargeAdd(0, ChargeRefusal.PotFull), MoltenIron(240).Add(Book, "game:ingot-iron", 1, Cap));
        var stainless = new FurnacePot { Melted = "stainless", MeltedUnits = 200 };
        Assert.Equal(new ChargeAdd(0, ChargeRefusal.DoesNotMix), stainless.Add(Book, "game:ingot-iron", 1, Cap));
        Assert.Equal(new ChargeAdd(0, ChargeRefusal.DoesNotMix), stainless.Add(Book, Stainless.Ferrochrome, 1, Cap));
        // a ferroalloy cake takes nothing
        var cake = new FurnacePot { Melted = "ferrosilicon", MeltedUnits = 300 };
        Assert.False(cake.TakesCharge(Book));
        Assert.Equal(0, cake.Add(Book, "game:crushed-quartz", 1, Cap).Count);
    }

    [Fact]
    public void A_melt_is_counted_as_its_bits()
    {
        Assert.Equal(new ChargeItem("game:metalbit-iron", 48), PotRecipes.MeltAsItems("game:ingot-iron", 240));
        Assert.Equal(new ChargeItem("game:metalbit-stainlesssteel", 60), PotRecipes.MeltAsItems(Stainless.Ingot, 300));
    }
}
