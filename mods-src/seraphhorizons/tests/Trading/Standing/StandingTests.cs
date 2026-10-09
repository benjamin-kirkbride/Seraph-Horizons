using System.Text.Json;
using SeraphHorizons.Mod.Trading.Standing.Core;

namespace SeraphHorizons.Tests.Trading.Standing;

public class StandingTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    internal static StandingRules Shipped() =>
        JsonSerializer.Deserialize<StandingRules>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "standing-tiers.json")), Options)!;

    internal static StandingRules Rules() => new()
    {
        Tiers =
        [
            new() { Code = "stranger", Points = 0 },
            new() { Code = "known", Points = 100, Unlocks = new() { WalletTier = 0, BuyPriceFactor = 0.95 } },
            new() { Code = "regular", Points = 300, Unlocks = new() { WalletTier = 1 } },
        ],
        Points = new() { PerGear = 1, Order = 40, Delivery = 30, DeliveryFailed = 150, OrderAbandoned = 60 },
        SpilloverShare = 0.1,
        EventsKept = 3,
    };

    [Fact]
    public void The_shipped_tiers_are_five_ascending_with_growing_unlocks()
    {
        var rules = Shipped();
        Assert.Empty(rules.Problems());
        Assert.Equal(["stranger", "known", "regular", "trusted", "partner"], rules.Tiers.Select(t => t.Code));
        for (int i = 1; i < rules.Tiers.Count; i++)
        {
            var (a, b) = (rules.Tiers[i - 1].Unlocks, rules.Tiers[i].Unlocks);
            Assert.True(b.BuyPriceFactor <= a.BuyPriceFactor && b.SellPriceFactor >= a.SellPriceFactor);
            Assert.True(b.WalletTier >= a.WalletTier && b.MapTier >= a.MapTier && b.DeliveryScale >= a.DeliveryScale);
        }
        // Wallet tiers index the lists' four wallets.
        Assert.InRange(rules.Tiers.Max(t => t.Unlocks.WalletTier), 0, 3);
        Assert.Equal(0.1, rules.SpilloverShare);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(99.9, 0)]
    [InlineData(100, 1)]
    [InlineData(299, 1)]
    [InlineData(300, 2)]
    [InlineData(100000, 2)]
    public void A_tier_holds_from_its_threshold(double points, int tier)
    {
        var rules = Rules();
        Assert.Equal(tier, rules.TierIndex(points));
        Assert.Equal(tier < 2 ? rules.Tiers[tier + 1].Code : null, rules.Next(points)?.Code);
    }

    [Fact]
    public void Broken_tiers_are_reported()
    {
        var rules = Rules();
        rules.Tiers[0].Points = 5;
        rules.Tiers[2].Points = 50;
        rules.Tiers[1].Unlocks.BuyPriceFactor = 0;
        var problems = rules.Problems();
        Assert.Contains(problems, p => p.Contains("not 0"));
        Assert.Contains(problems, p => p.Contains("regular"));
        Assert.Contains(problems, p => p.Contains("price factor"));
    }

    [Fact]
    public void A_deal_scores_its_gears_both_ways_and_records_the_event()
    {
        var ledger = new StandingLedger(Rules());
        ledger.OnDeal("p", null, "camp:0,0", 12, 5, day: 3);
        var r = ledger.Personal("p", "camp:0,0")!;
        Assert.Equal(17, r.Points);
        Assert.Equal(3, r.LastDay);
        Assert.Equal(StandingKinds.Deal, Assert.Single(r.Events).Kind);
    }

    [Fact]
    public void Standing_is_lost_only_by_penalties_and_never_below_zero()
    {
        var ledger = new StandingLedger(Rules());
        ledger.OnDeal("p", null, "t", 100, 0, 1);
        ledger.OnOrderDone("p", null, "t", 2);
        Assert.Equal(140, ledger.Personal("p", "t")!.Points);
        ledger.OnOrderAbandoned("p", null, "t", 3);
        Assert.Equal(80, ledger.Personal("p", "t")!.Points);
        ledger.OnDeliveryFailed("p", null, "t", 4);
        Assert.Equal(0, ledger.Personal("p", "t")!.Points);
        // The ring keeps the last three, and records what was actually taken.
        var events = ledger.Personal("p", "t")!.Events;
        Assert.Equal(3, events.Count);
        Assert.Equal(-80, events[^1].Points);
    }

    [Fact]
    public void A_delivery_on_time_credits_both_ends_and_a_late_one_the_receiver()
    {
        var ledger = new StandingLedger(Rules());
        ledger.OnDeliveryDone("p", null, "a", "b", bothEnds: true, 1);
        Assert.Equal(30, ledger.Personal("p", "a")!.Points);
        Assert.Equal(30, ledger.Personal("p", "b")!.Points);
        ledger.OnDeliveryDone("p", null, "a", "b", bothEnds: false, 2);
        Assert.Equal(30, ledger.Personal("p", "a")!.Points);
        Assert.Equal(60, ledger.Personal("p", "b")!.Points);
    }

    [Fact]
    public void Spillover_is_a_tenth_of_the_best_same_type_trader_in_range()
    {
        var self = new TraderSite("camp:0,0", "smith", 1000, 1000);
        var sites = new[]
        {
            self,
            new TraderSite("camp:1,0", "smith", 3000, 1000),   // 2 km
            new TraderSite("camp:3,0", "smith", 7100, 1000),   // 6.1 km: out
            new TraderSite("camp:0,1", "farmer", 1000, 3000),  // another type
            new TraderSite("camp:2,2", "smith", 5000, 5000),   // 5.66 km
        };
        var near = Spillover.Neighbours(self, sites, 6000).Select(s => s.Id).ToList();
        Assert.Equal(["camp:1,0", "camp:2,2"], near);

        var ledger = new StandingLedger(Rules());
        ledger.Set("p", "camp:1,0", 500, 1);
        ledger.Set("p", "camp:2,2", 900, 1);
        ledger.Set("p", "camp:3,0", 5000, 1);
        ledger.Set("p", "camp:0,0", 50, 1);
        var view = ledger.View("p", null, "camp:0,0", near);
        Assert.Equal(90, view.Spill);
        Assert.Equal(140, view.Effective);
        Assert.Equal("known", view.Tier.Code);
        Assert.Equal(0, Spillover.Bonus([], 0.1));
    }

    [Fact]
    public void Recent_players_are_those_whose_record_changed_since()
    {
        var ledger = new StandingLedger(Rules());
        ledger.OnDeal("old", null, "t", 10, 0, 1);
        ledger.OnDeal("new", null, "t", 10, 0, 20);
        ledger.OnDeal("elsewhere", null, "u", 10, 0, 20);
        Assert.Equal(["new"], ledger.RecentPlayers("t", 10));
    }

    [Fact]
    public void Admin_set_and_reset_touch_only_the_players_own_records()
    {
        var ledger = new StandingLedger(Rules());
        ledger.Set("p", "t", 250, 1);
        ledger.Set("p", "u", 10, 1);
        Assert.Equal(250, ledger.Personal("p", "t")!.Points);
        Assert.Equal(StandingKinds.Admin, ledger.Personal("p", "t")!.Events[^1].Kind);
        ledger.Set("p", "t", 20, 2);
        Assert.Equal(20, ledger.Personal("p", "t")!.Points);
        ledger.Reset("p", "t");
        Assert.Null(ledger.Personal("p", "t"));
        Assert.NotNull(ledger.Personal("p", "u"));
        ledger.Reset("p", null);
        Assert.Empty(ledger.PersonalRecords("p"));
    }

    [Fact]
    public void The_state_round_trips_as_json()
    {
        var ledger = new StandingLedger(Rules());
        ledger.OnDeal("p", 7, "camp:1,2", 30, 0, 1);
        ledger.Companies.Designate("p", 7);
        ledger.Companies.Sync("p", 7, 1);
        string json = JsonSerializer.Serialize(ledger.State);
        var back = new StandingLedger(Rules(), JsonSerializer.Deserialize<StandingState>(json)!);
        Assert.Equal(30, back.Personal("p", "camp:1,2")!.Points);
        Assert.Equal(30, back.Companies.Get(7, "camp:1,2")!.Points);
        Assert.Equal(7, back.Companies.Designated("p"));
        Assert.Contains("p", back.Companies.Record(7)!.Merged);
    }
}
