using System.Collections;
using System.Reflection;
using Vintagestory.API.Config;

namespace SeraphHorizons.RecipeExport.Items;

/// <summary>
/// <see cref="Lang.GetMatchingIfExists"/> for keys that mostly have no entry, as the
/// "-handbooktitle-"/"-handbooktext-" keys of every exported item do. On a miss the engine tries
/// every wildcard key and runs every regex key (thousands in the pack), twice: a full export
/// spent most of its item time there. This answers "no entry can match" from an index of those
/// keys and calls the engine only when one might, so the result is always the engine's own.
/// <para>
/// The index is conservative: an exact key, a wildcard key whose prefix the key starts with, or a
/// regex key whose literal head and tail the key has, counts as a possible match. When the
/// translation services do not have the members it reads (another game version), every key
/// goes to the engine. Build one per export, inside its <see cref="EnglishLocale"/>.
/// </para>
/// </summary>
internal sealed class LangIndex
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;

    // The engine builds a regex key as "^" + key.Replace("*", "(.*)") + "$", unescaped.
    private static readonly char[] RegexSpecial = { '\\', '.', '^', '$', '|', '?', '*', '+', '(', ')', '[', ']', '{', '}', '#' };

    private readonly List<Service>? _services;

    private sealed class Service
    {
        public required IDictionary Entries;
        public readonly HashSet<string> WildcardPrefixes = new(StringComparer.Ordinal);
        // Literal head of a regex key -> the literal tails that go with it.
        public readonly Dictionary<string, List<string>> RegexHeads = new(StringComparer.Ordinal);
    }

    public LangIndex()
    {
        var services = new List<Service>();
        // Lang.GetMatchingIfExists reads the current locale's service, then the default's.
        foreach (var locale in new[] { Lang.CurrentLocale, Lang.DefaultLocale }.Distinct())
        {
            if (locale == null || !Lang.AvailableLanguages.TryGetValue(locale, out var ts)) return;
            var service = Read(ts);
            if (service == null) return;
            services.Add(service);
        }
        _services = services;
    }

    /// <summary>Exactly <c>Lang.GetMatchingIfExists(key)</c>, for a key with a domain.</summary>
    public string? GetMatchingIfExists(string key)
    {
        if (_services != null && key.Contains(':') && !_services.Any(s => MightMatch(s, key))) return null;
        return Lang.GetMatchingIfExists(key);
    }

    private static Service? Read(ITranslationService ts)
    {
        // Loads a lazily loaded service, as the engine's own lookups do first.
        ts.HasTranslation("seraphexport:langindex-probe", findWildcarded: false);
        var type = ts.GetType();
        if (type.GetField("entryCache", Instance)?.GetValue(ts) is not IDictionary entries ||
            type.GetField("wildcardCache", Instance)?.GetValue(ts) is not IDictionary wildcards ||
            type.GetField("regexCache", Instance)?.GetValue(ts) is not IDictionary regexes)
            return null;
        var service = new Service { Entries = entries };
        foreach (var prefix in wildcards.Keys)
            if (prefix is string p) service.WildcardPrefixes.Add(p);
            else return null;
        foreach (var source in regexes.Keys)
        {
            if (source is not string text) return null;
            var (head, tail) = Literals(text);
            if (!service.RegexHeads.TryGetValue(head, out var tails)) service.RegexHeads[head] = tails = new();
            tails.Add(tail);
        }
        return service;
    }

    /// <summary>
    /// What any string the regex of key <paramref name="text"/> matches must start and end with.
    /// Empty when the pattern has an alternation, so every key passes.
    /// </summary>
    private static (string Head, string Tail) Literals(string text)
    {
        if (text.Contains('|')) return ("", "");
        int head = 0;
        while (head < text.Length && Array.IndexOf(RegexSpecial, text[head]) < 0) head++;
        // A quantifier after the run makes its last character optional.
        if (head < text.Length && head > 0 && text[head] is '?' or '*' or '+' or '{') head--;
        int tail = text.Length;
        while (tail > head && Array.IndexOf(RegexSpecial, text[tail - 1]) < 0) tail--;
        // A backslash before the run escapes its first character, which then is not literal text.
        if (tail > 0 && tail < text.Length && text[tail - 1] == '\\') tail++;
        return (text[..head], text[tail..]);
    }

    private static bool MightMatch(Service s, string key)
    {
        if (s.Entries.Contains(key)) return true;
        for (int i = 0; i <= key.Length; i++)
        {
            var prefix = key[..i];
            if (s.WildcardPrefixes.Contains(prefix)) return true;
            // "$" also matches before a final newline.
            if (s.RegexHeads.TryGetValue(prefix, out var tails) &&
                tails.Any(t => key.EndsWith(t, StringComparison.Ordinal) || key.EndsWith(t + "\n", StringComparison.Ordinal)))
                return true;
        }
        return false;
    }
}
