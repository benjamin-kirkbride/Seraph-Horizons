using SeraphHorizons.Mod.Trading.Economy.Core;
using SeraphHorizons.Mod.Trading.Standing.Core;
using SeraphHorizons.Mod.Trading.Window.Core;

namespace SeraphHorizons.Mod.Tests.Trading.Window;

public class TradeWindowModelTests
{
    private static readonly string[] Codes = ["stranger", "known", "regular", "trusted", "partner"];
    private static readonly double[] Thresholds = [0, 60, 250, 800, 2000];
    private static readonly int[] LeadMaps = [0, 2, 3, 5, 8];
    private static readonly int[] LeadReach = [1, 1, 2, 3, 5];

    /// <summary>The shipped tiers' shape: the five, their thresholds and the unlocks that matter here.</summary>
    internal static StandingSummary Summary(int tier, double points) => new()
    {
        TierIndex = tier,
        Points = points,
        Earn = new StandingPoints { PerGear = 1, Order = 40, Delivery = 30 },
        Tiers = Codes.Select((c, i) => new TierView
        {
            Code = c,
            Number = i + 1,
            Points = Thresholds[i],
            MapPrecision = Math.Min(3, i + 1),
            LeadMaps = LeadMaps[i],
            LeadReach = LeadReach[i],
            Unlocks = new TierUnlocks
            {
                MapTier = i,
                MapsToTraders = i >= 2,
                BuyPriceFactor = i == 0 ? 1 : 1 - 0.03 * i,
                SellPriceFactor = i == 0 ? 1 : 1 + 0.02 * i,
                WalletFactor = new[] { 1.0, 2, 5, 15, 40 }[i],
                DeliveryScale = i == 0 ? 0 : 0.5 + 0.5 * i,
                RareStock = i >= 3,
            },
        }).ToList(),
    };

    internal static WindowSwitches AllOn() => new()
    {
        Standing = true, Orders = true, Deliveries = true, Maps = true, EverythingPriced = true, StandingPrices = true,
    };

    private static TradeWindowState State(WindowSwitches? sw = null) => new()
    {
        TraderId = 7,
        Switches = sw ?? AllOn(),
        Standing = Summary(2, 310),
        Orders =
        [
            new OrderRow { Id = 3, Item = "game:ingot-copper", Quantity = 4, Value = 1.4, Payout = 56, Days = 4, DaysLeft = 2.5 },
            new OrderRow { Id = 1, Item = "game:ingot-tin", Quantity = 8, Delivered = 2, Value = 1.2, Payout = 96, PayoutPaid = 24, DaysLeft = 1.25, Mine = true, Held = 3 },
        ],
        DeliveryOffer = new DeliveryOfferRow { ToType = "cook", Distance = 2150, Dx = 1500, Dz = -1500, Days = 2.2, Deposit = 4, Fee = 6 },
    };

    [Fact]
    public void EveryTabShowsWithEveryFeatureOnWithItsCount()
    {
        var tabs = TradeWindowModel.Tabs(State());
        Assert.Equal([WindowTab.Trade, WindowTab.Orders, WindowTab.Deliveries, WindowTab.Maps, WindowTab.Standing], tabs.Select(t => t.Tab));
        Assert.Equal("trading-window-tab-orders(2)", tabs[1].Label.ToString());
        Assert.Equal("trading-window-tab-deliveries(1)", tabs[2].Label.ToString());
    }

    [Fact]
    public void ASwitchedOffFeatureHasNoTab()
    {
        var tabs = TradeWindowModel.Tabs(State(new WindowSwitches { Standing = false, Orders = false, Deliveries = true, Maps = false }));
        Assert.Equal([WindowTab.Trade, WindowTab.Deliveries], tabs.Select(t => t.Tab));
    }

    [Fact]
    public void TheStandingBarShowsTheRawNumbersInBrackets()
    {
        var bar = TradeWindowModel.Bar(Summary(2, 310.7), AllOn())!;
        Assert.Equal("regular", bar.TierCode);
        Assert.Equal(800, bar.NextPoints);
        Assert.Equal((310.7 - 250) / 550, bar.Fraction, 6);
        Assert.Equal("trading-window-standing-bar(trading-standing-tier-regular, 310, 800)", bar.Label.ToString());
    }

