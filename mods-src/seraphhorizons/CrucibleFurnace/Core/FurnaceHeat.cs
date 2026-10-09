namespace SeraphHorizons.Mod.CrucibleFurnace.Core;

/// <summary>What draws the air through a hole's fire.</summary>
public enum Draft
{
    /// <summary>No working stack: the hole is not in a row with a tall enough chimney.</summary>
    None,
    /// <summary>The chimney's draft alone.</summary>
    Chimney,
    /// <summary>Air piped in from a blower, on top of the chimney.</summary>
    Forced,
}

/// <summary>A hole's fire: its temperature, the coke in it (a fraction while a piece burns) and
/// whether it is lit.</summary>
public readonly record struct HoleHeat(double Temperature, double Coke, bool Lit);

/// <summary>
/// The hole's temperature model (README "Crucible furnace"). While lit, the coke burns down at
/// <see cref="CrucibleFurnaceConfig.HoursPerCoke"/> a piece whatever else happens; it heats the hole
/// only under a closed lid with a draft, at the chimney's or the forced air's rate up to that draft's
/// highest temperature (and above it, falls back to it). Otherwise, and once the coke is gone, the hole
/// cools to the air's temperature, faster with its lid open. Piecewise linear, so a long gap is the
/// same as many short ones.
/// </summary>
public static class FurnaceHeat
{
    public const double Ambient = 20;

    public static HoleHeat Advance(HoleHeat s, double hours, Draft draft, bool lidClosed, CrucibleFurnaceConfig c, double ambient = Ambient)
    {
        if (hours <= 0)
            return s;
        double t = s.Temperature;
        double coke = Math.Max(0, s.Coke);
        bool lit = s.Lit && coke > 0;
        double cooling = lidClosed ? c.CoolingPerHour : c.LidOpenCoolingPerHour;
        double burn = 0;
        if (lit)
        {
            burn = Math.Min(hours, coke * c.HoursPerCoke);
            coke -= burn / c.HoursPerCoke;
            if (coke <= 1e-9)
            {
                coke = 0;
                lit = false;
            }
            if (lidClosed && draft != Draft.None)
            {
                double max = MaxTemperature(draft, c);
                double rate = draft == Draft.Forced ? c.ForcedHeatingPerHour : c.ChimneyHeatingPerHour;
                t = t < max ? Math.Min(max, t + rate * burn) : Math.Max(max, t - c.CoolingPerHour * burn);
            }
            else
                t = Cool(t, burn, cooling, ambient);
        }
        t = Cool(t, hours - burn, cooling, ambient);
        return new HoleHeat(t, coke, lit);
    }

    /// <summary>The hottest a lit hole gets on <paramref name="draft"/>; the air's temperature with none.</summary>
    public static double MaxTemperature(Draft draft, CrucibleFurnaceConfig c) => draft switch
    {
        Draft.Forced => c.ForcedMaxTemperature,
        Draft.Chimney => c.ChimneyMaxTemperature,
        _ => Ambient,
    };

    /// <summary>Hours a lit hole under a closed lid takes from <paramref name="from"/> to
    /// <paramref name="to"/> °C on <paramref name="draft"/> (infinity if the draft never gets there).</summary>
    public static double HoursToReach(double from, double to, Draft draft, CrucibleFurnaceConfig c)
    {
        if (from >= to)
            return 0;
        if (draft == Draft.None || MaxTemperature(draft, c) < to)
            return double.PositiveInfinity;
        return (to - from) / (draft == Draft.Forced ? c.ForcedHeatingPerHour : c.ChimneyHeatingPerHour);
    }

    private static double Cool(double t, double hours, double rate, double ambient) =>
        hours <= 0 || t <= ambient ? t : Math.Max(ambient, t - rate * hours);
}

/// <summary>
/// The pulled pot's pour window (README "Crucible furnace"). The game cools a hot stack at its
/// <c>cooldownSpeed</c> (°C a game hour, 90 unless set), and a smelted container's metal is solid below
/// 0.9 of its melting point (<c>BlockSmeltedContainer.HasSolidifed</c>), so the pot is given the speed
/// that takes it from the furnace's temperature to that point in the window.
/// </summary>
public static class PourWindow
{
    /// <summary>The game's own cooldown speed, °C a game hour.</summary>
    public const double GameCooldownPerHour = 90;

    /// <summary>The fraction of the melting point below which the game counts the metal solid.</summary>
    public const double SolidFraction = 0.9;

    public static double SolidifiesAt(double meltingPoint) => SolidFraction * meltingPoint;

    /// <summary>The cooldown speed that takes a pot pulled at <paramref name="pulledAt"/> °C to
    /// solid in <paramref name="windowSeconds"/> real seconds, at <paramref name="gameHoursPerSecond"/>
    /// game hours a real second (1/120 at the game's default speed); at least the game's own speed.</summary>
    public static double CooldownPerHour(double pulledAt, double meltingPoint, double windowSeconds, double gameHoursPerSecond)
    {
        double drop = pulledAt - SolidifiesAt(meltingPoint);
        double hours = windowSeconds * gameHoursPerSecond;
        if (drop <= 0 || hours <= 0)
            return GameCooldownPerHour;
        return Math.Max(GameCooldownPerHour, drop / hours);
    }
}
