using SeraphHorizons.Mod.Trading.Economy.Core;

namespace SeraphHorizons.Mod.Tests.Trading.Economy;

public class SupplyTests
{
    private const string Iron = "game:ingot-iron";

    [Fact]
    public void RegionsAre8kmSquaresWithEightNeighboursWhoseSharesSumToOne()
    {
        Assert.Equal("62,62", SupplyRegion.KeyOf(512000, 512000));
        Assert.Equal("-1,0", SupplyRegion.KeyOf(-1, 8191.9));
        var n = SupplyRegion.Neighbours("0,0").ToList();
        Assert.Equal(8, n.Count);
        Assert.Equal(1, n.Sum(x => x.Share), 9);
        Assert.True(n.Single(x => x.Key == "1,0").Share > n.Single(x => x.Key == "1,1").Share);
    }

    [Fact]
    public void AStackOfPlanksAndAnIngotMoveTheirLevelsAlike()
    {
        var book = new SupplyBook();
        double planks = 64 * book.Weight(0.06, 64);
        double ingot = book.Weight(3.476, 1);
        Assert.InRange(planks / ingot, 0.9, 1.2);
        // Without a value, a full stack counts one level.
        Assert.Equal(1, 32 * book.Weight(0, 32), 9);
    }

    [Fact]
    public void SellingRaisesBuyingDrainsAndNeverBelowZero()
    {
        var book = new SupplyBook();
        book.Sold("0,0", Iron, 10, 0.35);
        Assert.Equal(3.5, book.Level("0,0", Iron), 9);
        book.Bought("0,0", Iron, 4, 0.35);
        Assert.Equal(2.1, book.Level("0,0", Iron), 9);
        book.Bought("0,0", Iron, 100, 0.35);
        Assert.Equal(0, book.Level("0,0", Iron));
        Assert.Equal(1, book.Factor("0,0", Iron));
    }

    [Fact]
    public void ALevelHalvesInItsHalfLifeWithoutSpread()
    {
        var book = new SupplyBook(new SupplySettings { HalfLifeDays = 10, SpreadFraction = 0 });
        book.Set("0,0", Iron, 8);
        for (int i = 0; i < 10; i++) book.Tick();
        Assert.Equal(4, book.Level("0,0", Iron), 6);
        Assert.Equal(10, book.Day);
    }

    [Fact]
    public void SpreadMovesAShareToTheNeighboursAndThinsWithDistance()
    {
        var book = new SupplyBook(new SupplySettings { HalfLifeDays = 1e9, SpreadFraction = 0.1 });
        book.Set("0,0", Iron, 100);
        book.Tick();
        Assert.Equal(90, book.Level("0,0", Iron), 6);
        double total = SupplyRegion.Neighbours("0,0").Sum(n => book.Level(n.Key, Iron));
        Assert.Equal(10, total, 6);
        Assert.True(book.Level("1,0", Iron) > book.Level("1,1", Iron));
        // Two regions away only after a second day, and less than next door.
        Assert.Equal(0, book.Level("2,0", Iron));
        book.Tick();
        Assert.True(book.Level("2,0", Iron) > 0);
        Assert.True(book.Level("2,0", Iron) < book.Level("1,0", Iron));
    }

    [Fact]
    public void OnePlayersOutputReachesTheNextRegionWithoutAWeekLongCrash()
    {
        // Ten iron ingots a day for a week in one region, with the defaults.
        var book = new SupplyBook();
        double weight = book.Weight(3.476, 1);
        for (int day = 0; day < 7; day++)
        {
            book.Sold("0,0", Iron, 10, weight);
            book.Tick();
        }
        double here = book.Factor("0,0", Iron), next = book.Factor("1,0", Iron);
        Assert.InRange(here, 0.4, 0.7);
        Assert.True(next < 0.99, $"the next region's factor is {next}");
        // A month later the price has mostly recovered.
        for (int day = 0; day < 30; day++) book.Tick();
        Assert.True(book.Factor("0,0", Iron) > 0.9, $"factor after a month {book.Factor("0,0", Iron)}");
    }

    [Fact]
    public void SmallLevelsArePruned()
    {
        var book = new SupplyBook(new SupplySettings { HalfLifeDays = 1, SpreadFraction = 0 });
        book.Set("0,0", Iron, 0.015);
        book.Tick();
        Assert.Null(book.Entry("0,0", Iron));
        Assert.Empty(book.Regions);
    }

    [Fact]
    public void PlayerSuppliedStockNeedsTheThresholdAndScalesWithTheLevel()
    {
        var book = new SupplyBook();
        double ingot = 3.476;
        Assert.Equal(0, book.ShelfStock(0.9, ingot, 4));
        int low = book.ShelfStock(1.04, ingot, 4);
        int high = book.ShelfStock(3, ingot, 4);
        Assert.Equal(1, low);
        Assert.True(high > low);
        // Capped at twice the entry's own stock.
        Assert.Equal(8, book.ShelfStock(1000, ingot, 4));
        // Not enough for a whole stack after the share: nothing.
        Assert.Equal(0, book.ShelfStock(1.2, stackValueGears: 30, entryStock: 2));
    }

    [Fact]
    public void ResetDropsAnItemOrARegion()
    {
        var book = new SupplyBook();
        book.Set("0,0", Iron, 2);
        book.Set("0,0", "game:plank-oak", 2);
        Assert.Equal(1, book.Reset("0,0", Iron));
        Assert.Equal(1, book.Reset("0,0"));
        Assert.Empty(book.Regions);
    }

    [Fact]
    public void HistoryKeepsTheLastChanges()
    {
        var book = new SupplyBook(new SupplySettings { HistorySize = 4 });
        for (int i = 0; i < 6; i++) book.Sold("0,0", Iron, 1, 1);
        book.Tick();
        var h = book.Entry("0,0", Iron)!.History;
        Assert.Equal(4, h.Count);
        Assert.Equal(SupplyEventKind.Day, h[^1].Kind);
        Assert.Equal(1, h[^1].Day);
        Assert.Equal(book.Level("0,0", Iron), h[^1].Level, 9);
    }

    [Fact]
    public void TheBookRoundTripsThroughJson()
    {
        var book = new SupplyBook();
        book.Sold("3,-2", Iron, 5, 0.35);
        book.Tick();
        book.Set("0,0", "game:plank-oak", 1.5);
        var again = SupplyBook.FromJson(book.ToJson(), book.Settings);
        Assert.Equal(book.Day, again.Day);
        Assert.Equal(book.Level("3,-2", Iron), again.Level("3,-2", Iron), 9);
        Assert.Equal(1.5, again.Level("0,0", "game:plank-oak"));
        Assert.Equal(book.Entry("3,-2", Iron)!.History, again.Entry("3,-2", Iron)!.History);
    }
}