    [Fact]
    public void AtTheTopTierTheBarIsFullAndHasNoNext()
    {
        var bar = TradeWindowModel.Bar(Summary(4, 2400), AllOn())!;
        Assert.Null(bar.NextPoints);
        Assert.Equal(1, bar.Fraction);
        Assert.StartsWith("trading-window-standing-top", bar.Label.Key);
    }

    [Fact]
    public void NoBarWithStandingOff() => Assert.Null(TradeWindowModel.Bar(Summary(1, 70), new WindowSwitches()));

    [Fact]
    public void TheFooterNamesTheSideBudgetByTheTradersGender()
    {
        Assert.Equal(["trading-window-footer-you(34)", "trading-window-footer-trader(Hanna, 120)", "trading-window-footer-side-her(21)"],
            TradeWindowModel.Footer(34, "Hanna", 120, 21, female: true).Select(t => t.ToString()));
        Assert.Equal("trading-window-footer-side-his(5)", TradeWindowModel.Footer(1, "Jon", 2, 5, female: false)[2].ToString());
        Assert.Equal(2, TradeWindowModel.Footer(1, "Jon", 2, null, female: false).Count);
    }

    [Fact]
    public void TheHeaderNamesTypeAndRegion() =>
        Assert.Equal("trading-window-header(Hanna, trading-type-smith, trading-region-cold, trading-region-igneous)",
            TradeWindowModel.Header("Hanna", "smith", "cold", "igneous").ToString());

    [Fact]
    public void TakenOrdersComeFirstAndCanBeHandedInWhenThePlayerCarriesTheGoods()
    {
        var lines = TradeWindowModel.Orders(State());
        Assert.Equal([1, 3], lines.Select(l => l.Row.Id));
        Assert.True(lines[0].CanHandIn);
        Assert.False(lines[0].CanTake);
        Assert.Equal("trading-window-order-taken(8, game:ingot-tin, 2, 72, 1.25)", lines[0].Line.ToString());
        Assert.True(lines[1].CanTake);
        Assert.Equal("trading-window-order-offer(4, game:ingot-copper, 5.6, 56, 4, 2.5)", lines[1].Line.ToString());
    }

    [Fact]
    public void NothingToHandInWithoutTheGoodsOrPastTheDeadline()
    {
        var state = State();
        state.Orders[1].Held = 0;
        Assert.False(TradeWindowModel.Orders(state)[0].CanHandIn);
        state.Orders[1].Held = 5;
        state.Orders[1].DaysLeft = -0.1;
        Assert.False(TradeWindowModel.Orders(state)[0].CanHandIn);
    }

    [Theory]
    [InlineData(0, -100, "n")]
    [InlineData(100, -100, "ne")]
    [InlineData(100, 0, "e")]
    [InlineData(100, 100, "se")]
    [InlineData(0, 100, "s")]
    [InlineData(-100, 100, "sw")]
    [InlineData(-100, 0, "w")]
    [InlineData(-100, -100, "nw")]
    [InlineData(10, -100, "n")]
    public void DirectionsAreTheGamesCompassNorthIsMinusZ(double dx, double dz, string dir) =>
        Assert.Equal("trading-window-dir-" + dir, TradeWindowModel.Direction(dx, dz).Key);

    [Fact]
    public void TheDeliveryOfferHasTakeAndMarkAndAPackageForHereCanBeHandedIn()
    {
        var state = State();
        state.Deliveries.Add(new DeliveryRow { Id = 9, ToType = "smith", ForHere = true, Carried = true, Deposit = 3, Fee = 5, HoursLeft = 2 });
        state.Deliveries.Add(new DeliveryRow { Id = 4, ToType = "farmer", Distance = 900, Dx = -900, HoursLeft = -1.5 });
        state.Deliveries.Add(new DeliveryRow { Id = 5, ToType = "mason", Distance = 3000, Dz = 3000, HoursLeft = 60, DaysLeft = 2.5 });
        state.Deliveries.Add(new DeliveryRow { Id = 6, ToType = "mason", Distance = 1000, Dz = -1000, HoursLeft = 7.5, DaysLeft = 0.3125 });
        var lines = TradeWindowModel.Deliveries(state);
        Assert.Equal(5, lines.Count);
        Assert.True(lines[0].CanHandIn);
        Assert.Equal("trading-window-delivery-forhere(9, 3, 5)", lines[0].Line.ToString());
        Assert.Equal("trading-window-delivery-late(4, trading-type-farmer, 0.9, trading-window-dir-w, 1.5)", lines[1].Line.ToString());
        Assert.True(lines[1].CanMark);
        // Time left in days, and in hours under a day.
        Assert.Equal("trading-window-delivery-mine(5, trading-type-mason, 3, trading-window-dir-s, trading-window-days(2.5), 0, 0)", lines[2].Line.ToString());
        Assert.Equal("trading-window-delivery-mine(6, trading-type-mason, 1, trading-window-dir-n, trading-window-hours(7.5), 0, 0)", lines[3].Line.ToString());
        Assert.Null(lines[4].Row);
        Assert.True(lines[4].CanTake && lines[4].CanMark);
        Assert.Equal("trading-window-delivery-offer(trading-type-cook, 2.2, trading-window-dir-ne, trading-window-days(2.2), 4, 6)", lines[4].Line.ToString());
    }

