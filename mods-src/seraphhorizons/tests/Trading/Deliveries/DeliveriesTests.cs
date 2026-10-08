using SeraphHorizons.Mod.Trading.Deliveries.Core;
using SeraphHorizons.Mod.Trading.Standing.Core;

namespace SeraphHorizons.Tests.Trading.Deliveries;

/// <summary>Deliveries (#454): destinations, the deadline, deposit and fee, the state
/// machine and which standing call each outcome makes.</summary>
public class DeliveriesTests
{
    private static readonly TraderSite Smith = new("camp:0,0", "smith", 0, 0);
    private static readonly TraderSite Cook = new("camp:1,0", "cook", 2000, 0);
    private static readonly TraderSite Smith2 = new("camp:0,1", "smith", 0, 1500);
    private static readonly TraderSite Far = new("camp:3,0", "farmer", 6000, 0);
    private static readonly TraderSite Next = new("camp:9,9", "mason", 100, 100);

    [Fact]
    public void The_deadline_is_a_game_day_a_km_and_never_under_a_day()
    {
        // 2 km: two game days.
        Assert.Equal(2, DeliveryPlanner.DeadlineDays(2000), 9);
        Assert.Equal(4.5, DeliveryPlanner.DeadlineDays(4500), 9);
        // Next door: still a whole day.
        Assert.Equal(DeliveryPlanner.MinDays, DeliveryPlanner.DeadlineDays(300));
        Assert.Equal(1, DeliveryPlanner.DeadlineDays(1000), 9);
        // The offer carries it, and a delivery made from it keeps it.
        var offer = DeliveryPlanner.Offer(Smith, Cook, 1, [0.5, 0.5, 0.5]);
        Assert.Equal(2, offer.Days, 9);
        var d = new DeliveryBook().Create(offer, "p", "P", 7);
        Assert.Equal(9, d.Deadline, 9);
        Assert.Equal(9 + DeliveryPlanner.GraceDays, d.GraceUntil, 9);
    }

    [Fact]
    public void A_saved_delivery_keeps_its_deadline()
    {
        // Saved under the old rule (15 real minutes for 2 km, 0.31 of a day): loaded as it was.
        const string json = """{"next":2,"deliveries":[{"id":1,"from":"camp:0,0","to":"camp:1,0","distance":2000,"player":"p","created":10,"deadline":10.3125,"grace":11.3125,"state":"Active"}]}""";
        var d = DeliveryBook.FromJson(json).Get(1)!;
        Assert.Equal(10.3125, d.Deadline, 9);
        Assert.Equal(11.3125, d.GraceUntil, 9);
    }

    [Fact]
    public void The_destination_is_in_reach_not_next_door_and_of_another_type_where_there_is_one()
    {
        var sites = new[] { Smith, Cook, Smith2, Far, Next };
        // Scale 1 reaches 3 km: the cook and the other smith; the cook is another type.
        Assert.Equal(Cook, DeliveryPlanner.Destination(Smith, sites, 1, 0.99));
        // Scale 2 reaches 6 km: the cook or the farmer.
        Assert.Contains(DeliveryPlanner.Destination(Smith, sites, 2, 0.99)!.Value.Id, new[] { Cook.Id, Far.Id });
        // Only a smith in reach: a smith it is.
        Assert.Equal(Smith2, DeliveryPlanner.Destination(Smith, [Smith, Smith2, Next], 1, 0.5));
        // Nothing in reach, or no standing for it.
        Assert.Null(DeliveryPlanner.Destination(Smith, [Smith, Next, Far], 1, 0.5));
        Assert.Null(DeliveryPlanner.Destination(Smith, sites, 0, 0.5));
    }

    [Fact]
    public void Deposit_is_10_to_30_and_fee_20_to_40_percent_of_the_value()
    {
        Assert.Equal(16, DeliveryPlanner.Value(1, 0));
        Assert.Equal(24, DeliveryPlanner.Value(1, 1));
        Assert.Equal(40, DeliveryPlanner.Value(2, 0.5));
        Assert.Equal(2, DeliveryPlanner.Deposit(20, 0));
        Assert.Equal(6, DeliveryPlanner.Deposit(20, 1));
        Assert.Equal(4, DeliveryPlanner.Fee(20, 0));
        Assert.Equal(8, DeliveryPlanner.Fee(20, 1));
        Assert.Equal(1, DeliveryPlanner.Deposit(1, 0));
        Assert.Equal(3, DeliveryPlanner.LateFee(5));
    }

