using SeraphHorizons.Mod.Trading.Orders.Core;

namespace SeraphHorizons.Tests.Trading.Orders;

/// <summary>Standing orders (#453): how many, how large and what they pay by standing tier, the
/// payout maths, the state machine and which outcomes cost standing.</summary>
public class OrdersTests
{
    private static readonly OrderCandidate Iron = new("game:ingot-iron", 3, 64);
    private static readonly OrderCandidate Planks = new("game:plank-oak", 0.25, 64);

    [Fact]
    public void A_trader_offers_two_orders_a_week_per_tier()
    {
        Assert.Equal([2, 4, 6, 8, 10], Enumerable.Range(1, 5).Select(OrderPlanner.PerWeek));
        // Below the first tier counts as a stranger.
        Assert.Equal(2, OrderPlanner.PerWeek(0));
    }

    [Fact]
    public void An_order_is_worth_lo_to_two_and_a_half_n_gears_and_pays_ten_to_twenty_times_that()
    {
        Assert.Equal([1, 1.375, 1.75, 2.125, 2.5], Enumerable.Range(1, 5).Select(OrderPlanner.MinWorth));
        Assert.Equal([2.5, 5, 7.5, 10, 12.5], Enumerable.Range(1, 5).Select(OrderPlanner.MaxWorth));
        Assert.Equal([10, 12.5, 15, 17.5, 20], Enumerable.Range(1, 5).Select(OrderPlanner.Multiplier));
        Assert.Equal(1, OrderPlanner.Worth(1, 0));
        Assert.Equal(12.5, OrderPlanner.Worth(5, 1));
        Assert.Equal(1.75, OrderPlanner.Worth(1, 0.5));
    }

    [Fact]
    public void The_days_are_between_3_and_6()
    {
        Assert.Equal(3, OrderPlanner.Days(0));
        Assert.Equal(6, OrderPlanner.Days(0.9999));
    }

    [Fact]
    public void The_quantity_is_the_worth_in_whole_items_rounded_up()
    {
        // 2.5 gears of iron at 3 a piece: one ingot; 7.5 gears: three.
        Assert.Equal(1, OrderPlanner.Quantity(3, 2.5, 64));
        Assert.Equal(3, OrderPlanner.Quantity(3, 7.5, 64));
        Assert.Equal(3, OrderPlanner.Quantity(3, 6.1, 64));
        // 5 gears of planks at a quarter each: 20.
        Assert.Equal(20, OrderPlanner.Quantity(0.25, 5, 64));
        // Never under one item, never over four stacks.
        Assert.Equal(1, OrderPlanner.Quantity(100, 1, 64));
        Assert.Equal(256, OrderPlanner.Quantity(0.01, 12.5, 64));
    }

    [Fact]
    public void The_payout_is_the_goods_worth_after_rounding_times_the_multiplier()
    {
        // One ingot (3 gears) for a stranger: 30; for a partner: 60.
        Assert.Equal(30, OrderPlanner.Payout(1, 3, 1));
        Assert.Equal(60, OrderPlanner.Payout(1, 3, 5));
        Assert.Equal(1, OrderPlanner.Payout(1, 0.01, 1));
    }

    [Fact]
    public void The_taker_s_tier_sizes_an_offer_unless_its_quantity_is_fixed()
    {
        var book = new OrderBook();
        var o = book.Offer("t", "smith", Iron, 0.5, 4, 0);
        Assert.Equal(0, o.Quantity);
        // A stranger: 1.75 gears' worth, one ingot, 30 gears. A partner: 7.5 gears' worth, three, 180.
        Assert.Equal((1, 30), OrderPlanner.Terms(o, 1, 64));
        Assert.Equal((3, 180), OrderPlanner.Terms(o, 5, 64));
        var fixedQty = book.Offer("t", "smith", Planks, 0.5, 4, 0, fixedQuantity: 16);
        Assert.Equal((16, 40), OrderPlanner.Terms(fixedQty, 1, 64));
        Assert.Equal((16, 80), OrderPlanner.Terms(fixedQty, 5, 64));
    }

    [Fact]
    public void The_payout_is_paid_pro_rata_and_the_rest_on_completion()
    {
        Assert.Equal(3, OrderPlanner.PayoutDue(10, 3, 1, 0));
        Assert.Equal(3, OrderPlanner.PayoutDue(10, 3, 2, 3));
        Assert.Equal(4, OrderPlanner.PayoutDue(10, 3, 3, 6));
        Assert.Equal(0, OrderPlanner.PayoutDue(10, 3, 3, 10));
    }

    [Fact]
    public void Picking_skips_what_is_already_on_order()
    {
        var all = new[] { Iron, Planks };
        Assert.Equal(Planks, OrderPlanner.Pick(all, new HashSet<string> { Iron.Item }, 0));
        Assert.Null(OrderPlanner.Pick(all, new HashSet<string> { Iron.Item, Planks.Item }, 0));
        Assert.Null(OrderPlanner.Pick([new OrderCandidate("game:x", 0, 1)], new HashSet<string>(), 0));
    }