    [Fact]
    public void WithoutAnOfferTheTabSaysWhy()
    {
        var state = State();
        state.DeliveryOffer = null;
        state.DeliveryWhy = "trading-deliveries-notyet";
        var line = Assert.Single(TradeWindowModel.Deliveries(state));
        Assert.False(line.CanTake);
        Assert.Equal("trading-deliveries-notyet", line.Line.Key);
    }

    [Theory]
    [InlineData("oremap", null, 1, true, MapOfferStatus.Available)]
    [InlineData("soldout", null, 1, true, MapOfferStatus.SoldOut)]
    [InlineData("oremap", null, 0, true, MapOfferStatus.SoldOut)]
    [InlineData("lead", "camp", 2, false, MapOfferStatus.Available)]
    [InlineData("lead", "prospector", 2, false, MapOfferStatus.Locked)]
    [InlineData("lead", "settlement", 2, false, MapOfferStatus.Locked)]
    [InlineData("lead", "far", 2, true, MapOfferStatus.Available)]
    [InlineData("surveying", null, 0, true, MapOfferStatus.Surveying)]
    [InlineData("surveying", null, 1, true, MapOfferStatus.Surveying)]
    public void MapOffersAreSoldOutLockedOrForSale(string offer, string? kind, int stock, bool leads, MapOfferStatus expected) =>
        Assert.Equal(expected, TradeWindowModel.MapStatus(offer, kind, stock, leads));

    [Fact]
    public void AnOreMapOfferNamesTheOreItsGradesAndRock()
    {
        var line = TradeWindowModel.OreMapLine("lead", ["galena", "cerussite"], "medium", "mostly:poor", "Limestone", 2400.4, 2);
        Assert.Equal("trading-window-map-ore-named(ore-list-and(orename-galena, orename-cerussite), "
                     + "ore-list-comma(ore-list-comma(ore-size-medium, ore-grades-mostly(ore-grade-poor)), trading-window-map-ore-host(Limestone)), 2400, 2)",
            line.ToString());
        // Three ores; no grades or rock known.
        Assert.Equal("trading-window-map-ore-named(ore-list-and(ore-list-comma(orename-a, orename-b), orename-c), ore-size-small, 10, 1)",
            TradeWindowModel.OreMapLine("x", ["a", "b", "c"], "small", null, null, 10, 1).ToString());
        // Shelved before maps named the ore: the metal.
        Assert.Equal("trading-window-map-ore", TradeWindowModel.OreMapLine("copper", [], null, null, null, 10, 1).Key);
        Assert.Equal("ore-grades-mixed(ore-grade-poor, ore-grade-medium)", TradeWindowModel.GradeText("mixed:poor,medium")!.ToString());
        Assert.Null(TradeWindowModel.GradeText("nonsense"));
    }

    [Fact]
    public void AGravelMapOfferNamesItsRockAndMetalsAndASurveyIsSaid()
    {
        Assert.Equal("trading-window-map-gravel-pans(Granite, ore-list-and(ore-metal-tin, ore-metal-gold), 300)",
            TradeWindowModel.GravelMapLine("Granite", ["tin", "gold"], 300).ToString());
        Assert.Equal("trading-window-map-gravel-rock", TradeWindowModel.GravelMapLine("Granite", [], 300).Key);
        Assert.Equal("trading-window-map-gravel", TradeWindowModel.GravelMapLine(null, [], 300).Key);
        Assert.Equal("trading-window-map-ore-surveying(oremap-metal-lead)", TradeWindowModel.SurveyingLine("lead").ToString());
        Assert.Equal("trading-window-map-gravel-surveying", TradeWindowModel.SurveyingLine("gravel").Key);
        Assert.Equal("trading-window-map-surveying", TradeWindowModel.MapStatusText(MapOfferStatus.Surveying, null)!.Key);
    }

