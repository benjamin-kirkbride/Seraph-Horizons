namespace SeraphHorizons.Mod.Ore.Core;

/// <summary>One leached mineral (#742): its raw form, the crude liquor the barrel soaks out of it, and
/// the crystals the cooking pot boils that down into (today's item, which every recipe using it
/// keeps taking).</summary>
public sealed record LeachedMineral(string Mineral, string RawCode, string LiquorCode, string CrystalCode);

/// <summary>
/// Leaching the industrial minerals (#742; README "Ore processing: leaching borax, saltpeter and
/// alum"): borax, saltpeter and alum come out of the ground as a heavy raw form (stack
/// <see cref="OreProducts.RawStack"/>) that no longer works where today's items are used. Raw
/// mineral and water soak in a barrel into a crude liquor of their own (not vanilla's diluted borax
/// or Expanded Matter's diluted alum and saltpeter, which have other uses and would let raw mineral
/// skip the pot), and the liquor boils down in a cooking pot into today's crystals. Game-independent;
/// the recipe files (<c>recipes/barrel/oreprocessing-leaching.json</c>,
/// <c>recipes/cooking/oreprocessing-evaporating.json</c>) hold these figures, and a test holds them
/// to it.
/// </summary>
public static class OreLeaching
{
    /// <summary>Litres of water one raw mineral soaks in, and of liquor it gives.</summary>
    public const int WaterLitresPerRaw = 1, LiquorLitresPerRaw = 1;

    /// <summary>Crystals one litre of liquor boils down to: one mined item makes one crystal item.</summary>
    public const int CrystalsPerLitre = 1;

    /// <summary>Hours the barrel stays sealed.</summary>
    public const double SealHours = 24;

    /// <summary>The raw saltpeter item, which the saltpeter blocks drop in place of saltpeter (vanilla's
    /// cave coating, Interesting Ore Gen's saltpeter ore, Saltpeter Production's nitre beds).</summary>
    public const string RawSaltpeter = OreProducts.Domain + ":rawsaltpeter";

    /// <summary>Saltpeter, the crystals, which those blocks dropped before.</summary>
    public const string Saltpeter = "game:saltpeter";

    /// <summary>Alum's crushed item, which the pulverizer no longer makes from raw alum: the recipes
    /// taking it take alum powder, the crystals, instead.</summary>
    public const string CrushedAlum = "game:crushed-alum";

    public static readonly IReadOnlyList<LeachedMineral> Minerals =
    [
        new("borax", "game:ore-borax", LiquorCode("borax"), "game:powder-borax"),
        new("saltpeter", RawSaltpeter, LiquorCode("saltpeter"), Saltpeter),
        new("alum", "game:ore-alum", LiquorCode("alum"), "game:powder-alum"),
    ];

    public static string LiquorCode(string mineral) => $"{OreProducts.Domain}:crudeliquorportion-{mineral}";

    /// <summary>The mineral whose raw form <paramref name="code"/> is, or null.</summary>
    public static LeachedMineral? OfRaw(string code) => Minerals.FirstOrDefault(m => m.RawCode == code);

    /// <summary>Vanilla's raw forms (borax and alum ore): the game's own items, restacked and kept
    /// from the quern and the pulverizer.</summary>
    public static IEnumerable<string> VanillaRaw => Minerals.Select(m => m.RawCode).Where(c => c.StartsWith("game:", StringComparison.Ordinal));
}
