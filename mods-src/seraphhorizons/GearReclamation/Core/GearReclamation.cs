namespace SeraphHorizons.Mod.GearReclamation.Core;

/// <summary>The gear codes of the reclamation line (#474): the contract the other gear steps (the
/// pickling tub, the consumer patches, the brine bath) are written against.</summary>
public static class GearCodes
{
    public const string Domain = "seraphhorizons";

    /// <summary>The usable gear: the only one any recipe wants.</summary>
    public const string Steel = "seraphhorizons:gear-steel";

    /// <summary>Out of the degreasing pot.</summary>
    public const string Degreased = "seraphhorizons:gear-degreased";

    /// <summary>Out of the pickling tub; flash-rusts.</summary>
    public const string Pickled = "seraphhorizons:gear-pickled";

    /// <summary>Out of the lime water barrel; flash-rusts.</summary>
    public const string Neutralized = "seraphhorizons:gear-neutralized";

    /// <summary>Out of the oil barrel: the lottery item, resolved in a player's inventory.</summary>
    public const string Oiled = "seraphhorizons:gear-oiled";

    /// <summary>The large usable gear, on the large temporal gear's shape.</summary>
    public const string LargeSteel = "seraphhorizons:largegear-steel";

    public const string Rusty = "game:gear-rusty";

    /// <summary>What a gear that does not come out sound is worth.</summary>
    public const string SteelBit = "game:metalbit-steel";

    /// <summary>An item attribute: its Perish transition to the rusty gear follows
    /// <see cref="GearReclamationConfig.FlashRustHours"/>. Any item of any step may carry it.</summary>
    public const string FlashRustAttribute = "seraphhorizonsFlashRust";
}

/// <summary>What a stack of oiled gears resolves into.</summary>
public readonly record struct LotteryResult(int Gears, int Steel, int Bits)
{
    /// <summary>Gears that did not come out sound.</summary>
    public int Failed => Gears - Steel;
}

/// <summary>The oil step's roll (#477): each oiled gear is sound with a chance, else scrap to steel
/// bits.</summary>
public static class GearLottery
{
    /// <summary>Rolls <paramref name="gears"/> gears, each sound when <paramref name="next"/> (uniform
    /// in [0, 1)) comes out below <paramref name="chance"/>; every other gear gives
    /// <paramref name="bitsPerFailure"/> bits. One draw per gear, in order, so the same random
    /// sequence gives the same result.</summary>
    public static LotteryResult Roll(int gears, double chance, int bitsPerFailure, Func<double> next)
    {
        if (gears <= 0)
            return new LotteryResult(0, 0, 0);
        chance = Math.Clamp(double.IsFinite(chance) ? chance : 0, 0, 1);
        bitsPerFailure = Math.Max(0, bitsPerFailure);
        int steel = 0;
        for (int i = 0; i < gears; i++)
            if (next() < chance)
                steel++;
        return new LotteryResult(gears, steel, (gears - steel) * bitsPerFailure);
    }

    /// <summary>The mean result of <see cref="Roll"/>: sound gears and bits.</summary>
    public static (double Steel, double Bits) Expected(int gears, double chance, int bitsPerFailure)
    {
        if (gears <= 0)
            return (0, 0);
        chance = Math.Clamp(double.IsFinite(chance) ? chance : 0, 0, 1);
        return (gears * chance, gears * (1 - chance) * Math.Max(0, bitsPerFailure));
    }

    /// <summary>The standard deviation of the sound gears out of <paramref name="gears"/>
    /// (binomial), for the tests' tolerance.</summary>
    public static double SteelDeviation(int gears, double chance) =>
        gears <= 0 ? 0 : Math.Sqrt(gears * chance * (1 - chance));

    /// <summary>Splits <paramref name="count"/> items into stacks of at most
    /// <paramref name="maxStack"/>.</summary>
    public static IEnumerable<int> Stacks(int count, int maxStack)
    {
        maxStack = Math.Max(1, maxStack);
        for (int left = count; left > 0; left -= maxStack)
            yield return Math.Min(left, maxStack);
    }
}

/// <summary>The flash rust of the bare gears (#474): a vanilla Perish transition to the rusty
/// gear, fresh for <see cref="Fresh"/> hours, then rusting over <see cref="Transition"/>.</summary>
public readonly record struct FlashRustHours(double Fresh, double Transition)
{
    /// <summary>The setting's hours are the fresh time; the rusting itself takes a quarter as long
    /// again, so a batch reads "fresh for 8 hours", then visibly goes, and is rusty 10 hours in.</summary>
    public static FlashRustHours For(double hours) => new(hours, hours / 4);

    /// <summary>Hours from bare to rusty.</summary>
    public double Total => Fresh + Transition;
}

/// <summary>Recipes with ingredients from optional mods (#475, #477): an ingredient whose code is
/// in a domain no loaded mod has is dropped, as an alternative, or takes its recipe with it.</summary>
public static class OptionalIngredients
{
    /// <summary>The domain of <paramref name="code"/> (<c>game</c> when it has none).</summary>
    public static string Domain(string code)
    {
        int colon = code.IndexOf(':');
        return colon < 0 ? "game" : code[..colon].Trim().ToLowerInvariant();
    }

    public static bool Available(string code, Func<string, bool> domainLoaded) => domainLoaded(Domain(code));

    /// <summary>The alternatives of one ingredient that can be made here, in order.</summary>
    public static IReadOnlyList<string> Keep(IEnumerable<string> alternatives, Func<string, bool> domainLoaded) =>
        alternatives.Where(c => Available(c, domainLoaded)).ToList();
}

/// <summary>GearReclamationSettings in ModConfig/seraphhorizons.json.</summary>
public class GearReclamationConfig
{
    /// <summary>In-game hours a pickled or neutralized gear stays bare before it starts to rust.</summary>
    public double FlashRustHours { get; set; } = 8;

    /// <summary>The chance each oiled gear is sound.</summary>
    public double UsableGearChance { get; set; } = 0.1;

    /// <summary>Steel bits for each oiled gear that is not.</summary>
    public int BitsPerFailedGear { get; set; } = 1;

    public static readonly GearReclamationConfig Defaults = new();

    /// <summary>Replaces values out of range with the default; returns a line per replaced value.</summary>
    public IReadOnlyList<string> Sanitise()
    {
        var fixes = new List<string>();
        if (!double.IsFinite(FlashRustHours) || FlashRustHours < 0.5 || FlashRustHours > 24 * 365)
        {
            fixes.Add($"FlashRustHours {FlashRustHours} is out of range (0.5 to 8760), using {Defaults.FlashRustHours}");
            FlashRustHours = Defaults.FlashRustHours;
        }
        if (!double.IsFinite(UsableGearChance) || UsableGearChance < 0 || UsableGearChance > 1)
        {
            fixes.Add($"UsableGearChance {UsableGearChance} is out of range (0 to 1), using {Defaults.UsableGearChance}");
            UsableGearChance = Defaults.UsableGearChance;
        }
        if (BitsPerFailedGear < 0 || BitsPerFailedGear > 20)
        {
            fixes.Add($"BitsPerFailedGear {BitsPerFailedGear} is out of range (0 to 20), using {Defaults.BitsPerFailedGear}");
            BitsPerFailedGear = Defaults.BitsPerFailedGear;
        }
        return fixes;
    }
}