    [Fact]
    public void AMapThePlayerHasIsGreyedOutWithYouHaveThis()
    {
        Assert.Equal(MapOfferStatus.Owned, TradeWindowModel.MapStatus("lead", "camp", 2, false, owned: true));
        Assert.Equal(MapOfferStatus.Owned, TradeWindowModel.MapStatus("gravelmap", null, 1, true, owned: true));
        // Sold out says so first.
        Assert.Equal(MapOfferStatus.SoldOut, TradeWindowModel.MapStatus("lead", "camp", 0, true, owned: true));
        Assert.Equal("trading-window-map-owned", TradeWindowModel.MapStatusText(MapOfferStatus.Owned, null)!.ToString());
        Assert.Equal("trading-window-map-soldout", TradeWindowModel.MapStatusText(MapOfferStatus.SoldOut, null)!.ToString());
        Assert.Equal("trading-window-map-locked(trading-standing-tier-trusted)",
            TradeWindowModel.MapStatusText(MapOfferStatus.Locked, TradeWindowModel.TierName("trusted"))!.ToString());
        Assert.Null(TradeWindowModel.MapStatusText(MapOfferStatus.Available, null));
        // The state carries which shelf slots they are.
        var state = TradeWindowState.FromJson(new TradeWindowState { OwnedMaps = [3, 7] }.ToJson());
        Assert.Equal([3, 7], state.OwnedMaps);
    }

    [Fact]
    public void TheStandingTabMarksTheCurrentTierWithItsThreshold()
    {
        var tiers = TradeWindowModel.Tiers(Summary(2, 310));
        Assert.Equal(5, tiers.Count);
        Assert.Equal([false, false, true, false, false], tiers.Select(t => t.Current));
        Assert.Equal("trading-window-tier-row(trading-standing-tier-trusted, 800)", tiers[3].Line.ToString());
    }

    [Fact]
    public void FactsFollowTheSwitches()
    {
        var tier = Summary(3, 900).Tier;
        var all = TradeWindowModel.Facts(tier, AllOn()).Select(f => f.Key).ToList();
        Assert.Contains("trading-window-fact-prices", all);
        Assert.Contains("trading-window-fact-orders", all);
        // Trusted, n = 4: 2.125–10 gears of goods, paid ×17.5.
        Assert.Contains("trading-window-fact-orders(2.13, 10, 17.5)", TradeWindowModel.Facts(tier, AllOn()).Select(f => f.ToString()));
        // A trusted customer's trader restocks to 15 times its wallet; a stranger's to the wallet.
        Assert.Contains("trading-window-fact-wallet(15)", TradeWindowModel.Facts(tier, AllOn()).Select(f => f.ToString()));
        Assert.DoesNotContain("trading-window-fact-wallet", TradeWindowModel.Facts(Summary(0, 0).Tier, AllOn()).Select(f => f.Key));
        Assert.Contains("trading-window-fact-deliveries", all);
        Assert.Contains("trading-window-fact-leads", all);
        Assert.Contains("trading-window-fact-settlement", all);
        Assert.Contains("trading-window-fact-rare", all);
        var few = TradeWindowModel.Facts(tier, new WindowSwitches { Standing = true }).Select(f => f.Key).ToList();
        Assert.DoesNotContain("trading-window-fact-prices", few);
        Assert.DoesNotContain("trading-window-fact-orders", few);
        Assert.DoesNotContain("trading-window-fact-maps", few);
        Assert.Contains("trading-window-fact-rare", few);
    }

    [Fact]
    public void DeliveryReachIsTheScaleTimesThreeKm() =>
        Assert.Contains("trading-window-fact-deliveries(4.5)", TradeWindowModel.Facts(Summary(2, 300).Tier, AllOn()).Select(f => f.ToString()));

    [Fact]
    public void TheNextTierShowsOnlyWhatChanges()
    {
        var s = Summary(2, 300);
        var gains = TradeWindowModel.Gains(s.Tier, s.Next!, AllOn()).Select(f => f.Key).ToList();
        Assert.Contains("trading-window-fact-rare", gains);
        Assert.Contains("trading-window-fact-prices", gains);
        // Regular to trusted: more camp leads further out; the settlement lead both have.
        Assert.Contains("trading-window-fact-leads(5, 3)", TradeWindowModel.Gains(s.Tier, s.Next!, AllOn()).Select(f => f.ToString()));
        Assert.DoesNotContain("trading-window-fact-settlement", gains);
    }

