namespace SeraphHorizons.Mod.CrucibleFurnace.Core;

/// <summary>
/// How the pots melt in the fire (README "Crucible furnace", "Melting takes time"), as Steelmaking
/// Expanded's blast furnace and the game's own steelmaking do it: a timer gated by heat, not a heat
/// balance. A pot's charge (or what was added to its molten bath) melts only while the fire is at or
/// above its recipe's temperature, at a speed set by smex's blast-furnace melt curve on the margin above
/// it (<see cref="HeatFactor"/>): a full pot takes <see cref="CrucibleFurnaceConfig.MeltHoursPerFullPot"/>
/// at a speed of 1, a part of a pot its share. Below the temperature the melt pauses, its progress kept.
/// A molten bath freezes once the fire has stayed below its solid point for
/// <see cref="CrucibleFurnaceConfig.MeltFreezeHours"/>. Charging chills the fire (<see cref="ChargeChill"/>).
/// <see cref="Run"/> advances the fire and the pots together in steps short enough that a long gap
/// comes out as many short ones; within a step the fire's temperature is taken as a straight line from
/// its start to its end (<see cref="FurnaceHeat.Advance"/> is piecewise linear) and the speed integrated
/// along it exactly.
/// </summary>
public static class FurnaceMelt
{
    /// <summary>The longest step <see cref="Run"/> takes, game hours (3 game minutes).</summary>
    public const double MaxStepHours = 0.05;

    /// <summary>Units of charge a pot melts an hour at a speed of 1.</summary>
    public static double UnitsPerHour(CrucibleFurnaceConfig c) => c.PotCapacityUnits / c.MeltHoursPerFullPot;

    /// <summary>The melt speed with the fire <paramref name="margin"/> °C above the recipe's
    /// temperature: none below it; at or above, smex's <c>clamp(base + margin / 100 × gain, min, max)</c>.</summary>
    public static double HeatFactor(double margin, CrucibleFurnaceConfig c) =>
        margin < 0 ? 0 : Math.Clamp(c.MeltSpeedBase + margin / 100 * c.MeltGainPer100C, c.MeltSpeedMin, Math.Max(c.MeltSpeedMin, c.MeltSpeedMax));

    /// <summary>The mean melt speed over a step in which the fire goes in a straight line from
    /// <paramref name="t0"/> to <paramref name="t1"/> °C, for a recipe melting at <paramref name="recipeT"/>.</summary>
    public static double MeanFactor(double t0, double t1, double recipeT, CrucibleFurnaceConfig c)
    {
        if (Math.Abs(t1 - t0) < 1e-9)
            return HeatFactor(t0 - recipeT, c);
        return (Integral(t1 - recipeT, c) - Integral(t0 - recipeT, c)) / (t1 - t0);
    }

    // ∫ HeatFactor from margin 0 to m (0 below): the floor, the line, then the ceiling
    private static double Integral(double m, CrucibleFurnaceConfig c)
    {
        if (m <= 0)
            return 0;
        double lo = c.MeltSpeedMin, hi = Math.Max(lo, c.MeltSpeedMax), b = c.MeltSpeedBase, k = c.MeltGainPer100C / 100;
        if (k <= 0)
            return Math.Clamp(b, lo, hi) * m;
        double a = Math.Max(0, (lo - b) / k), z = Math.Max(a, (hi - b) / k);
        double s = lo * Math.Min(m, a);
        double e = Math.Min(m, z);
        if (e > a)
            s += b * (e - a) + k * (e * e - a * a) / 2;
        if (m > z)
            s += hi * (m - z);
        return s;
    }

    /// <summary>The fire's temperature after <paramref name="units"/> at <paramref name="itemTemperature"/>
    /// go into a pot: <see cref="CrucibleFurnaceConfig.ChargeChillPerUnit"/> a unit, scaled by how much
    /// colder than the fire they are against the air (cold items the full figure, items at the fire's
    /// temperature nothing); never below the air's temperature.</summary>
    public static double ChargeChill(double fire, int units, double itemTemperature, CrucibleFurnaceConfig c, double ambient = FurnaceHeat.Ambient)
    {
        if (fire <= ambient || units <= 0)
            return fire;
        double share = Math.Clamp((fire - itemTemperature) / (fire - ambient), 0, 1);
        return Math.Max(ambient, fire - c.ChargeChillPerUnit * units * share);
    }

