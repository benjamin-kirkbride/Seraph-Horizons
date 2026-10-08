using SeraphHorizons.Mod.Trading.Maps.Core;

namespace SeraphHorizons.Mod.Tests.Trading.Maps;

/// <summary>Which waypoints mark what: a map the player has is refused, a better one replaces a
/// rougher marker, meeting a camp's trader replaces the lead's marker, and old unremembered markers
/// are found by icon, place and title.</summary>
public class MapMarksTests
{
    private const string Cook = "camp:250,250";

    private static MarkRecord Rec(string guid, string key, int precision) => new() { Guid = guid, Key = key, Precision = precision };

    private static WaypointView Wp(string? guid, double x = 0, double z = 0, string icon = "trader", string title = "Trader camp (cook)") =>
        new(guid, x, z, icon, title);

    [Fact]
    public void MarkedPrecisionCountsOnlyRememberedMarkersStillOnTheMap()
    {
        MarkRecord[] records = [Rec("a", Cook, MapMarks.LeadPrecision), Rec("b", Cook, MapMarks.Exact), Rec("c", "deposit:copper:1,1", 1)];
        Assert.Equal(MapMarks.Exact, MapMarks.MarkedPrecision(Cook, records, [Wp("a"), Wp("b")]));
        // The exact one was deleted on the map: the lead's still counts.
        Assert.Equal(MapMarks.LeadPrecision, MapMarks.MarkedPrecision(Cook, records, [Wp("a")]));
        Assert.Equal(0, MapMarks.MarkedPrecision(Cook, records, [Wp("c")]));
        Assert.Equal(0, MapMarks.MarkedPrecision("camp:1,1", records, [Wp("a"), Wp("b")]));
    }

    [Fact]
    public void AMapWhoseTargetIsMarkedOrCarriedIsRefused()
    {
        var lead = new MarkTarget(Cook, MapMarks.LeadPrecision);
        Assert.Equal(MarkCheck.Free, MapMarks.Check(lead, 0, []));
        // The same lead read already, or the trader met (exact).
        Assert.Equal(MarkCheck.Marked, MapMarks.Check(lead, MapMarks.LeadPrecision, []));
        Assert.Equal(MarkCheck.Marked, MapMarks.Check(lead, MapMarks.Exact, []));
        // A copy carried, unread (or still being drawn).
        Assert.Equal(MarkCheck.Held, MapMarks.Check(lead, 0, [new MarkTarget(Cook, MapMarks.LeadPrecision)]));
        // A lead to another camp is no copy.
        Assert.Equal(MarkCheck.Free, MapMarks.Check(lead, 0, [new MarkTarget("camp:1,1", MapMarks.LeadPrecision)]));
    }

    [Fact]
    public void ABetterMapIsAnUpgradeNotADuplicate()
    {
        var exact = new MarkTarget("deposit:copper:3,4", MapMarks.Exact);
        Assert.Equal(MarkCheck.Free, MapMarks.Check(exact, MapMarks.Rough, [new MarkTarget("deposit:copper:3,4", MapMarks.Fair)]));
        Assert.Equal(MarkCheck.Held, MapMarks.Check(exact, MapMarks.Rough, [new MarkTarget("deposit:copper:3,4", MapMarks.Exact)]));
        // And a rougher one than what is marked is refused.
        Assert.Equal(MarkCheck.Marked, MapMarks.Check(new MarkTarget("deposit:copper:3,4", MapMarks.Rough), MapMarks.Fair, []));
    }

    [Fact]
    public void MarkingReplacesRougherMarkersAndNothingAsGood()
    {
        MarkRecord[] records = [Rec("lead", Cook, MapMarks.LeadPrecision), Rec("gone", Cook, MapMarks.Rough), Rec("other", "camp:1,1", MapMarks.Exact)];
        WaypointView[] waypoints = [Wp("lead"), Wp("other")];
        // Meeting the cook: the lead's marker goes (the deleted one is not on the map to remove).
        Assert.Equal(["lead"], MapMarks.Replaces(new MarkTarget(Cook, MapMarks.Exact), records, waypoints));
        // Reading the lead again: one as good is there, nothing to add.
        Assert.Null(MapMarks.Replaces(new MarkTarget(Cook, MapMarks.LeadPrecision), records, waypoints));
        // Nothing marked yet: add, replace nothing.
        Assert.Empty(MapMarks.Replaces(new MarkTarget("camp:9,9", MapMarks.Exact), records, waypoints)!);
    }

    [Fact]
    public void OldTraderMarkersAreMatchedByIconPlaceAndTitle()
    {
        WaypointView[] waypoints =
        [
            Wp(null, 100, 100),                                                   // a lead's, before markers were remembered
            Wp("g1", 150, 60, title: "Trader camp (cook) (approximate, ±64 m)"),  // the same, with a guid
            Wp("g2", 100, 100, icon: "pick", title: "Trader camp (cook)"),        // another icon
            Wp("g3", 400, 400),                                                   // too far
            Wp("g4", 110, 90, title: "Trader camp (smith)"),                      // another camp
            Wp("g5", 105, 105),                                                   // remembered: not legacy
        ];
        var found = MapMarks.LegacyMatches(waypoints, [Rec("g5", Cook, MapMarks.LeadPrecision)], 120, 80, MapMarks.LegacyReach, "trader",
            ["Trader camp (cook)"]);
        Assert.Equal([null, "g1"], found.Select(w => w.Guid));
    }

    [Fact]
    public void ReachFollowsTheOreMapTiers()
    {
        Assert.Equal(400, MapMarks.ReachOf(MapMarks.Rough));
        Assert.Equal(150, MapMarks.ReachOf(MapMarks.Fair));
        Assert.Equal(0, MapMarks.ReachOf(MapMarks.Exact));
        Assert.Equal("deposit:gravel:3,4", MapMarks.DepositKey("gravel:3,4"));
        Assert.Equal("settlement:10,-20", MapMarks.SettlementKey(10, -20));
    }

    [Fact]
    public void TheBookForgetsDeletedMarkersAndRoundTrips()
    {
        var book = new MarkBook();
        book.Add("p", Rec("a", Cook, 2));
        book.Add("p", Rec("b", "camp:1,1", 3));
        book.Add("p", Rec("a", Cook, 3));
        Assert.Equal(2, book.Of("p").Count);
        Assert.Equal(3, book.Of("p").Single(r => r.Guid == "a").Precision);
        var copy = MarkBook.FromJson(book.ToJson());
        Assert.Equal(["a", "b"], copy.Of("p").Select(r => r.Guid).Order());
        copy.Prune("p", ["b", null]);
        Assert.Equal(["b"], copy.Of("p").Select(r => r.Guid));
        copy.Remove("p", ["b"]);
        Assert.Empty(copy.Of("p"));
        Assert.Empty(copy.Of("nobody"));
    }
}