    [Fact]
    public void An_order_is_offered_taken_delivered_in_part_and_completed()
    {
        var book = new OrderBook();
        var o = book.Offer("camp:1,1", "smith", Iron, 1, 4, 10);
        Assert.Equal(OrderState.Offered, o.State);
        Assert.Null(book.Deliver(o.Id, "p", 1, 10));
        // A regular (n = 3): 7.5 gears' worth, three ingots, 9 × 15 = 135.
        Assert.True(book.Accept(o.Id, "p", "P", 3, 64, 11));
        Assert.False(book.Accept(o.Id, "q", "Q", 3, 64, 11));
        Assert.Equal((3, 135, 15.0, 3), (o.Quantity, o.Payout, o.Multiplier, o.Tier));
        Assert.Equal(15, o.Deadline);
        Assert.Null(book.Deliver(o.Id, "q", 1, 11));

        var part = book.Deliver(o.Id, "p", 1, 12)!;
        Assert.Equal(1, part.Taken);
        Assert.Equal(45, part.PayoutToPlayer);
        Assert.False(part.Completed);
        Assert.Equal(OrderState.Accepted, o.State);

        // More than wanted: only the rest counts.
        var rest = book.Deliver(o.Id, "p", 10, 13)!;
        Assert.Equal(2, rest.Taken);
        Assert.Equal(90, rest.PayoutToPlayer);
        Assert.True(rest.Completed);
        Assert.Equal(0, rest.RefundToTrader);
        Assert.Equal(OrderState.Done, o.State);
        Assert.Equal(135, o.PayoutPaid);
        Assert.Null(book.Deliver(o.Id, "p", 1, 13));
    }

    [Fact]
    public void Past_the_deadline_only_a_taken_order_with_nothing_delivered_is_abandoned()
    {
        var book = new OrderBook();
        var offer = book.Offer("t", "smith", Iron, 0, 4, 0);
        var taken = book.Offer("t", "smith", Planks, 0, 4, 0);
        var part = book.Offer("u", "cook", Planks, 1, 4, 0);
        book.Accept(taken.Id, "p", "P", 1, 64, 0);
        book.Accept(part.Id, "p", "P", 1, 64, 0);
        book.Deliver(part.Id, "p", 2, 1);
        Assert.Empty(book.Tick(4));
        Assert.Null(book.Deliver(part.Id, "p", 1, 4.5));

        var changes = book.Tick(4.5).ToDictionary(c => c.Order.Id);
        Assert.Equal(OrderState.Expired, changes[offer.Id].To);
        Assert.False(changes[offer.Id].Abandoned);
        Assert.True(changes[taken.Id].Abandoned);
        Assert.Equal(OrderState.Expired, changes[part.Id].To);
        Assert.False(changes[part.Id].Abandoned);
        // Nothing was held back from the trader, so nothing goes back.
        Assert.All(changes.Values, c => Assert.Equal(0, c.RefundToTrader));
        Assert.Empty(book.OpenAt("t"));
    }

    [Fact]
    public void An_order_taken_before_payouts_were_new_money_gives_back_its_unpaid_reserve()
    {
        // As an old save has it: premium 12 held back, 4 of it paid.
        var book = OrderBook.FromJson("""
            {"next": 2, "orders": [{"id": 1, "trader": "t", "type": "smith", "item": "game:ingot-iron", "base": 8, "qty": 8, "lot": 1,
              "delivered": 3, "unit": 0.6, "factor": 1.5, "premium": 12, "reserved": 12, "paid": 4, "days": 4, "created": 0,
              "deadline": 4, "player": "p", "playerName": "P", "accepted": 0, "state": "Accepted"}]}
            """);
        var o = book.Get(1)!;
        Assert.Equal((8, 12, 12, 0.6), (o.Quantity, o.Payout, o.Reserved, o.Value));
        Assert.Equal(8, book.Tick(5).Single().RefundToTrader);
    }

    [Fact]
    public void Simulated_days_bring_the_deadline_closer()
    {
        var book = new OrderBook();
        var o = book.Offer("t", "smith", Iron, 0, 4, 10);
        book.Accept(o.Id, "p", "P", 1, 64, 10);
        for (int i = 0; i < 4; i++) book.Advance(1);
        Assert.Empty(book.Tick(10));
        book.Advance(1);
        Assert.True(book.Tick(10).Single().Abandoned);
    }

    [Fact]
    public void Admin_completion_pays_the_rest_and_cancelling_closes()
    {
        var book = new OrderBook();
        var o = book.Offer("t", "smith", Iron, 0, 4, 0, fixedQuantity: 4);
        Assert.Null(book.Complete(o.Id, 0));
        book.Accept(o.Id, "p", "P", 1, 64, 0);
        Assert.Equal(120, o.Payout);
        book.Deliver(o.Id, "p", 2, 1);
        var done = book.Complete(o.Id, 1)!;
        Assert.True(done.Completed);
        Assert.Equal(60, done.PayoutToPlayer);

        var c = book.Offer("t", "smith", Planks, 0, 4, 0);
        var cancelled = book.Cancel(c.Id, 0)!;
        Assert.Equal((OrderState.Cancelled, 0), (cancelled.To, cancelled.RefundToTrader));
        Assert.Null(book.Cancel(c.Id, 0));
    }

    [Fact]
    public void The_book_round_trips_and_forgets_old_closed_orders()
    {
        var book = new OrderBook();
        var o = book.Offer("t", "smith", Iron, 1, 4, 0);
        book.Accept(o.Id, "p", "P", 2, 64, 0);
        book.Deliver(o.Id, "p", 1, 1);
        book.Offer("t", "smith", Planks, 0.25, 3, 0);
        var copy = OrderBook.FromJson(book.ToJson());
        Assert.Equal(book.NextId, copy.NextId);
        var c = copy.Get(o.Id)!;
        Assert.Equal((OrderState.Accepted, 1, "p", o.Payout, 2), (c.State, c.Delivered, c.PlayerUid, c.Payout, c.Tier));
        Assert.Equal(0.25, copy.Get(o.Id + 1)!.Roll);
        Assert.Contains("\"Accepted\"", book.ToJson());

        copy.Tick(10);
        Assert.Equal(2, copy.All.Count());
        copy.Tick(10 + OrderBook.KeepClosedDays + 1);
        Assert.Empty(copy.All);
    }
}
