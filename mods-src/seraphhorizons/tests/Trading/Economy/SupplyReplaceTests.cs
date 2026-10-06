using SeraphHorizons.Mod.Trading.Economy.Core;

namespace SeraphHorizons.Mod.Tests.Trading.Economy;

public class SupplyReplaceTests
{
    [Fact]
    public void ExportThenImportRoundTripsLevelsHistoryAndDay()
    {
        var book = new SupplyBook();
        book.Sold("0,0", "game:ingot-iron", 10, 0.4);
        book.Tick();
        book.Set("1,0", "game:plank-oak", 2);
        var settings = new SupplySettings { HalfLifeDays = 3 };
        var other = new SupplyBook(settings);
        other.Set("5,5", "game:gold", 9);
        other.ReplaceWith(SupplyBook.FromJson(book.ToJson()));
        Assert.Equal(book.ToJson(), other.ToJson());
        Assert.Equal(1, other.Day);
        Assert.Equal(0, other.Level("5,5", "game:gold"));
        Assert.Same(settings, other.Settings);
        // The copy is the book's own: changing it leaves the source alone.
        other.Add("1,0", "game:plank-oak", 1);
        Assert.Equal(2, book.Level("1,0", "game:plank-oak"));
    }
}
