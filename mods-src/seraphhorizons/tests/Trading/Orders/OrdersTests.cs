using SeraphHorizons.Mod.Trading.Orders.Core;

namespace SeraphHorizons.Tests.Trading.Orders;

/// <summary>Standing orders (#453): generation and scaling, the premium maths, the state machine and
/// which outcomes cost standing.</summary>
public class OrdersTests
{
    private static readonly OrderCandidate Iron = new("game:ingot-iron", 3, 1, 64);
    private static readonly OrderCandidate Planks = new("game:plank-oak", 0.25, 16, 64);

    [Fact]
    public void A_trader_keeps_one_or_two_open()
    {
        Assert.Equal(1, OrderPlanner.TargetOpen(0.1));
        Assert.Equal(2, OrderPlanner.TargetOpen(0.9));
    }

    [Fact]
    public void The_factor_is_between_1_3_and_1_6_and_the_days_between_3_and_6()
    {
        Assert.Equal(1.3, OrderPlanner.Factor(0));
        Assert.Equal(1.6, OrderPlanner.Factor(1));
        Assert.InRange(OrderPlanner.Factor(0.5), 1.3, 1.6);
        Assert.Equal(3, OrderPlanner.Days(0));
        Assert.Equal(6, OrderPlanner.Days(0.9999));
    }

    [Fact]
    public void The_quantity_is_worth_the_base_gears_in_whole_lots_and_grows_with_the_scale()
    {
        // 5 gears of iron at 0.625 a piece (pay, a fifth of value): 8 ingots; twice the scale, 16.
        Assert.Equal(8, OrderPlanner.Quantity(0.625, 1, 64, 1));
        Assert.Equal(16, OrderPlanner.Quantity(0.625, 1, 64, 2));
        // 5 gears of planks at 0.0625 each: 80, in lots of 16.
        Assert.Equal(80, OrderPlanner.Quantity(0.0625, 16, 64, 1));
        Assert.Equal(0, OrderPlanner.Quantity(Planks.UnitPrice, Planks.Lot, Planks.MaxStack, 1.3) % 16);
        // Never under a lot, never over four stacks.
        Assert.Equal(1, OrderPlanner.Quantity(100, 1, 64, 1));
        Assert.Equal(256, OrderPlanner.Quantity(0.01, 16, 64, 4));
    }

    [Fact]
    public void The_premium_is_the_share_over_the_normal_price_and_at_least_a_gear()
    {
        Assert.Equal(12, OrderPlanner.Premium(8, 3, 1.5));
        Assert.Equal(1, OrderPlanner.Premium(1, 0.5, 1.3));
    }

    [Fact]
    public void The_premium_is_paid_pro_rata_and_the_rest_on_completion()
    {
        Assert.Equal(3, OrderPlanner.PremiumDue(10, 3, 1, 0));
        Assert.Equal(3, OrderPlanner.PremiumDue(10, 3, 2, 3));
        Assert.Equal(4, OrderPlanner.PremiumDue(10, 3, 3, 6));
        Assert.Equal(0, OrderPlanner.PremiumDue(10, 3, 3, 10));
    }

    [Fact]
    public void Picking_skips_what_is_already_on_order()
    {
        var all = new[] { Iron, Planks };
        Assert.Equal(Planks, OrderPlanner.Pick(all, new HashSet<string> { Iron.Item }, 0));
        Assert.Null(OrderPlanner.Pick(all, new HashSet<string> { Iron.Item, Planks.Item }, 0));
        Assert.Null(OrderPlanner.Pick([new OrderCandidate("game:x", 0, 1, 1)], new HashSet<string>(), 0));
    }

    [Fact]
    public void Taking_an_order_scales_it_as_far_as_the_wallet_covers_the_larger_premium()
    {
        var book = new OrderBook();
        var o = book.Offer("camp:1,1", "smith", Iron, 8, 1.5, 4, 10);
        Assert.Equal(12, o.Reserved);
        // Scale 2: 16 ingots, a premium of 24, 12 more than held back.
        Assert.Equal((16, 24), OrderPlanner.Scaled(o, 2, 64, 100));
        // A wallet of 6: as many as 6 more gears of premium cover, 12 ingots (premium 18).
        Assert.Equal((12, 18), OrderPlanner.Scaled(o, 2, 64, 6));
        // An empty wallet keeps the offer as it is; so does a scale under 1.
        Assert.Equal((8, 12), OrderPlanner.Scaled(o, 2, 64, 0));
        Assert.Equal((8, 12), OrderPlanner.Scaled(o, 0.5, 64, 100));
    }

