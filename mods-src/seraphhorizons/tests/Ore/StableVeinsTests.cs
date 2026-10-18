using SeraphHorizons.Mod.Ore.Core;

namespace SeraphHorizons.Tests.Ore;

public class StableVeinsTests
{
    private const int Air = 0, Granite = 1, Andesite = 2, Soil = 3, Ore = 4, SubOre = 5;

    private static bool IsRock(int id) => id is Granite or Andesite;

    private static (Dictionary<(int, int, int), int> Column, int Placed) Lay(Dictionary<(int, int, int), int> column,
        params (int X, int Y, int Z, int Id)[] writes)
    {
        var c = new Dictionary<(int, int, int), int>(column);
        int placed = StableVeinApply.Apply(writes, Granite, (x, y, z) => c.GetValueOrDefault((x, y, z), Air),
            (x, y, z, id) => c[(x, y, z)] = id, IsRock);
        return (c, placed);
    }

    [Fact]
    public void TheShellTurnsRockIntoTheHostAndOreFollowsIt()
    {
        var column = new Dictionary<(int, int, int), int> { [(0, 0, 0)] = Andesite, [(1, 0, 0)] = Soil, [(2, 0, 0)] = Air };
        // IOG's order: the shell (host rock) around each position, then the ore.
        var (c, placed) = Lay(column,
            (0, 0, 0, Granite), (1, 0, 0, Granite), (2, 0, 0, Granite),
            (0, 0, 0, Ore), (1, 0, 0, Ore), (2, 0, 0, Ore));
        Assert.Equal(Ore, c[(0, 0, 0)]);  // andesite: shelled to granite, then ore
        Assert.Equal(Soil, c[(1, 0, 0)]); // soil is not rock: no shell, no ore
        Assert.Equal(Air, c[(2, 0, 0)]);  // nor is air (a cave, the sky)
        Assert.Equal(2, placed);
    }

    [Fact]
    public void OreLandsOnlyOnTheHostRockAndNeverTwice()
    {
        var column = new Dictionary<(int, int, int), int> { [(0, 0, 0)] = Granite, [(1, 0, 0)] = Andesite };
        var (c, _) = Lay(column, (0, 0, 0, Ore), (0, 0, 0, SubOre), (1, 0, 0, Ore));
        Assert.Equal(Ore, c[(0, 0, 0)]);      // the first ore stands: a later write finds ore, not host rock
        Assert.Equal(Andesite, c[(1, 0, 0)]); // no shell there: andesite is not this vein's host
    }

    [Fact]
    public void TheResultDependsOnlyOnTheColumnAndTheWrites()
    {
        var column = new Dictionary<(int, int, int), int>();
        for (int x = 0; x < 6; x++) column[(x, 0, 0)] = x % 3 == 0 ? Soil : x % 2 == 0 ? Andesite : Granite;
        var writes = Enumerable.Range(0, 6).Select(x => (x, 0, 0, Granite))
            .Concat(Enumerable.Range(0, 6).Select(x => (x, 0, 0, Ore))).ToArray();
        Assert.Equal(Lay(column, writes).Column, Lay(column, writes).Column);
    }

    [Fact]
    public void ABoulderSitsWhereItsPathFirstReachesTheSurfaceOfItsColumn()
    {
        int? Flat(int x, int z) => 110;
        var path = new List<(int, int, int)> { (40, 100, 40), (41, 101, 40), (41, 109, 41), (42, 110, 41), (42, 111, 41) };
        Assert.Equal((42, 110, 41), StableVeinApply.BoulderSite(path, Flat));
        // Higher ground under the path: the first point at or above the height there.
        int? Hill(int x, int z) => x >= 41 ? 101 : 120;
        Assert.Equal((41, 101, 40), StableVeinApply.BoulderSite(path, Hill));
        // A path that leaves its column (x 63 -> 64) before the surface has none, nor one without heights.
        var drifting = new List<(int, int, int)> { (63, 100, 40), (64, 101, 40), (64, 115, 40) };
        Assert.Null(StableVeinApply.BoulderSite(drifting, Flat));
        Assert.Null(StableVeinApply.BoulderSite(path, (_, _) => null));
        Assert.Null(StableVeinApply.BoulderSite(new List<(int, int, int)>(), Flat));
    }

