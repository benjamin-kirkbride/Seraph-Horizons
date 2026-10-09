namespace SeraphHorizons.Mod.Core;

/// <summary>
/// BetterLoot+'s gear parts (<c>GearPartsRemoved</c>): which loot drops are gear parts, and what
/// each becomes. Four parts made one rusty gear, so a drop of parts becomes rusty gears at a quarter
/// of its average (and of its variance): the same rusty gears, on average, come in. They are added
/// to the creature's own rusty gear drop when it has one, so a creature still lists one rusty gear
/// drop; otherwise the part drop itself becomes the rusty gear drop.
/// Game-independent, so tests/ runs it without the game.
/// </summary>
public static class GearPartDropRules
{
    /// <summary>BetterLoot+'s gear part item.</summary>
    public const string GearPart = "betterloot:gearpart";

    /// <summary>What a gear part drop becomes.</summary>
    public const string RustyGear = "game:gear-rusty";

    /// <summary>Gear parts in a rusty gear (BetterLoot+'s <c>recipes/grid/rustygear.json</c>).</summary>
    public const int PartsPerGear = 4;

    /// <summary>One drop as BetterLoot+'s config has it.</summary>
    public readonly record struct Drop(string? Code, double Avg, double Var);

    /// <summary>A drop to set to this code, average and variance, by its index in the list.</summary>
    public readonly record struct Edit(int Index, string Code, double Avg, double Var);

    /// <summary>Whether a drop of this code is a gear part. Case and surrounding spaces do not
    /// matter, as BetterLoot+ trims the code and the game lowercases it; a code without a domain is
    /// in <c>game</c>, so never a gear part.</summary>
    public static bool IsGearPart(string? code) =>
        code != null && string.Equals(code.Trim(), GearPart, StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether a drop of this code is the rusty gear (<c>gear-rusty</c> is in <c>game</c>).</summary>
    public static bool IsRustyGear(string? code)
    {
        if (code == null)
            return false;
        code = code.Trim();
        return string.Equals(code, RustyGear, StringComparison.OrdinalIgnoreCase)
               || string.Equals(code, "gear-rusty", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>What to change in one creature's drops: the edits (the rusty gear drop that takes the
    /// parts in, with its new totals) and the indices of the part drops to remove, highest first.
    /// Nothing when there is no part drop.</summary>
    public static (List<Edit> Edits, List<int> Removed) Plan(IReadOnlyList<Drop> drops)
    {
        int target = -1;
        double avg = 0, var = 0;
        for (int i = 0; i < drops.Count; i++)
            if (IsRustyGear(drops[i].Code))
            {
                target = i;
                avg = drops[i].Avg;
                var = drops[i].Var;
                break;
            }
        var removed = new List<int>();
        bool any = false;
        for (int i = 0; i < drops.Count; i++)
        {
            if (!IsGearPart(drops[i].Code))
                continue;
            any = true;
            avg += drops[i].Avg / PartsPerGear;
            var += drops[i].Var / PartsPerGear;
            if (target < 0)
                target = i;
            else
                removed.Add(i);
        }
        if (!any)
            return ([], []);
        removed.Reverse();
        return ([new Edit(target, drops[target].Code is { } c && IsRustyGear(c) ? c : RustyGear, avg, var)], removed);
    }
}
