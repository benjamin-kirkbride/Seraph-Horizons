namespace SeraphHorizons.Mod.Ore.Core;

/// <summary>
/// Whether a deposit is under water: more than half of the columns its ore is in have more than
/// <see cref="MinDepth"/> blocks of water or ice standing over the terrain. Measured with the
/// deposit (<c>DepositService</c>) and kept on its record; its maps are named so and sold cheap
/// (<c>config/trading/map-prices.json</c>, <c>underwater</c>).
/// </summary>
public static class DepositWater
{
    /// <summary>Blocks of water or ice a column needs more than to count as under water.</summary>
    public const int MinDepth = 2;

    /// <summary>True when more than half of <paramref name="depths"/> (one per ore column, in
    /// blocks of water and ice) are deeper than <see cref="MinDepth"/>; false for none.</summary>
    public static bool IsUnderwater(IReadOnlyCollection<int> depths) =>
        depths.Count(d => d > MinDepth) * 2 > depths.Count;
}
