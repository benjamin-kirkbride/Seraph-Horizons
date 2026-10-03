namespace SeraphHorizons.Mod.CreativeModTabs.Core;

/// <summary>What one mod contributes to the assets, per asset domain: how many blocktype and itemtype
/// files (<see cref="Defining"/>) and how many assets in all (<see cref="Total"/>).</summary>
public readonly record struct DomainAssets(int Defining, int Total);

/// <summary>A loaded mod as <see cref="DomainOwners"/> sees it.</summary>
/// <param name="ModId">The modid.</param>
/// <param name="Name">Its display name (modinfo <c>name</c>).</param>
/// <param name="Domains">Asset domain to what the mod's own folder or zip holds in it.</param>
public sealed record ModAssets(string ModId, string Name, IReadOnlyDictionary<string, DomainAssets> Domains);

/// <summary>Whom a domain's creative stacks are filed under: a mod (<see cref="Key"/> is its modid),
/// the base game (<see cref="IsGame"/>, key <c>game</c>), or a domain no mod owns (the raw domain, which
/// is also the name).</summary>
public sealed record TabOwner(string Key, string Name, bool IsGame);

/// <summary>
/// Maps asset domains to the mod that owns them (docs/variant-grouping/creative-mod-tabs.md, "Attribution"). A collectible's
/// code takes the domain of the asset file that defines it, so the owner of a domain is, in this order:
/// <list type="number">
/// <item>the base game, for <c>game</c> (whatever other mods add under <c>game:</c> stays there);</item>
/// <item>the mod whose modid is the domain;</item>
/// <item>the mod with the most blocktype and itemtype files in the domain (the one that defines its
/// blocks and items, e.g. VintageEngineering for <c>vinteng</c>);</item>
/// <item>the mod with the most assets of any kind in it;</item>
/// <item>nobody: the raw domain stands for itself.</item>
/// </list>
/// Ties go to the smaller modid (ordinal), so the result never depends on load order.
/// </summary>
public static class DomainOwners
{
    public const string GameDomain = "game";

    public static TabOwner Resolve(string domain, IReadOnlyList<ModAssets> mods, string gameName)
    {
        if (string.Equals(domain, GameDomain, StringComparison.Ordinal)) return new TabOwner(GameDomain, gameName, true);
        ModAssets? byId = null;
        foreach (var m in mods)
            if (string.Equals(m.ModId, domain, StringComparison.OrdinalIgnoreCase) && (byId is null || string.CompareOrdinal(m.ModId, byId.ModId) < 0))
                byId = m;
        var owner = byId ?? Best(domain, mods, a => a.Defining) ?? Best(domain, mods, a => a.Total);
        return owner is null ? new TabOwner(domain, domain, false) : new TabOwner(owner.ModId, NameOf(owner), false);
    }

    public static Dictionary<string, TabOwner> ResolveAll(IEnumerable<string> domains, IReadOnlyList<ModAssets> mods, string gameName)
    {
        var result = new Dictionary<string, TabOwner>(StringComparer.Ordinal);
        foreach (var d in domains)
            if (!result.ContainsKey(d)) result[d] = Resolve(d, mods, gameName);
        return result;
    }

    static ModAssets? Best(string domain, IReadOnlyList<ModAssets> mods, Func<DomainAssets, int> count)
    {
        ModAssets? best = null;
        int bestCount = 0;
        foreach (var m in mods)
        {
            if (!m.Domains.TryGetValue(domain, out var a)) continue;
            int c = count(a);
            if (c <= 0) continue;
            if (best is null || c > bestCount || (c == bestCount && string.CompareOrdinal(m.ModId, best.ModId) < 0))
            {
                best = m;
                bestCount = c;
            }
        }
        return best;
    }

    static string NameOf(ModAssets m) => string.IsNullOrWhiteSpace(m.Name) ? m.ModId : m.Name.Trim();
}

/// <summary>One mod tab as the server announces it: both sides build the tab from this.</summary>
/// <param name="Code">The creative tab code (<see cref="ModTabPlanner.CodeFor"/>).</param>
/// <param name="Name">Display name: the mod's name, or the raw domain. The client shows its own lang
/// text for the base game's tab instead (<see cref="IsGame"/>).</param>
/// <param name="IsGame">The base game's tab (domain <c>game</c>), pinned first.</param>
/// <param name="Domains">The asset domains whose stacks the tab holds, sorted (ordinal).</param>
/// <param name="Count">How many stacks it holds.</param>
/// <param name="Hash"><see cref="ModTabPlanner.Hash"/> of its stacks' collectible codes, in order.</param>
public sealed record ModTabSpec(string Code, string Name, bool IsGame, IReadOnlyList<string> Domains, int Count, uint Hash);

/// <summary>One creative stack, in the game's creative order: its identity (for dedupe), its collectible
/// code and the code's domain.</summary>
public readonly record struct StackRef(string Key, string Code, string Domain);

