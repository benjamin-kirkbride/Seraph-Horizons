namespace SeraphHorizons.Mod.Trading.Core;

/// <summary>
/// The pack's eleven trader types (#448). Each is an entity variant
/// (<c>seraphhorizons:trader-{gender}-{type}-{climate}</c>, made by Trading/tools/make_entities.py)
/// with a trade list <c>config/tradelists/trader-{type}.json</c>. The prospector is placed on its
/// own lattice by <see cref="TraderGrid"/>, so that one is always near; the rest are chosen per cell
/// by weight.
/// </summary>
public static class TraderTypes
{
    public const string Smith = "smith";
    public const string Mechanic = "mechanic";
    public const string Prospector = "prospector";
    public const string Farmer = "farmer";
    public const string Cook = "cook";
    public const string Tailor = "tailor";
    public const string Carpenter = "carpenter";
    public const string Mason = "mason";
    public const string AnimalDealer = "animaldealer";
    public const string GeneralStore = "generalstore";
    public const string CurioDealer = "curiodealer";
    // Travelling merchants (#456): not camp types; they visit player-built inns
    // (Trading/Visitors/), as <c>seraphhorizons:visitor-{gender}-{type}-{climate}</c>.
    public const string TravellingMerchant = "travellingmerchant";
    public const string TravellingCurio = "travellingcurio";
    // Story traders: a story structure's own trader, never placed by the grid; its structure's
    // spawner is rewritten to it (Trading/Core/StoryTraders.cs). It keeps the game's quest
    // dialogue, with the pack's additions.
    public const string TreasureHunter = "treasurehunter";

    public static readonly IReadOnlyList<string> All =
    [
        Smith, Mechanic, Prospector, Farmer, Cook, Tailor, Carpenter, Mason, AnimalDealer, GeneralStore, CurioDealer,
    ];

    /// <summary>The visitors' types: they have trade lists but are never on the grid.</summary>
    public static readonly IReadOnlyList<string> Visitors = [TravellingMerchant, TravellingCurio];

    /// <summary>The story traders' types: they have trade lists and live in the game's story
    /// structures, never on the grid, and are not visitors.</summary>
    public static readonly IReadOnlyList<string> Story = [TreasureHunter];

    /// <summary>Every type with a trade list: the eleven camp types, the visitors and the story
    /// traders.</summary>
    public static IEnumerable<string> Listed => All.Concat(Visitors).Concat(Story);

    /// <summary>The entity code path of a type: <c>trader-{gender}-{type}-{climate}</c>, where
    /// climate is the outfit set (<c>cold</c>, <c>temperate</c> or <c>desert</c>, as vanilla's).</summary>
    public static string EntityPath(string gender, string type, string outfitClimate) => $"trader-{gender}-{type}-{outfitClimate}";

    /// <summary>The outfit set vanilla's traders use at a spot (its ModSystemClimateSpecificTraderTypes):
    /// desert from 15 °C when dry, cold below -4 °C.</summary>
    public static string OutfitClimate(float temperature, float rainfall) =>
        temperature >= 15f && rainfall <= 0.4f ? "desert" : temperature < -4f ? "cold" : "temperate";
}
