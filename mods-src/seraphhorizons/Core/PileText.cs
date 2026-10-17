using System.Globalization;

namespace SeraphHorizons.Mod.Core;

/// <summary>
/// The handbook's pile line (<c>PileHandbook.cs</c>, README "Big piles"): whether an item piles on
/// the ground and how. Game-independent, so tests/ runs it without the game; the lang function is
/// a parameter (<c>Lang.Get</c> in the game).
/// </summary>
public static class PileText
{
    /// <summary>How the item goes on the ground.</summary>
    public enum Kind
    {
        /// <summary>A pile: the game's <c>Stacking</c> ground storage, or an item pile (coal).</summary>
        Pile,
        /// <summary>The game's <c>Messy12</c>: a loose heap of up to 12, one block high.</summary>
        Loose,
        /// <summary>Set down one by one: centre, halves, against a wall or quadrants.</summary>
        Placed,
    }

    /// <summary>What the line says. <paramref name="MaxHigh"/> is the most piles one column
    /// holds, 0 for no limit.</summary>
    public readonly record struct Facts(Kind Kind, int Capacity, int Transfer, int Bulk, bool RequiresCtrl, bool BuildsUp, int MaxHigh = 0);

    /// <summary>A number as the line shows it: <c>2,048</c>.</summary>
    public static string Number(int n) => n.ToString("#,0", CultureInfo.InvariantCulture);

    /// <summary>The line: what it is and holds, then for a pile its clicks, its height and its keys.</summary>
    public static string Line(Facts f, Func<string, object[], string> lang)
    {
        if (f.Kind == Kind.Placed)
            return lang("seraphhorizons:piles-handbook-placed", [Number(f.Capacity)]);
        if (f.Kind == Kind.Loose)
            return lang("seraphhorizons:piles-handbook-loose", [Number(f.Capacity)]);
        var parts = new List<string> { lang("seraphhorizons:piles-handbook-pile", [Number(f.Capacity)]) };
        if (f.Transfer > 0) parts.Add(lang("seraphhorizons:piles-handbook-perclick", [Number(f.Transfer)]));
        if (f.Bulk > 0 && f.Bulk != f.Transfer) parts.Add(lang("seraphhorizons:piles-handbook-perctrlclick", [Number(f.Bulk)]));
        var sentences = new List<string> { string.Join(", ", parts) + "." };
        sentences.Add(!f.BuildsUp
            ? lang("seraphhorizons:piles-handbook-onehigh", [])
            : f.MaxHigh > 0
                ? lang("seraphhorizons:piles-handbook-higher-max", [Number(f.MaxHigh)])
                : lang("seraphhorizons:piles-handbook-higher", []));
        sentences.Add(lang(f.RequiresCtrl ? "seraphhorizons:piles-handbook-ctrl" : "seraphhorizons:piles-handbook-shift", []));
        return string.Join(" ", sentences);
    }
}
