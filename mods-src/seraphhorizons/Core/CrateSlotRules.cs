namespace SeraphHorizons.Mod.Core;

/// <summary>
/// Big crates (<c>BigCrates</c>): every crate type's slots scaled so that the roomiest crate of the
/// game (ebony and purpleheart, 25 slots) holds <see cref="BestSlots"/>, and every other in
/// proportion, rounded up: <c>ceil(slots * 32 / 25)</c>, so 16 becomes 21, 20 becomes 26 and 25
/// becomes 32. Game-independent, so tests/ runs it without the game.
/// </summary>
public static class CrateSlotRules
{
    /// <summary>The game's roomiest crate's slots.</summary>
    public const int BestBefore = 25;

    /// <summary>What that crate holds now.</summary>
    public const int BestSlots = 32;

    /// <summary>A crate type of <paramref name="slots"/> slots made big; 0 or fewer stays as it is.</summary>
    public static int Slots(int slots) =>
        slots <= 0 ? slots : (int)Math.Ceiling(slots * (double)BestSlots / BestBefore);
}
