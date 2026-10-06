using SeraphHorizons.Mod.Trading.Core;
using SeraphHorizons.Mod.Trading.Visitors.Core;

namespace SeraphHorizons.Mod.Tests.Trading.Visitors;

public class VisitScheduleTests
{
    private static InnRecord Idle() => new() { X = 10, Y = 70, Z = -5 };

    [Fact]
    public void An_idle_inn_is_evaluated_once_a_day()
    {
        var r = Idle();
        Assert.Equal(VisitAction.Evaluate, VisitPlanner.Due(r, 3.2));
        Assert.Null(VisitPlanner.Evaluated(r, 3.2, [], ["bed"], 0, 0));
        Assert.Equal(["bed"], r.LastMissing);
        Assert.Equal(VisitAction.None, VisitPlanner.Due(r, 3.9));
        Assert.Equal(VisitAction.Evaluate, VisitPlanner.Due(r, 4.0));
    }

    [Fact]
    public void The_whole_cycle_runs_from_a_passing_day_to_the_end_of_the_cooldown()
    {
        var s = new VisitSettings();
        var r = Idle();
        // Every condition passes on day 10: a visitor is scheduled 2–4 days on.
        Assert.Equal(VisitorKinds.Curio, VisitPlanner.Evaluated(r, 10, [VisitorKinds.General, VisitorKinds.Curio], [], 0.9, 0.5, s));
        Assert.Equal(InnPhase.Pending, r.Phase);
        Assert.Equal(13, r.ArriveDay, 6);
        Assert.Equal(VisitAction.None, VisitPlanner.Due(r, 12.9));
        Assert.Equal(VisitAction.Arrive, VisitPlanner.Due(r, 13));

        VisitPlanner.Arrived(r, 4242, 13, 1, s);
        Assert.Equal(InnPhase.Visiting, r.Phase);
        Assert.Equal(4242, r.EntityId);
        Assert.Equal(18, r.LeaveDay, 6);
        Assert.Equal(VisitAction.None, VisitPlanner.Due(r, 17.99));
        Assert.Equal(VisitAction.Leave, VisitPlanner.Due(r, 18));

        VisitPlanner.Left(r, 18, s);
        Assert.Equal(InnPhase.Cooldown, r.Phase);
        Assert.Equal(0, r.EntityId);
        Assert.Equal(28, r.CooldownUntil, 6);
        Assert.Equal(VisitAction.None, VisitPlanner.Due(r, 27));
        Assert.Equal(VisitAction.CooldownOver, VisitPlanner.Due(r, 28));
        VisitPlanner.CooledDown(r);
        Assert.Equal(VisitAction.Evaluate, VisitPlanner.Due(r, 28));
    }

    [Fact]
    public void Delays_and_stays_stay_inside_their_ranges()
    {
        var s = new VisitSettings();
        foreach (double roll in new[] { -1, 0, 0.3, 1, 2 })
        {
            var r = Idle();
            VisitPlanner.Schedule(r, VisitorKinds.General, 0, roll, s);
            Assert.InRange(r.ArriveDay, s.DelayMinDays, s.DelayMaxDays);
            VisitPlanner.Arrived(r, 1, 0, roll, s);
            Assert.InRange(r.LeaveDay, s.StayMinDays, s.StayMaxDays);
        }
    }

    [Fact]
    public void Nothing_is_scheduled_while_a_visit_is_on_or_cooling_down()
    {
        var r = Idle();
        VisitPlanner.Schedule(r, VisitorKinds.General, 0, 0);
        Assert.Null(VisitPlanner.Evaluated(r, 1, [VisitorKinds.Curio], [], 0, 0));
        Assert.Equal(VisitorKinds.General, r.Kind);
        VisitPlanner.Left(r, 1);
        Assert.Equal(VisitAction.None, VisitPlanner.Due(r, 5));
    }

    [Fact]
    public void A_pending_visit_called_off_goes_back_to_idle_without_a_cooldown()
    {
        var r = Idle();
        VisitPlanner.Schedule(r, VisitorKinds.General, 0, 0);
        VisitPlanner.Cancelled(r);
        Assert.Equal(InnPhase.Idle, r.Phase);
        Assert.Equal("", r.Kind);
    }

    [Fact]
    public void The_book_round_trips_through_json_and_finds_the_nearest_flag()
    {
        var book = new InnBook { Offset = 5 };
        var a = book.Raise(new InnPos(0, 70, 0), "uid-a");
        book.Raise(new InnPos(10, 70, 0), "");
        VisitPlanner.Schedule(a, VisitorKinds.Curio, 2, 0);
        VisitPlanner.Arrived(a, 77, 4, 0);
        var back = InnBook.FromJson(book.ToJson());
        Assert.Equal(5, back.Offset);
        Assert.Equal(2, back.Inns.Count);
        var a2 = back.Get(new InnPos(0, 70, 0))!;
        Assert.Equal(InnPhase.Visiting, a2.Phase);
        Assert.Equal("uid-a", a2.Owner);
        Assert.Equal(VisitorKinds.Curio, a2.Kind);
        Assert.Same(a2, back.ByEntity(77));
        Assert.Null(back.ByEntity(0));
        Assert.Equal(new InnPos(10, 70, 0), back.Nearest(new InnPos(8, 71, 1), 16)!.Pos);
        Assert.Null(back.Nearest(new InnPos(50, 70, 0), 16));
        // Raising an existing flag again keeps its owner unless someone raises it.
        Assert.Equal("uid-a", back.Raise(new InnPos(0, 70, 0), "").Owner);
        Assert.NotNull(back.Lower(new InnPos(0, 70, 0)));
        Assert.Null(back.Get(new InnPos(0, 70, 0)));
    }

    [Fact]
    public void Kinds_map_to_their_trader_types_and_standing_ids()
    {
        Assert.Equal(TraderTypes.TravellingMerchant, VisitorKinds.TypeOf(VisitorKinds.General));
        Assert.Equal(TraderTypes.TravellingCurio, VisitorKinds.TypeOf(VisitorKinds.Curio));
        Assert.Equal(VisitorKinds.Curio, VisitorKinds.KindOf(TraderTypes.TravellingCurio));
        Assert.Null(VisitorKinds.KindOf(TraderTypes.Smith));
        Assert.Equal("visitor:general", VisitorKinds.TraderId(VisitorKinds.General));
        Assert.True(SeraphHorizons.Mod.Trading.Standing.Core.TraderIds.IsValid(VisitorKinds.TraderId(VisitorKinds.Curio)));
        Assert.Empty(TraderTypes.Visitors.Intersect(TraderTypes.All));
    }
}