    [Fact]
    public void An_order_is_offered_taken_delivered_in_part_and_completed()
    {
        var book = new OrderBook();
        var o = book.Offer("camp:1,1", "smith", Iron, 8, 1.5, 4, 10);
        Assert.Equal(OrderState.Offered, o.State);
        Assert.Null(book.Deliver(o.Id, "p", 1, 10));
        Assert.True(book.Accept(o.Id, "p", "P", 8, 12, 11));
        Assert.False(book.Accept(o.Id, "q", "Q", 8, 12, 11));
        Assert.Equal(15, o.Deadline);
        Assert.Null(book.Deliver(o.Id, "q", 1, 11));

        var part = book.Deliver(o.Id, "p", 3, 12)!;
        Assert.Equal(3, part.Taken);
        Assert.Equal(4, part.PremiumToPlayer);
        Assert.False(part.Completed);
        Assert.Equal(OrderState.Accepted, o.State);

        // More than wanted: only the rest counts.
        var rest = book.Deliver(o.Id, "p", 10, 13)!;
        Assert.Equal(5, rest.Taken);
        Assert.Equal(8, rest.PremiumToPlayer);
        Assert.True(rest.Completed);
        Assert.Equal(OrderState.Done, o.State);
        Assert.Equal(12, o.PremiumPaid);
        Assert.Null(book.Deliver(o.Id, "p", 1, 13));
    }

    [Fact]
    public void Past_the_deadline_only_a_taken_order_with_nothing_delivered_is_abandoned()
    {
        var book = new OrderBook();
        var offer = book.Offer("t", "smith", Iron, 8, 1.5, 4, 0);
        var taken = book.Offer("t", "smith", Planks, 96, 1.5, 4, 0);
        var part = book.Offer("u", "cook", Iron, 8, 1.5, 4, 0);
        book.Accept(taken.Id, "p", "P", 96, taken.Reserved, 0);
        book.Accept(part.Id, "p", "P", 8, part.Reserved, 0);
        book.Deliver(part.Id, "p", 2, 1);
        Assert.Empty(book.Tick(4));
        Assert.Null(book.Deliver(part.Id, "p", 1, 4.5));

        var changes = book.Tick(4.5).ToDictionary(c => c.Order.Id);
        Assert.Equal(OrderState.Expired, changes[offer.Id].To);
        Assert.False(changes[offer.Id].Abandoned);
        Assert.Equal(offer.Reserved, changes[offer.Id].RefundToTrader);
        Assert.True(changes[taken.Id].Abandoned);
        Assert.Equal(taken.Reserved, changes[taken.Id].RefundToTrader);
        Assert.Equal(OrderState.Expired, changes[part.Id].To);
        Assert.False(changes[part.Id].Abandoned);
        Assert.Equal(part.Reserved - part.PremiumPaid, changes[part.Id].RefundToTrader);
        Assert.Empty(book.OpenAt("t"));
    }

    [Fact]
    public void Simulated_days_bring_the_deadline_closer()
    {
        var book = new OrderBook();
        var o = book.Offer("t", "smith", Iron, 8, 1.5, 4, 10);
        book.Accept(o.Id, "p", "P", 8, 12, 10);
        for (int i = 0; i < 4; i++) book.Advance(1);
        Assert.Empty(book.Tick(10));
        book.Advance(1);
        Assert.True(book.Tick(10).Single().Abandoned);
    }

    [Fact]
    public void Admin_completion_pays_the_rest_and_cancelling_refunds_the_unpaid_premium()
    {
        var book = new OrderBook();
        var o = book.Offer("t", "smith", Iron, 8, 1.5, 4, 0);
        Assert.Null(book.Complete(o.Id, 0));
        book.Accept(o.Id, "p", "P", 8, 12, 0);
        book.Deliver(o.Id, "p", 4, 1);
        var done = book.Complete(o.Id, 1)!;
        Assert.True(done.Completed);
        Assert.Equal(6, done.PremiumToPlayer);

        var c = book.Offer("t", "smith", Planks, 96, 1.5, 4, 0);
        Assert.Equal(c.Reserved, book.Cancel(c.Id, 0)!.RefundToTrader);
        Assert.Null(book.Cancel(c.Id, 0));
    }

    [Fact]
    public void The_book_round_trips_and_forgets_old_closed_orders()
    {
        var book = new OrderBook();
        var o = book.Offer("t", "smith", Iron, 8, 1.5, 4, 0);
        book.Accept(o.Id, "p", "P", 8, 12, 0);
        book.Deliver(o.Id, "p", 3, 1);
        book.Offer("t", "smith", Planks, 96, 1.4, 3, 0);
        var copy = OrderBook.FromJson(book.ToJson());
        Assert.Equal(book.NextId, copy.NextId);
        var c = copy.Get(o.Id)!;
        Assert.Equal((OrderState.Accepted, 3, "p", 12), (c.State, c.Delivered, c.PlayerUid, c.Premium));
        Assert.Contains("\"Accepted\"", book.ToJson());

        copy.Tick(10);
        Assert.Equal(2, copy.All.Count());
        copy.Tick(10 + OrderBook.KeepClosedDays + 1);
        Assert.Empty(copy.All);
    }
}
