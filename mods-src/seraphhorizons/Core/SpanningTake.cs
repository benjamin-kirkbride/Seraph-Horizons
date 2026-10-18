namespace SeraphHorizons.Mod.Core;

/// <summary>
/// A stack wanted from several slots (<c>DialogueStacks</c>): the game's dialogue looks for one slot
/// holding the whole stack it asks for, which a stack cap can make impossible (100 rusty gears, which
/// stack to 64). Here the wanted number is taken from the matching slots in order instead.
/// </summary>
public static class SpanningTake
{
    /// <summary>How many to take from each slot, in order, to make <paramref name="want"/>; null when
    /// the slots together hold fewer, or one slot alone holds enough (the game's own case).</summary>
    public static int[]? Plan(IReadOnlyList<int> sizes, int want)
    {
        if (want <= 0 || sizes.Any(s => s >= want) || sizes.Where(s => s > 0).Sum() < want) return null;
        var takes = new int[sizes.Count];
        int left = want;
        for (int i = 0; i < sizes.Count && left > 0; i++)
        {
            int n = Math.Min(Math.Max(0, sizes[i]), left);
            takes[i] = n;
            left -= n;
        }
        return takes;
    }
}
