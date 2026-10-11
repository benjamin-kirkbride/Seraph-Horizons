namespace SeraphHorizons.Mod.CrucibleFurnace.Core;

/// <summary>The ash-pit damper's three places, in the order a click cycles them.</summary>
public enum Damper
{
    /// <summary>Clear of the ash mouth: the chimney's full draught, the coke at its open rate.</summary>
    Open,
    /// <summary>Over the mouth's upper half: half the heating, the coke slower.</summary>
    Half,
    /// <summary>Over all of it: the fire banked, no heating, slow cooling, the coke slowest.</summary>
    Shut,
}

/// <summary>What a furnace's chimney gives it: <see cref="HeatingPerHour"/> °C an hour under a shut
/// lid, up to <see cref="MaxTemperature"/>, from a chimney <see cref="Height"/> blocks tall.</summary>
public readonly record struct Draught(double HeatingPerHour, double MaxTemperature, int Height);

/// <summary>A furnace's fire: its temperature, the coke in it (a fraction while a piece burns) and
/// whether it is lit.</summary>
public readonly record struct HoleHeat(double Temperature, double Coke, bool Lit);

/// <summary>
/// The furnace's temperature model (README "Crucible furnace"). Natural draught only, set by the
/// chimney's height (<see cref="DraughtOf"/>) and throttled at the ash-pit damper. While lit, the coke
/// burns down at <see cref="CrucibleFurnaceConfig.HoursPerCoke"/> a piece, slower with the damper half
/// or shut, whatever else happens; it heats the furnace only under a shut lid with a draught and the
/// damper not shut, at the draught's rate (halved with the damper half) up to the draught's highest
/// temperature (and above it, falls back to it). Otherwise, and once the coke is gone, the furnace
/// cools to the air's temperature: faster with its lid open, slower banked (lid and damper shut).
/// Piecewise linear, so a long gap is the same as many short ones.
/// </summary>
public static class FurnaceHeat
{
    public const double Ambient = 20;

    /// <summary>The draught of a chimney <paramref name="height"/> blocks tall, counted from the
    /// outlet up: none under <see cref="CrucibleFurnaceConfig.MinChimneyHeight"/>; at it, the base rate
    /// and maximum; each block above adds its share, up to <see cref="CrucibleFurnaceConfig.MaxChimneyHeight"/>.</summary>
    public static Draught? DraughtOf(int height, CrucibleFurnaceConfig c)
    {
        if (height < c.MinChimneyHeight)
            return null;
        int extra = Math.Max(0, Math.Min(height, c.MaxChimneyHeight) - c.MinChimneyHeight);
        return new Draught(c.ChimneyHeatingPerHour + extra * c.ChimneyHeatingPerExtraBlock,
                           c.ChimneyMaxTemperature + extra * c.ChimneyMaxPerExtraBlock, height);
    }

    /// <summary>The share of the coke's open burning rate the damper leaves.</summary>
    public static double BurnRate(Damper damper, CrucibleFurnaceConfig c) => damper switch
    {
        Damper.Half => c.DamperHalfBurn,
        Damper.Shut => c.DamperShutBurn,
        _ => 1,
    };

    /// <summary>The heating, °C an hour, a lit furnace under a shut lid gets on
    /// <paramref name="draught"/> through the damper; 0 with no draught or the damper shut.</summary>
    public static double HeatingRate(Draught? draught, Damper damper, CrucibleFurnaceConfig c) =>
        draught is not { } d ? 0 : damper switch
        {
            Damper.Half => d.HeatingPerHour * c.DamperHalfHeating,
            Damper.Shut => 0,
            _ => d.HeatingPerHour,
        };

    /// <summary>How fast the furnace cools, °C an hour: with its lid open, at the open rate; shut, at
    /// the closed rate, a quarter of it (by default) when banked behind a shut damper.</summary>
    public static double CoolingRate(bool lidClosed, Damper damper, CrucibleFurnaceConfig c) =>
        !lidClosed ? c.LidOpenCoolingPerHour
        : damper == Damper.Shut ? c.CoolingPerHour * c.DamperShutCooling
        : c.CoolingPerHour;

    /// <summary>Game hours a full piece of coke lasts behind the damper.</summary>
    public static double HoursPerCoke(Damper damper, CrucibleFurnaceConfig c) => c.HoursPerCoke / BurnRate(damper, c);

    public static HoleHeat Advance(HoleHeat s, double hours, Draught? draught, bool lidClosed, Damper damper,
                                   CrucibleFurnaceConfig c, double ambient = Ambient)
    {
        if (hours <= 0)
            return s;
        double t = s.Temperature;
        double coke = Math.Max(0, s.Coke);
        bool lit = s.Lit && coke > 0;
        double cooling = CoolingRate(lidClosed, damper, c);
        double perCoke = HoursPerCoke(damper, c);
        double burn = 0;
        if (lit)
        {
            burn = Math.Min(hours, coke * perCoke);
            coke -= burn / perCoke;
            if (coke <= 1e-9)
            {
                coke = 0;
                lit = false;
            }
            double rate = HeatingRate(draught, damper, c);
            if (lidClosed && draught is { } d && rate > 0)
                t = t < d.MaxTemperature ? Math.Min(d.MaxTemperature, t + rate * burn) : Math.Max(d.MaxTemperature, t - c.CoolingPerHour * burn);
            else
                t = Cool(t, burn, cooling, ambient);
        }
        t = Cool(t, hours - burn, cooling, ambient);
        return new HoleHeat(t, coke, lit);
    }

    /// <summary>The hottest a lit furnace gets on <paramref name="draught"/> with the damper not shut;
    /// the air's temperature with none.</summary>
    public static double MaxTemperature(Draught? draught) => draught?.MaxTemperature ?? Ambient;

    /// <summary>Hours a lit furnace under a shut lid takes from <paramref name="from"/> to
    /// <paramref name="to"/> °C (infinity if it never gets there).</summary>
    public static double HoursToReach(double from, double to, Draught? draught, Damper damper, CrucibleFurnaceConfig c)
    {
        if (from >= to)
            return 0;
        double rate = HeatingRate(draught, damper, c);
        if (rate <= 0 || MaxTemperature(draught) < to)
            return double.PositiveInfinity;
        return (to - from) / rate;
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