    [Fact]
    public void CampLeadFactsSayHowManyAndHowFarAndAStrangerGetsOneOnward()
    {
        var s = Summary(0, 0);
        var stranger = TradeWindowModel.Facts(s.Tiers[0], AllOn()).Select(f => f.ToString()).ToList();
        Assert.Contains("trading-window-fact-leads-stranger", stranger);
        Assert.DoesNotContain("trading-window-fact-settlement", stranger);
        // In rings of grid cells; one ring reads "within 1 ring".
        Assert.Contains("trading-window-fact-leads-one(2)", TradeWindowModel.Facts(s.Tiers[1], AllOn()).Select(f => f.ToString()));
        Assert.Contains("trading-window-fact-leads(3, 2)", TradeWindowModel.Facts(s.Tiers[2], AllOn()).Select(f => f.ToString()));
        Assert.Contains("trading-window-fact-leads(8, 5)", TradeWindowModel.Facts(s.Tiers[4], AllOn()).Select(f => f.ToString()));
    }

    [Fact]
    public void LeadOfferLinesNameTheCampItsWayRingAndPriceAndTheNoteSaysWhyNone()
    {
        var row = new LeadOfferRow { Cell = "1,0", Type = "smith", Distance = 2210.4, Dx = 2200, Dz = 0, Price = 3, Ring = 1 };
        Assert.Equal("trading-window-lead-offer", TradeWindowModel.LeadOfferLine(row).Key);
        Assert.Contains("2210", TradeWindowModel.LeadOfferLine(row).ToString());
        Assert.EndsWith(", 3, 1)", TradeWindowModel.LeadOfferLine(row).ToString());
        Assert.Equal("trading-window-lead-offer-prospector", TradeWindowModel.LeadOfferLine(new LeadOfferRow { Type = "prospector", Prospector = true }).Key);
        var none = new TradeWindowState { LeadsWhy = "trading-window-leads-strangerused" };
        Assert.Equal("trading-window-leads-strangerused", TradeWindowModel.LeadOffersNote(none)!.Key);
        Assert.Null(TradeWindowModel.LeadOffersNote(new TradeWindowState()));
        var some = TradeWindowState.FromJson(new TradeWindowState { LeadOffers = [row], LeadsBought = 2 }.ToJson());
        Assert.Equal("1,0", some.LeadOffers.Single().Cell);
        Assert.Equal(1, some.LeadOffers.Single().Ring);
        Assert.Equal("trading-window-leads-bought(2)", TradeWindowModel.LeadOffersNote(some)!.ToString());
    }

    [Fact]
    public void ThePityMapsLineSaysTheFirstMapIsOnTheTrader()
    {
        var pity = new LeadOfferRow { Cell = "1,1", Type = "prospector", Distance = 2900, Dx = 2000, Dz = 2000, Price = 10, Ring = 1, Prospector = true, Pity = true };
        Assert.Equal("trading-window-lead-offer-pity", TradeWindowModel.LeadOfferLine(pity).Key);
        Assert.Equal("trading-window-lead-offer-pity-any", TradeWindowModel.LeadOfferLine(new LeadOfferRow { Type = "cook", Pity = true }).Key);
        Assert.True(TradeWindowState.FromJson(new TradeWindowState { LeadOffers = [pity] }.ToJson()).LeadOffers.Single().Pity);
    }

    [Fact]
    public void StandingSectionsSayHowFarToTheNextTier()
    {
        var sections = TradeWindowModel.StandingSections(State());
        Assert.Equal(3, sections.Count);
        Assert.Equal("trading-window-standing-next(trading-standing-tier-trusted, 800, 490)", sections[1].Heading.ToString());
        Assert.Contains(sections[2].Lines, l => l.ToString() == "trading-window-earn-orders(40, 1)");
        Assert.Contains(sections[2].Lines, l => l.Key == "trading-window-earn-deliveries-offered");
    }

