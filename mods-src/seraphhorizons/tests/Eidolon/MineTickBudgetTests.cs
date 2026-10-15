using SeraphHorizons.Mod.Eidolon.Core;
using Xunit.Abstractions;
using static SeraphHorizons.Mod.Tests.Eidolon.MineTestWorld;

namespace SeraphHorizons.Mod.Tests.Eidolon;

/// <summary>The mine planner's main-thread share of a tick (<see cref="MineTickBudget"/>): wall time in the
/// game, a count of blocks read under the tests' virtual clock, the window read in the same ticks every run.</summary>
public class MineTickBudgetTests(ITestOutputHelper output)
{
    [Fact]
    public void Counted_in_blocks_it_lets_work_on_until_they_are_read_one_piece_past_at_most()
    {
        var budget = new MineTickBudget(MinePlanner.BudgetMs, 100);
        budget.Restart();
        Assert.True(budget.Take(40));
        Assert.True(budget.Take(40));
        Assert.False(budget.Spent);
        // 80 read: the next piece goes, past the budget, and nothing after it
        Assert.True(budget.Take(40));
        Assert.True(budget.Spent);
        Assert.False(budget.Take(1));
        budget.Restart();
        Assert.False(budget.Spent);
        Assert.True(budget.Take(1));
    }

    [Fact]
    public void Counted_in_blocks_it_takes_no_notice_of_the_time()
    {
        var budget = new MineTickBudget(0.001, 100);
        budget.Restart();
        Thread.Sleep(5);
        Assert.False(budget.Spent);
        Assert.True(budget.Take(99));
        Assert.True(budget.Take(1));
        Assert.True(budget.Spent);
    }

    [Fact]
    public void On_wall_time_it_takes_no_notice_of_the_blocks()
    {
        var budget = new MineTickBudget(10_000);
        budget.Restart();
        for (int i = 0; i < 1000; i++)
            Assert.True(budget.Take(int.MaxValue / 2));
        Assert.Null(budget.Reads);
        var brief = new MineTickBudget(1);
        brief.Restart();
        Thread.Sleep(5);
        Assert.True(brief.Spent);
        Assert.False(brief.Take(1));
    }

    [Fact]
    public void No_blocks_a_tick_is_refused() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new MineTickBudget(MinePlanner.BudgetMs, 0));

    [Fact]
    public void A_window_read_by_blocks_takes_the_same_ticks_every_run()
    {
        // as EidolonMinePlans reads a job's window: a column a piece, the window's height in blocks
        var world = new MineTestWorld().Ground(9).Hill(0, 15, 17, 0, 15, 10);
        var area = Area(0, 15, 10, 17, 0, 15);
        int Ticks(int reads)
        {
            var site = new MineSite(area, 2);
            var budget = new MineTickBudget(MinePlanner.BudgetMs, reads);
            using var loading = site.Load(world).GetEnumerator();
            for (int ticks = 1; ; ticks++)
            {
                budget.Restart();
                while (budget.Take(site.Window.SizeY))
                    if (!loading.MoveNext())
                        return ticks;
            }
        }
        var window = new MineSite(area, 2).Window;
        int columns = window.SizeX * window.SizeZ;
        int perTick = (Reads + window.SizeY - 1) / window.SizeY;
        // the last piece of a tick goes past the budget; the enumerator's end is one more piece
        Assert.Equal((columns + 1 + perTick - 1) / perTick, Ticks(Reads));
        Assert.Equal(Ticks(Reads), Ticks(Reads));

        // what the planner's side of a read costs here (the test world's, not the game's block accessor)
        var clock = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 20; i++)
            world.Site(area);
        output.WriteLine($"window {window.SizeX}x{window.SizeY}x{window.SizeZ}: "
            + $"{clock.Elapsed.TotalMilliseconds * 1000 / (20.0 * columns * window.SizeY):0.000} us a block read in the test world");
    }

    private const int Reads = 5000;
}
