namespace SeraphHorizons.Mod.Core;

/// <summary>Where the game keeps a lang entry, by its key (1.22.7 <c>TranslationService.LoadEntry</c>).</summary>
public enum LangKeyStore
{
    /// <summary>No <c>*</c>: the exact entries, which <c>GetAllEntries</c> returns.</summary>
    Exact,

    /// <summary>One <c>*</c>, at the end: the wildcard entries, keyed by the text before it; any
    /// key that starts with that text matches.</summary>
    Wildcard,

    /// <summary>Any other <c>*</c>: the regex entries, keyed by the whole key, each <c>*</c> matching
    /// anything.</summary>
    Regex,
}

/// <summary>
/// Lang keys with <c>*</c> in them, which another mod's text uses for a family of blocks
/// (<c>loggingmod:block-handbooktext-loggingmod:sawhorse-*</c>). The game keeps those apart from
/// the exact entries, so replacing one means finding it where the game put it. Game-independent,
/// so tests/ runs it without the game.
/// </summary>
public static class LangPatternKeys
{
    public static LangKeyStore StoreOf(string key)
    {
        int stars = key.Count(c => c == '*');
        return stars == 0 ? LangKeyStore.Exact
            : stars == 1 && key.EndsWith('*') ? LangKeyStore.Wildcard
            : LangKeyStore.Regex;
    }

    /// <summary>The key the game files the entry under in its <see cref="StoreOf"/> store. Keys
    /// must carry their domain (<c>loggingmod:...</c>), as the game files them.</summary>
    public static string StoredKey(string key) => StoreOf(key) == LangKeyStore.Wildcard ? key.TrimEnd('*') : key;

    /// <summary>A key the pattern matches, every <c>*</c> standing for <paramref name="part"/>: for
    /// looking the entry up as the handbook would.</summary>
    public static string Example(string key, string part = "x") => key.Replace("*", part);
}
