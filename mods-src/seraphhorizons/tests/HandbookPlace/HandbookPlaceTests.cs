using SeraphHorizons.Mod.HandbookPlace.Core;

namespace SeraphHorizons.Tests.HandbookPlace;

/// <summary>The handbook keeps its place (HandbookPlace/Core): what a close keeps, what an open puts
/// back when pages have changed, and how the list's scroll is put back over a list still filling.</summary>
public class HandbookPlaceTests
{
    static readonly HashSet<string> Pages = ["item-axe", "block-door", "tutorial-start"];
    static bool Exists(string code) => Pages.Contains(code);

    // ---- kept place ----

    [Fact]
    public void A_plain_list_at_the_top_keeps_nothing()
    {
        Assert.True(new KeptPlace(null, 0, []).IsEmpty);
        Assert.True(new KeptPlace("", float.NaN, []).IsEmpty);
        Assert.True(new KeptPlace("", -5, []).IsEmpty);
    }

    [Fact]
    public void Text_scroll_or_history_each_count()
    {
        Assert.False(new KeptPlace("axe", 0, []).IsEmpty);
        Assert.False(new KeptPlace("", 120, []).IsEmpty);
        Assert.False(new KeptPlace("", 0, [HistoryStep.Page("item-axe", 0)]).IsEmpty);
    }

    [Fact]
    public void History_is_cut_to_its_newest_steps()
    {
        var steps = Enumerable.Range(0, KeptPlace.MaxSteps + 10).Select(i => HistoryStep.Search("s" + i)).ToList();
        var place = new KeptPlace("", 0, steps);
        Assert.Equal(KeptPlace.MaxSteps, place.History.Count);
        Assert.Equal("s10", place.History[0].SearchText);
        Assert.Equal("s" + (KeptPlace.MaxSteps + 9), place.History[^1].SearchText);
    }

    // ---- plan ----

    [Fact]
    public void A_page_on_top_is_shown_with_its_scroll_and_the_history_behind_it()
    {
        var place = new KeptPlace("door", 340, [
            HistoryStep.Search("door"),
            HistoryStep.Page("block-door", 80),
            HistoryStep.Page("item-axe", 512),
        ]);
        var plan = place.Plan(Exists);
        Assert.True(plan.ShowsPage);
        Assert.Equal("door", plan.SearchText);
        Assert.Equal(340, plan.ListScrollY);
        Assert.Equal(place.History, plan.History);
        Assert.Equal(512, plan.History[^1].ScrollY);
    }

    [Fact]
    public void A_search_on_top_shows_the_list()
    {
        var plan = new KeptPlace("axe", 0, [HistoryStep.Page("item-axe", 0), HistoryStep.Search("axe")]).Plan(Exists);
        Assert.False(plan.ShowsPage);
        Assert.Equal(2, plan.History.Count);
    }

    [Fact]
    public void An_empty_history_shows_the_list()
    {
        var plan = new KeptPlace("axe", 90, []).Plan(Exists);
        Assert.False(plan.ShowsPage);
        Assert.Empty(plan.History);
        Assert.Equal(90, plan.ListScrollY);
    }

    [Fact]
    public void Pages_the_handbook_no_longer_has_are_dropped()
    {
        var plan = new KeptPlace("", 0, [
            HistoryStep.Page("item-axe", 10),
            HistoryStep.Page("item-gone", 20),
        ]).Plan(Exists);
        Assert.Equal([HistoryStep.Page("item-axe", 10)], plan.History);
        Assert.True(plan.ShowsPage);
    }

    [Fact]
    public void A_gone_page_on_top_leaves_the_list_shown_when_a_search_is_under_it()
    {
        var plan = new KeptPlace("axe", 0, [HistoryStep.Search("axe"), HistoryStep.Page("item-gone", 0)]).Plan(Exists);
        Assert.False(plan.ShowsPage);
        Assert.Equal([HistoryStep.Search("axe")], plan.History);
    }

    [Fact]
    public void Dropping_a_page_never_leaves_the_same_step_twice_in_a_row()
    {
        // axe -> gone -> axe: the game would never have pushed axe on axe.
        var plan = new KeptPlace("", 0, [
            HistoryStep.Page("item-axe", 10),
            HistoryStep.Page("item-gone", 0),
            HistoryStep.Page("item-axe", 70),
            HistoryStep.Search("x"),
            HistoryStep.Page("item-gone", 0),
            HistoryStep.Search("x"),
        ]).Plan(Exists);
        Assert.Equal([HistoryStep.Page("item-axe", 70), HistoryStep.Search("x")], plan.History);
    }

    [Fact]
    public void Different_searches_and_pages_stay_apart()
    {
        var steps = new[] { HistoryStep.Search("a"), HistoryStep.Search("b"), HistoryStep.Page("item-axe", 0), HistoryStep.Page("block-door", 0) };
        Assert.Equal(steps, new KeptPlace("", 0, steps).Plan(Exists).History);
    }

