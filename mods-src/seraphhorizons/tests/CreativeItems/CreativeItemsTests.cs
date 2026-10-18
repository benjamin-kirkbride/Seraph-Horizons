using SeraphHorizons.Mod.CreativeItems.Core;

namespace SeraphHorizons.Tests.CreativeItems;

/// <summary>The creative item source and sink (CreativeItems/Core): the source's feed at a rate,
/// unlimited, paused and to a limit; the sliding rate window; the sink's tally.</summary>
public class CreativeItemsTests
{
    // ---- the feed ----

    // Feeds `ticks` ticks of `seconds`, everything due taken; returns the total. Rates and tick
    // lengths here are exact in binary, so the counts are too.
    private static long Run(ItemFeed feed, int ticks, double seconds = 0.25)
    {
        for (int i = 0; i < ticks; i++)
            feed.Delivered(feed.Due(seconds));
        return feed.Total;
    }

    [Theory]
    [InlineData(1, 40, 10)]      // 10 s at 1/s
    [InlineData(0.5, 40, 5)]     // a fraction per tick, carried over
    [InlineData(0.25, 80, 5)]
    [InlineData(2.5, 16, 10)]
    [InlineData(100, 4, 100)]    // 25 a tick
    public void A_rate_gives_its_items_per_second_carrying_fractions(double rate, int ticks, long expected)
    {
        var feed = new ItemFeed();
        feed.Configure(rate, unlimited: false, paused: false, limit: 0);
        Assert.Equal(expected, Run(feed, ticks));
    }

    [Fact]
    public void What_the_hopper_cannot_take_builds_up_to_a_second_only()
    {
        var feed = new ItemFeed();
        feed.Configure(5, unlimited: false, paused: false, limit: 0);
        for (int i = 0; i < 50; i++)
            feed.Due(0.2);   // ten seconds, nothing taken
        Assert.Equal(5, feed.Due(0.2));
        feed.Delivered(2);
        Assert.Equal(3, feed.Due(0));
    }

    [Fact]
    public void Below_one_a_second_an_item_still_comes_once_it_is_whole()
    {
        var feed = new ItemFeed();
        feed.Configure(0.125, unlimited: false, paused: false, limit: 0);
        Assert.Equal(0, Run(feed, 31));
        Assert.Equal(1, Run(feed, 1));
    }

    [Fact]
    public void Unlimited_asks_for_all_the_hopper_takes_up_to_the_limit()
    {
        var feed = new ItemFeed();
        feed.Configure(1, unlimited: true, paused: false, limit: 0);
        Assert.Equal(long.MaxValue, feed.Due(0.2));
        feed.Configure(1, unlimited: true, paused: false, limit: 100);
        feed.Delivered(64);
        Assert.Equal(36, feed.Due(0.2));
    }

    [Fact]
    public void The_limit_stops_the_feed_and_a_reset_starts_it_again()
    {
        var feed = new ItemFeed();
        feed.Configure(10, unlimited: false, paused: false, limit: 7);
        Assert.Equal(7, Run(feed, 50));
        Assert.True(feed.LimitReached);
        Assert.False(feed.Running);
        Assert.Equal(0, feed.Due(1));
        feed.Reset();
        Assert.Equal(0, feed.Total);
        Assert.Equal(7, Run(feed, 50));
    }

    [Fact]
    public void Paused_gives_nothing_and_keeps_its_settings()
    {
        var feed = new ItemFeed();
        feed.Configure(3, unlimited: false, paused: true, limit: 20);
        Assert.Equal(0, Run(feed, 50));
        Assert.Equal(3, feed.Rate);
        Assert.Equal(20, feed.Limit);
        feed.Configure(3, unlimited: false, paused: false, limit: 20);
        Assert.Equal(3, Run(feed, 5));
    }

