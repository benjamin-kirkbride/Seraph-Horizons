using SeraphHorizons.Mod.Trading.Maps.Core;

namespace SeraphHorizons.Mod.Tests.Trading.Maps;

/// <summary>The groups' camp lead history: counts per trader that never reset, the stranger's map,
/// camps visited, pooled by company as standing is, and saved versioned.</summary>
public class LeadBookTests
{
    private const string Trader = "camp:1,2";

    [Fact]
    public void ASoloPlayerIsTheirOwnGroupAndACompanyAddsItsKey()
    {
        Assert.Equal(["player:u1"], LeadBook.KeysOf("u1", null));
        Assert.Equal(["player:u1", "company:7"], LeadBook.KeysOf("u1", 7));
    }

    [Fact]
    public void CountsPerTraderGoUpAndNeverReset()
    {
        var book = new LeadBook();
        var keys = LeadBook.KeysOf("u1", null);
        Assert.Equal(0, book.Bought(keys, Trader));
        book.RecordBought(keys, Trader, asStranger: false);
        book.RecordBought(keys, Trader, asStranger: false);
        Assert.Equal(2, book.Bought(keys, Trader));
        Assert.Equal(0, book.Bought(keys, "camp:9,9"));
        Assert.False(book.StrangerUsed(keys, Trader));
    }

    [Fact]
    public void AStrangersMapIsNotedForThatTraderOnly()
    {
        var book = new LeadBook();
        var keys = LeadBook.KeysOf("u1", null);
        book.RecordBought(keys, Trader, asStranger: true);
        Assert.True(book.StrangerUsed(keys, Trader));
        Assert.False(book.StrangerUsed(keys, "camp:0,0"));
    }

    [Fact]
    public void ACompanyPoolsWhatItsMembersBoughtAndVisited()
    {
        var book = new LeadBook();
        book.RecordBought(LeadBook.KeysOf("u1", 7), Trader, asStranger: true);
        book.RecordVisit(LeadBook.KeysOf("u1", 7), "camp:3,3");
        // Another member of the company: the company's count, map and visits are theirs too.
        var other = LeadBook.KeysOf("u2", 7);
        Assert.Equal(1, book.Bought(other, Trader));
        Assert.True(book.StrangerUsed(other, Trader));
        Assert.Contains("camp:3,3", book.Visited(other));
        // Their next buy goes past the company's count, for both.
        book.RecordBought(other, Trader, asStranger: false);
        Assert.Equal(2, book.Bought(LeadBook.KeysOf("u1", 7), Trader));
        // Leaving: the first player keeps their own history; the second only theirs.
        Assert.Equal(1, book.Bought(LeadBook.KeysOf("u1", null), Trader));
        Assert.True(book.StrangerUsed(LeadBook.KeysOf("u1", null), Trader));
        Assert.Equal(2, book.Bought(LeadBook.KeysOf("u2", null), Trader));
        Assert.False(book.StrangerUsed(LeadBook.KeysOf("u2", null), Trader));
        Assert.Empty(book.Visited(LeadBook.KeysOf("u2", null)));
    }

    [Fact]
    public void AVisitIsNewOnce()
    {
        var book = new LeadBook();
        var keys = LeadBook.KeysOf("u1", null);
        Assert.True(book.RecordVisit(keys, "camp:3,3"));
        Assert.False(book.RecordVisit(keys, "camp:3,3"));
    }

    [Fact]
    public void ItRoundTripsVersionedAndAnOldWorldStartsEmpty()
    {
        var book = new LeadBook();
        book.RecordBought(LeadBook.KeysOf("u1", 7), Trader, asStranger: true);
        book.RecordVisit(LeadBook.KeysOf("u1", null), "camp:3,3");
        book.RecordPity("u1");
        string json = book.ToJson();
        Assert.Contains("\"version\":2", json);
        var back = LeadBook.FromJson(json, out var problem);
        Assert.Null(problem);
        Assert.Equal(1, back.Bought(LeadBook.KeysOf("u1", 7), Trader));
        Assert.True(back.StrangerUsed(LeadBook.KeysOf("u1", null), Trader));
        Assert.Contains("camp:3,3", back.Visited(LeadBook.KeysOf("u1", null)));
        Assert.True(back.PityUsed("u1"));
        Assert.False(back.PityUsed("u2"));

        Assert.Empty(LeadBook.FromJson(null, out problem).Groups);
        Assert.Null(problem);
        var newer = LeadBook.FromJson("{\"version\":99,\"groups\":{\"player:u1\":{\"bought\":{\"camp:1,2\":3}}}}", out problem);
        Assert.Empty(newer.Groups);
        Assert.NotNull(problem);
    }

    [Fact]
    public void ThePityMapIsEachPlayersOwnNotTheCompanys()
    {
        var book = new LeadBook();
        Assert.False(book.PityUsed("u1"));
        book.RecordPity("u1");
        Assert.True(book.PityUsed("u1"));
        // u1 bought it in company 7; u2 joining the company still has theirs.
        book.RecordBought(LeadBook.KeysOf("u1", 7), Trader, asStranger: true);
        Assert.False(book.PityUsed("u2"));
        Assert.False(book.Groups["company:7"].PityMap);
        // The company's count and stranger map are pooled as ever.
        Assert.Equal(1, book.Bought(LeadBook.KeysOf("u2", 7), Trader));
        Assert.True(book.StrangerUsed(LeadBook.KeysOf("u2", 7), Trader));
    }

    [Fact]
    public void AVersionOneBookMigratesWithEveryPlayerYetToHaveTheirFirstMap()
    {
        const string v1 = "{\"version\":1,\"groups\":{"
                          + "\"player:u1\":{\"bought\":{\"camp:1,2\":3},\"strangerMaps\":[\"camp:1,2\"],\"visited\":[\"camp:3,3\"]},"
                          + "\"company:7\":{\"bought\":{\"camp:1,2\":4},\"strangerMaps\":[],\"visited\":[]}}}";
        var book = LeadBook.FromJson(v1, out var problem);
        Assert.Null(problem);
        Assert.Equal(LeadBook.Version, book.SavedVersion);
        Assert.Equal(2, LeadBook.Version);
        Assert.False(book.PityUsed("u1"));
        Assert.All(book.Groups.Values, g => Assert.False(g.PityMap));
        // The rest reads as it was.
        Assert.Equal(4, book.Bought(LeadBook.KeysOf("u1", 7), Trader));
        Assert.True(book.StrangerUsed(LeadBook.KeysOf("u1", null), Trader));
        Assert.Contains("camp:3,3", book.Visited(LeadBook.KeysOf("u1", null)));
        Assert.Contains("\"version\":2", book.ToJson());
    }
}
