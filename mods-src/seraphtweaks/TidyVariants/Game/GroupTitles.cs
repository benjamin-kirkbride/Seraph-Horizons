using System.Runtime.CompilerServices;
using SeraphHorizons.SeraphTweaks.TidyVariants.Core;
using Vintagestory.API.Config;

namespace SeraphHorizons.SeraphTweaks.TidyVariants;

/// <summary>
/// The display title of a group, shared by the creative tooltip and the handbook: the group's lang title if it
/// has a translation, else a title derived from its members' names (<see cref="TitleDeriver"/>), else the
/// representative's name. Derived titles are computed on first use and cached per bridge (one per side and
/// world) and language. Names come from <c>ItemStack.GetName()</c>, which works on both sides.
/// </summary>
public static class GroupTitles
{
    sealed class Cache(int groups, string locale)
    {
        public readonly string Locale = locale;
        public readonly string?[] Derived = new string?[groups];
        public readonly bool[] Done = new bool[groups];
    }

    static readonly ConditionalWeakTable<TidyBridge, Cache> Caches = new();

    /// <summary>The title to show for group <paramref name="group"/> (index into the resolution's groups).
    /// <paramref name="fallbackName"/> replaces the creative representative's name as the last resort (the
    /// handbook passes its representative page's name).</summary>
    public static string Of(TidyBridge bridge, int group, string? fallbackName = null)
    {
        var g = bridge.Resolution.Groups[group];
        return LangTitle(g) ?? Derived(bridge, group) ?? fallbackName ?? NameOf(bridge, g.Representative) ?? g.Id;
    }

    /// <summary>The group's lang title, when it has one with a translation.</summary>
    public static string? LangTitle(TidyGroup g) =>
        g.Title is { Length: > 0 } t && Lang.HasTranslation(t, findWildcarded: false, logErrors: false) ? Lang.Get(t) : null;

    /// <summary>The title derived from the members' names, or null when they share none worth showing.</summary>
    public static string? Derived(TidyBridge bridge, int group)
    {
        string locale = Lang.CurrentLocale ?? "en";
        var cache = Caches.GetValue(bridge, b => new Cache(b.Resolution.Groups.Count, locale));
        if (cache.Locale != locale)
        {
            Caches.AddOrUpdate(bridge, cache = new Cache(bridge.Resolution.Groups.Count, locale));
        }
        lock (cache)
        {
            if (cache.Done[group]) return cache.Derived[group];
            var g = bridge.Resolution.Groups[group];
            var names = new string?[g.Members.Count];
            for (int i = 0; i < names.Length; i++) names[i] = NameOf(bridge, g.Members[i]);
            string? title = TitleDeriver.Derive(names, NameOf(bridge, g.Representative), TitleDeriver.IsHeadLast(locale));
            cache.Derived[group] = title;
            cache.Done[group] = true;
            return title;
        }
    }

    /// <summary>An entry's display name, or null if its collectible throws.</summary>
    public static string? NameOf(TidyBridge bridge, int entry)
    {
        if ((uint)entry >= (uint)bridge.Count) return null;
        try { return bridge.StackOf(entry).GetName(); }
        catch { return null; }
    }
}
