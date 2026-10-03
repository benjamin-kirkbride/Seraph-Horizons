using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace SeraphHorizons.SeraphTweaks;

/// <summary>One edit to another mod's text: in <paramref name="Language"/>'s entry
/// <paramref name="Key"/>, the exact passage <paramref name="Old"/> becomes <paramref name="New"/>.
/// An empty <paramref name="Old"/> adds an entry the mod does not have. When the key only has a
/// wildcard entry (<c>blockdesc-ripple-*</c>), the edit is made to that text and set on the exact
/// key, which the game looks up first.</summary>
public sealed record LangEdit(string Language, string Key, string Old, string New);

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
                        logger.Warning($"[seraphtweaks] {edit.Language} lang entry {edit.Key} exists now; {modName} "
                                       + "changed, so it is not replaced");
                    else
                        entries[edit.Key] = edit.New;
                    continue;
                }
                if (text == null)
                {
                    logger.Warning($"[seraphtweaks] No {edit.Language} lang entry {edit.Key}; {modName} changed, so it "
                                   + "is not reworded");
                    continue;
                }
                if (!text.Contains(edit.Old))
                {
                    logger.Warning($"[seraphtweaks] {edit.Language} lang entry {edit.Key} no longer reads as "
                                   + $"expected; {modName} changed, so it is not reworded");
                    continue;
                }
                entries[edit.Key] = text.Replace(edit.Old, edit.New);
            }
        }
    }
}