/// <summary>
/// Plans the mod tabs from the creative stacks (docs/variant-grouping/creative-mod-tabs.md). Game-independent, so tests/ runs
/// it without the game.
///
/// Input is every stack the game puts into any default creative tab, in the order
/// <c>InventoryPlayerCreative.UpdateFromWorld</c> gathers them (blocks by material, then items by tool; per
/// collectible its plain stack, then its stack lists). <see cref="Dedupe"/> keeps the first of each identity,
/// so a stack listed in several default tabs appears once. Each remaining stack goes to the tab of its domain's
/// owner (<see cref="DomainOwners"/>), keeping that order inside the tab.
/// </summary>
public static class ModTabPlanner
{
    public const string CodePrefix = "seraphhorizons-modtab-";

    /// <summary>The tab code of an owner. A raw domain can't collide with a modid: a mod with that
    /// modid would own the domain.</summary>
    public static string CodeFor(TabOwner owner) => CodePrefix + (owner.IsGame ? DomainOwners.GameDomain : owner.Key);

    /// <summary>Indices of the first stack of each <see cref="StackRef.Key"/>, in order.</summary>
    public static List<int> Dedupe(IReadOnlyList<StackRef> stacks)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var keep = new List<int>(stacks.Count);
        for (int i = 0; i < stacks.Count; i++)
            if (seen.Add(stacks[i].Key)) keep.Add(i);
        return keep;
    }

    /// <summary>
    /// The tabs for deduplicated <paramref name="stacks"/>: one per owner with at least one stack, the base game's
    /// first, then by display name (case-insensitive), then by code. A domain missing from
    /// <paramref name="owners"/> is its own owner.
    /// </summary>
    public static List<ModTabSpec> Plan(IReadOnlyList<StackRef> stacks, IReadOnlyDictionary<string, TabOwner> owners)
    {
        var byCode = new Dictionary<string, (TabOwner Owner, SortedSet<string> Domains, List<string> Codes)>(StringComparer.Ordinal);
        foreach (var s in stacks)
        {
            var owner = owners.TryGetValue(s.Domain, out var o) ? o : new TabOwner(s.Domain, s.Domain, false);
            string code = CodeFor(owner);
            if (!byCode.TryGetValue(code, out var tab))
                byCode[code] = tab = (owner, new SortedSet<string>(StringComparer.Ordinal), []);
            tab.Domains.Add(s.Domain);
            tab.Codes.Add(s.Code);
        }
        return byCode
            .Select(kv => new ModTabSpec(kv.Key, kv.Value.Owner.Name, kv.Value.Owner.IsGame, kv.Value.Domains.ToList(),
                kv.Value.Codes.Count, Hash(kv.Value.Codes)))
            .OrderBy(t => t, TabOrder.Instance)
            .ToList();
    }

    /// <summary>For each stack, the index of the tab whose <see cref="ModTabSpec.Domains"/> holds its domain, or -1.</summary>
    public static int[] Assign(IReadOnlyList<StackRef> stacks, IReadOnlyList<ModTabSpec> tabs)
    {
        var tabOf = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int t = 0; t < tabs.Count; t++)
            foreach (var d in tabs[t].Domains)
                tabOf.TryAdd(d, t);
        var result = new int[stacks.Count];
        for (int i = 0; i < stacks.Count; i++)
            result[i] = tabOf.TryGetValue(stacks[i].Domain, out int t) ? t : -1;
        return result;
    }

    /// <summary>
    /// Whether this side's stacks fill the announced <paramref name="tabs"/> exactly as on the server: every stack
    /// has a tab, and every tab has the announced count and hash. Null if so, else what differs. A tab shown on
    /// the client must hold the same slots as the server's, which resolves clicks by tab index and slot id.
    /// </summary>
    public static string? Verify(IReadOnlyList<StackRef> stacks, IReadOnlyList<ModTabSpec> tabs, int[] assignment)
    {
        var codes = new List<string>[tabs.Count];
        for (int t = 0; t < tabs.Count; t++) codes[t] = [];
        for (int i = 0; i < stacks.Count; i++)
        {
            if (assignment[i] < 0) return $"no tab for domain {stacks[i].Domain} ({stacks[i].Code})";
            codes[assignment[i]].Add(stacks[i].Code);
        }
        for (int t = 0; t < tabs.Count; t++)
        {
            if (codes[t].Count != tabs[t].Count) return $"tab {tabs[t].Code}: {codes[t].Count} stacks here, {tabs[t].Count} on the server";
            if (Hash(codes[t]) != tabs[t].Hash) return $"tab {tabs[t].Code}: its stacks differ from the server's";
        }
        return null;
    }

    /// <summary>FNV-1a (32 bit) over the codes' UTF-16 units, each code followed by a separator.</summary>
    public static uint Hash(IEnumerable<string> codes)
    {
        uint h = 2166136261;
        foreach (var code in codes)
        {
            foreach (char c in code) h = (h ^ c) * 16777619;
            h = (h ^ '\n') * 16777619;
        }
        return h;
    }

    /// <summary>Base game first, then display name ignoring case, then code.</summary>
    public sealed class TabOrder : IComparer<ModTabSpec>
    {
        public static readonly TabOrder Instance = new();

        public int Compare(ModTabSpec? a, ModTabSpec? b)
        {
            if (ReferenceEquals(a, b)) return 0;
            if (a is null) return -1;
            if (b is null) return 1;
            if (a.IsGame != b.IsGame) return a.IsGame ? -1 : 1;
            int c = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            return c != 0 ? c : string.CompareOrdinal(a.Code, b.Code);
        }
    }
}
