using SeraphHorizons.Mod.Trading.Standing.Core;

namespace SeraphHorizons.Tests.Trading.Standing;

/// <summary>Companies (#463): which group is a player's company, merge by max on joining, nothing
/// leaving with a player, penalties routed to the company and the player who took the job.</summary>
public class CompanyTests
{
    private static StandingLedger Ledger() => new(StandingTests.Rules());

    private static readonly Func<int, bool> All = _ => true;

    [Fact]
    public void The_company_is_the_chosen_group_else_the_first_one_that_exists()
    {
        var book = Ledger().Companies;
        Assert.Null(book.Resolve("p", [], All));
        Assert.Equal(4, book.Resolve("p", [4, 2], All));
        Assert.Equal(2, book.Resolve("p", [4, 2], g => g != 4));
        book.Designate("p", 2);
        Assert.Equal(2, book.Resolve("p", [4, 2], All));
        // Chosen but no longer in it, or gone: back to the first.
        Assert.Equal(4, book.Resolve("p", [4], All));
        Assert.Equal(4, book.Resolve("p", [4, 2], g => g != 2));
    }

    [Fact]
    public void Joining_a_first_group_makes_it_the_company_and_a_second_does_not()
    {
        var book = Ledger().Companies;
        book.Joined("p", 3, [3], All);
        Assert.Equal(3, book.Designated("p"));
        book.Joined("p", 5, [3, 5], All);
        Assert.Equal(3, book.Designated("p"));
        // Left the first: the next join (or the first remaining group) takes over.
        book.Joined("p", 6, [5, 6], All);
        Assert.Equal(5, book.Designated("p"));
    }

    [Fact]
    public void Forming_or_joining_takes_the_max_per_trader_once()
    {
        var ledger = Ledger();
        ledger.Set("veteran", "a", 900, 1);
        ledger.Set("veteran", "b", 50, 1);
        ledger.Set("newcomer", "b", 200, 1);
        ledger.Set("newcomer", "c", 10, 1);

        Assert.True(ledger.Companies.Sync("veteran", 1, 2));
        Assert.True(ledger.Companies.Sync("newcomer", 1, 2));
        Assert.False(ledger.Companies.Sync("newcomer", 1, 3));
        Assert.Equal(900, ledger.Companies.Get(1, "a")!.Points);
        Assert.Equal(200, ledger.Companies.Get(1, "b")!.Points);
        Assert.Equal(10, ledger.Companies.Get(1, "c")!.Points);
        // A trader reads max(personal, company): the newcomer trades at the veteran's standing.
        Assert.Equal(900, ledger.Own("newcomer", 1, "a"));
        Assert.Equal(StandingKinds.Merge, ledger.Companies.Get(1, "a")!.Events[^1].Kind);
        // The personal records are untouched.
        Assert.Null(ledger.Personal("newcomer", "a"));
        Assert.Equal(50, ledger.Personal("veteran", "b")!.Points);
    }

    [Fact]
    public void Gains_go_to_the_player_and_the_company()
    {
        var ledger = Ledger();
        ledger.Companies.Sync("p", 1, 1);
        ledger.OnDeal("p", 1, "a", 40, 0, 2);
        Assert.Equal(40, ledger.Personal("p", "a")!.Points);
        Assert.Equal(40, ledger.Companies.Get(1, "a")!.Points);
    }

    [Fact]
    public void Leaving_keeps_only_the_personal_record()
    {
        var ledger = Ledger();
        ledger.Set("veteran", "a", 900, 1);
        ledger.Set("newcomer", "a", 20, 1);
        ledger.Companies.Sync("veteran", 1, 1);
        ledger.Companies.Sync("newcomer", 1, 1);
        ledger.OnDeal("newcomer", 1, "a", 30, 0, 2);
        Assert.Equal(930, ledger.Own("newcomer", 1, "a"));

        // Left (or kicked, or the company switched): no company.
        ledger.Companies.Sync("newcomer", null, 3);
        Assert.Equal(50, ledger.Own("newcomer", null, "a"));
        Assert.DoesNotContain("newcomer", ledger.Companies.Record(1)!.Merged);
        // The company keeps everything, the veteran too.
        Assert.Equal(930, ledger.Companies.Get(1, "a")!.Points);
        Assert.Equal(930, ledger.Own("veteran", 1, "a"));

        // Joining again merges again (max, so nothing is minted).
        Assert.True(ledger.Companies.Sync("newcomer", 1, 4));
        Assert.Equal(930, ledger.Companies.Get(1, "a")!.Points);
    }

    [Fact]
    public void Penalties_hit_the_company_and_the_player_who_took_the_job_only()
    {
        var ledger = Ledger();
        ledger.Set("a", "t", 500, 1);
        ledger.Set("b", "t", 400, 1);
        ledger.Companies.Sync("a", 1, 1);
        ledger.Companies.Sync("b", 1, 1);
        ledger.OnDeliveryFailed("b", 1, "t", 2);
        Assert.Equal(350, ledger.Companies.Get(1, "t")!.Points);
        Assert.Equal(250, ledger.Personal("b", "t")!.Points);
        Assert.Equal(500, ledger.Personal("a", "t")!.Points);
        // a still reads its own 500; syncing again does not lift the company back (merged once).
        Assert.Equal(500, ledger.Own("a", 1, "t"));
        ledger.Companies.Sync("a", 1, 3);
        Assert.Equal(350, ledger.Companies.Get(1, "t")!.Points);
    }

    [Fact]
    public void A_disbanded_company_is_forgotten()
    {
        var ledger = Ledger();
        ledger.Set("p", "t", 100, 1);
        ledger.Companies.Designate("p", 1);
        ledger.Companies.Sync("p", 1, 1);
        ledger.Companies.Disbanded(1);
        Assert.Null(ledger.Companies.Record(1));
        Assert.Null(ledger.Companies.Designated("p"));
        Assert.Equal(100, ledger.Own("p", null, "t"));
    }

    [Fact]
    public void Switching_company_pools_into_the_new_one_and_leaves_the_old_one_its_standing()
    {
        var ledger = Ledger();
        ledger.Set("p", "t", 300, 1);
        ledger.Companies.Sync("p", 1, 1);
        ledger.Companies.Designate("p", 2);
        Assert.Equal(2, ledger.Companies.Resolve("p", [1, 2], All));
        ledger.Companies.Sync("p", 2, 2);
        Assert.Equal(300, ledger.Companies.Get(2, "t")!.Points);
        Assert.Equal(300, ledger.Companies.Get(1, "t")!.Points);
        Assert.DoesNotContain("p", ledger.Companies.Record(1)!.Merged);
    }
}
