namespace SeraphHorizons.Mod.CreativeModTabs.Core;

/// <summary>What the game lays out in mod mode: the iteration order of the list, and the columns it yields.</summary>
/// <param name="Iteration">The tab codes in the order the mod mode's tab list holds them.</param>
/// <param name="Left">The codes the dialog puts in its left column, top to bottom.</param>
/// <param name="Right">The codes it puts in its right column, top to bottom.</param>
/// <param name="Ideal">Whether the left column is default mode's and the right one the mod tabs alone, in order.</param>
public sealed record ModModeLayout(List<string> Iteration, List<string> Left, List<string> Right, bool Ideal);

/// <summary>
/// How the creative dialog orders and splits its tabs (<c>GuiDialogInventory.ComposeCreativeInvDialog</c>,
/// VintagestoryLib 1.22.7), and the mod mode's tab list arranged so that the game itself shows the default
/// tabs of its left column there and the mod tabs as its right column (docs/variant-grouping/creative-mod-tabs.md).
/// Game-independent, so tests/ runs it without the game.
/// </summary>
public static class TabLayout
{
    /// <summary>How many tabs the dialog puts in its left column; the rest go in the right one.</summary>
    public const int LeftColumn = 16;

    /// <summary>The list order of a tab <c>config/creativetabs.json</c> doesn't name.</summary>
    public const double UnlistedOrder = 1.0;

    /// <summary>
    /// The dialog's order of <paramref name="codes"/>, given in the tab list's iteration order: each code is
    /// inserted before the first one already placed whose list order is equal or higher. So lower orders come
    /// first, and codes of equal order come out in the reverse of their iteration order.
    /// </summary>
    public static List<string> Order(IEnumerable<string> codes, Func<string, double> listOrder)
    {
        var ordered = new List<(string Code, double Order)>();
        foreach (var code in codes)
        {
            double order = listOrder(code);
            int at = 0;
            while (at < ordered.Count && ordered[at].Order < order) at++;
            ordered.Insert(at, (code, order));
        }
        return ordered.Select(t => t.Code).ToList();
    }

    /// <summary>
    /// The mod mode's list: the default tabs the dialog puts in its left column (the first
    /// <see cref="LeftColumn"/> of <paramref name="defaultCodes"/>' order) and every mod tab. The mod tabs are
    /// unlisted (order 1.0, like most default tabs), and equal orders come out reversed, so the mod tabs go first
    /// in reverse and the kept default tabs after them in their own iteration order: the dialog then shows the same
    /// left column as in default mode and the mod tabs, in <paramref name="modCodes"/>' order, as its right column.
    /// That fails only when no order can do it (a left tab ordered after 1.0, a mod tab code given an order, or
    /// fewer than <see cref="LeftColumn"/> default tabs, so mod tabs fill the left column); the list is the same,
    /// every tab is in one of the columns, and <see cref="ModModeLayout.Ideal"/> is false.
    /// </summary>
    /// <param name="defaultCodes">The default tabs' codes in the game's list's iteration order (index order).</param>
    /// <param name="modCodes">The mod tabs' codes in the order they should show.</param>
    /// <param name="listOrder">A code's <c>listOrder</c> in <c>config/creativetabs.json</c>, else <see cref="UnlistedOrder"/>.</param>
    public static ModModeLayout ForModMode(IReadOnlyList<string> defaultCodes, IReadOnlyList<string> modCodes, Func<string, double> listOrder)
    {
        var left = Order(defaultCodes, listOrder).Take(LeftColumn).ToList();
        var kept = new HashSet<string>(left, StringComparer.Ordinal);
        var iteration = new List<string>(modCodes.Count + left.Count);
        for (int i = modCodes.Count - 1; i >= 0; i--) iteration.Add(modCodes[i]);
        iteration.AddRange(defaultCodes.Where(kept.Contains));
        var shown = Order(iteration, listOrder);
        bool ideal = left.Count == LeftColumn && shown.SequenceEqual(left.Concat(modCodes), StringComparer.Ordinal);
        return new ModModeLayout(iteration, shown.Take(LeftColumn).ToList(), shown.Skip(LeftColumn).ToList(), ideal);
    }

    /// <summary>A display name as a lang entry's value: the game formats every value (<c>string.Format</c>), so
    /// braces are doubled to show as written.</summary>
    public static string LangValue(string name) => name.Replace("{", "{{").Replace("}", "}}");
}
