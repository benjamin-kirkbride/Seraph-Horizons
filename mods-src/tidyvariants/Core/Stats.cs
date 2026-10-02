namespace SeraphHorizons.TidyVariants.Core;

/// <summary>Entry counts for one domain or tab. <see cref="After"/> is tiles with nothing expanded: visible plain
/// entries plus one per group with a member there.</summary>
public sealed record CountStat(string Key, int Before, int Hidden, int After);

public sealed record GroupSummary(string Id, int Members, GroupSource Source, string? Title, string RepresentativeCode, string? ShippedPattern);

/// <summary>Numbers for the Atlas report (#259).</summary>
public sealed class TidyStats
{
    public int EntriesBefore { get; private init; }
    /// <summary>Tiles in the full list with nothing expanded.</summary>
    public int EntriesAfter { get; private init; }
    public int Hidden { get; private init; }
    public int HiddenByVariantRule { get; private init; }
    public int HiddenByOverride { get; private init; }
    public int Groups { get; private init; }
    public int AutomaticGroups { get; private init; }
    public int OverrideGroups { get; private init; }
    /// <summary>Groups that exist (that wide) because a shipped handbook groupBy joined automatic groups.</summary>
    public int ShippedGroups { get; private init; }
    /// <summary>Entries that are members of a group (of two or more).</summary>
    public int GroupedEntries { get; private init; }
    public IReadOnlyList<CountStat> PerDomain { get; private init; } = [];
    public IReadOnlyList<CountStat> PerTab { get; private init; } = [];
    /// <summary>Largest groups, biggest first (ties by id).</summary>
    public IReadOnlyList<GroupSummary> LargestGroups { get; private init; } = [];
    /// <summary>Groups with no lang key (all automatic groups, plus override groups without <c>title</c>), biggest first:
    /// their tile is named after the representative.</summary>
    public IReadOnlyList<GroupSummary> UntitledGroups { get; private init; } = [];
    /// <summary>Every <see cref="GroupSource.Shipped"/> group, biggest first, with the pattern that merged it.</summary>
    public IReadOnlyList<GroupSummary> ShippedGroupList { get; private init; } = [];

    public static TidyStats Compute(TidyResolution res, int largest = 25)
    {
        var entries = res.Entries;
        int hiddenVariant = 0, hiddenOverride = 0, grouped = 0;
        var domains = new SortedDictionary<string, int[]>(StringComparer.Ordinal); // before, hidden, plain
        var tabs = new SortedDictionary<string, int[]>(StringComparer.Ordinal);
        var groupDomains = new HashSet<(string, int)>();
        var groupTabs = new HashSet<(string, int)>();

        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            var h = res.HideReasonOf(i);
            if (h == HideReason.Variant) hiddenVariant++;
            else if (h == HideReason.Override) hiddenOverride++;
            int g = res.GroupOf(i);
            if (g >= 0) grouped++;

            Count(domains, e.Domain, h, g, groupDomains);
            foreach (var t in e.Tabs) Count(tabs, t, h, g, groupTabs);
        }

        static void Count(SortedDictionary<string, int[]> into, string key, HideReason h, int g, HashSet<(string, int)> groupsSeen)
        {
            if (!into.TryGetValue(key, out var c)) into[key] = c = new int[3];
            c[0]++;
            if (h != HideReason.None) c[1]++;
            else if (g < 0) c[2]++;
            else if (groupsSeen.Add((key, g))) c[2]++;
        }

        GroupSummary Summary(TidyGroup g) => new(g.Id, g.Members.Count, g.Source, g.Title, entries[g.Representative].ToString(), g.ShippedPattern);
        var bySize = res.Groups.OrderByDescending(g => g.Members.Count).ThenBy(g => g.Id, StringComparer.Ordinal).ToList();

        int visible = entries.Count - hiddenVariant - hiddenOverride;
        return new TidyStats
        {
            EntriesBefore = entries.Count,
            EntriesAfter = visible - grouped + res.Groups.Count,
            Hidden = hiddenVariant + hiddenOverride,
            HiddenByVariantRule = hiddenVariant,
            HiddenByOverride = hiddenOverride,
            Groups = res.Groups.Count,
            AutomaticGroups = res.Groups.Count(g => g.Source == GroupSource.Automatic),
            OverrideGroups = res.Groups.Count(g => g.Source == GroupSource.Override),
            ShippedGroups = res.Groups.Count(g => g.Source == GroupSource.Shipped),
            GroupedEntries = grouped,
            PerDomain = domains.Select(kv => new CountStat(kv.Key, kv.Value[0], kv.Value[1], kv.Value[2])).ToList(),
            PerTab = tabs.Select(kv => new CountStat(kv.Key, kv.Value[0], kv.Value[1], kv.Value[2])).ToList(),
            LargestGroups = bySize.Take(largest).Select(Summary).ToList(),
            UntitledGroups = bySize.Where(g => g.Title is null).Select(Summary).ToList(),
            ShippedGroupList = bySize.Where(g => g.Source == GroupSource.Shipped).Select(Summary).ToList(),
        };
    }
}