    [Fact]
    public void A_search_step_with_no_text_is_dropped()
    {
        var plan = new KeptPlace("", 0, [new HistoryStep(null, null, 0), HistoryStep.Page("item-axe", 0)]).Plan(Exists);
        Assert.Equal([HistoryStep.Page("item-axe", 0)], plan.History);
    }

    // ---- list scroll ----

    /// <summary>A scrollbar over a list that fills <paramref name="chunk"/> pixels at a time when the bar
    /// comes within <paramref name="near"/> of its end (as Dovidarium's lazy list), up to <paramref name="full"/>.</summary>
    sealed class FakeList(float total, float chunk, float full, float near = 500, float visible = 500)
    {
        public float Total = total;
        public float Position;
        public int Applies;

        float Max => Math.Max(0, Total - visible);

        public (float, float) Apply(float y)
        {
            Applies++;
            Position = Math.Min(y, Max);
            // The bar keeps its handle where it is as the list grows, so the content position moves on.
            while (Total < full && Total - visible - Position <= near)
            {
                float before = Max;
                Total = Math.Min(full, Total + chunk);
                if (before > 0) Position *= Max / before;
            }
            return (Position, Total);
        }
    }

    [Fact]
    public void A_list_already_long_enough_is_one_pass()
    {
        var list = new FakeList(total: 5000, chunk: 0, full: 5000);
        var scroll = new ListScroll(1200, 20);
        Assert.Equal(ListScroll.Outcome.Done, scroll.Try(list.Apply));
        Assert.Equal(1200, list.Position);
        Assert.Equal(1, list.Applies);
    }

    [Fact]
    public void A_lazy_list_is_grown_until_the_target_is_in_it_and_set_again_after_overshooting()
    {
        var list = new FakeList(total: 4000, chunk: 4000, full: 40000);
        var scroll = new ListScroll(15000, 20);
        Assert.Equal(ListScroll.Outcome.Done, scroll.Try(list.Apply));
        Assert.InRange(list.Position, 15000 - ListScroll.Tolerance, 15000 + ListScroll.Tolerance);
    }

    [Fact]
    public void A_list_shorter_than_the_target_ends_at_its_bottom_and_keeps_trying()
    {
        var list = new FakeList(total: 2000, chunk: 0, full: 2000);
        var scroll = new ListScroll(9000, 20);
        Assert.Equal(ListScroll.Outcome.Pending, scroll.Try(list.Apply));
        Assert.Equal(1500, list.Position);
        Assert.Equal(2, list.Applies); // the second pass sees it did not grow
    }

    [Fact]
    public void A_list_that_fills_later_is_reached_on_a_later_tick()
    {
        // An incremental search: empty at the open, its results a few frames later.
        var list = new FakeList(total: 0, chunk: 0, full: 0);
        var scroll = new ListScroll(800, 20);
        Assert.Equal(ListScroll.Outcome.Pending, scroll.Try(list.Apply));
        Assert.Equal(ListScroll.Outcome.Pending, scroll.Tick(list.Position, list.Apply));
        list.Total = 3000;
        Assert.Equal(ListScroll.Outcome.Done, scroll.Tick(list.Position, list.Apply));
        Assert.Equal(800, list.Position);
    }

    [Fact]
    public void The_player_moving_the_bar_stops_the_retries()
    {
        var list = new FakeList(total: 0, chunk: 0, full: 0);
        var scroll = new ListScroll(800, 20);
        Assert.Equal(ListScroll.Outcome.Pending, scroll.Try(list.Apply));
        list.Total = 3000;
        int applies = list.Applies;
        Assert.Equal(ListScroll.Outcome.Abandoned, scroll.Tick(250, list.Apply));
        Assert.Equal(applies, list.Applies);
    }

    [Fact]
    public void Retries_run_out()
    {
        var list = new FakeList(total: 0, chunk: 0, full: 0);
        var scroll = new ListScroll(800, 3);
        Assert.Equal(ListScroll.Outcome.Pending, scroll.Try(list.Apply));
        for (int i = 0; i < 3; i++)
            Assert.Equal(ListScroll.Outcome.Pending, scroll.Tick(list.Position, list.Apply));
        Assert.Equal(ListScroll.Outcome.Abandoned, scroll.Tick(list.Position, list.Apply));
    }

    [Fact]
    public void A_list_that_never_stops_growing_is_bounded_per_try()
    {
        int applies = 0;
        var scroll = new ListScroll(1000, 20);
        // Always short and always longer: the pass count caps it.
        var outcome = scroll.Try(y => { applies++; return (0, applies * 10f); });
        Assert.Equal(ListScroll.Outcome.Pending, outcome);
        Assert.Equal(ListScroll.MaxPassesPerTry, applies);
    }
}
