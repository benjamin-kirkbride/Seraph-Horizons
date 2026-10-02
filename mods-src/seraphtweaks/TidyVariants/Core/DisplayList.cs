namespace SeraphHorizons.SeraphTweaks.TidyVariants.Core;

[Flags]
public enum DisplayFlags
{
    /// <summary>A plain entry (ungrouped, a singleton, or the only surviving member of its group).</summary>
    None = 0,
    /// <summary>A collapsed group tile: <see cref="DisplayItem.Entry"/> is the representative among the matching members.</summary>
    Tile = 1,
    /// <summary>A member of an expanded group, shown inline.</summary>
    ExpandedMember = 2,
    /// <summary>First member of an expanded group's run (for the tinted border).</summary>
    GroupStart = 4,
    /// <summary>Last member of an expanded group's run.</summary>
    GroupEnd = 8,
    /// <summary>Set on members expanded because theirs was the only group left.</summary>
    AutoExpanded = 16,
}

/// <summary>One slot of the creative grid.</summary>
public readonly struct DisplayItem(int entry, int group, int membersStart, int membersCount, DisplayFlags flags)
{
    /// <summary>Entry index to draw (for a tile, the representative; plain click takes this one).</summary>
    public int Entry { get; } = entry;
    /// <summary>Group index for tiles and expanded members; -1 for plain entries.</summary>
    public int Group { get; } = group;
    /// <summary>Range in <see cref="DisplayListBuilder.Members"/> of the group's matching members (tiles and expanded members).</summary>
    public int MembersStart { get; } = membersStart;
    public int MembersCount { get; } = membersCount;
    public DisplayFlags Flags { get; } = flags;
    public bool IsTile => (Flags & DisplayFlags.Tile) != 0;
    public bool IsExpandedMember => (Flags & DisplayFlags.ExpandedMember) != 0;

    public override string ToString() => $"{Entry} g{Group} [{MembersStart}+{MembersCount}] {Flags}";
}

/// <summary>
/// Builds the creative grid after search: hidden entries dropped, each group collapsed into one tile of its
/// matching members at the position of its first match, expanded groups inline. Reuses its buffers, so a
/// <see cref="Build"/> per keystroke allocates nothing once warm. Not thread-safe: one builder per GUI.
/// </summary>
public sealed class DisplayListBuilder
{
    readonly TidyResolution res;
    readonly int[] count, stamp, offset, fill, best;
    readonly List<int> touched = [];
    readonly List<DisplayItem> items = [];
    int[] members;
    int cur;

    public DisplayListBuilder(TidyResolution resolution)
    {
        res = resolution;
        int g = resolution.Groups.Count;
        count = new int[g]; stamp = new int[g]; offset = new int[g]; fill = new int[g]; best = new int[g];
        members = new int[Math.Max(16, resolution.Entries.Count)];
    }

    /// <summary>The grid from the last <see cref="Build"/>. Valid until the next call.</summary>
    public IReadOnlyList<DisplayItem> Items => items;

    /// <summary>Member buffer the items' ranges point into (creative order within each group).</summary>
    public ReadOnlySpan<int> Members => members;

    public ReadOnlySpan<int> MembersOf(in DisplayItem item) => members.AsSpan(item.MembersStart, item.MembersCount);

    /// <summary>The group expanded because it was the only one left, or -1.</summary>
    public int AutoExpandedGroup { get; private set; } = -1;

    /// <param name="surviving">Entry indices that passed the search filter, in display order (the full tab
    /// list when there is no search). Hidden entries in it are dropped.</param>
    /// <param name="expanded">Groups the player expanded (group indices), or null.</param>
    public void Build(ReadOnlySpan<int> surviving, IReadOnlySet<int>? expanded = null)
    {
        if (surviving.Length > members.Length) members = new int[surviving.Length];
        items.Clear();
        touched.Clear();
        AutoExpandedGroup = -1;
        if (++cur == int.MaxValue) { Array.Clear(stamp); cur = 1; }

        // Pass 1: count matching members per group.
        foreach (int i in surviving)
        {
            if (res.IsHidden(i)) continue;
            int g = res.groupOf[i];
            if (g < 0) continue;
            if (stamp[g] != cur) { stamp[g] = cur; count[g] = 0; touched.Add(g); }
            count[g]++;
        }

        // Member ranges for groups with two or more matches; find a lone group to auto-expand.
        int pos = 0, multi = 0, lone = -1;
        foreach (int g in touched)
        {
            if (count[g] < 2) continue;
            multi++; lone = g;
            offset[g] = pos; fill[g] = pos; best[g] = -1;
            pos += count[g];
        }
        if (multi == 1) AutoExpandedGroup = lone;

        // Pass 2: fill member ranges and pick each group's representative among its matches.
        foreach (int i in surviving)
        {
            if (res.IsHidden(i)) continue;
            int g = res.groupOf[i];
            if (g < 0 || count[g] < 2) continue;
            members[fill[g]++] = i;
            if (best[g] < 0 || res.repRank[i] < res.repRank[best[g]]) best[g] = i;
        }

        // Pass 3: emit, each group once at its first match. count[g] = -1 marks a group as emitted.
        foreach (int i in surviving)
        {
            if (res.IsHidden(i)) continue;
            int g = res.groupOf[i];
            if (g < 0 || stamp[g] != cur || count[g] == 1) { items.Add(new DisplayItem(i, -1, 0, 0, DisplayFlags.None)); continue; }
            if (count[g] < 0) continue;
            int n = count[g];
            count[g] = -1;
            bool auto = g == AutoExpandedGroup;
            if (auto || (expanded?.Contains(g) ?? false))
            {
                var extra = auto ? DisplayFlags.AutoExpanded : DisplayFlags.None;
                for (int k = 0; k < n; k++)
                {
                    var f = DisplayFlags.ExpandedMember | extra;
                    if (k == 0) f |= DisplayFlags.GroupStart;
                    if (k == n - 1) f |= DisplayFlags.GroupEnd;
                    items.Add(new DisplayItem(members[offset[g] + k], g, offset[g], n, f));
                }
            }
            else items.Add(new DisplayItem(best[g], g, offset[g], n, DisplayFlags.Tile));
        }
    }
}
