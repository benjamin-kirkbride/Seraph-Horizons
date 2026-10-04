using SeraphHorizons.Mod.Core;

namespace SeraphHorizons.Mod.Tests;

public class LangEntriesTests
{
    [Fact]
    public void ReplacesAnExactEntryWhateverItSaid()
    {
        var entries = new Dictionary<string, string> { ["iw:a"] = "Old text" };
        Assert.Equal(LangChange.Changed, LangEntries.Replace(entries, "iw:a", null, "New text"));
        Assert.Equal("New text", entries["iw:a"]);
    }

    [Fact]
    public void ReplacingTwiceIsAlreadyDone()
    {
        var entries = new Dictionary<string, string> { ["iw:a"] = "Old text" };
        LangEntries.Replace(entries, "iw:a", null, "New text");
        Assert.Equal(LangChange.AlreadyDone, LangEntries.Replace(entries, "iw:a", null, "New text"));
        Assert.Equal("New text", entries["iw:a"]);
    }

    [Fact]
    public void AWildcardOnlyKeyGetsAnExactEntry()
    {
        var entries = new Dictionary<string, string>();
        Assert.Equal(LangChange.Changed, LangEntries.Replace(entries, "le:a-oak", "Wildcard text", "New text"));
        Assert.Equal("New text", entries["le:a-oak"]);
    }

    [Fact]
    public void ReplacingAMissingKeyAddsNothing()
    {
        var entries = new Dictionary<string, string>();
        Assert.Equal(LangChange.Missing, LangEntries.Replace(entries, "iw:a", null, "New text"));
        Assert.Empty(entries);
    }

    [Fact]
    public void RemovesAnEntryOnceAndASecondRunIsAlreadyDone()
    {
        var entries = new Dictionary<string, string> { ["iw:a"] = "Text", ["iw:b"] = "Other" };
        var removed = new HashSet<string>();
        Assert.Equal(LangChange.Changed, LangEntries.Remove(entries, removed, "iw:a"));
        Assert.Equal(LangChange.AlreadyDone, LangEntries.Remove(entries, removed, "iw:a"));
        Assert.Equal(["iw:b"], entries.Keys);
    }

    [Fact]
    public void RemovingAKeyThatWasNeverThereIsMissing() =>
        Assert.Equal(LangChange.Missing, LangEntries.Remove(new Dictionary<string, string>(), new HashSet<string>(), "iw:a"));
}
