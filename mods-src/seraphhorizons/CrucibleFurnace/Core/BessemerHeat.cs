namespace SeraphHorizons.Mod.CrucibleFurnace.Core;

/// <summary>
/// Stainless from Steelmaking Expanded's Bessemer converter (#484 part D, README "Crucible furnace",
/// "In bulk: the Bessemer converter"): ferrochrome charged into a heat makes it pour stainless when
/// its share of the metal is the pot's (<see cref="Stainless.RatioMet"/>) and the bath is hot enough
/// for stainless to stay liquid. Off the ratio, or too cold, the heat pours steel and the ferrochrome
/// goes to the slag. The parts that need no game.
/// </summary>
public static class BessemerHeat
{
    /// <summary>What a heat holding ferrochrome pours.</summary>
    public enum Outcome
    {
        /// <summary>No ferrochrome in the heat: it pours what it is.</summary>
        None,
        /// <summary>On the ratio and hot enough: it pours stainless, unit for unit.</summary>
        Stainless,
        /// <summary>Off the ratio: it pours steel, the ferrochrome lost to the slag.</summary>
        OffRatio,
        /// <summary>On the ratio but below stainless's melting point: steel, the ferrochrome lost.</summary>
        TooCold,
    }

    /// <summary>What a heat of <paramref name="heatUnits"/> units in all, <paramref name="ferrochromeUnits"/>
    /// of them ferrochrome, pours at <paramref name="temperature"/> °C, stainless melting at
    /// <paramref name="stainlessMeltingPoint"/>. A null temperature (the heat not blown yet) judges
    /// the ratio alone.</summary>
    public static Outcome Judge(int heatUnits, int ferrochromeUnits, float? temperature, float stainlessMeltingPoint)
    {
        if (ferrochromeUnits <= 0)
            return Outcome.None;
        if (!Stainless.RatioMet(heatUnits - ferrochromeUnits, ferrochromeUnits))
            return Outcome.OffRatio;
        return temperature is { } t && t < stainlessMeltingPoint ? Outcome.TooCold : Outcome.Stainless;
    }

    /// <summary>The units a heat of <paramref name="heatUnits"/> pours after <paramref name="outcome"/>:
    /// all of them as stainless, or less the ferrochrome as steel.</summary>
    public static int UnitsPoured(int heatUnits, int ferrochromeUnits, Outcome outcome) =>
        outcome is Outcome.OffRatio or Outcome.TooCold ? Math.Max(0, heatUnits - ferrochromeUnits) : heatUnits;

    /// <summary>Ferrochrome's share of a heat, in whole percent, for the block info.</summary>
    public static int SharePercent(int heatUnits, int ferrochromeUnits) =>
        heatUnits <= 0 ? 0 : (int)Math.Round(100.0 * ferrochromeUnits / heatUnits);
}
