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
}
