namespace SeraphHorizons.Mod.CrucibleFurnace.Core;

/// <summary>
/// Stainless steel as the pack makes it (#484 part C, README "Crucible furnace"): iron and low-carbon
/// ferrochrome, about 4 to 1 by metal units, which is about 13 % chromium (the ferrochrome is about
/// two thirds chromium). The crucible furnace's pot recipe is built from these figures, and so is
/// anything else that makes stainless from ferrochrome (smex's Bessemer converter, part D), so the
/// two routes agree on the ratio. A unit is the game's: an ingot is 100, a bit or a lump 5.
/// </summary>
public static class Stainless
{
    /// <summary>The ferrochrome lump the pot makes from chromite, ferrosilicon and lime.</summary>
    public const string Ferrochrome = "seraphhorizons:ferrochrome";

    /// <summary>The ferrosilicon lump the pot makes from quartz, iron bits and coke.</summary>
    public const string Ferrosilicon = "seraphhorizons:ferrosilicon";

    /// <summary>Metal units a ferrochrome (or ferrosilicon) lump is worth: a bit's.</summary>
    public const int UnitsPerLump = 5;

    public const string Ingot = "game:ingot-stainlesssteel";
    public const string Bit = "game:metalbit-stainlesssteel";
    public const string IronIngot = "game:ingot-iron";
    public const string IronBit = "game:metalbit-iron";

    /// <summary>Smallest share of ferrochrome in the metal (the rest iron): 18 %.</summary>
    public const double FerrochromeMin = 0.18;

    /// <summary>Largest share of ferrochrome in the metal: 22 %. Between the two, 4 : 1 (20 %) in the middle.</summary>
    public const double FerrochromeMax = 0.22;

    /// <summary>Whether <paramref name="ironUnits"/> of iron and <paramref name="ferrochromeUnits"/> of
    /// ferrochrome make stainless: some of both, the ferrochrome's share within
    /// [<see cref="FerrochromeMin"/>, <see cref="FerrochromeMax"/>] (a hair of slack for rounding).</summary>
    public static bool RatioMet(double ironUnits, double ferrochromeUnits)
    {
        if (ironUnits <= 0 || ferrochromeUnits <= 0)
            return false;
        double share = ferrochromeUnits / (ironUnits + ferrochromeUnits);
        return share >= FerrochromeMin - 1e-9 && share <= FerrochromeMax + 1e-9;
    }
}
