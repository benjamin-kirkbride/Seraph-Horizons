using Newtonsoft.Json;
using SeraphHorizons.Mod.Trading.Core;

namespace SeraphHorizons.Mod.Tests.Trading;

public class CampRegistryTests
{
    private static readonly CellKey Cell = new(3, -2);

    [Fact]
    public void TheFirstSpotIsTriedAndAPlacedCampStays()
    {
        var r = new CampRegistry();
        Assert.Equal(AttemptVerdict.Try, r.OnSpot(Cell, 0, 8));
        Assert.True(r.Placed(Cell, 0, "smith", 100, 70, 200, "cold/igneous", "game:trader/cold/hut1-agr/trader-cold", slope: 2));
        var placed = r.Get(Cell)!;
        Assert.Equal(CampStatus.Placed, placed.Status);
        Assert.Equal(("smith", 100, 70, 200, 2, 0), (placed.Type, placed.X, placed.Y, placed.Z, placed.Slope, placed.Attempt));
        Assert.Equal(AttemptVerdict.Skip, r.OnSpot(Cell, 0, 8));
        Assert.Equal(AttemptVerdict.Skip, r.OnSpot(Cell, 1, 8));
        Assert.Equal(AttemptVerdict.Skip, r.OnSecondChance(Cell));
    }

    [Fact]
    public void AnySpotTriesWhenItsChunkGeneratesInAnyOrder()
    {
        // #599: a spot whose chunk comes before an earlier spot's is tried, not burnt.
        var r = new CampRegistry();
        Assert.Equal(AttemptVerdict.Try, r.OnSpot(Cell, 5, 8));
        r.Missed(Cell, 8);
        Assert.Equal((CampStatus.Pending, 0), (r.Get(Cell)!.Status, r.Get(Cell)!.Attempt));
        Assert.Equal(AttemptVerdict.Try, r.OnSpot(Cell, 7, 8));
        Assert.True(r.Placed(Cell, 7, "cook", 1, 2, 3, "", ""));
        Assert.Equal((CampStatus.Placed, 7), (r.Get(Cell)!.Status, r.Get(Cell)!.Attempt));
        Assert.Equal(AttemptVerdict.Skip, r.OnSpot(Cell, 0, 8));
    }

    [Fact]
    public void ASpotIsTriedOnceAndTheNextSpotIsTheFirstUntried()
    {
        var r = new CampRegistry();
        Assert.Equal(0, CampRegistry.NextSpot(null, 4));
        r.OnSpot(Cell, 0, 4);
        r.Missed(Cell, 4);
        r.OnSpot(Cell, 2, 4);
        r.Missed(Cell, 4);
        Assert.Equal(AttemptVerdict.Skip, r.OnSpot(Cell, 2, 4));
        Assert.Equal([0, 2], r.Get(Cell)!.Tried);
        Assert.Equal(1, CampRegistry.NextSpot(r.Get(Cell), 4));
        Assert.Equal(1, r.Get(Cell)!.Attempt);
        Assert.Null(CampRegistry.NextSpot(null, 0));
    }

    [Fact]
    public void EverySpotMissedOpensTheCellToBoundedSecondChances()
    {
        var r = new CampRegistry();
        Assert.Equal(AttemptVerdict.Skip, r.OnSecondChance(Cell));
        foreach (int i in new[] { 3, 0, 2, 1 })
        {
            Assert.Equal(AttemptVerdict.Try, r.OnSpot(Cell, i, 4));
            r.Missed(Cell, 4);
        }
        var open = r.Get(Cell)!;
        Assert.Equal((CampStatus.Open, 4), (open.Status, open.Attempt));
        Assert.Null(CampRegistry.NextSpot(open, 4));
        for (int n = 0; n < TraderGrid.SecondChances; n++)
        {
            Assert.Equal(CampStatus.Open, r.Get(Cell)!.Status);
            Assert.Equal(AttemptVerdict.Try, r.OnSecondChance(Cell));
            r.Missed(Cell, 4);
        }
        Assert.Equal((CampStatus.Failed, TraderGrid.SecondChances), (r.Get(Cell)!.Status, r.Get(Cell)!.Retries));
        Assert.Equal(AttemptVerdict.Skip, r.OnSecondChance(Cell));
        Assert.False(r.Placed(Cell, -1, "cook", 1, 2, 3, "", ""));
    }