    [Theory]
    [InlineData(0, ItemFeed.MinRate)]
    [InlineData(-5, ItemFeed.MinRate)]
    [InlineData(1e9, ItemFeed.MaxRate)]
    [InlineData(double.NaN, ItemFeed.DefaultRate)]
    [InlineData(double.PositiveInfinity, ItemFeed.DefaultRate)]
    [InlineData(2.5, 2.5)]
    public void Rates_are_clamped(double rate, double expected) => Assert.Equal(expected, ItemFeed.ClampRate(rate));

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(500, 500)]
    [InlineData(long.MaxValue, ItemFeed.MaxLimit)]
    public void Limits_are_clamped(long limit, long expected) => Assert.Equal(expected, ItemFeed.ClampLimit(limit));

    [Fact]
    public void A_restored_count_and_fraction_are_clamped()
    {
        var feed = new ItemFeed();
        feed.Configure(2, unlimited: false, paused: false, limit: 0);
        feed.Restore(-4, double.NaN);
        Assert.Equal(0, feed.Total);
        Assert.Equal(0, feed.Owed);
        feed.Restore(12, 99);
        Assert.Equal(12, feed.Total);
        Assert.Equal(2, feed.Owed);
        feed.Restore(12, 0.6);
        Assert.Equal(0.6, feed.Owed, 9);
    }

    [Fact]
    public void Bad_tick_lengths_add_nothing()
    {
        var feed = new ItemFeed();
        feed.Configure(10, unlimited: false, paused: false, limit: 0);
        Assert.Equal(0, feed.Due(double.NaN));
        Assert.Equal(0, feed.Due(-3));
        Assert.Equal(0, feed.Owed);
    }

    // ---- the rate window ----

    [Fact]
    public void A_steady_rate_reads_per_minute_once_the_window_is_full()
    {
        var window = new RateWindow(0);
        for (int t = 0; t < 120; t++)
            window.Add(t + 0.5, 2);
        Assert.Equal(120, window.PerMinute(120), 0);
    }

    [Fact]
    public void Before_a_minute_has_passed_it_averages_over_the_time_so_far()
    {
        var window = new RateWindow(100);
        for (int t = 0; t < 20; t++)
            window.Add(100 + t + 0.5, 1);
        Assert.Equal(60, window.PerMinute(120), 0);
        // But over five seconds at least: one item at once is 12 a minute, not thousands.
        var fresh = new RateWindow(0);
        fresh.Add(0.1, 1);
        Assert.Equal(12, fresh.PerMinute(0.2), 6);
    }

    [Fact]
    public void Old_counts_slide_out_of_the_window()
    {
        var window = new RateWindow(0);
        window.Add(1.5, 600);
        Assert.True(window.PerMinute(60.9) > 600);
        Assert.Equal(0, window.PerMinute(61));
        window.Add(200, 5);
        Assert.Equal(5, window.PerMinute(200), 0);
    }

    [Fact]
    public void A_reset_or_a_clock_going_back_empties_it()
    {
        var window = new RateWindow(0);
        window.Add(30, 100);
        window.Reset(40);
        Assert.Equal(0, window.PerMinute(41));
        window.Add(50, 100);
        Assert.Equal(0, window.PerMinute(10));
    }

    // ---- the sink's tally ----

    [Fact]
    public void The_tally_counts_by_key_and_orders_most_first()
    {
        var tally = new ItemTally();
        tally.Add("item:game:stick", 5);
        tally.Add("block:game:cobblestone-granite", 64);
        tally.Add("item:game:stick", 3);
        tally.Add("item:game:flint", 8);
        tally.Add("item:game:nothing", 0);
        tally.Add("", 4);
        Assert.Equal(80, tally.Total);
        Assert.Equal(3, tally.Kinds);
        Assert.Equal(8, tally["item:game:stick"]);
        Assert.Equal(["block:game:cobblestone-granite", "item:game:flint", "item:game:stick"],
            tally.Ordered().Select(e => e.Key));
        tally.Reset();
        Assert.Equal(0, tally.Total);
        Assert.Empty(tally.Ordered());
    }

    [Fact]
    public void A_restored_tally_drops_bad_counts_and_sums_the_rest()
    {
        var tally = new ItemTally();
        tally.Add("item:game:stick", 1);
        tally.Restore([new("item:game:flint", 4), new("item:game:stick", -2), new("block:game:log", 6)]);
        Assert.Equal(10, tally.Total);
        Assert.Equal(0, tally["item:game:stick"]);
        Assert.Equal(2, tally.Kinds);
    }
}