    [Fact]
    public void ABouldersBlocksDependOnItsSiteAlone()
    {
        var a = StableVeinApply.BoulderBlocks(512010, 120, 511990, 2, 5);
        Assert.Equal(a, StableVeinApply.BoulderBlocks(512010, 120, 511990, 2, 5));
        Assert.NotEqual(a, StableVeinApply.BoulderBlocks(512011, 120, 511990, 2, 5));
        Assert.Contains((512010, 120, 511990), a); // the centre
        Assert.All(a, b => Assert.True(Math.Abs(b.X - 512010) <= 5 && Math.Abs(b.Y - 120) <= 5 && Math.Abs(b.Z - 511990) <= 5));
        Assert.Equal(a.Count, a.Distinct().Count());
    }

    [Fact]
    public void TheBookRoundTripsAndKeepsTheFirstDecision()
    {
        var book = new StableVeinBook();
        var vein = new StableVein("copper", new CellPos(102, -3), 2, 16039, -96, 1234567L, 513275, -3060,
            "game:rock-granite", "nativecopper#2", Holds: true);
        Assert.True(book.Add(vein));
        Assert.False(book.Add(vein with { WorldSeed = 1 })); // decided once
        book.Add(vein with { Metal = "iron", Spot = 0, Holds = false, Variant = "hematite#1" });
        var text = book.Serialize();
        var back = StableVeinBook.Parse(text);
        Assert.Equal(text, back.Serialize());
        Assert.Equal(vein, back.Get("copper", new CellPos(102, -3), 2));
        Assert.False(back.Get("iron", new CellPos(102, -3), 0)!.Holds);
        Assert.Null(back.Get("copper", new CellPos(102, -3), 0));
        Assert.Equal(0, StableVeinBook.Parse(null).Count);
        Assert.Throws<FormatException>(() => StableVeinBook.Parse("v9\n"));
    }

    [Fact]
    public void AWorldFromBeforeStableVeinsHasThemOff()
    {
        var old = OreWorldRecord.AllOff with { GradePockets = true };
        var text = old.Serialize().Replace("stableVeins=False\n", "");
        Assert.False(OreWorldRecord.Parse(text).StableVeins);
        var record = OreWorldRecord.AllOff with { StableVeins = true };
        Assert.True(OreWorldRecord.Parse(record.Serialize()).StableVeins);
        Assert.False(record.Effective(record with { StableVeins = false }).StableVeins);
    }

    // A record from before single ore drops has neither key: 1.25 ore a block, deposits on the old basis.
    [Fact]
    public void AWorldFromBeforeSingleOreDropsKeepsTheOldDropsAndSizes()
    {
        var old = OreWorldRecord.AllOff with { StableVeins = true };
        var text = old.Serialize().Replace("singleOreDrops=False\ndoubledDeposits=False\n", "");
        Assert.DoesNotContain("singleOreDrops", text);
        var parsed = OreWorldRecord.Parse(text);
        Assert.False(parsed.SingleOreDrops);
        Assert.False(parsed.DoubledDeposits);
        Assert.False(OreWorldRecord.ForWorld(null, false, OreWorldRecord.AllOff with { SingleOreDrops = true, DoubledDeposits = true }).DoubledDeposits);

        var record = OreWorldRecord.AllOff with { SingleOreDrops = true, DoubledDeposits = true };
        Assert.Equal(record, OreWorldRecord.Parse(record.Serialize()));
        // The drops follow the config's switch; the basis has none.
        var off = record.Effective(record with { SingleOreDrops = false, DoubledDeposits = false });
        Assert.False(off.SingleOreDrops);
        Assert.True(off.DoubledDeposits);
    }
}
