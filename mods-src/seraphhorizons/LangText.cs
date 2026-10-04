using System.Runtime.CompilerServices;
using SeraphHorizons.Mod.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace SeraphHorizons.Mod;

/// <summary>One edit to another mod's text: in <paramref name="Language"/>'s entry
/// <paramref name="Key"/>, the exact passage <paramref name="Old"/> becomes <paramref name="New"/>.
/// An empty <paramref name="Old"/> adds an entry the mod does not have. When the key only has a
/// wildcard entry (<c>blockdesc-ripple-*</c>), the edit is made to that text and set on the exact
/// key, which the game looks up first.</summary>
public sealed record LangEdit(string Language, string Key, string Old, string New);

/// <summary>A whole entry of another mod's text replaced: in <paramref name="Language"/>,
/// <paramref name="Key"/> reads <paramref name="Text"/>, whatever it said. For text rewritten
/// throughout, where an exact passage would be the whole entry.</summary>
public sealed record LangReplacement(string Language, string Key, string Text);

/// <summary>An entry of another mod's text removed from <paramref name="Language"/>, so the game
/// shows the English one: for a translation of text whose English has been rewritten.</summary>
public sealed record LangRemoval(string Language, string Key);

public static class LangText
{
    /// <summary>Applies <paramref name="edits"/> to each language's entries. Languages other than
    /// the current one load lazily, so this loads those few now, from the same assets the game
    /// would use. Safe to run twice: a singleplayer game runs it for the client and the server,
    /// which share the entries. An edit whose passage is gone logs a warning naming
    /// <paramref name="modName"/> and changes nothing.</summary>
    public static void Apply(IEnumerable<LangEdit> edits, string modName, ILogger logger)
    {
        foreach (var language in edits.GroupBy(edit => edit.Language))
        {
            if (!Lang.AvailableLanguages.TryGetValue(language.Key, out var translations))
                continue;
            var entries = translations.GetAllEntries();
            foreach (var edit in language)
            {
                string? text = entries.TryGetValue(edit.Key, out var exact) ? exact : translations.GetMatchingIfExists(edit.Key);
                if (text != null && text.Contains(edit.New))
                    continue;
                if (edit.Old.Length == 0)
                {
                    if (text != null)
                        logger.Warning($"[seraphhorizons] {edit.Language} lang entry {edit.Key} exists now; {modName} "
                                       + "changed, so it is not replaced");
                    else
                        entries[edit.Key] = edit.New;
                    continue;
                }
                if (text == null)
                {
                    logger.Warning($"[seraphhorizons] No {edit.Language} lang entry {edit.Key}; {modName} changed, so it "
                                   + "is not reworded");
                    continue;
                }
                if (!text.Contains(edit.Old))
                {
                    logger.Warning($"[seraphhorizons] {edit.Language} lang entry {edit.Key} no longer reads as "
                                   + $"expected; {modName} changed, so it is not reworded");
                    continue;
                }
                entries[edit.Key] = text.Replace(edit.Old, edit.New);
            }
        }
    }

    /// <summary>Applies <paramref name="replacements"/> (<see cref="LangEntries.Replace"/>). A key
    /// the mod no longer has logs a warning naming <paramref name="modName"/> and is not added.
    /// Safe to run twice, as <see cref="Apply"/>.</summary>
    public static void Replace(IEnumerable<LangReplacement> replacements, string modName, ILogger logger)
    {
        foreach (var language in replacements.GroupBy(replacement => replacement.Language))
        {
            if (!Lang.AvailableLanguages.TryGetValue(language.Key, out var translations))
                continue;
            var entries = translations.GetAllEntries();
            foreach (var replacement in language)
            {
                var change = LangEntries.Replace(entries, replacement.Key,
                    entries.ContainsKey(replacement.Key) ? null : translations.GetMatchingIfExists(replacement.Key),
                    replacement.Text);
                if (change == LangChange.Missing)
                    logger.Warning($"[seraphhorizons] No {replacement.Language} lang entry {replacement.Key}; {modName} "
                                   + "changed, so it is not replaced");
            }
        }
    }

    // The keys removed from each language's entries, so a second run (singleplayer's other side)
    // does not take its own removal for a key the mod dropped. Keyed by the entries themselves: a
    // reload of the languages makes new ones, from which nothing has been removed.
    private static readonly ConditionalWeakTable<IDictionary<string, string>, HashSet<string>> Removed = new();

    /// <summary>Applies <paramref name="removals"/> (<see cref="LangEntries.Remove"/>). Only exact
    /// entries can be removed, not wildcard ones. A key the mod no longer has logs a warning naming
    /// <paramref name="modName"/>. Safe to run twice, as <see cref="Apply"/>.</summary>
    public static void Remove(IEnumerable<LangRemoval> removals, string modName, ILogger logger)
    {
        foreach (var language in removals.GroupBy(removal => removal.Language))
        {
            if (!Lang.AvailableLanguages.TryGetValue(language.Key, out var translations))
                continue;
            var entries = translations.GetAllEntries();
            var removed = Removed.GetValue(entries, _ => []);
            foreach (var removal in language)
            {
                if (LangEntries.Remove(entries, removed, removal.Key) == LangChange.Missing)
                    logger.Warning($"[seraphhorizons] No {removal.Language} lang entry {removal.Key}; {modName} "
                                   + "changed, so there is nothing to remove");
            }
        }
    }
}
