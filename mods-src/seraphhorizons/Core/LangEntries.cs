namespace SeraphHorizons.Mod.Core;

/// <summary>What a whole-entry change did to one language's entries.</summary>
public enum LangChange
{
    /// <summary>The entries were changed.</summary>
    Changed,

    /// <summary>Already as asked: done earlier, in singleplayer by the other side.</summary>
    AlreadyDone,

    /// <summary>The key is not there to change: the mod changed.</summary>
    Missing,
}

/// <summary>
/// Whole-entry changes to one language's lang entries (<c>LangText.Replace</c> and
/// <c>LangText.Remove</c>), on the entry dictionary the game looks exact keys up in.
/// Game-independent, so tests/ runs it without the game.
/// </summary>
public static class LangEntries
{
    /// <summary>Sets <paramref name="key"/> to <paramref name="text"/>, whatever it said. A key with
    /// only a wildcard entry (<paramref name="wildcardText"/>, null if none) gets an exact entry,
    /// which the game looks up first. A key with neither is <see cref="LangChange.Missing"/>.</summary>
    public static LangChange Replace(IDictionary<string, string> entries, string key, string? wildcardText, string text)
    {
        string? current = entries.TryGetValue(key, out var exact) ? exact : wildcardText;
        if (current == null)
            return LangChange.Missing;
        if (current == text && exact != null)
            return LangChange.AlreadyDone;
        entries[key] = text;
        return LangChange.Changed;
    }

    /// <summary>Removes <paramref name="key"/>, so the game shows the default language's (English)
    /// text for it. <paramref name="removed"/> remembers the keys removed from these entries, so a
    /// second run (singleplayer's client and server share the entries) is
    /// <see cref="LangChange.AlreadyDone"/>, not <see cref="LangChange.Missing"/>.</summary>
    public static LangChange Remove(IDictionary<string, string> entries, ISet<string> removed, string key)
    {
        if (entries.Remove(key))
        {
            removed.Add(key);
            return LangChange.Changed;
        }
        return removed.Contains(key) ? LangChange.AlreadyDone : LangChange.Missing;
    }
}