    [Fact]
    public void ListedOffersArePaidFromTheWalletOffListFromTheSideBudget()
    {
        var listed = Pricing.Listed(2, 4, 1, 1);
        Assert.Equal(["trading-economy-offer-listed(2, 4, 1)", "trading-window-sell-lots(2, 4)"],
            TradeWindowModel.OfferLines(listed, true, 20, stackSize: 9).Select(t => t.ToString()));
        var off = Pricing.OffList(10, false, 0.2, 1, 1.05, 64);
        var lines = TradeWindowModel.OfferLines(off, false, 21, stackSize: 0).Select(t => t.ToString()).ToList();
        Assert.Equal("trading-economy-offer-offlist(2, 1, 10, 0.2, 1)", lines[0]);
        Assert.Equal("trading-economy-offer-modifiers(1.05)", lines[1]);
        Assert.Equal("trading-economy-offer-sidebudget(21)", lines[2]);
        Assert.Equal("trading-window-sell-short(1)", lines[3]);
        // Goods a related trader buys come from the wallet; its own shelf's goods are bought back cheap.
        var related = Pricing.OffList(10, false, 0.75, 1, 1, 64, budget: Budget.Main);
        Assert.Equal("trading-economy-offer-mainwallet", TradeWindowModel.OfferLines(related, false, 21)[1].ToString());
        var own = Pricing.OwnShelf(10, false, 0.2, 1, 1, 64);
        Assert.Equal(["trading-economy-offer-ownshelf(2, 1, 10, 0.2, 1)", "trading-economy-offer-sidebudget(21)"],
            TradeWindowModel.OfferLines(own, false, 21).Select(t => t.ToString()));
    }

    [Fact]
    public void RefusedAndUnwantedGoodsSayWhy()
    {
        Assert.Equal("trading-economy-refused-map", Assert.Single(TradeWindowModel.OfferLines(Offer.Refused(Refusal.MapOrLead), false, 0)).Key);
        Assert.Equal("trading-window-pays-none", Assert.Single(TradeWindowModel.OfferLines(null, false, 0)).Key);
        foreach (var r in Enum.GetValues<Refusal>().Where(r => r != Refusal.None))
            Assert.StartsWith("trading-economy-refused-", TradeWindowModel.RefusalKey(r));
    }

    [Fact]
    public void ShelfDetailsSayHowToTrade()
    {
        Assert.Equal(["trading-window-selected(2, game:bread)", "trading-window-selected-price(3, 2)", "trading-window-selected-stock(5)", "trading-window-hold-buy"],
            TradeWindowModel.ShelfDetails(new ItemRef("game:bread"), 2, 3, 5, traderSells: true, lockedForMe: false).Select(t => t.ToString()));
        Assert.Contains("trading-window-selected-soldout", TradeWindowModel.ShelfDetails(new ItemRef("game:bread"), 2, 3, 0, true, false).Select(t => t.Key));
        Assert.DoesNotContain("trading-window-hold-buy", TradeWindowModel.ShelfDetails(new ItemRef("game:bread"), 2, 3, 0, true, false).Select(t => t.Key));
        Assert.Contains("trading-window-selected-pays", TradeWindowModel.ShelfDetails(new ItemRef("game:ingot-iron"), 1, 2, 9, false, false).Select(t => t.Key));
    }

    [Fact]
    public void ALockedGoodNamesTheTierThatUnlocksIt()
    {
        var row = new LockedRow { Code = "seraphhorizons:schematic-windmill", Tier = 3, Reason = LockReason.Tier };
        Assert.Equal("trading-window-locked-tier(trading-standing-tier-trusted)", TradeWindowModel.LockedDetails(row, Summary(1, 70))[1].ToString());
        row.Reason = LockReason.Rare;
        row.Rotating = true;
        var lines = TradeWindowModel.LockedDetails(row, Summary(1, 70));
        Assert.Equal("trading-window-locked-rare", lines[1].Key);
        Assert.Equal("trading-window-locked-rotating", lines[2].Key);
    }
}

public class StandingSpeechTests
{
    private static TradeWindowState State(int tier, double points) => new()
    {
        Switches = TradeWindowModelTests.AllOn(),
        Standing = TradeWindowModelTests.Summary(tier, points),
        Orders = [new OrderRow { Id = 1 }, new OrderRow { Id = 2, Mine = true }],
    };