    private static (DeliveryBook Book, Delivery D) Started(double today = 10)
    {
        var book = new DeliveryBook();
        var offer = DeliveryPlanner.Offer(Smith, Cook, 1, [0.5, 0.5, 0.5]);
        return (book, book.Create(offer, "p", "P", today));
    }

    [Fact]
    public void On_time_returns_the_deposit_and_fee_with_standing_at_both_ends()
    {
        var (book, d) = Started();
        Assert.Equal(12, d.Deadline, 9);
        Assert.Equal(d.Deadline + DeliveryPlanner.GraceDays, d.GraceUntil, 9);
        Assert.Null(book.HandIn(d.Id, "q", Cook.Id, 10.1));
        Assert.Null(book.HandIn(d.Id, "p", Smith.Id, 10.1));
        var c = book.HandIn(d.Id, "p", Cook.Id, 10.1)!;
        Assert.Equal(DeliveryState.OnTime, c.To);
        Assert.Equal((d.Deposit, d.Fee), (c.DepositBack, c.Fee));
        Assert.Equal(new StandingCall(false, "p", Smith.Id, Cook.Id, true), c.Standing);
        Assert.Null(book.HandIn(d.Id, "p", Cook.Id, 10.1));
    }

    [Fact]
    public void Late_returns_the_deposit_and_half_the_fee_with_standing_at_the_receiver()
    {
        var (book, d) = Started();
        var c = book.HandIn(d.Id, "p", Cook.Id, d.Deadline + 0.5)!;
        Assert.Equal(DeliveryState.Late, c.To);
        Assert.Equal((d.Deposit, DeliveryPlanner.LateFee(d.Fee)), (c.DepositBack, c.Fee));
        Assert.False(c.Standing.Failed);
        Assert.False(c.Standing.BothEnds);
    }

    [Fact]
    public void Past_the_grace_it_fails_the_deposit_is_kept_and_the_sender_takes_the_standing()
    {
        var (book, d) = Started();
        Assert.Empty(book.Tick(d.GraceUntil));
        var c = book.Tick(d.GraceUntil + 0.01).Single();
        Assert.Equal(DeliveryState.Failed, c.To);
        Assert.Equal((0, 0), (c.DepositBack, c.Fee));
        Assert.Equal(new StandingCall(true, "p", Smith.Id, Cook.Id, false), c.Standing);
        Assert.Null(book.HandIn(d.Id, "p", Cook.Id, d.GraceUntil + 0.01));
    }

    [Fact]
    public void One_active_delivery_per_player_and_sender()
    {
        var (book, d) = Started();
        Assert.Same(d, book.ActiveFrom("p", Smith.Id));
        Assert.Null(book.ActiveFrom("p", Cook.Id));
        Assert.Null(book.ActiveFrom("q", Smith.Id));
        book.HandIn(d.Id, "p", Cook.Id, 10.1);
        Assert.Null(book.ActiveFrom("p", Smith.Id));
    }

    [Fact]
    public void Admin_expire_makes_it_late_and_simulated_days_run_the_clock()
    {
        var (book, d) = Started();
        book.Expire(d.Id, 10.05);
        Assert.Equal(DeliveryState.Late, book.HandIn(d.Id, "p", Cook.Id, 10.06)!.To);

        var (book2, d2) = Started();
        // Two days to deliver and one of grace: three simulated days leave it late, a fourth fails it.
        for (int i = 0; i < 3; i++) book2.Advance(1);
        Assert.Empty(book2.Tick(10));
        book2.Advance(1);
        Assert.Equal(DeliveryState.Failed, book2.Tick(10).Single().To);
        Assert.Null(book2.Fail(d2.Id, 10));

        var (book3, d3) = Started();
        Assert.Equal(DeliveryState.OnTime, book3.Complete(d3.Id, 99)!.To);
    }

    [Fact]
    public void The_book_round_trips()
    {
        var (book, d) = Started();
        var copy = DeliveryBook.FromJson(book.ToJson());
        var c = copy.Get(d.Id)!;
        Assert.Equal((d.From, d.To, d.Deposit, d.Fee, d.Deadline, DeliveryState.Active), (c.From, c.To, c.Deposit, c.Fee, c.Deadline, c.State));
        Assert.Equal(book.NextId, copy.NextId);
    }
}