    [Fact]
    public void ASecondChanceCanPlaceTheCamp()
    {
        var r = new CampRegistry();
        r.OnSpot(Cell, 0, 1);
        r.Missed(Cell, 1);
        Assert.False(r.Placed(Cell, -1, "cook", 1, 2, 3, "", ""));
        Assert.Equal(AttemptVerdict.Try, r.OnSecondChance(Cell));
        Assert.True(r.Placed(Cell, -1, "cook", 1, 2, 3, "", ""));
        Assert.Equal((CampStatus.Placed, -1, 1), (r.Get(Cell)!.Status, r.Get(Cell)!.Attempt, r.Get(Cell)!.Retries));
    }

    [Fact]
    public void ReportsForAnUnclaimedTryAreIgnored()
    {
        var r = new CampRegistry();
        r.OnSpot(Cell, 0, 4);
        Assert.False(r.Placed(Cell, 1, "cook", 1, 2, 3, "", ""));
        Assert.False(r.Placed(new CellKey(9, 9), 0, "cook", 1, 2, 3, "", ""));
        r.Missed(new CellKey(9, 9), 4);
        Assert.Null(r.Get(new CellKey(9, 9)));
        Assert.Equal(CampStatus.Pending, r.Get(Cell)!.Status);
    }

    [Fact]
    public void ASnapshotRestoresTheSameState()
    {
        var r = new CampRegistry();
        r.OnSpot(Cell, 2, 8);
        r.Missed(Cell, 8);
        r.OnSpot(new CellKey(0, 0), 0, 8);
        r.Placed(new CellKey(0, 0), 0, "mason", 5, 6, 7, "hot/metamorphic", "x", 1);
        var json = JsonConvert.SerializeObject(r.Snapshot());
        var back = new CampRegistry(JsonConvert.DeserializeObject<List<CampRecord>>(json)!);
        Assert.Equal([2], back.Get(Cell)!.Tried);
        Assert.Equal(CampStatus.Pending, back.Get(Cell)!.Status);
        Assert.Equal(("mason", 1), (back.Get(new CellKey(0, 0))!.Type, back.Get(new CellKey(0, 0))!.Slope));
        Assert.Null(back.Get(new CellKey(9, 9)));
    }

    // Records as saves before #599 hold them: Attempt and Passed, no Tried, Retries or Slope.
    private static CampRegistry Legacy(string json) =>
        new(JsonConvert.DeserializeObject<List<CampRecord>>(json)!);

    [Fact]
    public void AnOldPendingCellKeepsItsUngeneratedSpotsAndLosesTheBurntOnes()
    {
        // The issue's cell 247,248: waiting on spot 1, spots 2, 5 and 7 passed (burnt).
        var r = Legacy("""[{"CellX":3,"CellZ":-2,"Status":0,"Attempt":1,"Passed":[2,5,7],"Type":"","X":0,"Y":0,"Z":0,"Region":"","Structure":""}]""");
        var rec = r.Get(Cell)!;
        Assert.Equal(CampStatus.Pending, rec.Status);
        Assert.Equal([0, 2, 5, 7], rec.Tried);
        Assert.Empty(rec.Passed);
        Assert.Equal(1, CampRegistry.NextSpot(rec, 8));
        // Spots 1, 3, 4 and 6 have not generated: each tries when it does, in any order.
        Assert.Equal(AttemptVerdict.Skip, r.OnSpot(Cell, 5, 8));
        Assert.Equal(AttemptVerdict.Try, r.OnSpot(Cell, 4, 8));
        r.Missed(Cell, 8);
        Assert.Equal(AttemptVerdict.Try, r.OnSpot(Cell, 1, 8));
    }