    [Fact]
    public void ARegularHearsTheirTierAndTheNumbersInBrackets()
    {
        var speech = StandingSpeech.Build(State(2, 310));
        Assert.Equal(4, speech.Count);
        Assert.Equal("dialogue-standing-tier-regular", speech[0].Voice.Key);
        Assert.Equal(["trading-window-speech-points(trading-standing-tier-regular, 310)", "trading-window-speech-next(trading-standing-tier-trusted, 800)"],
            speech[0].Facts.Select(f => f.ToString()));
        Assert.Equal("dialogue-standing-gives", speech[1].Voice.Key);
        Assert.Equal("dialogue-standing-next", speech[2].Voice.Key);
        Assert.Equal("trading-window-speech-at(trading-standing-tier-trusted)", speech[2].Facts[0].ToString());
        Assert.Contains(speech[2].Facts, f => f.Key == "trading-window-fact-rare");
        Assert.Equal("dialogue-standing-earn", speech[3].Voice.Key);
        Assert.Contains(speech[3].Facts, f => f.ToString() == "trading-window-earn-orders(40, 1)");
    }

    [Fact]
    public void AStrangerHearsWhatAFirstVisitGetsAndAPartnerThatThereIsNoMore()
    {
        var stranger = StandingSpeech.Build(State(0, 5));
        Assert.Equal("dialogue-standing-tier-stranger", stranger[0].Voice.Key);
        Assert.Equal("dialogue-standing-gives-first", stranger[1].Voice.Key);
        var partner = StandingSpeech.Build(State(4, 2500));
        Assert.Single(partner[0].Facts);
        Assert.Equal("dialogue-standing-top", partner[2].Voice.Key);
    }

    [Fact]
    public void AnUnknownTierCodeGetsTheGenericLine()
    {
        var state = State(1, 70);
        state.Standing!.Tiers[1].Code = "friend";
        Assert.Equal("dialogue-standing-tier-other", StandingSpeech.Build(state)[0].Voice.Key);
    }

    [Fact]
    public void StandingOffIsOneLine()
    {
        var state = State(1, 70);
        state.Switches.Standing = false;
        Assert.Equal("dialogue-standing-off", Assert.Single(StandingSpeech.Build(state)).Voice.Key);
    }
}

public class WireFormatTests
{
    [Fact]
    public void TheStateRoundTrips()
    {
        var state = new TradeWindowState
        {
            TraderId = 42,
            Switches = TradeWindowModelTests.AllOn(),
            Standing = TradeWindowModelTests.Summary(2, 310.5),
            Orders = [new OrderRow { Id = 3, Item = "game:ingot-copper", Quantity = 4, Value = 1.4, Mine = true, Held = 2 }],
            DeliveryWhy = "trading-deliveries-notyet",
            Locked = [new LockedRow { Code = "game:anvil-iron", Tier = 3, Reason = LockReason.Rare, Rotating = true, Attributes = "{\"a\":1}" }],
            LeadsTier = 2,
        };
        var back = TradeWindowState.FromJson(state.ToJson());
        Assert.Equal(42, back.TraderId);
        Assert.True(back.Switches.Maps);
        Assert.Equal(310.5, back.Standing!.Points);
        Assert.Equal("trusted", back.Standing.Tiers[3].Code);
        Assert.True(back.Standing.Tiers[3].Unlocks.RareStock);
        Assert.Equal(0.91, back.Standing.Tiers[3].Unlocks.BuyPriceFactor, 6);
        Assert.Equal(2, back.Orders[0].Held);
        Assert.Null(back.DeliveryOffer);
        Assert.Equal(LockReason.Rare, back.Locked[0].Reason);
        Assert.Equal("{\"a\":1}", back.Locked[0].Attributes);
        Assert.Equal(2, back.LeadsTier);
    }

    [Fact]
    public void RequestsAndResultsRoundTrip()
    {
        var req = TradeRequest.FromJson(new TradeRequest { Action = TradeAction.HandInOrder, Id = 5, Slot = 2 }.ToJson());
        Assert.Equal(TradeAction.HandInOrder, req.Action);
        Assert.Equal(5, req.Id);
        Assert.Null(req.Code);
        Assert.Null(req.Price);
        var buy = TradeRequest.FromJson(new TradeRequest { Action = TradeAction.Buy, Slot = 3, Code = "game:bread", Price = 4 }.ToJson());
        Assert.Equal("game:bread", buy.Code);
        Assert.Equal(4, buy.Price);
        var res = TradeResult.FromJson(TradeResult.Refused(TradeAction.Buy, "trading-window-toofar", 2.5, 3).ToJson());
        Assert.False(res.Ok);
        Assert.Equal(["2.5", "3"], res.Args);
        Assert.Equal(TradeAction.Buy, res.Action);
    }
}
