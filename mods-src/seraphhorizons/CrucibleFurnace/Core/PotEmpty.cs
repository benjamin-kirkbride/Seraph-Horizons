namespace SeraphHorizons.Mod.CrucibleFurnace.Core;

/// <summary>
/// Emptying an unmelted charge out of a held melting pot, as the game's bucket is emptied: Ctrl and
/// right-click on a block (README "Crucible furnace", "Pulling and teeming"). Where the bucket spills its
/// liquid, the pot drops its charge's items where the click landed; the pot keeps its heats.
/// </summary>
public static class PotEmpty
{
    /// <summary>Whether a pot with <paramref name="chargeItems"/> kinds of charge at
    /// <paramref name="temperature"/> can be emptied: it has a charge and is cold enough to hold
    /// (<paramref name="tooHotToTouch"/>, the game's <c>GlobalConstants.TooHotToTouchTemperature</c>).</summary>
    public static bool Emptiable(int chargeItems, double temperature, double tooHotToTouch) =>
        chargeItems > 0 && temperature <= tooHotToTouch;

    /// <summary><paramref name="count"/> items as stacks of at most <paramref name="maxStack"/>.</summary>
    public static IEnumerable<int> Stacks(int count, int maxStack)
    {
        maxStack = Math.Max(1, maxStack);
        for (int left = count; left > 0; left -= maxStack)
            yield return Math.Min(left, maxStack);
    }
}