    [Fact]
    public void AnOldFailedCellIsOpenToSecondChances()
    {
        var r = Legacy("""[{"CellX":3,"CellZ":-2,"Status":2,"Attempt":8,"Passed":[],"Type":"","X":0,"Y":0,"Z":0,"Region":"","Structure":""}]""");
        var rec = r.Get(Cell)!;
        Assert.Equal((CampStatus.Open, 0), (rec.Status, rec.Retries));
        Assert.Equal(Enumerable.Range(0, 8), rec.Tried!);
        Assert.Equal(AttemptVerdict.Try, r.OnSecondChance(Cell));
    }

    [Fact]
    public void AnOldPlacedCampStaysPlaced()
    {
        var r = Legacy("""[{"CellX":3,"CellZ":-2,"Status":1,"Attempt":2,"Passed":[],"Type":"smith","X":1,"Y":2,"Z":3,"Region":"cold/igneous","Structure":"s"}]""");
        var rec = r.Get(Cell)!;
        Assert.Equal((CampStatus.Placed, "smith", 1, 2, 3), (rec.Status, rec.Type, rec.X, rec.Y, rec.Z));
        Assert.Equal(AttemptVerdict.Skip, r.OnSpot(Cell, 3, 8));
    }

    [Fact]
    public void TheListingShowsPlacedCampsPendingSpotsAndOpenCellsNearestFirst()
    {
        var grid = new TraderGrid(2024, TraderGridTests.Weights);
        var r = new CampRegistry();
        var placedCell = new CellKey(0, 0);
        var spot = grid.Spots(placedCell)[0];
        r.OnSpot(placedCell, 0, 8);
        r.Placed(placedCell, 0, grid.TypeOf(placedCell), spot.X, 110, spot.Z, "temperate/igneous", "s");
        var openCell = new CellKey(1, 0);
        int count = grid.Spots(openCell).Count;
        foreach (int i in Enumerable.Range(0, count))
        {
            r.OnSpot(openCell, i, count);
            r.Missed(openCell, count);
        }
        var pendingCell = new CellKey(0, 1);
        r.OnSpot(pendingCell, 0, grid.Spots(pendingCell).Count);
        r.Missed(pendingCell, grid.Spots(pendingCell).Count);
        var rows = CampListing.Rows(grid, r, 1024, 1024, 4096);
        Assert.Equal(rows.OrderBy(x => x.Distance).Select(x => x.Cell), rows.Select(x => x.Cell));
        Assert.All(rows, x => Assert.InRange(x.Distance, 0, 4096));
        var placed = rows.Single(x => x.Cell == placedCell);
        Assert.Equal((CampStatus.Placed, 110), (placed.Status, placed.Y));
        Assert.Equal(CampStatus.Open, rows.Single(x => x.Cell == openCell).Status);
        // A cell whose first spot missed shows its next spot.
        var pending = rows.Single(x => x.Cell == pendingCell);
        Assert.Equal((CampStatus.Pending, grid.Spots(pendingCell)[1]), (pending.Status, new Spot(pending.X, pending.Z)));
        var untouched = rows.First(x => x.Status == CampStatus.Pending && x.Cell != pendingCell);
        Assert.Equal(grid.Spots(untouched.Cell)[0], new Spot(untouched.X, untouched.Z));
        Assert.Equal(grid.TypeOf(untouched.Cell), untouched.Type);

        string Text(string key, object[] a) => key.Replace("seraphhorizons:", "") + "|" + string.Join("|", a);
        Assert.Equal($"trading-camp-placed|0,0|trading-type-{placed.Type}||{spot.X - 512}, 110, {spot.Z - 512}|temperate/igneous|{placed.Distance:0}",
            CampListing.Line(placed, 512, 512, Text));
        Assert.StartsWith("trading-camp-open|1,0|", CampListing.Line(rows.Single(x => x.Cell == openCell), 0, 0, Text));
        Assert.StartsWith("trading-camp-pending|", CampListing.Line(pending, 0, 0, Text));
        Assert.StartsWith("trading-camp-failed|", CampListing.Line(placed with { Status = CampStatus.Failed }, 0, 0, Text));
    }
}