    /// <summary>The share of the pot's charge (or additions) melted, 0 to 1.</summary>
    public static double Share(FurnacePot pot)
    {
        int units = pot.ChargeUnits;
        return units <= 0 ? 0 : Math.Clamp(pot.Progress / units, 0, 1);
    }

    /// <summary>Game hours the pot's charge still needs at the fire's <paramref name="fire"/> °C, or null
    /// when it is not melting (nothing to melt, off every ratio, or the fire below its temperature).</summary>
    public static double? HoursLeft(FurnacePot pot, PotRecipes recipes, double fire, CrucibleFurnaceConfig c)
    {
        if (pot.Melting(recipes) is not { } r)
            return null;
        double f = HeatFactor(fire - r.Temperature, c);
        if (f <= 0)
            return null;
        return Math.Max(0, pot.ChargeUnits - pot.Progress) / (UnitsPerHour(c) * f);
    }

    /// <summary>Advances the fire <paramref name="hours"/> (as <see cref="FurnaceHeat.Advance"/>) and the
    /// pots with it: melting, dissolving and freezing. <paramref name="solidPointOf"/> gives a recipe's
    /// solid point, °C (0.9 of its output's melting point), or null for a bath that never freezes.
    /// <paramref name="potsChanged"/> says whether a pot melted, dissolved or froze.</summary>
    public static HoleHeat Run(HoleHeat heat, double hours, Draught? draught, bool lidClosed, Damper damper,
                               IReadOnlyList<FurnacePot?> pots, PotRecipes recipes, Func<string, double?> solidPointOf,
                               CrucibleFurnaceConfig c, out bool potsChanged, double ambient = FurnaceHeat.Ambient)
    {
        potsChanged = false;
        while (hours > 1e-12)
        {
            // a dead fire at the air's temperature stays there: the rest in one step
            bool still = !heat.Lit && heat.Temperature <= ambient;
            double dt = still ? hours : Math.Min(hours, MaxStepHours);
            var next = FurnaceHeat.Advance(heat, dt, draught, lidClosed, damper, c, ambient);
            foreach (var pot in pots)
                if (pot != null)
                    potsChanged |= Step(pot, recipes, dt, heat.Temperature, next.Temperature, solidPointOf, c);
            heat = next;
            hours -= dt;
        }
        return heat;
    }

    /// <summary>One step of a pot, the fire going from <paramref name="t0"/> to <paramref name="t1"/> °C
    /// in a straight line over <paramref name="dt"/> hours; whether it melted, dissolved or froze.</summary>
    public static bool Step(FurnacePot pot, PotRecipes recipes, double dt, double t0, double t1,
                            Func<string, double?> solidPointOf, CrucibleFurnaceConfig c)
    {
        if (pot.Frozen || dt <= 0)
            return false;
        bool changed = false;
        if (pot.Melting(recipes) is { } r)
        {
            double rate = UnitsPerHour(c) * MeanFactor(t0, t1, r.Temperature, c);
            if (rate > 0)
            {
                pot.Progress += rate * dt;
                if (pot.Progress >= pot.ChargeUnits - 1e-9)
                {
                    pot.Finish(recipes, r);
                    changed = true;
                }
            }
        }
        if (pot.Melted is { } melted && recipes.Get(melted) is { Pourable: true } && solidPointOf(melted) is { } s)
        {
            if (t1 >= s)
                pot.BelowSolidHours = 0;
            else
            {
                pot.BelowSolidHours += t0 > s ? dt * (s - t1) / (t0 - t1) : dt;
                if (pot.BelowSolidHours >= c.MeltFreezeHours - 1e-9)
                {
                    pot.Frozen = true;
                    changed = true;
                }
            }
        }
        return changed;
    }

    /// <summary>Game hours a molten pot has before it freezes, while the fire is below its solid point.</summary>
    public static double FreezesIn(FurnacePot pot, CrucibleFurnaceConfig c) => Math.Max(0, c.MeltFreezeHours - pot.BelowSolidHours);
}
